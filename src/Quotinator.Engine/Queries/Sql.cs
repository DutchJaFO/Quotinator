namespace Quotinator.Engine.Queries;

/// <summary>All parameterised DML statements for Quotinator-domain tables.</summary>
/// <remarks>
/// Every SQL string touching a Quotinator-domain table (Quotes, Sources, Characters, People,
/// Conversations, and their detail/translation tables) lives here — see
/// <see cref="Quotinator.Data.Queries.Sql"/> for the generic infrastructure counterpart that stays
/// in Quotinator.Data (ADR 004: Quotinator.Data must stay domain-agnostic).
/// <para>
/// Static constants cover fixed queries. Static factory methods cover queries where dynamic
/// clauses (WHERE, field-filter) are appended at call time — every factory method is tested
/// directly in <c>SqlQueryGuardTests.AssembledQueryCases</c> with the full set of clause variants.
/// </para>
/// DDL that runs outside the versioned migration list is not included here. Migration constants
/// remain private inside <c>QuotinatorDatabaseInitializer</c> so their text is frozen at migration time.
/// </remarks>
internal static class Sql
{
    /// <summary>Quotes table — fixed queries and dynamic-query factory methods.</summary>
    internal static class Quotes
    {
        internal const string CountAll    = "SELECT COUNT(*) FROM Quotes;";
        internal const string CountActive = "SELECT COUNT(*) FROM Quotes WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Quotes;";

        internal const string Insert =
            "INSERT OR IGNORE INTO Quotes " +
            "(Id, QuoteText, OriginalLanguage, SourceId, CharacterId, PersonId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @QuoteText, @OriginalLanguage, @SourceId, @CharacterId, @PersonId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state; also used to read a fresh Add's just-inserted defaults.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM Quotes WHERE Id = @id;";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE Quotes SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;";

        // Deliberately excludes CompletenessStatus/NoValueKnown from the SET list (per #55/#165) — an
        // existing row being rewritten by newest-wins must never reset a human's completed review or
        // confirmed "no value known" markers. Only a genuinely new row (see Insert above) gets the
        // Incomplete/[] defaults.
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
                   q.ImportBatchId,
                   q.CompletenessStatus
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

        /// <summary>
        /// #179: Character is no longer scoped by a SourceId column — the same per-Source match is
        /// preserved in meaning, only in mechanism, via the CharacterSources join. #174 is where the
        /// matching key itself changes to something Source-independent.
        /// </summary>
        internal const string SelectIdBySourceAndName =
            "SELECT c.Id FROM Characters c " +
            "JOIN CharacterSources cs ON cs.CharacterId = c.Id " +
            "WHERE cs.SourceId = @sourceId AND c.Name = @name AND c.IsDeleted = 0 AND cs.IsDeleted = 0;";

        /// <summary>
        /// Number of active (non-deleted) Quotes still referencing this Character — used by #59's
        /// batch-undo to decide whether reversing a Character Add is safe (no live row still needs
        /// it) or must be skipped (still shared).
        /// </summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM Quotes WHERE CharacterId = @id AND IsDeleted = 0;";

        // #154's applier resolves an already-staged EntityId (a stable id or a real one from
        // planning-time lookup) idempotently — OR IGNORE lets two concurrently-applied batches that
        // both staged an Add for the same not-yet-existing Character land safely. #179 drops SourceId
        // from this table — the caller inserts the corresponding CharacterSources row separately, in
        // the same transaction, via CharacterSources.InsertIfNotExists.
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Characters (Id, Name, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";
    }

    /// <summary>CharacterSources join table (#179) — a Character may appear in multiple Sources.</summary>
    internal static class CharacterSources
    {
        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale, and always inserted alongside it in the same transaction.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @CharacterId, @SourceId, @DateCreated, NULL, NULL, 0);";

        /// <summary>CharacterSources carries a real FK to Characters(Id) — a stale Character's link rows must be removed before the Character itself is hard-deleted, or the delete violates the FK (same pattern as <see cref="QuoteGenres.DeleteForQuote"/>/<see cref="QuoteTranslations.DeleteForQuote"/>).</summary>
        internal const string DeleteForCharacter = "DELETE FROM CharacterSources WHERE CharacterId = @id;";

