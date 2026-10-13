using System;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Data;
using VPHelper.Core.Models;

namespace VPHelper.Core.Services;

/// <summary>Persistent, account-scoped cooldowns; failed enrichment must not block every sync.</summary>
public sealed class SyncRetryCache(VpDbContext db)
{
    public async Task<bool> IsCoolingDownAsync(string key, CancellationToken ct)
    {
        var row = await db.AppMetadata.FindAsync(new object[] { key }, ct);
        return row != null && long.TryParse(row.Value, out var until) &&
            until > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task SetAsync(string key, TimeSpan? cooldown, CancellationToken ct)
    {
        var row = await db.AppMetadata.FindAsync(new object[] { key }, ct);
        if (cooldown == null)
        {
            if (row != null) db.AppMetadata.Remove(row);
        }
        else
        {
            if (row == null) { row = new AppMetadata { Key = key }; db.AppMetadata.Add(row); }
            row.Value = DateTimeOffset.UtcNow.Add(cooldown.Value).ToUnixTimeSeconds().ToString();
        }
        await db.SaveChangesAsync(ct);
    }
}
