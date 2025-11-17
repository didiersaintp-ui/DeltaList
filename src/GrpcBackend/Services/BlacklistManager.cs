using DeltaList.Shared.Models;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using DeltaList.Shared.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GrpcBackend.Services;

public class BlacklistManager : IBlacklistManager
{
    private readonly ILogger<BlacklistManager> _logger;
    private readonly IDistributedCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly PanTokenizer _tokenizer;
    private readonly BlobContainerClient? _blobContainer;
    private readonly bool _isAzureConfigured;

    private ulong _currentSeqNo = 0;
    private readonly HashSet<string> _blacklistedTokens = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Delta history for offline device recovery
    private readonly ConcurrentDictionary<ulong, BlacklistDelta> _deltaHistory = new();
    private const int MAX_DELTA_HISTORY = 100;

    private const string BLACKLIST_CACHE_KEY = "blacklist:current";
    private const string SEQNO_CACHE_KEY = "blacklist:seqno";

    public BlacklistManager(
        ILogger<BlacklistManager> logger,
        IDistributedCache cache,
        MetricsCollector metrics,
        PanTokenizer tokenizer,
        BackendSettings settings)
    {
        _logger = logger;
        _cache = cache;
        _metrics = metrics;
        _tokenizer = tokenizer;

        // Try to initialize Azure Blob Storage for persistence
        if (!string.IsNullOrEmpty(settings.Storage.ConnectionString))
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(settings.Storage.ConnectionString);
                _blobContainer = blobServiceClient.GetBlobContainerClient(settings.Storage.BlacklistContainerName);
                _blobContainer.CreateIfNotExists();

                _isAzureConfigured = true;
                _logger.LogInformation(
                    "Azure Blob Storage initialized for blacklist (container: {Container})",
                    settings.Storage.BlacklistContainerName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to initialize Azure Blob Storage for blacklist, using cache-only mode");
                _isAzureConfigured = false;
            }
        }
        else
        {
            _logger.LogInformation("Azure Blob Storage not configured for blacklist, using cache-only mode");
            _isAzureConfigured = false;
        }
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            BlacklistState? state = null;

            // Try to load from Azure first (most persistent)
            if (_isAzureConfigured && _blobContainer != null)
            {
                state = await LoadFromAzureAsync();
            }

            // Fallback to cache
            if (state == null)
            {
                var blacklistJson = await _cache.GetStringAsync(BLACKLIST_CACHE_KEY);
                if (!string.IsNullOrEmpty(blacklistJson))
                {
                    state = JsonSerializer.Deserialize<BlacklistState>(blacklistJson);
                }
            }

            // Apply loaded state
            if (state != null)
            {
                _blacklistedTokens.UnionWith(state.PanTokens);
                _currentSeqNo = state.CurrentSeqNo;
                _logger.LogInformation(
                    "Loaded blacklist with {Count} tokens at sequence {SeqNo}",
                    _blacklistedTokens.Count, _currentSeqNo);
            }

