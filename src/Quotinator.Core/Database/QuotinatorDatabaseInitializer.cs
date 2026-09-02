using Quotinator.Data.Enums;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Core.Import;
using Quotinator.Core.Logging;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Core.Services;
using Quotinator.Core.Enums;

namespace Quotinator.Core.Database;

/// <summary>
/// Quotinator-specific database initialiser. Extends <see cref="DatabaseInitializer"/> with
/// seeding logic for Quotinator domain tables (Quotes, Sources, Characters, People, Genres).
/// </summary>
/// <remarks>Initialises the instance with all dependencies required for Quotinator seeding.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
/// <param name="options">Database file paths and settings.</param>
/// <param name="migrations">Ordered, append-only list of Quotinator.Core's own schema migrations to apply. Always applied after Quotinator.Data's own migrations.</param>
/// <param name="batches">Bundled and user seed batches applied when the database is empty.</param>
/// <param name="importBatches">Repository used to record each seed/import run as an <c>Import_Batch</c> row.</param>
/// <param name="actionCoordinator">Coordinator used to stage and apply the import actions produced by seeding.</param>
/// <param name="actionService">Service used to convert raw seed/import file content into staged import actions.</param>
/// <param name="actionWriter">Writer used to persist staged import actions directly, outside the coordinator's own stage/apply flow.</param>
/// <param name="auditWriter">Writes audit entries for reseed and reset operations.</param>
/// <param name="callerContext">Provides the agent identifier for audit entries.</param>
/// <param name="logger">Logger for startup diagnostics.</param>
/// <param name="sourceCacheUpdater">Downloads and converts live-updated source files before seeding, when auto-update is enabled.</param>
/// <param name="autoUpdateSources">Whether live source auto-update runs before seeding.</param>
/// <param name="autoPurgeBundledImportActions">Whether import actions produced from bundled/system content are automatically purged on successful apply.</param>
/// <param name="autoPurgeUserImportActions">Whether import actions produced from user-supplied content are automatically purged on successful apply.</param>
/// <param name="ruleFileOverridePathResolver">Resolves the on-disk path of a conflict-resolution-rule or source-alias override file for a given seed batch.</param>
/// <param name="sourceFileOverrideRegistry">Tracks which seed source files have a curator-authored override applied, so an override is not silently reapplied or skipped.</param>
/// <param name="fileResources">Repository used to record each seeded/imported file as a <c>FileResource</c> row.</param>
/// <param name="notificationReader">Supplies the active notifications trigger 1's dedupe compares against (#304).</param>
/// <param name="notificationWriter">Writes the reseed recommendation when source content changed under an already-populated database (#304).</param>
/// <param name="notificationTextSource">Resolves the recommendation's title and body in every language, so the text is stored per language rather than in whatever culture the host defaulted to (#304, #319).</param>
/// <param name="appVersionTracker">Supplies the <c>System_AppVersion</c> row every notification this initializer writes is attributed to, recording one when none exists yet (#302).</param>
/// <param name="versionService">Names the running application and version, for the row <paramref name="appVersionTracker"/> records when none exists yet (#302).</param>
/// <param name="diskSpaceProvider">Reports real available disk space for the backup pre-flight check (#277).</param>
/// <param name="baseline">Optional consolidated DDL for Quotinator.Core's own schema, used to create a genuinely fresh database in one step instead of replaying <paramref name="migrations"/>. When omitted, a fresh database always takes the full incremental path.</param>
public sealed class QuotinatorDatabaseInitializer(
    IDbConnectionFactory factory,
    DatabaseOptions options,
    IReadOnlyList<SchemaMigration> migrations,
    IReadOnlyList<SeedBatch> batches,
    IImportBatchRepository importBatches,
    IImportActionCoordinator actionCoordinator,
    IImportActionService actionService,
    IImportActionWriter actionWriter,
    IAuditEntryWriter auditWriter,
    ICallerContext callerContext,
    ILogger<DatabaseInitializer> logger,
    ISourceCacheUpdater sourceCacheUpdater,
    bool autoUpdateSources,
    bool autoPurgeBundledImportActions,
    bool autoPurgeUserImportActions,
    IRuleFileOverridePathResolver ruleFileOverridePathResolver,
    ISourceFileOverrideRegistry sourceFileOverrideRegistry,
    IFileResourceRepository fileResources,
    INotificationReader notificationReader,
    INotificationWriter notificationWriter,
    INotificationTextSource notificationTextSource,
    IAppVersionTracker appVersionTracker,
    IVersionService versionService,
    IDiskSpaceProvider diskSpaceProvider,
    SchemaBaseline? baseline = null) : DatabaseInitializer(factory, options, migrations, auditWriter, callerContext, logger, diskSpaceProvider, baseline)
{
    private readonly IReadOnlyList<SeedBatch> _batches = batches;
    private readonly IImportBatchRepository _importBatches = importBatches;
    private readonly IImportActionCoordinator _actionCoordinator = actionCoordinator;
    private readonly IImportActionService _actionService = actionService;
    private readonly IImportActionWriter _actionWriter = actionWriter;
    private readonly ISourceCacheUpdater _sourceCacheUpdater = sourceCacheUpdater;
    private readonly bool _autoUpdateSources = autoUpdateSources;
    private readonly bool _autoPurgeBundledImportActions = autoPurgeBundledImportActions;
    private readonly bool _autoPurgeUserImportActions = autoPurgeUserImportActions;
    private readonly IRuleFileOverridePathResolver _ruleFileOverridePathResolver = ruleFileOverridePathResolver;
    private readonly ISourceFileOverrideRegistry _sourceFileOverrideRegistry = sourceFileOverrideRegistry;
    private readonly IFileResourceRepository _fileResources = fileResources;
    private readonly INotificationReader _notificationReader = notificationReader;
    private readonly INotificationWriter _notificationWriter = notificationWriter;
    private readonly INotificationTextSource _notificationTextSource = notificationTextSource;
    private readonly IAppVersionTracker _appVersionTracker = appVersionTracker;
    private readonly IVersionService _versionService = versionService;

    /// <inheritdoc/>
    protected override async Task OnInitialisedAsync(SqliteConnection connection)
    {
        SourceCacheResolution resolution = await ResolveEffectiveBatchesAsync(forceRefresh: false);

        // Read before seeding: whether this database already held content is what separates "the sources
        // changed and our copy is now stale" from "the seed just applied those very changes". Neither
        // SeedIfEmptyAsync nor its internal counterpart reports back whether it did any work, and this is
        // the same gate SeedIfEmptyInternalAsync applies to itself.
        int quotesBeforeSeeding = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);

        await SeedIfEmptyAsync(connection, resolution.EffectiveBatches);
        await ReSeedGenresIfEmptyAsync(connection, resolution.EffectiveBatches);
        await RecommendReseedIfSourceContentChangedAsync(resolution, quotesBeforeSeeding);
        await LogDatabaseStatsAsync(connection);
    }

    /// <summary>
    /// #304 trigger 1: when a source file's content actually changed upstream and this database already
    /// held content, the stored data no longer reflects the sources — so recommend a reseed rather than
    /// performing one. Reseeding automatically here is explicitly out of the question (developer
    /// direction on #304): it would discard user content on a background startup path with nothing asked.
    /// </summary>
    /// <remarks>
    /// Written from inside the import/refresh machinery rather than by a <c>Program.cs</c> producer
    /// reading the resolution afterward, per ADR 018's event-driven system content rule and the
    /// relocation principle #302/#303 follow.
    /// </remarks>
    private async Task RecommendReseedIfSourceContentChangedAsync(SourceCacheResolution resolution, int quotesBeforeSeeding)
    {
        // No network check ran, so nothing was compared and nothing can be claimed to have changed.
        if (!_autoUpdateSources) return;

        // The seed applied whatever changed on this very run.
        if (quotesBeforeSeeding == 0) return;

        List<string> changedFiles = [.. resolution.Results
            .Where(result => result.Outcome == SourceRefreshOutcome.Updated)
            .Select(result => result.Name)
            .Order(StringComparer.OrdinalIgnoreCase)];

        if (changedFiles.Count == 0) return;

        // Ordered above so the same set of files produces the same identity regardless of the order the
        // refresh happened to report them in — otherwise a parallel refresh could re-notify for a
        // condition already active.
        ReseedRecommendedMetadataDto metadata = new()
        {
            Reason = ReseedReason.ContentChanged,
            ChangedFiles = changedFiles,
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        object[] bodyArgs = [string.Join(", ", changedFiles)];

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _notificationReader, _notificationWriter, NotificationType.ActionRequired, metadata,
            body: NotificationTranslations.Original(_notificationTextSource, NotificationMessageKeys.ReseedContentChangedBody, bodyArgs),
            // Provenance is left unstated rather than guessed: the initializer runs before the app
            // version row for this boot is recorded, and inventing one would misattribute the row.
            appVersionId: null,
            title: NotificationTranslations.Original(_notificationTextSource, NotificationMessageKeys.ReseedContentChangedTitle),
            dismissTrigger: NotificationDismissTrigger.Reseed,
            translations: NotificationTranslations.Build(
                _notificationTextSource,
                NotificationMessageKeys.ReseedContentChangedTitle,
                NotificationMessageKeys.ReseedContentChangedBody,
                bodyArgs: bodyArgs));
    }

    /// <summary>
    /// #302: confirms that one file reseeded with nothing left to review, reporting what it actually
    /// did per entity type. Written from inside the seeding loop rather than reconstructed afterward
    /// from <see cref="DatabaseInitializer.LastSeedReport"/> — the clean-apply branch is the only place
    /// that knows both which file and that it left nothing pending.
    /// </summary>
    /// <param name="fileName">The seed file that applied cleanly.</param>
    /// <param name="origin">Which directory the file came from — part of what identifies the confirmation, since <paramref name="fileName"/> is a bare name that both directories can hold.</param>
    /// <param name="actions">Every import action the file produced, which is what the breakdown counts.</param>
    private async Task ConfirmFileAppliedCleanlyAsync(string fileName, SeedBatchOrigin origin, IReadOnlyList<ImportActionEntity> actions)
    {
        List<ReseedEntityCountDto> counts = [.. actions
            .GroupBy(action => action.EntityType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReseedEntityCountDto
            {
                EntityType = group.Key,
                Added      = group.Count(a => a.ActionType.Parsed == ImportActionKind.Add),
                // Matches the batch's own RecordCount rule: an action resolved as Skip or Review changed
                // nothing, so counting it as modified would report work that never happened.
                Modified   = group.Count(a => a.ActionType.Parsed == ImportActionKind.Modify
                                           && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review)),
            })
            .Where(count => count.Added > 0 || count.Modified > 0)
            .OrderBy(count => count.EntityType, StringComparer.OrdinalIgnoreCase)];

        ReseedFileAppliedMetadataDto metadata = new()
        {
            FileName     = fileName,
            Origin       = origin.ToFileResourceOrigin(),
            Counts       = counts,
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        object[] bodyArgs = [fileName, counts.Sum(c => c.Added), counts.Sum(c => c.Modified)];

        // One key per origin rather than an origin word passed as an argument: bodyArgs is a single
        // array applied to every language, so a localised "bundled"/"user" would render in one
        // language for every reader.
        string bodyKey = origin == SeedBatchOrigin.UserImports
            ? NotificationMessageKeys.ReseedFileAppliedUserBody
            : NotificationMessageKeys.ReseedFileAppliedBundledBody;

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _notificationReader, _notificationWriter, NotificationType.Success, metadata,
            body: NotificationTranslations.Original(_notificationTextSource, bodyKey, bodyArgs),
            appVersionId: await CurrentAppVersionIdAsync(),
            title: NotificationTranslations.Original(_notificationTextSource, NotificationMessageKeys.ReseedFileAppliedTitle),
            // Deliberately no dismissTrigger: POST /admin/database/reseed dismisses every Reseed-triggered
            // row once ReseedAsync returns, which would wipe out the confirmations that same call wrote.
            translations: NotificationTranslations.Build(
                _notificationTextSource,
                NotificationMessageKeys.ReseedFileAppliedTitle,
                bodyKey,
                bodyArgs: bodyArgs));
    }

    /// <summary>
    /// #303: reports that one file's import actions are waiting on a decision, so the operator learns
    /// it without knowing to check <c>/import/actions</c> or read the log.
    /// </summary>
    /// <param name="fileName">The seed file whose actions are staged.</param>
    /// <param name="origin">Which directory the file came from — part of what identifies the alert.</param>
    /// <param name="batchId">The batch those actions belong to; what the alert's own dismissal matches on.</param>
    /// <param name="actions">Every import action the file produced, which is what the breakdown counts.</param>
    private async Task AlertReviewPendingAsync(string fileName, SeedBatchOrigin origin, string batchId, IReadOnlyList<ImportActionEntity> actions)
    {
        // Only the states a human can actually act on. An action that is Decided, Applied or Discarded
        // is not waiting for anyone, and counting it would overstate the work.
        ImportActionStatus[] reviewable =
        [
            ImportActionStatus.Pending,
            ImportActionStatus.Blocked,
            ImportActionStatus.Stale,
        ];

        List<ImportReviewCountDto> counts = [.. actions
            .Where(action => action.Status.Parsed is ImportActionStatus parsed && reviewable.Contains(parsed))
            .GroupBy(action => action.Status.Parsed!.Value)
            .Select(group => new ImportReviewCountDto { Status = group.Key.ToString(), Count = group.Count() })
            .Where(count => count.Count > 0)
            .OrderBy(count => count.Status, StringComparer.OrdinalIgnoreCase)];

        if (counts.Count == 0) return;

        ImportReviewPendingMetadataDto metadata = new()
        {
            FileName     = fileName,
            Origin       = origin.ToFileResourceOrigin(),
            BatchId      = batchId,
            Counts       = counts,
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        object[] bodyArgs = [fileName, counts.Sum(c => c.Count)];

        string bodyKey = origin == SeedBatchOrigin.UserImports
            ? NotificationMessageKeys.ImportReviewPendingUserBody
            : NotificationMessageKeys.ImportReviewPendingBundledBody;

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _notificationReader, _notificationWriter, NotificationType.ActionRequired, metadata,
            body: NotificationTranslations.Original(_notificationTextSource, bodyKey, bodyArgs),
            appVersionId: await CurrentAppVersionIdAsync(),
            title: NotificationTranslations.Original(_notificationTextSource, NotificationMessageKeys.ImportReviewPendingTitle),
            dismissTrigger: NotificationDismissTrigger.ImportReviewResolved,
            translations: NotificationTranslations.Build(
                _notificationTextSource,
                NotificationMessageKeys.ImportReviewPendingTitle,
                bodyKey,
                bodyArgs: bodyArgs));
    }

    /// <summary>
    /// The <c>System_AppVersion</c> row a notification written from here is attributed to, recording
    /// the running version first when nothing has been recorded yet (#302).
    /// <para>
    /// Safe to record from the reseed path specifically, and only there: <c>Program.cs</c> reads the
    /// *previous* version after <c>InitialiseAsync</c> and strictly before its own
    /// <c>RecordCurrentAsync</c>, because that read is #81's what's-new catch-up lower bound and
    /// recording first would overwrite it. A reseed happens long after that sequence has finished.
    /// </para>
    /// </summary>
    private async Task<Guid> CurrentAppVersionIdAsync()
    {
        AppVersionRecord? lastActive = await _appVersionTracker.GetLastActiveAsync();
        if (lastActive is not null) return lastActive.Id;

        AppVersionRecord recorded = await _appVersionTracker.RecordCurrentAsync(
            _versionService.Application, _versionService.Version);

        return recorded.Id;
    }

    /// <inheritdoc/>
    /// <remarks>Mirrors the two count-gates <see cref="OnInitialisedAsync"/> itself runs (#277): <see cref="SeedIfEmptyInternalAsync"/> would do real work whenever Quotes is empty, and <see cref="ReSeedGenresIfEmptyAsync"/> would do real work whenever Genres is empty but Quotes is not.</remarks>
    protected override async Task<bool> HasPendingContentSeedAsync(SqliteConnection connection)
    {
        int quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (quoteCount == 0) return true;

        int genreCount = await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll);
        return genreCount == 0;
    }

    /// <inheritdoc/>
    protected override async Task OnReseedAsync(SqliteConnection connection, bool forceSourceRefresh)
    {
        IReadOnlyList<SeedBatch> effectiveBatches = (await ResolveEffectiveBatchesAsync(forceSourceRefresh)).EffectiveBatches;
        int totalFiles = effectiveBatches.Sum(b => b.Files.Count);
        Logger.LogReseedRequested(totalFiles);

        await SharedSeedLock.WaitAsync();
        try
        {
            // #372: no deletion, and no emptiness check. A reseed imports the designated files and
            // nothing else — deciding what data survives is a second, independent job, and CLAUDE.md's
            // endpoint side-effect policy gives it to Reset, which rebuilds from the baseline. Starting
            // from scratch is Reset then Reseed, two explicit actions. The gate belongs to cold start
            // alone: here it would suppress the report the operator ran the reseed to get.
            await ImportDesignatedFilesAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reseed,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Seed] reseed complete");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// #156: Reset's one job is rebuilding the schema to an empty baseline (plus system content via
    /// <see cref="DatabaseInitializer.SeedSystemContentAsync"/>) — it no longer reimports bundled or
    /// user quote content, matching the Single Responsibility endpoint-side-effect policy (a caller
    /// resetting to start fresh must not be forced to re-accept optional bundled content every time).
    /// <see cref="ResolveEffectiveBatchesAsync"/> is still called so <paramref name="forceSourceRefresh"/>
    /// keeps its existing effect of refreshing the on-disk source cache — a disk-level concern
    /// independent of database content, outside that policy's scope — but its returned batches are
    /// discarded here, never imported.
    /// </remarks>
    protected override async Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
    {
        await ResolveEffectiveBatchesAsync(forceSourceRefresh);
        Logger.LogInformation("[Database - Init] reset requested — rebuilding schema from baseline...");

        await SharedSeedLock.WaitAsync();
        try
        {
            await DropAndRebuildAsync(connection, preserveSchemaVersion);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        // Reset performs no seeding — LastSeedReport must not keep echoing whatever the last real
        // seed/reseed reported, which would misleadingly suggest this Reset call imported something.
        LastSeedReport = [];

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reset,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Init] reset complete");
    }

    // #372: `BatchIdsAsync` and `DismissAlertsForRemovedBatchesAsync` lived here until a reseed stopped
    // removing batches. Both existed only to mark the alerts of just-truncated batches Obsolete, and
    // with nothing truncated there is nothing to mark. Removed rather than left uncalled.
    //
    // `NotificationDismissReason.Obsolete` itself stays, and that is a separate decision from removing
    // its producer. Databases upgraded from an earlier build hold rows already carrying it, so the enum
    // member, its CHECK constraint and `NotificationTable`'s rendering of it all have to keep working —
    // deleting the member would need a migration and would break the reading of history that already
    // exists. It currently has no producer, which is a fact worth stating rather than a gap to fill;
    // #369, which deals with review rows whose batch is genuinely gone, is where one may reappear.

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
        SourceCacheResolution resolution       = await ResolveEffectiveBatchesAsync(forceRefresh: false, allowNetworkOverride: false);
        IReadOnlyList<SeedBatch> effectiveBatches = resolution.EffectiveBatches;
        Dictionary<string, SourceRefreshResult> resultsByName    = resolution.Results.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        List<Data.Import.SeedFilePreview> filePreviews = [];
        List<FileImportReport> reports      = [];

        using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync();

        foreach (SeedBatch batch in effectiveBatches)
        {
            foreach (SeedFile seedFile in batch.Files)
            {
                string fileName    = Path.GetFileName(seedFile.FilePath);
                (ParsedSourceFileDto? parsed, SeedFileIssue? issue) = LoadSourceFileAsync(seedFile.FilePath);
                IReadOnlyList<SourceQuoteDto> quotes      = parsed.Quotes;
                SourceRefreshResult? refreshResult = resultsByName.GetValueOrDefault(fileName);
                ManifestPolicy filePolicy  = ManifestPolicy.Resolve(seedFile.Policy, batch.Policy);
                filePreviews.Add(new Quotinator.Data.Import.SeedFilePreview(fileName, quotes.Count, refreshResult?.Outcome, refreshResult?.LastRefreshedAtUtc, issue));

                ConflictRuleLookup conflictRules = await LoadConflictRulesAsync(seedFile.RuleFilePath, batch.Origin);
                SourceAliasLookup sourceAliases = await LoadSourceAliasesAsync(seedFile.SourceAliasFilePath, batch.Origin);

                IReadOnlyList<ImportActionEntity> actions = await ImportActionPlanner.PlanAsync(connection, quotes, Guid.NewGuid(), filePolicy.ForQuotes, transaction: null,
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
        bool allowNetwork = allowNetworkOverride ?? _autoUpdateSources;
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

    /// <summary>The shared seeding body behind both the cold-start path and an explicit reseed.</summary>
    /// <param name="connection">Open connection to the database being seeded.</param>
    /// <param name="effectiveBatches">The seed batches to apply, already resolved against the source cache.</param>
    /// <remarks>
    /// <para>
    /// Cold start's own entry point, and the only one that asks whether there is anything to do. An
    /// explicit reseed calls <see cref="ImportDesignatedFilesAsync"/> directly and never consults this
    /// gate — see #372: on that path the check is not a safeguard, it suppresses the report the
    /// operator ran the reseed to get.
    /// </para>
    /// <para>
    /// **"Empty" means no seedable content, not that no table has rows.** The check counts quotes on
    /// purpose. Broadening it to "any <c>Quotinator_</c> table has rows" would break the moment a
    /// baseline-seeded reference table exists — genres become one in #310/#268 — because a brand-new
    /// database would then read as already seeded and the seed would be skipped in silence. A
    /// user-updatable table is content however generic it looks: <c>Universe</c> is the near-miss.
    /// </para>
    /// </remarks>
    private async Task SeedIfEmptyInternalAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        int count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (count > 0) return;

        await ImportDesignatedFilesAsync(connection, effectiveBatches);
    }

    /// <summary>
    /// Imports every designated file from both origins, unconditionally. This is reseed's whole job
    /// (#372), and cold start's job once <see cref="SeedIfEmptyInternalAsync"/> has established there
    /// is content to seed.
    /// </summary>
    /// <param name="connection">Open connection to the database being imported into.</param>
    /// <param name="effectiveBatches">The seed batches to apply, already resolved against the source cache.</param>
    private async Task ImportDesignatedFilesAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] no source files configured — database will be empty");
            return;
        }

        LastSeedReport = [];

        List<FileImportReport> reports     = [];
        List<string> stagedFiles = [];

        foreach (SeedBatch batch in effectiveBatches)
        {
            foreach (SeedFile seedFile in batch.Files)
            {
                string fileName        = Path.GetFileName(seedFile.FilePath);
                (ParsedSourceFileDto? parsed, SeedFileIssue? _) = LoadSourceFileAsync(seedFile.FilePath);
                IReadOnlyList<SourceQuoteDto> quotes          = parsed.Quotes;
                ManifestPolicy filePolicy      = ManifestPolicy.Resolve(seedFile.Policy, batch.Policy);
                DuplicateResolutionPolicy policy          = filePolicy.ForQuotes;
                ConflictRuleLookup conflictRules   = await LoadConflictRulesAsync(seedFile.RuleFilePath, batch.Origin);
                SourceAliasLookup sourceAliases   = await LoadSourceAliasesAsync(seedFile.SourceAliasFilePath, batch.Origin);

                Logger.LogImportingQuotes(quotes.Count, fileName, batch.Label);

                ImportBatchEntity importBatch = await CreateImportBatchAsync(batch, seedFile, filePolicy);
                string batchIdStr  = importBatch.Id.ToCanonicalId();

                IReadOnlyList<ImportActionEntity> actions;
                using (SqliteTransaction tx = connection.BeginTransaction())
                {
                    actions = await ImportActionPlanner.PlanAsync(connection, quotes, importBatch.Id, policy, tx,
                        parsed.Sources, parsed.StageDirections, parsed.SoundCues, parsed.Conversations, parsed.People,
                        parsed.Series, parsed.Universe, parsed.Characters, conflictRules, sourceAliases);
                    await _actionCoordinator.StageAsync(actions, connection, tx);
                    tx.Commit();
                }

                FileImportReport report = ImportActionReportBuilder.Build(fileName, actions);
                reports.Add(report);
                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogFileReport(fileName, FormatReport(report));

                ImportActionBatchStatusResponse? applyResult = await _actionService.ApplyBatchAsync(batchIdStr, InitiatorType.Seed);
                if (applyResult is null)
                {
                    int imported = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Add);
                    int updated  = actions.Count(a => a.EntityType == ImportActionEntityTypes.Quote && a.ActionType.Parsed == ImportActionKind.Modify
                                                   && a.AppliedPolicy.Parsed is not (DuplicateResolutionPolicy.Skip or DuplicateResolutionPolicy.Review));

                    importBatch.Status      = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Applied.ToString(), ImportBatchStatus.Applied);
                    importBatch.AppliedAt   = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
                    importBatch.RecordCount = imported + updated;
                    await _importBatches.UpdateAsync(importBatch);

                    await AuditWriter.WriteAsync(new AuditEntryEntity
                    {
                        TableName   = "Quotinator_Quote",
                        RecordId    = batchIdStr,
                        Operation   = AuditOperation.BulkInsert,
                        Agent       = CallerContext.Agent,
                        PerformedAt = DateTime.UtcNow,
                    }, connection);

                    // #249: the batch reached zero pending actions — its Import_Action rows have
                    // served their purpose (resolving this import). Purge them when the relevant
                    // per-origin setting allows it; a temporary developer investigation of a specific
                    // source flips that one setting off first, so this stays a no-op for it.
                    bool autoPurge = batch.Origin == SeedBatchOrigin.UserImports
                        ? _autoPurgeUserImportActions
                        : _autoPurgeBundledImportActions;
                    if (autoPurge)
                    {
                        await _actionWriter.DeleteForBatchAsync(batchIdStr, connection);
                        await AuditWriter.WriteAsync(new AuditEntryEntity
                        {
                            TableName   = "Import_Action",
                            RecordId    = batchIdStr,
                            Operation   = AuditOperation.Purge,
                            Agent       = CallerContext.Agent,
                            PerformedAt = DateTime.UtcNow,
                        }, connection);
                    }

                    // Ungated on which caller began the seed. It was reseed-only until #302 was reopened
                    // (2026-09-02): the same four files applying identically produced four confirmations
                    // from the UI and none at startup, and the suppression rested on the startup modal's
                    // aggregate summary already covering a first install — which carries no file names,
                    // no origin, and no added-versus-updated split.
                    await ConfirmFileAppliedCleanlyAsync(fileName, batch.Origin, actions);
                }
                else
                {
                    stagedFiles.Add(fileName);
                    Logger.LogFileStagedAwaitingReview(fileName, batchIdStr, applyResult.PendingActionIds.Count);

                    // Ungated for the same reason as the confirmation above (#303): a first install whose
                    // bundled content staged conflicts genuinely has something to review, and the startup
                    // modal reports aggregate counts rather than that actions are waiting.
                    await AlertReviewPendingAsync(fileName, batch.Origin, batchIdStr, actions);
                }
            }
        }

        LastSeedReport = reports;

        Logger.LogSeedingComplete(reports.Count);

        if (stagedFiles.Count > 0 && Logger.IsEnabled(LogLevel.Information))
            Logger.LogFilesStagedAwaitingReview(stagedFiles.Count, string.Join(", ", stagedFiles));
    }

    private async Task ReSeedGenresIfEmptyAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        int genreCount = await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll);
        if (genreCount > 0) return;

        int quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (quoteCount == 0) return;

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] cannot re-seed genres — no source files configured");
            return;
        }

        Logger.LogInformation("[Database - Seed] re-seeding genres from source files...");

        string now      = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        int inserted = 0;

        foreach (SeedBatch batch in effectiveBatches)
        {
            foreach (SeedFile seedFile in batch.Files)
            {
                (List<SourceQuoteDto>? quotes, SeedFileIssue? _) = LoadQuotesFromFile(seedFile.FilePath);
                foreach (SourceQuoteDto q in quotes)
                {
                    foreach (string genre in q.Genres)
                    {
                        if (QuoteSeedWriter.TryNormaliseGenre(genre, out Genre g))
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

        Logger.LogGenreReseedComplete(inserted);
    }

    private async Task LogDatabaseStatsAsync(SqliteConnection connection)
    {
        QuoteCount          = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountActive);
        SourceCount         = await connection.ExecuteScalarAsync<int>(Sql.Sources.CountActive);
        CharacterCount      = await connection.ExecuteScalarAsync<int>(Sql.Characters.CountActive);
        PeopleCount         = await connection.ExecuteScalarAsync<int>(Sql.People.CountActive);
        SeriesCount         = await connection.ExecuteScalarAsync<int>(Sql.Series.CountActive);
        UniverseCount       = await connection.ExecuteScalarAsync<int>(Sql.Universe.CountActive);
        StageDirectionCount = await connection.ExecuteScalarAsync<int>(Sql.StageDirections.CountActive);
        SoundCueCount       = await connection.ExecuteScalarAsync<int>(Sql.SoundCues.CountActive);
        ConversationCount   = await connection.ExecuteScalarAsync<int>(Sql.Conversations.CountActive);

        Logger.LogDatabaseStats(
            QuoteCount, SourceCount, CharacterCount, PeopleCount,
            SeriesCount, UniverseCount, StageDirectionCount, SoundCueCount, ConversationCount);
    }

    private async Task<ImportBatchEntity> CreateImportBatchAsync(SeedBatch seedBatch, SeedFile seedFile, ManifestPolicy filePolicy)
    {
        ImportBatchType type   = DetermineType(seedBatch.Origin);
        DuplicateResolutionPolicy policy = filePolicy.ForQuotes;
        ImportBatchEntity batch = new ImportBatchEntity
        {
            Name           = Path.GetFileName(seedFile.FilePath),
            Type           = new SafeValue<ImportBatchType?>(type.ToString(), type),
            Url            = seedFile.Url,
            ImportedAt     = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(policy.ToString(), policy),
            Status         = new SafeValue<ImportBatchStatus?>(ImportBatchStatus.Staged.ToString(), ImportBatchStatus.Staged),
        };
        await _importBatches.InsertAsync(batch);

        // #251 — capture this file's own content for provenance. Skipped only when the file is
        // genuinely missing (already surfaced separately via LoadSourceFileAsync's own SeedFileIssue
        // path elsewhere in this seed pass) — a real failure during the write itself is not swallowed
        // here; it propagates and is caught by the outer seeding backup/restore/rethrow net #254
        // already wraps this whole hook in, the same as any other seeding failure.
        if (File.Exists(seedFile.FilePath))
        {
            bool isUserImports = seedBatch.Origin == SeedBatchOrigin.UserImports;
            FileResourceOrigin fileResourceOrigin   = seedBatch.Origin.ToFileResourceOrigin();
            // "sources"/"imports" per #252 — the only two local directories any write path has ever
            // captured from; a future consumer of System/User origin unrelated to quote sources
            // registers its own key without stretching what these two mean.
            string homeDirectoryKey     = isUserImports ? "imports" : "sources";
            string content = await File.ReadAllTextAsync(seedFile.FilePath);
            // OriginalFolderPath is null here — today's directory scan is flat (no subfolders under
            // data/sources/ or {dataDir}/imports/), confirmed via ManifestSeedPlanner's own
            // non-recursive Directory.GetFiles call, so there is no folder segment to record yet.
            await _fileResources.WriteAsync(
                Path.GetFileName(seedFile.FilePath), originalFolderPath: null, fileResourceOrigin, content, batch.Id,
                seedFile.Converter, seedFile.ConverterOptions?.GetRawText(), homeDirectoryKey);

            // Also capture the manifest.json that drove this seed pass, linked to this same batch —
            // content-hash dedup means only a new Import_FileResourceBatch link row is added for every
            // file in the same directory after the first, correctly reflecting that one manifest.json
            // version governed every batch created from it this session. Uses seedBatch.SourceDirectory
            // rather than seedFile.FilePath's own directory — ISourceCacheUpdater rewrites a downloaded
            // file's FilePath to a separate cache directory that never contains manifest.json, which
            // would silently miss the link for every downloaded source otherwise (found live in a T2 pass).
            string manifestDir  = seedBatch.SourceDirectory ?? Path.GetDirectoryName(seedFile.FilePath)!;
            string manifestPath = Path.Combine(manifestDir, ManifestSeedPlanner.ManifestFileName);
            if (File.Exists(manifestPath))
            {
                string manifestContent = await File.ReadAllTextAsync(manifestPath);
                await _fileResources.WriteAsync(
                    ManifestSeedPlanner.ManifestFileName, originalFolderPath: null, fileResourceOrigin, manifestContent, batch.Id,
                    homeDirectoryKey: homeDirectoryKey);
            }
        }

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

    private (List<SourceQuoteDto> Quotes, SeedFileIssue? Issue) LoadQuotesFromFile(string filePath)
    {
        (ParsedSourceFileDto? parsed, SeedFileIssue? issue) = LoadSourceFileAsync(filePath);
        return (parsed.Quotes.ToList(), issue);
    }

    /// <summary>
    /// #68: full extended parse (quotes plus stageDirections/soundCues/conversations), used by
    /// <see cref="SeedIfEmptyInternalAsync"/> to plan the three new entity types alongside quotes.
    /// <see cref="LoadQuotesFromFile"/> wraps this for the two call sites that only need the quotes —
    /// one parsing implementation, not two.
    /// </summary>
    private (ParsedSourceFileDto Parsed, SeedFileIssue? Issue) LoadSourceFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return (new ParsedSourceFileDto { Quotes = [] }, SeedFileIssue.Missing);

        string json = File.ReadAllText(filePath);
        if (SourceQuoteFileReader.TryParseExtended(json, out ParsedSourceFileDto? parsed)) return (parsed!, null);

        Logger.LogWarning("[Database - Seed] {File} is empty or not valid JSON — skipping", Path.GetFileName(filePath));
        return (new ParsedSourceFileDto { Quotes = [] }, SeedFileIssue.InvalidJson);
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

        string effectivePath = await EffectiveRuleFileResolver.ResolveEffectivePathAsync(
            ruleFilePath, origin, _ruleFileOverridePathResolver, _sourceFileOverrideRegistry, Logger);

        if (!File.Exists(effectivePath))
        {
            Logger.LogWarning("[Database - Seed] conflict-resolution rule file {File} referenced in manifest but not found — continuing without rules",
                Path.GetFileName(effectivePath));
            return ConflictRuleLookup.Empty;
        }

        try
        {
            string json     = await File.ReadAllTextAsync(effectivePath);
            ConflictResolutionRuleFileDto? ruleFile = JsonSerializer.Deserialize<ConflictResolutionRuleFileDto>(json, ConflictRuleReadOptions);
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

        string effectivePath = await EffectiveRuleFileResolver.ResolveEffectivePathAsync(
            sourceAliasFilePath, origin, _ruleFileOverridePathResolver, _sourceFileOverrideRegistry, Logger);

        if (!File.Exists(effectivePath))
        {
            Logger.LogWarning("[Database - Seed] source-alias file {File} referenced in manifest but not found — continuing without aliases",
                Path.GetFileName(effectivePath));
            return SourceAliasLookup.Empty;
        }

        try
        {
            string json      = await File.ReadAllTextAsync(effectivePath);
            SourceAliasRuleFileDto? aliasFile = JsonSerializer.Deserialize<SourceAliasRuleFileDto>(json, ConflictRuleReadOptions);
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