        /// <summary>Active (SourceId, SourceTitle) pairs linked to one Character — #185's GetById join. Selects
        /// Title alongside Id since the join through Sources (needed to exclude a soft-deleted Source) already
        /// has it for free, and the response must surface a display name per CLAUDE.md's "Masterdata reference
        /// shape" convention.</summary>
        internal const string SelectSourceReferencesForCharacter =
            "SELECT s.Id, s.Title FROM CharacterSources cs " +
            "JOIN Sources s ON s.Id = cs.SourceId AND s.IsDeleted = 0 " +
            "WHERE cs.CharacterId = @characterId AND cs.IsDeleted = 0;";

        /// <summary>
        /// Active (CharacterId, SourceId, SourceTitle) rows for a batch of Characters in a single round-trip —
        /// #185's list join. Dapper expands @characterIds from any IEnumerable&lt;Guid&gt; automatically (same
        /// pattern as RepositorySql.SelectByIds), avoiding one query per row across a page.
        /// </summary>
        internal const string SelectSourceReferencesForCharacters =
            "SELECT cs.CharacterId, s.Id AS SourceId, s.Title AS SourceTitle FROM CharacterSources cs " +
            "JOIN Sources s ON s.Id = cs.SourceId AND s.IsDeleted = 0 " +
            "WHERE cs.CharacterId IN @characterIds AND cs.IsDeleted = 0;";
    }

    /// <summary>People table.</summary>
    internal static class People
    {
        internal const string CountActive = "SELECT COUNT(*) FROM People WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM People;";
        internal const string SelectIdByName = "SELECT Id FROM People WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>
        /// #173's id-first lookup for an explicit <c>people[]</c> entry — mirrors
        /// <see cref="Sources.SelectExistingById"/>. Case-insensitive (#180 fix, same reasoning as
        /// <see cref="Sources.SelectExistingById"/>'s remark) — Person shares the identical exposure:
        /// an <c>EntityIdentity</c>-derived (always uppercase) row later referenced by an explicit
        /// <c>people[]</c> entry whose file-authored id casing isn't guaranteed to match.
        /// </summary>
        internal const string SelectExistingById =
            "SELECT Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM People WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM People WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE People SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>#173's Modify apply — writes an id-matched Person's corrected Name/DateOfBirth/DateOfDeath. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string UpdateFieldsById =
            "UPDATE People SET Name = @name, DateOfBirth = @dateOfBirth, DateOfDeath = @dateOfDeath, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Number of active (non-deleted) Quotes still referencing this Person — see <see cref="Characters.CountActiveReferences"/>'s remark. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM Quotes WHERE UPPER(PersonId) = UPPER(@id) AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO People (Id, Name, DateOfBirth, DateOfDeath, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, NULL, NULL, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";
    }

    /// <summary>Sources table.</summary>
    internal static class Sources
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Sources WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Sources;";
        internal const string SelectIdByTitleAndType =
            "SELECT Id FROM Sources WHERE Title = @title AND Type = @type AND IsDeleted = 0;";

        /// <summary>
        /// #180's natural-key lookup for a <c>sources[]</c> entry that omits an explicit id (the
        /// enrichment shape — see <see cref="Quotinator.Core.Import.SourceEntry"/>'s remarks). Returns
        /// the matched row's real id plus the two fields that path needs: <c>SeriesId</c> (the only
        /// field it diffs) and <c>Date</c> (carried through unchanged, so an entry that never mentions
        /// a date can't reset one). Title/Type are the lookup key here, so they are never re-read —
        /// they cannot differ by construction. Case-sensitive on Title/Type, matching
        /// <see cref="SelectIdByTitleAndType"/> exactly: these are free-text natural-key values, not
        /// identifiers, and loosening them would silently merge two genuinely distinct Sources (see
        /// #182 for that class of problem).
        /// </summary>
        internal const string SelectExistingByTitleAndType =
            "SELECT Id, Date, SeriesId, CompletenessStatus FROM Sources WHERE Title = @title AND Type = @type AND IsDeleted = 0;";

