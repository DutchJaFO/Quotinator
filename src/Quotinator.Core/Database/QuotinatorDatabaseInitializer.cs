using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Core.Services;

namespace Quotinator.Core.Database;

/// <summary>
/// Quotinator-specific database initialiser. Extends <see cref="DatabaseInitializer"/> with
/// seeding logic for Quotinator domain tables (Quotes, Sources, Characters, People, Genres).
/// </summary>
public sealed class QuotinatorDatabaseInitializer : DatabaseInitializer
{
    private readonly IReadOnlyList<SeedBatch>       _batches;
    private readonly IImportBatchRepository         _importBatches;
    private readonly IImportActionCoordinator       _actionCoordinator;
    private readonly IImportActionService           _actionService;
    private readonly ISourceCacheUpdater            _sourceCacheUpdater;
    private readonly bool                           _autoUpdateSources;
    private readonly IRuleFileOverridePathResolver  _ruleFileOverridePathResolver;
    private readonly ISourceFileOverrideRegistry    _sourceFileOverrideRegistry;

    /// <summary>Initialises the instance with all dependencies required for Quotinator seeding.</summary>
    public QuotinatorDatabaseInitializer(
        IDbConnectionFactory           factory,
        DatabaseOptions                options,
        IReadOnlyList<SchemaMigration> migrations,
        IReadOnlyList<SeedBatch>       batches,
        IImportBatchRepository         importBatches,
        IImportActionCoordinator       actionCoordinator,
        IImportActionService           actionService,
        ISystemAuditWriter             auditWriter,
        ICallerContext                 callerContext,
        ILogger<DatabaseInitializer>   logger,
        ISourceCacheUpdater            sourceCacheUpdater,
        bool                           autoUpdateSources,
        IRuleFileOverridePathResolver  ruleFileOverridePathResolver,
        ISourceFileOverrideRegistry    sourceFileOverrideRegistry,
        SchemaBaseline?                baseline = null)
        : base(factory, options, migrations, auditWriter, callerContext, logger, baseline)
    {
        _batches            = batches;
        _importBatches      = importBatches;
        _actionCoordinator  = actionCoordinator;
        _actionService      = actionService;
        _sourceCacheUpdater = sourceCacheUpdater;
        _autoUpdateSources  = autoUpdateSources;
        _ruleFileOverridePathResolver = ruleFileOverridePathResolver;
        _sourceFileOverrideRegistry   = sourceFileOverrideRegistry;
    }

