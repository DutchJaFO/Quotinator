using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Services;

/// <inheritdoc/>
/// <remarks>
/// Thin orchestrator (#154) over the shared staging engine: stage via <see cref="ImportActionPlanner"/>
/// (one commit), then — unless <c>preview</c> — attempt apply via <see cref="IImportActionService"/>
/// (a second, separate commit; a crash between the two leaves the batch <c>Staged</c>, an already-safe
/// state by this design). Replaces the old single-pass detect-and-write loop entirely.
/// </remarks>
public sealed class SqliteQuoteImportService : IQuoteImportService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IImportBatchRepository _importBatches;
    private readonly IImportActionCoordinator _actionCoordinator;
    private readonly IImportActionService _actionService;
    private readonly ISystemImportActionReader _actionReader;
    private readonly IReadOnlyDictionary<string, IQuoteSourceConverter> _converters;
    private readonly ManifestPolicy _configPolicy;

    /// <summary>Initialises the service with all dependencies required to import a single file.</summary>
    public SqliteQuoteImportService(
        IDbConnectionFactory factory,
        IImportBatchRepository importBatches,
        IImportActionCoordinator actionCoordinator,
        IImportActionService actionService,
        ISystemImportActionReader actionReader,
        IReadOnlyDictionary<string, IQuoteSourceConverter> converters,
        ManifestPolicy configPolicy)
    {
        _factory           = factory;
        _importBatches     = importBatches;
        _actionCoordinator = actionCoordinator;
        _actionService     = actionService;
        _actionReader      = actionReader;
        _converters        = converters;
        _configPolicy      = configPolicy;
    }

    /// <inheritdoc/>
    public async Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportRequestSettingsDto? settings, bool preview,
        CancellationToken cancellationToken = default)
    {
        var quotes = await LoadQuotesAsync(file, settings?.Converter, cancellationToken);
        var policy = ManifestPolicy.Resolve(ToManifestPolicy(settings?.DuplicateResolution), _configPolicy);
        var effectivePolicy = policy.ForQuotes;

        var (valid, errors) = ValidateRows(quotes);

        var batch = new ImportBatch
        {
            Name           = fileName,
            Type           = new SafeValue<ImportBatchType?>(ImportBatchType.Import.ToString(), ImportBatchType.Import),
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(effectivePolicy.ToString(), effectivePolicy),
            Status         = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Staged.ToString(), ImportBatchStatus.Staged),
        };
        await _importBatches.InsertAsync(batch);
        var batchIdStr = batch.Id.ToString("D").ToUpperInvariant();

        IReadOnlyList<SystemImportAction> actions;
        using (var conn = (SqliteConnection)_factory.CreateConnection())
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            actions = await ImportActionPlanner.PlanAsync(conn, valid, batch.Id, effectivePolicy, tx);
            await _actionCoordinator.StageAsync(actions, conn, tx);
            tx.Commit();
        }

        // Matches the pre-#154 summary contract exactly: Skip and Review both never write, so both
        // count as "skipped" here (Review is additionally left Pending, awaiting a manual decision).
        var imported = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Add);
        var updated  = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));
        var skipped  = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review);

        if (!preview)
        {
            var applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Import, cancellationToken);
            if (applyResult is null)
            {
                batch.Status    = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Applied.ToString(), ImportBatchStatus.Applied);
                batch.AppliedAt = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
                batch.RecordCount = imported + updated;
                await _importBatches.UpdateAsync(batch);
            }
        }

        return new ImportResultResponse
        {
            BatchId        = batch.Id,
            Preview        = preview,
            ConflictPolicy = ToWireString(effectivePolicy),
            Summary = new ImportSummary
            {
                Total    = quotes.Count,
                Imported = imported,
                Updated  = updated,
                Skipped  = skipped,
                Errors   = errors.Count
            },
            Conflicts = BuildConflictEntries(actions),
            Errors    = errors
        };
    }

    /// <inheritdoc/>
    public async Task<ImportResultResponse> ApplyStagedBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await _importBatches.GetByIdAsync(batchId) ?? throw new ImportBatchNotFoundException(batchId);
        var batchIdStr = batchId.ToString("D").ToUpperInvariant();

        var actions = await _actionReader.GetAllForBatchAsync(batchIdStr);

        var imported = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Add);
        var updated  = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));
        var skipped  = actions.Count(a => a.EntityType == "Quote" && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review);
        var totalQuotes = actions.Count(a => a.EntityType == "Quote");

        var applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Import, cancellationToken);
        if (applyResult is null)
        {
            batch.Status      = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Applied.ToString(), ImportBatchStatus.Applied);
            batch.AppliedAt   = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
            batch.RecordCount = imported + updated;
            await _importBatches.UpdateAsync(batch);
        }

        return new ImportResultResponse
        {
            BatchId        = batchId,
            Preview        = false,
            ConflictPolicy = batch.ConflictPolicy.Parsed is { } p ? ToWireString(p) : batch.ConflictPolicy.Raw,
            Summary = new ImportSummary
            {
                Total    = totalQuotes,
                Imported = imported,
                Updated  = updated,
                Skipped  = skipped,
                Errors   = 0
            },
            Conflicts = BuildConflictEntries(actions),
            Errors    = []
        };
    }

    // ── Response shaping (temporary — Task 33 replaces this with the /import/actions response shape) ──

    private static IReadOnlyList<ImportConflictEntry> BuildConflictEntries(IReadOnlyList<SystemImportAction> actions)
    {
        var entries = new List<ImportConflictEntry>();
        foreach (var action in actions)
        {
            if (action.EntityType != "Quote" || action.ActionType.Parsed != ImportActionKind.Modify)
                continue;

            var existingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.ExistingValue!)!;
            var incomingPayload = JsonSerializer.Deserialize<QuoteActionPayload>(action.IncomingValue!)!;
            var existingFields  = QuoteFieldMerge.ToFieldMap(existingPayload.Fields);
            var incomingFields  = QuoteFieldMerge.ToFieldMap(incomingPayload.Fields);
            var policy          = action.AppliedPolicy.Parsed!.Value;
            var isPending       = action.Status.Parsed == ImportActionStatus.Pending;

            var isMerge = policy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
            var mergeResult = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, policy) : null;

            entries.Add(new ImportConflictEntry
            {
                QuoteId       = action.EntityId,
                AppliedPolicy = ToWireString(policy),
                Status        = isPending ? "pending" : "resolved",
                ExistingValue = existingFields,
                IncomingValue = incomingFields,
                MergedFields  = mergeResult is null ? null : existingFields.Keys.ToDictionary(
                    f => f, f => mergeResult.FieldsFromIncoming.Contains(f) ? "theirs" : "ours"),
            });
        }
        return entries;
    }

    private static (List<SourceQuote> Valid, List<ImportRowError> Errors) ValidateRows(IReadOnlyList<SourceQuote> quotes)
    {
        var valid  = new List<SourceQuote>();
        var errors = new List<ImportRowError>();
        var row = 0;
        foreach (var q in quotes)
        {
            row++;
            if (string.IsNullOrWhiteSpace(q.QuoteText) || string.IsNullOrWhiteSpace(q.Source))
            {
                errors.Add(new ImportRowError { Row = row, QuoteId = q.Id, Message = "Missing quote text or source." });
                continue;
            }
            if (!Guid.TryParse(q.Id, out _))
            {
                errors.Add(new ImportRowError { Row = row, QuoteId = q.Id, Message = $"'{q.Id}' is not a valid Id." });
                continue;
            }
            valid.Add(q);
        }
        return (valid, errors);
    }

    private async Task<List<SourceQuote>> LoadQuotesAsync(Stream file, string? converterName, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "quotinator-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rawPath = Path.Combine(tempDir, "input.raw");
            await using (var rawStream = File.Create(rawPath))
                await file.CopyToAsync(rawStream, cancellationToken);

            var contentPath = rawPath;

            if (!string.IsNullOrEmpty(converterName))
            {
                if (!_converters.TryGetValue(converterName, out var converter))
                    throw new UnknownConverterException(converterName);

                var convertedPath = Path.Combine(tempDir, "converted.json");
                try
                {
                    await converter.ConvertAsync(rawPath, convertedPath, cancellationToken);
                }
                catch (SourceConversionException ex)
                {
                    throw new QuoteImportValidationException($"Conversion via '{converterName}' failed: {ex.Message}", ex);
                }
                contentPath = convertedPath;
            }

            var json = await File.ReadAllTextAsync(contentPath, cancellationToken);
            if (!SourceQuoteFileReader.TryParse(json, out var quotes))
                throw new QuoteImportValidationException("File content is not valid JSON in Quotinator's canonical quote schema.");

            if (quotes is null || quotes.Count == 0)
                throw new QuoteImportValidationException("File contained no quotes.");

            return quotes;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup of a request-scoped temp directory */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup of a request-scoped temp directory */ }
        }
    }

    private static ManifestPolicy? ToManifestPolicy(ManifestPolicyDto? dto) => dto is null ? null : new ManifestPolicy(
        Default:      dto.Default,
        Quotes:       dto.Quotes,
        Sources:      dto.Sources,
        Characters:   dto.Characters,
        People:       dto.People,
        Translations: dto.Translations);

    // Response-facing wire value must match the same kebab-case format DuplicateResolutionPolicyJsonConverter
    // produces elsewhere (manifest.json, ImportBatches) — the DB storage columns use the plain PascalCase
    // enum name instead (via SafeValue<T>.Raw), which is a separate, intentionally different convention.
    private static string ToWireString(DuplicateResolutionPolicy policy) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(policy.ToString());
}
