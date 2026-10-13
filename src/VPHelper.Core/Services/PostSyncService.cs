using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;

namespace VPHelper.Core.Services;

public record PostSyncResult(int Matched, int Updated, int Failed);

/// <summary>Only verified directory links/clone edges are eligible; manual choices are preserved.</summary>
public sealed class PostSyncService(VpDbContext db, RanklandClient client)
{
    public async Task<PostSyncResult> RunAsync(Platform platform, string handle,
        IProgress<RanklandProgress>? progress = null, CancellationToken ct = default)
    {
        var sync = new RanklandSyncService(db, client);
        progress?.Report(new RanklandProgress(0, 0, "正在刷新目录并自动关联比赛"));
        await sync.SyncCatalogAsync(ct);
        var mapping = await new MappingService(db).AutoMatchAllAsync(ct: ct);
        var gyms = db.Submissions.Where(s => s.Platform == platform && s.Handle == handle).Select(s => s.GymId);
        var ids = await db.ContestMappings.Where(m => m.Platform == platform && gyms.Contains(m.GymId) &&
                (m.Confirmed == MappingStatus.ManualConfirmed || m.Confirmed == MappingStatus.AutoConfirmed))
            .Select(m => m.OfficialId).Distinct().ToListAsync(ct);
        var updated = await sync.SyncSelectedAsync(ids, progress: progress, ct: ct);
        return new PostSyncResult(mapping.AutoConfirmed, updated, sync.FailedContests.Count);
    }
}
