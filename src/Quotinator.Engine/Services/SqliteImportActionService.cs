using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Helpers;
using Quotinator.Engine.Models;
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Services;

/// <inheritdoc/>
public sealed class SqliteImportActionService : IImportActionService
{
    private readonly ISystemImportActionReader _actionReader;
    private readonly IImportActionCoordinator _coordinator;
    private readonly ISystemChangeLogWriter _changeLogWriter;
    private readonly IRestorableRepository<QuoteEntity> _quoteRepository;
    private readonly IRestorableRepository<Source> _sourceRepository;
    private readonly IRestorableRepository<Character> _characterRepository;
    private readonly IRestorableRepository<Person> _personRepository;
    private readonly IImportBatchRepository _importBatchRepository;
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the service with the generic Data-layer pieces it wraps.</summary>
    public SqliteImportActionService(
        ISystemImportActionReader actionReader,
        IImportActionCoordinator coordinator,
        ISystemChangeLogWriter changeLogWriter,
        IRestorableRepository<QuoteEntity> quoteRepository,
        IRestorableRepository<Source> sourceRepository,
        IRestorableRepository<Character> characterRepository,
        IRestorableRepository<Person> personRepository,
        IImportBatchRepository importBatchRepository,
        IDbConnectionFactory factory)
    {
        _actionReader          = actionReader;
        _coordinator           = coordinator;
        _changeLogWriter       = changeLogWriter;
        _quoteRepository       = quoteRepository;
        _sourceRepository      = sourceRepository;
        _characterRepository   = characterRepository;
        _personRepository      = personRepository;
        _importBatchRepository = importBatchRepository;
        _factory               = factory;
    }

    /// <inheritdoc/>
    public async Task<ImportActionPageResponse> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var result     = await _actionReader.GetPagedAsync(batchId, status, entityType, page, pageSize);
        var batchCache = new Dictionary<string, IReadOnlyList<SystemImportAction>>();

        var items = new List<ImportActionSummaryResponse>(result.Items.Count);
        foreach (var action in result.Items)
            items.Add(await ToSummaryAsync(action, batchCache));

