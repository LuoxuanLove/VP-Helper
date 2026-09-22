using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Scraping;

namespace VPHelper.Core.Services;

/// <summary>Resolves Codeforces Gym clone chains through the logged-in browser fetcher.</summary>
public class GymCloneService
{
    private const int MaxDepth = 8;

    private readonly IBrowserFetcher _fetcher;
    private readonly Throttle _throttle;
    private readonly BackoffPolicy _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GymCloneService(
        IBrowserFetcher fetcher,
        Throttle throttle,
        BackoffPolicy? backoff = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _fetcher = fetcher;
        _throttle = throttle;
        _backoff = backoff ?? new BackoffPolicy();
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    public async Task<GymCloneChain> ResolveAsync(long gymId, CancellationToken ct = default)
    {
        var visited = new HashSet<long> { gymId };
        var visitedOrder = new List<long>();
        var edges = new List<GymCloneEdge>();
        long current = gymId;
        long? root = null;
        bool complete = false;

        for (int depth = 0; depth < MaxDepth; depth++)
        {
            visitedOrder.Add(current);
            var html = await FetchGymPageAsync(current, ct).ConfigureAwait(false);
            if (html == null)
                return new GymCloneChain(edges, visitedOrder, null, IsComplete: false);

            var clone = GymCloneParser.Parse(html);
            if (clone == null)
            {
                root = current == gymId ? null : current;
                complete = true;
                break;
            }

            edges.Add(new GymCloneEdge(current, clone.ParentGymId, clone.ParentTitle));

            if (!visited.Add(clone.ParentGymId))
            {
                root = current;
                complete = false;
                break;
            }

            current = clone.ParentGymId;
        }

        return new GymCloneChain(edges, visitedOrder, complete ? root : null, complete);
    }

    private async Task<string?> FetchGymPageAsync(long gymId, CancellationToken ct)
    {
        var url = $"https://codeforces.com/gym/{gymId}";
        for (int attempt = 0; attempt <= _backoff.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await _throttle.WaitAsync(ct).ConfigureAwait(false);

            FetchResult result;
            try
            {
                result = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = new FetchResult(url, null, FetchStatus.Error, ex.Message);
            }

            if (result.Status == FetchStatus.Ok)
                return result.Body;

            if (attempt == _backoff.MaxRetries)
                return null;

            await _delay(_backoff.DelayFor(attempt), ct).ConfigureAwait(false);
        }

        return null;
    }
}

public record GymCloneEdge(long GymId, long ParentGymId, string ParentTitle);

public record GymCloneChain(
    IReadOnlyList<GymCloneEdge> Edges,
    IReadOnlyList<long> VisitedGymIds,
    long? RootGymId,
    bool IsComplete)
{
    public long? DirectParentGymId => Edges.Count > 0 ? Edges[0].ParentGymId : null;
    public string? DirectParentTitle => Edges.Count > 0 ? Edges[0].ParentTitle : null;
}