        /// <summary>
        /// #162's id-first lookup for an explicit <c>sources[]</c> entry — a row already migrated to
        /// the explicit-id model. Distinct from <see cref="SelectIdByTitleAndType"/>'s natural-key
        /// fallback. SeriesId added by #180.
        ///
        /// Case-insensitive (this project's GUID/enum parameter binding rule, CLAUDE.md) — a
        /// file-authored id referencing an already-existing, <c>EntityIdentity</c>-derived row (which
        /// is always stored uppercase) must match regardless of which case the file itself uses.
        /// Found live while authoring #180's curated overlay file: a case mismatch here silently
        /// matches nothing, mirroring the exact class of bug this project has already hit and fixed
        /// piecemeal for GUID-typed query/route parameters — applied here on the same "case-insensitive
        /// by default" principle, not waited for a second live report first.
        /// </summary>
        internal const string SelectExistingById =
            "SELECT Title, Type, Date, SeriesId, CompletenessStatus FROM Sources WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM Sources WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE Sources SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>#162's Modify apply — writes an id-matched Source's corrected Title/Type/Date/SeriesId (SeriesId added by #180). Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal const string UpdateFieldsById =
            "UPDATE Sources SET Title = @title, Type = @type, Date = @date, SeriesId = @seriesId, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>
        /// Number of active (non-deleted) rows still referencing this Source — sums both direct
        /// Quotes and Characters linked via CharacterSources (#179 — a Character may now be linked to
        /// multiple Sources, so this counts links to THIS Source specifically, not all of a
        /// Character's links), since a Character can outlive the specific Quote that introduced it
        /// (see <see cref="Characters.CountActiveReferences"/>'s remark for the reversal use case).
        /// Case-insensitive — see <see cref="SelectExistingById"/>'s remark.
        /// </summary>
        internal const string CountActiveReferences =
            "SELECT (SELECT COUNT(*) FROM Quotes WHERE UPPER(SourceId) = UPPER(@id) AND IsDeleted = 0) " +
            "+ (SELECT COUNT(*) FROM CharacterSources cs JOIN Characters c ON c.Id = cs.CharacterId " +
            "   WHERE UPPER(cs.SourceId) = UPPER(@id) AND cs.IsDeleted = 0 AND c.IsDeleted = 0);";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale. SeriesId (nullable) added by #180, resolved by name at planning time.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Sources (Id, Title, Type, Date, SeriesId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Title, @Type, @Date, @SeriesId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Active Series reference for one Source — #184's GetById join. No row if the Source has
        /// no Series, or its Series has been soft-deleted.</summary>
        internal const string SelectSeriesReferenceForSource =
            "SELECT ser.Id, ser.Name FROM Sources s " +
            "JOIN Series ser ON ser.Id = s.SeriesId AND ser.IsDeleted = 0 " +
            "WHERE s.Id = @sourceId AND s.IsDeleted = 0;";