            // Load delta history from Azure for offline device recovery
            if (_isAzureConfigured && _blobContainer != null)
            {
                await LoadDeltaHistoryFromAzureAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BlacklistState?> GetCurrentBlacklistAsync(string deviceId)
    {
        await _lock.WaitAsync();
        try
        {
            return new BlacklistState
            {
                CurrentSeqNo = _currentSeqNo,
                LastUpdatedAt = DateTime.UtcNow,
                PanTokens = new HashSet<string>(_blacklistedTokens),
                ShardId = 0
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BlacklistDelta> AddToBlacklistAsync(
        IEnumerable<string> panTokens,
        string reason,
        string updatedBy)
    {
        await _lock.WaitAsync();
        try
        {
            var newTokens = panTokens.Where(t => !_blacklistedTokens.Contains(t)).ToList();

            if (!newTokens.Any())
            {
                _logger.LogInformation("No new tokens to add to blacklist");
                return CreateEmptyDelta();
            }

            foreach (var token in newTokens)
            {
                _blacklistedTokens.Add(token);
            }

            _currentSeqNo++;

            var delta = new BlacklistDelta
            {
                DeltaId = Guid.NewGuid().ToString(),
                SeqNo = _currentSeqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ShardId = 0
            };

            delta.Added.AddRange(newTokens);

            // Sign the delta
            var deltaJson = JsonSerializer.Serialize(new { delta.DeltaId, delta.SeqNo, delta.TimestampUtc, delta.Added });
            delta.Signature = _tokenizer.SignMessage(deltaJson);

            // Persist to cache and Azure
            await PersistBlacklistStateAsync();
            await PersistDeltaAsync(delta);

            _logger.LogInformation(
                "Added {Count} tokens to blacklist (SeqNo: {SeqNo}). Reason: {Reason}, By: {UpdatedBy}",
                newTokens.Count, _currentSeqNo, reason, updatedBy);

            return delta;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BlacklistDelta> RemoveFromBlacklistAsync(
        IEnumerable<string> panTokens,
        string reason,
        string updatedBy)
    {
        await _lock.WaitAsync();
        try
        {
            var tokensToRemove = panTokens.Where(t => _blacklistedTokens.Contains(t)).ToList();

            if (!tokensToRemove.Any())
            {
                _logger.LogInformation("No tokens to remove from blacklist");
                return CreateEmptyDelta();
            }

            foreach (var token in tokensToRemove)
            {
                _blacklistedTokens.Remove(token);
            }

            _currentSeqNo++;

            var delta = new BlacklistDelta
            {
                DeltaId = Guid.NewGuid().ToString(),
                SeqNo = _currentSeqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ShardId = 0
            };

            delta.Removed.AddRange(tokensToRemove);

            // Sign the delta
            var deltaJson = JsonSerializer.Serialize(new { delta.DeltaId, delta.SeqNo, delta.TimestampUtc, delta.Removed });
            delta.Signature = _tokenizer.SignMessage(deltaJson);

            // Persist to cache and Azure
            await PersistBlacklistStateAsync();
            await PersistDeltaAsync(delta);

            _logger.LogInformation(
                "Removed {Count} tokens from blacklist (SeqNo: {SeqNo}). Reason: {Reason}, By: {UpdatedBy}",
                tokensToRemove.Count, _currentSeqNo, reason, updatedBy);

            return delta;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<ulong> GetNextSequenceNumberAsync()
    {
        return Task.FromResult(_currentSeqNo + 1);
    }

    private async Task PersistBlacklistStateAsync()
    {
        var state = new BlacklistState
        {
            CurrentSeqNo = _currentSeqNo,
            LastUpdatedAt = DateTime.UtcNow,
            PanTokens = new HashSet<string>(_blacklistedTokens),
            ShardId = 0
        };

        // Persist to cache (fast)
        var json = JsonSerializer.Serialize(state);
        await _cache.SetStringAsync(BLACKLIST_CACHE_KEY, json);
        await _cache.SetStringAsync(SEQNO_CACHE_KEY, _currentSeqNo.ToString());

        // Persist to Azure (durable) - fire and forget for performance
        if (_isAzureConfigured && _blobContainer != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveToAzureAsync(state);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist blacklist state to Azure");
                }
            });
        }
    }

    private async Task PersistDeltaAsync(BlacklistDelta delta)
    {
        // Store in delta history for offline device recovery
        _deltaHistory.AddOrUpdate(delta.SeqNo, delta, (_, _) => delta);

        // Cleanup old deltas if history is too large
        if (_deltaHistory.Count > MAX_DELTA_HISTORY)
        {
            var oldestSeqNo = _deltaHistory.Keys.Min();
            _deltaHistory.TryRemove(oldestSeqNo, out _);
        }

        // Persist delta to Azure - fire and forget
        if (_isAzureConfigured && _blobContainer != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveDeltaToAzureAsync(delta);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist delta {DeltaId} to Azure", delta.DeltaId);
                }
            });
        }
    }

    private async Task<BlacklistState?> LoadFromAzureAsync()
    {
        if (_blobContainer == null)
            return null;

        try
        {
            var blobClient = _blobContainer.GetBlobClient("blacklist_current.json");

            if (!await blobClient.ExistsAsync())
            {
                _logger.LogInformation("No existing blacklist found in Azure");
                return null;
            }

            var download = await blobClient.DownloadContentAsync();
            var json = download.Value.Content.ToString();
            var state = JsonSerializer.Deserialize<BlacklistState>(json);

            _logger.LogInformation("Loaded blacklist from Azure: {Count} tokens at seq {SeqNo}",
                state?.PanTokens.Count ?? 0, state?.CurrentSeqNo ?? 0);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading blacklist from Azure");
            return null;
        }
    }

    private async Task SaveToAzureAsync(BlacklistState state)
    {
        if (_blobContainer == null)
            return;

        var blobClient = _blobContainer.GetBlobClient("blacklist_current.json");

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        var content = Encoding.UTF8.GetBytes(json);

        var metadata = new Dictionary<string, string>
        {
            { "seq_no", state.CurrentSeqNo.ToString() },
            { "token_count", state.PanTokens.Count.ToString() },
            { "updated_at", state.LastUpdatedAt.ToString("o") }
        };

        await blobClient.UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions
            {
                Metadata = metadata,
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            });

        _logger.LogDebug("Saved blacklist to Azure: seq {SeqNo}, {Count} tokens",
            state.CurrentSeqNo, state.PanTokens.Count);
    }

    private async Task SaveDeltaToAzureAsync(BlacklistDelta delta)
    {
        if (_blobContainer == null)
            return;

        // Store deltas in a versioned structure: deltas/{seqno}_{deltaid}.json
        var blobName = $"deltas/{delta.SeqNo:D10}_{delta.DeltaId}.json";
        var blobClient = _blobContainer.GetBlobClient(blobName);

        var json = JsonSerializer.Serialize(delta, new JsonSerializerOptions { WriteIndented = true });
        var content = Encoding.UTF8.GetBytes(json);

        var metadata = new Dictionary<string, string>
        {
            { "seq_no", delta.SeqNo.ToString() },
            { "delta_id", delta.DeltaId },
            { "added_count", delta.Added.Count.ToString() },
            { "removed_count", delta.Removed.Count.ToString() },
            { "timestamp", DateTimeOffset.FromUnixTimeMilliseconds(delta.TimestampUtc).ToString("o") }
        };

        await blobClient.UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions
            {
                Metadata = metadata,
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            });

        _logger.LogDebug("Saved delta to Azure: seq {SeqNo}, delta {DeltaId}",
            delta.SeqNo, delta.DeltaId);
    }

