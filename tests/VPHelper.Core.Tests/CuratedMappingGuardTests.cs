using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

public sealed class CuratedMappingGuardTests
{
    [Fact]
    public void Reconcile_AddsCuratedMappings_WithoutOverwritingManual()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new VpDbContext(opts);
        db.Database.EnsureCreated();

        db.GymContests.AddRange(
            new GymContest
            {
                Platform = Platform.Codeforces,
                GymId = 105486,
                Title = "Gym 105486",
                ProblemCount = 13,
            },
            new GymContest
            {
                Platform = Platform.Codeforces,
                GymId = 105484,
                Title = "Gym 105484",
                ProblemCount = 13,
            });
        db.ContestMappings.Add(new ContestMapping
        {
            Platform = Platform.Codeforces,
            GymId = 105484,
            OfficialId = "manual-official",
            Confidence = 1.0,
            Confirmed = MappingStatus.ManualConfirmed,
        });
        db.SaveChanges();

        CuratedMappingGuard.Reconcile(db);

        var auto = db.ContestMappings.Single(m => m.GymId == 105486);
        Assert.Equal("icpc2024chengdu", auto.OfficialId);
        Assert.Equal(MappingStatus.AutoConfirmed, auto.Confirmed);

        var manual = db.ContestMappings.Single(m => m.GymId == 105484);
        Assert.Equal("manual-official", manual.OfficialId);
        Assert.Equal(MappingStatus.ManualConfirmed, manual.Confirmed);

