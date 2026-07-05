using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Services;

/// <inheritdoc/>
public sealed class SqliteQuoteImportService : IQuoteImportService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IImportBatchRepository _importBatches;
    private readonly ISystemImportConflictWriter _conflictWriter;
    private readonly IReadOnlyDictionary<string, IQuoteSourceConverter> _converters;
    private readonly ManifestPolicy _configPolicy;

    /// <summary>Initialises the service with all dependencies required to import a single file.</summary>
    public SqliteQuoteImportService(
        IDbConnectionFactory factory,
        IImportBatchRepository importBatches,
        ISystemImportConflictWriter conflictWriter,
        IReadOnlyDictionary<string, IQuoteSourceConverter> converters,
        ManifestPolicy configPolicy)
    {
        _factory        = factory;
        _importBatches  = importBatches;
        _conflictWriter = conflictWriter;
        _converters     = converters;
        _configPolicy   = configPolicy;
    }

    /// <inheritdoc/>
    public async Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportRequestSettingsDto? settings, bool preview,
        CancellationToken cancellationToken = default)
    {
        var quotes = await LoadQuotesAsync(file, settings?.Converter, cancellationToken);
        var policy = ManifestPolicy.Resolve(ToManifestPolicy(settings?.DuplicateResolution), _configPolicy);
        var effectivePolicy = policy.ForQuotes;

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();

        var connection  = (SqliteConnection)uow.Connection!;
        var transaction = (SqliteTransaction?)uow.Transaction;

        var batch = new ImportBatch
        {
            Name           = fileName,
            Type           = ImportBatchType.Import.ToString(),
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(effectivePolicy.ToString(), effectivePolicy)
        };
        await _importBatches.InsertAsync(batch, uow);

        var seenIds        = new Dictionary<string, SourceQuote>(StringComparer.Ordinal);
        var sourceIndex     = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var characterIndex  = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personIndex     = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var conflicts       = new List<ImportConflictEntry>();
        var errors          = new List<ImportRowError>();
        var imported = 0;
        var updated  = 0;
        var skipped  = 0;
        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);

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

            var existingFields = seenIds.TryGetValue(q.Id, out var firstInFile)
                ? QuoteFieldMerge.ToFieldMap(firstInFile)
                : await QuoteSeedWriter.TryGetExistingFieldsAsync(connection, q.Id, transaction);

            if (existingFields is null)
            {
                seenIds[q.Id] = q;

                var sourceId    = await QuoteSeedWriter.GetOrCreateSourceAsync(connection, q, sourceIndex, batch.Id, transaction);
                var characterId = await QuoteSeedWriter.GetOrCreateCharacterAsync(connection, q, sourceId, characterIndex, batch.Id, transaction);
                var personId    = await QuoteSeedWriter.GetOrCreatePersonAsync(connection, q, personIndex, batch.Id, transaction);
                var quoteId     = Guid.Parse(q.Id);

                await connection.ExecuteAsync(
                    Sql.Quotes.Insert,
                    new
                    {
                        Id               = q.Id,
                        QuoteText        = q.QuoteText,
                        OriginalLanguage = q.OriginalLanguage,
                        SourceId         = sourceId,
                        CharacterId      = characterId,
                        PersonId         = personId,
                        ImportBatchId    = batch.Id,
                        DateCreated      = now
                    }, transaction);

                await QuoteSeedWriter.InsertTranslationsAsync(connection, q, quoteId, sourceId, now, transaction);
                await QuoteSeedWriter.InsertGenresAsync(connection, q, quoteId, now, transaction);
                imported++;
                continue;
            }

            var incomingFields = QuoteFieldMerge.ToFieldMap(q);
            var isMerge      = effectivePolicy is DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs;
            var mergeResult  = isMerge ? FieldMergeResolver.Resolve(existingFields, incomingFields, effectivePolicy) : null;
            var resolved     = mergeResult is not null ? QuoteFieldMerge.ApplyMergedFields(mergeResult.MergedFields, q) : q;

            await QuoteSeedWriter.LogImportConflictAsync(
                _conflictWriter, batch.Id, q.Id, effectivePolicy, existingFields, incomingFields, mergeResult, connection, transaction);

            conflicts.Add(new ImportConflictEntry
            {
                QuoteId       = q.Id,
                AppliedPolicy = ToWireString(effectivePolicy),
                Status        = effectivePolicy == DuplicateResolutionPolicy.Review ? "pending" : "resolved",
                ExistingValue = existingFields,
                IncomingValue = incomingFields,
                MergedFields  = mergeResult is null ? null : existingFields.Keys.ToDictionary(
                    f => f, f => mergeResult.FieldsFromIncoming.Contains(f) ? "theirs" : "ours"),
            });

            if (effectivePolicy is DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review)
            {
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote,      new { id = q.Id }, transaction);
            await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteForQuote, new { id = q.Id }, transaction);

            var owSourceId    = await QuoteSeedWriter.GetOrCreateSourceAsync(connection, resolved, sourceIndex, batch.Id, transaction);
            var owCharacterId = await QuoteSeedWriter.GetOrCreateCharacterAsync(connection, resolved, owSourceId, characterIndex, batch.Id, transaction);
            var owPersonId    = await QuoteSeedWriter.GetOrCreatePersonAsync(connection, resolved, personIndex, batch.Id, transaction);

            await connection.ExecuteAsync(
                Sql.Quotes.UpdateOnNewestWins,
                new
                {
                    text    = resolved.QuoteText,
                    lang    = resolved.OriginalLanguage,
                    sid     = owSourceId,
                    cid     = owCharacterId,
                    pid     = owPersonId,
                    batchId = batch.Id,
                    mod     = now,
                    id      = q.Id
                }, transaction);

            seenIds[q.Id] = resolved;

            var owQuoteId = Guid.Parse(q.Id);
            await QuoteSeedWriter.InsertTranslationsAsync(connection, resolved, owQuoteId, owSourceId, now, transaction);
            await QuoteSeedWriter.InsertGenresAsync(connection, resolved, owQuoteId, now, transaction);
            updated++;
        }

        await _importBatches.UpdateRecordCountAsync(batch.Id, imported + updated, uow);

        if (preview)
            await uow.RollbackAsync();
        else
            await uow.CommitAsync();

        return new ImportResultResponse
        {
            BatchId        = preview ? null : batch.Id,
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
            Conflicts = conflicts,
            Errors    = errors
        };
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
