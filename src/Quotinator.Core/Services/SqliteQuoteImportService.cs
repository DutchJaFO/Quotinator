using Quotinator.Data.Enums;
using System.Text.Json;
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

namespace Quotinator.Core.Services;

/// <inheritdoc/>
/// <remarks>
/// Thin orchestrator (#154) over the shared staging engine: stage via <see cref="ImportActionPlanner"/>
/// (one commit), then — unless <c>preview</c> — attempt apply via <see cref="IImportActionService"/>
/// (a second, separate commit; a crash between the two leaves the batch <c>Staged</c>, an already-safe
/// state by this design). Replaces the old single-pass detect-and-write loop entirely.
/// </remarks>
/// <summary>Initialises the service with all dependencies required to import a single file.</summary>
/// <param name="factory">Factory used to open the connection and transaction each stage/apply operation runs in.</param>
/// <param name="importBatches">Repository used to record each import run as an <c>Import_Batch</c> row.</param>
/// <param name="actionCoordinator">Coordinator used to stage and, unless previewing, apply the import actions produced from the file.</param>
/// <param name="actionService">Service used to apply a batch's decided/auto-resolved import actions to their target entities.</param>
/// <param name="actionReader">Reader used to look up the staged actions produced for a batch.</param>
/// <param name="converters">Registered <see cref="IQuoteSourceConverter"/> plugins, keyed by converter name, used to convert raw file content into the canonical schema.</param>
/// <param name="configPolicy">Policy governing default conflict-resolution and completeness behaviour for staged actions.</param>
/// <param name="fileResources">Repository used to record the imported file as a <c>FileResource</c> row.</param>
public sealed class SqliteQuoteImportService(
    IDbConnectionFactory factory,
    IImportBatchRepository importBatches,
    IImportActionCoordinator actionCoordinator,
    IImportActionService actionService,
    IImportActionReader actionReader,
    IReadOnlyDictionary<string, IQuoteSourceConverter> converters,
    ManifestPolicy configPolicy,
    IFileResourceRepository fileResources) : IQuoteImportService
{
    private readonly IDbConnectionFactory _factory = factory;
    private readonly IImportBatchRepository _importBatches = importBatches;
    private readonly IImportActionCoordinator _actionCoordinator = actionCoordinator;
    private readonly IImportActionService _actionService = actionService;
    private readonly IImportActionReader _actionReader = actionReader;
    private readonly IReadOnlyDictionary<string, IQuoteSourceConverter> _converters = converters;
    private readonly ManifestPolicy _configPolicy = configPolicy;
    private readonly IFileResourceRepository _fileResources = fileResources;

    /// <inheritdoc/>
    public async Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportSettingsDto? settings, bool preview, bool purgeOnSuccess = false,
        CancellationToken cancellationToken = default)
    {
        var (parsed, rawContent) = await LoadSourceFileAsync(file, settings?.Converter, settings?.ConverterOptions, cancellationToken);
        var quotes = parsed.Quotes;
        var policy = ManifestPolicy.Resolve(ToManifestPolicy(settings?.DuplicateResolution), _configPolicy);
        var effectivePolicy = policy.ForQuotes;

        var (valid, errors) = ValidateRows(quotes);

        var batch = new ImportBatchEntity
        {
            Name           = fileName,
            Type           = new SafeValue<ImportBatchType?>(ImportBatchType.Import.ToString(), ImportBatchType.Import),
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(effectivePolicy.ToString(), effectivePolicy),
            Status         = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Staged.ToString(), ImportBatchStatus.Staged),
        };
        await _importBatches.InsertAsync(batch);
        var batchIdStr = batch.Id.ToCanonicalId();

        // #251 — a multipart upload carries no folder information, only a bare filename, so
        // homeDirectoryKey is also always null (#252) — there is no root for it to be relative to.
        // Converter/ConverterOptions come from the request's own settings, not the raw content itself,
        // since the same bytes could be uploaded again later under different settings.
        await _fileResources.WriteAsync(
            fileName, originalFolderPath: null, FileResourceOrigin.Upload, rawContent, batch.Id,
            settings?.Converter, settings?.ConverterOptions?.GetRawText(), homeDirectoryKey: null,
            cancellationToken: cancellationToken);

        IReadOnlyList<ImportActionEntity> actions;
        using (var conn = (SqliteConnection)_factory.CreateConnection())
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            actions = await ImportActionPlanner.PlanAsync(conn, valid, batch.Id, effectivePolicy, tx,
                parsed.Sources, parsed.StageDirections, parsed.SoundCues, parsed.Conversations, parsed.People,
                parsed.Series, parsed.Universe, parsed.Characters);
            await _actionCoordinator.StageAsync(actions, conn, tx);
            tx.Commit();
        }

        // Matches the pre-#154 summary contract exactly: Skip and Review both never write, so both
        // count as "skipped" here (Review is additionally left Pending, awaiting a manual decision).
        var imported = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Add);
        // #168: a Blocked action has no AppliedPolicy (nothing decided yet), so "is not (Skip or
        // Review)" is true for it too — must be excluded explicitly, or a held/unwritten Blocked
        // action gets miscounted as a genuine update. #153: a Stale action is the same — nothing
        // decided yet, no AppliedPolicy.
        var updated  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.Status.Parsed is not (ImportActionStatus.Blocked or ImportActionStatus.Stale)
                                       && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));
        var skipped  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review);

        IReadOnlyList<Guid> pendingActionIds = [.. actions
            .Where(a => a.Status.Parsed is ImportActionStatus.Pending or ImportActionStatus.Blocked or ImportActionStatus.Stale)
            .Select(a => a.Id)];

        if (!preview)
        {
            var applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Import, purgeOnSuccess, cancellationToken);
            if (applyResult is null)
            {
                // #177: Status/AppliedAt are now set by ApplyBatchAsync itself, the shared choke point
                // every caller goes through — this call site only owns its own Quote-specific RecordCount.
                await _importBatches.UpdateRecordCountAsync(batch.Id, imported + updated);
            }
            else
            {
                pendingActionIds = applyResult.PendingActionIds;
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
            Conflicts        = BuildConflictEntries(actions),
            PendingActionIds = pendingActionIds,
            Errors           = errors,
            Report           = ImportActionReportBuilder.Build(fileName, actions)
        };
    }

    /// <inheritdoc/>
    public async Task<ImportResultResponse> ApplyStagedBatchAsync(Guid batchId, bool purgeOnSuccess = false, CancellationToken cancellationToken = default)
    {
        var batch = await _importBatches.GetByIdAsync(batchId) ?? throw new ImportBatchNotFoundException(batchId);
        var batchIdStr = batchId.ToCanonicalId();

        var actions = await _actionReader.GetAllForBatchAsync(batchIdStr);

        var imported = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Add);
        // #168: a Blocked action has no AppliedPolicy (nothing decided yet), so "is not (Skip or
        // Review)" is true for it too — must be excluded explicitly, or a held/unwritten Blocked
        // action gets miscounted as a genuine update. #153: a Stale action is the same — nothing
        // decided yet, no AppliedPolicy.
        var updated  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.Status.Parsed is not (ImportActionStatus.Blocked or ImportActionStatus.Stale)
                                       && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));
        var skipped  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                       && a.AppliedPolicy.Parsed is DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review);
        var totalQuotes = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote);

        var applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Import, purgeOnSuccess, cancellationToken);
        IReadOnlyList<Guid> pendingActionIds = [];
        if (applyResult is null)
        {
            // #177: Status/AppliedAt are now set by ApplyBatchAsync itself, the shared choke point
            // every caller goes through — this call site only owns its own Quote-specific RecordCount.
            await _importBatches.UpdateRecordCountAsync(batch.Id, imported + updated);
        }
        else
        {
            pendingActionIds = applyResult.PendingActionIds;
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
            Conflicts        = BuildConflictEntries(actions),
            PendingActionIds = pendingActionIds,
            Errors           = [],
            Report           = ImportActionReportBuilder.Build(batch.Name, actions)
        };
    }

    // ── Response shaping (temporary — Task 33 replaces this with the /import/actions response shape) ──

    private static List<ImportConflictEntry> BuildConflictEntries(IReadOnlyList<ImportActionEntity> actions)
    {
        var entries = new List<ImportConflictEntry>();
        foreach (var action in actions)
        {
            // #168: a Blocked Quote action (like a Blocked Source action) has no AppliedPolicy yet —
            // nothing has been decided. It's surfaced to callers via PendingActionIds, not this legacy
            // per-Quote conflicts list, which only ever covers a resolved-or-pending policy decision.
            // #153: a Stale action is the same — nothing decided yet either.
            if (action.EntityType != ImportActionEntityTypes.Quote || action.ActionType.Parsed != ImportActionKind.Modify
                || action.Status.Parsed is ImportActionStatus.Blocked or ImportActionStatus.Stale)
                continue;

            var existingPayload = JsonSerializer.Deserialize<QuoteActionPayloadDto>(action.ExistingValue!)!;
            var incomingPayload = JsonSerializer.Deserialize<QuoteActionPayloadDto>(action.IncomingValue!)!;
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

    private static (List<SourceQuoteDto> Valid, List<ImportRowError> Errors) ValidateRows(IReadOnlyList<SourceQuoteDto> quotes)
    {
        var valid  = new List<SourceQuoteDto>();
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

    /// <summary>
    /// #68: full extended parse (quotes plus stageDirections/soundCues/conversations). A converter's
    /// output is always plain quotes-only JSON (no converter plugin produces the extended object
    /// shape), so this naturally yields empty extended sections whenever a converter ran — no
    /// conditional needed; conversations are only ever present when the uploaded file is already in
    /// Quotinator's own extended format (e.g. re-uploading a curated source file with no converter).
    /// </summary>
    private async Task<(ParsedSourceFileDto Parsed, string RawContent)> LoadSourceFileAsync(Stream file, string? converterName, JsonElement? converterOptions, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "quotinator-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rawPath = Path.Combine(tempDir, "input.raw");
            await using (var rawStream = File.Create(rawPath))
                await file.CopyToAsync(rawStream, cancellationToken);

            // #251 — captured before any converter runs: this is the file the caller actually
            // uploaded, not a converter's transformed output, matching "reconstruct the original
            // content of a file" as the provenance record's own stated goal.
            var rawContent = await File.ReadAllTextAsync(rawPath, cancellationToken);
            var contentPath = rawPath;

            if (!string.IsNullOrEmpty(converterName))
            {
                if (!_converters.TryGetValue(converterName, out var converter))
                    throw new UnknownConverterException(converterName);

                var convertedPath = Path.Combine(tempDir, "converted.json");
                try
                {
                    await converter.ConvertAsync(rawPath, convertedPath, converterOptions, cancellationToken);
                }
                catch (SourceConversionException ex)
                {
                    throw new QuoteImportValidationException($"Conversion via '{converterName}' failed: {ex.Message}", ex);
                }
                contentPath = convertedPath;
            }

            var json = await File.ReadAllTextAsync(contentPath, cancellationToken);
            if (!SourceQuoteFileReader.TryParseExtended(json, out var parsed))
                throw new QuoteImportValidationException("File content is not valid JSON in Quotinator's canonical quote schema.");

            if (parsed is null || parsed.Quotes.Count == 0)
                throw new QuoteImportValidationException("File contained no quotes.");

            return (parsed, rawContent);
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