    /// <inheritdoc/>
    protected override async Task OnInitialisedAsync(SqliteConnection connection)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceRefresh: false)).EffectiveBatches;
        await SeedIfEmptyAsync(connection, effectiveBatches);
        await ReSeedGenresIfEmptyAsync(connection, effectiveBatches);
        await LogDatabaseStatsAsync(connection);
    }

    /// <inheritdoc/>
    protected override async Task OnReseedAsync(SqliteConnection connection, bool forceSourceRefresh)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceSourceRefresh)).EffectiveBatches;
        var totalFiles = effectiveBatches.Sum(b => b.Files.Count);
        Logger.LogInformation("[Database - Seed] reseed requested — clearing all data and reimporting from {Count} source file(s)...", totalFiles);

        await SharedSeedLock.WaitAsync();
        try
        {
            await TruncateDataAsync(connection);
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new SystemAuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reseed,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Seed] reseed complete");
    }

    /// <inheritdoc/>
    protected override async Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceSourceRefresh)).EffectiveBatches;
        var totalFiles = effectiveBatches.Sum(b => b.Files.Count);
        Logger.LogInformation("[Database - Init] reset requested — rebuilding schema and reimporting from {Count} source file(s)...", totalFiles);

        await SharedSeedLock.WaitAsync();
        try
        {
            await DropAndRebuildAsync(connection, preserveSchemaVersion);
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new SystemAuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reset,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Init] reset complete");
    }

    /// <summary>Truncates all Quotinator domain data tables. Called during reseed/reset.</summary>
    private static async Task TruncateDataAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        await connection.ExecuteAsync(Sql.ConversationLines.DeleteAll);
        await connection.ExecuteAsync(Sql.StageDirectionTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.SoundCueTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.Conversations.DeleteAll);
        await connection.ExecuteAsync(Sql.StageDirections.DeleteAll);
        await connection.ExecuteAsync(Sql.SoundCues.DeleteAll);
        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteAll);
        await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.SourceTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.CharacterTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.Quotes.DeleteAll);
        await connection.ExecuteAsync(Sql.Characters.DeleteAll);
        await connection.ExecuteAsync(Sql.People.DeleteAll);
        await connection.ExecuteAsync(Sql.Sources.DeleteAll);
        await connection.ExecuteAsync(Quotinator.Data.Queries.Sql.ImportBatches.DeleteAll);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// #221: builds a real <see cref="FileImportReport"/> per file via
    /// <see cref="ImportActionPlanner.PlanAsync"/> — the same classifier the real seeding pipeline
    /// uses — but never calls <see cref="IImportActionCoordinator.StageAsync"/> or
    /// <see cref="IImportActionService.ApplyBatchAsync"/>, so nothing is ever written. This is safe
    /// because <c>PlanAsync</c> itself only ever reads (every database call in it is a <c>SELECT</c>).
    /// See <see cref="SeedPreviewResult.Reports"/> for the one known limitation this implies (no
    /// cross-file simulation within a single preview call).
    /// </remarks>
    public override async Task<SeedPreviewResult> PreviewSeedAsync()
    {
        // Preview reflects whatever is already cached on disk — it never triggers a network call,
        // even when Quotinator__AutoUpdateSources is true, so calling it has no side effects.
        var resolution       = await ResolveEffectiveBatchesAsync(forceRefresh: false, allowNetworkOverride: false);
        var effectiveBatches = resolution.EffectiveBatches;
        var resultsByName    = resolution.Results.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var filePreviews = new List<SeedFilePreview>();
        var reports      = new List<FileImportReport>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var fileName    = Path.GetFileName(seedFile.FilePath);
                var (parsed, issue) = LoadSourceFileAsync(seedFile.FilePath);
                var quotes      = parsed.Quotes;
                var refreshResult = resultsByName.GetValueOrDefault(fileName);
                var filePolicy  = ManifestPolicy.Resolve(seedFile.Policy, batch.Policy);
                filePreviews.Add(new SeedFilePreview(fileName, quotes.Count, refreshResult?.Outcome, refreshResult?.LastRefreshedAtUtc, issue));

                var conflictRules = await LoadConflictRulesAsync(seedFile.RuleFilePath, batch.Origin);
                var sourceAliases = await LoadSourceAliasesAsync(seedFile.SourceAliasFilePath, batch.Origin);

                var actions = await ImportActionPlanner.PlanAsync(connection, quotes, Guid.NewGuid(), filePolicy.ForQuotes, transaction: null,
                    parsed.Sources, parsed.StageDirections, parsed.SoundCues, parsed.Conversations, parsed.People,
                    parsed.Series, parsed.Universe, parsed.Characters, conflictRules, sourceAliases);

                reports.Add(ImportActionReportBuilder.Build(fileName, actions));
            }
        }

        return new SeedPreviewResult(filePreviews, reports);
    }

    /// <inheritdoc/>
    public override async Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
        => await _sourceCacheUpdater.ResolveAsync(_batches, _autoUpdateSources, force);

    /// <summary>
    /// Resolves <see cref="_batches"/> to their effective form for this call via
    /// <see cref="_sourceCacheUpdater"/>. <see cref="_batches"/> itself is never mutated — this
    /// singleton is shared across concurrent Preview/Reseed/Reset calls, so each caller gets its
    /// own local effective list instead of a shared field that could race.
    /// </summary>
    /// <param name="forceRefresh">Bypasses the TTL check for every candidate entry; ignored when network access is not allowed.</param>
    /// <param name="allowNetworkOverride">
    /// Overrides <see cref="_autoUpdateSources"/> for this call. Used by <see cref="PreviewSeedAsync"/>
    /// to guarantee it never makes a network call regardless of configuration.
    /// </param>
    private async Task<SourceCacheResolution> ResolveEffectiveBatchesAsync(bool forceRefresh, bool? allowNetworkOverride = null)
    {
        var allowNetwork = allowNetworkOverride ?? _autoUpdateSources;
        return await _sourceCacheUpdater.ResolveAsync(_batches, allowNetwork, forceRefresh);
    }

    private async Task SeedIfEmptyAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        await SharedSeedLock.WaitAsync();
        try
        {
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }
    }

    private async Task SeedIfEmptyInternalAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        var count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (count > 0) return;

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] no source files configured — database will be empty");
            return;
        }

        LastSeedReport = [];

        var reports     = new List<FileImportReport>();
        var stagedFiles = new List<string>();

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var fileName        = Path.GetFileName(seedFile.FilePath);
                var (parsed, _)     = LoadSourceFileAsync(seedFile.FilePath);
                var quotes          = parsed.Quotes;
                var filePolicy      = ManifestPolicy.Resolve(seedFile.Policy, batch.Policy);
                var policy          = filePolicy.ForQuotes;
                var conflictRules   = await LoadConflictRulesAsync(seedFile.RuleFilePath, batch.Origin);
                var sourceAliases   = await LoadSourceAliasesAsync(seedFile.SourceAliasFilePath, batch.Origin);

                Logger.LogInformation("[Database - Seed] importing {Count} quotes from {File} ({Batch})...",
                    quotes.Count, fileName, batch.Label);

                var importBatch = await CreateImportBatchAsync(batch, seedFile, filePolicy);
                var batchIdStr  = importBatch.Id.ToCanonicalId();

                IReadOnlyList<SystemImportAction> actions;
                using (var tx = connection.BeginTransaction())
                {
                    actions = await ImportActionPlanner.PlanAsync(connection, quotes, importBatch.Id, policy, tx,
                        parsed.Sources, parsed.StageDirections, parsed.SoundCues, parsed.Conversations, parsed.People,
                        parsed.Series, parsed.Universe, parsed.Characters, conflictRules, sourceAliases);
                    await _actionCoordinator.StageAsync(actions, connection, tx);
                    tx.Commit();
                }

                var report = ImportActionReportBuilder.Build(fileName, actions);
                reports.Add(report);
                Logger.LogInformation("[Database - Seed] {File} report: {Report}", fileName, FormatReport(report));

                var applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Seed);
                if (applyResult is null)
                {
                    var imported = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Add);
                    var updated  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                                   && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));

                    importBatch.Status      = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Applied.ToString(), ImportBatchStatus.Applied);
                    importBatch.AppliedAt   = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
                    importBatch.RecordCount = imported + updated;
                    await _importBatches.UpdateAsync(importBatch);

                    await AuditWriter.WriteAsync(new SystemAuditEntry
                    {
                        TableName   = "Quotes",
                        RecordId    = batchIdStr,
                        Operation   = AuditOperation.BulkInsert,
                        Agent       = CallerContext.Agent,
                        PerformedAt = DateTime.UtcNow,
                    }, connection);
                }
                else
                {
                    stagedFiles.Add(fileName);
                    Logger.LogInformation(
                        "[Database - Seed] {File} left staged awaiting review — batch {BatchId}, {Count} action(s) pending a decision (GET /import/actions?batchId=<BatchId>)",
                        fileName, batchIdStr, applyResult.PendingActionIds.Count);
                }
            }
        }

        LastSeedReport = reports;

        Logger.LogInformation("[Database - Seed] seeding complete — {Count} file(s) processed", reports.Count);

        if (stagedFiles.Count > 0)
            Logger.LogInformation(
                "[Database - Seed] {Count} source file(s) staged awaiting review: {Files}",
                stagedFiles.Count, string.Join(", ", stagedFiles));
    }

    private async Task ReSeedGenresIfEmptyAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        var genreCount = await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll);
        if (genreCount > 0) return;

        var quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (quoteCount == 0) return;

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] cannot re-seed genres — no source files configured");
            return;
        }

        Logger.LogInformation("[Database - Seed] re-seeding genres from source files...");

        var now      = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var inserted = 0;

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var (quotes, _) = LoadQuotesFromFile(seedFile.FilePath);
                foreach (var q in quotes)
                {
                    foreach (var genre in q.Genres)
                    {
                        if (QuoteSeedWriter.TryNormaliseGenre(genre, out var g))
                        {
                            await connection.ExecuteAsync(
                                Sql.QuoteGenres.InsertWithExistsGuard,
                                new { Id = Guid.NewGuid().ToString(), QuoteId = q.Id, Genre = g.ToString(), DateCreated = now });
                            inserted++;
                        }
                    }
                }
            }
        }

        Logger.LogInformation("[Database - Seed] genre re-seed complete — {Count} genre rows processed", inserted);
    }

    private async Task LogDatabaseStatsAsync(SqliteConnection connection)
    {
        QuoteCount     = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountActive);
        SourceCount    = await connection.ExecuteScalarAsync<int>(Sql.Sources.CountActive);
        CharacterCount = await connection.ExecuteScalarAsync<int>(Sql.Characters.CountActive);
        PeopleCount    = await connection.ExecuteScalarAsync<int>(Sql.People.CountActive);

        Logger.LogInformation(
            "[Database - Stats] {Quotes} quotes  {Sources} sources  {Characters} characters  {People} people",
            QuoteCount, SourceCount, CharacterCount, PeopleCount);
    }

    private async Task<ImportBatch> CreateImportBatchAsync(SeedBatch seedBatch, SeedFile seedFile, ManifestPolicy filePolicy)
    {
        var type   = DetermineType(seedBatch.Origin);
        var policy = filePolicy.ForQuotes;
        var batch = new ImportBatch
        {
            Name           = Path.GetFileName(seedFile.FilePath),
            Type           = new SafeValue<ImportBatchType?>(type.ToString(), type),
            Url            = seedFile.Url,
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
            Status         = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Staged.ToString(), ImportBatchStatus.Staged),
        };
        await _importBatches.InsertAsync(batch);
        return batch;
    }

    // Origin decides the type, not URL presence — a user-imports-folder file that happens to
    // declare its own url/github manifest entry is still UserSeed, never Seed, so provenance
    // always reflects which folder the file was actually scanned from. A bundled file is always
    // Seed regardless of whether it has a URL — "System" is reserved for the database's own
    // System_-prefixed infrastructure tables (see Sql.Schema.GetUserTables), not for quote
    // content provenance; internally-authored bundled content (e.g. quotinator-curated.json) is
    // still replaceable, re-seeded content, just like externally-sourced bundled content.
    private static ImportBatchType DetermineType(SeedBatchOrigin origin) =>
        origin == SeedBatchOrigin.UserImports
            ? ImportBatchType.UserSeed
            : ImportBatchType.Seed;

    private (List<SourceQuote> Quotes, SeedFileIssue? Issue) LoadQuotesFromFile(string filePath)
    {
        var (parsed, issue) = LoadSourceFileAsync(filePath);
        return (parsed.Quotes.ToList(), issue);
    }

    /// <summary>
    /// #68: full extended parse (quotes plus stageDirections/soundCues/conversations), used by
    /// <see cref="SeedIfEmptyInternalAsync"/> to plan the three new entity types alongside quotes.
    /// <see cref="LoadQuotesFromFile"/> wraps this for the two call sites that only need the quotes —
    /// one parsing implementation, not two.
    /// </summary>
    private (ParsedSourceFile Parsed, SeedFileIssue? Issue) LoadSourceFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return (new ParsedSourceFile { Quotes = [] }, SeedFileIssue.Missing);

        var json = File.ReadAllText(filePath);
        if (SourceQuoteFileReader.TryParseExtended(json, out var parsed)) return (parsed!, null);

        Logger.LogWarning("[Database - Seed] {File} is empty or not valid JSON — skipping", Path.GetFileName(filePath));
        return (new ParsedSourceFile { Quotes = [] }, SeedFileIssue.InvalidJson);
    }

    /// <summary>Single-line, grep-friendly rendering of a <see cref="FileImportReport"/> for the seed log (#221).</summary>
    private static string FormatReport(FileImportReport report)
        => string.Join(" ", report.EntityTypes.Select(kv =>
            $"{kv.Key}[new={kv.Value.New} modified={kv.Value.Modified} blocked={kv.Value.Blocked} discarded={kv.Value.Discarded} pending={kv.Value.Pending} stale={kv.Value.Stale}]"));

    private static readonly JsonSerializerOptions ConflictRuleReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// #181: loads a source's own per-source conflict-resolution rule file, referenced by the
    /// manifest entry's <c>ruleFile</c> property. Missing/absent/invalid all resolve to
    /// <see cref="ConflictRuleLookup.Empty"/> — a rule file is an optimisation, never a hard
    /// requirement for seeding to proceed, matching <see cref="LoadSourceFileAsync"/>'s own
    /// fail-open convention for the source file itself. #153: prefers a registered, hash-verified
    /// override over the bundled/image copy, when one exists — see
    /// <see cref="EffectiveRuleFileResolver"/>.
    /// </summary>
    private async Task<ConflictRuleLookup> LoadConflictRulesAsync(string? ruleFilePath, SeedBatchOrigin origin)
    {
        if (ruleFilePath is null) return ConflictRuleLookup.Empty;

        var effectivePath = await EffectiveRuleFileResolver.ResolveEffectivePathAsync(
            ruleFilePath, origin, _ruleFileOverridePathResolver, _sourceFileOverrideRegistry, Logger);

        if (!File.Exists(effectivePath))
        {
            Logger.LogWarning("[Database - Seed] conflict-resolution rule file {File} referenced in manifest but not found — continuing without rules",
                Path.GetFileName(effectivePath));
            return ConflictRuleLookup.Empty;
        }

        try
        {
            var json     = await File.ReadAllTextAsync(effectivePath);
            var ruleFile = JsonSerializer.Deserialize<ConflictResolutionRuleFile>(json, ConflictRuleReadOptions);
            return new ConflictRuleLookup(ruleFile?.Rules ?? []);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "[Database - Seed] conflict-resolution rule file {File} is not valid JSON — continuing without rules",
                Path.GetFileName(effectivePath));
            return ConflictRuleLookup.Empty;
        }
    }

    /// <summary>
    /// #181: loads a source's own per-source title-alias file, referenced by the manifest entry's
    /// <c>sourceAliasFile</c> property. Missing/absent/invalid all resolve to
    /// <see cref="SourceAliasLookup.Empty"/> — same fail-open convention as <see cref="LoadConflictRulesAsync"/>.
    /// #153: prefers a registered, hash-verified override over the bundled/image copy, when one
    /// exists — see <see cref="EffectiveRuleFileResolver"/>.
    /// </summary>
    private async Task<SourceAliasLookup> LoadSourceAliasesAsync(string? sourceAliasFilePath, SeedBatchOrigin origin)
    {
        if (sourceAliasFilePath is null) return SourceAliasLookup.Empty;

        var effectivePath = await EffectiveRuleFileResolver.ResolveEffectivePathAsync(
            sourceAliasFilePath, origin, _ruleFileOverridePathResolver, _sourceFileOverrideRegistry, Logger);

        if (!File.Exists(effectivePath))
        {
            Logger.LogWarning("[Database - Seed] source-alias file {File} referenced in manifest but not found — continuing without aliases",
                Path.GetFileName(effectivePath));
            return SourceAliasLookup.Empty;
        }

        try
        {
            var json      = await File.ReadAllTextAsync(effectivePath);
            var aliasFile = JsonSerializer.Deserialize<SourceAliasRuleFile>(json, ConflictRuleReadOptions);
            return new SourceAliasLookup(aliasFile?.Aliases ?? []);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "[Database - Seed] source-alias file {File} is not valid JSON — continuing without aliases",
                Path.GetFileName(effectivePath));
            return SourceAliasLookup.Empty;
        }
    }

}
