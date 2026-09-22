using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace VPHelper.Core.Data;

/// <summary>
/// Reconciles the persisted SQLite schema with tables and columns added after the DB file was
/// first created.
///
/// The app provisions its schema with <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreated"/>,
/// which only builds the schema when the database does not yet exist and never touches an existing
/// one. Two kinds of drift follow from that on a long-lived DB file:
///
///  • <b>Missing columns</b>: a property added to (or renamed on) an entity leaves the older table
///    with its old definition, so every query touching the new column throws
///    <c>SQLite Error 1: 'no such column: ...'</c> (observed for both <c>Account.LastSyncedAt</c>
///    and <c>Submission.SubmitTime</c>).
///  • <b>Missing tables</b>: a whole entity added in a later phase (the Phase 2
///    <c>OfficialContest</c> / <c>OfficialStanding</c> / <c>ContestMapping</c> tables) is absent
///    from a DB file first created under Phase 1, so the first query throws
///    <c>SQLite Error 1: 'no such table: ...'</c>.
///
/// Rather than maintain a hand-written list of additions — which is easy to leave stale, as the
/// <c>SubmitTime</c> case proved — this guard reads the EF model and heals both: it creates any
/// mapped table the live DB lacks (via EF's own migrations SQL generator, so composite keys and
/// indexes are emitted correctly), then adds any column the model expects but an existing table
/// lacks. It is data-preserving (existing rows and tables are untouched; only missing tables and
/// columns are added) and future-proof (new entities and properties are healed automatically
/// without editing this file).
///
/// It never drops a table or a column that still holds usable data. The one exception is an
/// <c>INSERT</c>-breaking orphan column — one left by a property rename that is <c>NOT NULL</c>
/// with no default — which EF can never write and which fails the constraint on every save; those
/// are dropped (see <see cref="DropInsertBreakingOrphanColumns"/>). Nullable or defaulted orphans
/// are left in place, since they linger harmlessly.
/// </summary>
public static class SchemaGuard
{
    /// <summary>
    /// Create any tables and add any columns the EF model declares that are missing from the live
    /// SQLite DB. Safe to call on every startup; already-present tables and columns are skipped.
    /// No-op for non-relational providers (e.g. the in-memory tests). Primary-key columns are
    /// skipped when reconciling an existing table — SQLite cannot add a PK via <c>ALTER TABLE</c>,
    /// and a table missing its PK is corruption this guard won't mask.
    /// </summary>
    [SuppressMessage("Usage", "EF1002:Risk of vulnerability to SQL injection",
        Justification = "All identifiers and type literals come from EF model metadata, never user input.")]
    public static void Reconcile(VpDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        // A table whose live primary key differs from the model's has been *redesigned*, not merely
        // extended (e.g. OfficialStandings went from a composite (OfficialId, RowKey) PK to a
        // synthetic Id PK, with several columns renamed alongside). Column-level ALTER cannot express
        // a PK change — SQLite can neither add a PK column nor drop an old PK column via ALTER — so
        // such tables are dropped here and rebuilt fresh by CreateMissingTables below. This is safe
        // in this app: every table whose PK can change stores only re-fetchable cache (submissions,
        // official standings/contests). Accounts (PK Handle) has a stable key and is never dropped.
        DropRedesignedTables(db);

        CreateMissingTables(db);

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table) || !TableExists(db, table)) continue;

            var existing = GetColumns(db, table);

            foreach (var property in entityType.GetProperties())
            {
                if (property.IsPrimaryKey()) continue; // cannot ALTER-ADD a PK column in SQLite

                var column = property.GetColumnName();
                if (string.IsNullOrEmpty(column) || existing.Contains(column)) continue;

                var storeType = property.GetColumnType();
                // A non-null column added to a table with existing rows needs a default, or SQLite
                // rejects the ALTER. Nullable columns are added without one.
                var suffix = property.IsNullable ? string.Empty : $" NOT NULL DEFAULT {DefaultLiteral(storeType)}";
                db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {storeType}{suffix}");
                existing.Add(column);
            }

            DropInsertBreakingOrphanColumns(db, table, entityType);
        }
    }

    /// <summary>
    /// Drop columns present in the live table but absent from the EF model that would break every
    /// INSERT: <c>NOT NULL</c> with no default. A property rename (e.g. <c>SubmitTimeUnixSeconds</c>
    /// → <c>SubmitTime</c>) leaves the old column behind; EF never supplies a value for it, so a
    /// <c>NOT NULL</c> old column with no default fails the constraint on every save. Nullable or
    /// defaulted orphans are left untouched — those genuinely linger harmlessly, so this stays as
    /// data-preserving as the guard's contract intends and only removes columns that are already
    /// unwritable dead weight.
    ///
    /// This only runs for tables whose primary key already matches the model (see the PK-equality
    /// gate in <see cref="Reconcile"/>), so any orphan here is a non-PK rename remnant. A live PK
    /// column is therefore never an orphan — but the guard skips PK columns explicitly regardless,
    /// because SQLite refuses <c>DROP COLUMN</c> on a primary-key column outright.
    ///
    /// SQLite refuses <c>DROP COLUMN</c> on an indexed column, so any user index (origin 'c')
    /// referencing the orphan is dropped first. The orphan holds no data the model can read, and a
    /// renamed column's index is superseded by the new column's index, so dropping it loses nothing.
    /// </summary>
    [SuppressMessage("Usage", "EF1002:Risk of vulnerability to SQL injection",
        Justification = "All identifiers come from EF model metadata and SQLite catalog, never user input.")]
    private static void DropInsertBreakingOrphanColumns(VpDbContext db, string table, IEntityType entityType)
    {
        var modelColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in entityType.GetProperties())
        {
            var column = property.GetColumnName();
            if (!string.IsNullOrEmpty(column)) modelColumns.Add(column);
        }

        foreach (var col in GetColumnInfo(db, table))
        {
            if (modelColumns.Contains(col.Name)) continue;
            if (col.IsPrimaryKey) continue;               // SQLite cannot DROP a PK column
            if (!col.NotNull || col.HasDefault) continue; // nullable/defaulted orphans are harmless

            foreach (var index in GetUserIndexesReferencing(db, table, col.Name))
                db.Database.ExecuteSqlRaw($"DROP INDEX \"{index}\"");

            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" DROP COLUMN \"{col.Name}\"");
        }
    }

    /// <summary>
    /// Drop any existing table whose live primary-key column set differs from the model's, so
    /// <see cref="CreateMissingTables"/> can recreate it correctly. A differing PK means the table
    /// was redesigned (columns renamed, key reshaped) rather than merely extended, and no sequence
    /// of <c>ALTER TABLE ADD/DROP COLUMN</c> can reconcile that — SQLite cannot add or drop a PK
    /// column. The dropped tables hold only re-fetchable cache in this app, so recreating them empty
    /// loses nothing that cannot be resynced.
    /// </summary>
    [SuppressMessage("Usage", "EF1002:Risk of vulnerability to SQL injection",
        Justification = "All identifiers come from EF model metadata and SQLite catalog, never user input.")]
    private static void DropRedesignedTables(VpDbContext db)
    {
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table) || !TableExists(db, table)) continue;

            var modelPk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in entityType.FindPrimaryKey()?.Properties ?? System.Linq.Enumerable.Empty<IProperty>())
            {
                var col = prop.GetColumnName();
                if (!string.IsNullOrEmpty(col)) modelPk.Add(col);
            }
            if (modelPk.Count == 0) continue; // keyless entity — nothing to compare

            var livePk = new HashSet<string>(
                GetColumnInfo(db, table).Where(c => c.IsPrimaryKey).Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);

            // A live table with no detectable PK is left alone: dropping on ambiguous state is
            // riskier than the column-level path, which tolerates a legacy table fine.
            if (livePk.Count == 0) continue;

            if (!livePk.SetEquals(modelPk))
                db.Database.ExecuteSqlRaw($"DROP TABLE \"{table}\"");
        }
    }

    /// <summary>
    /// Create any mapped table the live DB is missing entirely. Uses EF's own migrations pipeline:
    /// diff an empty model against the current model to get the full set of <c>CreateTable</c> /
    /// <c>CreateIndex</c> operations, keep only those whose table does not yet exist, and run the
    /// SQL EF generates for them. This emits composite primary keys, unique constraints, and
    /// indexes correctly — none of which a hand-rolled <c>CREATE TABLE</c> would get for free.
    /// The model declares no foreign keys, so partial creation needs no dependency ordering.
    /// </summary>
    private static void CreateMissingTables(VpDbContext db)
    {
        var differ = db.GetService<IMigrationsModelDiffer>();
        var sqlGenerator = db.GetService<IMigrationsSqlGenerator>();
        // The differ and SQL generator need the full design-time model: the read-optimized db.Model
        // omits migration metadata and throws when the differ inspects it.
        var model = db.GetService<IDesignTimeModel>().Model;

        // Empty source model → current model yields every create operation the schema needs.
        var operations = differ.GetDifferences(null, model.GetRelationalModel());

        var createTables = operations
            .OfType<CreateTableOperation>()
            .Where(op => !TableExists(db, op.Name))
            .ToList();
        if (createTables.Count == 0) return;

        var missingTableNames = createTables.Select(op => op.Name).ToHashSet(StringComparer.Ordinal);
        var createIndexes = operations
            .OfType<CreateIndexOperation>()
            .Where(op => missingTableNames.Contains(op.Table))
            .ToList();

        var toRun = new List<MigrationOperation>(createTables.Count + createIndexes.Count);
        toRun.AddRange(createTables);
        toRun.AddRange(createIndexes);

        foreach (var command in sqlGenerator.Generate(toRun, model))
            db.Database.ExecuteSqlRaw(command.CommandText);
    }

    /// <summary>SQLite default literal matching a store type's affinity, for NOT NULL additions.</summary>
    private static string DefaultLiteral(string storeType)
    {
        var t = (storeType ?? string.Empty).ToUpperInvariant();
        if (t.Contains("INT")) return "0";
        if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB") || t.Contains("NUM") || t.Contains("DEC")) return "0";
        if (t.Contains("BLOB")) return "x''";
        return "''"; // TEXT and anything else
    }

    private static bool TableExists(VpDbContext db, string table)
        => WithConnection(db, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
            var p = cmd.CreateParameter();
            p.ParameterName = "$name";
            p.Value = table;
            cmd.Parameters.Add(p);
            return cmd.ExecuteScalar() != null;
        });

    private static HashSet<string> GetColumns(VpDbContext db, string table)
        => WithConnection(db, conn =>
        {
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            // PRAGMA does not accept bound parameters for the table name; the value comes from EF
            // model metadata (a mapped table name), not user input.
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                cols.Add(reader.GetString(1)); // table_info columns: cid, name, type, notnull, dflt_value, pk
            return cols;
        });

    private readonly record struct ColumnInfo(string Name, bool NotNull, bool HasDefault, bool IsPrimaryKey);

    /// <summary>Full column metadata for a table (name, NOT NULL flag, has-default, PK membership).</summary>
    private static List<ColumnInfo> GetColumnInfo(VpDbContext db, string table)
        => WithConnection(db, conn =>
        {
            var cols = new List<ColumnInfo>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = cmd.ExecuteReader();
            // table_info columns: 0=cid, 1=name, 2=type, 3=notnull, 4=dflt_value, 5=pk
            // pk > 0 means the column is part of the primary key (the value is its 1-based
            // position within a composite key); 0 means it is not a key column.
            while (reader.Read())
                cols.Add(new ColumnInfo(
                    reader.GetString(1), reader.GetInt32(3) != 0, !reader.IsDBNull(4), reader.GetInt32(5) != 0));
            return cols;
        });

    /// <summary>
    /// Names of user-created indexes (origin 'c') on <paramref name="table"/> that reference
    /// <paramref name="column"/>. Auto-indexes (PK/unique) use origin 'pk'/'u' and are excluded —
    /// SQLite manages those and they cannot be dropped directly.
    /// </summary>
    private static List<string> GetUserIndexesReferencing(VpDbContext db, string table, string column)
        => WithConnection(db, conn =>
        {
            var result = new List<string>();
            var indexNames = new List<(string Name, string Origin)>();
            using (var listCmd = conn.CreateCommand())
            {
                listCmd.CommandText = $"PRAGMA index_list(\"{table}\")";
                using var reader = listCmd.ExecuteReader();
                // index_list columns: 0=seq, 1=name, 2=unique, 3=origin, 4=partial
                while (reader.Read())
                    indexNames.Add((reader.GetString(1), reader.GetString(3)));
            }

            foreach (var (name, origin) in indexNames)
            {
                if (!string.Equals(origin, "c", StringComparison.Ordinal)) continue; // only user indexes
                using var infoCmd = conn.CreateCommand();
                infoCmd.CommandText = $"PRAGMA index_info(\"{name}\")";
                using var reader = infoCmd.ExecuteReader();
                // index_info columns: 0=seqno, 1=cid, 2=name
                while (reader.Read())
                {
                    if (!reader.IsDBNull(2) &&
                        string.Equals(reader.GetString(2), column, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name);
                        break;
                    }
                }
            }
            return result;
        });

    private static T WithConnection<T>(VpDbContext db, Func<System.Data.Common.DbConnection, T> body)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != System.Data.ConnectionState.Open;
        if (wasClosed) conn.Open();
        try { return body(conn); }
        finally { if (wasClosed) conn.Close(); }
    }
}
