namespace Quotinator.Data.Queries;

/// <summary>All parameterised DML statements used by DatabaseInitializer and SqliteQuoteService.</summary>
/// <remarks>
/// Every SQL string the application executes lives here. This makes the full set of queries
/// discoverable in one place and keeps the aggregate-guard unit tests exhaustive.
/// <para>
/// Static constants cover fixed queries. Static factory methods cover queries where dynamic
/// clauses (WHERE, field-filter) are appended at call time — every factory method is tested
/// directly in <c>SqlQueryGuardTests.AssembledQueryCases</c> with the full set of clause variants.
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

    /// <summary>Quotes table — fixed queries and dynamic-query factory methods.</summary>
    internal static class Quotes
    {
        internal const string CountAll    = "SELECT COUNT(*) FROM Quotes;";
        internal const string CountActive = "SELECT COUNT(*) FROM Quotes WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Quotes;";

        internal const string Insert =
            "INSERT OR IGNORE INTO Quotes " +
            "(Id, QuoteText, OriginalLanguage, SourceId, CharacterId, PersonId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, IsComplete, NoValueKnown) " +
            "VALUES (@Id, @QuoteText, @OriginalLanguage, @SourceId, @CharacterId, @PersonId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 0, '[]');";

        // Deliberately excludes IsComplete/NoValueKnown from the SET list (per #55) — an existing row
        // being rewritten by newest-wins must never reset a human's completed review or confirmed
        // "no value known" markers. Only a genuinely new row (see Insert above) gets the false/[] defaults.
        internal const string UpdateOnNewestWins =
            "UPDATE Quotes SET QuoteText=@text, OriginalLanguage=@lang, SourceId=@sid, " +
            "CharacterId=@cid, PersonId=@pid, ImportBatchId=@batchId, DateModified=@mod WHERE Id=@id;";

        // Shared SELECT projection used by all read factory methods below.
        // @lang is always bound — null when no translation is requested.
        private const string SelectBase = """
            SELECT
                q.Id,
                COALESCE(qt.QuoteText,  q.QuoteText)  AS QuoteText,
                q.OriginalLanguage,
                COALESCE(st.Title,      s.Title)       AS Source,
                s.Date,
                s.Type                                 AS SourceType,
                COALESCE(ct.Name,       c.Name)        AS Character,
                p.Name                                 AS Author,
                CASE WHEN qt.QuoteText IS NOT NULL THEN @lang ELSE q.OriginalLanguage END AS EffectiveLanguage
            FROM   Quotes          q
            JOIN   Sources         s  ON  s.Id  = q.SourceId                                          AND s.IsDeleted  = 0
            LEFT JOIN Characters   c  ON  c.Id  = q.CharacterId                                       AND c.IsDeleted  = 0
            LEFT JOIN People       p  ON  p.Id  = q.PersonId                                          AND p.IsDeleted  = 0
            LEFT JOIN QuoteTranslations    qt ON qt.QuoteId     = q.Id AND qt.Language = @lang        AND qt.IsDeleted = 0
            LEFT JOIN SourceTranslations   st ON st.SourceId    = s.Id AND st.Language = @lang        AND st.IsDeleted = 0
            LEFT JOIN CharacterTranslations ct ON ct.CharacterId = c.Id AND ct.Language = @lang       AND ct.IsDeleted = 0
            """;

        // COUNT base for GetRandom — includes character/author/source JOINs needed for all filter options.
        private const string CountForRandomBase =
            "SELECT COUNT(*) FROM Quotes q " +
            "JOIN Sources s ON s.Id = q.SourceId AND s.IsDeleted = 0 " +
            "LEFT JOIN Characters c ON c.Id = q.CharacterId AND c.IsDeleted = 0 " +
            "LEFT JOIN People p ON p.Id = q.PersonId AND p.IsDeleted = 0";

        // COUNT base for GetAll — Sources JOIN only; character/author/source filters not supported there.
        private const string CountForGetAllBase =
            "SELECT COUNT(*) FROM Quotes q JOIN Sources s ON s.Id = q.SourceId AND s.IsDeleted = 0";

        // ----- Dynamic query factory methods -----
        // Each method returns the complete SQL string for one specific call shape.
        // Tests call these methods with the full range of whereClause/fieldFilter inputs
        // to guarantee no aggregate vulnerability can be introduced dynamically.

        /// <summary>Single-quote lookup by Id.</summary>
        internal static string SelectById()
            => $"{SelectBase} WHERE q.Id = @id AND q.IsDeleted = 0";

        /// <summary>
        /// Raw (untranslated) single-quote lookup by Id — no translation JOINs, no <c>@lang</c> parameter.
        /// Used by merge/conflict-resolution logic that needs the original stored field values to compare
        /// against an incoming record, never a translated view. Unlike <see cref="SelectById()"/>, this
        /// also returns <c>Type</c> (needed to rebuild a full field map for merging).
        /// </summary>
        internal static string SelectRawById()
            => """
               SELECT
                   q.Id,
                   q.QuoteText,
                   q.OriginalLanguage,
                   s.Title AS Source,
                   s.Date,
                   s.Type,
                   c.Name  AS Character,
                   p.Name  AS Author,
                   q.ImportBatchId
               FROM   Quotes          q
               JOIN   Sources         s ON s.Id = q.SourceId    AND s.IsDeleted = 0
               LEFT JOIN Characters   c ON c.Id = q.CharacterId AND c.IsDeleted = 0
               LEFT JOIN People       p ON p.Id = q.PersonId    AND p.IsDeleted = 0
               WHERE q.Id = @id AND q.IsDeleted = 0
               """;

        /// <summary>Random selection with dynamic filter and row count.</summary>
        internal static string SelectRandom(string whereClause)
            => $"{SelectBase} {whereClause} ORDER BY RANDOM() LIMIT @count";

        /// <summary>Paginated listing with dynamic filter.</summary>
        internal static string SelectPaged(string whereClause)
            => $"{SelectBase} {whereClause} ORDER BY q.Id LIMIT @pageSize OFFSET @offset";

        /// <summary>Full-text search with dynamic filter and field predicate.</summary>
        internal static string SelectSearch(string whereClause, string fieldFilter)
            => $"{SelectBase} {whereClause} AND {fieldFilter} LIMIT @limit";

        /// <summary>Total matching count for GetRandom with dynamic filter.</summary>
        internal static string CountRandom(string whereClause)
            => $"{CountForRandomBase} {whereClause}";

        /// <summary>Total matching count for GetAll with dynamic filter.</summary>
        internal static string CountGetAll(string whereClause)
            => $"{CountForGetAllBase} {whereClause}";
    }

    /// <summary>Field-filter predicates for the Search endpoint — one per searchable column.</summary>
    internal static class SearchField
    {
        internal const string Quote     = "q.QuoteText LIKE @like";
        internal const string Source    = "s.Title LIKE @like";
        internal const string Character = "c.Name LIKE @like";
        internal const string Author    = "p.Name LIKE @like";
        internal const string All       = "(q.QuoteText LIKE @like OR s.Title LIKE @like OR c.Name LIKE @like OR p.Name LIKE @like)";
    }

    /// <summary>QuoteGenres junction table.</summary>
    internal static class QuoteGenres
    {
        internal const string CountAll       = "SELECT COUNT(*) FROM QuoteGenres;";
        internal const string DeleteAll      = "DELETE FROM QuoteGenres;";
        internal const string DeleteForQuote = "DELETE FROM QuoteGenres WHERE QuoteId = @id;";
        internal const string LoadForQuote   = "SELECT Genre FROM QuoteGenres WHERE QuoteId = @id AND IsDeleted = 0";

        internal const string Insert =
            "INSERT OR IGNORE INTO QuoteGenres " +
            "(Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @QuoteId, @Genre, @DateCreated, NULL, NULL, 0);";

        // WHERE EXISTS guards against FK violations during genre re-seed when source-file IDs
        // differ from those already in the database (e.g. after a UUID scheme change).
        internal const string InsertWithExistsGuard =
            "INSERT OR IGNORE INTO QuoteGenres " +
            "(Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "SELECT @Id, @QuoteId, @Genre, @DateCreated, NULL, NULL, 0 " +
            "WHERE EXISTS (SELECT 1 FROM Quotes WHERE Id = @QuoteId AND IsDeleted = 0);";
    }

    /// <summary>QuoteTranslations table.</summary>
    internal static class QuoteTranslations
    {
        internal const string DeleteAll      = "DELETE FROM QuoteTranslations;";
        internal const string DeleteForQuote = "DELETE FROM QuoteTranslations WHERE QuoteId = @id;";

        internal const string Insert =
            "INSERT OR IGNORE INTO QuoteTranslations " +
            "(Id, QuoteId, Language, QuoteText, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @QuoteId, @Language, @QuoteText, @DateCreated, NULL, NULL, 0);";
    }

    /// <summary>SourceTranslations table.</summary>
    internal static class SourceTranslations
    {
        internal const string DeleteAll      = "DELETE FROM SourceTranslations;";
        internal const string CountForSource =
            "SELECT COUNT(*) FROM SourceTranslations WHERE SourceId = @sid AND Language = @lang AND IsDeleted = 0;";
    }

    /// <summary>CharacterTranslations table.</summary>
    internal static class CharacterTranslations
    {
        internal const string DeleteAll = "DELETE FROM CharacterTranslations;";
    }

    /// <summary>Characters table.</summary>
    internal static class Characters
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Characters WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Characters;";
        internal const string SelectIdBySourceAndName =
            "SELECT Id FROM Characters WHERE SourceId = @sourceId AND Name = @name AND IsDeleted = 0;";

        // #154's applier resolves an already-staged EntityId (a stable id or a real one from
        // planning-time lookup) idempotently — OR IGNORE lets two concurrently-applied batches that
        // both staged an Add for the same not-yet-existing Character land safely.
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Characters (Id, SourceId, Name, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, IsComplete, NoValueKnown) " +
            "VALUES (@Id, @SourceId, @Name, @ImportBatchId, @DateCreated, NULL, NULL, 0, 0, '[]');";
    }

    /// <summary>People table.</summary>
    internal static class People
    {
        internal const string CountActive = "SELECT COUNT(*) FROM People WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM People;";
        internal const string SelectIdByName = "SELECT Id FROM People WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO People (Id, Name, DateOfBirth, DateOfDeath, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, IsComplete, NoValueKnown) " +
            "VALUES (@Id, @Name, NULL, NULL, @ImportBatchId, @DateCreated, NULL, NULL, 0, 0, '[]');";
    }

    /// <summary>Sources table.</summary>
    internal static class Sources
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Sources WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Sources;";
        internal const string SelectIdByTitleAndType =
            "SELECT Id FROM Sources WHERE Title = @title AND Type = @type AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Sources (Id, Title, Type, Date, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, IsComplete, NoValueKnown) " +
            "VALUES (@Id, @Title, @Type, @Date, @ImportBatchId, @DateCreated, NULL, NULL, 0, 0, '[]');";
    }

    /// <summary>ImportBatches table.</summary>
    internal static class ImportBatches
    {
        internal const string SelectAll =
            "SELECT * FROM ImportBatches WHERE IsDeleted = 0 ORDER BY ImportedAt DESC;";

        internal const string SelectByType =
            "SELECT * FROM ImportBatches WHERE IsDeleted = 0 AND Type = @type ORDER BY ImportedAt DESC;";

        internal const string UpdateRecordCount =
            "UPDATE ImportBatches SET RecordCount = @count, DateModified = @now WHERE Id = @id;";

        internal const string DeleteAll = "DELETE FROM ImportBatches;";
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
        /// </summary>
        internal static string SelectAllForBatch => $"SELECT {SelectColumns} FROM System_ImportActions WHERE UPPER(BatchId) = UPPER(@batchId);";

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
