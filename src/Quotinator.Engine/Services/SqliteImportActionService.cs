using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Database;
using Quotinator.Engine.Models;

namespace Quotinator.Engine.Services;

/// <inheritdoc/>
public sealed class SqliteImportActionService : IImportActionService
{
    private readonly ISystemImportActionReader _actionReader;
    private readonly IImportActionCoordinator _coordinator;
    private readonly ISystemChangeLogWriter _changeLogWriter;

    /// <summary>Initialises the service with the generic Data-layer pieces it wraps.</summary>
    public SqliteImportActionService(
        ISystemImportActionReader actionReader,
        IImportActionCoordinator coordinator,
        ISystemChangeLogWriter changeLogWriter)
    {
        _actionReader    = actionReader;
        _coordinator     = coordinator;
        _changeLogWriter = changeLogWriter;
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
        if (action.EntityType != "Quote")
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
        var pending = await _coordinator.TryApplyBatchAsync(
            batchId, (action, conn, tx) => ApplyResolvedActionAsync(action, conn, tx, initiatedByType), cancellationToken);

        return pending is null
            ? null
            : new ImportActionBatchStatusResponse { BatchId = batchId, PendingActionIds = pending };
    }

    /// <inheritdoc/>
    public async Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default)
        => await _coordinator.DiscardBatchAsync(batchId, cancellationToken);

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
            case "Source":
            {
                var payload = JsonSerializer.Deserialize<SourceActionPayload>(action.IncomingValue!)!;
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Title, payload.Type, batchId, now, changeLog);
                break;
            }
            case "Character":
            {
                var payload = JsonSerializer.Deserialize<CharacterActionPayload>(action.IncomingValue!)!;
                // Defensive: Characters.SourceId is a real FK, but System_ImportActions rows apply in
                // whatever order the coordinator returns them — this action's own Source may not have
                // applied yet. Idempotent, so re-running it here is safe either way.
                await EnsureSourceExistsAsync(sqliteConnection, sqliteTransaction, payload.SourceId, payload.SourceTitle, payload.SourceType, batchId, now, changeLog);
                await EnsureCharacterExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.SourceId, payload.Name, batchId, now, changeLog);
                break;
            }
            case "Person":
            {
                var payload = JsonSerializer.Deserialize<PersonActionPayload>(action.IncomingValue!)!;
                await EnsurePersonExistsAsync(sqliteConnection, sqliteTransaction, action.EntityId, payload.Name, batchId, now, changeLog);
                break;
            }
            case "Quote":
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
        if (action.EntityType != "Quote" || action.Status.Parsed != ImportActionStatus.Pending)
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
        if (action.EntityType != "Quote") return [];

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
            if (candidate.EntityType == "Source"    && candidate.EntityId == payload.SourceId) related.Add(candidate.Id);
            if (candidate.EntityType == "Character" && payload.CharacterId is not null && candidate.EntityId == payload.CharacterId) related.Add(candidate.Id);
            if (candidate.EntityType == "Person"    && payload.PersonId is not null && candidate.EntityId == payload.PersonId) related.Add(candidate.Id);
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
