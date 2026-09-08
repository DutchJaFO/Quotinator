using Quotinator.Core.Enums;
using Quotinator.Data.Enums;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Database;

/// <summary>
/// Side-effect-free classifier (#154) — computes exactly what an import/seed run would do, as a
/// list of <see cref="ImportActionEntity"/> rows, without writing to any domain table. Used
/// identically by <c>/import/preview</c>, <c>/import</c>'s staging phase, and the seed flow's own
/// staging step. Existence-checking for Source/Character/Person is always a natural-key DB lookup
/// (never a stable-id-based check — see <see cref="EntityIdentity"/>); a not-yet-existing entity's
/// resolved id is its <see cref="EntityIdentity"/>-derived stable id, used both as the id a later
/// apply step will insert with, and as the (currently unmatched, since nothing has been inserted
/// yet) foreign-key value used when checking whether any Character/Person referencing it already
/// exists — which correctly finds nothing, needing no special deferred-linking mechanism.
/// </summary>
internal static class ImportActionPlanner
{
    /// <summary>
    /// Classifies every row in <paramref name="quotes"/> into <see cref="ImportActionEntity"/>
    /// rows for the Quote itself and any not-yet-existing Source/Character/Person it references,
    /// then (#68) every not-yet-existing <paramref name="stageDirections"/>/<paramref name="soundCues"/>/
    /// <paramref name="conversations"/> row, in that order — a Conversation's lines reference the
    /// other two, and <see cref="Quotinator.Data.Queries.Sql.SystemImportActions.SelectAllForBatch"/>'s insertion-order
    /// guarantee is what lets apply time trust those referenced rows already exist by the time a
    /// Conversation's own action applies, without needing to defensively re-create them the way
    /// Quote/Character do for Source. Read-only against the database — never writes.
    /// </summary>
    internal static async Task<IReadOnlyList<ImportActionEntity>> PlanAsync(
        SqliteConnection connection, IReadOnlyList<SourceQuoteDto> quotes, Guid batchId,
        DuplicateResolutionPolicy policy, SqliteTransaction? transaction = null,
        IReadOnlyList<SourceEntryDto>? sources = null,
        IReadOnlyList<SourceStageDirectionDto>? stageDirections = null,
        IReadOnlyList<SourceSoundCueDto>? soundCues = null,
        IReadOnlyList<SourceConversationDto>? conversations = null,
        IReadOnlyList<PersonEntryDto>? people = null,
        IReadOnlyList<SeriesEntryDto>? series = null,
        IReadOnlyList<UniverseEntryDto>? universe = null,
        IReadOnlyList<CharacterEntryDto>? characters = null,
        ConflictRuleLookup? conflictRules = null,
        SourceAliasLookup? sourceAliases = null,
        IReadOnlyList<SeasonEntryDto>? seasons = null,
        List<RetirableRuleFinding>? retirableRuleFindings = null,
        QuoteExclusionLookup? quoteExclusions = null)
    {
        List<ImportActionEntity> actions = [];
        Dictionary<string, string> sourceIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // #374: date joins a Source's natural key, so more than one row can now share (Title, Type).
        // sourceIndex above still maps a key to a single id, for every consumer that has never needed
        // more than one Source per title (PlanSourcesAsync's own sources[] declarations included) —
        // these two structures are ResolveSourceAsync's own, tracking every dated variant seen so far
        // in this run so a second quote's differing date is never silently matched against the first
        // quote's not-yet-persisted row, and which variant ids have already had an action staged for
        // them this run so a repeat quote for an already-settled variant stages nothing further.
        Dictionary<string, List<SourceVariant>> sourceVariantsByKey = new Dictionary<string, List<SourceVariant>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> stagedSourceVariantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // #374: a quote is unique per Source. Two brand-new quote ids within this same batch can
        // collide on (QuoteText, SourceId) before either has reached the database — tracked here so the
        // second one is still caught, the same way stagedSourceVariantIds catches a same-batch Source
        // collision the database itself cannot yet see.
        HashSet<string> stagedQuoteTextsBySource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> characterIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> personIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> seriesIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> universeIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> seasonIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SourceQuoteDto> seenQuotes = new Dictionary<string, SourceQuoteDto>(StringComparer.Ordinal);
        Dictionary<string, CompletenessStatus> seenQuoteStatus = new Dictionary<string, CompletenessStatus>(StringComparer.Ordinal);
        // #378: a Pending Add is never registered into seenQuotes (its resolution is provisional, not a
        // confirmed same-batch reference — see the comment at its own check below), so two in-file
        // occurrences of the same id that both need review (dateNeedsReview or a Keep/Replace rule
        // matching against nothing) would otherwise each independently see "nothing exists yet" and each
        // stage their own Pending Add — found live via four real `quoteText` "Keep" rules whose two
        // byte-identical raw occurrences both triggered review. Tracked here, separately from seenQuotes,
        // specifically so the second occurrence recognises the first's Pending Add within this same batch,
        // before either has reached a database `SelectHasUnresolvedActionById` could see.
        HashSet<string> stagedPendingReviewAddIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string batchIdStr = batchId.ToCanonicalId();
        DateTime now = DateTime.UtcNow;

        // #180: Universe then Series are planned before Source — a declared series[] entry's own
        // universeName must resolve against an already-built universe index, and a sources[] entry's
        // seriesName must resolve against an already-built series index.
        await PlanUniverseAsync(connection, universe ?? [], batchIdStr, policy, universeIndex, actions, now, transaction, conflictRules, retirableRuleFindings);
        await PlanSeriesAsync(connection, series ?? [], batchIdStr, policy, universeIndex, seriesIndex, actions, now, transaction, conflictRules, retirableRuleFindings);

        // #375: Seasons follow Series for the same reason — a declared seasons[] entry's seriesName must
        // resolve against an already-built series index, and a sources[] entry's seasonNumber against
        // an already-built season index.
        await PlanSeasonsAsync(connection, seasons ?? [], batchIdStr, policy, seriesIndex, seasonIndex, actions, now, transaction, conflictRules, retirableRuleFindings);

        // #162: explicit Source declarations are planned before quotes resolve — a quote may
        // reference a source this same file also declares explicitly, mirroring the existing
        // conversations/stageDirections/soundCues ordering.
        await PlanSourcesAsync(connection, sources ?? [], batchIdStr, policy, sourceIndex, seriesIndex, seasonIndex, actions, now, transaction, conflictRules, retirableRuleFindings);

        // #173: same reasoning as Source above — a quote's author may reference a person this same
        // file also declares explicitly via people[].
        await PlanPeopleAsync(connection, people ?? [], batchIdStr, policy, personIndex, actions, now, transaction);

        // #175: same reasoning as Source/Person above — a quote's character may reference a
        // character this same file also declares explicitly via characters[].
        await PlanCharactersAsync(connection, characters ?? [], batchIdStr, policy, sourceIndex, characterIndex, actions, now, transaction);

        foreach (SourceQuoteDto rawQuote in quotes)
        {
            // #219: an excluded quote produces no action at all — not a decision to make, simply never
            // imported. Checked first, before id canonicalization or anything else, since an excluded
            // quote has nothing further to resolve.
            if (quoteExclusions is not null && quoteExclusions.Contains(rawQuote.Id))
                continue;

            // #210: canonicalize a file-authored Quotes.Id to lowercase at the single earliest point of
            // capture, matching this project's single canonical id convention (ADR 012, GuidHandler).
            // Every later reference to q.Id in this iteration (seenQuotes, EntityId, the resolved
            // SourceQuoteDto threaded through QuoteFieldMerge) is automatically canonical once this
            // substitution is made. SourceQuoteDto is a plain class with init-only properties, not a
            // record, so a corrected copy is built the same way ApplyMergedFields already does above,
            // not via a `with` expression.
            SourceQuoteDto q = EntityIdCanonicalizer.TryCanonicalizeLowercase(rawQuote.Id, out string? canonicalQuoteId)
                ? new SourceQuoteDto
                {
                    Id = canonicalQuoteId!,
                    QuoteText = rawQuote.QuoteText,
                    OriginalLanguage = rawQuote.OriginalLanguage,
                    Source = rawQuote.Source,
                    Date = rawQuote.Date,
                    Character = rawQuote.Character,
                    Author = rawQuote.Author,
                    Type = rawQuote.Type,
                    Genres = rawQuote.Genres,
                    Translations = rawQuote.Translations,
                }
                : rawQuote;

            // #181: a source-title alias, when one matches, substitutes the raw incoming source/type
            // for the already-canonical pair *before* ResolveSourceAsync ever runs — unlike a
            // ConflictResolutionRule (entity-id-keyed, Modify-path only), this must run first: it
            // determines which Source row the quote resolves to at all, not just what a Quote's own
            // field displays in its audit trail. See #181's plan doc, Step 10, for the Zootopia-class
            // bug this closes (a rule that only corrected the displayed field left the quote linked to
            // a spurious, alias-derived Source row).
            bool sourceAliasStale = false;
            if (sourceAliases is not null && sourceAliases.TryResolve(q.Source, q.Type.ToString(), q.Date, out (string CanonicalTitle, string CanonicalType, string? CanonicalDate) canonical))
            {
                // #153: staleness here means "this alias's canonical target was renamed/changed since
                // the alias was authored" — NOT "no Source with this exact title exists yet." Those
                // are different: an alias's own normal, legitimate job includes guiding the *first-ever*
                // creation of a Source under its correct canonical name (e.g. a brand-new database, or
                // this being the first bundled file to ever mention the title) — in that case nothing
                // with the canonical title exists yet by design, and that must never be flagged stale.
                // A naive "does a Source with this exact (title, type) exist right now" check cannot
                // tell these two cases apart, since both look identical (nothing found) — found live
                // via Docker T2 (#153): a first pass at this used exactly that check and produced 7
                // false positives against real bundled data, every one of them a legitimate first-time
                // alias-guided creation, not an actual rename.
                //
                // The reliable signal is the Source's own id, not its title: EntityIdentity.SourceId is
                // a deterministic hash of (title, type) fixed at creation and never recomputed on a
                // later Modify (only the row's Title/Type columns change; Id does not) — so the id a
                // Source would have gotten if created under exactly this canonical (title, type) pair
                // is knowable up front, before the row necessarily exists. If a row with that id exists
                // but its *current* Title/Type has since drifted away from the alias's own recorded
                // canonical pair, a rename genuinely happened and the alias is stale. If no row with
                // that id exists at all, nothing has been renamed — this is simply the first time this
                // canonical Source is being introduced, exactly what the alias exists to guide.
                // #374: a dated alias (CanonicalDate set) targets a specific second-or-later variant —
                // the same reason a date was needed to disambiguate the raw side in the first place — so
                // the 3-arg id form is the correct match for ResolveSourceAsync's own creation
                // convention. A date-less alias keeps the original 2-arg form unchanged.
                string canonicalId = canonical.CanonicalDate is null
                    ? EntityIdentity.SourceId(canonical.CanonicalTitle, canonical.CanonicalType)
                    : EntityIdentity.SourceId(canonical.CanonicalTitle, canonical.CanonicalType, canonical.CanonicalDate);
                (string Title, string Type, bool Found, string? IndexedId) canonicalRow = sourceIndex.TryGetValue($"{canonical.CanonicalTitle}|{canonical.CanonicalType}", out string? indexedId)
                    ? (Title: canonical.CanonicalTitle, Type: canonical.CanonicalType, Found: true, IndexedId: (string?)indexedId)
                    : await connection.QuerySingleOrDefaultAsync<(string Title, string Type, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                        Sql.Sources.SelectExistingById, new { id = canonicalId }, transaction) is { } row
                        ? (row.Title, row.Type, Found: true, IndexedId: canonicalId)
                        : (Title: canonical.CanonicalTitle, Type: canonical.CanonicalType, Found: false, IndexedId: (string?)null);

                // Not found at all → first-time creation, not stale. Found (by id) → stale only if the
                // live row's own Title/Type has drifted from what the alias itself claims is canonical.
                bool canonicalSourceExists = !canonicalRow.Found
                    || (string.Equals(canonicalRow.Title, canonical.CanonicalTitle, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(canonicalRow.Type, canonical.CanonicalType, StringComparison.OrdinalIgnoreCase));

                if (canonicalSourceExists)
                {
                    q = new SourceQuoteDto
                    {
                        Id = q.Id,
                        QuoteText = q.QuoteText,
                        OriginalLanguage = q.OriginalLanguage,
                        Source = canonical.CanonicalTitle,
                        Date = canonical.CanonicalDate ?? q.Date,
                        Character = q.Character,
                        Author = q.Author,
                        Type = QuoteSeedWriter.ParseQuoteType(canonical.CanonicalType),
                        Genres = q.Genres,
                        Translations = q.Translations,
                    };
                }
                else
                {
                    sourceAliasStale = true;
                }
            }

            // #378: existing must be known before Source/Character/Person resolution runs — a matching
            // rule's Keep/Replace decision needs the true existing value to correct q's own field before
            // ResolveSourceAsync ever sees it, the same reason Custom already needed to run early (#153,
            // below). Moved from its original position (after Source/Character/Person resolution) — this
            // lookup is a pure read by q.Id and does not depend on anything Source/Character/Person
            // resolution computes.
            QuoteSeedWriter.ExistingQuoteFields? existing = seenQuotes.TryGetValue(q.Id, out SourceQuoteDto? firstInFile)
                ? new QuoteSeedWriter.ExistingQuoteFields(QuoteFieldMerge.ToFieldMap(firstInFile), batchIdStr, seenQuoteStatus.GetValueOrDefault(q.Id, CompletenessStatus.Incomplete))
                : await QuoteSeedWriter.TryGetExistingFieldsAsync(connection, q.Id, transaction);

            // #153/#378: a matching ConflictResolutionRule's decided field applies to q itself, before
            // Source/Character/Person resolution ever runs — not just on a later Modify. Without this, a
            // rule correcting e.g. "character" would only ever take effect if this exact quote id is
            // re-encountered (in-file or cross-file) after its first Add, since the Modify branch below
            // is the only other place a rule is consulted; a quote appearing exactly once anywhere in the
            // bundled corpus would never see its correction at all. Running this before resolution also
            // means Character/Person/Source resolve against the corrected value, not the raw one — the
            // same reasoning as the SourceAliasRule substitution just above, and (#378) the reason
            // Keep/Replace must run here too now that existing is available: ResolveSourceAsync
            // resolves/creates a Source variant per *raw* incoming date, independently of what the
            // field-merge later decides, so a Keep that only takes effect afterward leaves the quote
            // linked to whichever variant its own raw date happened to match — and, for date
            // specifically, creates a second, spurious Source variant for the date Keep rejects, which
            // nothing ever cleans up even though no quote ends up referencing it. A brand-new quote
            // (existing is null) is not a reason to exclude Keep/Replace: "no existing value" is a real,
            // well-defined `null` on the existing side, not an absence of meaning — Keep(null) resolves
            // the field to null (a deliberate, if possibly-accidental-looking, curator choice) and
            // Replace(null) resolves to whatever the incoming side already says (a harmless no-op). Both
            // are genuine outcomes `FieldMergeResolver.ResolveWithDecisions` already defines correctly for
            // a null existing side; there is no reason to special-case them out here.
            // #378 (found live against the real corpus, 2026-09-08): a Keep/Replace rule that matches
            // while nothing exists yet is not silently applied (Keep would resolve to null — a possible
            // authoring mistake, e.g. a role-reversed or vestigial recorded snapshot, exactly what four
            // real `quoteText` "Keep" rules whose existingRecord/incomingRecord are already identical
            // turned out to be) and not silently ignored either — it holds the Add for review, the same
            // way an unresolved Source date already does, so a curator confirms whether it is a genuine
            // fix or a mistake before anything is written.
            bool keepOrReplaceAgainstNothing = false;
            if (conflictRules is not null && !sourceAliasStale)
            {
                IReadOnlyDictionary<string, object?> rawFields = QuoteFieldMerge.ToFieldMap(q);
                IReadOnlyDictionary<string, object?>? existingFieldsForEarlyRule = existing?.Fields;
                Dictionary<string, FieldMergeDecision>? earlyDecisions = null;
                foreach (string field in rawFields.Keys)
                {
                    // Null (not rawFields[field]) when nothing exists yet — a genuine "no value", not a
                    // stand-in for the incoming value. This is what lets Keep resolve to null instead of
                    // silently matching whatever the incoming side happens to say.
                    object? earlyExistingValue = existingFieldsForEarlyRule?.GetValueOrDefault(field);

                    // #153: a field already equal on both sides needs no decision — consulting the rule
                    // anyway would compare its recorded snapshot against an already-agreeing pair and
                    // spuriously flag it Stale/Retirable. Only meaningful once existing is known; a
                    // brand-new quote has no "already equal" to detect.
                    if (existingFieldsForEarlyRule is not null
                        && FieldMergeResolver.ValuesEqual(field, earlyExistingValue, rawFields[field], QuoteFieldMerge.CaseSensitiveContentFields))
                        continue;

                    if (conflictRules.TryResolve(q.Id, field, earlyExistingValue, rawFields[field], out FieldMergeDecision decision, out ConflictRuleOutcome outcome)
                        && outcome is ConflictRuleOutcome.Apply or ConflictRuleOutcome.AlreadyApplied)
                    {
                        if (existingFieldsForEarlyRule is not null || decision.Choice == FieldResolutionChoice.Custom)
                            (earlyDecisions ??= new Dictionary<string, FieldMergeDecision>(StringComparer.OrdinalIgnoreCase))[field] = decision;
                        else
                            keepOrReplaceAgainstNothing = true;
                    }
                }

                if (earlyDecisions is { Count: > 0 })
                {
                    // ApplyMergedFields indexes every field unconditionally, so ResolveWithDecisions
                    // must be given the full field set — but only the decided fields' "existing" side
                    // is the true existing value (null when nothing exists yet); every other field uses
                    // the same value on both sides (trivially not ambiguous, auto-resolves straight
                    // through) so an unrelated field genuinely ambiguous between existing and incoming
                    // (no matching rule at all, e.g. "character") never throws here — that stays the
                    // later Modify branch's own job.
                    Dictionary<string, object?> blendedExisting = new(StringComparer.OrdinalIgnoreCase);
                    foreach (string field in rawFields.Keys)
                        blendedExisting[field] = earlyDecisions.ContainsKey(field)
                            ? existingFieldsForEarlyRule?.GetValueOrDefault(field)
                            : rawFields[field];

                    FieldMergeResult corrected = FieldMergeResolver.ResolveWithDecisions(
                        blendedExisting, rawFields, earlyDecisions, QuoteFieldMerge.CaseSensitiveContentFields);
                    q = QuoteFieldMerge.ApplyMergedFields(corrected.MergedFields, q);
                }
            }

            (string sourceId, bool dateNeedsReview) = await ResolveSourceAsync(connection, q, sourceIndex, sourceVariantsByKey, stagedSourceVariantIds, batchIdStr, actions, now, transaction, policy);
            string? characterId = await ResolveCharacterAsync(connection, q, sourceId, characterIndex, batchIdStr, actions, now, transaction);
            string? personId = await ResolvePersonAsync(connection, q, personIndex, batchIdStr, actions, now, transaction);

            if (existing is null)
            {
                // #374: neither a Pending nor a Blocked action is ever applied, so a quote reported for
                // either reason on an earlier reseed still has no row in Quotinator_Quote and would
                // otherwise look "never-before-seen" again on every later reseed — staging a fresh
                // duplicate action on top of the still-unresolved one and growing without bound. Checked
                // first, so an already-reported conflict is recognised before any of the branches below
                // run. Found live: originally gated on dateNeedsReview alone (the mechanism this check
                // was first written for), which left the Blocked collision path below completely
                // unguarded — a real reseed duplicated every Blocked collision action on top of the
                // still-unresolved one from the previous reseed.
                if (await connection.ExecuteScalarAsync<int>(Sql.Quotes.SelectHasUnresolvedActionById, new { id = q.Id }, transaction) > 0)
                    continue;

                // #378: the same accumulation-prevention reasoning as the check just above, extended to
                // this same batch's own earlier occurrences — the database check above can never see a
                // Pending Add this same PlanAsync call staged moments ago, since staging happens only
                // after planning finishes.
                if ((dateNeedsReview || keepOrReplaceAgainstNothing) && !stagedPendingReviewAddIds.Add(q.Id))
                    continue;

                QuoteActionPayloadDto payload = new QuoteActionPayloadDto
                {
                    Fields = QuoteFieldMerge.ToDto(q),
                    SourceId = sourceId,
                    CharacterId = characterId,
                    PersonId = personId,
                };

                // #374: this id has never been seen, but its own (QuoteText, SourceId) may already
                // belong to a different quote — two independently-computed QuoteIdentity.StableId
                // values can collide on content alone (most often after an alias resolves two
                // originally-different Source titles onto one canonical row). Checked against both this
                // same batch's own staged Adds (not yet in the database) and the database itself, but
                // only when this quote isn't already headed for review via a stale alias or an unresolved
                // Source date — either already holds the action, and stacking a second, unrelated review
                // reason on top would only obscure which one actually needs the curator's attention.
                bool collidesWithinBatch = false;
                string? collidingExistingId = null;
                if (!sourceAliasStale && !dateNeedsReview && !keepOrReplaceAgainstNothing)
                {
                    string quoteTextKey = $"{sourceId}|{q.QuoteText}";
                    collidesWithinBatch = !stagedQuoteTextsBySource.Add(quoteTextKey);
                    if (!collidesWithinBatch)
                        collidingExistingId = await connection.QuerySingleOrDefaultAsync<string?>(
                            Sql.Quotes.SelectExistingIdByTextAndSource, new { quoteText = q.QuoteText, sourceId }, transaction);
                }

                if (collidesWithinBatch || collidingExistingId is not null)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchIdStr,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                        EntityType = ImportActionEntityTypes.Quote,
                        EntityId = q.Id,
                        ExistingValue = collidingExistingId is not null ? JsonSerializer.Serialize(new { conflictingQuoteId = collidingExistingId }) : null,
                        IncomingValue = JsonSerializer.Serialize(payload),
                        AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153/#374: a stale alias substitution or an unresolved Source date leaves this quote's
                // Source resolution provisional — never registered as a confirmed same-batch reference
                // (matching how a Pending Modify is never registered either), so a later quote in this
                // same batch referencing this id re-checks against real DB state rather than this
                // unconfirmed one.
                if (!sourceAliasStale && !dateNeedsReview && !keepOrReplaceAgainstNothing)
                {
                    seenQuotes[q.Id] = q;
                    seenQuoteStatus[q.Id] = CompletenessStatus.Incomplete;
                }
                ImportActionStatus addStatus = dateNeedsReview || keepOrReplaceAgainstNothing ? ImportActionStatus.Pending
                    : sourceAliasStale ? ImportActionStatus.Stale
                    : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchIdStr,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                    EntityType = ImportActionEntityTypes.Quote,
                    EntityId = q.Id,
                    IncomingValue = JsonSerializer.Serialize(payload),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(addStatus.ToString(), addStatus),
                    DetectedAt = now,
                });
                continue;
            }

            IReadOnlyDictionary<string, object?> existingFields = existing.Value.Fields;
            string? existingBatchId = existing.Value.ImportBatchId;
            IReadOnlyDictionary<string, object?> incomingFields = QuoteFieldMerge.ToFieldMap(q);

            // #374: the same accumulation-prevention check the Add branch above already makes, extended
            // to Modify. Before the quote/title/character case-sensitivity rule, a Modify's own ambiguous
            // fields either auto-resolved or (rarely) blocked a Complete row — nothing stayed genuinely
            // Pending release over release the way an Add-branch tv-date conflict could. Once a case-only
            // difference became its own permanent ambiguity, that stopped being true: a quote already
            // reported this way is never applied, so the *stored* row a later reseed compares against
            // never changes, and every reseed re-staged a fresh duplicate Pending action on top of the
            // one still awaiting review. Found live: a single already-known conflict, left unresolved,
            // made the staged count grow 4 → 19 in one reseed once the real corpus's other case-only
            // disagreements were counted too. Skipped only for a same-batch collision
            // (`existingBatchId == batchIdStr`, via `seenQuotes` above) — nothing can be in
            // `Import_Action` yet for a row this same `PlanAsync` call hasn't finished staging.
            if (existingBatchId != batchIdStr
                && await connection.ExecuteScalarAsync<int>(Sql.Quotes.SelectHasUnresolvedActionById, new { id = q.Id }, transaction) > 0)
                continue;

            bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
            FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy, QuoteFieldMerge.CaseSensitiveContentFields) : null;
            // Skip's resolved payload is the existing row's own values (nothing changes) — not the
            // incoming row's, which is what "resolved" would otherwise default to. The applier's Quote
            // case checks AppliedPolicy==Skip and skips the write/changelog entirely regardless, but
            // storing the accurate "nothing changes" payload here keeps GET /import/actions honest.
            SourceQuoteDto resolved = policy switch
            {
                DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs => QuoteFieldMerge.ApplyMergedFields(mergeResult!.MergedFields, q),
                DuplicateResolutionPolicy.Skip => QuoteFieldMerge.ApplyMergedFields(existingFields, q),
                _ => q,
            };

            // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved), not
            // the raw incoming value — Skip's resolved value always equals existingFields (nothing
            // written), so Skip can never block a Complete quote; a merge policy only blocks on
            // fields the merge itself would actually change.
            IReadOnlyDictionary<string, object?> resolvedFields = QuoteFieldMerge.ToFieldMap(resolved);
            HashSet<string> effectiveChanged = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Key, kv.Value, resolvedFields.GetValueOrDefault(kv.Key), QuoteFieldMerge.CaseSensitiveContentFields)).Select(kv => kv.Key)];

            // #373: the row arrived and already matches. Reported as Unchanged rather than Modify,
            // which claims a write that never happens, and rather than nothing at all, which leaves a
            // reader unable to tell it from a row the file never mentioned.
            //
            // Compared incoming-against-stored, NOT via effectiveChanged. Those differ under Skip:
            // `resolved` is deliberately set to the existing values there, so effectiveChanged is
            // empty for every Skip whether or not the content actually matches. This issue's plan
            // named effectiveChanged and was wrong about it — the question is whether the file agrees
            // with the database, which no policy changes the answer to.
            bool contentIsIdentical = existingFields.Keys
                .Union(incomingFields.Keys)
                .All(field => FieldMergeResolver.ValuesEqual(
                    field, existingFields.GetValueOrDefault(field), incomingFields.GetValueOrDefault(field), QuoteFieldMerge.CaseSensitiveContentFields));

            if (contentIsIdentical)
            {
                actions.Add(new ImportActionEntity
                {
                    BatchId = batchIdStr,
                    ExistingBatchId = existingBatchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Unchanged.ToString(), ImportActionKind.Unchanged),
                    EntityType = ImportActionEntityTypes.Quote,
                    EntityId = q.Id,
                    ExistingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    IncomingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    // Terminal: nothing to decide, nothing to write, nothing to reverse.
                    Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Applied.ToString(), ImportActionStatus.Applied),
                    DetectedAt = now,
                });
                continue;
            }

            if (CompletenessGuard.ShouldBlock(existing.Value.CompletenessStatus, effectiveChanged))
            {
                actions.Add(new ImportActionEntity
                {
                    BatchId = batchIdStr,
                    ExistingBatchId = existingBatchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Quote,
                    EntityId = q.Id,
                    ExistingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    IncomingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                    DetectedAt = now,
                });
                continue;
            }

            // #181: a matching per-source rule auto-resolves a field instead of leaving it Pending for
            // a human — either an otherwise-ambiguous field, or (via Custom) a field that's simply
            // wrong/missing on both sides and needs a value neither side actually has. Only relevant
            // under Review — every other policy already resolves deterministically without one. A rule
            // never bypasses CompletenessGuard above — a Complete row still blocks regardless of
            // whether a rule could have resolved the change.
            FieldMergeResult? ruleResolved = null;
            if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
            {
                Dictionary<string, FieldMergeDecision> ruleDecisions = [];
                bool hasStaleRule = false;
                foreach (string field in existingFields.Keys)
                {
                    // #153: a field already equal on both sides needs no decision at all (matching
                    // FieldMergeResolver's own "equal values keep existing" auto-resolution) — most
                    // often true here for a field the Add-branch's own early Custom-rule application
                    // just above already corrected identically on every in-file occurrence. Consulting
                    // the rule anyway would compare its recorded snapshot (describing the field's
                    // pre-correction shape) against the now-already-corrected value and spuriously
                    // flag it stale, blocking the whole action for a field that needed no resolution.
                    if (FieldMergeResolver.ValuesEqual(field, existingFields[field], incomingFields.GetValueOrDefault(field), QuoteFieldMerge.CaseSensitiveContentFields))
                        continue;

                    if (!conflictRules.TryResolve(q.Id, field, existingFields[field], incomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                        continue;
                    bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                    if (isStale) hasStaleRule = true;
                    else ruleDecisions[field] = decision;
                    if (outcome == ConflictRuleOutcome.Retirable)
                        retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Quote, q.Id, field));
                }

                // #153: a stale rule holds the whole action for review, the same way a Blocked action
                // does above — never silently reapplied, and never mixed with a partial auto-resolve
                // of the action's other fields.
                if (hasStaleRule)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchIdStr,
                        ExistingBatchId = existingBatchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Quote,
                        EntityId = q.Id,
                        ExistingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                        IncomingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Stale.ToString(), ImportActionStatus.Stale),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153: always attempt the resolve, even with zero rule decisions — a record with every
                // field already equal (nothing ambiguous at all, e.g. because the Add-branch's own
                // early Custom-rule application already made both sides agree) must still resolve
                // instead of falling through to Pending. Gating this on ruleDecisions.Count > 0 was a
                // pre-existing bug masked by every existing rule also recording a redundant decision for
                // an already-equal field — removing that redundancy (the ValuesEqual skip above) is
                // what surfaced it. ResolveWithDecisions itself already handles zero decisions safely:
                // equal fields auto-resolve, and only a genuinely ambiguous field with no decision throws.
                try
                {
                    ruleResolved = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions, QuoteFieldMerge.CaseSensitiveContentFields);
                }
                catch (UnresolvedFieldConflictException)
                {
                    // Not every ambiguous field has a matching rule — fall through to normal Pending staging.
                }
            }

            if (ruleResolved is not null)
                resolved = QuoteFieldMerge.ApplyMergedFields(ruleResolved.MergedFields, q);

            // Review is the only policy left Pending; every other policy is Decided at detection
            // time, with the final resolved values already computed so apply never needs policy logic.
            // A fully rule-resolved Review action is also Decided immediately — nothing is left for a
            // human to decide. #153: a stale alias substitution overrides all of that — this quote's
            // Source resolution is unreliable regardless of how cleanly its fields would otherwise
            // have resolved, so it is held for review the same way a stale rule already is above.
            bool isPending = policy == DuplicateResolutionPolicy.Review && ruleResolved is null;
            ImportActionStatus status = sourceAliasStale ? ImportActionStatus.Stale : isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;
            bool isUnresolved = isPending || sourceAliasStale;

            // #378: `sourceId` was resolved against q's own raw incoming date, before this field-merge
            // decision was known — correct for Replace (which keeps the incoming date) and for Custom
            // (already folded into q before ResolveSourceAsync ever ran), but wrong the moment Keep
            // settles on a *different* date than the one Source resolution saw. Re-picking from the same
            // variant cache ResolveSourceAsync already populated for this title is safe unconditionally:
            // when resolved.Date matches what ResolveSourceAsync already saw, this finds the same variant
            // right back. A resolved date with no matching variant (should not happen for Keep/Replace —
            // both only ever produce a value some occurrence already established a variant for) falls
            // back to the original sourceId rather than risk misassigning to nothing.
            string effectiveSourceId = isUnresolved
                ? sourceId
                : ReresolveSourceIdForDecidedDate(sourceId, resolved.Date, q.Source, q.Type.ToString(), sourceVariantsByKey);

            actions.Add(new ImportActionEntity
            {
                BatchId = batchIdStr,
                ExistingBatchId = existingBatchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                EntityType = ImportActionEntityTypes.Quote,
                EntityId = q.Id,
                ExistingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                IncomingValue = JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                MergedFields = isUnresolved ? null : JsonSerializer.Serialize(new QuoteActionPayloadDto { Fields = QuoteFieldMerge.ToDto(resolved), SourceId = effectiveSourceId, CharacterId = characterId, PersonId = personId }),
                AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                DetectedAt = now,
            });

            if (!isUnresolved)
            {
                seenQuotes[q.Id] = resolved;
                seenQuoteStatus[q.Id] = existing.Value.CompletenessStatus;
            }
        }

        await PlanStageDirectionsAsync(connection, stageDirections ?? [], batchIdStr, policy, actions, now, transaction);
        await PlanSoundCuesAsync(connection, soundCues ?? [], batchIdStr, policy, actions, now, transaction);
        await PlanConversationsAsync(connection, conversations ?? [], batchIdStr, policy, actions, now, transaction);

        return actions;
    }

    /// <summary>
    /// #373: one action recording that an entity arrived and already matched what is stored.
    /// <para>
    /// Terminal by construction — <c>Applied</c>, with no <c>ExistingValue</c>, because there is
    /// nothing to decide, write or reverse and the two sides are the same value. Shared so every
    /// resolver reports it identically; a per-resolver copy is how <c>Series</c> and <c>Universe</c>
    /// would quietly end up shaped differently from <c>Source</c>.
    /// </para>
    /// </summary>
    /// <param name="batchId">The batch this planning pass belongs to.</param>
    /// <param name="entityType">Which entity type arrived unchanged.</param>
    /// <param name="entityId">The id of the row it matched.</param>
    /// <param name="payload">The incoming value, serialised as the action's own record of what arrived.</param>
    /// <param name="now">Detection timestamp for the action.</param>
    private static ImportActionEntity UnchangedAction(
        string batchId, string entityType, string entityId, object payload, DateTime now) => new()
    {
        BatchId = batchId,
        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Unchanged.ToString(), ImportActionKind.Unchanged),
        EntityType = entityType,
        EntityId = entityId,
        IncomingValue = JsonSerializer.Serialize(payload, payload.GetType()),
        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Applied.ToString(), ImportActionStatus.Applied),
        DetectedAt = now,
    };

    /// <summary>
    /// #374: one Source row matching a (Title, Type) pair — since Date joined the natural key, more
    /// than one of these can share a title, distinguished by <see cref="Date"/>.
    /// </summary>
    private readonly record struct SourceVariant(string Id, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus);

    /// <summary>
    /// #374: picks which of an already-seen title's <paramref name="variants"/> a quote claiming
    /// <paramref name="incomingDate"/> belongs to, in priority order: (1) an exact date match, treating
    /// two date-less rows as matching each other; (2) if the incoming side states a date and no exact
    /// match exists, a date-less row to backfill — preserving the pre-#374 "we didn't know the date
    /// yet" behaviour; (3) if the incoming side states no date at all, the nearest existing variant
    /// (the first one seen) rather than manufacturing a needless duplicate for a quote with nothing to
    /// disagree about. Returns <see langword="null"/> only when none of these apply — meaning a new,
    /// distinctly-dated variant is genuinely needed.
    /// </summary>
    private static SourceVariant? PickSourceVariant(IReadOnlyList<SourceVariant> variants, string? incomingDate)
    {
        foreach (SourceVariant variant in variants)
            if (FieldMergeResolver.ValuesEqual(variant.Date, incomingDate))
                return variant;

        if (incomingDate is not null)
        {
            foreach (SourceVariant variant in variants)
                if (variant.Date is null)
                    return variant;
            return null;
        }

        return variants.Count > 0 ? variants[0] : null;
    }

    /// <summary>
    /// #378: a quote's own Source is resolved against its raw incoming date before any field-merge
    /// decision for that quote's own <c>date</c> field is known — correct for Replace (which keeps the
    /// incoming value the resolution already saw) and for Custom (folded into the quote before
    /// <see cref="ResolveSourceAsync"/> ever ran), but wrong the moment Keep settles on a *different*
    /// date. Re-picks from <paramref name="variantsByKey"/> — the same cache <see cref="ResolveSourceAsync"/>
    /// already populated for <paramref name="sourceTitle"/>/<paramref name="typeStr"/> — using
    /// <paramref name="decidedDate"/> instead of the quote's raw date. Safe to call unconditionally: when
    /// <paramref name="decidedDate"/> already matches what <see cref="ResolveSourceAsync"/> saw, this
    /// finds the same variant right back. Falls back to <paramref name="originalSourceId"/> when no
    /// variant matches at all (should not happen for Keep/Replace — both only ever produce a value some
    /// occurrence already established a variant for) rather than risk misassigning to nothing.
    /// </summary>
    private static string ReresolveSourceIdForDecidedDate(
        string originalSourceId, string? decidedDate, string sourceTitle, string typeStr,
        Dictionary<string, List<SourceVariant>> variantsByKey)
    {
        if (!variantsByKey.TryGetValue($"{sourceTitle}|{typeStr}", out List<SourceVariant>? variants))
            return originalSourceId;

        return PickSourceVariant(variants, decidedDate)?.Id ?? originalSourceId;
    }

    /// <summary>
    /// #374: the result of resolving a quote's Source. <paramref name="DateNeedsReview"/> is
    /// <see langword="true"/> exactly when <see cref="ResolveSourceAsync"/> attached the quote to
    /// <see cref="SourceId"/> *despite* a date disagreement it could not safely resolve on its own —
    /// see that method's own remarks for why this is a reported conflict, not a silent choice.
    /// </summary>
    private readonly record struct SourceResolution(string SourceId, bool DateNeedsReview);

    /// <summary>
    /// #374: types capable of having a Series/Season structure (ADR 011) — currently just `tv`, matching
    /// where the ambiguity below was actually found (Arrow, Mr. Robot). Not `Anime`: no bundled evidence
    /// yet that its quotes carry the same kind of per-quote wrong-year noise; extend here if that
    /// changes, rather than guessing ahead of evidence.
    /// </summary>
    private static bool IsSeriesCapable(string typeStr) => string.Equals(typeStr, nameof(QuoteType.Tv), StringComparison.OrdinalIgnoreCase);

    private static async Task<SourceResolution> ResolveSourceAsync(
        SqliteConnection connection, SourceQuoteDto q, Dictionary<string, string> index,
        Dictionary<string, List<SourceVariant>> variantsByKey, HashSet<string> stagedVariantIds,
        string batchId, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction,
        DuplicateResolutionPolicy policy = DuplicateResolutionPolicy.Review)
    {
        string typeStr = q.Type.ToString();
        string key = $"{q.Source}|{typeStr}";

        // #162/#374: raw string ids throughout, not Guid?-typed — a natural-key-matched row's id may be
        // an explicit, not-necessarily-canonically-cased file-authored id (from a sources[] entry), not
        // only a Guid.NewGuid()/EntityIdentity-derived one. Guid has no memory of original string
        // casing — ToString("D") always renders lowercase regardless of what was actually stored — so
        // round-tripping through Guid? and re-casing would silently produce a string that no longer
        // matches the real row's id if that row predates a casing-convention change (this project has
        // been through two: see ADR 012's revision history).
        if (!variantsByKey.TryGetValue(key, out List<SourceVariant>? variants))
        {
            // #245: SelectAllExistingByTitleAndType (not the narrower id-only SelectIdByTitleAndType) so
            // a Source first created date-less via a sources[] entry (#162/#180) can be backfilled here
            // once a later quote supplies a Date — #191's own fix only ever populates Date on a
            // brand-new Add below, never on a row that already exists. #374: every matching row, not
            // just one — every dated variant of this title needs to be seen at once so a second,
            // differently-dated quote is never matched against the wrong one.
            IEnumerable<SourceVariant> existingRows = await connection.QueryAsync<SourceVariant>(
                Sql.Sources.SelectAllExistingByTitleAndType, new { title = q.Source, type = typeStr }, transaction);
            variants = [.. existingRows];
            variantsByKey[key] = variants;
        }

        SourceVariant? picked = PickSourceVariant(variants, q.Date);

        // #374: `index` is shared with PlanSourcesAsync's own sources[] resolution (and, historically,
        // was ResolveSourceAsync's own single-value cache). A key already resolved there — a curated
        // declaration, not yet reflected in the database — must never be treated as "no variant found"
        // and duplicated; adopt it as a known variant instead.
        if (picked is null && index.TryGetValue(key, out string? preResolvedId) && !variants.Exists(v => v.Id == preResolvedId))
        {
            SourceVariant preResolved = new(preResolvedId, q.Date, null, null, SafeValue<CompletenessStatus?>.Empty);
            variants.Add(preResolved);
            stagedVariantIds.Add(preResolvedId);
            return new SourceResolution(preResolvedId, DateNeedsReview: false);
        }

        if (picked is { } row)
        {
            // #374: a variant already settled earlier in this same batch (staged once, whether Add,
            // Modify, Blocked or Unchanged) must not be re-evaluated for every later quote referencing
            // it — this is what index's own pre-#374 single-entry cache achieved for the single-variant
            // case, extended here to hold per-variant, not just per-key.
            if (stagedVariantIds.Add(row.Id))
            {
                if (row.Date is null && q.Date is not null)
                {
                    SourceActionPayloadDto existingPayload = new SourceActionPayloadDto(q.Source, typeStr, row.Date, row.SeriesId, row.SeasonId);
                    SourceActionPayloadDto incomingPayload = new SourceActionPayloadDto(q.Source, typeStr, q.Date, row.SeriesId, row.SeasonId);
                    HashSet<string> changedFields = ["date"];
                    CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;

                    actions.Add(CompletenessGuard.ShouldBlock(currentStatus, changedFields)
                        ? new ImportActionEntity
                        {
                            BatchId = batchId,
                            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                            EntityType = ImportActionEntityTypes.Source,
                            EntityId = row.Id,
                            ExistingValue = JsonSerializer.Serialize(existingPayload),
                            IncomingValue = JsonSerializer.Serialize(incomingPayload),
                            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                            DetectedAt = now,
                        }
                        : new ImportActionEntity
                        {
                            BatchId = batchId,
                            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                            EntityType = ImportActionEntityTypes.Source,
                            EntityId = row.Id,
                            ExistingValue = JsonSerializer.Serialize(existingPayload),
                            IncomingValue = JsonSerializer.Serialize(incomingPayload),
                            MergedFields = JsonSerializer.Serialize(incomingPayload),
                            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                            DetectedAt = now,
                        });

                    // #374: the backfilled date is now this variant's real date — a later quote in this
                    // same batch that also carries this date must match it directly, not re-trigger a
                    // second backfill of what is, from that quote's perspective, an already-dated row.
                    int variantIndex = variants.FindIndex(v => v.Id == row.Id);
                    variants[variantIndex] = row with { Date = q.Date };
                }
                else
                {
                    // #373: the Source arrived and already matches. Emitted once per distinct Source, on
                    // the database lookup rather than on every quote referencing it — stagedVariantIds
                    // is what makes that "once", exactly as it does for the Add below.
                    actions.Add(UnchangedAction(
                        batchId, ImportActionEntityTypes.Source, row.Id,
                        new SourceActionPayloadDto(q.Source, typeStr, row.Date, row.SeriesId, row.SeasonId), now));
                }
            }

            index[key] = row.Id;
            return new SourceResolution(row.Id, DateNeedsReview: false);
        }

        // #374 (developer decision, 2026-09-04): a series-capable type (currently just `tv`) with no
        // Series data yet cannot tell "a genuinely new season" from "this one quote's year is just
        // wrong" — both look identical from the raw import alone. Rather than guess by creating a new
        // Source variant (which silently fragments one show into several, found live against Arrow and
        // Mr. Robot), this is exactly the "we don't know what to do with the data" case that must be
        // reported as a conflict: attach to the nearest existing variant for now (the #375 nearest-Source
        // principle) and let the caller flag the quote itself for review, where a curator decides between
        // adding an unknown new season or accepting the quote into the existing show-level Source. Once a
        // variant already carries a SeriesId (a real Series, with a known date range, exists to place the
        // claimed year against), this ambiguity goes away and the normal per-variant resolution applies.
        if (variants.Count > 0 && IsSeriesCapable(typeStr) && variants.TrueForAll(v => v.SeriesId is null))
        {
            SourceVariant nearest = variants[0];
            index[key] = nearest.Id;
            return new SourceResolution(nearest.Id, DateNeedsReview: true);
        }

        // #374: no variant matches this quote's date claim. A genuinely first-ever title (no variant
        // seen at all yet, from neither the database nor this batch so far) keeps the legacy date-less
        // id every existing Source already has, even when this quote itself carries a date — the
        // overwhelmingly common case, and the one every pre-#374 id must keep resolving to. Only a
        // second-or-later variant needs the date folded into its id, to avoid colliding with the first.
        // The raw spelling is kept deliberately. Normalising it onto the first-seen row's casing was
        // tried and reverted (2026-09-08): it made a casing divergence invisible, so a first-seen
        // "the mOvie Title" would silently become canonical and every later, correct spelling would be
        // absorbed into it with nothing to notice. A casing duplicate must be explicitly permitted —
        // declared as a SourceAliasRule naming the canonical spelling — never hidden by the importer.
        string stableId = variants.Count == 0
            ? EntityIdentity.SourceId(q.Source, typeStr)
            : EntityIdentity.SourceId(q.Source, typeStr, q.Date);
        // A second-or-later date for a title already seen is genuinely ambiguous: either one of the two
        // dates is wrong, or the title names two works. Creating the variant silently picks the second
        // reading without saying so. Under Review the choice is staged Pending instead, and the two
        // answers are the two mechanisms that resolve it — a dated SourceAliasRule ("one date is
        // wrong") or explicit sources[] declarations ("two distinct works"). Under an auto-resolving
        // policy the caller has already said not to ask.
        bool dateIsAmbiguous = variants.Count > 0 && policy == DuplicateResolutionPolicy.Review;

        variants.Add(new SourceVariant(stableId, q.Date, null, null, SafeValue<CompletenessStatus?>.Empty));
        stagedVariantIds.Add(stableId);
        index[key] = stableId;

        actions.Add(new ImportActionEntity
        {
            BatchId = batchId,
            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType = ImportActionEntityTypes.Source,
            EntityId = stableId,
            IncomingValue = JsonSerializer.Serialize(new SourceActionPayloadDto(q.Source, typeStr, q.Date)),
            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt = now,
        });

        return new SourceResolution(stableId, dateIsAmbiguous);
    }

    private static async Task<string?> ResolveCharacterAsync(
        SqliteConnection connection, SourceQuoteDto q, string sourceId, Dictionary<string, string> index,
        string batchId, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        string sourceTypeStr = q.Type.ToString();
        if (string.IsNullOrWhiteSpace(q.Character)) return null;

        string key = $"{sourceId}|{q.Character}";
        if (index.TryGetValue(key, out string? existing)) return existing;

        // #174, ADR 013: the resolving Source's own SeriesId is the Series-relatedness signal for the
        // merge-candidate lookup below — NULL for a not-yet-existing (brand-new) Source, which
        // correctly means "no known Series" (Decision 1(c)'s conservative default).
        string? seriesId = await connection.ExecuteScalarAsync<string?>(
            Sql.Sources.SelectSeriesIdById, new { id = sourceId }, transaction);

        Guid? existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.Characters.SelectGlobalCandidateId,
            new { sourceId, name = q.Character, sourceType = sourceTypeStr, seriesId }, transaction);
        if (existingId is { } foundId)
        {
            string idStr = foundId.ToCanonicalId();
            index[key] = idStr;
            // #373: arrived and already matches — reported once per distinct Character, not per quote.
            actions.Add(UnchangedAction(
                batchId, ImportActionEntityTypes.Character, idStr,
                new CharacterActionPayloadDto(sourceId, q.Character, q.Source, sourceTypeStr), now));
            return idStr;
        }

        string stableId = EntityIdentity.CharacterId(sourceId, q.Character, sourceTypeStr);
        index[key] = stableId;

        actions.Add(new ImportActionEntity
        {
            BatchId = batchId,
            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType = ImportActionEntityTypes.Character,
            EntityId = stableId,
            IncomingValue = JsonSerializer.Serialize(new CharacterActionPayloadDto(sourceId, q.Character, q.Source, sourceTypeStr)),
            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt = now,
        });

        return stableId;
    }

    private static async Task<string?> ResolvePersonAsync(
        SqliteConnection connection, SourceQuoteDto q, Dictionary<string, string> index,
        string batchId, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        if (string.IsNullOrWhiteSpace(q.Author)) return null;

        if (index.TryGetValue(q.Author, out string? existing)) return existing;

        Guid? existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.People.SelectIdByName, new { name = q.Author }, transaction);
        if (existingId is { } foundId)
        {
            string idStr = foundId.ToCanonicalId();
            index[q.Author] = idStr;
            // #373: arrived and already matches — reported once per distinct Person, not per quote.
            actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Person, idStr, new PersonActionPayloadDto(q.Author), now));
            return idStr;
        }

        string stableId = EntityIdentity.PersonId(q.Author);
        index[q.Author] = stableId;

        actions.Add(new ImportActionEntity
        {
            BatchId = batchId,
            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType = ImportActionEntityTypes.Person,
            EntityId = stableId,
            IncomingValue = JsonSerializer.Serialize(new PersonActionPayloadDto(q.Author)),
            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt = now,
        });

        return stableId;
    }

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync, both feed the same decide-time <c>FieldMergeResolver</c> field-name vocabulary. <c>seriesId</c> added by #180.</summary>
    private static Dictionary<string, object?> ToFieldMap(SourceActionPayloadDto payload) =>
        new Dictionary<string, object?> { ["title"] = payload.Title, ["type"] = payload.Type, ["date"] = payload.Date, ["seriesId"] = payload.SeriesId, ["seasonId"] = payload.SeasonId };

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#171).</summary>
    private static Dictionary<string, object?> ToFieldMap(StageDirectionActionPayloadDto payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["imageUrl"] = payload.ImageUrl };

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#172).</summary>
    private static Dictionary<string, object?> ToFieldMap(SoundCueActionPayloadDto payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["soundFileUrl"] = payload.SoundFileUrl, ["imageUrl"] = payload.ImageUrl };

    // ── #162: explicit Source planning ───────────────────────────────────────
    // Unlike ResolveSourceAsync (natural-key match only, never compares Date/Title/Type once
    // matched), a declared sources[] entry is matched by its own explicit id first — decoupling
    // matching from content, so Title/Type/Date can all be freely corrected once a Source has
    // adopted this model.

    private static async Task PlanSourcesAsync(
        SqliteConnection connection, IReadOnlyList<SourceEntryDto> sources, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> sourceIndex,
        Dictionary<string, string> seriesIndex, Dictionary<string, string> seasonIndex,
        List<ImportActionEntity> actions, DateTime now,
        SqliteTransaction? transaction, ConflictRuleLookup? conflictRules = null,
        List<RetirableRuleFinding>? retirableRuleFindings = null)
    {
        // Dates already known for each (title, type) in this batch — seeded per key from the rows
        // already in the database, then grown as declarations are staged. See the Add path's own
        // remark for why the declaration path needs its own variant tracking.
        Dictionary<string, HashSet<string?>> declaredDatesByKey = [];

        foreach (SourceEntryDto s in sources)
        {
            string typeStr = s.Type.ToString();

            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured — every later reference to the file's id (lookup, matchedId, addId) uses this
            // canonicalized form instead of the file's raw casing. A malformed or absent id passes
            // through unchanged; general id-format validation is out of scope here.
            string? canonicalId = s.Id is { } sIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(sIdRaw, out string? sIdCanonical)
                ? sIdCanonical
                : s.Id;

            // #180/#190: seriesName resolution is Optional-aware — an absent seriesName stays Absent
            // (never touches the existing Series link, resolved per-branch below via ResolveAgainst);
            // an explicit null stays a genuine clear; a real name resolves via the same-batch index
            // first (populated by PlanSeriesAsync, which must run before this method), falling back to
            // a DB lookup for a Series declared in an earlier batch. A name with no match anywhere
            // resolves to null — silently dropped, same as PlanSeriesAsync's own dangling-universeName
            // treatment.
            Optional<string> resolvedSeriesId = Optional<string>.Absent;
            if (s.SeriesName.HasValue)
            {
                string? seriesName = s.SeriesName.Value;
                resolvedSeriesId = seriesName is null
                    ? Optional<string>.Of(null)
                    : Optional<string>.Of(seriesIndex.TryGetValue(seriesName, out string? indexed)
                        ? indexed
                        : await connection.ExecuteScalarAsync<Guid?>(Sql.Series.SelectIdByName, new { name = seriesName }, transaction) is { } found
                            ? found.ToCanonicalId()
                            : null);
            }

            // #375: seasonNumber resolution mirrors seriesName's Optional handling above — absent
            // leaves the existing Season link untouched, an explicit null clears it, and a number
            // resolves against the season index PlanSeasonsAsync built, keyed by (SeriesId, Number).
            // A number naming no declared or existing Season resolves to null, silently dropped, same
            // as a dangling seriesName.
            Optional<string> resolvedSeasonId = Optional<string>.Absent;
            if (s.SeasonNumber.HasValue)
            {
                int? seasonNumber = s.SeasonNumber.Value;
                resolvedSeasonId = seasonNumber is null
                    ? Optional<string>.Of(null)
                    : Optional<string>.Of(seasonIndex.TryGetValue(SeasonKey(resolvedSeriesId.HasValue ? resolvedSeriesId.Value : null, seasonNumber.Value), out string? indexedSeason)
                        ? indexedSeason
                        : await connection.ExecuteScalarAsync<Guid?>(
                            Sql.Season.SelectIdBySeriesAndNumber,
                            new { seriesId = resolvedSeriesId.HasValue ? resolvedSeriesId.Value : null, number = seasonNumber.Value },
                            transaction) is { } foundSeason
                            ? foundSeason.ToCanonicalId()
                            : null);
            }

            // #162's correction shape: an entry carrying an explicit id is matched by it first. An
            // entry omitting one (#180's enrichment shape) skips straight to the natural-key path below.
            (string Title, string Type, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = canonicalId is { } explicitId
                ? await connection.QuerySingleOrDefaultAsync<(string Title, string Type, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Sources.SelectExistingById, new { id = explicitId }, transaction)
                : null;

            if (existing is { } row)
            {
                string matchedId = canonicalId!; // Non-null by construction: `existing` is only set when canonicalId is.
                // #190: an absent Date/SeriesName resolves to the existing row's own value — never a
                // change, under any policy. See OptionalExtensions.ResolveAgainst.
                string? incomingDate = s.Date.ResolveAgainst(row.Date);
                string? incomingSeriesId = resolvedSeriesId.ResolveAgainst(row.SeriesId);
                SourceActionPayloadDto existingPayload = new SourceActionPayloadDto(row.Title, row.Type, row.Date, row.SeriesId, row.SeasonId);
                SourceActionPayloadDto incomingPayload = new SourceActionPayloadDto(s.Title, typeStr, incomingDate, incomingSeriesId, resolvedSeasonId.ResolveAgainst(row.SeasonId));
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                // The corrected Title/Type is what a same-batch quote referencing this Source should
                // resolve to — indexed regardless of whether this ends up changed/blocked/unchanged.
                sourceIndex[$"{s.Title}|{typeStr}"] = matchedId;

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                // #181: build the rule-decisions map before the "nothing changed" early exit below —
                // a Custom rule can correct a field that's identical (e.g. missing) on both sides.
                Dictionary<string, FieldMergeDecision> ruleDecisions = [];
                bool hasStaleRule = false;
                if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
                {
                    foreach (string field in existingFields.Keys)
                    {
                        if (!conflictRules.TryResolve(matchedId, field, existingFields[field], incomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                            continue;
                        bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                        if (isStale) hasStaleRule = true;
                        else ruleDecisions[field] = decision;
                        if (outcome == ConflictRuleOutcome.Retirable)
                            retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Source, matchedId, field));
                    }
                }

                // #153: a stale rule must still be surfaced even when nothing else changed — never
                // silently skipped just because the raw fields themselves happen to already agree.
                // #373: and when nothing changed, the row is reported as Unchanged rather than skipped
                // — "silent reuse" is exactly what left a whole entity type absent from the report.
                if (changedFields.Count == 0 && ruleDecisions.Count == 0 && !hasStaleRule)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Source, matchedId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                SourceActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SourceActionPayloadDto((string)mergeResult!.MergedFields["title"]!, (string)mergeResult.MergedFields["type"]!, (string?)mergeResult.MergedFields["date"], (string?)mergeResult.MergedFields["seriesId"]),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above — Skip's resolved
                // value is always existingPayload (nothing written), so Skip can never block a
                // Complete row; a merge policy only blocks on fields the merge itself would change.
                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Source,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153: a stale rule holds the whole action for review, same as Blocked above —
                // checked only once we know this action isn't already Blocked.
                if (hasStaleRule)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Source,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Stale.ToString(), ImportActionStatus.Stale),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #181: a rule never bypasses CompletenessGuard above — only tried once we know this
                // action isn't Blocked.
                FieldMergeResult? ruleResolved = null;
                if (ruleDecisions.Count > 0)
                {
                    try { ruleResolved = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions); }
                    catch (UnresolvedFieldConflictException) { /* Not every ambiguous field has a matching rule — fall through to normal Pending staging. */ }
                }
                if (ruleResolved is not null)
                    resolved = new SourceActionPayloadDto((string)ruleResolved.MergedFields["title"]!, (string)ruleResolved.MergedFields["type"]!, (string?)ruleResolved.MergedFields["date"], (string?)ruleResolved.MergedFields["seriesId"], (string?)ruleResolved.MergedFields["seasonId"]);

                bool isPending = policy == DuplicateResolutionPolicy.Review && ruleResolved is null;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Source,
                    EntityId = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            // Falls back to natural-key (title+type): either the entry omits an explicit id (#180's
            // enrichment shape) or it carries one that matches no row yet (a not-yet-migrated row —
            // #162's scope boundary).
            //
            // #374: found live (T2 Docker, a full-corpus reseed) — once Date joined a Source's natural
            // key (step 6), more than one row can share (Title, Type), and the single-row
            // SelectExistingByTitleAndType this branch used unchanged (per step 6's own "every other
            // Title/Type consumer still expects at most one row" assumption) crashed with "Sequence
            // contains more than one element" the moment a real title here — an enrichment entry with
            // no date of its own — matched two already-dated variants created by an earlier file's
            // quotes. Uses the same SelectAllExistingByTitleAndType + PickSourceVariant machinery
            // ResolveSourceAsync already established for exactly this ambiguity, rather than a second,
            // divergent resolution rule for the same problem: an entry with no date claim (absent or
            // explicit null both collapse to no claim here) picks the nearest variant; a dated entry
            // matches its exact variant or falls back to a date-less one to backfill.
            IEnumerable<(string Id, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)> keyVariantRows = await connection.QueryAsync<(string Id, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)>(
                Sql.Sources.SelectAllExistingByTitleAndType, new { title = s.Title, type = typeStr }, transaction);
            List<SourceVariant> keyVariants = [.. keyVariantRows.Select(r => new SourceVariant(r.Id, r.Date, r.SeriesId, r.SeasonId, r.CompletenessStatus))];
            SourceVariant? pickedKeyVariant = PickSourceVariant(keyVariants, s.Date.HasValue ? s.Date.Value : null);
            (string Id, string? Date, string? SeriesId, string? SeasonId, SafeValue<CompletenessStatus?> CompletenessStatus)? existingByKey = pickedKeyVariant is { } picked
                ? (picked.Id, picked.Date, picked.SeriesId, picked.SeasonId, picked.CompletenessStatus)
                : null;

            if (existingByKey is { } keyRow)
            {
                // Indexed to the row's REAL id — a same-batch quote referencing this title/type must
                // resolve to the existing row, never to this entry's own (possibly absent) id.
                sourceIndex[$"{s.Title}|{typeStr}"] = keyRow.Id;

                // #190: retired the old hard-coded Date carry-through — Date and SeriesId now both
                // resolve the same Optional-aware way the explicit-id branch above uses. Title/Type
                // still cannot differ on this path by construction (they are the lookup key;
                // correcting them is #162's explicit-id job). A natural-key entry that never mentions
                // "date" still never changes it (ResolveAgainst falls back to keyRow.Date); one that
                // explicitly sets "date" now actually takes effect, where it was previously always
                // silently ignored regardless of what the file said.
                string? keyIncomingDate = s.Date.ResolveAgainst(keyRow.Date);
                string? keyIncomingSeriesId = resolvedSeriesId.ResolveAgainst(keyRow.SeriesId);
                SourceActionPayloadDto keyExistingPayload = new SourceActionPayloadDto(s.Title, typeStr, keyRow.Date, keyRow.SeriesId, keyRow.SeasonId);
                SourceActionPayloadDto keyIncomingPayload = new SourceActionPayloadDto(s.Title, typeStr, keyIncomingDate, keyIncomingSeriesId, resolvedSeasonId.ResolveAgainst(keyRow.SeasonId));
                Dictionary<string, object?> keyExistingFields = ToFieldMap(keyExistingPayload);
                Dictionary<string, object?> keyIncomingFields = ToFieldMap(keyIncomingPayload);

                HashSet<string> changedFields = [.. keyExistingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, keyIncomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                // #181: build the rule-decisions map before the "nothing changed" early exit below —
                // a Custom rule can correct a field that's identical (e.g. missing) on both sides.
                Dictionary<string, FieldMergeDecision> keyRuleDecisions = [];
                bool keyHasStaleRule = false;
                if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
                {
                    foreach (string field in keyExistingFields.Keys)
                    {
                        if (!conflictRules.TryResolve(keyRow.Id, field, keyExistingFields[field], keyIncomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                            continue;
                        bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                        if (isStale) keyHasStaleRule = true;
                        else keyRuleDecisions[field] = decision;
                        if (outcome == ConflictRuleOutcome.Retirable)
                            retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Source, keyRow.Id, field));
                    }
                }

                // #153: a stale rule must still be surfaced even when nothing else changed.
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0 && keyRuleDecisions.Count == 0 && !keyHasStaleRule)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Source, keyRow.Id, keyIncomingPayload, now));
                    continue;
                }

                // #190 drive-by fix: this branch previously never consulted FieldMergeResolver.Resolve
                // for MergeOurs/MergeTheirs at all — it always took keyIncomingPayload for any policy
                // but Skip, so MergeOurs could silently overwrite an existing Series link even though
                // its own contract is "existing wins on a genuine conflict". Now matches the
                // explicit-id branch's shape exactly.
                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(keyExistingFields, keyIncomingFields, policy) : null;
                SourceActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SourceActionPayloadDto((string)mergeResult!.MergedFields["title"]!, (string)mergeResult.MergedFields["type"]!, (string?)mergeResult.MergedFields["date"], (string?)mergeResult.MergedFields["seriesId"]),
                    DuplicateResolutionPolicy.Skip => keyExistingPayload,
                    _ => keyIncomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. keyExistingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus keyCurrentStatus = keyRow.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(keyCurrentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Source,
                        EntityId = keyRow.Id,
                        ExistingValue = JsonSerializer.Serialize(keyExistingPayload),
                        IncomingValue = JsonSerializer.Serialize(keyIncomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153: a stale rule holds the whole action for review, same as Blocked above.
                if (keyHasStaleRule)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Source,
                        EntityId = keyRow.Id,
                        ExistingValue = JsonSerializer.Serialize(keyExistingPayload),
                        IncomingValue = JsonSerializer.Serialize(keyIncomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Stale.ToString(), ImportActionStatus.Stale),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #181: a rule never bypasses CompletenessGuard above — only tried once we know this
                // action isn't Blocked.
                FieldMergeResult? keyRuleResolved = null;
                if (keyRuleDecisions.Count > 0)
                {
                    try { keyRuleResolved = FieldMergeResolver.ResolveWithDecisions(keyExistingFields, keyIncomingFields, keyRuleDecisions); }
                    catch (UnresolvedFieldConflictException) { /* Not every ambiguous field has a matching rule — fall through to normal Pending staging. */ }
                }
                if (keyRuleResolved is not null)
                    resolved = new SourceActionPayloadDto((string)keyRuleResolved.MergedFields["title"]!, (string)keyRuleResolved.MergedFields["type"]!, (string?)keyRuleResolved.MergedFields["date"], (string?)keyRuleResolved.MergedFields["seriesId"], (string?)keyRuleResolved.MergedFields["seasonId"]);

                bool keyIsPending = policy == DuplicateResolutionPolicy.Review && keyRuleResolved is null;
                ImportActionStatus keyStatus = keyIsPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Source,
                    EntityId = keyRow.Id,
                    ExistingValue = JsonSerializer.Serialize(keyExistingPayload),
                    IncomingValue = JsonSerializer.Serialize(keyIncomingPayload),
                    MergedFields = keyIsPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(keyStatus.ToString(), keyStatus),
                    DetectedAt = now,
                });
                continue;
            }

            // No match by id or natural key — a genuine Add. With no explicit id in the file, the
            // EntityIdentity-derived stable id is used: the same value ResolveSourceAsync would
            // independently compute for a quote referencing this same title/type, so both resolve to
            // one row rather than two.
            // #374's variant convention, which this path never had: the first declaration of a
            // (title, type) keeps the date-less id every existing Source already carries, and a
            // second-or-later one claiming a *different* date folds that date in. Without this, two
            // declarations of one title computed the identical id and collided, so a genuinely
            // two-version title — The Lion King, 1994 and 2019, both correct — could not be declared
            // at all. Same date as an earlier declaration still collapses onto it, which is what keeps
            // a repeated entry idempotent.
            string declKey = $"{s.Title}|{typeStr}";
            string? declDate = s.Date.HasValue ? s.Date.Value : null;
            if (!declaredDatesByKey.TryGetValue(declKey, out HashSet<string?>? seenDates))
                declaredDatesByKey[declKey] = seenDates = [.. keyVariants.Select(v => v.Date)];

            string addId = canonicalId
                ?? (seenDates.Count > 0 && !seenDates.Contains(declDate)
                        ? EntityIdentity.SourceId(s.Title, typeStr, declDate)
                        : EntityIdentity.SourceId(s.Title, typeStr));
            seenDates.Add(declDate);

            // Indexed so a same-batch quote referencing this exact title/type resolves to this same
            // new row, instead of ResolveSourceAsync independently deriving its own EntityIdentity
            // stable id (which would differ from a file-declared id).
            sourceIndex[$"{s.Title}|{typeStr}"] = addId;

            // #190: no existing row to preserve, so ResolveAgainst(null) — an absent property simply
            // resolves to null, matching this project's existing Add-path behaviour exactly.
            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Source,
                EntityId = addId,
                IncomingValue = JsonSerializer.Serialize(new SourceActionPayloadDto(s.Title, typeStr, s.Date.ResolveAgainst(null), resolvedSeriesId.ResolveAgainst(null), resolvedSeasonId.ResolveAgainst(null))),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#173).</summary>
    private static Dictionary<string, object?> ToFieldMap(PersonActionPayloadDto payload) => new() { ["name"] = payload.Name, ["dateOfBirth"] = payload.DateOfBirth, ["dateOfDeath"] = payload.DateOfDeath };

    /// <summary>
    /// Only <c>name</c> is diffed — <c>SourceId</c>/<c>SourceTitle</c>/<c>SourceType</c> are never
    /// Modify-able (ADR 013 Decision 9: <c>SourceType</c> is immutable once a Character exists), so
    /// they're excluded from the diff vocabulary even though the payload itself still carries them
    /// for audit/informational purposes. Same key name as <see cref="Quotinator.Core.Services.
    /// SqliteImportActionService"/>'s own private overload — must stay in sync (#175).
    /// </summary>
    private static Dictionary<string, object?> ToFieldMap(CharacterActionPayloadDto payload) => new() { ["name"] = payload.Name };

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#163).</summary>
    private static Dictionary<string, object?> ToFieldMap(SeriesActionPayloadDto payload) => new() { ["name"] = payload.Name, ["universeId"] = payload.UniverseId };

    /// <summary>Same key name as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#163).</summary>
    private static Dictionary<string, object?> ToFieldMap(UniverseActionPayloadDto payload) => new() { ["name"] = payload.Name };

    // ── #173: explicit Person planning ───────────────────────────────────────
    // Same shape as PlanSourcesAsync — id-first lookup, natural-key fallback for a not-yet-migrated
    // row, personIndex populated in both branches so a same-batch quote's author resolves to the
    // declared row instead of independently deriving its own EntityIdentity stable id (the #162
    // test-7a-shaped threading risk).

    private static async Task PlanPeopleAsync(
        SqliteConnection connection, IReadOnlyList<PersonEntryDto> people, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> personIndex,
        List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (PersonEntryDto p in people)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured — PersonEntryDto.Id is required, so there is no absent case to preserve.
            string canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(p.Id, out string? pIdCanonical) ? pIdCanonical! : p.Id;

            string resolvedId = canonicalId;
            (string Name, string? DateOfBirth, string? DateOfDeath, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = await connection.QuerySingleOrDefaultAsync<(string Name, string? DateOfBirth, string? DateOfDeath, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.People.SelectExistingById, new { id = canonicalId }, transaction);

            // #373: PersonEntryDto.Id is required, so this reaches the natural key only when a declared
            // id matched nothing while the Name matched — the not-yet-migrated row #173 scoped out.
            // Developer decision 2026-09-08: Person joins the other three, that boundary was never
            // validated. It now takes the same comparison path rather than resolving an id and returning.
            if (existing is null
                && await connection.ExecuteScalarAsync<Guid?>(Sql.People.SelectIdByName, new { name = p.Name }, transaction) is { } byKey)
            {
                resolvedId = byKey.ToCanonicalId();
                existing   = await connection.QuerySingleOrDefaultAsync<(string Name, string? DateOfBirth, string? DateOfDeath, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.People.SelectExistingById, new { id = resolvedId }, transaction);
            }

            if (existing is { } row)
            {
                // #190: an absent DateOfBirth/DateOfDeath resolves to the existing row's own value —
                // never a change, under any policy.
                string? incomingDob = p.DateOfBirth.ResolveAgainst(row.DateOfBirth);
                string? incomingDod = p.DateOfDeath.ResolveAgainst(row.DateOfDeath);
                PersonActionPayloadDto existingPayload = new PersonActionPayloadDto(row.Name, row.DateOfBirth, row.DateOfDeath);
                PersonActionPayloadDto incomingPayload = new PersonActionPayloadDto(p.Name, incomingDob, incomingDod);
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                personIndex[p.Name] = resolvedId;

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Person, resolvedId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                PersonActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new PersonActionPayloadDto((string)mergeResult!.MergedFields["name"]!, (string?)mergeResult.MergedFields["dateOfBirth"], (string?)mergeResult.MergedFields["dateOfDeath"]),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Person,
                        EntityId = resolvedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                bool isPending = policy == DuplicateResolutionPolicy.Review;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Person,
                    EntityId = resolvedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            personIndex[p.Name] = canonicalId;

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Person,
                EntityId = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new PersonActionPayloadDto(p.Name, p.DateOfBirth.ResolveAgainst(null), p.DateOfDeath.ResolveAgainst(null))),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    // ── #175: explicit Character planning ────────────────────────────────────
    // Widened schema (ADR 013-aware, developer decision 2026-07-24): unlike Person, a characters[]
    // entry's own natural-key fallback must resolve through ADR 013's real Type-anchored,
    // Series-scoped algorithm (Sql.Characters.SelectGlobalCandidateId), not a simple Name-only
    // lookup — a bare Name can legitimately match more than one Character. sourceTitle/sourceType
    // supply the Source context that algorithm needs, and are resolved/staged unconditionally (even
    // on the Correction/id-matched path) so CharacterActionPayloadDto's SourceId always carries a real,
    // meaningful value for the audit trail, not just on the Add path.

    private static async Task<string> ResolveOrStageSourceIdAsync(
        SqliteConnection connection, string title, string typeStr, Dictionary<string, string> sourceIndex,
        string batchId, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        string key = $"{title}|{typeStr}";
        if (sourceIndex.TryGetValue(key, out string? indexed)) return indexed;

        string? existingId = await connection.ExecuteScalarAsync<string?>(
            Sql.Sources.SelectIdByTitleAndType, new { title, type = typeStr }, transaction);
        if (existingId is { } foundId)
        {
            sourceIndex[key] = foundId;
            return foundId;
        }

        string stableId = EntityIdentity.SourceId(title, typeStr);
        sourceIndex[key] = stableId;

        actions.Add(new ImportActionEntity
        {
            BatchId = batchId,
            ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType = ImportActionEntityTypes.Source,
            EntityId = stableId,
            IncomingValue = JsonSerializer.Serialize(new SourceActionPayloadDto(title, typeStr)),
            Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt = now,
        });

        return stableId;
    }

    private static async Task PlanCharactersAsync(
        SqliteConnection connection, IReadOnlyList<CharacterEntryDto> characters, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> sourceIndex,
        Dictionary<string, string> characterIndex, List<ImportActionEntity> actions, DateTime now,
        SqliteTransaction? transaction)
    {
        foreach (CharacterEntryDto c in characters)
        {
            string sourceTypeStr = c.SourceType.ToString();
            string? canonicalId = c.Id is { } cIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(cIdRaw, out string? cIdCanonical)
                ? cIdCanonical
                : c.Id;

            string resolvedSourceId = await ResolveOrStageSourceIdAsync(connection, c.SourceTitle, sourceTypeStr, sourceIndex, batchId, actions, now, transaction);

            (string Name, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = null;
            string? matchedId = null;

            if (canonicalId is { } explicitId)
            {
                (string Name, SafeValue<CompletenessStatus?> CompletenessStatus)? byId = await connection.QuerySingleOrDefaultAsync<(string Name, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Characters.SelectExistingById, new { id = explicitId }, transaction);
                if (byId is { } row)
                {
                    existing = row;
                    matchedId = explicitId;
                }
            }

            if (matchedId is null)
            {
                // No id, or a declared id that matched nothing — falls back to ADR 013's own
                // Type-anchored, Series-scoped matching algorithm, mirroring ResolveCharacterAsync's
                // own resolution exactly.
                string? seriesId = await connection.ExecuteScalarAsync<string?>(
                    Sql.Sources.SelectSeriesIdById, new { id = resolvedSourceId }, transaction);
                Guid? candidateId = await connection.ExecuteScalarAsync<Guid?>(
                    Sql.Characters.SelectGlobalCandidateId,
                    new { sourceId = resolvedSourceId, name = c.Name, sourceType = sourceTypeStr, seriesId }, transaction);

                if (candidateId is { } foundId)
                {
                    matchedId = foundId.ToCanonicalId();
                    existing = await connection.QuerySingleOrDefaultAsync<(string Name, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                        Sql.Characters.SelectExistingById, new { id = matchedId }, transaction);
                }
            }

            if (matchedId is null)
            {
                // Genuinely new — deliberately out of scope for this entry to establish any
                // cross-Source link beyond the one it declares (ADR 013's own Notes on #175). Mirrors
                // PlanSourcesAsync's own Add-path precedent exactly: an explicit id that matched
                // nothing (neither by id nor by ADR 013's own algorithm) is still honoured as the new
                // row's id; only a genuinely id-less entry gets an EntityIdentity-derived one.
                string stableId = canonicalId ?? EntityIdentity.CharacterId(resolvedSourceId, c.Name, sourceTypeStr);
                characterIndex[$"{resolvedSourceId}|{c.Name}"] = stableId;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                    EntityType = ImportActionEntityTypes.Character,
                    EntityId = stableId,
                    IncomingValue = JsonSerializer.Serialize(new CharacterActionPayloadDto(resolvedSourceId, c.Name, c.SourceTitle, sourceTypeStr)),
                    Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                    DetectedAt = now,
                });
                continue;
            }

            (string Name, SafeValue<CompletenessStatus?> CompletenessStatus) row2 = existing!.Value;
            CharacterActionPayloadDto existingPayload = new CharacterActionPayloadDto(resolvedSourceId, row2.Name, c.SourceTitle, sourceTypeStr);
            CharacterActionPayloadDto incomingPayload = new CharacterActionPayloadDto(resolvedSourceId, c.Name, c.SourceTitle, sourceTypeStr);
            Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
            Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

            // Indexed under the file's own declared name (not the corrected one), mirroring Person's
            // own Correction-path indexing (#173) — a same-batch quote referencing this exact
            // (Source, Name) pair resolves to the matched row either way.
            characterIndex[$"{resolvedSourceId}|{c.Name}"] = matchedId;

            HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];
            // #373: reported as Unchanged rather than silently reused.
            if (changedFields.Count == 0)
            {
                actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Character, matchedId, incomingPayload, now));
                continue;
            }

            bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
            FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
            CharacterActionPayloadDto resolved = policy switch
            {
                DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                    new CharacterActionPayloadDto(resolvedSourceId, (string)mergeResult!.MergedFields["name"]!, c.SourceTitle, sourceTypeStr),
                DuplicateResolutionPolicy.Skip => existingPayload,
                _ => incomingPayload,
            };

            // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
            // not the raw incoming value used for the "unchanged" check above.
            Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
            HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

            CompletenessStatus currentStatus = row2.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
            if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
            {
                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Character,
                    EntityId = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                    DetectedAt = now,
                });
                continue;
            }

            bool isPending = policy == DuplicateResolutionPolicy.Review;
            ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                EntityType = ImportActionEntityTypes.Character,
                EntityId = matchedId,
                ExistingValue = JsonSerializer.Serialize(existingPayload),
                IncomingValue = JsonSerializer.Serialize(incomingPayload),
                MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                DetectedAt = now,
            });
        }
    }

    // ── #180: explicit Universe/Series planning ──────────────────────────────
    // Add-only, natural-key-keyed by Name — no explicit id in the file (unlike Source/Person), since
    // EntityIdentity.SeriesId/UniverseId derives it from the name alone. No Modify/merge semantics:
    // a Universe/Series entry that already exists by name is simply reused (indexed for a dependent
    // Series/Source to resolve against), never diffed or re-staged.

    // ── #163: Universe/Series widened to the two-shape (Correction/Creation) pattern ────────
    // Mirrors PlanPeopleAsync's shape exactly (id-match → field-map diff → unchanged-check →
    // policy-based resolution → CompletenessGuard.ShouldBlock → stage Blocked/Modify), plus
    // PlanSourcesAsync's own explicit-id-honoured-on-Add precedent (canonicalId ?? EntityIdentity...)
    // for the genuinely-new Add path — the same fix #175 found missing for Character.

    private static async Task PlanUniverseAsync(
        SqliteConnection connection, IReadOnlyList<UniverseEntryDto> universes, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> universeIndex,
        List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction,
        ConflictRuleLookup? conflictRules = null,
        List<RetirableRuleFinding>? retirableRuleFindings = null)
    {
        foreach (UniverseEntryDto u in universes)
        {
            string? canonicalId = u.Id is { } uIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(uIdRaw, out string? uIdCanonical)
                ? uIdCanonical
                : u.Id;

            string? resolvedId = canonicalId;
            (string Name, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = resolvedId is { } explicitId
                ? await connection.QuerySingleOrDefaultAsync<(string Name, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Universe.SelectExistingById, new { id = explicitId }, transaction)
                : null;

            // #373: an id-less entry, or a declared id that matched nothing, resolves by natural key
            // and then takes the same comparison path an explicit id does. Two lookup paths — only one
            // of which read any field — is what let a changed field be discarded rather than staged.
            if (existing is null
                && await connection.ExecuteScalarAsync<Guid?>(Sql.Universe.SelectIdByName, new { name = u.Name }, transaction) is { } byKey)
            {
                resolvedId = byKey.ToCanonicalId();
                existing   = await connection.QuerySingleOrDefaultAsync<(string Name, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Universe.SelectExistingById, new { id = resolvedId }, transaction);
            }

            if (existing is { } row)
            {
                string matchedId = resolvedId!;
                UniverseActionPayloadDto existingPayload = new UniverseActionPayloadDto(row.Name);
                UniverseActionPayloadDto incomingPayload = new UniverseActionPayloadDto(u.Name);
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                universeIndex[u.Name] = matchedId;

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                // #181: build the rule-decisions map before the "nothing changed" early exit below —
                // a Custom rule can correct a field that's identical (e.g. missing) on both sides, which
                // must still get a chance to apply rather than being skipped as "unchanged".
                Dictionary<string, FieldMergeDecision> ruleDecisions = [];
                bool hasStaleRule = false;
                if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
                {
                    foreach (string field in existingFields.Keys)
                    {
                        if (!conflictRules.TryResolve(matchedId, field, existingFields[field], incomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                            continue;
                        bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                        if (isStale) hasStaleRule = true;
                        else ruleDecisions[field] = decision;
                        if (outcome == ConflictRuleOutcome.Retirable)
                            retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Universe, matchedId, field));
                    }
                }

                // #153: a stale rule must still be surfaced even when nothing else changed.
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0 && ruleDecisions.Count == 0 && !hasStaleRule)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Universe, matchedId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                UniverseActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new UniverseActionPayloadDto((string)mergeResult!.MergedFields["name"]!),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Universe,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153: a stale rule holds the whole action for review, same as Blocked above.
                if (hasStaleRule)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Universe,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Stale.ToString(), ImportActionStatus.Stale),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #181: a rule never bypasses CompletenessGuard above — only tried once we know this
                // action isn't Blocked.
                FieldMergeResult? ruleResolved = null;
                if (ruleDecisions.Count > 0)
                {
                    try { ruleResolved = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions); }
                    catch (UnresolvedFieldConflictException) { /* Not every ambiguous field has a matching rule — fall through to normal Pending staging. */ }
                }
                if (ruleResolved is not null)
                    resolved = new UniverseActionPayloadDto((string)ruleResolved.MergedFields["name"]!);

                bool isPending = policy == DuplicateResolutionPolicy.Review && ruleResolved is null;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Universe,
                    EntityId = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            string stableId = canonicalId ?? EntityIdentity.UniverseId(u.Name);
            universeIndex[u.Name] = stableId;

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Universe,
                EntityId = stableId,
                IncomingValue = JsonSerializer.Serialize(new UniverseActionPayloadDto(u.Name)),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    /// <summary>
    /// Resolves a declared <c>series[]</c> entry's <c>universeName</c> against <paramref name="universeIndex"/>
    /// — populated by <see cref="PlanUniverseAsync"/>, which must run first in <see cref="PlanAsync"/>.
    /// A <c>universeName</c> with no matching entry in the file's own <c>universe[]</c> section and no
    /// existing DB row resolves to <c>null</c> (silently dropped, not an error) — #180's spec does not
    /// require validating a dangling reference; a future issue can add that if it becomes a real need.
    /// </summary>
    private static async Task PlanSeriesAsync(
        SqliteConnection connection, IReadOnlyList<SeriesEntryDto> series, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> universeIndex,
        Dictionary<string, string> seriesIndex, List<ImportActionEntity> actions, DateTime now,
        SqliteTransaction? transaction, ConflictRuleLookup? conflictRules = null,
        List<RetirableRuleFinding>? retirableRuleFindings = null)
    {
        foreach (SeriesEntryDto s in series)
        {
            string? canonicalId = s.Id is { } sIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(sIdRaw, out string? sIdCanonical)
                ? sIdCanonical
                : s.Id;

            async Task<string?> ResolveUniverseIdAsync(string? universeName) =>
                universeName is null
                    ? null
                    : universeIndex.TryGetValue(universeName, out string? indexed)
                        ? indexed
                        : await connection.ExecuteScalarAsync<Guid?>(Sql.Universe.SelectIdByName, new { name = universeName }, transaction) is { } found
                            ? found.ToCanonicalId()
                            : null;

            string? resolvedId = canonicalId;
            (string Name, string? UniverseId, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = resolvedId is { } explicitId
                ? await connection.QuerySingleOrDefaultAsync<(string Name, string? UniverseId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Series.SelectExistingById, new { id = explicitId }, transaction)
                : null;

            // #373: see PlanUniverseAsync's own remark — the natural-key match takes the same
            // comparison path an explicit id does, rather than resolving an id and returning.
            if (existing is null
                && await connection.ExecuteScalarAsync<Guid?>(Sql.Series.SelectIdByName, new { name = s.Name }, transaction) is { } byKey)
            {
                resolvedId = byKey.ToCanonicalId();
                existing   = await connection.QuerySingleOrDefaultAsync<(string Name, string? UniverseId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Series.SelectExistingById, new { id = resolvedId }, transaction);
            }

            if (existing is { } row)
            {
                string matchedId = resolvedId!;
                string? incomingUniverseId = await ResolveUniverseIdAsync(s.UniverseName);
                // UniverseName is only ever carried on the incoming side: EnsureUniverseExistsAsync's
                // defensive re-insert (guarding against Series applying before its own Universe within
                // the same batch) only genuinely fires when the resolved UniverseId is the incoming one
                // — the existing side's Universe row is already known to exist, so the insert there is
                // always a safe no-op regardless of what name is passed.
                SeriesActionPayloadDto existingPayload = new SeriesActionPayloadDto(row.Name, row.UniverseId);
                SeriesActionPayloadDto incomingPayload = new SeriesActionPayloadDto(s.Name, incomingUniverseId, s.UniverseName);
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                seriesIndex[s.Name] = matchedId;

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                // #181: build the rule-decisions map before the "nothing changed" early exit below —
                // a Custom rule can correct a field that's identical (e.g. missing) on both sides, which
                // must still get a chance to apply rather than being skipped as "unchanged".
                Dictionary<string, FieldMergeDecision> ruleDecisions = [];
                bool hasStaleRule = false;
                if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
                {
                    foreach (string field in existingFields.Keys)
                    {
                        if (!conflictRules.TryResolve(matchedId, field, existingFields[field], incomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                            continue;
                        bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                        if (isStale) hasStaleRule = true;
                        else ruleDecisions[field] = decision;
                        if (outcome == ConflictRuleOutcome.Retirable)
                            retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Series, matchedId, field));
                    }
                }

                // #153: a stale rule must still be surfaced even when nothing else changed.
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0 && ruleDecisions.Count == 0 && !hasStaleRule)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Series, matchedId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                SeriesActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SeriesActionPayloadDto(
                            (string)mergeResult!.MergedFields["name"]!,
                            (string?)mergeResult.MergedFields["universeId"],
                            (string?)mergeResult.MergedFields["universeId"] == incomingUniverseId ? s.UniverseName : null),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Series,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #153: a stale rule holds the whole action for review, same as Blocked above.
                if (hasStaleRule)
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Series,
                        EntityId = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Stale.ToString(), ImportActionStatus.Stale),
                        DetectedAt = now,
                    });
                    continue;
                }

                // #181: a rule never bypasses CompletenessGuard above — only tried once we know this
                // action isn't Blocked.
                FieldMergeResult? ruleResolved = null;
                if (ruleDecisions.Count > 0)
                {
                    try { ruleResolved = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions); }
                    catch (UnresolvedFieldConflictException) { /* Not every ambiguous field has a matching rule — fall through to normal Pending staging. */ }
                }
                if (ruleResolved is not null)
                {
                    string? resolvedUniverseId = (string?)ruleResolved.MergedFields["universeId"];
                    resolved = new SeriesActionPayloadDto(
                        (string)ruleResolved.MergedFields["name"]!,
                        resolvedUniverseId,
                        resolvedUniverseId == incomingUniverseId ? s.UniverseName : null);
                }

                bool isPending = policy == DuplicateResolutionPolicy.Review && ruleResolved is null;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Series,
                    EntityId = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            string? universeId = await ResolveUniverseIdAsync(s.UniverseName);
            string stableId = canonicalId ?? EntityIdentity.SeriesId(s.Name);
            seriesIndex[s.Name] = stableId;

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Series,
                EntityId = stableId,
                IncomingValue = JsonSerializer.Serialize(new SeriesActionPayloadDto(s.Name, universeId, s.UniverseName)),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    /// <summary>
    /// #375: plans a declared <c>seasons[]</c> entry, resolving its <c>seriesName</c> against
    /// <paramref name="seriesIndex"/> — populated by <see cref="PlanSeriesAsync"/>, which must run
    /// first in <see cref="PlanAsync"/>. The natural key is (SeriesId, Number) rather than a name,
    /// which is the one structural difference from every sibling planner here: an ordinal only
    /// identifies a season within its parent. <paramref name="seasonIndex"/> is keyed the same way, so
    /// <see cref="ResolveSourceAsync"/> can resolve a <c>seasonNumber</c> to a real Season id.
    /// A <c>seriesName</c> that resolves to nothing leaves the Season parentless rather than failing —
    /// same silently-dropped dangling-reference handling as <see cref="PlanSeriesAsync"/>'s own
    /// <c>universeName</c>.
    /// </summary>
    private static async Task PlanSeasonsAsync(
        SqliteConnection connection, IReadOnlyList<SeasonEntryDto> seasons, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> seriesIndex,
        Dictionary<string, string> seasonIndex, List<ImportActionEntity> actions, DateTime now,
        SqliteTransaction? transaction, ConflictRuleLookup? conflictRules = null,
        List<RetirableRuleFinding>? retirableRuleFindings = null)
    {
        foreach (SeasonEntryDto se in seasons)
        {
            string? canonicalId = se.Id is { } seIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(seIdRaw, out string? seIdCanonical)
                ? seIdCanonical
                : se.Id;

            string? seriesId = se.SeriesName is null
                ? null
                : seriesIndex.TryGetValue(se.SeriesName, out string? indexedSeries)
                    ? indexedSeries
                    : await connection.ExecuteScalarAsync<Guid?>(Sql.Series.SelectIdByName, new { name = se.SeriesName }, transaction) is { } foundSeries
                        ? foundSeries.ToCanonicalId()
                        : null;

            SeasonActionPayloadDto incomingPayload = new(se.Number, se.Title, se.Subtitle, seriesId, se.SeriesName);

            string? resolvedId = canonicalId;
            (int Number, string? Title, string? Subtitle, string? SeriesId, SafeValue<CompletenessStatus?> CompletenessStatus)? existing =
                resolvedId is { } explicitId
                    ? await connection.QuerySingleOrDefaultAsync<(int, string?, string?, string?, SafeValue<CompletenessStatus?>)?>(
                        Sql.Season.SelectExistingById, new { id = explicitId }, transaction)
                    : null;

            // #373: see PlanUniverseAsync's own remark. Season is the sharpest case of it — its
            // natural-key query selects Id alone while SelectExistingById reads Number/Title/Subtitle/
            // SeriesId, so a corrected title on an id-less entry was read by nothing and discarded.
            if (existing is null
                && await connection.ExecuteScalarAsync<Guid?>(Sql.Season.SelectIdBySeriesAndNumber, new { seriesId, number = se.Number }, transaction) is { } byKey)
            {
                resolvedId = byKey.ToCanonicalId();
                existing   = await connection.QuerySingleOrDefaultAsync<(int, string?, string?, string?, SafeValue<CompletenessStatus?>)?>(
                    Sql.Season.SelectExistingById, new { id = resolvedId }, transaction);
            }

            if (existing is { } row)
            {
                string matchedId = resolvedId!;
                SeasonActionPayloadDto existingPayload = new(row.Number, row.Title, row.Subtitle, row.SeriesId);
                Dictionary<string, object?> existingFields = ToSeasonFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToSeasonFieldMap(incomingPayload);

                seasonIndex[SeasonKey(row.SeriesId, row.Number)] = matchedId;
                if (seriesId is not null)
                    seasonIndex[SeasonKey(seriesId, se.Number)] = matchedId;

                HashSet<string> changedFields = [.. existingFields
                    .Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key)))
                    .Select(kv => kv.Key)];

                Dictionary<string, FieldMergeDecision> ruleDecisions = [];
                bool hasStaleRule = false;
                if (policy == DuplicateResolutionPolicy.Review && conflictRules is not null)
                {
                    foreach (string field in existingFields.Keys)
                    {
                        if (!conflictRules.TryResolve(matchedId, field, existingFields[field], incomingFields.GetValueOrDefault(field), out FieldMergeDecision decision, out ConflictRuleOutcome outcome))
                            continue;
                        bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;
                        if (isStale) hasStaleRule = true;
                        else ruleDecisions[field] = decision;
                        if (outcome == ConflictRuleOutcome.Retirable)
                            retirableRuleFindings?.Add(new RetirableRuleFinding(ImportActionEntityTypes.Season, matchedId, field));
                    }
                }

                // #373: an unchanged row is reported as such rather than silently skipped.
                if (changedFields.Count == 0 && ruleDecisions.Count == 0 && !hasStaleRule)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Season, matchedId, incomingPayload, now));
                    continue;
                }

                SeasonActionPayloadDto resolved = policy == DuplicateResolutionPolicy.Skip ? existingPayload : incomingPayload;

                // A matching rule resolves the conflict outright instead of falling through to Pending —
                // the same branch Series, Universe and Character each carry. Season shipped without it
                // (#375), so no ConflictResolutionRule could ever settle a Season disagreement and it
                // would stay Pending on every reseed, forever. Found by the Pending/rule-resolved pair
                // this entity had never had.
                FieldMergeResult? ruleResolved = null;
                if (ruleDecisions.Count > 0)
                {
                    try { ruleResolved = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions); }
                    catch (UnresolvedFieldConflictException) { /* Not every ambiguous field has a matching rule — fall through to normal Pending staging. */ }
                }
                if (ruleResolved is not null)
                {
                    resolved = new SeasonActionPayloadDto(
                        Convert.ToInt32(ruleResolved.MergedFields["number"], System.Globalization.CultureInfo.InvariantCulture),
                        (string?)ruleResolved.MergedFields["title"],
                        (string?)ruleResolved.MergedFields["subtitle"],
                        (string?)ruleResolved.MergedFields["seriesId"],
                        se.SeriesName);
                }

                Dictionary<string, object?> resolvedFields = ToSeasonFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields
                    .Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key)))
                    .Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                ImportActionStatus modifyStatus =
                    CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields) ? ImportActionStatus.Blocked
                    : hasStaleRule ? ImportActionStatus.Stale
                    : policy == DuplicateResolutionPolicy.Review && ruleResolved is null ? ImportActionStatus.Pending
                    : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Season,
                    EntityId = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = modifyStatus == ImportActionStatus.Decided ? JsonSerializer.Serialize(resolved) : null,
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(modifyStatus.ToString(), modifyStatus),
                    DetectedAt = now,
                });
                continue;
            }

            string stableId = canonicalId ?? EntityIdentity.SeasonId(seriesId ?? string.Empty, se.Number);
            seasonIndex[SeasonKey(seriesId, se.Number)] = stableId;

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Season,
                EntityId = stableId,
                IncomingValue = JsonSerializer.Serialize(incomingPayload),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    /// <summary>The (SeriesId, Number) natural key, rendered as one index key. A parentless season keys on its number alone.</summary>
    private static string SeasonKey(string? seriesId, int number) =>
        $"{seriesId?.ToLowerInvariant() ?? string.Empty}|{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static Dictionary<string, object?> ToSeasonFieldMap(SeasonActionPayloadDto payload) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"]   = payload.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["title"]    = payload.Title,
        ["subtitle"] = payload.Subtitle,
        ["seriesId"] = payload.SeriesId,
    };

    // ── #68: StageDirection/SoundCue/Conversation planning ──────────────────
    // All three are Add-only and id-keyed (the file supplies an explicit id, like Quote — not a
    // natural-key-derived EntityIdentity stable id like Source/Character/Person), so planning is
    // just "does a row with this id already exist" — no Modify/merge semantics.

    private static async Task PlanStageDirectionsAsync(
        SqliteConnection connection, IReadOnlyList<SourceStageDirectionDto> stageDirections, string batchId,
        DuplicateResolutionPolicy policy, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (SourceStageDirectionDto sd in stageDirections)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for StageDirection — matched purely by id.
            string canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(sd.Id, out string? sdIdCanonical) ? sdIdCanonical! : sd.Id;

            (string Text, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.StageDirections.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                Dictionary<string, SourceStageDirectionTranslation> emptyTranslations = [];
                // #190: an absent ImageUrl resolves to the existing row's own value — never a change.
                string? incomingImageUrl = sd.ImageUrl.ResolveAgainst(row.ImageUrl);
                StageDirectionActionPayloadDto existingPayload = new StageDirectionActionPayloadDto(row.Text, row.ImageUrl, emptyTranslations);
                StageDirectionActionPayloadDto incomingPayload = new StageDirectionActionPayloadDto(sd.Text, incomingImageUrl, emptyTranslations);
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.StageDirection, canonicalId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                StageDirectionActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new StageDirectionActionPayloadDto((string)mergeResult!.MergedFields["text"]!, (string?)mergeResult.MergedFields["imageUrl"], emptyTranslations),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.StageDirection,
                        EntityId = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                bool isPending = policy == DuplicateResolutionPolicy.Review;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.StageDirection,
                    EntityId = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.StageDirection,
                EntityId = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new StageDirectionActionPayloadDto(sd.Text, sd.ImageUrl.ResolveAgainst(null), sd.Translations)),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    private static async Task PlanSoundCuesAsync(
        SqliteConnection connection, IReadOnlyList<SourceSoundCueDto> soundCues, string batchId,
        DuplicateResolutionPolicy policy, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (SourceSoundCueDto sc in soundCues)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for SoundCue — matched purely by id.
            string canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(sc.Id, out string? scIdCanonical) ? scIdCanonical! : sc.Id;

            (string Text, string? SoundFileUrl, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? SoundFileUrl, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.SoundCues.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                Dictionary<string, SourceSoundCueTranslation> emptyTranslations = [];
                // #190: an absent SoundFileUrl/ImageUrl resolves to the existing row's own value — never a change.
                string? incomingSoundFileUrl = sc.SoundFileUrl.ResolveAgainst(row.SoundFileUrl);
                string? incomingImageUrl = sc.ImageUrl.ResolveAgainst(row.ImageUrl);
                SoundCueActionPayloadDto existingPayload = new SoundCueActionPayloadDto(row.Text, row.SoundFileUrl, row.ImageUrl, emptyTranslations);
                SoundCueActionPayloadDto incomingPayload = new SoundCueActionPayloadDto(sc.Text, incomingSoundFileUrl, incomingImageUrl, emptyTranslations);
                Dictionary<string, object?> existingFields = ToFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToFieldMap(incomingPayload);

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.SoundCue, canonicalId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                SoundCueActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SoundCueActionPayloadDto((string)mergeResult!.MergedFields["text"]!, (string?)mergeResult.MergedFields["soundFileUrl"], (string?)mergeResult.MergedFields["imageUrl"], emptyTranslations),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                Dictionary<string, object?> resolvedFields = ToFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.SoundCue,
                        EntityId = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                bool isPending = policy == DuplicateResolutionPolicy.Review;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.SoundCue,
                    EntityId = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.SoundCue,
                EntityId = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new SoundCueActionPayloadDto(sc.Text, sc.SoundFileUrl.ResolveAgainst(null), sc.ImageUrl.ResolveAgainst(null), sc.Translations)),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }

    /// <summary>
    /// Planned last, after <see cref="PlanStageDirectionsAsync"/>/<see cref="PlanSoundCuesAsync"/> —
    /// a Conversation's <see cref="ConversationActionPayloadDto.Lines"/> reference Quote/StageDirection/
    /// SoundCue ids directly (no id resolution needed, unlike Source/Character/Person), trusting the
    /// referenced rows are staged earlier in the same batch and — per <see cref="PlanAsync"/>'s own
    /// remark — will therefore apply first too.
    /// </summary>
    private static Dictionary<string, object?> ToConversationFieldMap(ConversationActionPayloadDto payload) =>
        new Dictionary<string, object?> { ["description"] = payload.Description };

    private static async Task PlanConversationsAsync(
        SqliteConnection connection, IReadOnlyList<SourceConversationDto> conversations, string batchId,
        DuplicateResolutionPolicy policy, List<ImportActionEntity> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (SourceConversationDto c in conversations)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for Conversation — matched purely by id.
            // Cross-references inside c.Lines (StageDirectionId/SoundCueId/QuoteId) are a separate,
            // not-yet-scoped capture point — tracked in a follow-up issue, not touched here.
            string canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(c.Id, out string? cIdCanonical) ? cIdCanonical! : c.Id;

            (string? Description, SafeValue<CompletenessStatus?> CompletenessStatus)? existing = await connection.QuerySingleOrDefaultAsync<(string? Description, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.Conversations.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                // #190: an absent Description resolves to the existing row's own value — never a change.
                string? incomingDescription = c.Description.ResolveAgainst(row.Description);
                ConversationActionPayloadDto existingPayload = new ConversationActionPayloadDto(row.Description, []);
                ConversationActionPayloadDto incomingPayload = new ConversationActionPayloadDto(incomingDescription, []);
                Dictionary<string, object?> existingFields = ToConversationFieldMap(existingPayload);
                Dictionary<string, object?> incomingFields = ToConversationFieldMap(incomingPayload);

                HashSet<string> changedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];
                // #373: reported as Unchanged rather than silently reused.
                if (changedFields.Count == 0)
                {
                    actions.Add(UnchangedAction(batchId, ImportActionEntityTypes.Conversation, canonicalId, incomingPayload, now));
                    continue;
                }

                bool isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                FieldMergeResult? mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                ConversationActionPayloadDto resolved = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new ConversationActionPayloadDto((string?)mergeResult!.MergedFields["description"], []),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                Dictionary<string, object?> resolvedFields = ToConversationFieldMap(resolved);
                HashSet<string> effectiveChangedFields = [.. existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key)];

                CompletenessStatus currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new ImportActionEntity
                    {
                        BatchId = batchId,
                        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType = ImportActionEntityTypes.Conversation,
                        EntityId = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt = now,
                    });
                    continue;
                }

                bool isPending = policy == DuplicateResolutionPolicy.Review;
                ImportActionStatus status = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new ImportActionEntity
                {
                    BatchId = batchId,
                    ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType = ImportActionEntityTypes.Conversation,
                    EntityId = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt = now,
                });
                continue;
            }

            // #209/#210: a line's QuoteId/StageDirectionId/SoundCueId is a curator-typed reference to
            // another entry in this same file, which is canonicalized to this project's single
            // canonical id form at its own capture point (above, for StageDirections/SoundCues/
            // Conversations; in this method's own quote loop, for Quote) — the reference must be
            // canonicalized identically here too, or ConversationLines' real FOREIGN KEY constraint to
            // Quotes(Id)/StageDirections(Id)/SoundCues(Id) fails outright once the referenced row's own
            // id no longer matches the file's raw casing.
            List<ConversationLinePayloadDto> lines = [.. c.Lines
                .Select(l => new ConversationLinePayloadDto(
                    l.Order, l.Type,
                    l.QuoteId is { } qRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(qRaw, out string? qCanonical) ? qCanonical : l.QuoteId,
                    l.StageDirectionId is { } sdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(sdRaw, out string? sdCanonical) ? sdCanonical : l.StageDirectionId,
                    l.SoundCueId is { } scRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(scRaw, out string? scCanonical) ? scCanonical : l.SoundCueId))];

            actions.Add(new ImportActionEntity
            {
                BatchId = batchId,
                ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType = ImportActionEntityTypes.Conversation,
                EntityId = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new ConversationActionPayloadDto(c.Description.ResolveAgainst(null), lines)),
                Status = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt = now,
            });
        }
    }
}

