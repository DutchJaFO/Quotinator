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
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Services;

/// <inheritdoc/>
public sealed class SqliteConflictResolutionService : IConflictResolutionService
{
    private readonly ISystemImportConflictReader _conflictReader;
    private readonly IConflictResolutionCoordinator _coordinator;
    private readonly IImportBatchRepository _importBatches;
    private readonly ISystemChangeLogWriter _changeLogWriter;

    /// <summary>Initialises the service with the generic Data-layer pieces it wraps and the Quotinator-specific batch label lookup.</summary>
    public SqliteConflictResolutionService(
        ISystemImportConflictReader conflictReader,
        IConflictResolutionCoordinator coordinator,
        IImportBatchRepository importBatches,
        ISystemChangeLogWriter changeLogWriter)
    {
        _conflictReader  = conflictReader;
        _coordinator     = coordinator;
        _importBatches   = importBatches;
        _changeLogWriter = changeLogWriter;
    }

    /// <inheritdoc/>
    public async Task<ConflictPageResponse> GetPagedAsync(string? batchId, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var result     = await _conflictReader.GetPagedAsync(batchId, status, page, pageSize);
        var labelCache = new Dictionary<string, string?>();

        var items = new List<ConflictSummaryResponse>(result.Items.Count);
        foreach (var conflict in result.Items)
            items.Add(await ToSummaryAsync(conflict, labelCache));

        return new ConflictPageResponse
        {
            TotalMatching = result.TotalCount,
            TotalPages    = result.TotalPages,
            Page          = result.Page,
            PageSize      = result.PageSize,
            Items         = items,
        };
    }

    /// <inheritdoc/>
    public async Task DecideAsync(Guid conflictId, ConflictDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var conflict = await _conflictReader.GetByIdAsync(conflictId) ?? throw new ConflictNotFoundException(conflictId);

        var existing   = DeserializeFields(conflict.ExistingValue);
        var incoming   = DeserializeFields(conflict.IncomingValue);
        var decisions  = ToDecisionMap(request);

        // Validate immediately — an ambiguous field with no decision must fail here, not silently
        // defer the problem to apply time.
        FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);

