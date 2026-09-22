using System;
using System.Threading;
using System.Threading.Tasks;

namespace VPHelper.Core.Scraping;

/// <summary>
/// Serial request pacer: guarantees a minimum interval between consecutive calls
/// (DESIGN 2.4 / 6.2 — all fetching serial, ≥2s configurable). Thread-safe.
/// </summary>
public class Throttle
{
    private readonly TimeSpan _minInterval;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastAt = DateTimeOffset.MinValue;

    public Throttle(TimeSpan minInterval, Func<DateTimeOffset>? now = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _minInterval = minInterval;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    /// <summary>Wait until at least the min interval has elapsed since the previous call.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _now();
            var elapsed = now - _lastAt;
            if (elapsed < _minInterval)
                await _delay(_minInterval - elapsed, ct).ConfigureAwait(false);
            _lastAt = _now();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Exponential backoff schedule for retrying failed fetches (DESIGN 6.2).
/// Attempt 0 → Base, attempt n → Base * 2^n, capped at Max, with optional jitter fraction.
/// </summary>
public class BackoffPolicy
{
    public TimeSpan Base { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan Max { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxRetries { get; init; } = 5;

    public TimeSpan DelayFor(int attempt)
    {
        if (attempt < 0) attempt = 0;
        double factor = Math.Pow(2, attempt);
        double ms = Base.TotalMilliseconds * factor;
        if (ms > Max.TotalMilliseconds || double.IsInfinity(ms)) ms = Max.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(ms);
    }
}