/// <summary>Staged payload for a Quote Add/Modify <see cref="ImportActionEntity"/> — the 8 mergeable fields plus the resolved Source/Character/Person ids the applier needs, so it never depends on those actions having run first.</summary>
internal sealed class QuoteActionPayloadDto
{
    /// <summary>The quote's mergeable field values.</summary>
    public QuoteConflictFieldsDto Fields { get; init; } = new();

    /// <summary>Resolved Source id — either a real existing id or an <see cref="EntityIdentity"/>-derived stable id for a not-yet-created row.</summary>
    public required string SourceId { get; init; }

    /// <summary>Resolved Character id, or <c>null</c> when the quote has no character.</summary>
    public string? CharacterId { get; init; }

    /// <summary>Resolved Person id, or <c>null</c> when the quote has no author.</summary>
    public string? PersonId { get; init; }
}

/// <summary>Staged payload for a Source Add/Modify <see cref="ImportActionEntity"/> (#162 adds <see cref="Date"/>; #180 adds <see cref="SeriesId"/> — a resolved id, not the file's own <c>seriesName</c> text).</summary>
internal sealed record SourceActionPayloadDto(string Title, string Type, string? Date = null, string? SeriesId = null, string? SeasonId = null);

/// <summary>
/// #374, step 10 — a <see cref="ConflictRuleOutcome.Retirable"/> outcome for one governed field on one
/// entity, collected into <c>PlanAsync</c>'s optional <c>retirableRuleFindings</c> sink so a
/// caller can report it (the seed pipeline logs it; a caller with no interest in this passes nothing
/// and nothing is collected). Advice about the rule file, not a defect in the row it was found on — the
/// row itself still stages <see cref="ImportActionStatus.Stale"/> exactly as before this existed.
/// </summary>
internal sealed record RetirableRuleFinding(string EntityType, string EntityId, string Field);

