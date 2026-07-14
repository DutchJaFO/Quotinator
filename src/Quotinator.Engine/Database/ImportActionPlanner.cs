using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Engine.Helpers;
using Quotinator.Engine.Queries;

namespace Quotinator.Engine.Database;

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
        IReadOnlyList<PersonEntry>? people = null)
    {
        var actions        = new List<SystemImportAction>();
        var sourceIndex    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenQuotes     = new Dictionary<string, SourceQuote>(StringComparer.Ordinal);
        var seenQuoteStatus = new Dictionary<string, CompletenessStatus>(StringComparer.Ordinal);

        var batchIdStr = batchId.ToString("D").ToUpperInvariant();
        var now        = DateTime.UtcNow;

        // #162: explicit Source declarations are planned before quotes resolve — a quote may
        // reference a source this same file also declares explicitly, mirroring the existing
        // conversations/stageDirections/soundCues ordering.
        await PlanSourcesAsync(connection, sources ?? [], batchIdStr, policy, sourceIndex, actions, now, transaction);

        // #173: same reasoning as Source above — a quote's author may reference a person this same
        // file also declares explicitly via people[].
        await PlanPeopleAsync(connection, people ?? [], batchIdStr, policy, personIndex, actions, now, transaction);

        foreach (var q in quotes)
        {
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
        // not-necessarily-uppercase file-authored id (from a sources[] entry), not only a
        // Guid.NewGuid()/EntityIdentity-derived one. Guid has no memory of original string casing —
        // ToString("D") always renders lowercase regardless of what was actually stored — so
        // round-tripping through Guid? and re-casing would silently produce a different string than
        // the real row's id, exactly the bug ClearStaleAddTargetsAsync's own remarks warn about for
        // Quote ids.
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
            IncomingValue = JsonSerializer.Serialize(new SourceActionPayload(q.Source, typeStr)),
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
            var idStr = foundId.ToString("D").ToUpperInvariant();
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
            var idStr = foundId.ToString("D").ToUpperInvariant();
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

    /// <summary>Same key names as <see cref="Quotinator.Engine.Services.SqliteImportActionService"/>'s own private overload — must stay in sync, both feed the same decide-time <c>FieldMergeResolver</c> field-name vocabulary.</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(SourceActionPayload payload) =>
        new Dictionary<string, object?> { ["title"] = payload.Title, ["type"] = payload.Type, ["date"] = payload.Date };

    /// <summary>Same key names as <see cref="Quotinator.Engine.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#171).</summary>
    private static IReadOnlyDictionary<string, object?> ToFieldMap(StageDirectionActionPayload payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["imageUrl"] = payload.ImageUrl };

    /// <summary>Same key names as <see cref="Quotinator.Engine.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#172).</summary>
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
        List<SystemImportAction> actions, DateTime now, SqliteTransaction? transaction)
    {
        foreach (var s in sources)
        {
            var typeStr = s.Type.ToString();
            var existing = await connection.QuerySingleOrDefaultAsync<(string Title, string Type, string? Date, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.Sources.SelectExistingById, new { id = s.Id }, transaction);

            if (existing is { } row)
            {
                var existingPayload = new SourceActionPayload(row.Title, row.Type, row.Date);
                var incomingPayload = new SourceActionPayload(s.Title, typeStr, s.Date);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                // The corrected Title/Type is what a same-batch quote referencing this Source should
                // resolve to — indexed regardless of whether this ends up changed/blocked/unchanged.
                sourceIndex[$"{s.Title}|{typeStr}"] = s.Id;

                var changedFields = new HashSet<string>(
                    existingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, incomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
                if (changedFields.Count == 0) continue; // Unchanged — silent reuse, same as a natural-key match.

                var isMerge     = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
                var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;
                var resolved    = policy switch
                {
                    DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs =>
                        new SourceActionPayload((string)mergeResult!.MergedFields["title"]!, (string)mergeResult.MergedFields["type"]!, (string?)mergeResult.MergedFields["date"]),
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
                        EntityId      = s.Id,
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
                    EntityId      = s.Id,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            // Falls back to natural-key: a not-yet-migrated row (or a converter-driven file with no
            // sources section at all) — Title/Type correction isn't available on it yet (see #162's
            // scope boundary); nothing to stage here either way, since a quote referencing this same
            // title/type will find it via ResolveSourceAsync's own natural-key lookup as today.
            var matchesByKey = await connection.ExecuteScalarAsync<Guid?>(
                Sql.Sources.SelectIdByTitleAndType, new { title = s.Title, type = typeStr }, transaction);
            if (matchesByKey is not null) continue;

            // Indexed so a same-batch quote referencing this exact title/type resolves to this same
            // new row, instead of ResolveSourceAsync independently deriving its own EntityIdentity
            // stable id (which would differ from this file-declared id).
            sourceIndex[$"{s.Title}|{typeStr}"] = s.Id;

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Source,
                EntityId      = s.Id,
                IncomingValue = JsonSerializer.Serialize(new SourceActionPayload(s.Title, typeStr, s.Date)),
                Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                DetectedAt    = now,
            });
        }
    }

    /// <summary>Same key names as <see cref="Quotinator.Engine.Services.SqliteImportActionService"/>'s own private overload — must stay in sync (#173).</summary>
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
            var existing = await connection.QuerySingleOrDefaultAsync<(string Name, string? DateOfBirth, string? DateOfDeath, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.People.SelectExistingById, new { id = p.Id }, transaction);

            if (existing is { } row)
            {
                var existingPayload = new PersonActionPayload(row.Name, row.DateOfBirth, row.DateOfDeath);
                var incomingPayload = new PersonActionPayload(p.Name, p.DateOfBirth, p.DateOfDeath);
                var existingFields  = ToFieldMap(existingPayload);
                var incomingFields  = ToFieldMap(incomingPayload);

                personIndex[p.Name] = p.Id;

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
                        EntityId      = p.Id,
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
                    EntityId      = p.Id,
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
                personIndex[p.Name] = matchesByKey.Value.ToString("D").ToUpperInvariant();
                continue;
            }

            personIndex[p.Name] = p.Id;

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Person,
                EntityId      = p.Id,
                IncomingValue = JsonSerializer.Serialize(new PersonActionPayload(p.Name, p.DateOfBirth, p.DateOfDeath)),
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
            var existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.StageDirections.SelectExistingById, new { id = sd.Id }, transaction);

            if (existing is { } row)
            {
                var emptyTranslations = new Dictionary<string, SourceStageDirectionTranslation>();
                var existingPayload = new StageDirectionActionPayload(row.Text, row.ImageUrl, emptyTranslations);
                var incomingPayload = new StageDirectionActionPayload(sd.Text, sd.ImageUrl, emptyTranslations);
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
                        EntityId      = sd.Id,
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
                    EntityId      = sd.Id,
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
                EntityId      = sd.Id,
                IncomingValue = JsonSerializer.Serialize(new StageDirectionActionPayload(sd.Text, sd.ImageUrl, sd.Translations)),
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
            var existing = await connection.QuerySingleOrDefaultAsync<(string Text, string? SoundFileUrl, string? ImageUrl, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.SoundCues.SelectExistingById, new { id = sc.Id }, transaction);

            if (existing is { } row)
            {
                var emptyTranslations = new Dictionary<string, SourceSoundCueTranslation>();
                var existingPayload = new SoundCueActionPayload(row.Text, row.SoundFileUrl, row.ImageUrl, emptyTranslations);
                var incomingPayload = new SoundCueActionPayload(sc.Text, sc.SoundFileUrl, sc.ImageUrl, emptyTranslations);
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
                        EntityId      = sc.Id,
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
                    EntityId      = sc.Id,
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
                EntityId      = sc.Id,
                IncomingValue = JsonSerializer.Serialize(new SoundCueActionPayload(sc.Text, sc.SoundFileUrl, sc.ImageUrl, sc.Translations)),
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
            var existing = await connection.QuerySingleOrDefaultAsync<(string? Description, SafeValue<CompletenessStatus?> CompletenessStatus)?>(
                Sql.Conversations.SelectExistingById, new { id = c.Id }, transaction);

            if (existing is { } row)
            {
                var existingPayload = new ConversationActionPayload(row.Description, []);
                var incomingPayload = new ConversationActionPayload(c.Description, []);
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
                        EntityId      = c.Id,
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
                    EntityId      = c.Id,
                    ExistingValue = JsonSerializer.Serialize(existingPayload),
                    IncomingValue = JsonSerializer.Serialize(incomingPayload),
                    MergedFields  = isPending ? null : JsonSerializer.Serialize(resolved),
                    AppliedPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
                    Status        = new SafeValue<ImportActionStatus?>(status.ToString(), status),
                    DetectedAt    = now,
                });
                continue;
            }

            var lines = c.Lines
                .Select(l => new ConversationLinePayload(l.Order, l.Type, l.QuoteId, l.StageDirectionId, l.SoundCueId))
                .ToList();

            actions.Add(new SystemImportAction
            {
                BatchId       = batchId,
                ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
                EntityType    = ImportActionEntityTypes.Conversation,
                EntityId      = c.Id,
                IncomingValue = JsonSerializer.Serialize(new ConversationActionPayload(c.Description, lines)),
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

/// <summary>Staged payload for a Source Add/Modify <see cref="SystemImportAction"/> (#162 adds <see cref="Date"/>).</summary>
internal sealed record SourceActionPayload(string Title, string Type, string? Date = null);

/// <summary>
/// Staged payload for a Character Add <see cref="SystemImportAction"/>. Carries the owning Source's
/// own title/type (denormalized, not just its id) so the applier can defensively ensure the Source
/// row exists before inserting the Character — <c>System_ImportActions</c> rows apply in whatever
/// order the coordinator returns them (no cross-entity-type ordering guarantee), and
/// <c>Characters.SourceId</c> is a real foreign key.
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
