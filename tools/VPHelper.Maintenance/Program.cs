using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: VPHelper.Maintenance <existing database> <srk-collection snapshot directory>");
    return 2;
}
var dbPath = Path.GetFullPath(args[0]);
var snapshot = Path.GetFullPath(args[1]);
if (!File.Exists(dbPath) || !File.Exists(Path.Combine(snapshot, "official", "config.yaml")))
    throw new ArgumentException("Existing database and official/config.yaml are required.");
var backup = dbPath + ".before-rankland-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db";
using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup }.ToString()))
{
    source.Open(); target.Open(); source.BackupDatabase(target);
}
Console.WriteLine($"Backup: {backup}");
var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()).Options;
await using var db = new VpDbContext(options);
SchemaGuard.Reconcile(db);
var sync = new RanklandSyncService(db, new RanklandClient(new SnapshotSource(snapshot)));
var updated = await sync.SyncAsync(forceRefresh: true, progress: new ConsoleProgress());
var mapping = await new MappingService(db).AutoMatchAllAsync();
Console.WriteLine($"Updated={updated}; Failed={sync.FailedContests.Count}; AutoMatched={mapping.AutoConfirmed}; ManualPreserved={mapping.SkippedManual}; Standings={await db.OfficialStandings.CountAsync()}");
foreach (var id in sync.FailedContests) Console.WriteLine($"FAILED: {id}");
return sync.FailedContests.Count == 0 ? 0 : 1;

sealed class ConsoleProgress : IProgress<RanklandProgress>
{
    public void Report(RanklandProgress p) => Console.WriteLine($"{p.Done}/{p.Total} {p.Message}");
}

sealed class SnapshotSource(string root) : IRawContentSource
{
    public async Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default)
    {
        const string prefix = "https://raw.githubusercontent.com/algoux/srk-collection/master/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Unexpected source URL.");
        var file = Path.GetFullPath(Path.Combine(root, url[prefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Snapshot path escaped its root.");
        return File.Exists(file) ? new RawContent(url, await File.ReadAllTextAsync(file, ct), null, false) : null;
    }
}
