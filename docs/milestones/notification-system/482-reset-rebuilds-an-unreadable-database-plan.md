# #482: A Reset cannot rebuild a database SQLite cannot read

**Status:** Planning
**GitHub issue:** #482
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute from step 2.** Every decision under *Decisions* is settled, and the issue body states the
same scope.

---

## What this fixes

A Reset of a database file SQLite cannot read answers `409`, obstacle `SourceUnreadable`, with or without
`allowNoBackup=true`, and the repair action on a degraded page reports the same refusal. The only way
out is to stop the application and delete the file by hand, which a Home Assistant add-on user may not
be able to do. #327 is blocked on this, since its requirement is that a degraded state can be resolved
from inside the application.

The cause is that `DatabaseInitializer.DropAndRebuildAsync` still treats a Reset as surgery on the old
file: it opens it, backs it up through SQLite and drops its tables one by one. #156 decided a Reset
recreates the database through the baseline path a fresh install uses, so none of that needs anything
from the old file except a backup, and an unreadable file cannot be backed up.

**Not this issue:** the `/health` reason's misdiagnosis of an unreadable file as a schema-version
mismatch is #457, which depends on this one; the six admin endpoints that answer `500` while degraded
are #453; listing, downloading and removing a set-aside file is #481.

**What the issue decides** (developer decisions, 2026-11-01): a Reset recreates the database whatever
state the old file is in, and drops no table. A corrupt file is never treated as a backup: it is moved,
with its `-wal` and `-shm`, into a reserved folder inside the backup folder, replaced by the new
database, and announced by a static notification. Listing, downloading and removing that file is #481.

---

## How it is built

**The rebuild happens beside the database, never inside it.** A Reset builds a new database file in the
data directory through the existing baseline path (`ApplyMigrationsAsync` on an empty file, which also
runs `SeedSystemContentAsync`), restores the preserved version rows into it when
`preserveSchemaVersion=true`, and only then puts it in place. A failure at any point before that leaves
the old file untouched, so "a failed Reset leaves the previous state in place" holds by construction
rather than by restoring a backup.

**How it is put in place depends on whether the old file can be read.**

| Old file | Before the build | Putting it in place |
|---|---|---|
| Readable | The normal backup, per ADR 025; a failed backup refuses unless `allowNoBackup=true`, as today | Copied over the live database through SQLite's online backup API, the same call `RestoreBackup` already makes in the other direction. Connections already open see the new database; no file is swapped under them |
| Unreadable | Nothing: no backup is attempted, so no backup-storage condition can refuse | The connection pool for the database is cleared, the file and its `-wal`/`-shm` are moved into the reserved folder under a unique name, and the new file is moved into its place |

A readable file is not swapped because on Linux a pooled connection keeps the old inode open and would
go on writing to the moved file, and on Windows the move fails outright. An unreadable file can be
swapped because the degraded state already gates every database read, and its pool is cleared first.

**Readability is decided by reading, before any storage check.** A cheap read of `sqlite_master` on the
old file classifies it, using the same `SQLITE_NOTADB` (26) and `SQLITE_CORRUPT` (11) codes
`ClassifyCopyFailure` already uses. It must come first: `CheckBackupReadiness` refuses a full backup
folder before anything reads the file, and the issue requires that a full backup folder does not stop
the Reset of an unreadable file. A file whose header reads but whose pages are corrupt passes the probe
and then fails the backup copy with `SourceUnreadable`; that file takes the unreadable branch too, and
the fragment the failed copy left in the backup folder is deleted, since it would otherwise be listed as
a backup.

