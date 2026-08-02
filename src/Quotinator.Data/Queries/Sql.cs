namespace Quotinator.Data.Queries;

/// <summary>Generic infrastructure SQL — schema/version bookkeeping, join helpers, and the System_-prefixed tables Quotinator.Data itself owns.</summary>
/// <remarks>
/// Quotinator-domain SQL (Quotes, Sources, Characters, Conversations, etc.) lives in
/// <c>Quotinator.Core.Queries.Sql</c> instead — Quotinator.Data must stay domain-agnostic
/// (ADR 004). Everything in this file is reusable infrastructure with no dependency on any
/// Quotinator-specific table shape.
/// <para>
/// Static constants cover fixed queries. Static factory methods cover queries where dynamic
/// clauses (WHERE, field-filter) are appended at call time — every factory method is tested
/// directly in a guard test with the full set of clause variants.
/// </para>
/// PRAGMA statements are excluded; they are defined inline and carry no aggregate-vulnerability risk.
/// DDL that runs outside the versioned migration list (e.g. the SchemaVersion bootstrap table) is
/// included so the inventory is complete. Migration constants remain private inside DatabaseInitializer
/// so their text is frozen at migration time.
/// </remarks>
internal static class Sql
{
    /// <summary>
    /// System_SchemaVersion (Quotinator.Data's own migrations) and System_ConsumerSchemaVersion
    /// (the consuming project's migrations) — two independent version-tracking tables, each with
    /// its own stable, locally-numbered history. Kept separate so "version N" always means the
    /// same specific migration for whichever side owns it, unaffected by the other side's
    /// migration count changing over time.
    /// </summary>
    internal static class Schema
    {
        // Bootstrap-only, one-time legacy detection — not part of the numbered migration list.
        // Runs before the current version is even known, since SchemaVersion itself is what the
        // numbered migration system depends on to know what to apply. Idempotent by construction:
        // once the split below has happened, sqlite_master no longer contains a table literally
        // named SchemaVersion, so this check is a no-op on every subsequent startup.
        internal const string LegacySchemaVersionExists =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";

        // #155: the legacy, pre-#143 SchemaVersion table held one row per version, versions 1-4
        // (InitialSchema, ReseedGenres, ImportBatches, CreateAuditEntriesTable — v1.7.2's real,
        // shipped migration list). Versions 1-3 are genuinely Consumer-owned under the current split;
        // version 4 is Data-owned migration 1 (renumbered, since Data's own list starts fresh at 1).
        // A bare table rename (this project's original #143 approach) copied the raw stored version
        // number straight into System_SchemaVersion, which every later Data migration then read as
        // "already applied" purely by numeric coincidence with Data's own 1-4 — silently skipping
        // migrations 2-4 on any real v1.7.2 upgrade. Splitting explicitly, by hardcoded version
        // number, avoids this: no other historical shape of the legacy table has ever existed, since
        // nothing has shipped between v1.7.2 and the #143 split that introduced these two tables.
        internal const string SplitLegacySchemaVersionIntoConsumer =
            "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) " +
            "SELECT Version, AppliedAt FROM SchemaVersion WHERE Version IN (1, 2, 3);";
        internal const string SplitLegacySchemaVersionIntoData =
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) " +
            "SELECT 1, AppliedAt FROM SchemaVersion WHERE Version = 4;";
        internal const string DropLegacySchemaVersionTable =
            "DROP TABLE SchemaVersion;";

        // Detects a completely empty database — zero tables of any kind, including the version
        // tables themselves. Used to decide whether a fresh database can take the one-step baseline
        // path instead of replaying migration history. Deliberately not GetUserTables (below),
        // which excludes System_-prefixed tables by design — a database containing only an empty
        // version table is not "empty" for baseline purposes.
        internal const string AnyTableExists =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        internal const string CreateDataVersionTable = "CREATE TABLE IF NOT EXISTS System_SchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);";
        internal const string GetDataCurrentVersion  = "SELECT COALESCE(MAX(Version), 0) FROM System_SchemaVersion;";
        internal const string InsertDataVersion      = "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (@v, @at);";

