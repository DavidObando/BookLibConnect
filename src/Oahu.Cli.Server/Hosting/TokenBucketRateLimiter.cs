using System;
using System.Collections.Concurrent;

namespace Oahu.Cli.Server.Hosting;

/// <summary>
/// Lightweight token-bucket rate limiter keyed by bearer token. Default policy
/// is 60 requests / minute with a burst of 10 (per design §15.2). The bucket
/// refills continuously based on elapsed wall-clock time so brief idle periods
/// regenerate budget naturally.
/// </summary>
/// <remarks>
/// We intentionally avoid the framework <c>RateLimiter</c> middleware here —
/// the policy is small, the per-token semantics are simpler to express
/// explicitly, and we want to keep transport-layer concerns inside the Server
/// project rather than spreading a new ASP.NET configuration surface.
/// </remarks>
public sealed class TokenBucketRateLimiter
{
    /// <summary>Sustained refill in tokens per second (60/minute = 1.0).</summary>
    private const double DefaultRatePerSecond = 1.0;

    /// <summary>Maximum tokens any caller may accumulate (burst capacity).</summary>
    private const int DefaultBurst = 10;

    private readonly ConcurrentDictionary<string, Bucket> buckets = new(StringComparer.Ordinal);
    private readonly double ratePerSecond;
    private readonly int burst;

    public TokenBucketRateLimiter()
        : this(DefaultRatePerSecond, DefaultBurst)
    {
    }

    public TokenBucketRateLimiter(double ratePerSecond, int burst)
    {
        if (ratePerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratePerSecond));
        }
        if (burst <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(burst));
        }
        this.ratePerSecond = ratePerSecond;
        this.burst = burst;
    }

    /// <summary>Try to consume one token. Returns false when the caller is rate-limited.</summary>
    public bool TryAcquire(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return true; // no key = no enforcement (e.g. unauthenticated request, which auth will reject anyway)
        }

        var bucket = this.buckets.GetOrAdd(key, _ => new Bucket(this.burst));
        lock (bucket)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - bucket.LastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                bucket.Tokens = Math.Min(this.burst, bucket.Tokens + (elapsed * this.ratePerSecond));
                bucket.LastRefill = now;
            }
            if (bucket.Tokens < 1.0)
            {
                return false;
            }
            bucket.Tokens -= 1.0;
            return true;
        }
    }

    private sealed class Bucket
    {
        public Bucket(int initialTokens)
        {
            this.Tokens = initialTokens;
            this.LastRefill = DateTimeOffset.UtcNow;
        }

        public double Tokens { get; set; }

        public DateTimeOffset LastRefill { get; set; }
    }
}
