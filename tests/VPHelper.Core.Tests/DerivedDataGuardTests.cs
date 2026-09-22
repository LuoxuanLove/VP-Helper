using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using Xunit;

namespace VPHelper.Core.Tests;

public class DerivedDataGuardTests
{
    private static VpDbContext OpenDb(string dbPath)
    {
        var options = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new VpDbContext(options);
        ctx.Database.EnsureCreated();
        SchemaGuard.Reconcile(ctx);
        return ctx;
    }

    [Fact]
    public async Task Reconcile_RebuildsStaleDerivedResults_WithoutDroppingSubmissions()
    {
        const string handle = "LuoxuanLove";
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_guard_{Guid.NewGuid():N}.db");
        try
        {
            await using var db = OpenDb(dbPath);
            db.Submissions.Add(new Submission
            {
                Platform = Platform.Codeforces,
                Id = 1,
                Handle = handle,
                GymId = 105486,
                ProblemIndex = "A",
                Verdict = "OK",
                SubmitTime = 1000,
            });
            db.VpResults.Add(new VpResult
            {
                Platform = Platform.Codeforces,
                Handle = handle,
                GymId = 105486,
                Kind = ParticipationKind.Practice,
                SolvedCount = 99,
                Penalty = 0,
                FirstAcPerProblem = "{}",
            });
            db.AppMetadata.Add(new AppMetadata { Key = "derivedDataVersion", Value = "1" });
            await db.SaveChangesAsync();

            DerivedDataGuard.Reconcile(db);

            Assert.Equal(1, await db.Submissions.CountAsync());
            var vp = await db.VpResults.FindAsync(Platform.Codeforces, handle, 105486L, ParticipationKind.Virtual);
            Assert.NotNull(vp);
            Assert.Equal(1, vp!.SolvedCount);
            Assert.Null(await db.VpResults.FindAsync(Platform.Codeforces, handle, 105486L, ParticipationKind.Practice));
            Assert.Equal(DerivedDataGuard.CurrentVersion.ToString(),
                (await db.AppMetadata.FindAsync("derivedDataVersion"))!.Value);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Reconcile_CurrentVersion_RepairsStaleAccountSubmissionTotal()
    {
        const string handle = "LuoxuanLove";
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_guard_{Guid.NewGuid():N}.db");
        try
        {
            await using var db = OpenDb(dbPath);
            db.Accounts.Add(new Account
            {
                Platform = Platform.Qoj,
                Handle = handle,
                TotalSubmissions = 0,
            });
            db.Submissions.AddRange(
                new Submission { Platform = Platform.Qoj, Id = 1, Handle = handle, GymId = 100, ProblemIndex = "A", Verdict = "Accepted" },
                new Submission { Platform = Platform.Qoj, Id = 2, Handle = handle, GymId = 100, ProblemIndex = "B", Verdict = "Wrong Answer" });
            db.AppMetadata.Add(new AppMetadata
            {
                Key = "derivedDataVersion",
                Value = DerivedDataGuard.CurrentVersion.ToString(),
            });
            await db.SaveChangesAsync();

            DerivedDataGuard.Reconcile(db);

            var account = await db.Accounts.FindAsync(Platform.Qoj, handle);
            Assert.NotNull(account);
            Assert.Equal(2, account!.TotalSubmissions);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Reconcile_VersionTwoQojRows_RekeysNumericProblemIdsToContestLetters()
    {
        const string handle = "LuoxuanLove";
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_qoj_upgrade_{Guid.NewGuid():N}.db");
        try
        {
            await using var db = OpenDb(dbPath);
            db.QojContestProblems.Add(new QojContestProblem
            {
                ContestId = 3767,
                ProblemId = "18316",
                ProblemIndex = "J",
                Title = "插松枝",
                ProblemUrl = "https://qoj.ac/contest/3767/problem/18316",
            });
            db.Submissions.AddRange(
                new Submission
                {
                    Platform = Platform.Qoj, Id = 1, Handle = handle, GymId = 3767,
                    ProblemIndex = "18316", Verdict = "AC", SubmitTime = 100, ParticipantType = "VIRTUAL",
                    RelativeTime = 100,
                },
                new Submission
                {
                    Platform = Platform.Qoj, Id = 2, Handle = handle, GymId = 3767,
                    ProblemIndex = "18316", Verdict = "WA", SubmitTime = 90, ParticipantType = "VIRTUAL",
                    RelativeTime = 90,
                });
            db.AppMetadata.Add(new AppMetadata { Key = "derivedDataVersion", Value = "2" });
            await db.SaveChangesAsync();

            DerivedDataGuard.Reconcile(db);

            var submissions = await db.Submissions.Where(s => s.Platform == Platform.Qoj).ToListAsync();
            Assert.All(submissions, s =>
            {
                Assert.Equal("18316", s.ProblemId);
                Assert.Equal("J", s.ProblemIndex);
                Assert.Equal("插松枝", s.ProblemName);
                Assert.Equal("https://qoj.ac/contest/3767/problem/18316", s.ProblemUrl);
            });
            var vp = await db.VpResults.FindAsync(Platform.Qoj, handle, 3767L, ParticipationKind.Virtual);
            Assert.NotNull(vp);
            Assert.Equal(1, vp!.SolvedCount);
            Assert.Contains("J", vp.FirstAcPerProblem);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }
}
