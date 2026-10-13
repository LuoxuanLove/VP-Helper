using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Scraping;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

public class SyncReliabilityTests
{
    private sealed class Fetcher(Func<string, FetchResult> reply) : IBrowserFetcher
    {
        public int Calls;
        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(reply(url));
        }
    }

    [Fact]
    public async Task PermanentApiFailure_PreservesReasonWithoutRetry()
    {
        var f = new Fetcher(u => new(u, "{\"status\":\"FAILED\",\"comment\":\"contest unavailable\"}", FetchStatus.Error, "HTTP 400", 400));
        var api = new ContestStatusApi(f, new Throttle(TimeSpan.Zero), delay: (_, _) => throw new Exception("Unexpected retry"));
        var result = await api.FetchGymAsync(679438, "alice");
        Assert.False(result.Ok);
        Assert.Equal("contest unavailable", result.Comment);
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task RateLimit_IsStillRetried()
    {
        int calls = 0, waits = 0;
        var f = new Fetcher(u => ++calls == 1 ? new(u, null, FetchStatus.RateLimited, "HTTP 429", 429)
            : new(u, "{\"status\":\"OK\",\"result\":[]}", FetchStatus.Ok));
        var api = new ContestStatusApi(f, new Throttle(TimeSpan.Zero), delay: (_, _) => { waits++; return Task.CompletedTask; });
        Assert.True((await api.FetchGymAsync(1, "alice")).Ok);
        Assert.Equal(2, f.Calls);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task CloneChain_UsesPersistedEdgesWithoutNetwork()
    {
        var f = new Fetcher(_ => throw new Exception("Unexpected request"));
        var service = new GymCloneService(f, new Throttle(TimeSpan.Zero));
        service.SeedCache(new[] {
            new GymContest { GymId = 10, CloneParentGymId = 20, CloneCheckedAt = 1 },
            new GymContest { GymId = 20, CloneCheckedAt = 1 }
        });
        var chain = await service.ResolveAsync(10);
        Assert.True(chain.IsComplete);
        Assert.Equal(20L, chain.RootGymId);
        Assert.Equal(0, f.Calls);
    }

    [Fact]
    public async Task CloneRoot_IsFetchedOncePerPipeline()
    {
        var f = new Fetcher(u => new(u, "<html><body>No clone</body></html>", FetchStatus.Ok));
        var service = new GymCloneService(f, new Throttle(TimeSpan.Zero));
        await service.ResolveAsync(20);
        await service.ResolveAsync(20);
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task IncrementalSync_DoesNotRepeatUnavailableApiOnNextRun()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        db.Submissions.Add(new Submission { Id = 1, GymId = 679438, Handle = "alice", ProblemIndex = "A", Verdict = "OK" });
        await db.SaveChangesAsync();
        var html = new Fetcher(u => new(u, "<html><body><table></table></body></html>", FetchStatus.Ok));
        var api = new Fetcher(u => new(u, "{\"status\":\"FAILED\",\"comment\":\"unavailable\"}", FetchStatus.Error, "HTTP 400", 400));
        var service = new GymSyncService(db, new SubmissionScraper(html, new Throttle(TimeSpan.Zero)),
            new ContestStatusApi(api, new Throttle(TimeSpan.Zero)));
        await service.SyncAsync("alice");
        await service.SyncAsync("alice");
        Assert.Equal(1, api.Calls);
        Assert.Equal(1, await db.Submissions.CountAsync());
    }

    [Fact]
    public async Task Cooldown_PersistsAndIsAccountScoped()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        await using (var db = new VpDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await new SyncRetryCache(db).SetAsync("cf-type-retry:alice:10", TimeSpan.FromHours(6), default);
        }
        await using var fresh = new VpDbContext(options);
        var cache = new SyncRetryCache(fresh);
        Assert.True(await cache.IsCoolingDownAsync("cf-type-retry:alice:10", default));
        Assert.False(await cache.IsCoolingDownAsync("cf-type-retry:bob:10", default));
        await cache.SetAsync("cf-type-retry:alice:10", TimeSpan.FromSeconds(-1), default);
        Assert.False(await cache.IsCoolingDownAsync("cf-type-retry:alice:10", default));
    }
}
