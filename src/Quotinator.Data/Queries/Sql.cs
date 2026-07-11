namespace Quotinator.Data.Queries;

/// <summary>Generic infrastructure SQL — schema/version bookkeeping, join helpers, and the System_-prefixed tables Quotinator.Data itself owns.</summary>
/// <remarks>
/// Quotinator-domain SQL (Quotes, Sources, Characters, Conversations, etc.) lives in
/// <c>Quotinator.Engine.Queries.Sql</c> instead — Quotinator.Data must stay domain-agnostic
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
        // once the rename below has happened, sqlite_master no longer contains a table literally
        // named SchemaVersion, so this check is a no-op on every subsequent startup. Only concerns
        // Data's own table — System_ConsumerSchemaVersion is a brand-new table with no legacy name.
        internal const string LegacySchemaVersionExists =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";
        internal const string RenameLegacySchemaVersionTable =
            "ALTER TABLE SchemaVersion RENAME TO System_SchemaVersion;";

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
        // A "system table" is any table whose name starts with a literal System_ prefix — this
        // query never needs to know specific names, so a consuming project can add its own
        // protected tables (e.g. a DB-backed enum-like lookup) with zero changes here. The
        // underscore must be escaped: SQL LIKE treats '_' as a single-character wildcard, so an
        // unescaped 'System_%' would also match an unrelated table like SystemInventory. The
        // ESCAPE clause makes '\_' match a literal underscore only, so SystemInventory (no
        // underscore) is correctly NOT treated as protected.
        internal const string GetUserTables =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
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
            SELECT [w].[Id] AS WidgetId, [w].[Label],
                   [o].[Name] AS OwnerName
            FROM   [Widgets] [w]
            {Joins.Inner("Owners", "o", "w", "OwnerId", "Id")}
            WHERE  [w].[IsDeleted] = 0
            """;
    }

    /// <summary>System_AuditEntries table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.SystemAuditWriter"/>.</summary>
    internal static class SystemAudit
    {
        /// <summary>Removes all audit entries.</summary>
        internal const string DeleteAll     = "DELETE FROM System_AuditEntries;";

        /// <summary>Removes audit entries for a specific table name.</summary>
        internal const string DeleteByTable = "DELETE FROM System_AuditEntries WHERE TableName = @table;";

        // COUNT base — shared by CountPaged factory method below.
        private const string CountPagedBase = "SELECT COUNT(*) FROM System_AuditEntries";

        /// <summary>Paginated audit entry listing, newest first, with optional filters.</summary>
        internal static string SelectPaged(bool filterTable, bool filterRecordId)
            => "SELECT Id, TableName, RecordId, Operation, Agent, PerformedAt FROM System_AuditEntries" +
               BuildWhere(filterTable, filterRecordId) +
               " ORDER BY PerformedAt DESC LIMIT @pageSize OFFSET @offset;";

        /// <summary>Total matching count for the audit list endpoint.</summary>
        internal static string CountPaged(bool filterTable, bool filterRecordId)
            => CountPagedBase + BuildWhere(filterTable, filterRecordId) + ";";

        private static string BuildWhere(bool filterTable, bool filterRecordId)
        {
            var parts = new List<string>(2);
            if (filterTable)    parts.Add("TableName = @table");
            if (filterRecordId) parts.Add("RecordId = @recordId");
            return parts.Count > 0 ? " WHERE " + string.Join(" AND ", parts) : string.Empty;
        }
    }

    /// <summary>System_ImportActions table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.SystemImportActionWriter"/>.</summary>
    internal static class SystemImportActions
    {
        /// <summary>Removes all import-action rows.</summary>
        internal const string DeleteAll = "DELETE FROM System_ImportActions;";

        // COUNT base — shared by CountPaged factory method below.
        private const string CountPagedBase = "SELECT COUNT(*) FROM System_ImportActions";

        // Column list shared by every SELECT below.
        private const string SelectColumns =
            "Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue, IncomingValue, AppliedPolicy, Status, MergedFields, DetectedAt, AppliedAt, DiscardedAt";

        /// <summary>Paginated action listing, newest first, with optional filters.</summary>
        internal static string SelectPaged(bool filterBatchId, bool filterStatus, bool filterEntityType = false)
            => $"SELECT {SelectColumns} FROM System_ImportActions" +
               BuildWhere(filterBatchId, filterStatus, filterEntityType) +
               " ORDER BY DetectedAt DESC LIMIT @pageSize OFFSET @offset;";

        /// <summary>Total matching count for the action list endpoint.</summary>
        internal static string CountPaged(bool filterBatchId, bool filterStatus, bool filterEntityType = false)
            => CountPagedBase + BuildWhere(filterBatchId, filterStatus, filterEntityType) + ";";

        /// <summary>Single-action lookup by Id (#154's decide/undo/apply/discard flows).</summary>
        internal static string SelectById => $"SELECT {SelectColumns} FROM System_ImportActions WHERE Id = @id;";

        /// <summary>
        /// Every action sharing a BatchId, any status — #154's apply-batch readiness check needs the
        /// complete set, not a page. Case-insensitive: a consumer's own batch-id response DTO may be
        /// typed as <c>Guid</c>, which .NET serializes lowercase by default, while stored <c>BatchId</c>
        /// values are always uppercase (<c>Guid.ToString("D").ToUpperInvariant()</c>) — a caller
        /// round-tripping the batch id straight from such a response must still match.
        /// <c>ORDER BY rowid</c> makes the result deterministic and matches insertion order — the
        /// same reasoning as <see cref="Repositories.SystemImportActionWriter"/>'s sequential writes,
        /// and load-bearing for a consumer whose <c>applyResolvedAction</c> callback (called once per
        /// action, in whatever order this query returns) may need one action's row to already exist
        /// when a later action in the same batch defensively references it. Relying on insertion order
        /// here is only safe because <c>WriteManyAsync</c> inserts sequentially, in the exact order a
        /// consumer's planner produced — never reordered, never bulk/set-based.
        /// </summary>
        internal static string SelectAllForBatch => $"SELECT {SelectColumns} FROM System_ImportActions WHERE UPPER(BatchId) = UPPER(@batchId) ORDER BY rowid ASC;";

        /// <summary>Stages a per-field decision (#154) — Status→Decided, MergedFields holds the decision payload. Idempotent: resubmitting overwrites the prior decision.</summary>
        internal const string MarkDecided =
            "UPDATE System_ImportActions SET Status = @status, MergedFields = @mergedFields, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>Reverts a staged decision back to Pending (#154's undo-before-apply) — clears MergedFields.</summary>
        internal const string ClearDecision =
            "UPDATE System_ImportActions SET Status = @status, MergedFields = NULL, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>Marks an action applied once its batch has been applied (#154) — AppliedAt set.</summary>
        internal const string MarkApplied =
            "UPDATE System_ImportActions SET Status = @status, AppliedAt = @appliedAt, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>Marks every action sharing a BatchId discarded in one statement (#154) — DiscardedAt set. Case-insensitive — see <see cref="SelectAllForBatch"/>.</summary>
        internal const string MarkBatchDiscarded =
            "UPDATE System_ImportActions SET Status = @status, DiscardedAt = @discardedAt, DateModified = @dateModified WHERE UPPER(BatchId) = UPPER(@batchId);";

        /// <summary>
        /// Case-insensitive on every filter — see <see cref="SelectAllForBatch"/>'s remark for why
        /// <c>BatchId</c> needs it; <c>Status</c>/<c>EntityType</c> need the same treatment because
        /// they arrive as raw query-string values (e.g. <c>?status=pending</c>), and a caller's
        /// casing is never guaranteed to match the enum member name's exact casing as stored.
        /// </summary>
        private static string BuildWhere(bool filterBatchId, bool filterStatus, bool filterEntityType)
        {
            var parts = new List<string>(3);
            if (filterBatchId)    parts.Add("UPPER(BatchId) = UPPER(@batchId)");
            if (filterStatus)     parts.Add("UPPER(Status) = UPPER(@status)");
            if (filterEntityType) parts.Add("UPPER(EntityType) = UPPER(@entityType)");
            return parts.Count > 0 ? " WHERE " + string.Join(" AND ", parts) : string.Empty;
        }
    }

    /// <summary>System_ChangeLog table. INSERT is handled by Dapper.Contrib via <see cref="Repositories.SystemChangeLogWriter"/>.</summary>
    internal static class SystemChangeLog
    {
        /// <summary>Removes all change-log rows.</summary>
        internal const string DeleteAll = "DELETE FROM System_ChangeLog;";

        /// <summary>Every change-log entry for a single entity, newest first.</summary>
        internal const string SelectByEntity =
            "SELECT Id, EntityType, EntityId, InitiatedByType, InitiatedById, Action, Field, OldValue, NewValue, OccurredAt " +
            "FROM System_ChangeLog WHERE EntityType = @entityType AND EntityId = @entityId ORDER BY OccurredAt DESC;";
    }
}