        // No DeleteAllDataVersions/GetAllDataVersions — Quotinator.Data's own migration history is
        // never wiped or replayed by a Reset (see DropAndRebuildAsync), so nothing ever needs to
        // snapshot or clear this table's rows.

        internal const string CreateConsumerVersionTable = "CREATE TABLE IF NOT EXISTS System_ConsumerSchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);";
        internal const string GetConsumerCurrentVersion  = "SELECT COALESCE(MAX(Version), 0) FROM System_ConsumerSchemaVersion;";
        internal const string InsertConsumerVersion      = "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (@v, @at);";
        internal const string DeleteAllConsumerVersions  = "DELETE FROM System_ConsumerSchemaVersion;";
        internal const string GetAllConsumerVersions     = "SELECT Version, AppliedAt FROM System_ConsumerSchemaVersion;";

        // Returns all user-created table names, excluding SQLite internals and any table
        // designated as protected system infrastructure. Used by ResetAsync to discover tables
        // dynamically so that new tables added in future migrations are dropped without requiring
        // a manual update here. FK checks must be off before dropping the results
        // (PRAGMA foreign_keys = OFF).
        // A "system table" is any table whose name starts with a literal Import_/Audit_/System_
        // prefix (ADR 015) — this query never needs to know specific names, so a consuming project
        // can add its own protected tables (e.g. a DB-backed enum-like lookup) with zero changes
        // here. Import_Batch belongs in this protected set too: ADR 014 already distinguishes
        // "provenance" (where a row's content came from — Import_Batch) from "domain content" as two
        // separate concepts, and a Reset dropping it — while never replaying Quotinator.Data's own
        // migrations that create it (see DropAndRebuildAsync's own remarks) — would leave it
        // permanently missing, not recreated on the next InitialiseAsync call. Its own dangling
        // references after a Reset are tolerated exactly like the four ADR-014 audit-trail tables'.
        // Each underscore must be escaped: SQL LIKE treats '_' as a single-character wildcard, so an
        // unescaped 'System_%' would also match an unrelated table like SystemInventory. The ESCAPE
        // clause makes '\_' match a literal underscore only, so SystemInventory (no underscore) is
        // correctly NOT treated as protected.
        internal const string GetUserTables =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
            "AND name NOT LIKE 'Import\\_%' ESCAPE '\\' " +
            "AND name NOT LIKE 'Audit\\_%' ESCAPE '\\' " +
            "AND name NOT LIKE 'System\\_%' ESCAPE '\\';";
    }

    /// <summary>JOIN fragment helpers — assembles INNER JOIN and LEFT JOIN clauses with bracket-quoted identifiers.</summary>
    /// <remarks>
    /// Parameters must always be compile-time string literals — never user input, never runtime strings.
    /// Bracket quoting is a defence-in-depth measure, not a licence to pass dynamic values.
    /// </remarks>
    internal static class Joins
    {
        /// <summary>Returns an INNER JOIN clause with all identifiers bracket-quoted.</summary>
        internal static string Inner(string rightTable, string rightAlias, string leftAlias, string leftKey, string rightKey)
            => $"INNER JOIN [{rightTable}] [{rightAlias}] ON [{leftAlias}].[{leftKey}] = [{rightAlias}].[{rightKey}]";

        /// <summary>Returns a LEFT JOIN clause with all identifiers bracket-quoted.</summary>
        internal static string Left(string rightTable, string rightAlias, string leftAlias, string leftKey, string rightKey)
            => $"LEFT JOIN [{rightTable}] [{rightAlias}] ON [{leftAlias}].[{leftKey}] = [{rightAlias}].[{rightKey}]";
    }

    /// <summary>Full query factory methods for join queries — assembled from <see cref="Joins"/> fragments.</summary>
    internal static class Queries
    {
        /// <summary>Canonical Widget-with-Owner join query — example of the <c>IJoinStrategy&lt;TResult&gt;</c> pattern.</summary>
        internal static string WidgetWithOwner() => $"""
            SELECT {IdClauses.SelectColumn("[w].[Id]", "WidgetId")}, [w].[Label],
                   [o].[Name] AS OwnerName
            FROM   [Widgets] [w]
            {Joins.Inner("Owners", "o", "w", "OwnerId", "Id")}
            WHERE  [w].[IsDeleted] = 0
            """;
    }

    /// <summary>
    /// Import_Batch table (formerly ImportBatches — #253/ADR 015). Never interacts with a
    /// consumer-defined entity — pure import/seed bookkeeping (which batch, when, by what policy,
    /// how many records, current lifecycle status), the same category as
    /// <c>SeedBatch</c>/<c>ManifestPolicy</c> (see ADR 004's consumer-entity-interaction test, issue
    /// #158).
    /// </summary>
    internal static class ImportBatches
    {
        // #212: built by reflecting over ImportBatch's own properties (Repositories.ReflectedColumnMetadata),
        // not hand-typed — never needs updating when a property is added, removed, or renamed on
        // ImportBatch, the same flexibility SELECT * provided, now combined with an explicit,
        // guard-visible column list. Every *Id-suffixed column found this way is wrapped via
        // IdClauses.SelectColumn. Not a const because this involves reflection + method calls, evaluated
        // once per process (ReflectedColumnMetadata caches per-Type internally).
        private static readonly string SelectColumns =
            Repositories.RepositorySql.BuildSelectColumns(Repositories.ReflectedColumnMetadata.For(typeof(Entities.ImportBatchEntity)));

        // ImportedAt has only whole-second precision, so two batches created within the same second
        // (routine in tests, and possible in fast-successive real API calls) tie under ORDER BY
        // ImportedAt DESC alone — SQLite does not guarantee a stable order for ties. ROWID DESC breaks
        // the tie deterministically in insertion order (a consumer's own strict batch-undo stack may
        // rely on this ordering being exact, not just "usually right" — found via a genuinely red test).
        internal static readonly string SelectAll =
            $"SELECT {SelectColumns} FROM Import_Batch WHERE IsDeleted = 0 ORDER BY ImportedAt DESC, ROWID DESC;";

        internal static readonly string SelectByType =
            $"SELECT {SelectColumns} FROM Import_Batch WHERE IsDeleted = 0 AND Type = @type ORDER BY ImportedAt DESC, ROWID DESC;";

        // Case-insensitive (#210) via IdClauses — see docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md.
        internal static readonly string UpdateRecordCount =
            $"UPDATE Import_Batch SET RecordCount = @count, DateModified = @now WHERE {IdClauses.Equals("Id", "id")};";

        internal const string DeleteAll = "DELETE FROM Import_Batch;";

        // COUNT base — shared by CountPaged factory method below.
        private const string CountPagedBase = "SELECT COUNT(*) FROM Import_Batch WHERE IsDeleted = 0";

        /// <summary>Paginated batch listing (#251's own GET endpoint), newest first, with optional filters.</summary>
        internal static string SelectPaged(bool filterType, bool filterStatus)
            => $"SELECT {SelectColumns} FROM Import_Batch WHERE IsDeleted = 0" +
               BuildWhere(filterType, filterStatus) +
               " ORDER BY ImportedAt DESC, ROWID DESC LIMIT @pageSize OFFSET @offset;";

        /// <summary>Total matching count for the paginated batch listing.</summary>
        internal static string CountPaged(bool filterType, bool filterStatus)
            => CountPagedBase + BuildWhere(filterType, filterStatus) + ";";

        // Type/Status comparisons are case-insensitive (project-wide convention — see CLAUDE.md's
        // "GUID/enum/id/Name/Title comparisons are case-insensitive by default").
        private static string BuildWhere(bool filterType, bool filterStatus)
        {
            var parts = new List<string>(2);
            if (filterType)   parts.Add(TextClauses.Equals("Type", "type"));
            if (filterStatus) parts.Add(TextClauses.Equals("Status", "status"));
            return parts.Count > 0 ? " AND " + string.Join(" AND ", parts) : string.Empty;
        }
    }

    /// <summary>Audit_Entry table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.AuditEntryWriter"/>.</summary>
    internal static class SystemAudit
    {
        /// <summary>Removes all audit entries.</summary>
        internal const string DeleteAll     = "DELETE FROM Audit_Entry;";

        /// <summary>Removes audit entries for a specific table name. Case-insensitive (#216) — a
        /// lowercase <c>?table=</c> (the natural spelling given this endpoint's own JSON casing
        /// conventions) previously matched nothing and silently deleted zero rows, looking like
        /// success while doing nothing.</summary>
        internal static readonly string DeleteByTable = $"DELETE FROM Audit_Entry WHERE {TextClauses.Equals("TableName", "table")};";

        // COUNT base — shared by CountPaged factory method below.
        private const string CountPagedBase = "SELECT COUNT(*) FROM Audit_Entry";

        /// <summary>
        /// Paginated audit entry listing, newest first, with optional filters. Every id column
        /// (<c>Id</c>, <c>RecordId</c>) is read through <c>LOWER(...)</c> — PK and FK alike, regardless
        /// of what C# type ultimately receives it — so a row written under any prior casing convention
        /// still renders consistently, without needing a data migration to re-case already-stored rows.
        /// </summary>
        internal static string SelectPaged(bool filterTable, bool filterRecordId)
            => $"SELECT {IdClauses.SelectColumn("Id")}, TableName, {IdClauses.SelectColumn("RecordId")}, Operation, Agent, PerformedAt FROM Audit_Entry" +
               BuildWhere(filterTable, filterRecordId) +
               " ORDER BY PerformedAt DESC LIMIT @pageSize OFFSET @offset;";

        /// <summary>Total matching count for the audit list endpoint.</summary>
        internal static string CountPaged(bool filterTable, bool filterRecordId)
            => CountPagedBase + BuildWhere(filterTable, filterRecordId) + ";";

        // RecordId comparison is case-insensitive (#210): a caller's ?recordId= query-string value is
        // never canonicalized before reaching here — a mismatched-case value (any casing a client
        // happens to send) previously matched nothing. See
        // docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md.
        // TableName comparison is case-insensitive too (#216) — same reasoning as DeleteByTable above.
        private static string BuildWhere(bool filterTable, bool filterRecordId)
        {
            var parts = new List<string>(2);
            if (filterTable)    parts.Add(TextClauses.Equals("TableName", "table"));
            if (filterRecordId) parts.Add(IdClauses.Equals("RecordId", "recordId"));
            return parts.Count > 0 ? " WHERE " + string.Join(" AND ", parts) : string.Empty;
        }
    }

    /// <summary>Import_Action table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.ImportActionWriter"/>.</summary>
    internal static class SystemImportActions
    {
        /// <summary>Removes all import-action rows.</summary>
        internal const string DeleteAll = "DELETE FROM Import_Action;";

        // COUNT base — shared by CountPaged factory method below.
        private const string CountPagedBase = "SELECT COUNT(*) FROM Import_Action";

        // Column list shared by every SELECT below. Every id column (Id/BatchId/EntityId/
        // ExistingBatchId) is read through LOWER(...) — PK and FK alike, regardless of what C# type
        // ultimately receives it — so a row written before this project settled on its current casing
        // convention still renders consistently, without needing a data migration to re-case
        // already-stored rows. Not a const because IdClauses.SelectColumn is a method call.
        private static readonly string SelectColumns =
            $"{IdClauses.SelectColumn("Id")}, {IdClauses.SelectColumn("BatchId")}, ActionType, EntityType, {IdClauses.SelectColumn("EntityId")}, {IdClauses.SelectColumn("ExistingBatchId")}, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, OriginalDecision, MarkCompletenessAs, DetectedAt, AppliedAt, DiscardedAt";

        /// <summary>Paginated action listing, newest first, with optional filters.</summary>
        internal static string SelectPaged(bool filterBatchId, bool filterStatus, bool filterEntityType = false)
            => $"SELECT {SelectColumns} FROM Import_Action" +
               BuildWhere(filterBatchId, filterStatus, filterEntityType) +
               " ORDER BY DetectedAt DESC LIMIT @pageSize OFFSET @offset;";

        /// <summary>Total matching count for the action list endpoint.</summary>
        internal static string CountPaged(bool filterBatchId, bool filterStatus, bool filterEntityType = false)
            => CountPagedBase + BuildWhere(filterBatchId, filterStatus, filterEntityType) + ";";

        /// <summary>
        /// Single-action lookup by Id (#154's decide/undo/apply/discard flows). Case-insensitive
        /// (#210) — found live during the IdClauses refactor: this was declared as a property, not a
        /// field, which meant it silently evaded every guard test's reflection-based enumeration
        /// (both scanned only <c>GetFields</c>) despite being a real, reachable comparison via
        /// <see cref="Repositories.ImportActionReader"/>'s own <c>GetByIdAsync</c>. Fixed here, and the
        /// guard tests' reflection was widened to scan properties too so this class of gap can't
        /// recur — see <c>EnumerateSqlConstants</c> in both <c>SqlQueryGuardTests</c> files.
        /// </summary>
        internal static string SelectById => $"SELECT {SelectColumns} FROM Import_Action WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>
        /// Every action sharing a BatchId, any status — #154's apply-batch readiness check needs the
        /// complete set, not a page. Case-insensitive as a defence-in-depth measure even though stored
        /// <c>BatchId</c> values and .NET's default <c>Guid</c> serialization are both lowercase today
        /// (ADR 012, <see cref="Helpers.GuidExtensions.ToCanonicalId"/>) — a caller round-tripping the
        /// batch id straight from a response should still match regardless of casing.
        /// <c>ORDER BY rowid</c> makes the result deterministic and matches insertion order — the
        /// same reasoning as <see cref="Repositories.ImportActionWriter"/>'s sequential writes,
        /// and load-bearing for a consumer whose <c>applyResolvedAction</c> callback (called once per
        /// action, in whatever order this query returns) may need one action's row to already exist
        /// when a later action in the same batch defensively references it. Relying on insertion order
        /// here is only safe because <c>WriteManyAsync</c> inserts sequentially, in the exact order a
        /// consumer's planner produced — never reordered, never bulk/set-based.
        /// </summary>
        internal static string SelectAllForBatch => $"SELECT {SelectColumns} FROM Import_Action WHERE {IdClauses.Equals("BatchId", "batchId")} ORDER BY rowid ASC;";

        /// <summary>
        /// Stages a per-field decision (#154) — Status→Decided, MergedFields holds the decision
        /// payload. Idempotent: resubmitting overwrites the prior decision.
        /// <c>MarkCompletenessAs</c> (#165) is always written, including <c>NULL</c> — resubmitting
        /// a decide call without the override must clear a previously-set one, not leave it stale.
        /// <c>OriginalDecision</c> (#163) is always written too, including <c>NULL</c> — it stores the
        /// caller's actual per-field <c>Keep</c>/<c>Replace</c>/<c>Custom</c> choices (serialized),
        /// separately from <c>MergedFields</c>'s resolved value, so a later export can show exactly
        /// what was decided rather than inferring it from the resolved value alone.
        /// </summary>
        // Case-insensitive (#210) via IdClauses — see docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md.
        internal static readonly string MarkDecided =
            $"UPDATE Import_Action SET Status = @status, MergedFields = @mergedFields, MarkCompletenessAs = @markCompletenessAs, OriginalDecision = @originalDecision, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Reverts a staged decision back to Pending (#154's undo-before-apply) — clears MergedFields. Case-insensitive — see <see cref="MarkDecided"/>.</summary>
        internal static readonly string ClearDecision =
            $"UPDATE Import_Action SET Status = @status, MergedFields = NULL, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Marks an action applied once its batch has been applied (#154) — AppliedAt set. Case-insensitive — see <see cref="MarkDecided"/>.</summary>
        internal static readonly string MarkApplied =
            $"UPDATE Import_Action SET Status = @status, AppliedAt = @appliedAt, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Marks every action sharing a BatchId discarded in one statement (#154) — DiscardedAt set. Case-insensitive — see <see cref="SelectAllForBatch"/>.</summary>
        internal static readonly string MarkBatchDiscarded =
            $"UPDATE Import_Action SET Status = @status, DiscardedAt = @discardedAt, DateModified = @dateModified WHERE {IdClauses.Equals("BatchId", "batchId")};";

        /// <summary>
        /// Case-insensitive on every filter — see <see cref="SelectAllForBatch"/>'s remark for why
        /// <c>BatchId</c> needs it; <c>Status</c>/<c>EntityType</c> need the same treatment because
        /// they arrive as raw query-string values (e.g. <c>?status=pending</c>), and a caller's
        /// casing is never guaranteed to match the enum member name's exact casing as stored.
        /// </summary>
        private static string BuildWhere(bool filterBatchId, bool filterStatus, bool filterEntityType)
        {
            var parts = new List<string>(3);
            if (filterBatchId)    parts.Add(IdClauses.Equals("BatchId", "batchId"));
            if (filterStatus)     parts.Add(TextClauses.Equals("Status", "status"));
            if (filterEntityType) parts.Add(TextClauses.Equals("EntityType", "entityType"));
            return parts.Count > 0 ? " WHERE " + string.Join(" AND ", parts) : string.Empty;
        }
    }

    /// <summary>Audit_Change table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.ChangeWriter"/>.</summary>
    internal static class SystemChangeLog
    {
        /// <summary>Removes all change-log rows.</summary>
        internal const string DeleteAll = "DELETE FROM Audit_Change;";

        /// <summary>
        /// Every change-log entry for a single entity, newest first. <c>EntityId</c> comparison is
        /// case-insensitive (#210) — same reasoning as every other id column in this codebase, applied
        /// even though no endpoint currently exposes this reader over HTTP; <c>ISystemChangeLogReader
        /// .GetHistoryAsync</c> is a real, DI-registered reader regardless. <c>EntityId</c> is also read
        /// through <c>LOWER(...)</c> in the SELECT list — the same read-time presentation-normalization
        /// mechanism as <c>Sql.SystemAudit.SelectPaged</c>/<c>Sql.SystemImportActions.SelectColumns</c>
        /// (ADR 012). <c>InitiatedById</c> is deliberately NOT wrapped: unlike <c>EntityId</c>, which is
        /// always an id, <c>InitiatedById</c> is polymorphic (an import batch UUID, an HTTP route, or an
        /// enrichment provider name — see <see cref="Entities.ChangeEntity.InitiatedById"/>), and
        /// forcing it lowercase would corrupt meaningful casing in the non-id cases. <c>EntityType</c>
        /// is case-insensitive too (#216) — same class of gap as <c>EntityId</c> on this very query,
        /// found during a comprehensive audit before any endpoint ever exposed this reader.
        /// </summary>
        internal static readonly string SelectByEntity =
            $"SELECT {IdClauses.SelectColumn("Id")}, EntityType, {IdClauses.SelectColumn("EntityId")}, InitiatedByType, InitiatedById, Action, Field, OldValue, NewValue, OccurredAt " +
            $"FROM Audit_Change WHERE {TextClauses.Equals("EntityType", "entityType")} AND {IdClauses.Equals("EntityId", "entityId")} ORDER BY OccurredAt DESC;";
    }

    /// <summary>
    /// Import_SourceFileOverride table (#153). INSERT/UPDATE are handled by Dapper.Contrib via
    /// <see cref="Repositories.SourceFileOverrideRegistry"/>; only the lookup needs hand-written SQL.
    /// </summary>
    internal static class SystemSourceFileOverrides
    {
        /// <summary>
        /// The one row for a given (FileName, Origin) pair, if a generated override has ever been
        /// registered for it. Case-insensitive on both — a filename/origin round-tripped through a
        /// caller is never guaranteed to match the exact casing originally stored.
        /// </summary>
        internal static readonly string SelectByFileNameAndOrigin =
            $"SELECT {IdClauses.SelectColumn("Id")}, FileName, Origin, ContentHash, {IdClauses.SelectColumn("SourceBatchId")}, DateCreated, DateModified, DateDeleted, IsDeleted " +
            $"FROM Import_SourceFileOverride WHERE {TextClauses.Equals("FileName", "fileName")} AND {TextClauses.Equals("Origin", "origin")} AND IsDeleted = 0;";
    }

    /// <summary>
    /// Import_FileResource/Import_FileResourceLine/Import_FileResourceBatch tables (#251). INSERT of
    /// the parent row and its line rows are handled by Dapper.Contrib via
    /// <see cref="Repositories.SqliteFileResourceRepository"/>; hand-written SQL covers the dedup
    /// lookup, ordered line read, download read, and the per-FileName prune sweep.
    /// </summary>
    internal static class FileResources
    {
        /// <summary>The one row for a given content hash, if this exact content has been captured before.</summary>
        internal static readonly string SelectByContentHash =
            $"SELECT {IdClauses.SelectColumn("Id")}, FileName, OriginalFolderPath, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, " +
            "Converter, ConverterOptions, FirstSeenAtUtc, LastSeenAtUtc, DateCreated, DateModified, DateDeleted, IsDeleted " +
            "FROM Import_FileResource WHERE ContentHash = @contentHash AND IsDeleted = 0;";

        /// <summary>A single file resource by id, for the download endpoint. Case-insensitive per this project's id-comparison convention.</summary>
        internal static readonly string SelectById =
            $"SELECT {IdClauses.SelectColumn("Id")}, FileName, OriginalFolderPath, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, " +
            $"Converter, ConverterOptions, FirstSeenAtUtc, LastSeenAtUtc, DateCreated, DateModified, DateDeleted, IsDeleted " +
            $"FROM Import_FileResource WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>
        /// Touches an existing row's LastSeenAtUtc/DateModified and overwrites Converter/ConverterOptions
        /// with the latest capture's values — content already captured, seen again, possibly under
        /// different converter settings than last time (see FileResourceMigrations' own correction note).
        /// </summary>
        internal static readonly string UpdateLastSeenAtUtc =
            $"UPDATE Import_FileResource SET LastSeenAtUtc = @lastSeenAtUtc, Converter = @converter, " +
            $"ConverterOptions = @converterOptions, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Every line of a file resource's content, in order — used to reconstruct it.</summary>
        internal static readonly string SelectLinesByFileResourceId =
            $"SELECT {IdClauses.SelectColumn("Id")}, {IdClauses.SelectColumn("FileResourceId")}, LineNumber, Text, DateCreated, DateModified, DateDeleted, IsDeleted " +
            $"FROM Import_FileResourceLine WHERE {IdClauses.Equals("FileResourceId", "fileResourceId")} AND IsDeleted = 0 ORDER BY LineNumber;";

        /// <summary>
        /// Ids of every Import_FileResource row beyond the <c>keepPerFile</c> most-recently-seen
        /// (by LastSeenAtUtc) distinct rows per FileName — the set a prune sweep hard-deletes.
        /// Secondary sort on the table's own implicit <c>rowid</c> (insertion order) breaks ties —
        /// LastSeenAtUtc has only second-level precision (SafeValue.TimestampFormat), so two writes
        /// within the same second would otherwise leave SQLite's own tie-break order unspecified.
        /// </summary>
        internal static readonly string SelectIdsBeyondRetentionPerFileName =
            $"SELECT {IdClauses.SelectColumn("Id")} FROM (" +
            $"  SELECT {IdClauses.SelectColumn("Id")}, ROW_NUMBER() OVER (PARTITION BY FileName ORDER BY LastSeenAtUtc DESC, rowid DESC) AS rn " +
            "  FROM Import_FileResource WHERE IsDeleted = 0" +
            ") WHERE rn > @keepPerFile;";

        /// <summary>
        /// Hard-deletes the given Import_FileResource rows — relies on the schema's own
        /// ON DELETE CASCADE to remove the matching Import_FileResourceLine/Import_FileResourceBatch
        /// rows, which requires the issuing connection to have foreign_keys = ON (the caller's
        /// responsibility; off by default per connection).
        /// </summary>
        internal static readonly string DeleteByIds =
            $"DELETE FROM Import_FileResource WHERE {IdClauses.In("Id", "ids")};";

        /// <summary>Ids of every batch a file resource is linked to, most recent first — the detail endpoint's <c>linkedBatchIds</c>.</summary>
        internal static readonly string SelectBatchIdsForFileResource =
            $"SELECT {IdClauses.SelectColumn("ImportBatchId")} FROM Import_FileResourceBatch " +
            $"WHERE {IdClauses.Equals("FileResourceId", "fileResourceId")} AND IsDeleted = 0 ORDER BY ImportedAt DESC;";

        // COUNT base — shared by CountPage factory method below. Aliased "fr" even without a join, so
        // CountPage and SelectPage below can share the same BuildWhere.
        private const string CountPageBase = "SELECT COUNT(*) FROM Import_FileResource fr WHERE fr.IsDeleted = 0";

        /// <summary>Total matching count for the paginated file-resource listing.</summary>
        internal static string CountPage(bool filterFileName, bool filterOrigin)
            => CountPageBase + BuildWhere(filterFileName, filterOrigin) + ";";

        /// <summary>
        /// Paginated file-resource listing (#251's own GET endpoint) with each row's linked-batch count.
        /// Deliberately a correlated scalar subquery, not an outer join with a row-grouping clause — the
        /// latter is exactly the aggregate-plus-grouping shape <c>SqlAggregateGuard</c> flags for
        /// CVE-2025-6965 review (see docs/sql-safety.md), and a per-row scalar subquery avoids the
        /// question entirely (no grouping clause anywhere in the statement) while still costing one
        /// index-backed lookup per row, the same as the join would — chosen to sidestep the guard rather
        /// than argue past it. Avoids the N+1 a separate follow-up query per row would cost (matching
        /// #195's own N+1-avoidance rule for pagination generally). No line content — that stays on the
        /// dedicated download endpoint.
        /// </summary>
        internal static string SelectPage(bool filterFileName, bool filterOrigin)
            => $"SELECT {IdClauses.SelectColumn("fr.Id")}, fr.FileName, fr.OriginalFolderPath, fr.Origin, fr.ContentHash, " +
               "fr.LineEnding, fr.EndsWithTrailingNewline, fr.Converter, fr.ConverterOptions, " +
               "fr.FirstSeenAtUtc, fr.LastSeenAtUtc, " +
               "(SELECT COUNT(*) FROM Import_FileResourceBatch frb " +
               $"WHERE {IdClauses.Join("frb.FileResourceId", "fr.Id")} AND frb.IsDeleted = 0) AS LinkedBatchCount " +
               "FROM Import_FileResource fr " +
               "WHERE fr.IsDeleted = 0" + BuildWhere(filterFileName, filterOrigin) +
               " ORDER BY fr.FileName ASC, fr.LastSeenAtUtc DESC LIMIT @pageSize OFFSET @offset;";

        // FileName/Origin comparisons are case-insensitive (project-wide convention).
        private static string BuildWhere(bool filterFileName, bool filterOrigin)
        {
            var parts = new List<string>(2);
            if (filterFileName) parts.Add(TextClauses.Equals("fr.FileName", "fileName"));
            if (filterOrigin)   parts.Add(TextClauses.Equals("fr.Origin", "origin"));
            return parts.Count > 0 ? " AND " + string.Join(" AND ", parts) : string.Empty;
        }
    }
}
