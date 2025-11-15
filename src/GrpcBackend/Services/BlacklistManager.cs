using DeltaList.Shared.Models;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GrpcBackend.Services;

public class BlacklistManager : IBlacklistManager
{
    private readonly ILogger<BlacklistManager> _logger;
    private readonly IDistributedCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly PanTokenizer _tokenizer;

    private ulong _currentSeqNo = 0;
    private readonly HashSet<string> _blacklistedTokens = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string BLACKLIST_CACHE_KEY = "blacklist:current";
    private const string SEQNO_CACHE_KEY = "blacklist:seqno";

    public BlacklistManager(
        ILogger<BlacklistManager> logger,
        IDistributedCache cache,
        MetricsCollector metrics,
        PanTokenizer tokenizer)
    {
        _logger = logger;
        _cache = cache;
        _metrics = metrics;
        _tokenizer = tokenizer;
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Load current state from cache
            var blacklistJson = await _cache.GetStringAsync(BLACKLIST_CACHE_KEY);
            if (!string.IsNullOrEmpty(blacklistJson))
            {
                var state = JsonSerializer.Deserialize<BlacklistState>(blacklistJson);
                if (state != null)
                {
                    _blacklistedTokens.UnionWith(state.PanTokens);
                    _currentSeqNo = state.CurrentSeqNo;
                    _logger.LogInformation(
                        "Loaded blacklist with {Count} tokens at sequence {SeqNo}",
                        _blacklistedTokens.Count, _currentSeqNo);
                }
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

            // Persist to cache
            await PersistBlacklistStateAsync();

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

            // Persist to cache
            await PersistBlacklistStateAsync();

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

        var json = JsonSerializer.Serialize(state);
        await _cache.SetStringAsync(BLACKLIST_CACHE_KEY, json);
        await _cache.SetStringAsync(SEQNO_CACHE_KEY, _currentSeqNo.ToString());
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