        var decisionsJson = JsonSerializer.Serialize(request);
        await _coordinator.DecideAsync(conflictId, decisionsJson);
    }

    /// <inheritdoc/>
    public async Task UndoDecisionAsync(Guid conflictId, CancellationToken cancellationToken = default)
        => await _coordinator.UndoDecisionAsync(conflictId);

    /// <inheritdoc/>
    public async Task<ConflictBatchStatusResponse?> ApplyBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var pending = await _coordinator.TryApplyBatchAsync(batchId, ApplyResolvedConflictAsync, cancellationToken);

        return pending is null
            ? null
            : new ConflictBatchStatusResponse { BatchId = batchId, PendingConflictIds = pending };
    }

    // ── The one domain-specific step ────────────────────────────────────────

    /// <summary>
    /// The only Quotinator-specific piece of the whole workflow: given a conflict whose decision has
    /// already been staged, resolve the final field values and write them to
    /// <c>Quotes</c>/<c>Sources</c>/<c>Characters</c>/<c>People</c>/<c>QuoteGenres</c>, using the exact
    /// same FK-resolution sequence already used for merge-policy duplicates during seeding/import.
    /// Quote translations are deliberately left untouched — they were already excluded from the
    /// mergeable field set before #149 existed (a distinct, manually-curated concern), and the original
    /// incoming file's translation data isn't available any more by the time a batch is applied.
    /// </summary>
    private async Task ApplyResolvedConflictAsync(SystemImportConflict conflict, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
    {
        var sqliteConnection  = (SqliteConnection)connection;
        var sqliteTransaction = (SqliteTransaction)transaction;

        var existing  = DeserializeFields(conflict.ExistingValue);
        var incoming  = DeserializeFields(conflict.IncomingValue);
        var request   = JsonSerializer.Deserialize<ConflictDecisionRequest>(conflict.MergedFields!)
                         ?? throw new InvalidOperationException($"Conflict '{conflict.Id}' is Decided but has no stored decision.");
        var decisions = ToDecisionMap(request);

        var result = FieldMergeResolver.ResolveWithDecisions(existing, incoming, decisions);
        var merged = result.MergedFields;

        var resolved = new SourceQuote
        {
            Id               = conflict.EntityId!,
            QuoteText        = (string)merged["quoteText"]!,
            OriginalLanguage = (string)merged["originalLanguage"]!,
            Source           = (string)merged["source"]!,
            Date             = (string?)merged["date"],
            Character        = (string?)merged["character"],
            Author           = (string?)merged["author"],
            Type             = (string)merged["type"]!,
            Genres           = (List<string>)merged["genres"]!,
        };

        var now         = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var batchId     = Guid.Parse(conflict.BatchId);
        var changeLog   = new QuoteSeedWriter.ChangeLogContext(_changeLogWriter, InitiatorType.WriteEndpoint, conflict.BatchId);
        var sourceIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        await sqliteConnection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote, new { id = resolved.Id }, sqliteTransaction);

        var sourceId    = await QuoteSeedWriter.GetOrCreateSourceAsync(sqliteConnection, resolved, sourceIndex, batchId, changeLog, sqliteTransaction);
        var characterId = await QuoteSeedWriter.GetOrCreateCharacterAsync(sqliteConnection, resolved, sourceId, characterIndex, batchId, changeLog, sqliteTransaction);
        var personId    = await QuoteSeedWriter.GetOrCreatePersonAsync(sqliteConnection, resolved, personIndex, batchId, changeLog, sqliteTransaction);

        await sqliteConnection.ExecuteAsync(
            Sql.Quotes.UpdateOnNewestWins,
            new
            {
                text    = resolved.QuoteText,
                lang    = resolved.OriginalLanguage,
                sid     = sourceId,
                cid     = characterId,
                pid     = personId,
                batchId,
                mod     = now,
                id      = resolved.Id,
            },
            sqliteTransaction);

        await QuoteSeedWriter.InsertGenresAsync(sqliteConnection, resolved, Guid.Parse(resolved.Id), now, sqliteTransaction);

        await QuoteSeedWriter.LogChangeAsync(changeLog, "quote", resolved.Id, ChangeAction.Modified,
            oldValue: existing, newValue: merged, sqliteConnection, sqliteTransaction);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<ConflictSummaryResponse> ToSummaryAsync(SystemImportConflict conflict, Dictionary<string, string?> labelCache)
    {
        var existingFields = JsonSerializer.Deserialize<QuoteConflictFieldsDto>(conflict.ExistingValue!) ?? new QuoteConflictFieldsDto();
        var incomingFields = JsonSerializer.Deserialize<QuoteConflictFieldsDto>(conflict.IncomingValue!) ?? new QuoteConflictFieldsDto();

        var ambiguous = conflict.Status == ImportConflictStatus.Pending
            ? ComputeAmbiguousFields(DeserializeFields(conflict.ExistingValue), DeserializeFields(conflict.IncomingValue))
            : [];

        return new ConflictSummaryResponse
        {
            Id                 = conflict.Id,
            EntityType         = conflict.EntityType,
            EntityId           = conflict.EntityId,
            Status             = conflict.Status,
            BatchId            = conflict.BatchId,
            BatchLabel         = await ResolveBatchLabelAsync(conflict.BatchId, labelCache),
            ExistingBatchId    = conflict.ExistingBatchId,
            ExistingBatchLabel = conflict.ExistingBatchId is null ? null : await ResolveBatchLabelAsync(conflict.ExistingBatchId, labelCache),
            SameFile           = conflict.ExistingBatchId is not null && conflict.ExistingBatchId == conflict.BatchId,
            AppliedPolicy      = conflict.AppliedPolicy.Raw,
            DetectedAt         = conflict.DetectedAt,
            ResolvedAt         = conflict.ResolvedAt,
            ExistingFields     = existingFields,
            IncomingFields     = incomingFields,
            AmbiguousFields    = ambiguous,
        };
    }

    private async Task<string?> ResolveBatchLabelAsync(string batchId, Dictionary<string, string?> cache)
    {
        if (cache.TryGetValue(batchId, out var cached)) return cached;
        if (!Guid.TryParse(batchId, out var id)) return null;

        var batch = await _importBatches.GetByIdAsync(id);
        var label = batch?.Name;
        cache[batchId] = label;
        return label;
    }

    private static IReadOnlyList<string> ComputeAmbiguousFields(IReadOnlyDictionary<string, object?> existing, IReadOnlyDictionary<string, object?> incoming)
    {
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

    private static IReadOnlyDictionary<string, object?> DeserializeFields(string? json)
    {
        var dto = JsonSerializer.Deserialize<QuoteConflictFieldsDto>(json ?? "{}") ?? new QuoteConflictFieldsDto();
        return new Dictionary<string, object?>
        {
            ["quoteText"]        = dto.QuoteText,
            ["originalLanguage"] = dto.OriginalLanguage,
            ["source"]           = dto.Source,
            ["date"]             = dto.Date,
            ["character"]        = dto.Character,
            ["author"]           = dto.Author,
            ["type"]             = dto.Type,
            ["genres"]           = dto.Genres,
        };
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