    private async Task LoadDeltaHistoryFromAzureAsync()
    {
        if (_blobContainer == null)
            return;

        try
        {
            var prefix = "deltas/";
            var deltaList = new List<(ulong seqNo, string blobName)>();

            // List all delta blobs
            await foreach (var blobItem in _blobContainer.GetBlobsAsync(prefix: prefix))
            {
                // Extract seq no from blob name: deltas/0000000123_guid.json
                var fileName = Path.GetFileNameWithoutExtension(blobItem.Name);
                var parts = fileName.Split('_');
                if (parts.Length >= 1 && ulong.TryParse(parts[0], out var seqNo))
                {
                    deltaList.Add((seqNo, blobItem.Name));
                }
            }

            // Load the most recent deltas (up to MAX_DELTA_HISTORY)
            var recentDeltas = deltaList
                .OrderByDescending(d => d.seqNo)
                .Take(MAX_DELTA_HISTORY);

            foreach (var (seqNo, blobName) in recentDeltas)
            {
                var blobClient = _blobContainer.GetBlobClient(blobName);
                var download = await blobClient.DownloadContentAsync();
                var json = download.Value.Content.ToString();
                var delta = JsonSerializer.Deserialize<BlacklistDelta>(json);

                if (delta != null)
                {
                    _deltaHistory.TryAdd(seqNo, delta);
                }
            }

            _logger.LogInformation("Loaded {Count} deltas from Azure for offline device recovery",
                _deltaHistory.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading delta history from Azure");
        }
    }

    private BlacklistDelta CreateEmptyDelta()
    {
        return new BlacklistDelta
        {
            DeltaId = Guid.NewGuid().ToString(),
            SeqNo = _currentSeqNo,
            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ShardId = 0
        };
    }
}
