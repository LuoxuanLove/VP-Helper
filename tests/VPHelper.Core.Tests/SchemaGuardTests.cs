using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using Xunit;

namespace VPHelper.Core.Tests;

public class SchemaGuardTests
{
    // Build a context over a shared in-memory connection so schema changes made on the raw
    // connection (simulating an older DB file) are visible to EF and survive across contexts.
    private static (SqliteConnection Conn, VpDbContext Ctx) OpenShared()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        return (conn, new VpDbContext(options));
    }

    [Fact]
    public async Task Reconcile_AddsMissingColumn_ToOldSchema()
    {
        var (conn, ctx) = OpenShared();
        await using var _ = ctx;

        // Simulate a DB created before LastSyncedAt existed: an Accounts table with only Handle.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE \"Accounts\" (\"Handle\" TEXT NOT NULL CONSTRAINT \"PK_Accounts\" PRIMARY KEY)";
            cmd.ExecuteNonQuery();
        }

        // Before reconciling, any EF query touching LastSyncedAt throws 'no such column'.
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await ctx.Accounts.Select(a => a.LastSyncedAt).ToListAsync());

        SchemaGuard.Reconcile(ctx);

        // After reconciling, the column exists and round-trips through EF.
        ctx.Accounts.Add(new Account { Handle = "tourist", LastSyncedAt = 42 });
        await ctx.SaveChangesAsync();

        var back = await ctx.Accounts.SingleAsync(a => a.Handle == "tourist");
        Assert.Equal(42, back.LastSyncedAt);
    }

    [Fact]
    public async Task Reconcile_CreatesMissingTable_ToOldSchema()
    {
        var (conn, ctx) = OpenShared();
        await using var _ = ctx;
        using var __ = conn;

        // Simulate a Phase 1 DB: only the Accounts table exists; the Phase 2 tables
        // (OfficialContests / OfficialStandings / ContestMappings) were never created.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE \"Accounts\" (\"Handle\" TEXT NOT NULL CONSTRAINT \"PK_Accounts\" PRIMARY KEY, \"LastSyncedAt\" INTEGER NULL)";
            cmd.ExecuteNonQuery();
        }

        // Before reconciling, any EF query against a Phase 2 table throws 'no such table'.
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await ctx.ContestMappings.ToListAsync());

        SchemaGuard.Reconcile(ctx);

        // After reconciling, the composite-key table exists and round-trips through EF.
        ctx.ContestMappings.Add(new ContestMapping { GymId = 1, OfficialId = "x", Confidence = 0.9, Confirmed = 1 });
        await ctx.SaveChangesAsync();

        var back = await ctx.ContestMappings.SingleAsync();
        Assert.Equal(1, back.GymId);
        Assert.Equal("x", back.OfficialId);

        // The pre-existing Accounts table (and its data column) is left intact.
        ctx.Accounts.Add(new Account { Handle = "tourist", LastSyncedAt = 5 });
        await ctx.SaveChangesAsync();
        Assert.Equal(5, (await ctx.Accounts.SingleAsync()).LastSyncedAt);
    }

    [Fact]
    public async Task Reconcile_DropsInsertBreakingOrphanColumn_AfterRename()
    {
        var (conn, ctx) = OpenShared();
        await using var _ = ctx;
        using var __ = conn;

        // Simulate a DB where Submission.SubmitTimeUnixSeconds was later renamed to SubmitTime:
        // the old NOT NULL column with no default lingers, and an index still references it.
        // EF never supplies a value for the orphan, so every INSERT fails the NOT NULL constraint.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Submissions"" (
                    ""Platform"" INTEGER NOT NULL,
                    ""Id"" INTEGER NOT NULL,
                    ""Handle"" TEXT NOT NULL,
                    ""GymId"" INTEGER NOT NULL,
                    ""ProblemIndex"" TEXT NOT NULL,
                    ""ProblemName"" TEXT NOT NULL,
                    ""Verdict"" TEXT NOT NULL,
                    ""SubmitTimeUnixSeconds"" INTEGER NOT NULL,
                    CONSTRAINT ""PK_Submissions"" PRIMARY KEY (""Platform"", ""Id""));
                CREATE INDEX ""IX_Submissions_Handle_GymId_SubmitTimeUnixSeconds""
                    ON ""Submissions"" (""Handle"", ""GymId"", ""SubmitTimeUnixSeconds"");";
            cmd.ExecuteNonQuery();
        }

        SchemaGuard.Reconcile(ctx);

        // After reconciling, the new SubmitTime column exists and the orphan is gone, so an
        // INSERT that only fills the model's columns succeeds.
        ctx.Submissions.Add(new Submission
        {
            Id = 1, Handle = "tourist", GymId = 100, ProblemIndex = "A",
            ProblemName = "P", Verdict = "OK", SubmitTime = 1700000000,
        });
        await ctx.SaveChangesAsync();

        var back = await ctx.Submissions.SingleAsync();
        Assert.Equal(1700000000, back.SubmitTime);

        // The orphan column and its index are gone.
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Submissions') WHERE name='SubmitTimeUnixSeconds'";
            Assert.Equal(0L, (long)check.ExecuteScalar()!);
        }
    }

    [Fact]
    public async Task Reconcile_KeepsHarmlessNullableOrphanColumn()
    {
        var (conn, ctx) = OpenShared();
        await using var _ = ctx;
        using var __ = conn;

        // A nullable orphan column does not break inserts, so the guard must leave it alone
        // (data-preserving contract).
        // The table must already carry the model's current composite (Platform, Id) primary key, so
        // the guard takes the column-reconcile path (which preserves the orphan) rather than the
        // redesigned-table rebuild path (which would drop it — covered by its own test below).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Submissions"" (
                    ""Platform"" INTEGER NOT NULL,
                    ""Id"" INTEGER NOT NULL,
                    ""Handle"" TEXT NOT NULL,
                    ""GymId"" INTEGER NOT NULL,
                    ""ProblemIndex"" TEXT NOT NULL,
                    ""ProblemName"" TEXT NOT NULL,
                    ""Verdict"" TEXT NOT NULL,
                    ""SubmitTime"" INTEGER NOT NULL DEFAULT 0,
                    ""LegacyNote"" TEXT NULL,
                    CONSTRAINT ""PK_Submissions"" PRIMARY KEY (""Platform"", ""Id""));";
            cmd.ExecuteNonQuery();
        }

        SchemaGuard.Reconcile(ctx);

        ctx.Submissions.Add(new Submission
        {
            Id = 1, Handle = "tourist", GymId = 100, ProblemIndex = "A",
            ProblemName = "P", Verdict = "OK", SubmitTime = 1700000000,
        });
        await ctx.SaveChangesAsync();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Submissions') WHERE name='LegacyNote'";
        Assert.Equal(1L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public async Task Reconcile_RebuildsRedesignedTable_WhenPrimaryKeyChanged()
    {
        var (conn, ctx) = OpenShared();
        await using var _ = ctx;
        using var __ = conn;

        // Simulate the legacy OfficialStandings: a composite (OfficialId, RowKey) primary key with
        // the old column names (PenaltySeconds/IsOfficial/HasEffectiveSubmission/PerProblemJson),
        // before the redesign to a synthetic Id PK. Column-level ALTER cannot reshape a PK, so the
        // guard must drop and recreate the table from the current model.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""OfficialStandings"" (
                    ""OfficialId"" TEXT NOT NULL,
                    ""RowKey"" TEXT NOT NULL,
                    ""Rank"" INTEGER NOT NULL,
                    ""TeamName"" TEXT NOT NULL,
                    ""Organization"" TEXT NULL,
                    ""SolvedCount"" INTEGER NOT NULL,
                    ""PenaltySeconds"" INTEGER NOT NULL,
                    ""IsOfficial"" INTEGER NOT NULL,
                    ""HasEffectiveSubmission"" INTEGER NOT NULL,
                    ""PerProblemJson"" TEXT NOT NULL,
                    CONSTRAINT ""PK_OfficialStandings"" PRIMARY KEY (""OfficialId"", ""RowKey""));
                INSERT INTO ""OfficialStandings""
                    (""OfficialId"", ""RowKey"", ""Rank"", ""TeamName"", ""Organization"",
                     ""SolvedCount"", ""PenaltySeconds"", ""IsOfficial"", ""HasEffectiveSubmission"", ""PerProblemJson"")
                    VALUES ('c1', 'r1', 1, 'Team', 'Org', 3, 120, 1, 1, '{}');";
            cmd.ExecuteNonQuery();
        }

        SchemaGuard.Reconcile(ctx);

        // The rebuilt table has the model's synthetic Id PK, so an INSERT against the current model
        // succeeds (this would fail on the legacy schema — no Id column, wrong PK, missing columns).
        ctx.OfficialStandings.Add(new OfficialStanding
        {
            OfficialId = "c1", Rank = 1, TeamName = "Team", Organization = "Org",
            SolvedCount = 3, Penalty = 120, Official = true, HadValidSubmission = true,
            PerProblem = "{}",
        });
        await ctx.SaveChangesAsync();

        var back = await ctx.OfficialStandings.SingleAsync();
        Assert.True(back.Id > 0);         // synthetic surrogate key assigned by the rebuilt table
        Assert.Equal("c1", back.OfficialId);
        Assert.Equal(120, back.Penalty);

        // The legacy PK column is gone from the rebuilt table.
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('OfficialStandings') WHERE name='RowKey'";
        Assert.Equal(0L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void Reconcile_IsIdempotent_OnCurrentSchema()
    {
        var (conn, ctx) = OpenShared();
        using var _ = ctx;
        using var __ = conn;

        ctx.Database.EnsureCreated(); // current schema already has LastSyncedAt

        // Calling repeatedly must not throw (duplicate-column) and must leave the column intact.
        SchemaGuard.Reconcile(ctx);
        SchemaGuard.Reconcile(ctx);

        ctx.Accounts.Add(new Account { Handle = "petr", LastSyncedAt = 7 });
        ctx.SaveChanges();
        Assert.Equal(7, ctx.Accounts.Single().LastSyncedAt);
    }
}
