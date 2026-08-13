namespace LancachePrefill;

/// <summary>
/// Token-bucket rate limiter shared across concurrent chunk downloads.
/// Rate 0 (or negative) means unlimited. Bucket capacity is one second of
/// tokens, so short bursts smooth out without sustained overshoot.
/// </summary>
public sealed class BandwidthLimiter
{
    private readonly object _lock = new();
    private double _tokens;
    private long _bytesPerSec;
    private DateTime _lastRefill = DateTime.UtcNow;

    public void SetRate(long bytesPerSec)
    {
        lock (_lock)
        {
            _bytesPerSec = bytesPerSec;
            _tokens = Math.Min(_tokens, Math.Max(0, bytesPerSec));
            _lastRefill = DateTime.UtcNow;
        }
    }

    /// <summary>Blocks until <paramref name="bytes"/> tokens are available. No-op when unlimited.</summary>
    public async Task WaitAsync(int bytes, CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_lock)
            {
                if (_bytesPerSec <= 0) return;
                var now = DateTime.UtcNow;
                _tokens = Math.Min(_bytesPerSec, _tokens + (now - _lastRefill).TotalSeconds * _bytesPerSec);
                _lastRefill = now;
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }
                wait = TimeSpan.FromSeconds(Math.Clamp((bytes - _tokens) / _bytesPerSec, 0.01, 1.0));
            }
            await Task.Delay(wait, ct);
        }
    }
}
