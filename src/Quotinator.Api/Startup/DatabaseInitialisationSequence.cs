using Quotinator.Api.Logging;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Startup;

/// <summary>
/// What startup does with the main database, as one sequence that startup and
/// <c>POST /api/v1/admin/database/migrate</c> both run (#490 step 17): <see cref="InitialiseAsync"/> initialises it and
/// records whether the application is healthy, and <see cref="CompleteAsync"/> does what a healthy start does
/// afterwards. Moved out of <c>Program.cs</c> so a migration the operator resumes leaves exactly the state a healthy
/// restart would, rather than a second, drifting copy of it.
/// </summary>
/// <param name="dbInitializer">The main database's initializer.</param>
/// <param name="dbHealth">Whether the application is healthy, and why not.</param>
/// <param name="appVersionTracker">Records the version this instance runs, and reads the last one.</param>
/// <param name="versionService">The running application and version.</param>
/// <param name="notificationReader">Reads notifications, for deduplication.</param>
/// <param name="notificationWriter">Writes notifications.</param>
/// <param name="localizer">The notification texts in every language.</param>
/// <param name="changelogReader">The changelog the what's-new notification is built from.</param>
/// <param name="conditionChecks">Re-checks every notification whose condition can change while the application runs.</param>
/// <param name="backgroundWork">Startup work that runs in the background, and is waited for before the gate opens.</param>
/// <param name="logger">Logs what degraded the application, and each non-fatal failure.</param>
internal sealed class DatabaseInitialisationSequence(
    IDatabaseInitializer dbInitializer,
    DatabaseHealthState dbHealth,
    IAppVersionTracker appVersionTracker,
    IVersionService versionService,
    INotificationReader notificationReader,
    INotificationWriter notificationWriter,
    IApiLocalizer localizer,
    IChangelogReader changelogReader,
    NotificationConditionChecks conditionChecks,
    StartupBackgroundWork backgroundWork,
    ILogger<DatabaseInitialisationSequence> logger)
{
    /// <summary>
    /// Initialises the main database and records on <see cref="DatabaseHealthState"/> whether the application is
    /// healthy, and why not. Returns how initialisation ended, or <see langword="null"/> when it threw, which is
    /// recorded as a degraded reason too.
    /// </summary>
    /// <param name="allowNoBackup">
    /// The operator's confirmation that a migration may run without its backup when none can be taken (#490 step 17).
    /// Startup never passes it.
    /// </param>
    public async Task<DatabaseOperationResult?> InitialiseAsync(bool allowNoBackup = false)
    {
        // A database initialisation failure must never crash the whole process outright: that would
        // also make POST /api/v1/admin/database/reset unreachable, the one endpoint actually capable of
        // resolving the underlying schema/version mismatch (found live, 2026-08-02: exiting on this
        // exception meant the operator's own documented remedy could never be reached). Catching it here
        // logs one clear, actionable message, then records the failure on DatabaseHealthState and lets
        // startup continue: the app still binds and stays reachable for health/version/admin traffic, while
        // DatabaseHealthGateMiddleware degrades every other request to a clear 503 instead of letting it
        // throw the same raw exception per-request.
        try
        {
            DatabaseOperationResult initialisation = await dbInitializer.InitialiseAsync(allowNoBackup);

            // #490: the check startup ran is kept as a result, whatever it found, so the layer a degraded start
            // failed at, and a finding that leaves startup healthy, can be read rather than only logged.
            if (initialisation.Check is { } startupCheck)
                dbHealth.RecordStartupCheck(startupCheck);

            // #490: one mapping from how initialisation ended to the reason startup degrades with, holding the
            // #348, #350 and #305 cases in their order of precedence. Degrading raises no notification: writing
            // that row into a schema the build has not migrated, or knows to differ, is the unprotected write
            // #348 refuses.
            string? degradedReason = StartupDegradationReasons.For(
                initialisation, dbInitializer.SchemaVersionOvershootDetected,
                dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion,
                dbInitializer.LastIntegrityResult);

            if (degradedReason is null)
            {
                // Only a resumed migration has a degradation of its own to clear. At startup the application may
                // already be degraded for a reason found before the database, an unwritable key ring (#326), and
                // a clean initialisation does not make that reason untrue.
                if (dbHealth.MigrationAwaitsConfirmation)
                    dbHealth.MarkHealthy();
                return initialisation;
            }

            logger.LogStartupDegraded(degradedReason);
            // #490 step 17: the one degradation the migrate endpoint can resume.
            Degrade(degradedReason, migrationRefused: initialisation is { RefusedStep: BackupGuardedStep.Migration, BackupObstacle: not null });
            return initialisation;
        }
        // #326: an unwritable data directory is a different fault with a different remedy, and the generic
        // reason below actively misdirects for it: it tells the operator to run a database Reset, which also
        // writes and therefore cannot work here. Measured by scripts/testing/sqlite-storage-probe.csx: an unwritable
        // directory surfaces SQLITE_CANTOPEN (14), a writable directory holding a read-only file surfaces
        // SQLITE_READONLY (8). Matched on SqliteErrorCode, never SqliteExtendedErrorCode: the extended code
        // varies by cause (526 CANTOPEN_ISDIR for a directory at the database path) while the primary code
        // does not.
        catch (Exception ex) when (DataDirectoryFault.IsNotWritable(ex))
        {
            logger.LogStartupDatabaseInitFailed(ex, StartupDegradationReasons.DataDirectoryNotWritable);
            Degrade(StartupDegradationReasons.DataDirectoryNotWritable, migrationRefused: false);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogStartupDatabaseInitFailed(ex, StartupDegradationReasons.InitialisationFailed);
            Degrade(StartupDegradationReasons.InitialisationFailed, migrationRefused: false);
            return null;
        }
    }

    /// <summary>
    /// What a healthy start does once the database is initialised: reads the last active version, records the
    /// current one, writes the notifications startup owes, and re-checks the notifications whose condition can
    /// change. Each step is gated on health as it was in <c>Program.cs</c>, and the background work is waited for.
    /// </summary>
    public async Task CompleteAsync()
    {
        // #81: the version this app instance was running as of its *previous* healthy startup, the lower
        // bound of the what's-new catch-up range. Read here rather than before migrations, and the ordering is
        // load-bearing, not incidental.
        //
        // It originally ran before InitialiseAsync, on the reasoning that a missing System_AppVersion table
        // (a fresh install, or the first boot after #81 introduced it) reads as null: "nothing to catch up
        // on". #312 broke that: this query now selects Application and orders by SequenceNumber, columns
        // migrations 6 and 7 add, so a database where the table already exists but those columns do not (any
        // database at data v4 or v5, i.e. one that ran a build between #81 and #312) threw
        // `no such column: Application` straight past the missing-table catch and killed startup. Found live
        // in T1 on exactly such a database; T2 could not have caught it, since it upgraded from v1.8.3, where
        // the table does not exist at all and the catch does apply.
        //
        // Reading after migrations is not a workaround, it is the correct order: migrations 6 and 7 only add
        // columns and backfill SequenceNumber; they never touch a recorded Version, so "which version ran
        // last" is identical either side of them, while only the later position is guaranteed to have a schema
        // matching the query. Widening the catch to swallow `no such column` was rejected: it would leave the
        // same trap armed for the next column added to this query, and CLAUDE.md's "no exception-based
        // recovery" rule is precisely about not inferring schema state from thrown exceptions.
        //
        // Still strictly before RecordCurrentAsync below, which is what would overwrite the answer.
        //
        // #326: gated and guarded, matching RecordCurrentAsync immediately below. This was the one statement
        // in the whole post-StartAsync sequence that could still terminate the process: AppVersionTracker
        // catches only "no such table: System_AppVersion", so a data directory that cannot be written threw
        // SQLITE_CANTOPEN straight past it and killed startup before StartupPhaseState.MarkComplete(),
        // taking the degraded UI, /health, the OpenAPI surface and POST /admin/database/reset down with it.
        // A failure here leaves lastActiveVersion null, which the #81 producer below already treats as
        // "nothing to catch up on", and that producer is itself gated on dbHealth.IsHealthy anyway.
        AppVersionRecord? lastActive = null;
        if (dbHealth.IsHealthy)
        {
            try
            {
                lastActive = await appVersionTracker.GetLastActiveAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to read the last active app version; non-fatal, startup continues. " +
                    "The what's-new notification has no catch-up range this startup.");
            }
        }

        string? lastActiveVersion = lastActive?.Version;

        // #81: System_AppVersion is meant to always carry the current version once startup is healthy:
        // the same "structurally required, not the caller's optional content" reasoning CLAUDE.md's endpoint
        // side-effect policy applies elsewhere, just applied to a startup step instead of an endpoint. Fast,
        // synchronous, single-row write against the already-open main database (matching #279's/#289's own
        // synchronous read+write producers): safe to await inline, unlike #309's changelog database or the
        // slower catch-up logic below.
        //
        // #312 moved this ahead of the notification producers below, which it used to follow. Every producer
        // now stamps AppVersionId on what it writes, and a foreign key cannot reference a row that does not
        // exist yet. Ordering is safe because lastActiveVersion was captured further up, before migrations
        // ran; recording the current version here cannot disturb what the catch-up range already read.
        AppVersionRecord? currentVersion = null;
        if (dbHealth.IsHealthy)
        {
            try
            {
                currentVersion = await appVersionTracker.RecordCurrentAsync(versionService.Application, versionService.Version);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to record the current app version; non-fatal, startup continues. " +
                    "The what's-new notification's catch-up range may be inaccurate on the next restart, and " +
                    "notifications written during this startup carry no app-version provenance.");
            }
        }

        // #279: first concrete producer for #278's notification mechanism: announces the two breaking
        // operationId renames this release ships. Idempotent across restarts (NotificationSeeding compares
        // this payload structurally against notification history), so this call is safe to leave in place
        // indefinitely rather than needing to be removed after the first deploy. Deliberately outside the
        // critical DB-init try/catch above and in its own non-fatal guard: a failure here (e.g. a test's
        // NoOpDatabaseInitializer, which never creates System_Notification) must never mark the whole app
        // unhealthy; writing an announcement notification is inherently non-critical, unlike schema init itself.
        if (dbHealth.IsHealthy)
        {
            try
            {
                // Hoisted so the payload's content hash covers exactly the text that gets written. Hashing a
                // second copy of the same words would be a copy that can drift.
                const string announcementBody = OperationIdRenameAnnouncement.Body;

                await NotificationSeeding.SeedOnceAsync(
                    notificationReader,
                    notificationWriter,
                    NotificationType.Warning,
                    new AnnouncementMetadataDto
                    {
                        Announcement = "GetAllImportBatches",
                        // The release this announcement is about (v1.8.3 shipped the renames), not the version
                        // running now, which the row's own AppVersionId records. The two coincide only until
                        // the next release.
                        ReleaseState = NotificationReleaseState.Released,
                        Version      = "1.8.3",
                        ContentHash  = NotificationContentHash.Of(announcementBody),
                    },
                    title: "Two API operation IDs were renamed",
                    body: announcementBody,
                    appVersionId: currentVersion?.Id,
                    // #319: every language at once. The English above stays the notification's own text (the
                    // content hash is taken over it, and the read path falls back to it), so only the other
                    // languages become translation rows.
                    translations: NotificationTranslations.Build(
                        localizer,
                        ApiMessages.NotificationOperationIdRenameTitle,
                        ApiMessages.NotificationOperationIdRenameBody));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to seed the #279 operation-id-rename notification; non-fatal, startup " +
                    "continues. This does not mean the database is broken or corrupted: it means a table the current " +
                    "schema version implies should exist (e.g. System_Notification) is actually missing on disk, a " +
                    "mismatch normal operation shouldn't produce.");
            }
        }

        // #289: second producer for #278's notification mechanism: announces a schema-version overshoot, a
        // recorded version exceeding this build's own known migration count. The dedupe key includes the
        // actual detected versions, not a fixed string like #279's: repeats of the same already-notified
        // overshoot state (e.g. the operator hasn't reset yet and the app just restarted) stay deduped, but a
        // genuinely different future overshoot still gets its own notification. ActionRequired + DatabaseReset
        // dismiss trigger: POST /admin/database/reset already calls
        // DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset) (see AdminEndpoints.cs), so this
        // clears itself automatically once the operator resets.
        //
        // #350: deliberately NOT gated on dbHealth.IsHealthy, unlike every other producer below. An overshoot
        // now degrades the database, so that gate would suppress the one notification that explains why the
        // application is degraded. The write targets a notifications table whose shape this build cannot
        // vouch for, which is what the try/catch below is for: a failure here is logged and startup carries
        // on. This is the opposite of #348's refused-migration decision not to notify at all, and the
        // difference is the direction of the mismatch: a refusal leaves the schema behind this build, so the
        // write is known to target an unmigrated table, while an overshoot leaves it ahead, where the table is
        // very likely intact and this row is the only record of the fault that outlives the restart.
        if (dbInitializer.SchemaVersionOvershootDetected)
        {
            try
            {
                await NotificationSeeding.SeedOnceAsync(
                    notificationReader,
                    notificationWriter,
                    NotificationType.ActionRequired,
                    new SchemaVersionOvershootMetadataDto
                    {
                        DataSchemaVersion = dbInitializer.DataSchemaVersion,
                        AppSchemaVersion  = dbInitializer.SchemaVersion,
                        // Not about a release at all: this describes the database's own recorded state, which
                        // no version number characterises. Said outright rather than borrowing the running
                        // version, which would also make the same unresolved overshoot re-announce itself on
                        // every upgrade.
                        ReleaseState = NotificationReleaseState.NotApplicable,
                    },
                    // #319: both recorded versions are substituted into each language's own template, so the
                    // numbers are not embedded in prose written once in English. The structured values stay in
                    // the metadata payload above; this is the same pair, rendered.
                    title: NotificationTranslations.Original(
                               localizer, ApiMessages.NotificationSchemaOvershootTitle),
                    body: NotificationTranslations.Original(
                               localizer, ApiMessages.NotificationSchemaOvershootBody,
                               dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion),
                    dismissTrigger: NotificationDismissTrigger.DatabaseReset,
                    appVersionId: currentVersion?.Id,
                    translations: NotificationTranslations.Build(
                        localizer,
                        ApiMessages.NotificationSchemaOvershootTitle,
                        ApiMessages.NotificationSchemaOvershootBody,
                        bodyArgs: [dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion]));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to seed the #289 schema-version-overshoot notification; non-fatal, startup continues.");
            }
        }

        // #371: a database created from scratch says so. Written here rather than from inside
        // Quotinator.Data's migration code, following #289's precedent immediately above: System_Notification
        // may not exist while migrations run, and a table-rebuild migration may be mid-flight. SeedOnceAsync
        // rather than its active-only sibling, because this is an event that happened once, not a condition
        // that can recur: no dismiss trigger, since nothing resolves a fact, and no expiry, matching #312's
        // opt-in default and #413's finding that a default expiry outlives its own correctness. Nothing is
        // written when the database already existed, which is requirement 5.
        if (dbHealth.IsHealthy && dbInitializer.DatabaseWasCreated)
        {
            try
            {
                await NotificationSeeding.SeedOnceAsync(
                    notificationReader,
                    notificationWriter,
                    NotificationType.Information,
                    new DatabaseCreatedMetadataDto
                    {
                        DataSchemaVersion = dbInitializer.DataSchemaVersion,
                        AppSchemaVersion  = dbInitializer.SchemaVersion,
                        // Not about a release: this describes the database's own state, which no version
                        // number characterises. #289's producer says the same thing for the same reason.
                        ReleaseState = NotificationReleaseState.NotApplicable,
                    },
                    title: NotificationTranslations.Original(
                               localizer, ApiMessages.NotificationDatabaseCreatedTitle),
                    body: NotificationTranslations.Original(
                               localizer, ApiMessages.NotificationDatabaseCreatedBody,
                               dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion),
                    appVersionId: currentVersion?.Id,
                    translations: NotificationTranslations.Build(
                        localizer,
                        ApiMessages.NotificationDatabaseCreatedTitle,
                        ApiMessages.NotificationDatabaseCreatedBody,
                        bodyArgs: [dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion]));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to seed the #371 database-created notification; non-fatal, startup continues.");
            }
        }

        // #371: one notification per migration this startup applied, each naming what it did. Nothing is
        // persisted beyond the payload: the description comes from this build's own migration list by version
        // number, and because that list is append-only and frozen, a database migrated by an older build
        // resolves correctly against a newer one's. A long upgrade therefore reports one row per migration,
        // which is what the issue asks for. The list is empty on a created database (the baseline path applies
        // no individual migration) and on an up-to-date one, so neither writes anything here.
        if (dbHealth.IsHealthy && dbInitializer.MigrationsApplied.Count > 0)
        {
            try
            {
                foreach (AppliedMigrationResult migration in dbInitializer.MigrationsApplied)
                {
                    await NotificationSeeding.SeedOnceAsync(
                        notificationReader,
                        notificationWriter,
                        NotificationType.Information,
                        new MigrationAppliedMetadataDto
                        {
                            Phase            = migration.Phase,
                            MigrationVersion = migration.Version,
                            Description      = migration.Description,
                            ReleaseState     = NotificationReleaseState.NotApplicable,
                        },
                        title: NotificationTranslations.Original(
                                   localizer, ApiMessages.NotificationMigrationAppliedTitle,
                                   migration.Phase, migration.Version),
                        // The description itself is English wherever it is read: it lives on the migration
                        // record, in code. Only the sentence around it is translated, which is why the body is
                        // a lead-in rather than a bare placeholder.
                        body: NotificationTranslations.Original(
                                  localizer, ApiMessages.NotificationMigrationAppliedBody, migration.Description),
                        appVersionId: currentVersion?.Id,
                        translations: NotificationTranslations.Build(
                            localizer,
                            ApiMessages.NotificationMigrationAppliedTitle,
                            ApiMessages.NotificationMigrationAppliedBody,
                            titleArgs: [migration.Phase, migration.Version],
                            bodyArgs: [migration.Description]));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to seed the #371 migration-applied notifications; non-fatal, startup continues.");
            }
        }

        // #81: third producer for #278's notification mechanism: announces every release's
        // notification-flagged changelog highlights (#307's ChangelogReservedAudience.Notification
        // convention) missed since lastActiveVersion (captured above, before migrations ran), one
        // notification per release. Reads via IChangelogReader (#309), which falls back to the JSON-backed
        // IChangelogService on its own if the changelog database isn't ready or available, so this producer
        // doesn't need to know or care which path served the document. "Seen" state is the existing
        // notification history itself (dismissing via POST /notifications/{id}/dismiss stops it reappearing);
        // no separate cookie or localStorage marker is needed. Runs detached, like #309's own changelog-import
        // task. Found live: awaiting IChangelogReader.GetDocumentAsync inline here delayed
        // StartupPhaseState.MarkComplete() enough to reintroduce the exact race #309's Step 6 fix already
        // solved once, this time affecting far more of the test suite since every WebApplicationFactory-based
        // test spins up its own full startup sequence. Timing relative to MarkComplete() doesn't matter for
        // correctness here, the same as #279's/#289's producers: writing an announcement is inherently
        // non-critical.
        if (dbHealth.IsHealthy)
        {
            backgroundWork.Start(async () =>
            {
                try
                {
                    ChangelogDocument? whatsNewDocument = await changelogReader.GetDocumentAsync(null);
                    // #319: the other languages' documents, each checked against what was actually asked for.
                    Dictionary<string, ChangelogDocument> translatedDocuments =
                        await WhatsNewNotification.LoadTranslatedDocumentsAsync(changelogReader);
                    await WhatsNewNotification.SeedAsync(
                        notificationReader,
                        notificationWriter,
                        whatsNewDocument,
                        lastActiveVersion,
                        versionService.Version,
                        currentVersion?.Id,
                        localizer,
                        translatedDocuments);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Server] Failed to seed the #81 what's-new notification; non-fatal, startup continues.");
                }
            });
        }

        // #348: a completed startup re-checks every notification whose condition can change while the
        // application runs. This is where the backups taken before migrating and before loading content are
        // accounted for: the warning they may call for cannot be written at the moment of the pre-migration
        // backup, into a table the build has not migrated yet, so it is written here, once the schema is current.
        // Only while healthy: a degraded database has nothing to write a notification into.
        // Its own try/catch was the first copy of this, and the endpoints then diverged from it, which is how a
        // read-only data directory turned a handled 409 into an unhandled 500 (backup/05 step 7). One
        // implementation now, named for the kind of call site it serves.
        if (dbHealth.IsHealthy)
            await conditionChecks.RunReportingFailuresAsync();

        // #419: the work started above runs in the background, so without this the gate opened while it was
        // still writing. An external write arriving in that window ran against a database startup had not
        // finished with: a reset would remove the System_AppVersion row the what's-new write was about to
        // point at, and that write then failed on a foreign key. Waiting here is the whole fix, at the one
        // place the rule is expressed, rather than a guard inside each producer.
        // Completion, not success: a piece of work that failed has logged its own failure and must not hold
        // the gate shut for ever.
        await backgroundWork.WhenAllCompletedAsync();
    }

    // At startup the first reason recorded stands (#326's first-wins rule), whichever part of startup found it.
    // From the migrate endpoint the application is degraded on a refused migration: refused again, that stands;
    // degraded for another reason, the new one replaces it, since the old one no longer describes the database
    // (#305's rule).
    private void Degrade(string reason, bool migrationRefused)
    {
        if (migrationRefused)
            dbHealth.MarkMigrationRefused(reason);
        else if (dbHealth.MigrationAwaitsConfirmation)
            dbHealth.ReplaceFailure(reason);
        else
            dbHealth.MarkFailed(reason);
    }
}
