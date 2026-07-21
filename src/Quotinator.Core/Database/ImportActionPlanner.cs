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
/// list of <see cref="SystemImportAction"/> rows, without writing to any domain table. Used
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
    /// Classifies every row in <paramref name="quotes"/> into <see cref="SystemImportAction"/>
    /// rows for the Quote itself and any not-yet-existing Source/Character/Person it references,
    /// then (#68) every not-yet-existing <paramref name="stageDirections"/>/<paramref name="soundCues"/>/
    /// <paramref name="conversations"/> row, in that order — a Conversation's lines reference the
    /// other two, and <see cref="Quotinator.Data.Queries.Sql.SystemImportActions.SelectAllForBatch"/>'s insertion-order
    /// guarantee is what lets apply time trust those referenced rows already exist by the time a
    /// Conversation's own action applies, without needing to defensively re-create them the way
    /// Quote/Character do for Source. Read-only against the database — never writes.
    /// </summary>
    internal static async Task<IReadOnlyList<SystemImportAction>> PlanAsync(
        SqliteConnection connection, IReadOnlyList<SourceQuote> quotes, Guid batchId,
        DuplicateResolutionPolicy policy, SqliteTransaction? transaction = null,
        IReadOnlyList<SourceEntry>? sources = null,
        IReadOnlyList<SourceStageDirection>? stageDirections = null,
        IReadOnlyList<SourceSoundCue>? soundCues = null,
        IReadOnlyList<SourceConversation>? conversations = null,
        IReadOnlyList<PersonEntry>? people = null,
        IReadOnlyList<SeriesEntry>? series = null,
        IReadOnlyList<UniverseEntry>? universe = null)
    {
        var actions        = new List<SystemImportAction>();
        var sourceIndex    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seriesIndex    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var universeIndex  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenQuotes     = new Dictionary<string, SourceQuote>(StringComparer.Ordinal);
        var seenQuoteStatus = new Dictionary<string, CompletenessStatus>(StringComparer.Ordinal);

        var batchIdStr = batchId.ToCanonicalId();
        var now        = DateTime.UtcNow;

        // #180: Universe then Series are planned before Source — a declared series[] entry's own
        // universeName must resolve against an already-built universe index, and a sources[] entry's
        // seriesName must resolve against an already-built series index.
        await PlanUniverseAsync(connection, universe ?? [], batchIdStr, universeIndex, actions, now, transaction);
        await PlanSeriesAsync(connection, series ?? [], batchIdStr, universeIndex, seriesIndex, actions, now, transaction);

        // #162: explicit Source declarations are planned before quotes resolve — a quote may
        // reference a source this same file also declares explicitly, mirroring the existing
        // conversations/stageDirections/soundCues ordering.
        await PlanSourcesAsync(connection, sources ?? [], batchIdStr, policy, sourceIndex, seriesIndex, actions, now, transaction);

        // #173: same reasoning as Source above — a quote's author may reference a person this same
        // file also declares explicitly via people[].
        await PlanPeopleAsync(connection, people ?? [], batchIdStr, policy, personIndex, actions, now, transaction);

        foreach (var rawQuote in quotes)
        {
            // #210: canonicalize a file-authored Quotes.Id to lowercase at the single earliest point of
            // capture, matching this project's single canonical id convention (ADR 012, GuidHandler).
            // Every later reference to q.Id in this iteration (seenQuotes, EntityId, the resolved
            // SourceQuote threaded through QuoteFieldMerge) is automatically canonical once this
            // substitution is made. SourceQuote is a plain class with init-only properties, not a
            // record, so a corrected copy is built the same way ApplyMergedFields already does above,
            // not via a `with` expression.
            var q = EntityIdCanonicalizer.TryCanonicalizeLowercase(rawQuote.Id, out var canonicalQuoteId)
                ? new SourceQuote
                {
                    Id               = canonicalQuoteId!,
                    QuoteText        = rawQuote.QuoteText,
                    OriginalLanguage = rawQuote.OriginalLanguage,
                    Source           = rawQuote.Source,
                    Date             = rawQuote.Date,
                    Character        = rawQuote.Character,
                    Author           = rawQuote.Author,
                    Type             = rawQuote.Type,
                    Genres           = rawQuote.Genres,
                    Translations     = rawQuote.Translations,
                }
                : rawQuote;

            var sourceId    = await ResolveSourceAsync(connection, q, sourceIndex, batchIdStr, actions, now, transaction);
            var characterId = await ResolveCharacterAsync(connection, q, sourceId, characterIndex, batchIdStr, actions, now, transaction);
            var personId    = await ResolvePersonAsync(connection, q, personIndex, batchIdStr, actions, now, transaction);

            var existing = seenQuotes.TryGetValue(q.Id, out var firstInFile)
                ? new QuoteSeedWriter.ExistingQuoteFields(QuoteFieldMerge.ToFieldMap(firstInFile), batchIdStr, seenQuoteStatus.GetValueOrDefault(q.Id, CompletenessStatus.Incomplete))
                : await QuoteSeedWriter.TryGetExistingFieldsAsync(connection, q.Id, transaction);

            if (existing is null)
            {
                seenQuotes[q.Id] = q;
                seenQuoteStatus[q.Id] = CompletenessStatus.Incomplete;
                var payload = new QuoteActionPayload
                {
                    Fields    = QuoteFieldMerge.ToDto(q),
                    SourceId  = sourceId,
                    CharacterId = characterId,
                    PersonId  = personId,
                };

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchIdStr,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                    EntityType    = ImportActionEntityTypes.Quote,
                    EntityId      = q.Id,
                    IncomingValue = JsonSerializer.Serialize(payload),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                    DetectedAt    = now,
                });
                continue;
            }

            var existingFields  = existing.Value.Fields;
            var existingBatchId = existing.Value.ImportBatchId;
            var incomingFields  = QuoteFieldMerge.ToFieldMap(q);

            var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
            var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
            // Skip's resolved payload is the existing row's own values (nothing changes) — not the
            // incoming row's, which is what "resolved" would otherwise default to. The applier's Quote
            // case checks AppliedPolicy==Skip and skips the write/changelog entirely regardless, but
            // storing the accurate "nothing changes" payload here keeps GET /import/actions honest.
            var resolved = policy switch
            {
                DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs => QuoteFieldMerge.ApplyMergedFields(mergeResult!.MergedFields, q),
                DuplicateResolutionPolicy.Skip => QuoteFieldMerge.ApplyMergedFields(existingFields, q),
                _ => q,
            };

            // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved), not
            // the raw incoming value — Skip's resolved value always equals existingFields (nothing
            // written), so Skip can never block a Complete quote; a merge policy only blocks on
            // fields the merge itself would actually change.
            var resolvedFields     = QuoteFieldMerge.ToFieldMap(resolved);
            var effectiveChanged   = new HashSet<string>(
                existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

            if (CompletenessGuard.ShouldBlock(existing.Value.CompletenessStatus, effectiveChanged))
            {
                actions.Add(new SystemImportAction
                {
                    BatchId         = batchIdStr,
                    ExistingBatchId = existingBatchId,
                    ActionType      = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType      = ImportActionEntityTypes.Quote,
                    EntityId        = q.Id,
                    ExistingValue   = JsonSerializer.Serialize(new QuoteActionPayload { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    IncomingValue   = JsonSerializer.Serialize(new QuoteActionPayload { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                    Status          = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                    DetectedAt      = now,
                });
                continue;
            }

            // Review is the only policy left Pending; every other policy is Decided at detection
            // time, with the final resolved values already computed so apply never needs policy logic.
            var isPending = policy == DuplicateResolutionPolicy.Review;
            var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

            actions.Add(new SystemImportAction
            {
                BatchId         = batchIdStr,
                ExistingBatchId = existingBatchId,
                ActionType      = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                EntityType      = ImportActionEntityTypes.Quote,
                EntityId        = q.Id,
                ExistingValue   = JsonSerializer.Serialize(new QuoteActionPayload { Fields = QuoteFieldMerge.ToDto(existingFields), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                IncomingValue   = JsonSerializer.Serialize(new QuoteActionPayload { Fields = QuoteFieldMerge.ToDto(q), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                MergedFields    = isPending ? null : JsonSerializer.Serialize(new QuoteActionPayload { Fields = QuoteFieldMerge.ToDto(resolved), SourceId = sourceId, CharacterId = characterId, PersonId = personId }),
                AppliedPolicy   = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                Status          = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                DetectedAt      = now,
            });

            if (!isPending)
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

    private static async Task<string> ResolveSourceAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, string> index,
        string batchId, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        var typeStr = q.Type.ToString();
        var key     = $"{q.Source}|{typeStr}";
        if (index.TryGetValue(key, out var existing)) return existing;

        // #162: raw string, not Guid?-typed — a natural-key-matched row's id may now be an explicit,
        // not-necessarily-canonically-cased file-authored id (from a sources[] entry), not only a
        // Guid.NewGuid()/EntityIdentity-derived one. Guid has no memory of original string casing —
        // ToString("D") always renders lowercase regardless of what was actually stored — so
        // round-tripping through Guid? and re-casing would silently produce a string that no longer
        // matches the real row's id if that row predates a casing-convention change (this project has
        // been through two: see ADR 012's revision history).
        var existingId = await connection.ExecuteScalarAsync<string?>(
            Sql.Sources.SelectIdByTitleAndType, new { title = q.Source, type = typeStr }, transaction);
        if (existingId is { } foundId)
        {
            index[key] = foundId;
            return foundId;
        }

        var stableId = EntityIdentity.SourceId(q.Source, typeStr);
        index[key] = stableId;

        actions.Add(new SystemImportAction
        {
            BatchId       = batchId,
            ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType    = ImportActionEntityTypes.Source,
            EntityId      = stableId,
            IncomingValue = JsonSerializer.Serialize(new SourceActionPayload(q.Source, typeStr, q.Date)),
            Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt    = now,
        });

        return stableId;
    }

    private static async Task<string?> ResolveCharacterAsync(
        SqliteConnection connection, SourceQuote q, string sourceId, Dictionary<string, string> index,
        string batchId, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        var sourceTypeStr = q.Type.ToString();
        if (string.IsNullOrWhiteSpace(q.Character)) return null;

        var key = $"{sourceId}|{q.Character}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.Characters.SelectIdBySourceAndName, new { sourceId, name = q.Character }, transaction);
        if (existingId is { } foundId)
        {
            var idStr = foundId.ToCanonicalId();
            index[key] = idStr;
            return idStr;
        }

        var stableId = EntityIdentity.CharacterId(sourceId, q.Character);
        index[key] = stableId;

        actions.Add(new SystemImportAction
        {
            BatchId       = batchId,
            ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType    = ImportActionEntityTypes.Character,
            EntityId      = stableId,
            IncomingValue = JsonSerializer.Serialize(new CharacterActionPayload(sourceId, q.Character, q.Source, sourceTypeStr)),
            Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt    = now,
        });

        return stableId;
    }

    private static async Task<string?> ResolvePersonAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, string> index,
        string batchId, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        if (string.IsNullOrWhiteSpace(q.Author)) return null;

        if (index.TryGetValue(q.Author, out var existing)) return existing;

        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.People.SelectIdByName, new { name = q.Author }, transaction);
        if (existingId is { } foundId)
        {
            var idStr = foundId.ToCanonicalId();
            index[q.Author] = idStr;
            return idStr;
        }

        var stableId = EntityIdentity.PersonId(q.Author);
        index[q.Author] = stableId;

        actions.Add(new SystemImportAction
        {
            BatchId       = batchId,
            ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
            EntityType    = ImportActionEntityTypes.Person,
            EntityId      = stableId,
            IncomingValue = JsonSerializer.Serialize(new PersonActionPayload(q.Author)),
            Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
            DetectedAt    = now,
        });

        return stableId;
    }

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync, both feed the same decide-time <c>FieldMergeResolver</c> field-name vocabulary. <c>seriesId</c> added by #180.</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(SourceActionPayload payload) =>
        new Dictionary<string, object?> { ["title"] = payload.Title, ["type"] = payload.Type, ["date"] = payload.Date, ["seriesId"] = payload.SeriesId };

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#171).</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(StageDirectionActionPayload payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["imageUrl"] = payload.ImageUrl };

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#172).</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(SoundCueActionPayload payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["soundFileUrl"] = payload.SoundFileUrl, ["imageUrl"] = payload.ImageUrl };

    // ── #162: explicit Source planning ───────────────────────────────────────
    // Unlike ResolveSourceAsync (natural-key match only, never compares Date/Title/Type once
    // matched), a declared sources[] entry is matched by its own explicit id first — decoupling
    // matching from content, so Title/Type/Date can all be freely corrected once a Source has
    // adopted this model.

    private static async Task PlanSourcesAsync(
        SqliteConnection connection, IReadOnlyList<SourceEntry> sources, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> sourceIndex,
        Dictionary<string, string> seriesIndex, List<SystemImportAction> actions, DateTime now,
        SqliteTransaction? transaction)
    {
        foreach (var s in sources)
        {
            var typeStr = s.Type.ToString();

            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured — every later reference to the file's id (lookup, matchedId, addId) uses this
            // canonicalized form instead of the file's raw casing. A malformed or absent id passes
            // through unchanged; general id-format validation is out of scope here.
            var canonicalId = s.Id is { } sIdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(sIdRaw, out var sIdCanonical)
                ? sIdCanonical
                : s.Id;

            // #180/#190: seriesName resolution is Optional-aware — an absent seriesName stays Absent
            // (never touches the existing Series link, resolved per-branch below via ResolveAgainst);
            // an explicit null stays a genuine clear; a real name resolves via the same-batch index
            // first (populated by PlanSeriesAsync, which must run before this method), falling back to
            // a DB lookup for a Series declared in an earlier batch. A name with no match anywhere
            // resolves to null — silently dropped, same as PlanSeriesAsync's own dangling-universeName
            // treatment.
            var resolvedSeriesId = Optional<string>.Absent;
            if (s.SeriesName.HasValue)
            {
                var seriesName = s.SeriesName.Value;
                resolvedSeriesId = seriesName is null
                    ? Optional<string>.Of(null)
                    : Optional<string>.Of(seriesIndex.TryGetValue(seriesName, out var indexed)
                        ? indexed
                        : await connection.ExecuteScalarAsync<Guid?>(Sql.Series.SelectIdByName, new { name = seriesName }, transaction) is { } found
                            ? found.ToCanonicalId()
                            : null);
            }

            // #162's correction shape: an entry carrying an explicit id is matched by it first. An
            // entry omitting one (#180's enrichment shape) skips straight to the natural-key path below.
            var existing = canonicalId is { } explicitId
                ? await connection.QuerySingleOrDefaultAsync<(string Title, string Type, string? Date, string? SeriesId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                    Sql.Sources.SelectExistingById, new { id = explicitId }, transaction)
                : null;

            if (existing is { } row)
            {
                var matchedId       = canonicalId!; // Non-null by construction: `existing` is only set when canonicalId is.
                // #190: an absent Date/SeriesName resolves to the existing row's own value — never a
                // change, under any policy. See OptionalExtensions.ResolveAgainst.
                var incomingDate     = s.Date.ResolveAgainst(row.Date);
                var incomingSeriesId = resolvedSeriesId.ResolveAgainst(row.SeriesId);
                var existingPayload = new SourceActionPayload(row.Title, row.Type, row.Date, row.SeriesId);
                var incomingPayload = new SourceActionPayload(s.Title, typeStr, incomingDate, incomingSeriesId);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                // The corrected Title/Type is what a same-batch quote referencing this Source should
                // resolve to — indexed regardless of whether this ends up changed/blocked/unchanged.
                sourceIndex[$"{s.Title}|{typeStr}"] = matchedId;

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SourceActionPayload((string)mergeResult!.MergedFields["title"]!, (string)mergeResult.MergedFields["type"]!, (string?)mergeResult.MergedFields["date"], (string?)mergeResult.MergedFields["seriesId"]),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above — Skip's resolved
                // value is always existingPayload (nothing written), so Skip can never block a
                // Complete row; a merge policy only blocks on fields the merge itself would change.
                var resolvedFields        = ToFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.Source,
                        EntityId      = matchedId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var isPending = policy == DuplicateResolutionPolicy.Review;
                var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.Source,
                    EntityId      = matchedId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            // Falls back to natural-key (title+type): either the entry omits an explicit id (#180's
            // enrichment shape) or it carries one that matches no row yet (a not-yet-migrated row —
            // #162's scope boundary).
            var existingByKey = await connection.QuerySingleOrDefaultAsync<(string Id, string? Date, string? SeriesId, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.Sources.SelectExistingByTitleAndType, new { title = s.Title, type = typeStr }, transaction);

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
                var keyIncomingDate     = s.Date.ResolveAgainst(keyRow.Date);
                var keyIncomingSeriesId = resolvedSeriesId.ResolveAgainst(keyRow.SeriesId);
                var keyExistingPayload = new SourceActionPayload(s.Title, typeStr, keyRow.Date, keyRow.SeriesId);
                var keyIncomingPayload = new SourceActionPayload(s.Title, typeStr, keyIncomingDate, keyIncomingSeriesId);
                var keyExistingFields  = ToFieldMap(keyExistingPayload);
                var keyIncomingFields  = ToFieldMap(keyIncomingPayload);

                var changedFields = new HashSet<string>(
                    keyExistingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, keyIncomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as the explicit-id branch above.

                // #190 drive-by fix: this branch previously never consulted FieldMergeResolver.Resolve
                // for MergeOurs/MergeTheirs at all — it always took keyIncomingPayload for any policy
                // but Skip, so MergeOurs could silently overwrite an existing Series link even though
                // its own contract is "existing wins on a genuine conflict". Now matches the
                // explicit-id branch's shape exactly.
                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(keyExistingFields, keyIncomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SourceActionPayload((string)mergeResult!.MergedFields["title"]!, (string)mergeResult.MergedFields["type"]!, (string?)mergeResult.MergedFields["date"], (string?)mergeResult.MergedFields["seriesId"]),
                    DuplicateResolutionPolicy.Skip => keyExistingPayload,
                    _ => keyIncomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                var resolvedFields        = ToFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    keyExistingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var keyCurrentStatus = keyRow.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(keyCurrentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.Source,
                        EntityId      = keyRow.Id,
                        ExistingValue = JsonSerializer.Serialize(keyExistingPayload),
                        IncomingValue = JsonSerializer.Serialize(keyIncomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var keyIsPending = policy == DuplicateResolutionPolicy.Review;
                var keyStatus    = keyIsPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.Source,
                    EntityId      = keyRow.Id,
                    ExistingValue = JsonSerializer.Serialize(keyExistingPayload),
                    IncomingValue = JsonSerializer.Serialize(keyIncomingPayload),
                    MergedFields  = keyIsPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(keyStatus.ToString(), keyStatus),
                    DetectedAt    = now,
                });
                continue;
            }

            // No match by id or natural key — a genuine Add. With no explicit id in the file, the
            // EntityIdentity-derived stable id is used: the same value ResolveSourceAsync would
            // independently compute for a quote referencing this same title/type, so both resolve to
            // one row rather than two.
            var addId = canonicalId ?? EntityIdentity.SourceId(s.Title, typeStr);

            // Indexed so a same-batch quote referencing this exact title/type resolves to this same
            // new row, instead of ResolveSourceAsync independently deriving its own EntityIdentity
            // stable id (which would differ from a file-declared id).
            sourceIndex[$"{s.Title}|{typeStr}"] = addId;

            // #190: no existing row to preserve, so ResolveAgainst(null) — an absent property simply
            // resolves to null, matching this project's existing Add-path behaviour exactly.
            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Source,
                EntityId      = addId,
                IncomingValue = JsonSerializer.Serialize(new SourceActionPayload(s.Title, typeStr, s.Date.ResolveAgainst(null), resolvedSeriesId.ResolveAgainst(null))),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    /// <summary>Same key names as <see cref="Quotinator.Core.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#173).</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(PersonActionPayload payload) =>
        new Dictionary<string, object?> { ["name"] = payload.Name, ["dateOfBirth"] = payload.DateOfBirth, ["dateOfDeath"] = payload.DateOfDeath };

    // ── #173: explicit Person planning ───────────────────────────────────────
    // Same shape as PlanSourcesAsync — id-first lookup, natural-key fallback for a not-yet-migrated
    // row, personIndex populated in both branches so a same-batch quote's author resolves to the
    // declared row instead of independently deriving its own EntityIdentity stable id (the #162
    // test-7a-shaped threading risk).

    private static async Task PlanPeopleAsync(
        SqliteConnection connection, IReadOnlyList<PersonEntry> people, string batchId,
        DuplicateResolutionPolicy policy, Dictionary<string, string> personIndex,
        List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var p in people)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured — PersonEntry.Id is required, so there is no absent case to preserve.
            var canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(p.Id, out var pIdCanonical) ? pIdCanonical! : p.Id;

            var existing = await connection.QuerySingleOrDefaultAsync<(string Name, string? DateOfBirth, string? DateOfDeath, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.People.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                // #190: an absent DateOfBirth/DateOfDeath resolves to the existing row's own value —
                // never a change, under any policy.
                var incomingDob = p.DateOfBirth.ResolveAgainst(row.DateOfBirth);
                var incomingDod = p.DateOfDeath.ResolveAgainst(row.DateOfDeath);
                var existingPayload = new PersonActionPayload(row.Name, row.DateOfBirth, row.DateOfDeath);
                var incomingPayload = new PersonActionPayload(p.Name, incomingDob, incomingDod);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                personIndex[p.Name] = canonicalId;

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new PersonActionPayload((string)mergeResult!.MergedFields["name"]!, (string?)mergeResult.MergedFields["dateOfBirth"], (string?)mergeResult.MergedFields["dateOfDeath"]),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                var resolvedFields        = ToFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.Person,
                        EntityId      = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var isPending = policy == DuplicateResolutionPolicy.Review;
                var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.Person,
                    EntityId      = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            // Falls back to natural-key: a not-yet-migrated row found only by Name — Name/DateOfBirth/
            // DateOfDeath correction isn't available on it yet (#173's scope boundary, same as #162's).
            var matchesByKey = await connection.ExecuteScalarAsync<Guid?>(
                Sql.People.SelectIdByName, new { name = p.Name }, transaction);
            if (matchesByKey is not null)
            {
                personIndex[p.Name] = matchesByKey.Value.ToCanonicalId();
                continue;
            }

            personIndex[p.Name] = canonicalId;

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Person,
                EntityId      = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new PersonActionPayload(p.Name, p.DateOfBirth.ResolveAgainst(null), p.DateOfDeath.ResolveAgainst(null))),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    // ── #180: explicit Universe/Series planning ──────────────────────────────
    // Add-only, natural-key-keyed by Name — no explicit id in the file (unlike Source/Person), since
    // EntityIdentity.SeriesId/UniverseId derives it from the name alone. No Modify/merge semantics:
    // a Universe/Series entry that already exists by name is simply reused (indexed for a dependent
    // Series/Source to resolve against), never diffed or re-staged.

    private static async Task PlanUniverseAsync(
        SqliteConnection connection, IReadOnlyList<UniverseEntry> universes, string batchId,
        Dictionary<string, string> universeIndex, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var u in universes)
        {
            var matchesByKey = await connection.ExecuteScalarAsync<Guid?>(
                Sql.Universe.SelectIdByName, new { name = u.Name }, transaction);
            if (matchesByKey is not null)
            {
                universeIndex[u.Name] = matchesByKey.Value.ToCanonicalId();
                continue;
            }

            var stableId = EntityIdentity.UniverseId(u.Name);
            universeIndex[u.Name] = stableId;

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Universe,
                EntityId      = stableId,
                IncomingValue = JsonSerializer.Serialize(new UniverseActionPayload(u.Name)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
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
        SqliteConnection connection, IReadOnlyList<SeriesEntry> series, string batchId,
        Dictionary<string, string> universeIndex, Dictionary<string, string> seriesIndex,
        List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var s in series)
        {
            var matchesByKey = await connection.ExecuteScalarAsync<Guid?>(
                Sql.Series.SelectIdByName, new { name = s.Name }, transaction);
            if (matchesByKey is not null)
            {
                seriesIndex[s.Name] = matchesByKey.Value.ToCanonicalId();
                continue;
            }

            string? universeId = null;
            if (s.UniverseName is { } universeName)
                universeId = universeIndex.TryGetValue(universeName, out var indexed)
                    ? indexed
                    : await connection.ExecuteScalarAsync<Guid?>(Sql.Universe.SelectIdByName, new { name = universeName }, transaction) is { } found
                        ? found.ToCanonicalId()
                        : null;

            var stableId = EntityIdentity.SeriesId(s.Name);
            seriesIndex[s.Name] = stableId;

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Series,
                EntityId      = stableId,
                IncomingValue = JsonSerializer.Serialize(new SeriesActionPayload(s.Name, universeId)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    // ── #68: StageDirection/SoundCue/Conversation planning ──────────────────
    // All three are Add-only and id-keyed (the file supplies an explicit id, like Quote — not a
    // natural-key-derived EntityIdentity stable id like Source/Character/Person), so planning is
    // just "does a row with this id already exist" — no Modify/merge semantics.

    private static async Task PlanStageDirectionsAsync(
        SqliteConnection connection, IReadOnlyList<SourceStageDirection> stageDirections, string batchId,
        DuplicateResolutionPolicy policy, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var sd in stageDirections)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for StageDirection — matched purely by id.
            var canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(sd.Id, out var sdIdCanonical) ? sdIdCanonical! : sd.Id;

            var existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.StageDirections.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                var emptyTranslations = new Dictionary<string, SourceStageDirectionTranslation>();
                // #190: an absent ImageUrl resolves to the existing row's own value — never a change.
                var incomingImageUrl = sd.ImageUrl.ResolveAgainst(row.ImageUrl);
                var existingPayload = new StageDirectionActionPayload(row.Text, row.ImageUrl, emptyTranslations);
                var incomingPayload = new StageDirectionActionPayload(sd.Text, incomingImageUrl, emptyTranslations);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new StageDirectionActionPayload((string)mergeResult!.MergedFields["text"]!, (string?)mergeResult.MergedFields["imageUrl"], emptyTranslations),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                var resolvedFields        = ToFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.StageDirection,
                        EntityId      = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var isPending = policy == DuplicateResolutionPolicy.Review;
                var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.StageDirection,
                    EntityId      = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.StageDirection,
                EntityId      = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new StageDirectionActionPayload(sd.Text, sd.ImageUrl.ResolveAgainst(null), sd.Translations)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    private static async Task PlanSoundCuesAsync(
        SqliteConnection connection, IReadOnlyList<SourceSoundCue> soundCues, string batchId,
        DuplicateResolutionPolicy policy, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var sc in soundCues)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for SoundCue — matched purely by id.
            var canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(sc.Id, out var scIdCanonical) ? scIdCanonical! : sc.Id;

            var existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? SoundFileUrl, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.SoundCues.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                var emptyTranslations = new Dictionary<string, SourceSoundCueTranslation>();
                // #190: an absent SoundFileUrl/ImageUrl resolves to the existing row's own value — never a change.
                var incomingSoundFileUrl = sc.SoundFileUrl.ResolveAgainst(row.SoundFileUrl);
                var incomingImageUrl     = sc.ImageUrl.ResolveAgainst(row.ImageUrl);
                var existingPayload = new SoundCueActionPayload(row.Text, row.SoundFileUrl, row.ImageUrl, emptyTranslations);
                var incomingPayload = new SoundCueActionPayload(sc.Text, incomingSoundFileUrl, incomingImageUrl, emptyTranslations);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SoundCueActionPayload((string)mergeResult!.MergedFields["text"]!, (string?)mergeResult.MergedFields["soundFileUrl"], (string?)mergeResult.MergedFields["imageUrl"], emptyTranslations),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                var resolvedFields        = ToFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.SoundCue,
                        EntityId      = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var isPending = policy == DuplicateResolutionPolicy.Review;
                var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.SoundCue,
                    EntityId      = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.SoundCue,
                EntityId      = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new SoundCueActionPayload(sc.Text, sc.SoundFileUrl.ResolveAgainst(null), sc.ImageUrl.ResolveAgainst(null), sc.Translations)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    /// <summary>
    /// Planned last, after <see cref="PlanStageDirectionsAsync"/>/<see cref="PlanSoundCuesAsync"/> —
    /// a Conversation's <see cref="ConversationActionPayload.Lines"/> reference Quote/StageDirection/
    /// SoundCue ids directly (no id resolution needed, unlike Source/Character/Person), trusting the
    /// referenced rows are staged earlier in the same batch and — per <see cref="PlanAsync"/>'s own
    /// remark — will therefore apply first too.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ToConversationFieldMap(ConversationActionPayload payload) =>
        new Dictionary<string, object?> { ["description"] = payload.Description };

    private static async Task PlanConversationsAsync(
        SqliteConnection connection, IReadOnlyList<SourceConversation> conversations, string batchId,
        DuplicateResolutionPolicy policy, List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var c in conversations)
        {
            // #209: canonicalize once, at the single earliest point this entry's explicit id is
            // captured. No natural-key fallback exists for Conversation — matched purely by id.
            // Cross-references inside c.Lines (StageDirectionId/SoundCueId/QuoteId) are a separate,
            // not-yet-scoped capture point — tracked in a follow-up issue, not touched here.
            var canonicalId = EntityIdCanonicalizer.TryCanonicalizeLowercase(c.Id, out var cIdCanonical) ? cIdCanonical! : c.Id;

            var existing = await connection.QuerySingleOrDefaultAsync<(string? Description, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.Conversations.SelectExistingById, new { id = canonicalId }, transaction);

            if (existing is { } row)
            {
                // #190: an absent Description resolves to the existing row's own value — never a change.
                var incomingDescription = c.Description.ResolveAgainst(row.Description);
                var existingPayload = new ConversationActionPayload(row.Description, []);
                var incomingPayload = new ConversationActionPayload(incomingDescription, []);
                var existingFields  = ToConversationFieldMap(existingPayload);
                var incomingFields  = ToConversationFieldMap(incomingPayload);

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new ConversationActionPayload((string?)mergeResult!.MergedFields["description"], []),
                    DuplicateResolutionPolicy.Skip => existingPayload,
                    _ => incomingPayload,
                };

                // #168: ShouldBlock is evaluated against what would actually be WRITTEN (resolved),
                // not the raw incoming value used for the "unchanged" check above.
                var resolvedFields        = ToConversationFieldMap(resolved);
                var effectiveChangedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, resolvedFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));

                var currentStatus = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
                if (CompletenessGuard.ShouldBlock(currentStatus, effectiveChangedFields))
                {
                    actions.Add(new SystemImportAction
                    {
                        BatchId       = batchId,
                        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                        EntityType    = ImportActionEntityTypes.Conversation,
                        EntityId      = canonicalId,
                        ExistingValue = JsonSerializer.Serialize(existingPayload),
                        IncomingValue = JsonSerializer.Serialize(incomingPayload),
                        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
                        DetectedAt    = now,
                    });
                    continue;
                }

                var isPending = policy == DuplicateResolutionPolicy.Review;
                var status    = isPending ? ImportActionStatus.Pending : ImportActionStatus.Decided;

                actions.Add(new SystemImportAction
                {
                    BatchId       = batchId,
                    ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
                    EntityType    = ImportActionEntityTypes.Conversation,
                    EntityId      = canonicalId,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
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
            var lines = c.Lines
                .Select(l => new ConversationLinePayload(
                    l.Order, l.Type,
                    l.QuoteId is { } qRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(qRaw, out var qCanonical) ? qCanonical : l.QuoteId,
                    l.StageDirectionId is { } sdRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(sdRaw, out var sdCanonical) ? sdCanonical : l.StageDirectionId,
                    l.SoundCueId is { } scRaw && EntityIdCanonicalizer.TryCanonicalizeLowercase(scRaw, out var scCanonical) ? scCanonical : l.SoundCueId))
                .ToList();

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Conversation,
                EntityId      = canonicalId,
                IncomingValue = JsonSerializer.Serialize(new ConversationActionPayload(c.Description.ResolveAgainst(null), lines)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }
}

/// <summary>Staged payload for a Quote Add/Modify <see cref="SystemImportAction"/> — the 8 mergeable fields plus the resolved Source/Character/Person ids the applier needs, so it never depends on those actions having run first.</summary>
internal sealed class QuoteActionPayload
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

/// <summary>Staged payload for a Source Add/Modify <see cref="SystemImportAction"/> (#162 adds <see cref="Date"/>; #180 adds <see cref="SeriesId"/> — a resolved id, not the file's own <c>seriesName</c> text).</summary>
internal sealed record SourceActionPayload(string Title, string Type, string? Date = null, string? SeriesId = null);

/// <summary>Staged payload for a Series Add <see cref="SystemImportAction"/> (#180). <see cref="UniverseId"/> is a resolved id, not the file's own <c>universeName</c> text.</summary>
internal sealed record SeriesActionPayload(string Name, string? UniverseId = null);

/// <summary>Staged payload for a Universe Add <see cref="SystemImportAction"/> (#180).</summary>
internal sealed record UniverseActionPayload(string Name);

/// <summary>
/// Staged payload for a Character Add <see cref="SystemImportAction"/>. Carries the owning Source's
/// own title/type (denormalized, not just its id) so the applier can defensively ensure the Source
/// row exists before inserting the Character — <c>System_ImportActions</c> rows apply in whatever
/// order the coordinator returns them (no cross-entity-type ordering guarantee), and
/// <c>CharacterSources.SourceId</c> (#179) is a real foreign key. This payload still carries a
/// single <c>SourceId</c> per Character, unchanged by #179 — Character's many-to-many relationship
/// to Source is #174's concern, not this one's (#179 only changes the storage mechanism, not the
/// matching/payload shape).
/// </summary>
internal sealed record CharacterActionPayload(string SourceId, string Name, string SourceTitle, string SourceType);

/// <summary>Staged payload for a Person Add <see cref="SystemImportAction"/>.</summary>
internal sealed record PersonActionPayload(string Name, string? DateOfBirth = null, string? DateOfDeath = null);

/// <summary>Staged payload for a StageDirection Add <see cref="SystemImportAction"/> (#68).</summary>
internal sealed record StageDirectionActionPayload(
    string Text, string? ImageUrl, IReadOnlyDictionary<string, SourceStageDirectionTranslation> Translations);

/// <summary>Staged payload for a SoundCue Add <see cref="SystemImportAction"/> (#68).</summary>
internal sealed record SoundCueActionPayload(
    string Text, string? SoundFileUrl, string? ImageUrl, IReadOnlyDictionary<string, SourceSoundCueTranslation> Translations);

/// <summary>One line of a <see cref="ConversationActionPayload"/> — mirrors <see cref="SourceConversationLine"/>.</summary>
internal sealed record ConversationLinePayload(
    int Order, ConversationLineType Type, string? QuoteId, string? StageDirectionId, string? SoundCueId);

/// <summary>Staged payload for a Conversation Add <see cref="SystemImportAction"/> (#68) — carries its full ordered line list, not staged as separate actions (see <see cref="ImportActionPlanner.PlanAsync"/>'s remark).</summary>
internal sealed record ConversationActionPayload(string? Description, IReadOnlyList<ConversationLinePayload> Lines);