        return new ImportActionPageResponse
        {
            TotalMatching = result.TotalCount,
            TotalPages    = result.TotalPages,
            Page          = result.Page,
            PageSize      = result.PageSize,
            Items         = items,
        };
    }

    /// <inheritdoc/>
    public async Task DecideAsync(Guid actionId, ConflictDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var action = await _actionReader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);
        if (action.EntityType != ImportActionEntityTypes.Quote)
            throw new ImportActionNotDecidableException(actionId, action.EntityType);

        var existingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.ExistingValue!)!;
        var incomingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.IncomingValue!)!;

        var existing  = QuoteFieldMerge.ToFieldMap(existingPayload.Fields);
        var incoming  = QuoteFieldMerge.ToFieldMap(incomingPayload.Fields);
        var decisions = ToDecisionMap(request);

        // Validate immediately — an ambiguous field with no decision must fail here, not silently
        // defer the problem to apply time.
        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        // Store the fully resolved payload (not the raw decision request) — apply never needs to
        // re-run FieldMergeResolver or know about policies/decisions at all.
        var resolvedPayload = new QuoteActionPayload
        {
            Fields      = QuoteFieldMerge.ToDto(result.MergedFields),
            SourceId    = incomingPayload.SourceId,
            CharacterId = incomingPayload.CharacterId,
            PersonId    = incomingPayload.PersonId,
        };

        await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedPayload));
    }

    /// <inheritdoc/>
    public async Task UndoDecisionAsync(Guid actionId, CancellationToken cancellationToken = default)
        => await _coordinator.UndoDecisionAsync(actionId);

    /// <inheritdoc/>
    public async Task<ImportActionBatchStatusResponse?> ApplyBatchAsync(string batchId, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default)
    {
        await ClearStaleAddTargetsAsync(batchId);

        var pending = await _coordinator.TryApplyBatchAsync(
            batchId, (action, conn, tx) => ApplyResolvedActionAsync(action, conn, tx, initiatedByType), cancellationToken);

        return pending is null
            ? null
            : new ImportActionBatchStatusResponse { BatchId = batchId, PendingActionIds = pending };
    }

    /// <summary>
    /// #59: a soft-deleted row must never block a fresh insert at the same id — every existence
    /// check the planner relies on for duplicate detection filters <c>IsDeleted = 0</c>, so
    /// re-importing previously-undone content stages a fresh Add against an id that is still
    /// physically occupied by a soft-deleted row, and <c>INSERT OR IGNORE</c> would otherwise
    /// silently no-op against it. Hard-deletes every Add action's target before the batch applies.
    /// <para/>
    /// Must run in dependency order — Quote first, then Character, then Source/Person — not the
    /// apply-time insert order (Source/Person, then Character, then Quote): a stale Source or
    /// Character can still be physically referenced by a stale Quote/Character row (SQLite enforces
    /// foreign keys against the physical row, not the logical <c>IsDeleted</c> flag), so the
    /// referencing row must be cleared first regardless of which order the batch's own actions
    /// happen to apply in later. This runs once per batch, before <see cref="IImportActionCoordinator.TryApplyBatchAsync"/>
    /// opens its own transaction — not inside it, since hard-deleting an already soft-deleted row is
    /// idempotent and safe to repeat if a retry re-runs this pass.
    /// </summary>
    private async Task ClearStaleAddTargetsAsync(string batchId)
    {
        var actions = await _actionReader.GetAllForBatchAsync(batchId);
        var adds    = actions.Where(a => a.ActionType.Parsed == ImportActionKind.Add).ToList();

        // Quote ids are NOT necessarily uppercase — an explicit "id" in a source file can be any
        // case, and QuoteIdentity.StableId (the hash-derived fallback, deliberately frozen) returns
        // Guid.ToString()'s default lowercase format. IRestorableRepository<T>.HardDeleteAsync takes
        // a Guid, which the registered GuidHandler always uppercases before comparing — silently
        // matching zero rows against a lowercase-stored Quote.Id (SQLite's default TEXT comparison
        // is case-sensitive). Sql.Quotes.Insert/SelectRawById/UpdateOnNewestWins all compare Id as a
        // plain string with no case normalization, so raw SQL matching that same convention is used
        // here instead — found via a genuinely red test, not assumed correct.
        using var quoteConn = _factory.CreateConnection();
        quoteConn.Open();
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Quote))
        {
            // QuoteGenres/QuoteTranslations both carry a hard FK to Quotes(Id) — a stale Quote's
            // genre rows (written by every Add, per QuoteSeedWriter.InsertGenresAsync) still
            // physically exist even though only the Quote row itself was soft-deleted on reversal,
            // and block the hard-delete below with the same FK violation this whole method exists to
            // avoid. Found live (T2), not by the unit suite — the test fixture used had no genres.
            await quoteConn.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(Sql.QuoteTranslations.DeleteForQuote, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Quotes"), new { id = action.EntityId });
        }

        // Character/Source/Person Add ids are always freshly computed via EntityIdentity (never a
        // natural-key lookup result — a natural-key match means "already exists", which is a Modify,
        // never an Add), and EntityIdentity.StableId always uppercases — safe to use the repository's
        // Guid-typed API here.
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Character))
            await _characterRepository.HardDeleteAsync(Guid.Parse(action.EntityId));

        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Source))
            await _sourceRepository.HardDeleteAsync(Guid.Parse(action.EntityId));

        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Person))
            await _personRepository.HardDeleteAsync(Guid.Parse(action.EntityId));
    }

    /// <inheritdoc/>
    public async Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default)
        => await _coordinator.DiscardBatchAsync(batchId, cancellationToken);

    /// <inheritdoc/>
    public async Task ReverseBatchAsync(string batchId, bool preview = false, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(batchId, out var batchGuid))
            throw new ImportBatchNotFoundException(Guid.Empty);

        var batch = await _importBatchRepository.GetByIdAsync(batchGuid) ?? throw new ImportBatchNotFoundException(batchGuid);
        if (batch.IsDeleted)
            throw new ImportBatchNotFoundException(batchGuid); // Already reversed — treated as absent, matching every other soft-deleted row in this codebase.

        if (batch.Status.Parsed != ImportBatchStatus.Applied)
            throw new ImportBatchStateException(batchId, $"is not currently applied (status: {batch.Status.Raw}) and cannot be reversed.");

        // #59 Scope changes decision 7: strict global LIFO stack, not a per-entity overlap check —
        // GetAllAsync is already newest-first, IsDeleted = 0 filtered (Sql.ImportBatches.SelectAll).
        // The first Applied entry in that list is, by definition, the only batch currently reversible.
        var liveBatches = await _importBatchRepository.GetAllAsync();
        var topOfStack  = liveBatches.FirstOrDefault(b => b.Status.Parsed == ImportBatchStatus.Applied);
        if (topOfStack is null || topOfStack.Id != batchGuid)
        {
            var blocker = topOfStack is null ? "no batch" : $"'{topOfStack.Name}' ({topOfStack.Id})";
            throw new ImportBatchStateException(batchId, $"is not the most recently applied batch — {blocker} must be reversed first.");
        }

        var actions = await _actionReader.GetAllForBatchAsync(batchId);
        if (actions.Count == 0)
            throw new ImportBatchStateException(batchId, "has no actions and cannot be reversed.");

        // Preview stops here — every blocking condition above has already been checked, so a caller
        // knows whether the real call would succeed, without anything being written.
        if (preview)
            return;

        var stillApplied = await _coordinator.TryReverseBatchAsync(
            batchId, (actions, conn, tx) => ReverseAppliedActionsAsync(actions, conn, tx, initiatedByType), cancellationToken);

        // Defensive only — batch.Status == Applied already guarantees every one of its actions is
        // Applied too (they transition together in TryApplyBatchAsync), so this should be unreachable.
        if (stillApplied is not null)
            throw new ImportBatchStateException(batchId, "has actions that are not Applied and cannot be reversed.");
    }

    /// <summary>
    /// The domain-specific whole-batch reversal callback passed to <see cref="IImportActionCoordinator.TryReverseBatchAsync"/>.
    /// Sorts Quote → Character → Source/Person (spec item 4's bottom-up ordering — a Source/Character
    /// still referenced by one of this batch's own about-to-be-removed quotes must not be kept just
    /// because it was checked before those quotes were cleared). As the last step, soft-deletes the
    /// batch's own <c>ImportBatch</c> row — see Scope changes decision 6.
    /// </summary>
    private async Task ReverseAppliedActionsAsync(IReadOnlyList<SystemImportAction> actions, IDbConnection connection, IDbTransaction transaction, InitiatorType initiatedByType)
    {
        var sqliteConnection  = (SqliteConnection)connection;
        var sqliteTransaction = (SqliteTransaction)transaction;
        var uow       = new SqliteUnitOfWork(connection, transaction);
        var now       = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var batchGuid = Guid.Parse(actions[0].BatchId);
        var changeLog = new QuoteSeedWriter.ChangeLogContext(_changeLogWriter, initiatedByType, actions[0].BatchId);

        var order = new Dictionary<string, int>
        {
            [ImportActionEntityTypes.Quote]     = 0,
            [ImportActionEntityTypes.Character] = 1,
            [ImportActionEntityTypes.Source]    = 2,
            [ImportActionEntityTypes.Person]    = 2,
        };
        foreach (var action in actions.OrderBy(a => order.GetValueOrDefault(a.EntityType, 3)))
        {
            switch (action.EntityType)
            {
                case ImportActionEntityTypes.Quote:
                    await ReverseQuoteActionAsync(action, sqliteConnection, sqliteTransaction, uow, now, changeLog);
                    break;
                case ImportActionEntityTypes.Character:
                    var charRefs = await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.Characters.CountActiveReferences, action.EntityId);
                    if (charRefs)
                        break;
                    await _characterRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "character", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Source:
                    var sourceRefs = await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.Sources.CountActiveReferences, action.EntityId);
                    if (sourceRefs)
                        break;
                    await _sourceRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "source", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Person:
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.People.CountActiveReferences, action.EntityId))
                        break;
                    await _personRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "person", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                default:
                    throw new InvalidOperationException($"Action '{action.Id}' has an unrecognised EntityType '{action.EntityType}'.");
            }
        }

        await _importBatchRepository.SoftDeleteAsync(batchGuid, uow);
    }

    /// <summary>Reverses one Quote action: soft-delete for an Add, field-restore for a genuine Modify, no-op write for a Skip-policy Modify.</summary>
    private async Task ReverseQuoteActionAsync(SystemImportAction action, SqliteConnection connection, SqliteTransaction transaction, IUnitOfWork uow, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        var isAdd = action.ActionType.Parsed == ImportActionKind.Add;

        if (isAdd)
        {
            // Raw SQL, not _quoteRepository.SoftDeleteAsync — see ClearStaleAddTargetsAsync's remarks
            // on why a Quote's own Id can't safely go through the repository's Guid-typed (forced
            // uppercase) comparison.
            await connection.ExecuteAsync(RepositorySql.SoftDelete("Quotes"),
                new { now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat), id = action.EntityId }, transaction);
            await QuoteSeedWriter.LogChangeAsync(changeLog, "quote", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, connection, transaction);
            return;
        }

        if (action.AppliedPolicy.Parsed == DuplicateResolutionPolicy.Skip)
            return; // Nothing was ever written for a Skip-policy Modify — its reversal is a no-op write.

        var existingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.ExistingValue!)!;
        var existingFields  = QuoteFieldMerge.ToFieldMap(existingPayload.Fields);
        // Only .Id and .Translations are actually taken from this template (see ApplyMergedFields'
        // own remarks) — QuoteText/Source are required properties but immediately overwritten below.
        var resolved = QuoteFieldMerge.ApplyMergedFields(existingFields, new SourceQuote { Id = action.EntityId, QuoteText = string.Empty, Source = string.Empty });

        // #59 Risk 1: existingPayload.SourceId/CharacterId/PersonId are the *incoming* quote's
        // resolved ids at staging time (see ImportActionPlanner.PlanAsync), not the existing row's
        // actual linkage — invisible when source/character/author text didn't change, wrong the
        // moment it did. Re-resolve from the restored text via the same natural-key lookups the
        // planner itself uses; never trust the stored ids directly.
        var sourceId = await connection.ExecuteScalarAsync<Guid?>(Sql.Sources.SelectIdByTitleAndType,
            new { title = resolved.Source, type = resolved.Type.ToString() }, transaction);
        if (sourceId is null)
            throw new ImportBatchStateException(action.BatchId, $"cannot be reversed — action '{action.Id}''s original Source '{resolved.Source}' ({resolved.Type}) no longer exists.");

        Guid? characterId = null;
        if (!string.IsNullOrWhiteSpace(resolved.Character))
        {
            characterId = await connection.ExecuteScalarAsync<Guid?>(Sql.Characters.SelectIdBySourceAndName,
                new { sourceId = sourceId.Value.ToString("D").ToUpperInvariant(), name = resolved.Character }, transaction);
            if (characterId is null)
                throw new ImportBatchStateException(action.BatchId, $"cannot be reversed — action '{action.Id}''s original Character '{resolved.Character}' no longer exists.");
        }

        Guid? personId = null;
        if (!string.IsNullOrWhiteSpace(resolved.Author))
        {
            personId = await connection.ExecuteScalarAsync<Guid?>(Sql.People.SelectIdByName, new { name = resolved.Author }, transaction);
            if (personId is null)
                throw new ImportBatchStateException(action.BatchId, $"cannot be reversed — action '{action.Id}''s original Person '{resolved.Author}' no longer exists.");
        }

        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote, new { id = resolved.Id }, transaction);

        // ExistingBatchId — not action.BatchId (the reversing batch) — restores provenance to the
        // batch that actually owns the content being brought back (#59 spec item 2 / Risk 2). Null
        // is itself a legitimate, meaningful value here (QuoteEntity.ImportBatchId's own remark:
        // "Null for records predating provenance tracking") — restoring it must preserve that, not
        // crash trying to parse a batch id that never existed.
        await connection.ExecuteAsync(Sql.Quotes.UpdateOnNewestWins, new
        {
            text    = resolved.QuoteText,
            lang    = resolved.OriginalLanguage,
            sid     = sourceId.Value,
            cid     = characterId,
            pid     = personId,
            batchId = action.ExistingBatchId is null ? (Guid?)null : Guid.Parse(action.ExistingBatchId),
            mod     = now,
            id      = resolved.Id,
        }, transaction);

        await QuoteSeedWriter.InsertGenresAsync(connection, resolved, Guid.Parse(resolved.Id), now, transaction);
        await QuoteSeedWriter.LogChangeAsync(changeLog, "quote", resolved.Id, ChangeAction.Modified,
            oldValue: action.MergedFields ?? action.IncomingValue, newValue: existingPayload, connection, transaction);
    }

    /// <summary>Spec item 3: a Source/Character/Person Add is reversible only if no active row still references it.</summary>
    private static async Task<bool> HasActiveReferencesAsync(SqliteConnection connection, SqliteTransaction transaction, string countSql, string id)
        => await connection.ExecuteScalarAsync<int>(countSql, new { id }, transaction) > 0;

    // ── The domain-specific apply dispatch ──────────────────────────────────

    /// <summary>
    /// Given a <c>Decided</c> action, writes it to the entity's own table. Dispatches on
    /// <see cref="SystemImportAction.EntityType"/> — Source/Character/Person are idempotent
    /// insert-if-not-exists using the precomputed stable id (safe even if a concurrently-applied
    /// batch already created the same row); Quote is a uniform, policy-agnostic write, since the
    /// planner/<see cref="DecideAsync"/> already computed the final resolved field values.
    /// </summary>
    private async Task ApplyResolvedActionAsync(SystemImportAction action, IDbConnection connection, IDbTransaction transaction, InitiatorType initiatedByType)
    {
        var sqliteConnection  = (SqliteConnection)connection;
        var sqliteTransaction = (SqliteTransaction)transaction;
        var now       = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var batchId   = Guid.Parse(action.BatchId);
        var changeLog = new QuoteSeedWriter.ChangeLogContext(_changeLogWriter, initiatedByType, action.BatchId);

        switch (action.EntityType)
        {
            case ImportActionEntityTypes.Source:
            {
                var payload = JsonSerializer.Deserialize<SourceActionPayload>(action.IncomingValue!)!;
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Title, payload.Type, batchId, now, changeLog);
                break;
            }
            case ImportActionEntityTypes.Character:
            {
                var payload = JsonSerializer.Deserialize<CharacterActionPayload>(action.IncomingValue!)!;
                // Defensive: Characters.SourceId is a real FK, but System_ImportActions rows apply in
                // whatever order the coordinator returns them — this action's own Source may not have
                // applied yet. Idempotent, so re-running it here is safe either way.
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, payload.SourceId, payload.SourceTitle, payload.SourceType, batchId, now, changeLog);
                await EnsureCharacterExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.SourceId, payload.Name, batchId, now, changeLog);
                break;
            }
            case ImportActionEntityTypes.Person:
            {
                var payload = JsonSerializer.Deserialize<PersonActionPayload>(action.IncomingValue!)!;
                await EnsurePersonExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Name, batchId, now, changeLog);
                break;
            }
            case ImportActionEntityTypes.Quote:
            {
                var isAdd = action.ActionType.Parsed == ImportActionKind.Add;

                // Skip means "existing row wins, untouched" — no write, no changelog, matching #64's
                // policy exactly (the only difference from #149's model: the action row itself still
                // exists for audit visibility via GET /import/actions, even though nothing was written).
                // Only applies to a genuine duplicate Modify — a brand-new Add always writes, even
                // when the file's effective policy happens to be Skip (AppliedPolicy is stamped onto
                // every action, Add included, but Skip has no meaning for a row with nothing to
                // conflict against).
                if (!isAdd && action.AppliedPolicy.Parsed == DuplicateResolutionPolicy.Skip)
                    break;

                var json  = isAdd ? action.IncomingValue : action.MergedFields;
                var payload = JsonSerializer.Deserialize<QuoteActionPayload>(json!)
                              ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload.");
                var resolved = new SourceQuote
                {
                    Id               = action.EntityId,
                    QuoteText        = payload.Fields.QuoteText!,
                    OriginalLanguage = payload.Fields.OriginalLanguage!,
                    Source           = payload.Fields.Source!,
                    Date             = payload.Fields.Date,
                    Character        = payload.Fields.Character,
                    Author           = payload.Fields.Author,
                    Type             = payload.Fields.Type ?? QuoteType.Unknown,
                    Genres           = payload.Fields.Genres,
                };

                // Defensive: same ordering caveat as Character above — this Quote's Source/Character/
                // Person actions may not have applied yet.
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, payload.SourceId, resolved.Source, resolved.Type.ToString(), batchId, now, changeLog);
                if (payload.CharacterId is not null)
                    await EnsureCharacterExistsAsync(sqliteConnection, sqliteTransaction, payload.CharacterId, payload.SourceId, resolved.Character!, batchId, now, changeLog);
                if (payload.PersonId is not null)
                    await EnsurePersonExistsAsync(sqliteConnection, sqliteTransaction, payload.PersonId, resolved.Author!, batchId, now, changeLog);

                if (isAdd)
                {
                    // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync,
                    // before this batch's apply transaction opened — see that method's remarks for
                    // why it can't be done per-action, in insert order, here.
                    await sqliteConnection.ExecuteAsync(Sql.Quotes.Insert, new
                    {
                        Id               = resolved.Id,
                        QuoteText        = resolved.QuoteText,
                        OriginalLanguage = resolved.OriginalLanguage,
                        SourceId         = payload.SourceId,
                        CharacterId      = payload.CharacterId,
                        PersonId         = payload.PersonId,
                        ImportBatchId    = batchId,
                        DateCreated      = now
                    }, sqliteTransaction);

                    await QuoteSeedWriter.LogChangeAsync(changeLog, "quote", resolved.Id, ChangeAction.Created,
                        oldValue: null, newValue: QuoteFieldMerge.ToFieldMap(resolved), sqliteConnection, sqliteTransaction);
                }
                else
                {
                    await sqliteConnection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote, new { id = resolved.Id }, sqliteTransaction);

                    await sqliteConnection.ExecuteAsync(Sql.Quotes.UpdateOnNewestWins, new
                    {
                        text    = resolved.QuoteText,
                        lang    = resolved.OriginalLanguage,
                        sid     = payload.SourceId,
                        cid     = payload.CharacterId,
                        pid     = payload.PersonId,
                        batchId,
                        mod     = now,
                        id      = resolved.Id,
                    }, sqliteTransaction);

                    await QuoteSeedWriter.LogChangeAsync(changeLog, "quote", resolved.Id, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: payload, sqliteConnection, sqliteTransaction);
                }

                await QuoteSeedWriter.InsertGenresAsync(sqliteConnection, resolved, Guid.Parse(resolved.Id), now, sqliteTransaction);
                break;
            }
            default:
                throw new InvalidOperationException($"Action '{action.Id}' has an unrecognised EntityType '{action.EntityType}'.");
        }
    }

    // ── Idempotent ensure-exists helpers ────────────────────────────────────
    // Each is INSERT OR IGNORE keyed by a precomputed stable/real id, so calling any of these more
    // than once (from a dependent entity's own defensive check, or a concurrently-applied batch
    // that staged an Add for the same not-yet-existing row) is always safe.

    private async Task EnsureSourceExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string title, string type,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.Sources.InsertIfNotExists,
            new { Id = id, Title = title, Type = type, Date = (string?)null, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "source", id, ChangeAction.Created,
                oldValue: null, newValue: new { title, type }, connection, transaction);
    }

    private async Task EnsureCharacterExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string sourceId, string name,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.Characters.InsertIfNotExists,
            new { Id = id, SourceId = sourceId, Name = name, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "character", id, ChangeAction.Created,
                oldValue: null, newValue: new { name }, connection, transaction);
    }

    private async Task EnsurePersonExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string name,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.People.InsertIfNotExists,
            new { Id = id, Name = name, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "person", id, ChangeAction.Created,
                oldValue: null, newValue: new { name }, connection, transaction);
    }

    // ── GetPagedAsync helpers ────────────────────────────────────────────────

    private async Task<ImportActionSummaryResponse> ToSummaryAsync(SystemImportAction action, Dictionary<string, IReadOnlyList<SystemImportAction>> batchCache)
        => new()
        {
            Id               = action.Id,
            BatchId          = action.BatchId,
            ActionType       = action.ActionType.Raw,
            EntityType       = action.EntityType,
            EntityId         = action.EntityId,
            ExistingBatchId  = action.ExistingBatchId,
            Status           = action.Status.Raw,
            AppliedPolicy    = action.AppliedPolicy.Raw,
            DetectedAt       = action.DetectedAt,
            AppliedAt        = action.AppliedAt,
            DiscardedAt      = action.DiscardedAt,
            ExistingFields   = BuildFields(action.EntityType, action.ExistingValue),
            IncomingFields   = BuildFields(action.EntityType, action.IncomingValue) ?? new Dictionary<string, object?>(),
            MergedFields     = BuildFields(action.EntityType, action.MergedFields),
            RelatedActionIds = await ComputeRelatedActionIdsAsync(action, batchCache),
            AmbiguousFields  = ComputeAmbiguousFields(action),
        };

    private static IReadOnlyDictionary<string, object?>? BuildFields(string entityType, string? json)
    {
        if (json is null) return null;
        return entityType switch
        {
            "Quote"     => QuoteFieldMerge.ToFieldMap(JsonSerializer.Deserialize<QuoteActionPayload>(json)!.Fields),
            "Source"    => ToFieldMap(JsonSerializer.Deserialize<SourceActionPayload>(json)!),
            "Character" => ToFieldMap(JsonSerializer.Deserialize<CharacterActionPayload>(json)!),
            "Person"    => ToFieldMap(JsonSerializer.Deserialize<PersonActionPayload>(json)!),
            _           => null,
        };
    }

    private static IReadOnlyDictionary<string, object?> ToFieldMap(SourceActionPayload payload) =>
        new Dictionary<string, object?> { ["title"] = payload.Title, ["type"] = payload.Type };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(CharacterActionPayload payload) =>
        new Dictionary<string, object?> { ["name"] = payload.Name, ["sourceId"] = payload.SourceId };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(PersonActionPayload payload) =>
        new Dictionary<string, object?> { ["name"] = payload.Name };

    private static IReadOnlyList<string> ComputeAmbiguousFields(SystemImportAction action)
    {
        if (action.EntityType != ImportActionEntityTypes.Quote || action.Status.Parsed != ImportActionStatus.Pending)
            return [];

        var existingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.ExistingValue!)!;
        var incomingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.IncomingValue!)!;
        var existing = QuoteFieldMerge.ToFieldMap(existingPayload.Fields);
        var incoming = QuoteFieldMerge.ToFieldMap(incomingPayload.Fields);

        try
        {
            FieldMergeResolver.ResolveWithDecisions(existing, incoming, new Dictionary<string, FieldMergeDecision>());
            return [];
        }
        catch (UnresolvedFieldConflictException ex)
        {
            return ex.FieldNames;
        }
    }

    private async Task<IReadOnlyList<Guid>> ComputeRelatedActionIdsAsync(SystemImportAction action, Dictionary<string, IReadOnlyList<SystemImportAction>> batchCache)
    {
        if (action.EntityType != ImportActionEntityTypes.Quote) return [];

        var json    = action.ActionType.Parsed == ImportActionKind.Add ? action.IncomingValue : (action.MergedFields ?? action.IncomingValue);
        var payload = JsonSerializer.Deserialize<QuoteActionPayload>(json!)!;

        if (!batchCache.TryGetValue(action.BatchId, out var batchActions))
        {
            batchActions = await _actionReader.GetAllForBatchAsync(action.BatchId);
            batchCache[action.BatchId] = batchActions;
        }

        var related = new List<Guid>();
        foreach (var candidate in batchActions)
        {
            if (candidate.Id == action.Id) continue;
            if (candidate.EntityType == ImportActionEntityTypes.Source    && candidate.EntityId == payload.SourceId) related.Add(candidate.Id);
            if (candidate.EntityType == ImportActionEntityTypes.Character && payload.CharacterId is not null && candidate.EntityId == payload.CharacterId) related.Add(candidate.Id);
            if (candidate.EntityType == ImportActionEntityTypes.Person    && payload.PersonId is not null && candidate.EntityId == payload.PersonId) related.Add(candidate.Id);
        }
        return related;
    }

    private static Dictionary<string, FieldMergeDecision> ToDecisionMap(ConflictDecisionRequest request)
    {
        var map = new Dictionary<string, FieldMergeDecision>();

        void Add(string field, FieldDecision? decision)
        {
            if (decision is null) return;
            map[field] = new FieldMergeDecision(decision.Choice, decision.Value);
        }

        Add("quoteText", request.QuoteText);
        Add("originalLanguage", request.OriginalLanguage);
        Add("source", request.Source);
        Add("date", request.Date);
        Add("character", request.Character);
        Add("author", request.Author);
        Add("type", request.Type);

        if (request.Genres is not null)
            map["genres"] = new FieldMergeDecision(request.Genres.Choice, request.Genres.Value);

        return map;
    }
}
