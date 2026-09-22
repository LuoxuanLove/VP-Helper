using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Models;

namespace VPHelper.Core.Data;

/// <summary>EF Core context over the local SQLite store (DESIGN §5).</summary>
public class VpDbContext : DbContext
{
    public VpDbContext(DbContextOptions<VpDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<GymContest> GymContests => Set<GymContest>();
    public DbSet<QojContestProblem> QojContestProblems => Set<QojContestProblem>();
    public DbSet<VpResult> VpResults => Set<VpResult>();
    public DbSet<GymVpSelection> GymVpSelections => Set<GymVpSelection>();
    public DbSet<OfficialContest> OfficialContests => Set<OfficialContest>();
    public DbSet<OfficialProblem> OfficialProblems => Set<OfficialProblem>();
    public DbSet<OfficialStanding> OfficialStandings => Set<OfficialStanding>();
    public DbSet<ContestMapping> ContestMappings => Set<ContestMapping>();
    public DbSet<AppMetadata> AppMetadata => Set<AppMetadata>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // The same handle names distinct accounts on different judges (and the same person reuses
        // one handle across CF and QOJ here), so Platform is part of every account/contest key.
        b.Entity<Account>().HasKey(a => new { a.Platform, a.Handle });

        // Submission ids are unique only within a platform, so the PK is (Platform, Id).
        b.Entity<Submission>().HasKey(s => new { s.Platform, s.Id });
        b.Entity<Submission>().HasIndex(s => new { s.Platform, s.Handle, s.GymId });
        b.Entity<Submission>().HasIndex(s => s.SubmitTime);

        b.Entity<GymContest>().HasKey(g => new { g.Platform, g.GymId });

        b.Entity<QojContestProblem>().HasKey(p => new { p.ContestId, p.ProblemId });
        b.Entity<QojContestProblem>().HasIndex(p => new { p.ContestId, p.ProblemIndex });

        // VP and practice aggregates for the same (platform, handle, gym) are distinct rows: Kind is
        // part of the key.
        b.Entity<VpResult>().HasKey(v => new { v.Platform, v.Handle, v.GymId, v.Kind });

        b.Entity<GymVpSelection>().HasKey(s => new { s.Platform, s.GymId });

        b.Entity<OfficialContest>().HasKey(o => o.Id);
        b.Entity<OfficialContest>().HasIndex(o => o.CategoryPath);

        b.Entity<OfficialProblem>().HasKey(o => new { o.OfficialId, o.Alias });
        b.Entity<OfficialProblem>().HasIndex(o => o.OfficialId);

        b.Entity<OfficialStanding>().HasKey(o => o.Id);
        b.Entity<OfficialStanding>().Property(o => o.Id).ValueGeneratedOnAdd();
        b.Entity<OfficialStanding>().HasIndex(o => o.OfficialId);

        b.Entity<ContestMapping>().HasKey(m => new { m.Platform, m.GymId, m.OfficialId });
        b.Entity<ContestMapping>().HasIndex(m => new { m.Platform, m.GymId });

        b.Entity<AppMetadata>().HasKey(m => m.Key);
    }
}
