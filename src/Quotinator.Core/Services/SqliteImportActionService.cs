using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Services;

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
    private readonly IRestorableRepository<ConversationEntity> _conversationRepository;
    private readonly IRestorableRepository<StageDirectionEntity> _stageDirectionRepository;
    private readonly IRestorableRepository<SoundCueEntity> _soundCueRepository;
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
        IRestorableRepository<ConversationEntity> conversationRepository,
        IRestorableRepository<StageDirectionEntity> stageDirectionRepository,
        IRestorableRepository<SoundCueEntity> soundCueRepository,
        IImportBatchRepository importBatchRepository,
        IDbConnectionFactory factory)
    {
        _actionReader             = actionReader;
        _coordinator              = coordinator;
        _changeLogWriter          = changeLogWriter;
        _quoteRepository          = quoteRepository;
        _sourceRepository         = sourceRepository;
        _characterRepository      = characterRepository;
        _personRepository         = personRepository;
        _conversationRepository   = conversationRepository;
        _stageDirectionRepository = stageDirectionRepository;
        _soundCueRepository       = soundCueRepository;
        _importBatchRepository    = importBatchRepository;
        _factory                  = factory;
    }

    /// <inheritdoc/>
    public async Task<PagedItems<ImportActionSummaryResponse>> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var result     = await _actionReader.GetPagedAsync(batchId, status, entityType, page, pageSize);
        var batchCache = new Dictionary<string, IReadOnlyList<SystemImportAction>>();

        var items = new List<ImportActionSummaryResponse>(result.Items.Count);
        foreach (var action in result.Items)
            items.Add(await ToSummaryAsync(action, batchCache));

        return new PagedItems<ImportActionSummaryResponse>(items, result.Page, result.PageSize, result.TotalCount);
    }

    /// <inheritdoc/>
    public async Task DecideAsync(Guid actionId, ConflictDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var action = await _actionReader.GetByIdAsync(actionId) ?? throw new ImportActionNotFoundException(actionId);

        if (action.EntityType == ImportActionEntityTypes.Source && action.ActionType.Parsed == ImportActionKind.Modify)
        {
            var existingSourcePayload = JsonSerializer.Deserialize<SourceActionPayload>(action.ExistingValue!)!;
            var incomingSourcePayload = JsonSerializer.Deserialize<SourceActionPayload>(action.IncomingValue!)!;

            var existingSourceFields = ToFieldMap(existingSourcePayload);
            var incomingSourceFields = ToFieldMap(incomingSourcePayload);
            var sourceDecisions      = ToSourceDecisionMap(request);

            var sourceResult = FieldMergeResolver.ResolveWithDecisions(existingSourceFields, incomingSourceFields, sourceDecisions);

            var resolvedSourcePayload = new SourceActionPayload(
                (string)sourceResult.MergedFields["title"]!,
                (string)sourceResult.MergedFields["type"]!,
                (string?)sourceResult.MergedFields["date"],
                (string?)sourceResult.MergedFields["seriesId"]);

            await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedSourcePayload), request.MarkCompletenessAs);
            return;
        }

        if (action.EntityType == ImportActionEntityTypes.StageDirection && action.ActionType.Parsed == ImportActionKind.Modify)
        {
            var existingStageDirectionPayload = JsonSerializer.Deserialize<StageDirectionActionPayload>(action.ExistingValue!)!;
            var incomingStageDirectionPayload = JsonSerializer.Deserialize<StageDirectionActionPayload>(action.IncomingValue!)!;

            var existingStageDirectionFields = ToFieldMap(existingStageDirectionPayload);
            var incomingStageDirectionFields = ToFieldMap(incomingStageDirectionPayload);
            var stageDirectionDecisions      = ToStageDirectionDecisionMap(request);

            var stageDirectionResult = FieldMergeResolver.ResolveWithDecisions(existingStageDirectionFields, incomingStageDirectionFields, stageDirectionDecisions);

            var resolvedStageDirectionPayload = new StageDirectionActionPayload(
                (string)stageDirectionResult.MergedFields["text"]!,
                (string?)stageDirectionResult.MergedFields["imageUrl"],
                existingStageDirectionPayload.Translations);

            await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedStageDirectionPayload), request.MarkCompletenessAs);
            return;
        }

        if (action.EntityType == ImportActionEntityTypes.SoundCue && action.ActionType.Parsed == ImportActionKind.Modify)
        {
            var existingSoundCuePayload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.ExistingValue!)!;
            var incomingSoundCuePayload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.IncomingValue!)!;

            var existingSoundCueFields = ToFieldMap(existingSoundCuePayload);
            var incomingSoundCueFields = ToFieldMap(incomingSoundCuePayload);
            var soundCueDecisions      = ToSoundCueDecisionMap(request);

            var soundCueResult = FieldMergeResolver.ResolveWithDecisions(existingSoundCueFields, incomingSoundCueFields, soundCueDecisions);

            var resolvedSoundCuePayload = new SoundCueActionPayload(
                (string)soundCueResult.MergedFields["text"]!,
                (string?)soundCueResult.MergedFields["soundFileUrl"],
                (string?)soundCueResult.MergedFields["imageUrl"],
                existingSoundCuePayload.Translations);

            await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedSoundCuePayload), request.MarkCompletenessAs);
            return;
        }

        if (action.EntityType == ImportActionEntityTypes.Conversation && action.ActionType.Parsed == ImportActionKind.Modify)
        {
            var existingConversationPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.ExistingValue!)!;
            var incomingConversationPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.IncomingValue!)!;

            var existingConversationFields = new Dictionary<string, object?> { ["description"] = existingConversationPayload.Description };
            var incomingConversationFields = new Dictionary<string, object?> { ["description"] = incomingConversationPayload.Description };
            var conversationDecisions      = new Dictionary<string, FieldMergeDecision>();
            if (request.ConversationDescription is { } cd)
                conversationDecisions["description"] = new FieldMergeDecision(cd.Choice, cd.Value);

            var conversationResult = FieldMergeResolver.ResolveWithDecisions(existingConversationFields, incomingConversationFields, conversationDecisions);

            var resolvedConversationPayload = new ConversationActionPayload((string?)conversationResult.MergedFields["description"], []);

            await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedConversationPayload), request.MarkCompletenessAs);
            return;
        }

        if (action.EntityType == ImportActionEntityTypes.Person && action.ActionType.Parsed == ImportActionKind.Modify)
        {
            var existingPersonPayload = JsonSerializer.Deserialize<PersonActionPayload>(action.ExistingValue!)!;
            var incomingPersonPayload = JsonSerializer.Deserialize<PersonActionPayload>(action.IncomingValue!)!;

            var existingPersonFields = ToFieldMap(existingPersonPayload);
            var incomingPersonFields = ToFieldMap(incomingPersonPayload);
            var personDecisions      = ToPersonDecisionMap(request);

            var personResult = FieldMergeResolver.ResolveWithDecisions(existingPersonFields, incomingPersonFields, personDecisions);

            var resolvedPersonPayload = new PersonActionPayload(
                (string)personResult.MergedFields["name"]!,
                (string?)personResult.MergedFields["dateOfBirth"],
                (string?)personResult.MergedFields["dateOfDeath"]);

            await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedPersonPayload), request.MarkCompletenessAs);
            return;
        }

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

        await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedPayload), request.MarkCompletenessAs);
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

        // Like Source below, a Quote Add's id can be file-authored (or QuoteIdentity.StableId-derived)
        // rather than always freshly computed the way Character/Series/Universe's always are — it is
        // canonicalized at ImportActionPlanner's capture point (ADR 012), so action.EntityId here is
        // already reliably canonical, but raw SQL (not the Guid-typed repository path) is still used
        // for consistency with Source/Person/Conversation/StageDirection/SoundCue below, all of which
        // share this same file-authored-id shape.
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

        // Character/Person Add ids are always freshly computed via EntityIdentity (never a
        // natural-key lookup result — a natural-key match means "already exists", which is a Modify,
        // never an Add), and EntityIdentity.StableId always canonicalizes — safe to use the
        // repository's Guid-typed API here.
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Character))
        {
            // #179: CharacterSources carries a real FK to Characters(Id) — its link row(s) must be
            // removed first, or the hard-delete below violates the FK (found live via this exact
            // regression: ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow /
            // ReverseBatchAsync_ThenReImport_QuoteWithGenres_ResurrectsWithoutForeignKeyViolation).
            await quoteConn.ExecuteAsync(Sql.CharacterSources.DeleteForCharacter, new { id = action.EntityId });
            await _characterRepository.HardDeleteAsync(Guid.Parse(action.EntityId));
        }

        // #162: unlike Character/Person, a Source Add's id is no longer always EntityIdentity-derived
        // — an explicit sources[] entry supplies its own file-authored id, which is not guaranteed to
        // be canonically cased the way EntityIdentity.StableId is. Raw SQL, not the Guid-typed
        // repository path, same reasoning as Conversation/StageDirection/SoundCue below.
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Source))
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Sources"), new { id = action.EntityId });

        // #173: a people[] entry supplies its own file-authored id, not guaranteed to be canonically
        // cased — raw SQL, not the Guid-typed repository path, same fix #162 made for Source above.
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Person))
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("People"), new { id = action.EntityId });

        // #180: Series/Universe entries have no explicit-id file section (matched by Name only, like
        // Character/Person implicitly), so their Add id is always EntityIdentity-derived (always
        // canonical) — the Guid-typed repository path would be safe here too, but raw SQL is used for
        // consistency with Sql.Series/Sql.Universe's own no-repository query set (#183/#187/#188 are
        // where a real IRestorableRepository<SeriesEntity>/<UniverseEntity> gets introduced).
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Series))
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Series"), new { id = action.EntityId });

        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Universe))
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Universe"), new { id = action.EntityId });

        // #68: Conversation/StageDirection/SoundCue ids are explicit-in-file, like Quote's — not
        // EntityIdentity-derived like Character/Person's — so the same raw-SQL, no-forced-canonical-
        // casing approach applies here too, not the repository's Guid-typed path. Each clears its
        // own detail rows first (ConversationLines/*Translations), same FK-blocking reason as
        // QuoteGenres/QuoteTranslations above.
        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Conversation))
        {
            await quoteConn.ExecuteAsync(Sql.ConversationLines.DeleteForConversation, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Conversations"), new { id = action.EntityId });
        }

        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.StageDirection))
        {
            await quoteConn.ExecuteAsync(Sql.ConversationLines.DeleteForStageDirection, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(Sql.StageDirectionTranslations.DeleteForStageDirection, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("StageDirections"), new { id = action.EntityId });
        }

        foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.SoundCue))
        {
            await quoteConn.ExecuteAsync(Sql.ConversationLines.DeleteForSoundCue, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(Sql.SoundCueTranslations.DeleteForSoundCue, new { id = action.EntityId });
            await quoteConn.ExecuteAsync(RepositorySql.HardDelete("SoundCues"), new { id = action.EntityId });
        }
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
            [ImportActionEntityTypes.Quote]        = 0,
            [ImportActionEntityTypes.Conversation] = 0,
            [ImportActionEntityTypes.Character]    = 1,
            [ImportActionEntityTypes.Source]       = 2,
            [ImportActionEntityTypes.Person]       = 2,
            // #180: reversed after Source (whose SeriesId may still point at it) and Universe after
            // Series (whose UniverseId may still point at it) — same active-reference-respecting
            // ordering reasoning as StageDirection/SoundCue below, one level shallower.
            [ImportActionEntityTypes.Series]         = 3,
            [ImportActionEntityTypes.Universe]       = 4,
            // #68: reversed last — StageDirection/SoundCue's active-reference check (joined through
            // Conversations, see Sql.StageDirections.CountActiveReferences' remark) needs Conversation
            // already reversed in this same pass, or it would still see the about-to-be-removed
            // Conversation's lines as live references and refuse to soft-delete a StageDirection/
            // SoundCue this batch itself introduced alongside that Conversation.
            [ImportActionEntityTypes.StageDirection] = 4,
            [ImportActionEntityTypes.SoundCue]       = 4,
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
                    if (action.ActionType.Parsed == ImportActionKind.Modify)
                    {
                        // #162: a Modify reversal restores the prior field values — it never deletes
                        // anything, so no active-reference check is needed (unlike an Add reversal).
                        var existingSourcePayload = JsonSerializer.Deserialize<SourceActionPayload>(action.ExistingValue!)!;
                        await sqliteConnection.ExecuteAsync(Sql.Sources.UpdateFieldsById, new
                        {
                            title = existingSourcePayload.Title,
                            type  = existingSourcePayload.Type,
                            date  = existingSourcePayload.Date,
                            seriesId = existingSourcePayload.SeriesId,
                            dateModified = now,
                            id    = action.EntityId,
                        }, sqliteTransaction);
                        await QuoteSeedWriter.LogChangeAsync(changeLog, "source", action.EntityId, ChangeAction.Modified, oldValue: null, newValue: existingSourcePayload, sqliteConnection, sqliteTransaction);
                        break;
                    }
                    var sourceRefs = await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.Sources.CountActiveReferences, action.EntityId);
                    if (sourceRefs)
                        break;
                    // #162: raw SQL, not the Guid-typed repository path — see ClearStaleAddTargetsAsync's
                    // remark; a Source Add's id may now be an explicit, not-necessarily-canonically-cased
                    // file-authored id, not always an EntityIdentity-derived one.
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("Sources"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "source", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Person:
                    if (action.ActionType.Parsed == ImportActionKind.Modify)
                    {
                        // #173: a Modify reversal restores the prior field values — it never deletes
                        // anything, so no active-reference check is needed (unlike an Add reversal).
                        var existingPersonPayload = JsonSerializer.Deserialize<PersonActionPayload>(action.ExistingValue!)!;
                        await sqliteConnection.ExecuteAsync(Sql.People.UpdateFieldsById, new
                        {
                            name = existingPersonPayload.Name,
                            dateOfBirth = existingPersonPayload.DateOfBirth,
                            dateOfDeath = existingPersonPayload.DateOfDeath,
                            dateModified = now,
                            id = action.EntityId,
                        }, sqliteTransaction);
                        await QuoteSeedWriter.LogChangeAsync(changeLog, "person", action.EntityId, ChangeAction.Modified, oldValue: null, newValue: existingPersonPayload, sqliteConnection, sqliteTransaction);
                        break;
                    }
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.People.CountActiveReferences, action.EntityId))
                        break;
                    // #173: raw SQL, not the Guid-typed repository path — a people[] Add's id may now
                    // be an explicit, not-necessarily-canonically-cased file-authored id, same fix #162
                    // made for Source (SqliteImportActionService.cs's Source case above).
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("People"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "person", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Series:
                    // #180: Add-only, no Modify branch — a Series is never anything but soft-deleted
                    // on reversal, guarded by the same active-reference check as Source/Person's own
                    // Add branch (a Source this same batch didn't touch may still point at it).
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.Series.CountActiveReferences, action.EntityId))
                        break;
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("Series"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "series", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Universe:
                    // #180: Add-only, no Modify branch — see Series' remark above.
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.Universe.CountActiveReferences, action.EntityId))
                        break;
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("Universe"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "universe", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.Conversation:
                    if (action.ActionType.Parsed == ImportActionKind.Modify)
                    {
                        // #176: a Modify reversal restores the prior Description only — it never
                        // touches Lines and never deletes anything, so no active-reference check is
                        // needed (unlike an Add reversal).
                        var existingConversationPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.ExistingValue!)!;
                        await sqliteConnection.ExecuteAsync(Sql.Conversations.UpdateDescriptionById, new
                        {
                            description = existingConversationPayload.Description,
                            dateModified = now,
                            id = action.EntityId,
                        }, sqliteTransaction);
                        await QuoteSeedWriter.LogChangeAsync(changeLog, "conversation", action.EntityId, ChangeAction.Modified, oldValue: null, newValue: existingConversationPayload, sqliteConnection, sqliteTransaction);
                        break;
                    }
                    // #68: id-keyed like Quote (explicit id-in-file, not EntityIdentity-derived) —
                    // raw SQL, not the Guid-typed repository path, same reasoning as
                    // ReverseQuoteActionAsync's Add branch. No active-reference check: nothing else
                    // carries an FK to a Conversation (see Sql.Conversations' own remark). Its
                    // ConversationLines are left orphaned, same precedent as QuoteGenres/
                    // QuoteTranslations on a reversed Quote Add.
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("Conversations"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "conversation", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.StageDirection:
                    if (action.ActionType.Parsed == ImportActionKind.Modify)
                    {
                        // #171: a Modify reversal restores the prior field values — it never deletes
                        // anything, so no active-reference check is needed (unlike an Add reversal).
                        var existingStageDirectionPayload = JsonSerializer.Deserialize<StageDirectionActionPayload>(action.ExistingValue!)!;
                        await sqliteConnection.ExecuteAsync(Sql.StageDirections.UpdateFieldsById, new
                        {
                            text = existingStageDirectionPayload.Text,
                            imageUrl = existingStageDirectionPayload.ImageUrl,
                            dateModified = now,
                            id = action.EntityId,
                        }, sqliteTransaction);
                        await QuoteSeedWriter.LogChangeAsync(changeLog, "stageDirection", action.EntityId, ChangeAction.Modified, oldValue: null, newValue: existingStageDirectionPayload, sqliteConnection, sqliteTransaction);
                        break;
                    }
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.StageDirections.CountActiveReferences, action.EntityId))
                        break;
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("StageDirections"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "stageDirection", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
                    break;
                case ImportActionEntityTypes.SoundCue:
                    if (action.ActionType.Parsed == ImportActionKind.Modify)
                    {
                        // #172: a Modify reversal restores the prior field values — it never deletes
                        // anything, so no active-reference check is needed (unlike an Add reversal).
                        var existingSoundCuePayload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.ExistingValue!)!;
                        await sqliteConnection.ExecuteAsync(Sql.SoundCues.UpdateFieldsById, new
                        {
                            text = existingSoundCuePayload.Text,
                            soundFileUrl = existingSoundCuePayload.SoundFileUrl,
                            imageUrl = existingSoundCuePayload.ImageUrl,
                            dateModified = now,
                            id = action.EntityId,
                        }, sqliteTransaction);
                        await QuoteSeedWriter.LogChangeAsync(changeLog, "soundCue", action.EntityId, ChangeAction.Modified, oldValue: null, newValue: existingSoundCuePayload, sqliteConnection, sqliteTransaction);
                        break;
                    }
                    if (await HasActiveReferencesAsync(sqliteConnection, sqliteTransaction, Sql.SoundCues.CountActiveReferences, action.EntityId))
                        break;
                    await sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("SoundCues"), new { now, id = action.EntityId }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "soundCue", action.EntityId, ChangeAction.SoftDelete, oldValue: null, newValue: null, sqliteConnection, sqliteTransaction);
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
            // on why Quote uses the same raw-SQL convention as Source/Person/Conversation/
            // StageDirection/SoundCue.
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
                new { sourceId = sourceId.Value.ToCanonicalId(), name = resolved.Character }, transaction);
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
                var isSourceAdd = action.ActionType.Parsed == ImportActionKind.Add;
                if (isSourceAdd)
                {
                    var payload = JsonSerializer.Deserialize<SourceActionPayload>(action.IncomingValue!)!;
                    await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Title, payload.Type, batchId, now, changeLog, payload.Date, payload.SeriesId);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<SourceActionPayload>(action.MergedFields
                        ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload."))!;
                    await sqliteConnection.ExecuteAsync(Sql.Sources.UpdateFieldsById, new
                    {
                        title = payload.Title,
                        type  = payload.Type,
                        date  = payload.Date,
                        seriesId = payload.SeriesId,
                        dateModified = now,
                        id    = action.EntityId,
                    }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "source", action.EntityId, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: payload, sqliteConnection, sqliteTransaction);
                }

                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Sources.SelectCompletenessById, Sql.Sources.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.Character:
            {
                var payload = JsonSerializer.Deserialize<CharacterActionPayload>(action.IncomingValue!)!;
                // Defensive: CharacterSources.SourceId (#179) is a real FK, but System_ImportActions
                // rows apply in whatever order the coordinator returns them — this action's own
                // Source may not have applied yet. Idempotent, so re-running it here is safe either way.
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, payload.SourceId, payload.SourceTitle, payload.SourceType, batchId, now, changeLog);
                await EnsureCharacterExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.SourceId, payload.Name, batchId, now, changeLog);
                break;
            }
            case ImportActionEntityTypes.Person:
            {
                var isPersonAdd = action.ActionType.Parsed == ImportActionKind.Add;
                if (isPersonAdd)
                {
                    var payload = JsonSerializer.Deserialize<PersonActionPayload>(action.IncomingValue!)!;
                    await EnsurePersonExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Name, batchId, now, changeLog,
                        payload.DateOfBirth, payload.DateOfDeath);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<PersonActionPayload>(action.MergedFields
                        ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload."))!;
                    await sqliteConnection.ExecuteAsync(Sql.People.UpdateFieldsById, new
                    {
                        name = payload.Name,
                        dateOfBirth = payload.DateOfBirth,
                        dateOfDeath = payload.DateOfDeath,
                        dateModified = now,
                        id = action.EntityId,
                    }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "person", action.EntityId, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: payload, sqliteConnection, sqliteTransaction);
                }

                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.People.SelectCompletenessById, Sql.People.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.Universe:
            {
                // #180: Add-only — no Modify branch exists, unlike Source/Person above.
                var payload = JsonSerializer.Deserialize<UniverseActionPayload>(action.IncomingValue!)!;
                await EnsureUniverseExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Name, batchId, now, changeLog);
                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Universe.SelectCompletenessById, Sql.Universe.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.Series:
            {
                // #180: Add-only — no Modify branch exists, unlike Source/Person above.
                var payload = JsonSerializer.Deserialize<SeriesActionPayload>(action.IncomingValue!)!;
                // Defensive: Series.UniverseId (#179) is a real FK, but System_ImportActions rows
                // apply in whatever order the coordinator returns them — this action's own Universe
                // may not have applied yet. Idempotent, so re-running it here is safe either way.
                if (payload.UniverseId is not null)
                    await EnsureUniverseExistsAsync(sqliteConnection, sqliteTransaction, payload.UniverseId, payload.Name, batchId, now, changeLog);
                await EnsureSeriesExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Name, payload.UniverseId, batchId, now, changeLog);
                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Series.SelectCompletenessById, Sql.Series.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
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
                // Person actions may not have applied yet. resolved.Date is the quote-level "date
                // associated with the source" field (SourceQuote.Date's own doc comment) — the same
                // value a Source's own explicit sources[] entry would carry, for a Source that only
                // this quote ever introduces.
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, payload.SourceId, resolved.Source, resolved.Type.ToString(), batchId, now, changeLog, resolved.Date);
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
                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Quotes.SelectCompletenessById, Sql.Quotes.UpdateCompletenessById, resolved.Id, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.StageDirection:
            {
                var isStageDirectionAdd = action.ActionType.Parsed == ImportActionKind.Add;
                if (isStageDirectionAdd)
                {
                    var payload = JsonSerializer.Deserialize<StageDirectionActionPayload>(action.IncomingValue!)!;
                    await EnsureStageDirectionExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload, batchId, now, changeLog);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<StageDirectionActionPayload>(action.MergedFields
                        ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload."))!;
                    await sqliteConnection.ExecuteAsync(Sql.StageDirections.UpdateFieldsById, new
                    {
                        text = payload.Text,
                        imageUrl = payload.ImageUrl,
                        dateModified = now,
                        id = action.EntityId,
                    }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "stageDirection", action.EntityId, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: payload, sqliteConnection, sqliteTransaction);
                }

                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.StageDirections.SelectCompletenessById, Sql.StageDirections.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.SoundCue:
            {
                var isSoundCueAdd = action.ActionType.Parsed == ImportActionKind.Add;
                if (isSoundCueAdd)
                {
                    var payload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.IncomingValue!)!;
                    await EnsureSoundCueExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload, batchId, now, changeLog);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.MergedFields
                        ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload."))!;
                    await sqliteConnection.ExecuteAsync(Sql.SoundCues.UpdateFieldsById, new
                    {
                        text = payload.Text,
                        soundFileUrl = payload.SoundFileUrl,
                        imageUrl = payload.ImageUrl,
                        dateModified = now,
                        id = action.EntityId,
                    }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "soundCue", action.EntityId, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: payload, sqliteConnection, sqliteTransaction);
                }

                await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.SoundCues.SelectCompletenessById, Sql.SoundCues.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                break;
            }
            case ImportActionEntityTypes.Conversation:
            {
                if (action.ActionType.Parsed == ImportActionKind.Modify)
                {
                    var modifyPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.MergedFields
                        ?? throw new InvalidOperationException($"Action '{action.Id}' is Decided but has no resolved payload."))!;
                    await sqliteConnection.ExecuteAsync(Sql.Conversations.UpdateDescriptionById, new
                    {
                        description = modifyPayload.Description,
                        dateModified = now,
                        id = action.EntityId,
                    }, sqliteTransaction);
                    await QuoteSeedWriter.LogChangeAsync(changeLog, "conversation", action.EntityId, ChangeAction.Modified,
                        oldValue: action.ExistingValue, newValue: modifyPayload, sqliteConnection, sqliteTransaction);
                    await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Conversations.SelectCompletenessById, Sql.Conversations.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                    break;
                }

                // Trusts its referenced Quote/StageDirection/SoundCue rows already applied — see
                // PlanAsync's remark on why that ordering is safe to rely on here, unlike
                // Character/Quote's defensive re-ensure.
                var payload = JsonSerializer.Deserialize<ConversationActionPayload>(action.IncomingValue!)!;
                var inserted = await sqliteConnection.ExecuteAsync(Sql.Conversations.InsertIfNotExists,
                    new { Id = action.EntityId, Description = payload.Description, ImportBatchId = batchId, DateCreated = now }, sqliteTransaction);

                if (inserted > 0)
                {
                    foreach (var line in payload.Lines)
                    {
                        await sqliteConnection.ExecuteAsync(Sql.ConversationLines.Insert, new
                        {
                            Id               = Guid.NewGuid().ToString(),
                            ConversationId   = action.EntityId,
                            Order            = line.Order,
                            LineType         = line.Type.ToString(),
                            QuoteId          = line.QuoteId,
                            StageDirectionId = line.StageDirectionId,
                            SoundCueId       = line.SoundCueId,
                            DateCreated      = now,
                        }, sqliteTransaction);
                    }

                    await QuoteSeedWriter.LogChangeAsync(changeLog, "conversation", action.EntityId, ChangeAction.Created,
                        oldValue: null, newValue: payload, sqliteConnection, sqliteTransaction);
                    await ApplyCompletenessAsync(sqliteConnection, sqliteTransaction, Sql.Conversations.SelectCompletenessById, Sql.Conversations.UpdateCompletenessById, action.EntityId, action.MarkCompletenessAs.Parsed, now);
                }
                break;
            }
            default:
                throw new InvalidOperationException($"Action '{action.Id}' has an unrecognised EntityType '{action.EntityType}'.");
        }
    }

    // ── Completeness (#165) ──────────────────────────────────────────────────

    /// <summary>
    /// Persists the row's <c>CompletenessStatus</c> after an apply — <paramref name="markCompletenessAs"/>
    /// (the decide-time override, if any) always wins; otherwise falls back to
    /// <see cref="CompletenessGuard.ComputeNextStatus"/> against the row's own current state. Callers
    /// pass their own <c>SelectCompletenessById</c>/<c>UpdateCompletenessById</c> query pair (one per
    /// entity table) — this method itself has no table-specific knowledge.
    /// </summary>
    private static async Task ApplyCompletenessAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        string selectCompletenessSql, string updateCompletenessSql,
        string id, CompletenessStatus? markCompletenessAs, string now)
    {
        var row = await connection.QuerySingleAsync<(SafeValue<CompletenessStatus?> CompletenessStatus, IReadOnlyList<string> NoValueKnown)>(
            selectCompletenessSql, new { id }, transaction);
        var current = row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete;
        var next = markCompletenessAs ?? CompletenessGuard.ComputeNextStatus(current, row.NoValueKnown);

        if (next != current)
            await connection.ExecuteAsync(updateCompletenessSql, new { completenessStatus = next.ToString(), dateModified = now, id }, transaction);
    }

    // ── Idempotent ensure-exists helpers ────────────────────────────────────
    // Each is INSERT OR IGNORE keyed by a precomputed stable/real id, so calling any of these more
    // than once (from a dependent entity's own defensive check, or a concurrently-applied batch
    // that staged an Add for the same not-yet-existing row) is always safe.

    private async Task EnsureSourceExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string title, string type,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog, string? date = null, string? seriesId = null)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        // #180: seriesId defaults to null — every defensive call site (Character/Quote's own "ensure
        // the referenced Source exists" checks) has no Series context of its own; only a genuine
        // Source Add action (which does) passes one.
        var inserted = await connection.ExecuteAsync(Sql.Sources.InsertIfNotExists,
            new { Id = id, Title = title, Type = type, Date = date, SeriesId = seriesId, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "source", id, ChangeAction.Created,
                oldValue: null, newValue: new { title, type, date, seriesId }, connection, transaction);
    }

    private async Task EnsureSeriesExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string name, string? universeId,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        var inserted = await connection.ExecuteAsync(Sql.Series.InsertIfNotExists,
            new { Id = id, Name = name, UniverseId = universeId, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "series", id, ChangeAction.Created,
                oldValue: null, newValue: new { name, universeId }, connection, transaction);
    }

    private async Task EnsureUniverseExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string name,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        var inserted = await connection.ExecuteAsync(Sql.Universe.InsertIfNotExists,
            new { Id = id, Name = name, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "universe", id, ChangeAction.Created,
                oldValue: null, newValue: new { name }, connection, transaction);
    }

    private async Task EnsureCharacterExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string sourceId, string name,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.Characters.InsertIfNotExists,
            new { Id = id, Name = name, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "character", id, ChangeAction.Created,
                oldValue: null, newValue: new { name }, connection, transaction);

        // #179: Character<->Source is many-to-many via CharacterSources — always ensured alongside
        // the Character row itself, whether the Character was just inserted or already existed.
        await connection.ExecuteAsync(Sql.CharacterSources.InsertIfNotExists,
            new { Id = Guid.NewGuid().ToString(), CharacterId = id, SourceId = sourceId, DateCreated = now }, transaction);
    }

    private async Task EnsurePersonExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, string name,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog,
        string? dateOfBirth = null, string? dateOfDeath = null)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.People.InsertIfNotExists,
            new { Id = id, Name = name, DateOfBirth = dateOfBirth, DateOfDeath = dateOfDeath, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted > 0)
            await QuoteSeedWriter.LogChangeAsync(changeLog, "person", id, ChangeAction.Created,
                oldValue: null, newValue: new { name, dateOfBirth, dateOfDeath }, connection, transaction);
    }

    /// <summary>#68: id-keyed like Quote (see <see cref="ImportActionEntityTypes.Conversation"/>'s remark), not natural-key-keyed like the three helpers above — <paramref name="id"/> is the file's own explicit id, used as-is.</summary>
    private async Task EnsureStageDirectionExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, StageDirectionActionPayload payload,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.StageDirections.InsertIfNotExists,
            new { Id = id, Text = payload.Text, ImageUrl = payload.ImageUrl, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted == 0) return;

        foreach (var (language, translation) in payload.Translations)
        {
            await connection.ExecuteAsync(Sql.StageDirectionTranslations.Insert, new
            {
                Id               = Guid.NewGuid().ToString(),
                StageDirectionId = id,
                Language         = language,
                Text             = translation.Text,
                DateCreated      = now,
            }, transaction);
        }

        await QuoteSeedWriter.LogChangeAsync(changeLog, "stageDirection", id, ChangeAction.Created,
            oldValue: null, newValue: payload, connection, transaction);
    }

    /// <summary>#68: id-keyed like <see cref="EnsureStageDirectionExistsAsync"/> — see its remark.</summary>
    private async Task EnsureSoundCueExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id, SoundCueActionPayload payload,
        Guid batchId, string now, QuoteSeedWriter.ChangeLogContext changeLog)
    {
        // #59: stale-row hard-delete already happened in ClearStaleAddTargetsAsync — see its remarks.
        var inserted = await connection.ExecuteAsync(Sql.SoundCues.InsertIfNotExists,
            new { Id = id, Text = payload.Text, SoundFileUrl = payload.SoundFileUrl, ImageUrl = payload.ImageUrl, ImportBatchId = batchId, DateCreated = now }, transaction);
        if (inserted == 0) return;

        foreach (var (language, translation) in payload.Translations)
        {
            await connection.ExecuteAsync(Sql.SoundCueTranslations.Insert, new
            {
                Id          = Guid.NewGuid().ToString(),
                SoundCueId  = id,
                Language    = language,
                Text        = translation.Text,
                DateCreated = now,
            }, transaction);
        }

        await QuoteSeedWriter.LogChangeAsync(changeLog, "soundCue", id, ChangeAction.Created,
            oldValue: null, newValue: payload, connection, transaction);
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
            "Quote"          => QuoteFieldMerge.ToFieldMap(JsonSerializer.Deserialize<QuoteActionPayload>(json)!.Fields),
            "Source"         => ToFieldMap(JsonSerializer.Deserialize<SourceActionPayload>(json)!),
            "Character"      => ToFieldMap(JsonSerializer.Deserialize<CharacterActionPayload>(json)!),
            "Person"         => ToFieldMap(JsonSerializer.Deserialize<PersonActionPayload>(json)!),
            "StageDirection" => ToFieldMap(JsonSerializer.Deserialize<StageDirectionActionPayload>(json)!),
            "SoundCue"       => ToFieldMap(JsonSerializer.Deserialize<SoundCueActionPayload>(json)!),
            "Conversation"   => ToFieldMap(JsonSerializer.Deserialize<ConversationActionPayload>(json)!),
            _                => null,
        };
    }

    private static IReadOnlyDictionary<string, object?> ToFieldMap(SourceActionPayload payload) =>
        new Dictionary<string, object?> { ["title"] = payload.Title, ["type"] = payload.Type, ["date"] = payload.Date, ["seriesId"] = payload.SeriesId };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(CharacterActionPayload payload) =>
        new Dictionary<string, object?> { ["name"] = payload.Name, ["sourceId"] = payload.SourceId };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(PersonActionPayload payload) =>
        new Dictionary<string, object?> { ["name"] = payload.Name, ["dateOfBirth"] = payload.DateOfBirth, ["dateOfDeath"] = payload.DateOfDeath };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(StageDirectionActionPayload payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["imageUrl"] = payload.ImageUrl };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(SoundCueActionPayload payload) =>
        new Dictionary<string, object?> { ["text"] = payload.Text, ["soundFileUrl"] = payload.SoundFileUrl, ["imageUrl"] = payload.ImageUrl };

    private static IReadOnlyDictionary<string, object?> ToFieldMap(ConversationActionPayload payload) =>
        new Dictionary<string, object?> { ["description"] = payload.Description, ["lineCount"] = payload.Lines.Count };

    private static IReadOnlyList<string> ComputeAmbiguousFields(SystemImportAction action)
    {
        if (action.Status.Parsed != ImportActionStatus.Pending)
            return [];

        IReadOnlyDictionary<string, object?> existing;
        IReadOnlyDictionary<string, object?> incoming;

        switch (action.EntityType)
        {
            case ImportActionEntityTypes.Quote:
            {
                var existingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.ExistingValue!)!;
                var incomingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.IncomingValue!)!;
                existing = QuoteFieldMerge.ToFieldMap(existingPayload.Fields);
                incoming = QuoteFieldMerge.ToFieldMap(incomingPayload.Fields);
                break;
            }
            case ImportActionEntityTypes.Source:
            {
                existing = ToFieldMap(JsonSerializer.Deserialize<SourceActionPayload>(action.ExistingValue!)!);
                incoming = ToFieldMap(JsonSerializer.Deserialize<SourceActionPayload>(action.IncomingValue!)!);
                break;
            }
            case ImportActionEntityTypes.Person:
            {
                existing = ToFieldMap(JsonSerializer.Deserialize<PersonActionPayload>(action.ExistingValue!)!);
                incoming = ToFieldMap(JsonSerializer.Deserialize<PersonActionPayload>(action.IncomingValue!)!);
                break;
            }
            case ImportActionEntityTypes.StageDirection:
            {
                existing = ToFieldMap(JsonSerializer.Deserialize<StageDirectionActionPayload>(action.ExistingValue!)!);
                incoming = ToFieldMap(JsonSerializer.Deserialize<StageDirectionActionPayload>(action.IncomingValue!)!);
                break;
            }
            case ImportActionEntityTypes.SoundCue:
            {
                existing = ToFieldMap(JsonSerializer.Deserialize<SoundCueActionPayload>(action.ExistingValue!)!);
                incoming = ToFieldMap(JsonSerializer.Deserialize<SoundCueActionPayload>(action.IncomingValue!)!);
                break;
            }
            case ImportActionEntityTypes.Conversation:
            {
                var existingPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.ExistingValue!)!;
                var incomingPayload = JsonSerializer.Deserialize<ConversationActionPayload>(action.IncomingValue!)!;
                existing = new Dictionary<string, object?> { ["description"] = existingPayload.Description };
                incoming = new Dictionary<string, object?> { ["description"] = incomingPayload.Description };
                break;
            }
            default:
                return [];
        }

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

    private static Dictionary<string, FieldMergeDecision> ToSourceDecisionMap(ConflictDecisionRequest request)
    {
        var map = new Dictionary<string, FieldMergeDecision>();

        void Add(string field, FieldDecision? decision)
        {
            if (decision is null) return;
            map[field] = new FieldMergeDecision(decision.Choice, decision.Value);
        }

        Add("title", request.SourceTitle);
        Add("type", request.SourceType);
        Add("date", request.SourceDate);
        Add("seriesId", request.SourceSeriesId);

        return map;
    }

    private static Dictionary<string, FieldMergeDecision> ToPersonDecisionMap(ConflictDecisionRequest request)
    {
        var map = new Dictionary<string, FieldMergeDecision>();

        void Add(string field, FieldDecision? decision)
        {
            if (decision is null) return;
            map[field] = new FieldMergeDecision(decision.Choice, decision.Value);
        }

        Add("name", request.PersonName);
        Add("dateOfBirth", request.PersonDateOfBirth);
        Add("dateOfDeath", request.PersonDateOfDeath);

        return map;
    }

    private static Dictionary<string, FieldMergeDecision> ToStageDirectionDecisionMap(ConflictDecisionRequest request)
    {
        var map = new Dictionary<string, FieldMergeDecision>();

        void Add(string field, FieldDecision? decision)
        {
            if (decision is null) return;
            map[field] = new FieldMergeDecision(decision.Choice, decision.Value);
        }

        Add("text", request.StageDirectionText);
        Add("imageUrl", request.StageDirectionImageUrl);

        return map;
    }

    private static Dictionary<string, FieldMergeDecision> ToSoundCueDecisionMap(ConflictDecisionRequest request)
    {
        var map = new Dictionary<string, FieldMergeDecision>();

        void Add(string field, FieldDecision? decision)
        {
            if (decision is null) return;
            map[field] = new FieldMergeDecision(decision.Choice, decision.Value);
        }

        Add("text", request.SoundCueText);
        Add("soundFileUrl", request.SoundCueSoundFileUrl);
        Add("imageUrl", request.SoundCueImageUrl);

        return map;
    }
}
