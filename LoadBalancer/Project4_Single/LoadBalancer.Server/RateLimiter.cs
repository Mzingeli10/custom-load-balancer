using System.Collections.Concurrent;

namespace LoadBalancer.Server;

public class RateLimiter
{
    private record Bucket(double Tokens, DateTime LastRefill);
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly double _maxTokens;
    private readonly double _refillRatePerSecond;

    public RateLimiter(double maxTokens = 20, double refillRatePerSecond = 5)
    {
        _maxTokens = maxTokens;
        _refillRatePerSecond = refillRatePerSecond;
    }

    public bool Allow(string clientIp)
    {
        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(clientIp, _ => new Bucket(_maxTokens, now));
        var elapsed = (now - bucket.LastRefill).TotalSeconds;
        var newTokens = Math.Min(_maxTokens, bucket.Tokens + elapsed * _refillRatePerSecond);
        if (newTokens < 1)
        {
            _buckets[clientIp] = bucket with { Tokens = newTokens, LastRefill = now };
            return false;
        }
        _buckets[clientIp] = new Bucket(newTokens - 1, now);
        return true;
    }

    public (double Tokens, double Max) GetStatus(string clientIp)
        => _buckets.TryGetValue(clientIp, out var b) ? (b.Tokens, _maxTokens) : (_maxTokens, _maxTokens);
}