**The notification is written by the initializer, after the new database is in place.** A protected
hook on `DatabaseInitializer`, overridden in `QuotinatorDatabaseInitializer`, follows the
`OnContentLoadRefusedAsync` precedent and ADR 018's rule that event-driven content is written where the
event happens. It is the one point all three callers pass through (the admin endpoint, the
Notifications-page action and the degraded page's repair action), so nothing is written three times. It
carries no `DatabaseReset` dismiss trigger, which every caller fires after a Reset and which would
dismiss it at once.

---

## Decisions

Each was a mismatch between the issue and the code or documents as they stand, raised per
`process.md` → Planning step 3. **All seven were accepted as recommended (developer, 2026-11-09)**, with
question 5 widened: every contradiction is corrected in this issue, ADR 015's included, and none is
raised separately. The developer added two directions alongside, recorded as decisions 8 and 9.

1. **A `backup_path` on another volume.** The issue says moving the file needs no extra disk space. That
   holds only when the backup folder shares a filesystem with the data directory. `Quotinator:BackupPath`
   and the add-on's `backup_path` option can put it elsewhere, and a move across filesystems is a copy
   that needs room. **Decided:** keep the reserved folder in the backup folder as decided; a
   cross-filesystem move that fails refuses the Reset with the old file untouched and any partial copy
   removed, and the limitation is stated in the endpoint description and in the `backup_path` option's
   description, which lives in `addon/translations/{en,nl,de}.yaml` rather than `addon/DOCS.md`, mirrored
   to `addon-beta/translations/` in the same commit per `CLAUDE.md`'s add-on option checklist.
2. **The obstacle an unwritable location is reported as.** The issue requires that an unwritable data
   directory still refuses. Today that refusal is a `BackupOutcome`, which names the backup destination,
   not the data directory, and ADR 022's spirit is that a wrong name is worse than no name. An unwritable
   *backup folder* also blocks the unreadable branch, since the file has nowhere to go. **Decided:**
   a new `DatabaseOperationResult` refusal carrying a new `ResetObstacle` enum in `Quotinator.Data/Enums/`
   (`DataDirectoryNotWritable`, `SetAsideFolderNotWritable`, `SetAsideMoveFailed`), answered `409` with
   its own cause and remedies, alongside the existing backup refusal rather than inside it. It is not
   persisted, so ADR 008's `CHECK` rule does not reach it.
3. **The reserved folder's name.** #481 depends on it. **Decided:** `unreadable`, held once as
   `BackupFileNames.UnreadableFolderName`, with set-aside files named
   `quotinatordata_unreadable_{yyyyMMddTHHmmssfff}Z.db` and a numeric suffix if that exists.
4. **A new notification payload kind.** The notification needs the set-aside file's name as a payload
   fact (ADR 026 rule 6), which #481 can later link to. That is a new `NotificationMetadataKind`, so
   `System_Notification`'s `MetadataKind` `CHECK` widens, which is a table-rebuild migration in
   `DataOwnedMigrations` plus the baseline, per ADR 008. **Decided:** add it
   (`UnreadableDatabaseSetAside`); the milestone-close consolidation absorbs the migration.
5. **Which documents stop describing a Reset as dropping tables.** The issue names
   `DatabaseInitializer.cs`'s comments, `CLAUDE.md` and ADR 025. Step 10 lists every other document
   found saying the same, or contradicting the current Reset, ADR 015 among them: it states that
   `System_SchemaVersion` *"is never wiped or replayed by a Reset"*, which the code and `CLAUDE.md`
   contradict. **Decided:** every one of them is corrected in this issue. There can be no contradiction
   left in the documentation.
6. **Whether this needs an ADR of its own.** "A file that cannot be read is never a backup, and is set
   aside in a reserved folder that counts towards the ceiling" constrains #481 and any later restore
   route. **Decided:** a decision added to ADR 025, which already governs where backups come from,
   rather than ADR 027.
7. **Two rows in the issue's Failing tests table.** `ResetAsync_HealthyDatabase_LeavesNoFileInTheReservedFolder`
   is listed `❌` but should be green today, because no reserved folder exists yet; step 3 confirms it.
   It is a control, as #423's plan found of its own mislabelled row. And `BackupObstacleGuidanceTests.SourceUnreadable_DoesNotOfferTheOverride`
   is listed as changing, but what it asserts stays true: migrating or loading content from an unreadable
   file still cannot proceed without a backup. **Decided:** correct the issue body to mark the first
   `✅ control`, and keep the second, adding a sibling that asserts the guidance now names a Reset.
8. **`CLAUDE.md` points to an ADR; it never restates one** (developer direction, 2026-11-09). The
   contradictions in decision 5 exist because `CLAUDE.md` restated ADR content and was not updated when
   those ADRs changed. So every `CLAUDE.md` passage this issue touches is reduced to a pointer to the ADR
   that defines the rule, and the principle itself is written into `CLAUDE.md`'s *Authoritative sources*
   section and the ADR README's rules, so it governs later edits too. Where `CLAUDE.md` holds a rule
   that no ADR defines, pointing needs a home first: the Reset and migration-recovery rules in *No
   exception-based migration recovery* are that case, and move into a new ADR 027, *A Reset recreates
   the database; a failed migration is restored, never interpreted*. ADR 025 still takes the set-aside
   decision (question 6), since that is about what counts as a backup.
9. **Every affected test is reviewed and brought up to date in this issue** (developer direction,
   2026-11-09), unit tests and automated documents alike, not only the ones the issue's table names.
   Step 2 carries the inventory and the rule each one is judged by.

---

## Steps

### 1. Confirm the decisions above

**Status:** ✅ Done, 2026-11-09

Decisions recorded above. The issue body was updated through the draft-then-review rule and approved:
its Failing tests table is corrected for question 7, and decisions 5, 8 and 9 are added to its Expected
behaviour, so the issue page states the scope this plan executes.

### 2. Review every affected test and automated document

**Status:** ⬜ Not started

Before any test is written, every existing test that exercises a Reset, a backup refusal, backup
storage or the notification kinds is read and classified, and the classification is recorded here
against each file. Inventory taken 2026-11-09 by searching for `ResetAsync`, `DropAndRebuild`,
`GetAllTables`, `SourceUnreadable`, `database/reset` and `DatabaseReset`:

- **`Quotinator.Core.Tests`:** `DatabaseInitializerTests`, `DatabaseIntegrityCheckTests`,
  `ConflictResolutionTests`, `SourceCacheWiringTests`, `UserSystemReseedConceptTests`.
- **`Quotinator.Data.Tests`:** `DatabaseBackupQuotaTests`, `DatabaseBackupOutcomeTests`,
  `BackupStorageBudgetTests`, `DatabaseInitializerOwnershipTests`, `SystemReseedConceptTests`,
  `NotificationSeedingTests`, `NotificationWriterTests`.
- **`Quotinator.Api.Tests`:** `AdminEndpointsTests`, `BackupTestHarness`, `NotificationActionExecutorTests`,
  `BackupObstacleGuidanceTests`, `StartupResilienceTests`, `ProgramNotificationSeedingRegressionTests`,
  `NotificationTableTests`, `DatabaseRepairActionsTests`, `DatabaseHealthGateMiddlewareTests`,
  `StartupWaitMiddlewareTests`, `QuoteAndAdminResponseSchemaTests`, `BackupQuotaWarningTests`,
  `SchemaIntegrityGuidanceTests`, `StartupSummaryLoggerTests`.
- **`docs/automated-testing/`:** `backup/01` to `05` and `08`; `database-lifecycle/03`;
  `startup-and-degradation/01`, `02`, `03`, `05`, `06`, `08`, `09`, `10` and `14`;
  `notifications-and-changelog/01`, `10`, `12` and `14`; `import-and-staged-actions/19`; the README index.

Each test is one of four:

| Class | Means | What happens |
|---|---|---|
| Unchanged | Its assertion holds after the fix | Run in step 12 as a regression check |
| Changes | It asserts behaviour this issue deliberately changes | Rewritten to the new behaviour, with the reason recorded here, so a changed test is never mistaken for a weakened one |
| Removed | It asserts a state a Reset can no longer reach, such as the `SourceUnreadable` refusal | Deleted, naming the test that now covers what replaced it |
| Extended | It enumerates something this issue adds to, such as every notification kind | Given the new member |

Already known: `GetAllTables_ReturnsEveryTableRegardlessOfPrefix` is *Removed* with the query;
`AdminEndpointsTests.ResetDatabase_WhenTheSourceIsUnreadable_*` is *Removed*;
`NotificationTableTests`, `NotificationMetadataKind_IsAcceptedBy*` and `notifications-and-changelog/14`
are *Extended* with `UnreadableDatabaseSetAside`; `backup/01` and `02` *Change*, as step 4 describes.
Tests that restore a backup after a failed Reset *Change*, since a failed rebuild now never touches the
live database. The search may miss a test that reaches a Reset indirectly; step 12's full run is what
catches those, and any it finds are classified here too.

### 3. Write the failing unit tests and confirm them red

**Status:** ⬜ Not started

Every test in the Verification table marked *Unit test*, written before any production change and run
against the current code. A test counts as red only when it fails on its own assertion after its
preconditions passed, not because a precondition such as the Reset returning `200` failed first, as
#423 required. Rows marked *control* must be green now and stay green. The tests land in their own
`test [#482]` commit before any production code.

`ResetAsync_CorruptDatabase_TakesNoBackup` also answers a question this plan cannot from reading: whether
today's refused Reset leaves a fragment from its failed copy in the backup folder. If it does, that is
recorded here as part of what this issue fixes.

The fixture builders follow `startup-and-degradation/08` and `09`: 34 bytes of text for *not a
database*, and a seeded file cut short for *truncated*. *Corrupt beyond its header* overwrites a page
after the first, so the probe passes and the copy fails.

### 4. Write the automated documents and confirm them red

**Status:** ⬜ Not started

- **`backup/01` and `backup/02`** are rewritten and renamed, since their titles state the refusal this
  issue removes: `backup/01-a-reset-sets-aside-a-database-it-cannot-read.md` and
  `backup/02-a-reset-sets-aside-a-truncated-database.md`. Each asserts the Reset answers `200`, `/health`
  returns to `200`, the file is in the reserved folder, and the notification names it. Each still ends
  with a positive control on a healthy database, per the suite's *ends by proving the remedy works*
  rule. Every link to the old names is updated, including the README index and `startup-and-degradation/08`.
- **`startup-and-degradation/08` and `09`** gain a final recovery step through the Reset the reason
  names, replacing *Observed effect*'s *"Not workable as offered"*.
- **`backup/09-a-reset-of-an-unreadable-database-refuses-when-the-data-directory-cannot-be-written.md`**,
  new, `Constrained` with `--read-only-data`: the Reset refuses with question 2's obstacle, and the old
  file is still in place with nothing moved.

Each runs red against a canary: the commit before this issue's first commit, built under
`quotinator:canary-482`, then the container, image and worktree removed. If `TestProbe` cannot assert
that a folder holds exactly one file matching a pattern, which the set-aside name needs, that option is
added to `tools/Quotinator.Tools.TestProbe` in this issue.

`database-lifecycle/03`'s fixture gains nothing: the new notification kind is created by a Reset rather
than surviving one, and notifications are already a category that document reads before and after.

### 5. Count subfolders towards backup storage

**Status:** ⬜ Not started

`BackupStorageBudget.UsedBytes` enumerates recursively, so every check sharing it (the readiness check,
the attempt, `BackupQuotaCheck`, `BackupMaxExceededCheck` and `GetUsage`) counts the reserved folder from
the moment a file arrives. `GetUsage`'s `FileCount` and `List` stay top-level: separating the two groups
is #481's requirement 1 and 4.

### 6. Classify the old file before anything else

**Status:** ⬜ Not started

`ResetAsync` probes readability first and branches. The readable branch keeps today's order: readiness
check, refusal or override, backup. A backup that fails with `SourceUnreadable` switches to the
unreadable branch and deletes its fragment. The pre-flight for the unreadable branch is the data
directory and the reserved folder being writable (`DirectoryWriteProbe`), refused per question 2.

The probe is where ADR 022 places the catch: SQLite offers no way to ask whether a file is a database
without reading it, so the `SqliteException` is legitimate, is caught at the probe as the first point an
outcome can be formed, and is logged through `LogExceptionHandled`. The same holds for an `IOException`
from a move across filesystems. The probe reads `sqlite_master`, an engine-specific read ADR 003's design
goal 5 permits inside `Quotinator.Data` because nothing engine-neutral answers the question; a `SELECT 1`
does not touch the file.

The `catch (SqliteException) when 26 or 11 or 14 or 8` backstop is reworked: an unreadable source can no
longer be a refusal, and codes 14 and 8 now come from building or placing the new file. Every branch it
keeps is covered by a test.

### 7. Rebuild beside the database and put it in place

**Status:** ⬜ Not started

`DropAndRebuildAsync` becomes `RebuildAsync`, with the behaviour under *How it is built*. The connection
`OnResetAsync` receives is closed and its pool cleared before an unreadable file is moved, then reopened
on the new file, so the override's audit entry and statistics run against the new database without
changing the hook's signature. The build file is removed on every failure path.

`DropAllTablesAsync` and `Sql.Schema.GetAllTables` are removed if nothing else uses them, together with
`GetAllTables_ReturnsEveryTableRegardlessOfPrefix`, which tests a query this issue retires.

### 8. Write the set-aside notification

**Status:** ⬜ Not started

- `NotificationMetadataKind.UnreadableDatabaseSetAside`, its `UnreadableDatabaseSetAsideMetadataDto`
  (the file name and the reserved folder; no prose), its entry in `NotificationMetadataKinds`, the
  `MetadataKind` `CHECK` widened in a new `DataOwnedMigrations` entry with a `Description`, and
  `DataBaselineSql` updated to match, in one commit. ADR 009's milestone-close verification covers the
  migration; this issue adds nothing to that gate.
- Title and body keys in `UI.en-GB.json`, `UI.nl.json` and `UI.de.json`, constants in `ApiMessages` /
  `NotificationMessageKeys`. Static text per ADR 026: the old file could not be read, was set aside, its
  location, that it may be flawed, and that it counts towards backup storage. Every sentence stays true
  when read later, including after #481 removes the file, because it records what happened.
- The hook described under *How it is built*, overridden in `QuotinatorDatabaseInitializer`, type
  `Warning`, no dismiss trigger.

### 9. Correct the guidance, the log text and the endpoint descriptions

**Status:** ⬜ Not started

`BackupObstacleGuidance`'s `SourceUnreadable` cause and remedies name a Reset, which now sets the file
aside, instead of stopping the application to delete it. Question 2's obstacles get a
`ResetObstacleGuidance` beside it in `Quotinator.Api/Startup/`, with the same cause-and-remedies shape,
so the endpoint and the repair action word them from one place. The reset endpoint's `[Description]` and
`docs/api-endpoints.md` describe the rebuild, the set-aside, the notification, the new refusal, and the
cross-filesystem limitation, in the same commit. `DatabaseRepairActions.RepairOutcomeText` needs no new
case; it is checked against the new refusal from question 2 and given one if the result reaches it.

`LogMessages.LogResetRefusedNoBackup`'s text says *"A reset drops every table"*; it is reworded, and
`docs/knowledgebase/no-backup-could-be-taken.md`, which quotes that line, follows it in step 10.

### 10. Leave no contradiction in the documentation

**Status:** ⬜ Not started

In `docs [#482]` commits after the code, one per document family, each ADR edited in place with no
revision section, per the ADR README.

**ADRs, which define the rules:**

- **ADR 027, new:** *A Reset recreates the database; a failed migration is restored, never interpreted*.
  It takes the rules `CLAUDE.md`'s *No exception-based migration recovery* section defines today and no
  ADR states (decision 8): a Reset rebuilds through the baseline and never repairs in place, what
  `preserveSchemaVersion` keeps, that a version/schema mismatch is a hard failure with no self-heal, and
  that a failed migration restores its backup and rethrows. Indexed in the ADR README in the same commit.
- **ADR 025:** the set-aside decision (question 6), and its Reset paragraph reworded from *"drops every
  table"* to replacing the database.
- **ADR 003:** design goal 5's example *"listing the tables a Reset drops"* replaced by the readability
  probe, which is now the metadata read a Reset makes.
- **ADR 014:** its context describes the pre-#156 Reset as current (*"Today, all four audit-trail tables
  are excluded from the tables a Reset drops"*); restated as the Reset that is.
- **ADR 015:** *"`System_SchemaVersion` is never wiped or replayed by a Reset"* corrected, and its
  consequence about a database stranded by an edited Data-owned migration restated against what a Reset
  actually does.

**`CLAUDE.md`, which points:** the *No exception-based migration recovery* section, the Reset sentence in
*Audit-trail tables never purge dangling references*, and the backup paragraph in *Endpoint side-effect
policy* each become a short statement naming ADR 027, 014 or 025, with no restated rule. The pointer
principle itself is added to *Authoritative sources* (decision 8), and to the ADR README's rules.

**Other documents, which describe:** `docs/api-endpoints.md` and `docs/data-import.md` (both say the
Reset *"drops the entire database"*), `docs/database-conventions.md`'s audit-trail row that still calls
#156 *"proposed"*, `docs/knowledgebase/the-database-file-cannot-be-read.md` (its remedy becomes the
Reset; its retention decided at close by `docs/knowledgebase.md`'s rule) and
`docs/knowledgebase/no-backup-could-be-taken.md`, and the comments in `DatabaseInitializer.cs` and
`QuotinatorDatabaseInitializer.cs`.

**Checked and left alone:** `docs/troubleshooting.md` and `addon/DOCS.md` describe the v1.5 to v1.6.1
Reset defect in the past tense, which stays true; `docs/release-verification.md`'s *"table-wipe logic"*
names a category of change, not a mechanism.

The search for this list was by phrase (`drop`, `every table`, `wipe`, `survive`, `never wiped`,
`DropAndRebuild`, `SourceUnreadable`) across `CLAUDE.md` and `docs/`, outside milestone plan docs, which
record their own time. Row 28's test runs the same search on every build, so a phrase missed here or
reintroduced later fails rather than drifting.

### 11. Boyscout pass over every touched file

**Status:** ⬜ Not started

Every touched file added to `.editorconfig`'s scoped `IDE0008` and `IDE0090` sections the moment it is
first touched, with both conversions run until the counts stop dropping and the follow-on
`IDE0028`/`IDE0305` fixed; `[Subsystem - Phase]` prefixes on log lines in touched files; `nameof` for
column names in any touched `Sql.*` class. If `DatabaseRepairActions.razor.cs` is touched, its
hard-coded English outcome text moves into the `i18ntext` files, since it breaches the localisation rule
with no reason stated; if it is not touched, that breach is reported to the developer instead.

### 12. Build and run the full suite

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both `0 Warning(s)  0 Error(s)`, every test green. A failure in a test step 2 did not classify is
classified there before it is fixed.

### 13. T1

**Status:** ⬜ Not started

The developer starts the application in Visual Studio against a database file replaced with non-database
bytes, confirms the degraded page loads, presses *Reset the database* and *Confirm*, and confirms the
page returns to the healthy application and the Notifications page shows the set-aside notification.
This is the issue's reproduction step 3, which no endpoint test drives.

### 14. T2

**Status:** ⬜ Not started

The documents from step 4 against `quotinator:local`, then the issue scope: the smoke set, plus every
document step 2 classified as *Changes* or *Extended*, and `database-lifecycle/03` and `backup/03`, `04`,
`05` and `08`, because the readable branch's Reset is reimplemented and its backup refusals and ceiling
behaviour must not move. Each *Observed effect* is re-measured where its document changed.

### 15. Changelog

**Status:** ⬜ Not started

`unreleased` in `changelog.en.json`, `nl.json` and `de.json` in one commit, with `482` in `issues`, a
`fixed` entry and a highlight (a Reset now recovers a database file that cannot be read). `CHANGELOG.md`
regenerated with `--max-releases 3`; the add-on changelogs are not.

---

## Scope changes

None yet.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A Reset of a file that is not a database rebuilds an empty database | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_RebuildsAnEmptyDatabase` |
| 2 | ❌ | A Reset of a truncated file rebuilds an empty database | Unit test | `DatabaseInitializerTests.ResetAsync_TruncatedDatabase_RebuildsAnEmptyDatabase` |
| 3 | ❌ | A file corrupt beyond its header is set aside too, and the failed copy leaves no fragment | Unit test | `DatabaseInitializerTests.ResetAsync_DatabaseCorruptBeyondItsHeader_SetsItAsideAndLeavesNoBackupFragment` |
| 4 | ❌ | The unreadable file is moved into the reserved folder | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_MovesTheFileToTheReservedFolder` |
| 5 | ❌ | Its `-wal` and `-shm` move with it | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithSidecars_MovesTheSidecarsWithIt` |
| 6 | ❌ | An unreadable file is never treated as a backup | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_TakesNoBackup` |
| 7 | ❌ | The set-aside file is not listed as a backup | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_SetAsideFileIsNotListedAsABackup` |
| 8 | ❌ | A readable database still gets its backup first (control: green before and after) | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_StillTakesItsBackup` |
| 9 | ❌ | A readable database leaves nothing in the reserved folder (control: green before and after) | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_LeavesNoFileInTheReservedFolder` |
| 10 | ❌ | A connection opened before a readable Reset sees the rebuilt database | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_AConnectionOpenedBeforeTheResetSeesTheRebuiltDatabase` |
| 11 | ❌ | A failed backup of a readable database still refuses, and the override still works (control: green before and after) | Unit test | `dotnet test tests/Quotinator.Core.Tests --configuration Release --filter "FullyQualifiedName~DatabaseInitializerTests.ResetAsync"`: every existing #348 refusal and override test passes |
| 12 | ❌ | A failed rebuild of a readable database leaves it as it was | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_FailedRebuild_LeavesTheDatabaseAsItWas` |
| 13 | ❌ | A failed rebuild of an unreadable file leaves that file in place, with nothing moved | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_FailedRebuild_LeavesTheFileInPlace` |
| 14 | ❌ | Files in subfolders of the backup folder count towards storage | Unit test | `BackupStorageBudgetTests.UsedBytes_FileInASubfolder_IsCounted` |
| 15 | ❌ | The set-aside file counts from the moment it arrives | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_SetAsideFileCountsTowardsBackupStorage` |
| 16 | ❌ | A full backup folder does not stop the Reset of an unreadable file | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithAFullBackupFolder_StillRebuilds` |
| 17 | ❌ | A full disk does not stop the Reset of an unreadable file | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithNoFreeDiskSpace_StillRebuilds` |
| 18 | ❌ | Two unreadable files never overwrite each other | Unit test | `DatabaseInitializerTests.ResetAsync_TwoCorruptDatabases_KeepsBothFiles` |
| 19 | ❌ | An unwritable data directory still refuses, naming its obstacle, with the file untouched | Live (Docker) | `backup/09-a-reset-of-an-unreadable-database-refuses-when-the-data-directory-cannot-be-written.md`, every step exits `0` |
| 20 | ❌ | `preserveSchemaVersion=true` on an unreadable file proceeds with the baseline's own version rows | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_PreserveSchemaVersion_ProceedsWithTheBaselineVersionRows` |
| 21 | ❌ | The new payload kind is accepted by the baseline and the incremental replay alike | Unit test | `DatabaseInitializerOwnershipTests.NotificationMetadataKind_IsAcceptedByTheBaseline` and `.NotificationMetadataKind_IsAcceptedByTheIncrementalReplay`, each with a new `UnreadableDatabaseSetAside` row; `.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` stays green |
| 22 | ❌ | A Reset of an unreadable file through the endpoint returns the application to healthy | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetRecoversToHealthy` |
| 23 | ❌ | The Reset leaves a notification naming the set-aside file, written after the wipe, with its text stored in every language | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetLeavesANotificationNamingTheSetAsideFile` |
| 24 | ❌ | The degraded page's repair action recovers an unreadable file too | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_RepairActionRecoversToHealthy` |
| 25 | ❌ | The endpoint answers the new refusal as `409`, naming its obstacle and remedies | Unit test | `AdminEndpointsTests.ResetDatabase_WhenTheDataDirectoryCannotBeWritten_Returns409NamingTheObstacle`. `ResetDatabase_WhenTheSourceIsUnreadable_DoesNotOfferTheOverride` and `.ResetDatabase_WhenTheSourceIsUnreadable_OffersReplacingTheFile` are removed, since a Reset can no longer produce that refusal; row 22 asserts what replaces it |
| 26 | ❌ | The `SourceUnreadable` guidance names a Reset, and still offers no override | Unit test | `BackupObstacleGuidanceTests.SourceUnreadable_OffersAReset`; `.SourceUnreadable_DoesNotOfferTheOverride` stays green |
| 27 | ❌ | Every new refusal obstacle has a cause and at least one remedy | Unit test | `ResetObstacleGuidanceTests.EveryObstacle_HasACauseAndARemedy` |
| 28 | ❌ | No document or comment describes a Reset as dropping tables, or an unreadable file as unresettable | Unit test | `RepositoryStructureTests.NothingDescribesAResetAsDroppingTables`, searching `CLAUDE.md`, `docs/` outside milestone plan docs, and the endpoint `[Description]`s for step 10's phrases |
| 29 | ❌ | The `CLAUDE.md` passages on Reset, migration recovery and backups point to ADRs 027, 025 and 014 instead of restating them, and the pointer principle is written down | Unit test | `RepositoryStructureTests.ClaudeMdResetPassagesPointToTheirAdrs`: each passage links its ADR and carries none of the rule phrases the ADR defines; `CLAUDE.md`'s *Authoritative sources* and the ADR README state the principle |
| 30 | ❌ | ADR 027 exists and is indexed | Unit test | `RepositoryStructureTests.EveryAdrIsIndexed`, new, written in step 3: every file in `docs/architecture-decisions/` has a row in the README index, and is listed in `Quotinator.slnx`. No such guard exists today (searched 2026-11-09) |
| 31 | ❌ | On a real volume, a Reset of a non-database file recovers, sets it aside and notifies | Live (Docker) | `backup/01-a-reset-sets-aside-a-database-it-cannot-read.md`, every step exits `0` |
| 32 | ❌ | The same for a truncated file | Live (Docker) | `backup/02-a-reset-sets-aside-a-truncated-database.md`, every step exits `0` |
| 33 | ❌ | A degraded start on a non-database file is recovered by the Reset its reason names | Live (Docker) | `startup-and-degradation/08`, every step exits `0`, including the new recovery step |
| 34 | ❌ | The same for a truncated file | Live (Docker) | `startup-and-degradation/09`, every step exits `0`, including the new recovery step |
| 35 | ❌ | A Reset of a readable database is still a full wipe (control: green before and after) | Live (Docker) | `database-lifecycle/03-reset-is-a-full-wipe.md`, every step exits `0` |
| 36 | ❌ | The degraded page's *Reset the database* recovers the application and shows the notification | Manual | Step 13: the developer confirms the page returns healthy and the Notifications page lists the set-aside notification |
| 37 | ❌ | Every test step 2 classified as *Unchanged*, *Changes* or *Extended* passes, and the full suite is clean | Build | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: `0 Warning(s)  0 Error(s)`, all tests pass |