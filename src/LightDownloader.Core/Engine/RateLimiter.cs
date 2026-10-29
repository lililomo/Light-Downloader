using System.Diagnostics;

namespace LightDownloader.Core.Engine;

/// <summary>
/// Global token-bucket throttle shared by every segment worker.
/// A limit of 0 means unlimited.
/// </summary>
public sealed class RateLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastTicks = Stopwatch.GetTimestamp();

    public RateLimiter(long bytesPerSecond) => _bytesPerSecond = bytesPerSecond;

    public void SetLimit(long bytesPerSecond)
    {
        lock (_lock)
        {
            Refill();
            _bytesPerSecond = bytesPerSecond;
        }
    }

    public async Task ThrottleAsync(int bytes, CancellationToken ct)
    {
        while (true)
        {
            int waitMs;
            lock (_lock)
            {
                if (_bytesPerSecond <= 0) return;
                Refill();
                double cap = Math.Max(_bytesPerSecond, bytes);
                if (_tokens > cap) _tokens = cap;
                if (_tokens >= bytes) { _tokens -= bytes; return; }
                double needed = bytes - _tokens;
                waitMs = (int)Math.Min(1000, needed / _bytesPerSecond * 1000.0) + 1;
            }
            await Task.Delay(waitMs, ct).ConfigureAwait(false);
        }
    }

    private void Refill()
    {
        long now = Stopwatch.GetTimestamp();
        double sec = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        if (_bytesPerSecond > 0)
            _tokens = Math.Min(_bytesPerSecond, _tokens + sec * _bytesPerSecond);
    }
}