        /// <summary>
        /// Active Series references for a batch of Sources in a single round-trip — #184's list join,
        /// avoiding one query per row across a page. A Source with no active Series link is simply absent
        /// from the result.
        /// </summary>
        internal const string SelectSeriesReferencesForSources =
            "SELECT s.Id AS SourceId, ser.Id AS SeriesId, ser.Name AS SeriesName FROM Sources s " +
            "JOIN Series ser ON ser.Id = s.SeriesId AND ser.IsDeleted = 0 " +
            "WHERE s.Id IN @sourceIds AND s.IsDeleted = 0;";
    }

    /// <summary>
    /// Series table (#179 schema, #180 JSON wiring). Add-only from the import path — a Series has
    /// only a Name, so there is no Modify/decidability surface the way Source/Person have.
    /// </summary>
    internal static class Series
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Series WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Series;";
        internal const string SelectIdByName = "SELECT Id FROM Series WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale. UniverseId (nullable) is resolved by name at planning time, same as Sources.SeriesId.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Series (Id, Name, UniverseId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @UniverseId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Number of active (non-deleted) Sources still referencing this Series — see <see cref="Characters.CountActiveReferences"/>'s remark.</summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM Sources WHERE SeriesId = @id AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM Series WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE Series SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Active Universe reference for one Series — #187's GetById join. No row if the Series has no
        /// Universe, or its Universe has been soft-deleted.</summary>
        internal const string SelectUniverseReferenceForSeries =
            "SELECT u.Id, u.Name FROM Series s " +
            "JOIN Universe u ON u.Id = s.UniverseId AND u.IsDeleted = 0 " +
            "WHERE s.Id = @seriesId AND s.IsDeleted = 0;";

        /// <summary>
        /// Active Universe references for a batch of Series in a single round-trip — #187's list join, avoiding
        /// one query per row across a page. A Series with no active Universe link is simply absent from the result.
        /// </summary>
        internal const string SelectUniverseReferencesForSeries =
            "SELECT s.Id AS SeriesId, u.Id AS UniverseId, u.Name AS UniverseName FROM Series s " +
            "JOIN Universe u ON u.Id = s.UniverseId AND u.IsDeleted = 0 " +
            "WHERE s.Id IN @seriesIds AND s.IsDeleted = 0;";
    }

    /// <summary>
    /// Universe table (#179 schema, #180 JSON wiring). Add-only from the import path — a Universe has
    /// only a Name, so there is no Modify/decidability surface the way Source/Person have.
    /// </summary>
    internal static class Universe
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Universe WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Universe;";
        internal const string SelectIdByName = "SELECT Id FROM Universe WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Universe (Id, Name, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Number of active (non-deleted) Series still referencing this Universe — see <see cref="Characters.CountActiveReferences"/>'s remark.</summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM Series WHERE UniverseId = @id AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM Universe WHERE UPPER(Id) = UPPER(@id);";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE Universe SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE UPPER(Id) = UPPER(@id);";
    }

    /// <summary>
    /// Conversations table (#67/#68). Unlike Source/Character/Person, a Conversation's id is
    /// explicit in the source file (like Quote), not <c>EntityIdentity</c>-derived from a natural
    /// key — so existence is checked by id, and there is no <c>CountActiveReferences</c> (nothing
    /// else carries an FK to a Conversation; its own <c>ConversationLines</c> point away from it,
    /// not toward it, same as QuoteGenres/QuoteTranslations point away from Quote).
    /// </summary>
    internal static class Conversations
    {
        internal const string DeleteAll = "DELETE FROM Conversations;";
        internal const string SelectIdById = "SELECT Id FROM Conversations WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>
        /// #69: <c>GET /api/v1/conversations/{id}</c>'s own lookup — case-insensitive (<c>UPPER</c>),
        /// unlike <see cref="SelectIdById"/> above, because this id comes from a user-supplied route
        /// parameter (established rule: new GUID route parameters default to case-insensitive
        /// matching), while <see cref="SelectIdById"/> only ever compares against another id already
        /// stored in this database (an incoming file's own casing, matched exactly, same as Quote).
        /// </summary>
        internal const string SelectForRead =
            "SELECT Id, Description FROM Conversations WHERE UPPER(Id) = UPPER(@id) AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Conversations (Id, Description, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Description, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>#176's id-first lookup for an explicit <c>conversations[]</c> entry — mirrors <see cref="Sources.SelectExistingById"/>. Never selects <c>lines</c> — out of scope for Modify.</summary>
        internal const string SelectExistingById =
            "SELECT Description, CompletenessStatus FROM Conversations WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM Conversations WHERE Id = @id;";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE Conversations SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>#176's Modify apply — writes an id-matched Conversation's corrected Description only. Never touches Lines/CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for the latter.</summary>
        internal const string UpdateDescriptionById =
            "UPDATE Conversations SET Description = @description, DateModified = @dateModified WHERE Id = @id;";
    }

    /// <summary>
    /// ConversationLines table (#67/#68) — detail rows of a Conversation, the same relationship
    /// QuoteGenres/QuoteTranslations have to Quote. Never staged as its own <c>SystemImportAction</c>;
    /// written alongside its parent Conversation's own apply step.
    /// </summary>
    internal static class ConversationLines
    {
        internal const string DeleteAll = "DELETE FROM ConversationLines;";

        /// <summary>
        /// Clears a Conversation's own lines before its stale row is hard-deleted — same pattern as
        /// <see cref="QuoteGenres.DeleteForQuote"/>/<see cref="QuoteTranslations.DeleteForQuote"/>
        /// clearing a Quote's detail rows first: the FK to ConversationLines would otherwise block
        /// the hard-delete (#59's stale-Add-target scenario).
        /// </summary>
        internal const string DeleteForConversation = "DELETE FROM ConversationLines WHERE ConversationId = @id;";

        /// <summary>Clears any lines still pointing at a stale StageDirection before its hard-delete — same rationale as <see cref="DeleteForConversation"/>.</summary>
        internal const string DeleteForStageDirection = "DELETE FROM ConversationLines WHERE StageDirectionId = @id;";

        /// <summary>Clears any lines still pointing at a stale SoundCue before its hard-delete — same rationale as <see cref="DeleteForConversation"/>.</summary>
        internal const string DeleteForSoundCue = "DELETE FROM ConversationLines WHERE SoundCueId = @id;";

        internal const string Insert =
            "INSERT OR IGNORE INTO ConversationLines " +
            "(Id, ConversationId, [Order], LineType, QuoteId, StageDirectionId, SoundCueId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @ConversationId, @Order, @LineType, @QuoteId, @StageDirectionId, @SoundCueId, @DateCreated, NULL, NULL, 0);";

        /// <summary>#69: a conversation's full ordered line list — the parent Conversation's own existence was already confirmed by <see cref="Conversations.SelectForRead"/>, so this is a plain, case-sensitive FK match.</summary>
        internal const string SelectByConversationId =
            "SELECT [Order], LineType, QuoteId, StageDirectionId, SoundCueId FROM ConversationLines " +
            "WHERE ConversationId = @conversationId AND IsDeleted = 0 ORDER BY [Order] ASC;";

        /// <summary>
        /// #69: every conversation a quote appears in, with its position and the conversation's total
        /// line count — backs the consumer's <c>QuoteResponse.Conversations</c> field (every read
        /// endpoint) and <c>/random</c>'s conversation-selection step. Joins through Conversations for
        /// the same reason <see cref="StageDirections.CountActiveReferences"/> does — IsDeleted only
        /// ever changes on the parent, never on a ConversationLines row itself.
        /// </summary>
        internal const string SelectMembershipForQuote =
            "SELECT cl.ConversationId, cl.[Order] AS Position, " +
            "(SELECT COUNT(*) FROM ConversationLines cl2 WHERE cl2.ConversationId = cl.ConversationId AND cl2.IsDeleted = 0) AS TotalLines " +
            "FROM ConversationLines cl " +
            "INNER JOIN Conversations c ON c.Id = cl.ConversationId AND c.IsDeleted = 0 " +
            "WHERE cl.QuoteId = @quoteId AND cl.IsDeleted = 0;";

        /// <summary>#69: every QuoteId referenced by a conversation's lines — used by <c>/random</c>'s dedup to exclude every quote in a selected conversation, not only the one that triggered the selection.</summary>
        internal const string SelectQuoteIdsForConversation =
            "SELECT QuoteId FROM ConversationLines WHERE ConversationId = @conversationId AND QuoteId IS NOT NULL AND IsDeleted = 0;";

        /// <summary>
        /// Active line counts for a batch of Conversations in a single round-trip — #189's list join, avoiding
        /// one query per row across a page. Uses a correlated <c>COUNT(*)</c> subquery per row — the same
        /// pattern <see cref="SelectMembershipForQuote"/> already uses — rather than a grouping clause, so
        /// this does not trip <c>SqlAggregateGuard</c>'s CVE-2025-6965 heuristic; see docs/sql-safety.md.
        /// A Conversation with zero active lines is simply absent from the result — callers default missing
        /// keys to 0. <c>UPPER(cl.ConversationId)</c> in the WHERE clause is required, not cosmetic — #68's
        /// curated JSON conversations were seeded with their file-authored lowercase ids preserved verbatim
        /// (per CLAUDE.md's case-insensitivity convention: an import file's own explicit id is under no
        /// obligation to match the codebase's usual stored-uppercase convention), while GuidHandler always
        /// uppercases the bound @conversationIds parameters — an exact-case IN match against the unmodified
        /// column silently matched nothing, live-verified during this issue's own T2 pass.
        /// </summary>
        internal const string SelectLineCountsForConversations =
            "SELECT DISTINCT cl.ConversationId, " +
            "CAST((SELECT COUNT(*) FROM ConversationLines cl2 WHERE cl2.ConversationId = cl.ConversationId AND cl2.IsDeleted = 0) AS INTEGER) AS LineCount " +
            "FROM ConversationLines cl " +
            "WHERE UPPER(cl.ConversationId) IN @conversationIds AND cl.IsDeleted = 0;";
    }

    /// <summary>StageDirections table (#67/#68). Explicit-id existence check, like <see cref="Conversations"/> — see its remark.</summary>
    internal static class StageDirections
    {
        internal const string DeleteAll = "DELETE FROM StageDirections;";
        internal const string SelectIdById = "SELECT Id FROM StageDirections WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>#171's id-first lookup for an explicit <c>stageDirections[]</c> entry — mirrors <see cref="Sources.SelectExistingById"/>.</summary>
        internal const string SelectExistingById =
            "SELECT Text, ImageUrl, CompletenessStatus FROM StageDirections WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM StageDirections WHERE Id = @id;";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE StageDirections SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>#171's Modify apply — writes an id-matched StageDirection's corrected Text/ImageUrl. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that.</summary>
        internal const string UpdateFieldsById =
            "UPDATE StageDirections SET Text = @text, ImageUrl = @imageUrl, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>
        /// Number of active ConversationLines still referencing this StageDirection — see
        /// <see cref="Characters.CountActiveReferences"/>'s remark for the reversal use case.
        /// Joins through Conversations rather than filtering <c>ConversationLines.IsDeleted</c>
        /// directly — a ConversationLines row is never independently soft-deleted (it's a detail row
        /// of its parent Conversation, same relationship QuoteGenres/QuoteTranslations have to
        /// Quote), so its own IsDeleted flag never reflects whether its parent conversation is still
        /// live; only the parent's IsDeleted does.
        /// </summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM ConversationLines cl " +
            "INNER JOIN Conversations c ON c.Id = cl.ConversationId " +
            "WHERE cl.StageDirectionId = @id AND c.IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO StageDirections (Id, Text, ImageUrl, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Text, @ImageUrl, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>
        /// #69: single-row lookup with optional translation, for embedding inside a conversation's
        /// line list. <c>@id</c> is always an internally-known StageDirectionId (from a
        /// ConversationLines row), never user input directly — plain, case-sensitive match, same as
        /// <see cref="Quotes.SelectById"/>. StageDirections has no OriginalLanguage column (unlike
        /// Quotes) — #67's schema never added one, since every bundled stage direction is English;
        /// EffectiveLanguage hardcodes the <c>'en'</c> fallback that <c>Quote.OriginalLanguage</c>
        /// otherwise defaults to. Revisit with a real migration if non-English stage directions are
        /// ever needed.
        /// </summary>
        internal const string SelectByIdWithTranslation =
            """
            SELECT sd.Id, COALESCE(sdt.Text, sd.Text) AS Text, sd.ImageUrl,
                   CASE WHEN sdt.Text IS NOT NULL THEN @lang ELSE 'en' END AS EffectiveLanguage
            FROM StageDirections sd
            LEFT JOIN StageDirectionTranslations sdt ON sdt.StageDirectionId = sd.Id AND sdt.Language = @lang AND sdt.IsDeleted = 0
            WHERE sd.Id = @id AND sd.IsDeleted = 0;
            """;
    }

    /// <summary>StageDirectionTranslations table (#67/#68) — detail rows of a StageDirection, same relationship as <see cref="QuoteTranslations"/> to Quote.</summary>
    internal static class StageDirectionTranslations
    {
        internal const string DeleteAll = "DELETE FROM StageDirectionTranslations;";
        internal const string DeleteForStageDirection = "DELETE FROM StageDirectionTranslations WHERE StageDirectionId = @id;";

        internal const string Insert =
            "INSERT OR IGNORE INTO StageDirectionTranslations " +
            "(Id, StageDirectionId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @StageDirectionId, @Language, @Text, @DateCreated, NULL, NULL, 0);";
    }

    /// <summary>SoundCues table (#67/#68). Explicit-id existence check, like <see cref="Conversations"/> — see its remark.</summary>
    internal static class SoundCues
    {
        internal const string DeleteAll = "DELETE FROM SoundCues;";
        internal const string SelectIdById = "SELECT Id FROM SoundCues WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>#172's id-first lookup for an explicit <c>soundCues[]</c> entry — mirrors <see cref="Sources.SelectExistingById"/>.</summary>
        internal const string SelectExistingById =
            "SELECT Text, SoundFileUrl, ImageUrl, CompletenessStatus FROM SoundCues WHERE Id = @id AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state.</summary>
        internal const string SelectCompletenessById =
            "SELECT CompletenessStatus, NoValueKnown FROM SoundCues WHERE Id = @id;";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert.</summary>
        internal const string UpdateCompletenessById =
            "UPDATE SoundCues SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>#172's Modify apply — writes an id-matched SoundCue's corrected Text/SoundFileUrl/ImageUrl. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that.</summary>
        internal const string UpdateFieldsById =
            "UPDATE SoundCues SET Text = @text, SoundFileUrl = @soundFileUrl, ImageUrl = @imageUrl, DateModified = @dateModified WHERE Id = @id;";

        /// <summary>Number of active ConversationLines still referencing this SoundCue — see <see cref="StageDirections.CountActiveReferences"/>'s remark for why this joins through Conversations.</summary>
        internal const string CountActiveReferences =
            "SELECT COUNT(*) FROM ConversationLines cl " +
            "INNER JOIN Conversations c ON c.Id = cl.ConversationId " +
            "WHERE cl.SoundCueId = @id AND c.IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO SoundCues (Id, Text, SoundFileUrl, ImageUrl, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>#69: single-row lookup with optional translation — see <see cref="StageDirections.SelectByIdWithTranslation"/>'s remark (same 'en'-hardcoded rationale, same internal-id-only usage).</summary>
        internal const string SelectByIdWithTranslation =
            """
            SELECT sc.Id, COALESCE(sct.Text, sc.Text) AS Text, sc.SoundFileUrl, sc.ImageUrl,
                   CASE WHEN sct.Text IS NOT NULL THEN @lang ELSE 'en' END AS EffectiveLanguage
            FROM SoundCues sc
            LEFT JOIN SoundCueTranslations sct ON sct.SoundCueId = sc.Id AND sct.Language = @lang AND sct.IsDeleted = 0
            WHERE sc.Id = @id AND sc.IsDeleted = 0;
            """;
    }

    /// <summary>SoundCueTranslations table (#67/#68) — detail rows of a SoundCue, same relationship as <see cref="QuoteTranslations"/> to Quote.</summary>
    internal static class SoundCueTranslations
    {
        internal const string DeleteAll = "DELETE FROM SoundCueTranslations;";
        internal const string DeleteForSoundCue = "DELETE FROM SoundCueTranslations WHERE SoundCueId = @id;";

        internal const string Insert =
            "INSERT OR IGNORE INTO SoundCueTranslations " +
            "(Id, SoundCueId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @SoundCueId, @Language, @Text, @DateCreated, NULL, NULL, 0);";
    }
}
