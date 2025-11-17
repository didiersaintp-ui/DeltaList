using DeltaList.Shared.Models;
using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DeltaList.Shared.Services;

public class BlacklistManager : IBlacklistManager
{
    private readonly ILogger<BlacklistManager> _logger;
    private readonly IDistributedCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly PanTokenizer _tokenizer;

    // Sharded blacklist storage
    private readonly ConcurrentDictionary<int, HashSet<string>> _blacklistByShardId = new();
    private readonly ConcurrentDictionary<int, ulong> _seqNoByShardId = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locksByShardId = new();

    // Delta history for catch-up
    private readonly ConcurrentBag<BlacklistDelta> _deltaHistory = new();
    private const int MAX_DELTA_HISTORY = 1000;

    private const string BLACKLIST_CACHE_KEY_PREFIX = "blacklist:shard:";
    private const string SEQNO_CACHE_KEY_PREFIX = "blacklist:seqno:shard:";

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
        _logger.LogInformation("Initializing BlacklistManager");

        // Initialize shard 0 (default shard for PoC)
        await InitializeShardAsync(0);
    }

    private async Task InitializeShardAsync(int shardId)
    {
        var lockObj = _locksByShardId.GetOrAdd(shardId, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();
        try
        {
            // Load current state from cache
            var cacheKey = BLACKLIST_CACHE_KEY_PREFIX + shardId;
            var blacklistJson = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(blacklistJson))
            {
                var state = JsonSerializer.Deserialize<BlacklistState>(blacklistJson);
                if (state != null)
                {
                    _blacklistByShardId[shardId] = state.PanTokens;
                    _seqNoByShardId[shardId] = state.CurrentSeqNo;

                    _logger.LogInformation(
                        "Loaded blacklist shard {ShardId} with {Count} tokens at sequence {SeqNo}",
                        shardId, state.PanTokens.Count, state.CurrentSeqNo);
                }
            }
            else
            {
                _blacklistByShardId[shardId] = new HashSet<string>();
                _seqNoByShardId[shardId] = 0;

                _logger.LogInformation("Initialized empty blacklist for shard {ShardId}", shardId);
            }
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task<(ulong seqNo, int devicesNotified)> AddToBlacklistAsync(
        IEnumerable<string> panTokens,
        int shardId,
        string reason,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        // If shardId is -1, apply to all shards
        if (shardId == -1)
        {
            var tasks = _blacklistByShardId.Keys
                .Select(sid => AddToBlacklistAsync(panTokens, sid, reason, updatedBy, cancellationToken))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var totalDevices = results.Sum(r => r.devicesNotified);
            var maxSeqNo = results.Max(r => r.seqNo);

            return (maxSeqNo, totalDevices);
        }

        await EnsureShardInitializedAsync(shardId);

        var lockObj = _locksByShardId[shardId];
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            var tokens = panTokens.ToList();
            var blacklist = _blacklistByShardId[shardId];
            var newTokens = tokens.Where(t => !blacklist.Contains(t)).ToList();

            if (!newTokens.Any())
            {
                _logger.LogInformation("No new tokens to add to blacklist shard {ShardId}", shardId);
                return (_seqNoByShardId[shardId], 0);
            }

            foreach (var token in newTokens)
            {
                blacklist.Add(token);
            }

            _seqNoByShardId[shardId]++;
            var seqNo = _seqNoByShardId[shardId];

            var delta = new BlacklistDelta
            {
                DeltaId = Guid.NewGuid().ToString(),
                SeqNo = seqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ShardId = shardId
            };

            delta.Added.AddRange(newTokens);

            // Sign the delta
            var deltaJson = JsonSerializer.Serialize(new { delta.DeltaId, delta.SeqNo, delta.TimestampUtc, delta.Added, delta.ShardId });
            delta.Signature = _tokenizer.SignMessage(deltaJson);

            // Add to history for catch-up
            _deltaHistory.Add(delta);
            TrimDeltaHistory();

            // Persist to cache
            await PersistBlacklistStateAsync(shardId);

            _logger.LogInformation(
                "Added {Count} tokens to blacklist shard {ShardId} (SeqNo: {SeqNo}). Reason: {Reason}, By: {UpdatedBy}",
                newTokens.Count, shardId, seqNo, reason, updatedBy);

            // For PoC, assume all devices in shard notified
            return (seqNo, 1000); // Mock value
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task<(ulong seqNo, int devicesNotified)> RemoveFromBlacklistAsync(
        IEnumerable<string> panTokens,
        int shardId,
        string reason,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        // If shardId is -1, apply to all shards
        if (shardId == -1)
        {
            var tasks = _blacklistByShardId.Keys
                .Select(sid => RemoveFromBlacklistAsync(panTokens, sid, reason, updatedBy, cancellationToken))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var totalDevices = results.Sum(r => r.devicesNotified);
            var maxSeqNo = results.Max(r => r.seqNo);

            return (maxSeqNo, totalDevices);
        }

        await EnsureShardInitializedAsync(shardId);

        var lockObj = _locksByShardId[shardId];
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            var tokens = panTokens.ToList();
            var blacklist = _blacklistByShardId[shardId];
            var tokensToRemove = tokens.Where(t => blacklist.Contains(t)).ToList();

            if (!tokensToRemove.Any())
            {
                _logger.LogInformation("No tokens to remove from blacklist shard {ShardId}", shardId);
                return (_seqNoByShardId[shardId], 0);
            }

            foreach (var token in tokensToRemove)
            {
                blacklist.Remove(token);
            }

            _seqNoByShardId[shardId]++;
            var seqNo = _seqNoByShardId[shardId];

            var delta = new BlacklistDelta
            {
                DeltaId = Guid.NewGuid().ToString(),
                SeqNo = seqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ShardId = shardId
            };

            delta.Removed.AddRange(tokensToRemove);

            // Sign the delta
            var deltaJson = JsonSerializer.Serialize(new { delta.DeltaId, delta.SeqNo, delta.TimestampUtc, delta.Removed, delta.ShardId });
            delta.Signature = _tokenizer.SignMessage(deltaJson);

            // Add to history for catch-up
            _deltaHistory.Add(delta);
            TrimDeltaHistory();

            // Persist to cache
            await PersistBlacklistStateAsync(shardId);

            _logger.LogInformation(
                "Removed {Count} tokens from blacklist shard {ShardId} (SeqNo: {SeqNo}). Reason: {Reason}, By: {UpdatedBy}",
                tokensToRemove.Count, shardId, seqNo, reason, updatedBy);

            // For PoC, assume all devices in shard notified
            return (seqNo, 1000); // Mock value
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task<BlacklistState> GetBlacklistStateAsync(int shardId, CancellationToken cancellationToken = default)
    {
        if (shardId == -1)
        {
            // Return combined state of all shards
            var allTokens = new HashSet<string>();
            ulong maxSeqNo = 0;

            foreach (var sid in _blacklistByShardId.Keys)
            {
                var state = await GetBlacklistStateAsync(sid, cancellationToken);
                allTokens.UnionWith(state.PanTokens);
                if (state.CurrentSeqNo > maxSeqNo)
                    maxSeqNo = state.CurrentSeqNo;
            }

            return new BlacklistState
            {
                CurrentSeqNo = maxSeqNo,
                LastUpdatedAt = DateTime.UtcNow,
                PanTokens = allTokens,
                ShardId = -1
            };
        }

        await EnsureShardInitializedAsync(shardId);

        var lockObj = _locksByShardId[shardId];
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            return new BlacklistState
            {
                CurrentSeqNo = _seqNoByShardId[shardId],
                LastUpdatedAt = DateTime.UtcNow,
                PanTokens = new HashSet<string>(_blacklistByShardId[shardId]),
                ShardId = shardId
            };
        }
        finally
        {
            lockObj.Release();
        }
    }

    public Task<IEnumerable<BlacklistDelta>> GetDeltasSinceAsync(
        ulong sinceSeqNo,
        int shardId,
        CancellationToken cancellationToken = default)
    {
        var deltas = _deltaHistory
            .Where(d => d.SeqNo > sinceSeqNo && (shardId == -1 || d.ShardId == shardId))
            .OrderBy(d => d.SeqNo)
            .AsEnumerable();

        return Task.FromResult(deltas);
    }

    public async Task<bool> IsBlacklistedAsync(string panToken, int shardId, CancellationToken cancellationToken = default)
    {
        await EnsureShardInitializedAsync(shardId);

        var lockObj = _locksByShardId[shardId];
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            return _blacklistByShardId[shardId].Contains(panToken);
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task<int> GetBlacklistCountAsync(int shardId, CancellationToken cancellationToken = default)
    {
        if (shardId == -1)
        {
            var counts = await Task.WhenAll(
                _blacklistByShardId.Keys.Select(sid => GetBlacklistCountAsync(sid, cancellationToken)));
            return counts.Sum();
        }

        await EnsureShardInitializedAsync(shardId);

        var lockObj = _locksByShardId[shardId];
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            return _blacklistByShardId[shardId].Count;
        }
        finally
        {
            lockObj.Release();
        }
    }

    private async Task EnsureShardInitializedAsync(int shardId)
    {
        if (!_blacklistByShardId.ContainsKey(shardId))
        {
            await InitializeShardAsync(shardId);
        }
    }

    private async Task PersistBlacklistStateAsync(int shardId)
    {
        var state = new BlacklistState
        {
            CurrentSeqNo = _seqNoByShardId[shardId],
            LastUpdatedAt = DateTime.UtcNow,
            PanTokens = new HashSet<string>(_blacklistByShardId[shardId]),
            ShardId = shardId
        };

        var json = JsonSerializer.Serialize(state);
        var cacheKey = BLACKLIST_CACHE_KEY_PREFIX + shardId;
        await _cache.SetStringAsync(cacheKey, json);

        var seqNoKey = SEQNO_CACHE_KEY_PREFIX + shardId;
        await _cache.SetStringAsync(seqNoKey, _seqNoByShardId[shardId].ToString());
    }

    private void TrimDeltaHistory()
    {
        if (_deltaHistory.Count > MAX_DELTA_HISTORY)
        {
            var toRemove = _deltaHistory.Count - MAX_DELTA_HISTORY;
            _logger.LogInformation("Trimming delta history: removing {Count} old deltas", toRemove);
            // Note: ConcurrentBag doesn't support efficient removal
            // In production, use a different data structure
        }
    }
}
