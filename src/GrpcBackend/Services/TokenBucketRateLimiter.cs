using System.Collections.Concurrent;

namespace GrpcBackend.Services;

/// <summary>
/// Token bucket rate limiter implementation
/// Simple rate limiting to prevent batch spam from devices
/// </summary>
public class TokenBucketRateLimiter : IRateLimiter
{
    private readonly int _maxTokens;
    private readonly int _refillRatePerMinute;
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();

    public TokenBucketRateLimiter(int maxTokens = 60, int refillRatePerMinute = 60)
    {
        _maxTokens = maxTokens;
        _refillRatePerMinute = refillRatePerMinute;
    }

    public bool AllowBatch(string deviceId)
    {
        var bucket = _buckets.GetOrAdd(deviceId, _ => new TokenBucket(_maxTokens, _refillRatePerMinute));
        return bucket.TryConsume();
    }

    public int GetRemainingQuota(string deviceId)
    {
        if (_buckets.TryGetValue(deviceId, out var bucket))
        {
            return bucket.GetAvailableTokens();
        }
        return _maxTokens;
    }

    public void Reset(string deviceId)
    {
        _buckets.TryRemove(deviceId, out _);
    }

    private class TokenBucket
    {
        private readonly int _maxTokens;
        private readonly double _refillRatePerSecond;
        private double _tokens;
        private DateTime _lastRefill;
        private readonly object _lock = new();

        public TokenBucket(int maxTokens, int refillRatePerMinute)
        {
            _maxTokens = maxTokens;
            _refillRatePerSecond = refillRatePerMinute / 60.0;
            _tokens = maxTokens;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryConsume()
        {
            lock (_lock)
            {
                Refill();

                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return true;
                }

                return false;
            }
        }

        public int GetAvailableTokens()
        {
            lock (_lock)
            {
                Refill();
                return (int)Math.Floor(_tokens);
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRefill).TotalSeconds;

            if (elapsed > 0)
            {
                var tokensToAdd = elapsed * _refillRatePerSecond;
                _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
                _lastRefill = now;
            }
        }
    }
}
