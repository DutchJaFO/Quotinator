using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

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
/// Every id-to-id comparison (a WHERE-clause parameter match, an IN-list match, or a JOIN
/// condition) is built through <see cref="IdClauses"/>, not hand-typed <c>LOWER(...)</c> — see
/// ADR 012 and <c>docs/database-conventions.md</c>'s "Entity id casing" section. A fixed query
/// that calls <see cref="IdClauses"/> must be <c>static readonly</c>, not <c>const</c> — C# does
/// not allow a method call in a compile-time constant expression.
/// <para/>
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

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state; also used to read a fresh Add's just-inserted defaults. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark; #210 extends this to Quote.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM Quotes WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE Quotes SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        // Deliberately excludes CompletenessStatus/NoValueKnown from the SET list (per #55/#165) — an
        // existing row being rewritten by newest-wins must never reset a human's completed review or
        // confirmed "no value known" markers. Only a genuinely new row (see Insert above) gets the
        // Incomplete/[] defaults. Case-insensitive WHERE — see Sources.SelectExistingById's remark.
        internal static readonly string UpdateOnNewestWins =
            $"UPDATE Quotes SET QuoteText=@text, OriginalLanguage=@lang, SourceId=@sid, " +
            $"CharacterId=@cid, PersonId=@pid, ImportBatchId=@batchId, DateModified=@mod WHERE {IdClauses.Equals("Id", "id")};";

        // Shared SELECT projection used by all read factory methods below. Every JOIN condition
        // between two id columns is LOWER()-wrapped via IdClauses.Join — defense-in-depth, since
        // both sides are already canonical by construction once write-side canonicalization
        // (EntityIdCanonicalizer) is in place, per the developer's "never assume" directive (#210).
        // @lang is always bound — null when no translation is requested.
        private static readonly string SelectBase = $"""
            SELECT
                {IdClauses.SelectColumn("q.Id", "Id")},
                COALESCE(qt.QuoteText,  q.QuoteText)  AS QuoteText,
                q.OriginalLanguage,
                COALESCE(st.Title,      s.Title)       AS Source,
                s.Date,
                s.Type                                 AS SourceType,
                COALESCE(ct.Name,       c.Name)        AS Character,
                p.Name                                 AS Author,
                CASE WHEN qt.QuoteText IS NOT NULL THEN @lang ELSE q.OriginalLanguage END AS EffectiveLanguage,
                {IdClauses.SelectColumn("ser.Id", "SeriesId")},
                ser.Name                               AS SeriesName,
                {IdClauses.SelectColumn("uni.Id", "UniverseId")},
                uni.Name                                AS UniverseName
            FROM   Quotes          q
            JOIN   Sources         s  ON  {IdClauses.Join("s.Id", "q.SourceId")}                                          AND s.IsDeleted  = 0
            LEFT JOIN Characters   c  ON  {IdClauses.Join("c.Id", "q.CharacterId")}                                       AND c.IsDeleted  = 0
            LEFT JOIN People       p  ON  {IdClauses.Join("p.Id", "q.PersonId")}                                          AND p.IsDeleted  = 0
            LEFT JOIN QuoteTranslations    qt ON {IdClauses.Join("qt.QuoteId", "q.Id")} AND qt.Language = @lang        AND qt.IsDeleted = 0
            LEFT JOIN SourceTranslations   st ON {IdClauses.Join("st.SourceId", "s.Id")} AND st.Language = @lang        AND st.IsDeleted = 0
            LEFT JOIN CharacterTranslations ct ON {IdClauses.Join("ct.CharacterId", "c.Id")} AND ct.Language = @lang       AND ct.IsDeleted = 0
            LEFT JOIN Series       ser ON {IdClauses.Join("ser.Id", "s.SeriesId")}                                         AND ser.IsDeleted = 0
            LEFT JOIN Universe     uni ON {IdClauses.Join("uni.Id", "ser.UniverseId")}                                     AND uni.IsDeleted = 0
            """;

        // COUNT base for GetRandom — includes character/author/source JOINs needed for all filter options.
        private static readonly string CountForRandomBase =
            "SELECT COUNT(*) FROM Quotes q " +
            $"JOIN Sources s ON {IdClauses.Join("s.Id", "q.SourceId")} AND s.IsDeleted = 0 " +
            $"LEFT JOIN Characters c ON {IdClauses.Join("c.Id", "q.CharacterId")} AND c.IsDeleted = 0 " +
            $"LEFT JOIN People p ON {IdClauses.Join("p.Id", "q.PersonId")} AND p.IsDeleted = 0";

        // COUNT base for GetAll — Sources JOIN only; character/author/source filters not supported there.
        private static readonly string CountForGetAllBase =
            $"SELECT COUNT(*) FROM Quotes q JOIN Sources s ON {IdClauses.Join("s.Id", "q.SourceId")} AND s.IsDeleted = 0";

        // ----- Dynamic query factory methods -----
        // Each method returns the complete SQL string for one specific call shape.
        // Tests call these methods with the full range of whereClause/fieldFilter inputs
        // to guarantee no aggregate vulnerability can be introduced dynamically.

        /// <summary>Single-quote lookup by Id. Case-insensitive (#210) — see <see cref="Sources.SelectExistingById"/>'s remark; this was previously the one fully-unmitigated gap of this kind, since Quotes.Id had no read-side tolerance at all before #210.</summary>
        internal static string SelectById()
            => $"{SelectBase} WHERE {IdClauses.Equals("q.Id", "id")} AND q.IsDeleted = 0";

        /// <summary>
        /// Raw (untranslated) single-quote lookup by Id — no translation JOINs, no <c>@lang</c> parameter.
        /// Used by merge/conflict-resolution logic that needs the original stored field values to compare
        /// against an incoming record, never a translated view. Unlike <see cref="SelectById()"/>, this
        /// also returns <c>Type</c> (needed to rebuild a full field map for merging). Case-insensitive (#210) — same as <see cref="SelectById()"/>.
        /// </summary>
        internal static string SelectRawById()
            => $"""
               SELECT
                   {IdClauses.SelectColumn("q.Id", "Id")},
                   q.QuoteText,
                   q.OriginalLanguage,
                   s.Title AS Source,
                   s.Date,
                   s.Type,
                   c.Name  AS Character,
                   p.Name  AS Author,
                   {IdClauses.SelectColumn("q.ImportBatchId", "ImportBatchId")},
                   q.CompletenessStatus
               FROM   Quotes          q
               JOIN   Sources         s ON {IdClauses.Join("s.Id", "q.SourceId")}    AND s.IsDeleted = 0
               LEFT JOIN Characters   c ON {IdClauses.Join("c.Id", "q.CharacterId")} AND c.IsDeleted = 0
               LEFT JOIN People       p ON {IdClauses.Join("p.Id", "q.PersonId")}    AND p.IsDeleted = 0
               WHERE {IdClauses.Equals("q.Id", "id")} AND q.IsDeleted = 0
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
        internal const string CountAll  = "SELECT COUNT(*) FROM QuoteGenres;";
        internal const string DeleteAll = "DELETE FROM QuoteGenres;";

        internal static readonly string DeleteForQuote = $"DELETE FROM QuoteGenres WHERE {IdClauses.Equals("QuoteId", "id")};";
        internal static readonly string LoadForQuote   = $"SELECT Genre FROM QuoteGenres WHERE {IdClauses.Equals("QuoteId", "id")} AND IsDeleted = 0";

        internal const string Insert =
            "INSERT OR IGNORE INTO QuoteGenres " +
            "(Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @QuoteId, @Genre, @DateCreated, NULL, NULL, 0);";

        // WHERE EXISTS guards against FK violations during genre re-seed when source-file IDs
        // differ from those already in the database (e.g. after a UUID scheme change).
        // Case-insensitive — see Sources.SelectExistingById's remark.
        internal static readonly string InsertWithExistsGuard =
            "INSERT OR IGNORE INTO QuoteGenres " +
            "(Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "SELECT @Id, @QuoteId, @Genre, @DateCreated, NULL, NULL, 0 " +
            $"WHERE EXISTS (SELECT 1 FROM Quotes WHERE {IdClauses.Equals("Id", "QuoteId")} AND IsDeleted = 0);";
    }

    /// <summary>QuoteTranslations table.</summary>
    internal static class QuoteTranslations
    {
        internal const string DeleteAll = "DELETE FROM QuoteTranslations;";
        internal static readonly string DeleteForQuote = $"DELETE FROM QuoteTranslations WHERE {IdClauses.Equals("QuoteId", "id")};";

        internal const string Insert =
            "INSERT OR IGNORE INTO QuoteTranslations " +
            "(Id, QuoteId, Language, QuoteText, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @QuoteId, @Language, @QuoteText, @DateCreated, NULL, NULL, 0);";
    }

    /// <summary>SourceTranslations table.</summary>
    internal static class SourceTranslations
    {
        internal const string DeleteAll = "DELETE FROM SourceTranslations;";
        internal static readonly string CountForSource =
            $"SELECT COUNT(*) FROM SourceTranslations WHERE {IdClauses.Equals("SourceId", "sid")} AND Language = @lang AND IsDeleted = 0;";
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
        internal static readonly string SelectIdBySourceAndName =
            $"SELECT {IdClauses.SelectColumn("c.Id", "Id")} FROM Characters c " +
            $"JOIN CharacterSources cs ON {IdClauses.Join("cs.CharacterId", "c.Id")} " +
            $"WHERE {IdClauses.Equals("cs.SourceId", "sourceId")} AND c.Name = @name AND c.IsDeleted = 0 AND cs.IsDeleted = 0;";

        /// <summary>
        /// Number of active (non-deleted) Quotes still referencing this Character — used by #59's
        /// batch-undo to decide whether reversing a Character Add is safe (no live row still needs
        /// it) or must be skipped (still shared). Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.
        /// </summary>
        internal static readonly string CountActiveReferences =
            $"SELECT COUNT(*) FROM Quotes WHERE {IdClauses.Equals("CharacterId", "id")} AND IsDeleted = 0;";

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

        /// <summary>CharacterSources carries a real FK to Characters(Id) — a stale Character's link rows must be removed before the Character itself is hard-deleted, or the delete violates the FK (same pattern as <see cref="QuoteGenres.DeleteForQuote"/>/<see cref="QuoteTranslations.DeleteForQuote"/>). Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string DeleteForCharacter = $"DELETE FROM CharacterSources WHERE {IdClauses.Equals("CharacterId", "id")};";

        /// <summary>Active (SourceId, SourceTitle) pairs linked to one Character — #185's GetById join. Selects
        /// Title alongside Id since the join through Sources (needed to exclude a soft-deleted Source) already
        /// has it for free, and the response must surface a display name per CLAUDE.md's "Masterdata reference
        /// shape" convention. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectSourceReferencesForCharacter =
            $"SELECT {IdClauses.SelectColumn("s.Id", "Id")}, s.Title FROM CharacterSources cs " +
            $"JOIN Sources s ON {IdClauses.Join("s.Id", "cs.SourceId")} AND s.IsDeleted = 0 " +
            $"WHERE {IdClauses.Equals("cs.CharacterId", "characterId")} AND cs.IsDeleted = 0;";

        /// <summary>
        /// Active (CharacterId, SourceId, SourceTitle) rows for a batch of Characters in a single round-trip —
        /// #185's list join. Dapper expands @characterIds from any IEnumerable&lt;Guid&gt; automatically (same
        /// pattern as RepositorySql.SelectByIds), avoiding one query per row across a page. The IN clause's
        /// column side is LOWER()-wrapped (#210) — the caller-supplied list itself is already pre-canonicalized
        /// via <c>GuidExtensions.ToCanonicalId()</c> (see <c>CharacterSourceLinkReader</c>), but wrapping the
        /// column here means the query stays correct even if a future caller doesn't.
        /// </summary>
        internal static readonly string SelectSourceReferencesForCharacters =
            $"SELECT {IdClauses.SelectColumn("cs.CharacterId", "CharacterId")}, {IdClauses.SelectColumn("s.Id", "SourceId")}, s.Title AS SourceTitle FROM CharacterSources cs " +
            $"JOIN Sources s ON {IdClauses.Join("s.Id", "cs.SourceId")} AND s.IsDeleted = 0 " +
            $"WHERE {IdClauses.In("cs.CharacterId", "characterIds")} AND cs.IsDeleted = 0;";
    }

    /// <summary>People table.</summary>
    internal static class People
    {
        internal const string CountActive = "SELECT COUNT(*) FROM People WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM People;";
        internal static readonly string SelectIdByName = $"SELECT {IdClauses.SelectColumn("Id")} FROM People WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>
        /// #173's id-first lookup for an explicit <c>people[]</c> entry — mirrors
        /// <see cref="Sources.SelectExistingById"/>. Case-insensitive (#180 fix, same reasoning as
        /// <see cref="Sources.SelectExistingById"/>'s remark) — Person shares the identical exposure:
        /// an <c>EntityIdentity</c>-derived (always lowercase) row later referenced by an explicit
        /// <c>people[]</c> entry whose file-authored id casing isn't guaranteed to match.
        /// </summary>
        internal static readonly string SelectExistingById =
            $"SELECT Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM People WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM People WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE People SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>#173's Modify apply — writes an id-matched Person's corrected Name/DateOfBirth/DateOfDeath. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateFieldsById =
            $"UPDATE People SET Name = @name, DateOfBirth = @dateOfBirth, DateOfDeath = @dateOfDeath, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Number of active (non-deleted) Quotes still referencing this Person — see <see cref="Characters.CountActiveReferences"/>'s remark. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string CountActiveReferences =
            $"SELECT COUNT(*) FROM Quotes WHERE {IdClauses.Equals("PersonId", "id")} AND IsDeleted = 0;";

        /// <summary>
        /// See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.
        /// Unlike Character/Universe/Source, Person carries two additional fields known at Add time
        /// (<c>@DateOfBirth</c>/<c>@DateOfDeath</c>) — bound directly rather than hardcoded to NULL.
        /// </summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO People (Id, Name, DateOfBirth, DateOfDeath, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @DateOfBirth, @DateOfDeath, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";
    }

    /// <summary>Sources table.</summary>
    internal static class Sources
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Sources WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Sources;";
        internal static readonly string SelectIdByTitleAndType =
            $"SELECT {IdClauses.SelectColumn("Id")} FROM Sources WHERE Title = @title AND Type = @type AND IsDeleted = 0;";

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
        internal static readonly string SelectExistingByTitleAndType =
            $"SELECT {IdClauses.SelectColumn("Id")}, Date, {IdClauses.SelectColumn("SeriesId")}, CompletenessStatus FROM Sources WHERE Title = @title AND Type = @type AND IsDeleted = 0;";

        /// <summary>
        /// #162's id-first lookup for an explicit <c>sources[]</c> entry — a row already migrated to
        /// the explicit-id model. Distinct from <see cref="SelectIdByTitleAndType"/>'s natural-key
        /// fallback. SeriesId added by #180.
        ///
        /// Case-insensitive (this project's GUID/enum parameter binding rule, CLAUDE.md) — a
        /// file-authored id referencing an already-existing, <c>EntityIdentity</c>-derived row (which
        /// is always stored lowercase) must match regardless of which case the file itself uses.
        /// Found live while authoring #180's curated overlay file: a case mismatch here silently
        /// matches nothing, mirroring the exact class of bug this project has already hit and fixed
        /// piecemeal for GUID-typed query/route parameters — applied here on the same "case-insensitive
        /// by default" principle, not waited for a second live report first.
        /// </summary>
        internal static readonly string SelectExistingById =
            $"SELECT Title, Type, Date, {IdClauses.SelectColumn("SeriesId")}, CompletenessStatus FROM Sources WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM Sources WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE Sources SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>#162's Modify apply — writes an id-matched Source's corrected Title/Type/Date/SeriesId (SeriesId added by #180). Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateFieldsById =
            $"UPDATE Sources SET Title = @title, Type = @type, Date = @date, SeriesId = @seriesId, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>
        /// Number of active (non-deleted) rows still referencing this Source — sums both direct
        /// Quotes and Characters linked via CharacterSources (#179 — a Character may now be linked to
        /// multiple Sources, so this counts links to THIS Source specifically, not all of a
        /// Character's links), since a Character can outlive the specific Quote that introduced it
        /// (see <see cref="Characters.CountActiveReferences"/>'s remark for the reversal use case).
        /// Case-insensitive — see <see cref="SelectExistingById"/>'s remark.
        /// </summary>
        internal static readonly string CountActiveReferences =
            $"SELECT (SELECT COUNT(*) FROM Quotes WHERE {IdClauses.Equals("SourceId", "id")} AND IsDeleted = 0) " +
            $"+ (SELECT COUNT(*) FROM CharacterSources cs JOIN Characters c ON {IdClauses.Join("c.Id", "cs.CharacterId")} " +
            $"   WHERE {IdClauses.Equals("cs.SourceId", "id")} AND cs.IsDeleted = 0 AND c.IsDeleted = 0);";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale. SeriesId (nullable) added by #180, resolved by name at planning time.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Sources (Id, Title, Type, Date, SeriesId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Title, @Type, @Date, @SeriesId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Active Series reference for one Source — #184's GetById join. No row if the Source has
        /// no Series, or its Series has been soft-deleted.</summary>
        internal static readonly string SelectSeriesReferenceForSource =
            $"SELECT {IdClauses.SelectColumn("ser.Id", "Id")}, ser.Name FROM Sources s " +
            $"JOIN Series ser ON {IdClauses.Join("ser.Id", "s.SeriesId")} AND ser.IsDeleted = 0 " +
            $"WHERE {IdClauses.Equals("s.Id", "sourceId")} AND s.IsDeleted = 0;";

        /// <summary>
        /// Active Series references for a batch of Sources in a single round-trip — #184's list join,
        /// avoiding one query per row across a page. A Source with no active Series link is simply absent
        /// from the result. See <see cref="CharacterSources.SelectSourceReferencesForCharacters"/>'s remark
        /// on why the IN clause's column side is LOWER()-wrapped too.
        /// </summary>
        internal static readonly string SelectSeriesReferencesForSources =
            $"SELECT {IdClauses.SelectColumn("s.Id", "SourceId")}, {IdClauses.SelectColumn("ser.Id", "SeriesId")}, ser.Name AS SeriesName FROM Sources s " +
            $"JOIN Series ser ON {IdClauses.Join("ser.Id", "s.SeriesId")} AND ser.IsDeleted = 0 " +
            $"WHERE {IdClauses.In("s.Id", "sourceIds")} AND s.IsDeleted = 0;";
    }

    /// <summary>
    /// Series table (#179 schema, #180 JSON wiring). Add-only from the import path — a Series has
    /// only a Name, so there is no Modify/decidability surface the way Source/Person have.
    /// </summary>
    internal static class Series
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Series WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Series;";
        internal static readonly string SelectIdByName = $"SELECT {IdClauses.SelectColumn("Id")} FROM Series WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale. UniverseId (nullable) is resolved by name at planning time, same as Sources.SeriesId.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Series (Id, Name, UniverseId, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @UniverseId, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Number of active (non-deleted) Sources still referencing this Series — see <see cref="Characters.CountActiveReferences"/>'s remark.</summary>
        internal static readonly string CountActiveReferences =
            $"SELECT COUNT(*) FROM Sources WHERE {IdClauses.Equals("SeriesId", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM Series WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE Series SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Active Universe reference for one Series — #187's GetById join. No row if the Series has no
        /// Universe, or its Universe has been soft-deleted.</summary>
        internal static readonly string SelectUniverseReferenceForSeries =
            $"SELECT {IdClauses.SelectColumn("u.Id", "Id")}, u.Name FROM Series s " +
            $"JOIN Universe u ON {IdClauses.Join("u.Id", "s.UniverseId")} AND u.IsDeleted = 0 " +
            $"WHERE {IdClauses.Equals("s.Id", "seriesId")} AND s.IsDeleted = 0;";

        /// <summary>
        /// Active Universe references for a batch of Series in a single round-trip — #187's list join, avoiding
        /// one query per row across a page. A Series with no active Universe link is simply absent from the result.
        /// See <see cref="CharacterSources.SelectSourceReferencesForCharacters"/>'s remark on why the IN clause's
        /// column side is LOWER()-wrapped too.
        /// </summary>
        internal static readonly string SelectUniverseReferencesForSeries =
            $"SELECT {IdClauses.SelectColumn("s.Id", "SeriesId")}, {IdClauses.SelectColumn("u.Id", "UniverseId")}, u.Name AS UniverseName FROM Series s " +
            $"JOIN Universe u ON {IdClauses.Join("u.Id", "s.UniverseId")} AND u.IsDeleted = 0 " +
            $"WHERE {IdClauses.In("s.Id", "seriesIds")} AND s.IsDeleted = 0;";
    }

    /// <summary>
    /// Universe table (#179 schema, #180 JSON wiring). Add-only from the import path — a Universe has
    /// only a Name, so there is no Modify/decidability surface the way Source/Person have.
    /// </summary>
    internal static class Universe
    {
        internal const string CountActive = "SELECT COUNT(*) FROM Universe WHERE IsDeleted = 0;";
        internal const string DeleteAll   = "DELETE FROM Universe;";
        internal static readonly string SelectIdByName = $"SELECT {IdClauses.SelectColumn("Id")} FROM Universe WHERE Name = @name AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Universe (Id, Name, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@Id, @Name, @ImportBatchId, @DateCreated, NULL, NULL, 0, 'Incomplete', '[]');";

        /// <summary>Number of active (non-deleted) Series still referencing this Universe — see <see cref="Characters.CountActiveReferences"/>'s remark.</summary>
        internal static readonly string CountActiveReferences =
            $"SELECT COUNT(*) FROM Series WHERE {IdClauses.Equals("UniverseId", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM Universe WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="Sources.SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE Universe SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";
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

        /// <summary>Case-insensitive (#210) — see <see cref="SelectForRead"/>'s remark; every id-comparison query in this codebase is case-insensitive by default now (ADR 012), not just the ones with a known-differently-cased caller.</summary>
        internal static readonly string SelectIdById = $"SELECT {IdClauses.SelectColumn("Id")} FROM Conversations WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>
        /// #69: <c>GET /api/v1/conversations/{id}</c>'s own lookup — case-insensitive via
        /// <see cref="IdClauses"/>, same as <see cref="SelectIdById"/> above; both queries are
        /// case-insensitive by default now (ADR 012), whether the id being matched came from a
        /// user-supplied route parameter or from another id already stored in this database.
        /// </summary>
        internal static readonly string SelectForRead =
            $"SELECT {IdClauses.SelectColumn("Id")}, Description FROM Conversations WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO Conversations (Id, Description, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Description, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>
        /// #176's id-first lookup for an explicit <c>conversations[]</c> entry — mirrors
        /// <see cref="Sources.SelectExistingById"/>. Never selects <c>lines</c> — out of scope for
        /// Modify. Case-insensitive via <see cref="IdClauses"/> since #209 — a file-authored explicit
        /// id is canonicalized at capture (<c>ImportActionPlanner</c>), but a row already stored under
        /// a pre-#209 raw casing must still match.
        /// </summary>
        internal static readonly string SelectExistingById =
            $"SELECT Description, CompletenessStatus FROM Conversations WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM Conversations WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE Conversations SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>#176's Modify apply — writes an id-matched Conversation's corrected Description only. Never touches Lines/CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for the latter. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateDescriptionById =
            $"UPDATE Conversations SET Description = @description, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";
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
        internal static readonly string DeleteForConversation = $"DELETE FROM ConversationLines WHERE {IdClauses.Equals("ConversationId", "id")};";

        /// <summary>Clears any lines still pointing at a stale StageDirection before its hard-delete — same rationale as <see cref="DeleteForConversation"/>.</summary>
        internal static readonly string DeleteForStageDirection = $"DELETE FROM ConversationLines WHERE {IdClauses.Equals("StageDirectionId", "id")};";

        /// <summary>Clears any lines still pointing at a stale SoundCue before its hard-delete — same rationale as <see cref="DeleteForConversation"/>.</summary>
        internal static readonly string DeleteForSoundCue = $"DELETE FROM ConversationLines WHERE {IdClauses.Equals("SoundCueId", "id")};";

        internal const string Insert =
            "INSERT OR IGNORE INTO ConversationLines " +
            "(Id, ConversationId, [Order], LineType, QuoteId, StageDirectionId, SoundCueId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @ConversationId, @Order, @LineType, @QuoteId, @StageDirectionId, @SoundCueId, @DateCreated, NULL, NULL, 0);";

        /// <summary>#69: a conversation's full ordered line list. Case-insensitive (#210) — never assume a comparison stays safe just because a known caller already confirmed the parent's existence; see ADR 012.</summary>
        internal static readonly string SelectByConversationId =
            $"SELECT [Order], LineType, {IdClauses.SelectColumn("QuoteId")}, {IdClauses.SelectColumn("StageDirectionId")}, {IdClauses.SelectColumn("SoundCueId")} FROM ConversationLines " +
            $"WHERE {IdClauses.Equals("ConversationId", "conversationId")} AND IsDeleted = 0 ORDER BY [Order] ASC;";

        /// <summary>
        /// #69: every conversation a quote appears in, with its position and the conversation's total
        /// line count — backs the consumer's <c>QuoteResponse.Conversations</c> field (every read
        /// endpoint) and <c>/random</c>'s conversation-selection step. Joins through Conversations for
        /// the same reason <see cref="StageDirections.CountActiveReferences"/> does — IsDeleted only
        /// ever changes on the parent, never on a ConversationLines row itself. Case-insensitive (#210).
        /// The correlated subquery's own <c>cl2.ConversationId = cl.ConversationId</c> match is wrapped
        /// the same as any other id-to-id comparison, per the developer's "wrap joins too" decision.
        /// </summary>
        internal static readonly string SelectMembershipForQuote =
            $"SELECT {IdClauses.SelectColumn("cl.ConversationId", "ConversationId")}, cl.[Order] AS Position, " +
            $"(SELECT COUNT(*) FROM ConversationLines cl2 WHERE {IdClauses.Join("cl2.ConversationId", "cl.ConversationId")} AND cl2.IsDeleted = 0) AS TotalLines " +
            "FROM ConversationLines cl " +
            $"INNER JOIN Conversations c ON {IdClauses.Join("c.Id", "cl.ConversationId")} AND c.IsDeleted = 0 " +
            $"WHERE {IdClauses.Equals("cl.QuoteId", "quoteId")} AND cl.IsDeleted = 0;";

        /// <summary>#69: every QuoteId referenced by a conversation's lines — used by <c>/random</c>'s dedup to exclude every quote in a selected conversation, not only the one that triggered the selection. Case-insensitive (#210).</summary>
        internal static readonly string SelectQuoteIdsForConversation =
            $"SELECT {IdClauses.SelectColumn("QuoteId")} FROM ConversationLines WHERE {IdClauses.Equals("ConversationId", "conversationId")} AND QuoteId IS NOT NULL AND IsDeleted = 0;";

        /// <summary>
        /// Active line counts for a batch of Conversations in a single round-trip — #189's list join, avoiding
        /// one query per row across a page. Uses a correlated <c>COUNT(*)</c> subquery per row — the same
        /// pattern <see cref="SelectMembershipForQuote"/> already uses — rather than a grouping clause, so
        /// this does not trip <c>SqlAggregateGuard</c>'s CVE-2025-6965 heuristic; see docs/sql-safety.md.
        /// A Conversation with zero active lines is simply absent from the result — callers default missing
        /// keys to 0. The IN clause's column side is LOWER()-wrapped (#210), not cosmetic — #68's curated
        /// JSON conversations were seeded with their file-authored ids preserved verbatim (per CLAUDE.md's
        /// case-insensitivity convention: an import file's own explicit id is under no obligation to match
        /// this codebase's canonical casing), and separately, binding a raw <c>Guid</c> list directly (as
        /// <c>@conversationIds</c> originally was) does not reliably go through any casing handler at all —
        /// an exact-case IN match against the unwrapped column silently matched nothing, live-verified
        /// during this issue's own T2 pass. The caller now pre-canonicalizes via
        /// <c>GuidExtensions.ToCanonicalId()</c> before binding (see <c>ConversationLineCountReader</c>),
        /// matching this column's <c>LOWER()</c> wrap.
        /// </summary>
        internal static readonly string SelectLineCountsForConversations =
            $"SELECT DISTINCT {IdClauses.SelectColumn("cl.ConversationId", "ConversationId")}, " +
            $"CAST((SELECT COUNT(*) FROM ConversationLines cl2 WHERE {IdClauses.Join("cl2.ConversationId", "cl.ConversationId")} AND cl2.IsDeleted = 0) AS INTEGER) AS LineCount " +
            "FROM ConversationLines cl " +
            $"WHERE {IdClauses.In("cl.ConversationId", "conversationIds")} AND cl.IsDeleted = 0;";
    }

    /// <summary>StageDirections table (#67/#68). Explicit-id existence check, like <see cref="Conversations"/> — see its remark.</summary>
    internal static class StageDirections
    {
        internal const string DeleteAll = "DELETE FROM StageDirections;";

        /// <summary>Case-insensitive (#210) — see <see cref="Conversations.SelectIdById"/>'s remark.</summary>
        internal static readonly string SelectIdById = $"SELECT {IdClauses.SelectColumn("Id")} FROM StageDirections WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>
        /// #171's id-first lookup for an explicit <c>stageDirections[]</c> entry — mirrors
        /// <see cref="Sources.SelectExistingById"/>. Case-insensitive via <see cref="IdClauses"/>
        /// since #209 — a file-authored explicit id is canonicalized at capture
        /// (<c>ImportActionPlanner</c>), but a row already stored under a pre-#209 raw casing must
        /// still match.
        /// </summary>
        internal static readonly string SelectExistingById =
            $"SELECT Text, ImageUrl, CompletenessStatus FROM StageDirections WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM StageDirections WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE StageDirections SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>#171's Modify apply — writes an id-matched StageDirection's corrected Text/ImageUrl. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateFieldsById =
            $"UPDATE StageDirections SET Text = @text, ImageUrl = @imageUrl, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>
        /// Number of active ConversationLines still referencing this StageDirection — see
        /// <see cref="Characters.CountActiveReferences"/>'s remark for the reversal use case.
        /// Joins through Conversations rather than filtering <c>ConversationLines.IsDeleted</c>
        /// directly — a ConversationLines row is never independently soft-deleted (it's a detail row
        /// of its parent Conversation, same relationship QuoteGenres/QuoteTranslations have to
        /// Quote), so its own IsDeleted flag never reflects whether its parent conversation is still
        /// live; only the parent's IsDeleted does.
        /// </summary>
        internal static readonly string CountActiveReferences =
            "SELECT COUNT(*) FROM ConversationLines cl " +
            $"INNER JOIN Conversations c ON {IdClauses.Join("c.Id", "cl.ConversationId")} " +
            $"WHERE {IdClauses.Equals("cl.StageDirectionId", "id")} AND c.IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO StageDirections (Id, Text, ImageUrl, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Text, @ImageUrl, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>
        /// #69: single-row lookup with optional translation, for embedding inside a conversation's
        /// line list. <c>@id</c> is always an internally-known StageDirectionId (from a
        /// ConversationLines row), never user input directly — but case-insensitive (#210) anyway, per
        /// ADR 012's own point: never assume a comparison stays safe just because today's only caller
        /// happens to supply matching casing. StageDirections has no OriginalLanguage column (unlike
        /// Quotes) — #67's schema never added one, since every bundled stage direction is English;
        /// EffectiveLanguage hardcodes the <c>'en'</c> fallback that <c>Quote.OriginalLanguage</c>
        /// otherwise defaults to. Revisit with a real migration if non-English stage directions are
        /// ever needed.
        /// </summary>
        internal static readonly string SelectByIdWithTranslation =
            $"""
            SELECT {IdClauses.SelectColumn("sd.Id", "Id")}, COALESCE(sdt.Text, sd.Text) AS Text, sd.ImageUrl,
                   CASE WHEN sdt.Text IS NOT NULL THEN @lang ELSE 'en' END AS EffectiveLanguage
            FROM StageDirections sd
            LEFT JOIN StageDirectionTranslations sdt ON {IdClauses.Join("sdt.StageDirectionId", "sd.Id")} AND sdt.Language = @lang AND sdt.IsDeleted = 0
            WHERE {IdClauses.Equals("sd.Id", "id")} AND sd.IsDeleted = 0;
            """;
    }

    /// <summary>StageDirectionTranslations table (#67/#68) — detail rows of a StageDirection, same relationship as <see cref="QuoteTranslations"/> to Quote.</summary>
    internal static class StageDirectionTranslations
    {
        internal const string DeleteAll = "DELETE FROM StageDirectionTranslations;";
        internal static readonly string DeleteForStageDirection = $"DELETE FROM StageDirectionTranslations WHERE {IdClauses.Equals("StageDirectionId", "id")};";

        internal const string Insert =
            "INSERT OR IGNORE INTO StageDirectionTranslations " +
            "(Id, StageDirectionId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @StageDirectionId, @Language, @Text, @DateCreated, NULL, NULL, 0);";
    }

    /// <summary>SoundCues table (#67/#68). Explicit-id existence check, like <see cref="Conversations"/> — see its remark.</summary>
    internal static class SoundCues
    {
        internal const string DeleteAll = "DELETE FROM SoundCues;";

        /// <summary>Case-insensitive (#210) — see <see cref="Conversations.SelectIdById"/>'s remark.</summary>
        internal static readonly string SelectIdById = $"SELECT {IdClauses.SelectColumn("Id")} FROM SoundCues WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>
        /// #172's id-first lookup for an explicit <c>soundCues[]</c> entry — mirrors
        /// <see cref="Sources.SelectExistingById"/>. Case-insensitive via <see cref="IdClauses"/>
        /// since #209 — a file-authored explicit id is canonicalized at capture
        /// (<c>ImportActionPlanner</c>), but a row already stored under a pre-#209 raw casing must
        /// still match.
        /// </summary>
        internal static readonly string SelectExistingById =
            $"SELECT Text, SoundFileUrl, ImageUrl, CompletenessStatus FROM SoundCues WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

        /// <summary>Read before an apply so #165's CompletenessGuard.ComputeNextStatus can see the before-state. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string SelectCompletenessById =
            $"SELECT CompletenessStatus, NoValueKnown FROM SoundCues WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Persists #165's decide-time override or auto-computed transition — the only path allowed to change CompletenessStatus after insert. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateCompletenessById =
            $"UPDATE SoundCues SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>#172's Modify apply — writes an id-matched SoundCue's corrected Text/SoundFileUrl/ImageUrl. Never touches CompletenessStatus/NoValueKnown; see <see cref="UpdateCompletenessById"/> for that. Case-insensitive — see <see cref="SelectExistingById"/>'s remark.</summary>
        internal static readonly string UpdateFieldsById =
            $"UPDATE SoundCues SET Text = @text, SoundFileUrl = @soundFileUrl, ImageUrl = @imageUrl, DateModified = @dateModified WHERE {IdClauses.Equals("Id", "id")};";

        /// <summary>Number of active ConversationLines still referencing this SoundCue — see <see cref="StageDirections.CountActiveReferences"/>'s remark for why this joins through Conversations.</summary>
        internal static readonly string CountActiveReferences =
            "SELECT COUNT(*) FROM ConversationLines cl " +
            $"INNER JOIN Conversations c ON {IdClauses.Join("c.Id", "cl.ConversationId")} " +
            $"WHERE {IdClauses.Equals("cl.SoundCueId", "id")} AND c.IsDeleted = 0;";

        /// <summary>See <see cref="Characters.InsertIfNotExists"/>'s remark — same idempotent-Add rationale.</summary>
        internal const string InsertIfNotExists =
            "INSERT OR IGNORE INTO SoundCues (Id, Text, SoundFileUrl, ImageUrl, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @Text, @SoundFileUrl, @ImageUrl, @ImportBatchId, @DateCreated, NULL, NULL, 0);";

        /// <summary>#69: single-row lookup with optional translation — see <see cref="StageDirections.SelectByIdWithTranslation"/>'s remark (same 'en'-hardcoded rationale, same internal-id-only usage).</summary>
        internal static readonly string SelectByIdWithTranslation =
            $"""
            SELECT {IdClauses.SelectColumn("sc.Id", "Id")}, COALESCE(sct.Text, sc.Text) AS Text, sc.SoundFileUrl, sc.ImageUrl,
                   CASE WHEN sct.Text IS NOT NULL THEN @lang ELSE 'en' END AS EffectiveLanguage
            FROM SoundCues sc
            LEFT JOIN SoundCueTranslations sct ON {IdClauses.Join("sct.SoundCueId", "sc.Id")} AND sct.Language = @lang AND sct.IsDeleted = 0
            WHERE {IdClauses.Equals("sc.Id", "id")} AND sc.IsDeleted = 0;
            """;
    }

    /// <summary>SoundCueTranslations table (#67/#68) — detail rows of a SoundCue, same relationship as <see cref="QuoteTranslations"/> to Quote.</summary>
    internal static class SoundCueTranslations
    {
        internal const string DeleteAll = "DELETE FROM SoundCueTranslations;";
        internal static readonly string DeleteForSoundCue = $"DELETE FROM SoundCueTranslations WHERE {IdClauses.Equals("SoundCueId", "id")};";

        internal const string Insert =
            "INSERT OR IGNORE INTO SoundCueTranslations " +
            "(Id, SoundCueId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted) " +
            "VALUES (@Id, @SoundCueId, @Language, @Text, @DateCreated, NULL, NULL, 0);";
    }
}