/// <summary>Staged payload for a Series Add <see cref="ImportActionEntity"/> (#180). <see cref="UniverseId"/> is a resolved id, not the file's own <c>universeName</c> text.</summary>
internal sealed record SeriesActionPayloadDto(string Name, string? UniverseId = null, string? UniverseName = null);

/// <summary>Staged payload for a Universe Add <see cref="ImportActionEntity"/> (#180).</summary>
internal sealed record UniverseActionPayloadDto(string Name);

/// <summary>#375: a Season action's payload — its ordinal, optional title and subtitle, and its resolved Series id.
/// <c>SeriesName</c> travels on the incoming side only, mirroring <see cref="SeriesActionPayloadDto"/>'s own
/// <c>UniverseName</c>, so an apply can re-resolve the parent when Season applies before its Series.</summary>
internal sealed record SeasonActionPayloadDto(int Number, string? Title = null, string? Subtitle = null, string? SeriesId = null, string? SeriesName = null);

/// <summary>
/// Staged payload for a Character Add <see cref="ImportActionEntity"/>. Carries the owning Source's
/// own title/type (denormalized, not just its id) so the applier can defensively ensure the Source
/// row exists before inserting the Character — <c>System_ImportActions</c> rows apply in whatever
/// order the coordinator returns them (no cross-entity-type ordering guarantee), and
/// <c>CharacterSources.SourceId</c> (#179) is a real foreign key. This payload still carries a
/// single <c>SourceId</c> per Character even after #174/ADR 013's global-identity merge algorithm —
/// deliberately kept unchanged: it represents "the specific Source this particular Add action is
/// linking" at the moment the Add is raised, which stays exactly correct under the many-to-many
/// model (a Character can accumulate further <c>CharacterSources</c> links over time via separate
/// resolutions, but each individual Add action only ever introduces one). See ADR 013 Decision 9.
/// </summary>
internal sealed record CharacterActionPayloadDto(string SourceId, string Name, string SourceTitle, string SourceType);

