using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

public class ThrottleTests
{
    [Fact]
    public async Task Throttle_DelaysWhenCalledTooSoon()
    {
        var now = DateTimeOffset.UnixEpoch;
        var delays = new List<TimeSpan>();
        var throttle = new Throttle(
            TimeSpan.FromSeconds(2),
            now: () => now,
            delay: (d, _) => { delays.Add(d); now += d; return Task.CompletedTask; });

        await throttle.WaitAsync(); // first call: no wait
        await throttle.WaitAsync(); // immediately after: must wait ~2s

        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[0]);
    }

    [Fact]
    public async Task Throttle_NoDelayWhenEnoughElapsed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var delays = new List<TimeSpan>();
        var throttle = new Throttle(
            TimeSpan.FromSeconds(2),
            now: () => now,
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        await throttle.WaitAsync();
        now += TimeSpan.FromSeconds(5);
        await throttle.WaitAsync();

        Assert.Empty(delays);
    }

    [Fact]
    public void Backoff_ExponentialAndCapped()
    {
        var b = new BackoffPolicy
        {
            Base = TimeSpan.FromSeconds(2),
            Max = TimeSpan.FromSeconds(30),
            MaxRetries = 10,
        };
        Assert.Equal(TimeSpan.FromSeconds(2), b.DelayFor(0));
        Assert.Equal(TimeSpan.FromSeconds(4), b.DelayFor(1));
        Assert.Equal(TimeSpan.FromSeconds(8), b.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(16), b.DelayFor(3));
        Assert.Equal(TimeSpan.FromSeconds(30), b.DelayFor(4)); // capped
        Assert.Equal(TimeSpan.FromSeconds(30), b.DelayFor(9)); // stays capped
    }
}