        var metadata = db.AppMetadata.Single(m => m.Key == "curatedMappingVersion");
        Assert.Equal(CuratedMappingGuard.CurrentVersion.ToString(), metadata.Value);
    }

    [Fact]
    public void Reconcile_AddsVerifiedCloneSourcesAndQojAliases()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new VpDbContext(opts);
        db.Database.EnsureCreated();

        var sourceMappings = new (long GymId, string OfficialId)[]
        {
            (632808L, "sdcpc14th"),
            (634205L, "icpc2024preliminary-2"),
            (635617L, "ccpc2024preliminary"),
            (637222L, "ccpc2024chongqing"),
            (642844L, "icpc2024hangzhou"),
            (643698L, "icpc2024kunming"),
            (644777L, "icpc2024nanjing"),
            (647317L, "ccpc2024zhengzhou"),
            (648287L, "icpc2024shenyang"),
            (668092L, "jlcpc17th"),
            (677629L, "icpc2025invitational-nanchang"),
            (679438L, "hecpc9th"),
            (680980L, "icpc2025invitational-wuhan"),
            (682064L, "shcpc2025"),
        };
        db.GymContests.AddRange(sourceMappings.Select(x => new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = x.GymId,
            Title = $"Gym {x.GymId}",
        }));
        db.GymContests.AddRange(
            new GymContest { Platform = Platform.Qoj, GymId = 1464, Title = "QOJ 1464" },
            new GymContest { Platform = Platform.Qoj, GymId = 1668, Title = "QOJ 1668" },
            new GymContest { Platform = Platform.Qoj, GymId = 2645, Title = "QOJ 2645" },
            new GymContest { Platform = Platform.Qoj, GymId = 3729, Title = "QOJ 3729" },
            new GymContest { Platform = Platform.Qoj, GymId = 3767, Title = "QOJ 3767" },
            new GymContest { Platform = Platform.Qoj, GymId = 1485, Title = "QOJ 1485" },
            new GymContest { Platform = Platform.Qoj, GymId = 1794, Title = "QOJ 1794" },
            new GymContest { Platform = Platform.Qoj, GymId = 2513, Title = "QOJ 2513" },
            new GymContest { Platform = Platform.Qoj, GymId = 2116, Title = "QOJ 2116" },
            new GymContest { Platform = Platform.Qoj, GymId = 2180, Title = "QOJ 2180" });
        db.SaveChanges();

        CuratedMappingGuard.Reconcile(db);

        foreach (var (gymId, officialId) in sourceMappings)
        {
            var mapping = db.ContestMappings.Single(m =>
                m.Platform == Platform.Codeforces && m.GymId == gymId);
            Assert.Equal(officialId, mapping.OfficialId);
            Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
        }

        Assert.Equal("bjcpc2024", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 1668).OfficialId);
        Assert.Equal("icpc2025shanghai", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 2645).OfficialId);
        Assert.Equal("icpc2026invitational-xi_an", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 3729).OfficialId);
        Assert.Equal("sdcpc16th", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 3767).OfficialId);
        Assert.Equal("icpc2023preliminary-1", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 1485).OfficialId);
        Assert.Equal("icpc2024preliminary-1", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 1794).OfficialId);
        Assert.Equal("icpc2025preliminary-1", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 2513).OfficialId);
        Assert.Equal("icpc2015changchun", db.ContestMappings.Single(m =>
            m.Platform == Platform.Qoj && m.GymId == 2116).OfficialId);
        Assert.DoesNotContain(db.ContestMappings, m =>
            m.Platform == Platform.Qoj && m.GymId == 1464);
        Assert.DoesNotContain(db.ContestMappings, m =>
            m.Platform == Platform.Qoj && m.GymId == 2180);
    }

    [Fact]
    public void Reconcile_RemovesRetiredAutoMappings_WithoutTouchingManual()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new VpDbContext(opts);
        db.Database.EnsureCreated();

        db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = 106139,
            Title = "The 21st Hunan Provincial Collegiate Programming Contest",
            ProblemCount = 7,
        });
        db.ContestMappings.AddRange(
            new ContestMapping
            {
                Platform = Platform.Codeforces,
                GymId = 106139,
                OfficialId = "hncpc20th",
                Confidence = 0.99,
                Confirmed = MappingStatus.AutoConfirmed,
            },
            new ContestMapping
            {
                Platform = Platform.Codeforces,
                GymId = 106140,
                OfficialId = "hncpc20th",
                Confidence = 1.0,
                Confirmed = MappingStatus.ManualConfirmed,
            });
        db.SaveChanges();

        CuratedMappingGuard.Reconcile(db);

        Assert.DoesNotContain(db.ContestMappings, m => m.GymId == 106139 && m.OfficialId == "hncpc20th");
        Assert.Contains(db.ContestMappings, m => m.GymId == 106140 && m.OfficialId == "hncpc20th");
    }

    [Fact]
    public void Reconcile_RemovesAmbiguousQojAutoMappings_WithoutTouchingManual()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new VpDbContext(opts);
        db.Database.EnsureCreated();

        db.GymContests.AddRange(
            new GymContest { Platform = Platform.Qoj, GymId = 3756, Title = "QOJ 3756" },
            new GymContest { Platform = Platform.Qoj, GymId = 3757, Title = "QOJ 3757" });
        db.ContestMappings.AddRange(
            new ContestMapping
            {
                Platform = Platform.Qoj,
                GymId = 3756,
                OfficialId = "gdcpc23rd",
                Confidence = 0.99,
                Confirmed = MappingStatus.AutoConfirmed,
            },
            new ContestMapping
            {
                Platform = Platform.Qoj,
                GymId = 3757,
                OfficialId = "manual-official",
                Confidence = 1.0,
                Confirmed = MappingStatus.ManualConfirmed,
            });
        db.SaveChanges();

        CuratedMappingGuard.Reconcile(db);

        Assert.DoesNotContain(db.ContestMappings, m =>
            m.Platform == Platform.Qoj && m.GymId == 3756 && m.OfficialId == "gdcpc23rd");
        Assert.Contains(db.ContestMappings, m =>
            m.Platform == Platform.Qoj && m.GymId == 3757 && m.OfficialId == "manual-official");
    }
}