/// <summary>Staged payload for a Person Add <see cref="ImportActionEntity"/>.</summary>
internal sealed record PersonActionPayloadDto(string Name, string? DateOfBirth = null, string? DateOfDeath = null);

/// <summary>Staged payload for a StageDirection Add <see cref="ImportActionEntity"/> (#68).</summary>
internal sealed record StageDirectionActionPayloadDto(
    string Text, string? ImageUrl, IReadOnlyDictionary<string, SourceStageDirectionTranslation> Translations);

/// <summary>Staged payload for a SoundCue Add <see cref="ImportActionEntity"/> (#68).</summary>
internal sealed record SoundCueActionPayloadDto(
    string Text, string? SoundFileUrl, string? ImageUrl, IReadOnlyDictionary<string, SourceSoundCueTranslation> Translations);

/// <summary>One line of a <see cref="ConversationActionPayloadDto"/> — mirrors <see cref="SourceConversationLineDto"/>.</summary>
internal sealed record ConversationLinePayloadDto(
    int Order, ConversationLineType Type, string? QuoteId, string? StageDirectionId, string? SoundCueId);

/// <summary>Staged payload for a Conversation Add <see cref="ImportActionEntity"/> (#68) — carries its full ordered line list, not staged as separate actions (see <see cref="ImportActionPlanner.PlanAsync"/>'s remark).</summary>
internal sealed record ConversationActionPayloadDto(string? Description, IReadOnlyList<ConversationLinePayloadDto> Lines);
