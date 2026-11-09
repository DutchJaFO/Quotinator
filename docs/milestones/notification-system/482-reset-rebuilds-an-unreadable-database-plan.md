# #482: A Reset cannot rebuild a database SQLite cannot read

**Status:** In progress (step 13, T1)
**GitHub issue:** #482
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Step 13, T1, by the developer.** Every other step is done, T2 included. Verification row 36 is the only
one open, and it closes when the developer confirms step 13's reproduction in Visual Studio.

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

**Readability is decided by reading every page, before any storage check.** `PRAGMA quick_check` on the
old file classifies it: anything but `ok`, or the `SQLITE_NOTADB` (26) and `SQLITE_CORRUPT` (11) codes
`ClassifyCopyFailure` already uses, means unreadable. Opening the file or reading `sqlite_master` is not
enough: SQLite's page copy does not validate what it copies, so a file whose first page reads and whose
later pages are damaged would be copied into the backup folder as a backup, the exact thing the issue
forbids. It must come first: `CheckBackupReadiness` refuses a full backup folder before anything reads
the file, and the issue requires that a full backup folder does not stop the Reset of an unreadable file.
A backup copy that still fails with `SourceUnreadable`, because the file changed after the check, takes
the unreadable branch too, and the fragment it left is deleted.

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

**Status:** ✅ Done, 2026-11-09. Classification below, then the inventory it was drawn from.

**Classification.** A file not named here was read and holds only *Unchanged* tests: its tests reach a
Reset through a readable database, a stub `IDatabaseInitializer` or a route list, none of which this
issue changes.

| File / document | Test or passage | Class | Why |
|---|---|---|---|
| `AdminEndpointsTests` | `ResetDatabase_WhenTheSourceIsUnreadable_DoesNotOfferTheOverride`, `..._OffersReplacingTheFile` | Removed | A Reset can no longer refuse with `SourceUnreadable`; row 22 asserts what replaces it |
| `AdminEndpointsTests` | `ResetDatabase_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500` | Changes | Its spy refuses with `SourceUnreadable`, an obstacle a Reset can no longer report; switched to `BudgetExceeded`, which still reaches the same `409`. The assertion is unchanged |
| `AdminEndpointsTests` | the comment above `..._DoesNotOfferTheOverride` (*"fails inside the table drop"*) | Removed | Goes with its test |
| `BackupObstacleGuidanceTests` | `SourceUnreadable_DoesNotOfferTheOverride` | Changes | Assertion stays (decision 7); its comment's reason, *"cannot be dropped table by table"*, is replaced by the true one: migration and content load cannot proceed without a backup |
| `DatabaseInitializerOwnershipTests` | `ResetAsync_MigrationFailsDuringReplay_RestoresPreResetBackupAndRethrows` | Changes | The assertion (pre-reset data intact, exception rethrown) holds, but its name and comment say a backup is restored after a table drop. Renamed `..._LeavesTheDatabaseUntouchedAndRethrows`, comment restated |
| `DatabaseInitializerOwnershipTests`, `SystemReseedConceptTests`, `UserSystemReseedConceptTests` | the test doubles' `OnResetAsync => DropAndRebuildAsync(...)` | Changes | Mechanical: the method is renamed `RebuildAsync` (step 7) |
| `DatabaseInitializerOwnershipTests` | `NotificationMetadataKind_IsAcceptedByTheBaseline`, `..._IsAcceptedByTheIncrementalReplay` | Extended | `UnreadableDatabaseSetAside` row |
| `DatabaseInitializerTests` | `GetAllTables_ReturnsEveryTableRegardlessOfPrefix` | Removed | Tests the query step 7 retires, if nothing else uses it |
| `DatabaseInitializerTests` | `ResetAsync_AfterInitialise_RebuildsSchemaAndDoesNotReseed` | Changes | Comment only: *"drops and recreates all tables"* |
| `DatabaseBackupQuotaTests` | `ResetAsync_WhenNoBackupCanBeTaken_NeverReachesTheDestructiveStep` | Changes | Message only: *"the tables were never dropped"* |
| `StartupResilienceTests` | `Startup_DatabaseFileCorrupt_HealthReasonNamesARemedyThatCanWork` | Changes | Comment only: *"A Reset refuses on a file SQLite cannot open"* becomes false. The assertion is about the health reason's wording, which is #457's |
| `NotificationTableTests` | the per-kind maps at the payload-rendering tests | Extended | `UnreadableDatabaseSetAside` entries |
| `DatabaseRepairActionsTests` | `RepairOutcomeText` cases | Extended | A case for the new refusal (decision 2), if the executor's result reaches the control |
| `NotificationActionExecutorTests` | `ResetDatabase_IsOffered_WhenABackupCanBeTaken`, `ResetDatabase_IsWithheld_WhenABackupCannotBeTaken` | Unchanged | Checked deliberately: availability serves the Notifications page, which renders only while the database is readable, and a readable database still needs its backup. The degraded page's repair control does not consult availability, so an unreadable file is never withheld its Reset |
| `DatabaseBackupQuotaTests`, `DatabaseIntegrityCheckTests`, `ConflictResolutionTests`, `SourceCacheWiringTests`, `BackupQuotaWarningTests`, the rest of `DatabaseInitializerTests` | every Reset test | Unchanged | Readable databases; the readable branch keeps its order and its backup |
| `DatabaseBackupOutcomeTests` | `CorruptSourceDatabase_IsReportedAsSourceUnreadable` and its sibling | Unchanged | `CreateBackup` on a corrupt file still reports `SourceUnreadable`; the Reset now acts on it rather than refusing |
| `backup/01`, `backup/02` | whole document | Changes | Renamed and rewritten (step 4) |
| `backup/03` | *Observed effect*'s reference to `DropAndRebuildAsync`, and *"drop every table"* | Changes | The method's new name, and wording |
| `backup/08` | *"A Reset drops every table, this row included"* | Changes | Wording; what the step measures is unchanged |
| `notifications-and-changelog/10` | its description of the Reset as *"drops and rebuilds"* | Changes | Wording, found by step 3's guard |
| `startup-and-degradation/08`, `09` | *Observed effect*'s recovery paragraph; a new final recovery step | Changes | Step 4 |
| `notifications-and-changelog/14` | the kind list | Extended | `UnreadableDatabaseSetAside` |
| `README.md` (automated testing) | the `backup/` index | Changes | Two renamed rows, `backup/09` added |
| `backup/03`, `04`, `05`; `database-lifecycle/03`; `startup-and-degradation/01`, `02`, `03`, `05`, `06`, `10`, `14`; `notifications-and-changelog/01`, `10`, `12`; `import-and-staged-actions/19` | everything else | Unchanged | Reset of a readable database, or the Reset route as a setup step |

**Inventory**

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
| Changes | It asserts behaviour this issue deliberately changes, or its name, comment or message describes the old Reset | Rewritten to the new behaviour, with the reason recorded here, so a changed test is never mistaken for a weakened one. A test's comment is documentation, and decision 5 leaves no contradiction there either |
| Removed | It asserts a state a Reset can no longer reach, such as the `SourceUnreadable` refusal | Deleted, naming the test that now covers what replaced it |
| Extended | It enumerates something this issue adds to, such as every notification kind | Given the new member |

The search was widened twice: by test name (`Reset`, `Rebuild`, `Drop`, `Backup`, `Unreadable`,
`Corrupt`), which added `StartupBackupRefusalTests`, `DatabaseBackupPreflightTests`, the
`AdminBackup*EndpointTests` and `DatabaseBackupReaderTests` (all *Unchanged*: backup readiness and the
backup endpoints, which this issue does not touch beyond step 5's counting); and by the phrases step 10
searches documents for, which found the comment-only *Changes* above. A test that reaches a Reset
indirectly may still be missed; step 12's full run catches it, and it is classified here before it is
fixed.

### 3. Write the failing unit tests and confirm them red

**Status:** ✅ Done, 2026-11-09. Run against the code at `670e6e56`: 23 red on their own assertions, 6
controls green, and the two tests that need a type this issue introduces deferred to the commit that
introduces it.

Every test in the Verification table marked *Unit test*, written before any production change and run
against the current code. A test counts as red only when it fails on its own assertion after its
preconditions passed, not because a precondition such as the Reset returning `200` failed first, as
#423 required. Rows marked *control* must be green now and stay green. The tests land in their own
`test [#482]` commit before any production code.

The new Reset tests live in `DatabaseInitializerTests.UnreadableReset.cs`, a partial of the existing
class. The reserved folder's name and the new notification kind and obstacle are written as literals in
the tests rather than read from production constants: they are a contract with the user and with #481,
and a rename must fail a test rather than follow silently. That is also what lets every test compile,
and so run red, before the constants exist.

**What the red run found.**

- **A refused Reset of a corrupt file leaves a fragment in the backup folder, and the backup listing
  offers it as a restore point.** `ResetAsync_CorruptDatabase_TakesNoBackup` and
  `ResetAsync_CorruptDatabase_SetAsideFileIsNotListedAsABackup` are both red on it: the failed copy
  leaves its destination file behind, and `GET /api/v1/admin/backups` lists it. This issue removes it
  by never attempting the copy for an unreadable file, and step 6 deletes any fragment a copy still
  leaves.
- **Opening the file is not a sufficient readability check**, recorded under *How it is built*: the
  probe is `PRAGMA quick_check`, which reads every page. Found while designing
  `ResetAsync_DatabaseCorruptBeyondItsHeader_SetsItAsideAndKeepsNoBackup`, whose fixture's first page
  reads and whose later pages do not.
- **Three rows were controls, not red tests**, and are marked so in the table: a connection opened
  before a readable Reset already sees the rebuilt database (row 10), a failed rebuild of a readable
  database already leaves it as it was (row 12), and every ADR is already indexed (row 30). Each guards
  a property the change must keep.
- **Two tests need the code they test before they can compile**, and are written in the same commit as
  that code: `ResetObstacleGuidanceTests.EveryObstacle_HasACauseAndARemedy` enumerates an enum that does
  not exist yet, and the kind-acceptance tests in `DatabaseInitializerOwnershipTests` enumerate
  `NotificationMetadataKind` themselves, so they turn red the moment its new member is declared and the
  `CHECK` has not yet been widened. That moment is their red run, recorded at step 8.
- **The documentation guard found more than step 10 listed**: `IDatabaseInitializer.cs`'s Reset summary,
  `notifications-and-changelog/10`, and further lines in `backup/01` and `backup/03`. Step 10 now names
  them.

The fixture builders follow `startup-and-degradation/08` and `09`: 34 bytes of text for *not a
database*, and a seeded file cut short for *truncated*. *Corrupt beyond its header* overwrites every page
after the first.

### 4. Write the automated documents and confirm them red

**Status:** ✅ Done, 2026-11-09. All five red against `quotinator:canary-482` (built from `69800a23`), each
at the step it exists for, with every earlier step green; container, image and worktree removed.

| Document | Control steps | Red at | Read |
|---|---|---|---|
| `backup/01` | 1–3 | 4 | `409`, `backupObstacle=SourceUnreadable` |
| `backup/02` | 1–3 | 4 | `409`, `backupObstacle=SourceUnreadable` |
| `startup-and-degradation/08` | 1–4 | 5 | `409`, `/health` still `503`, no `backups/unreadable/` |
| `startup-and-degradation/09` | 1–4 | 5 | the same |
| `backup/09` | 1–3 | 4 | `409` with `backupObstacle=DestinationFileNotWritable` and no `resetObstacle`; file unmoved |

**Two readings beyond the steps.** After `backup/01`'s refused Resets, `backups/` held one `.db` file per
attempt: the fragment step 3's red run predicted, now seen live. And `backup/09`'s refusal on the old build
blamed the backup folder rather than the source, so its canary paragraph was corrected from the
`SourceUnreadable` it had assumed.

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
`quotinator:canary-482`, then the container, image and worktree removed. `TestProbe` already had what the
set-aside name needs (`--expect-match-count`, and `--expect-identical-in` to recognise the file by its
bytes), so no tool change was needed.

**Written 2026-11-09.** The two rewrites end with a Reset of the now-healthy database as their positive
control: it takes a backup and leaves the reserved folder as it was, so the set-aside assertions are about
the unreadable file rather than every Reset. `08` and `09` gain step 5, the recovery, with the old step 5
moved to 6; their *Observed effect* recovery paragraphs now say the route is asserted rather than
recording a refusal, and `08`'s note that the reason names the wrong remedy first is restated against
#457, which owns the reason. All three new or rewritten documents join the README's *Observed effects
awaiting measurement* list (now 40 of 84) until step 14 measures them, and the index, the solution and
`startup-and-degradation/09`'s links follow the renames. `AutomatedTestingSuiteTests` and the
`RepositoryStructureTests` that guard the suite pass after the change.

`database-lifecycle/03`'s fixture gains nothing: the new notification kind is created by a Reset rather
than surviving one, and notifications are already a category that document reads before and after.

### 5. Count subfolders towards backup storage

**Status:** ✅ Done, 2026-11-09. `UsedBytes_FileInASubfolder_IsCounted` green.

**Missed by step 2's search:** `BackupStorageBudgetTests.UsedBytes_IgnoresFilesInSubdirectories`
asserted the opposite decision, and its name matched none of the patterns searched. It is *Removed*,
replaced by the test above, whose comment records that the folder's own files only was the earlier
decision.

`BackupStorageBudget.UsedBytes` enumerates recursively, so every check sharing it (the readiness check,
the attempt, `BackupQuotaCheck`, `BackupMaxExceededCheck` and `GetUsage`) counts the reserved folder from
the moment a file arrives. `GetUsage`'s `FileCount` and `List` stay top-level: separating the two groups
is #481's requirement 1 and 4.

### 6. Classify the old file before anything else

**Status:** ✅ Done, 2026-11-09. `IsDatabaseFileReadable` and `CheckSetAsideReadiness` in `DatabaseInitializer`.

**The check opens the file `immutable`, read-only, and that is load-bearing.** The red tests found that
any other open alters what is being set aside: a read-write open of a damaged file let SQLite discard a
`-wal` it could not use, and even a read-only open rewrote the `-shm`, both caught by
`ResetAsync_CorruptDatabaseWithSidecars_MovesTheSidecarsWithIt`. `immutable=1` reads the main file alone,
with no locking and no shared memory, which also lets it open a WAL database on a read-only mount without
creating a `-shm` it cannot write.

**A failed backup copy no longer leaves a fragment, for every caller.** `CreateBackup` deletes the file it
was writing when the copy fails, so the defect step 3 found is gone from on-demand backups and migrations
too, not only from a Reset. `DiskFilledDuringBackup`'s remedy *"remove the partially written backup file"*
became false with it, and was replaced.

`ResetAsync` probes readability first and branches. The readable branch keeps today's order: readiness
check, refusal or override, backup. A backup that fails with `SourceUnreadable` switches to the
unreadable branch and deletes its fragment. The pre-flight for the unreadable branch is the data
directory and the reserved folder being writable (`DirectoryWriteProbe`), refused per question 2.

The probe is where ADR 022 places the catch: SQLite offers no way to ask whether a file is a database
without reading it, so the `SqliteException` is legitimate, is caught at the probe as the first point an
outcome can be formed, and is logged through `LogExceptionHandled`. The same holds for an `IOException`
from a move across filesystems. The probe is `PRAGMA quick_check`, an engine-specific read ADR 003's
design goal 5 permits inside `Quotinator.Data` for verifying a database's integrity, because nothing
engine-neutral answers the question.

The `catch (SqliteException) when 26 or 11 or 14 or 8` backstop is reworked: an unreadable source can no
longer be a refusal, and codes 14 and 8 now come from building or placing the new file. Every branch it
keeps is covered by a test.

### 7. Rebuild beside the database and put it in place

**Status:** ✅ Done, 2026-11-09. `RebuildAsync`, `BuildDatabaseAsync`, `ReplaceInPlace`, `SetAsideAndReplaceAsync`;
`DropAllTablesAsync` and `Sql.Schema.GetAllTables` removed with their test. Every Reset test in Core
(21) and Data green; the readable branch's existing tests, including every #348 refusal, unchanged.

**A finding outside this issue, preserved rather than fixed: `allowNoBackup=true` cannot take effect.**
When a readable database's backup cannot be taken, the override passes the pre-flight, and then the
Reset's own backup fails the same check and refuses. No test exercised the override against a real
backup failure: `ResetAsync_WithTheOverride_ReportsThatTheBackupWasSkipped` uses a double whose Reset
takes no backup. `RebuildAsync` keeps that behaviour exactly, so this issue changes nothing about it; it
is raised with the developer to file separately.

`DropAndRebuildAsync` becomes `RebuildAsync`, with the behaviour under *How it is built*. The connection
`OnResetAsync` receives is closed and its pool cleared before an unreadable file is moved, then reopened
on the new file, so the override's audit entry and statistics run against the new database without
changing the hook's signature. The build file is removed on every failure path.

`DropAllTablesAsync` and `Sql.Schema.GetAllTables` are removed if nothing else uses them, together with
`GetAllTables_ReturnsEveryTableRegardlessOfPrefix`, which tests a query this issue retires.

### 8. Write the set-aside notification

**Status:** ✅ Done, 2026-11-09. Migration 29. `NotificationMetadataKind_IsAcceptedByTheBaseline` and
`..._IsAcceptedByTheIncrementalReplay` ran red for `UnreadableDatabaseSetAside` between declaring it and
widening the `CHECK`, then green. Written through `OnUnreadableDatabaseSetAsideAsync`, type `Warning`, in
all three languages; `NotificationTableTests` and `notifications-and-changelog/14` extended. The API
reports `metadataKind` in lower case, as the suite's existing documents already assume, so `backup/01`
and `02` were corrected to `unreadabledatabasesetaside`.

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

**Status:** ✅ Done, 2026-11-09. `ResetObstacleGuidance` with its test; the endpoint, the executor and
`DatabaseRepairActions` handle `ResetObstacle`; the `[Description]`, `docs/api-endpoints.md` and the
`backup_path` option text in all six add-on translation files carry the cross-volume limit. The new log
lines use `[Database - Init]`, the prefix the Reset already logs under, rather than a new one.

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

**Status:** ✅ Done, 2026-11-09. ADR 027 written, indexed and in the solution; ADRs 003, 014, 015 and 025
corrected; the three `CLAUDE.md` passages reduced to pointers and the pointer principle written into
`CLAUDE.md`'s *Authoritative sources* and the ADR README's rules. `NothingDescribesAResetAsDroppingTables`,
`ClaudeMdResetPassagesPointToTheirAdrs` and both `ThePointerPrincipleIsWrittenDown` rows green.

**Found beyond the list below, and corrected:**

- **ADR 014's context still argued for the export as a future release gate** (*"Once #156 ships"*), and
  carried a dated correction paragraph. Both restated as the current fact, per the ADR README's rule
  against history sections: the export exists, and a Reset without it is a defect against the ADR.
- **`IDatabaseInitializer.ResetAsync`'s `preserveSchemaVersion` documentation was false before this
  issue**: it said the history is *"left untouched instead of being cleared and replayed"*. Restated as the
  rows being carried into the new database.
- **Three comments still named `Sql.Schema.GetUserTables`**, retired by #156: `AuditMigrations`
  (describing `System_AuditEntries` as protected from a Reset), `ImportBatchType` (*"re-seeded … on every
  reseed/reset"*) and `QuotinatorDatabaseInitializer`. Corrected; the first two files joined the boyscout
  scope.
- **The phrase search cannot see a document that is right in words but wrong in fact**: the knowledgebase
  entry *the database file cannot be read* named no superseded phrase, yet its whole remedy was to delete
  the file by hand. Rewritten around the Reset, its title changed in the knowledgebase index. Its
  retention is decided at close by `docs/knowledgebase.md`'s rule: it never reached a release.
- **`backup/03`'s quoted remedies** describe the fragment a failed copy used to leave. Kept as the measured
  quote it is, with a paragraph saying what #482 changed and that none of the document's assertions move.

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
  probe, `PRAGMA quick_check`, which is now the metadata read a Reset makes.
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
Reset *"drops the entire database"*), `IDatabaseInitializer.cs`'s Reset summary (*"drops and rebuilds"*),
the automated documents step 2 classifies as *Changes*, `docs/database-conventions.md`'s audit-trail row that still calls
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

**Status:** ✅ Done, 2026-11-09. Every touched file is in both scoped sections, and the build reports no
`IDE0008` or `IDE0090`. The new log lines carry `[Database - Init]`.

- **`DatabaseRepairActions.razor.cs`'s outcome sentences moved into the translation files**, five keys in
  `UI.en-GB.json`, `UI.nl.json` and `UI.de.json`, with `RepairOutcomeText` taking the `UI` table.
  `DatabaseRepairActionsTests.EveryOutcomeSentence_ComesFromTheTranslationFiles` asserts each outcome
  starts with its baseline sentence. The obstacle's cause and remedies still come from
  `BackupObstacleGuidance` and `ResetObstacleGuidance`, which the API's problem responses share and which
  are English by their own design until #333; that is recorded at the method, not changed here.
- **`nameof` in `Sql.Schema` has nothing to point at**: no entity type exists for the two version tables,
  only a private record inside `DatabaseInitializer`. Its stale `GetUserTables` comment was corrected
  instead.

Every touched file added to `.editorconfig`'s scoped `IDE0008` and `IDE0090` sections the moment it is
first touched, with both conversions run until the counts stop dropping and the follow-on
`IDE0028`/`IDE0305` fixed; `[Subsystem - Phase]` prefixes on log lines in touched files; `nameof` for
column names in any touched `Sql.*` class. If `DatabaseRepairActions.razor.cs` is touched, its
hard-coded English outcome text moves into the `i18ntext` files, since it breaches the localisation rule
with no reason stated; if it is not touched, that breach is reported to the developer instead.

### 12. Build and run the full suite

**Status:** ✅ Done, 2026-11-09. `dotnet build --configuration Release`: `0 Warning(s)  0 Error(s)`.
`dotnet test --configuration Release --verbosity normal -m:1`: 4,983 tests across 12 projects, all
passing, `0 Warning(s)  0 Error(s)`. No test outside step 2's classification failed.

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

**Status:** ✅ Done, 2026-11-09. 28 documents against `quotinator:local` built from the working tree at
`271c4c54`; every assertion that belongs to this issue green.

The documents from step 4 against `quotinator:local`, then the issue scope: the smoke set, plus every
document step 2 classified as *Changes* or *Extended*, and `database-lifecycle/03` and `backup/03`, `04`,
`05` and `08`, because the readable branch's Reset is reimplemented and its backup refusals and ceiling
behaviour must not move. Each *Observed effect* is re-measured where its document changed.

- **Green on every step:** `backup/01`, `02`, `03`, `04`, `05`, `08`, `09`; `startup-and-degradation/01`,
  `03`, `05`, `06`, `08` to `14`; `notifications-and-changelog/07`, `10` and `14`; `api-surface/01` and
  `02`; `import-and-staged-actions/01`, `14` and `19`.
- **Green apart from a known issue the document names:** `database-lifecycle/03` step 8 read every
  `/version` count as `0`, which is #468; steps 9 to 12, the Reset of a readable database, all green.
  `notifications-and-changelog/01` step 9 read `totalCount=0`, which is #472.
- **`scripts/testing/capture-page.csx` repaired, because it verifies this issue.** The first run of
  `notifications-and-changelog/01` and `14` failed their log checks on exceptions the capture tool caused.
  In 2 to 4 of every 10 captures against a fresh container, foreground or background alike, the page
  stopped producing frames: plain JavaScript still ran, but `requestAnimationFrame` never fired, so
  `Page.captureScreenshot` was never answered and the capture waited forever. Killing it left the page's
  Blazor circuit open, and the server logged a `WebSocketException` four times. Asking again on the same
  page, forcing a repaint, re-applying the viewport, bringing the page to the front and launch flags
  against background throttling all failed to unstick it. The stall shows before the page script runs:
  across 24 captures, a frame check right after loading predicted every outcome. The script now runs
  that check and replaces a stalled browser with a fresh one, up to five times, before the page script
  can press anything; every browser call has a timeout, and the browser is closed rather than killed on
  every path. Measured on 30 captures: 30 written, 9 of them after a fresh browser, no exception in the
  container log, no process or profile left behind. Both documents then re-ran green with their own log
  checks, every `[Runtime - Exception]` count `0`, `01` still stopping only at step 9 (#472).
- **Observed effects measured:** `backup/01`, `02` and `09`, which leave *Observed effects awaiting
  measurement* (now 37 of 84), and the recovery paragraphs of `startup-and-degradation/08` and `09`.

### 15. Changelog

**Status:** ✅ Done, 2026-11-09. `482` in `unreleased.issues`, one highlight and one `fixed` entry in each of
`changelog.en.json`, `nl.json` and `de.json`, and `CHANGELOG.md` regenerated. The add-on changelogs are
untouched, per the release workflow.

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
| 1 | ✅ | A Reset of a file that is not a database rebuilds an empty database | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_RebuildsAnEmptyDatabase` |
| 2 | ✅ | A Reset of a truncated file rebuilds an empty database | Unit test | `DatabaseInitializerTests.ResetAsync_TruncatedDatabase_RebuildsAnEmptyDatabase` |
| 3 | ✅ | A file corrupt beyond its header is set aside too, and nothing of it is kept as a backup | Unit test | `DatabaseInitializerTests.ResetAsync_DatabaseCorruptBeyondItsHeader_SetsItAsideAndKeepsNoBackup` |
| 4 | ✅ | The unreadable file is moved into the reserved folder | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_MovesTheFileToTheReservedFolder` |
| 5 | ✅ | Its `-wal` and `-shm` move with it | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithSidecars_MovesTheSidecarsWithIt` |
| 6 | ✅ | An unreadable file is never treated as a backup | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_TakesNoBackup` |
| 7 | ✅ | The set-aside file is not listed as a backup | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_SetAsideFileIsNotListedAsABackup` |
| 8 | ✅ | A readable database still gets its backup first (control: green before and after) | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_StillTakesItsBackup` |
| 9 | ✅ | A readable database leaves nothing in the reserved folder (control: green before and after) | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_LeavesNoFileInTheReservedFolder` |
| 10 | ✅ | A connection opened before a readable Reset sees the rebuilt database (control: green before and after) | Unit test | `DatabaseInitializerTests.ResetAsync_HealthyDatabase_AConnectionOpenedBeforeTheResetSeesTheRebuiltDatabase` |
| 11 | ✅ | A failed backup of a readable database still refuses, and the override still works (control: green before and after) | Unit test | `dotnet test tests/Quotinator.Core.Tests --configuration Release --filter "FullyQualifiedName~DatabaseInitializerTests.ResetAsync"`: every existing #348 refusal and override test passes |
| 12 | ✅ | A failed rebuild of a readable database leaves it as it was, and the exception reaches the caller (control: green before and after) | Unit test | `DatabaseInitializerOwnershipTests.ResetAsync_MigrationFailsDuringReplay_LeavesTheDatabaseUntouchedAndRethrows`, renamed from `..._RestoresPreResetBackupAndRethrows` (step 2) |
| 13 | ✅ | A failed rebuild of an unreadable file leaves that file in place, with nothing moved, and the exception reaches the caller | Unit test | `DatabaseInitializerOwnershipTests.ResetAsync_CorruptDatabase_FailedRebuild_LeavesTheFileInPlace` |
| 14 | ✅ | Files in subfolders of the backup folder count towards storage | Unit test | `BackupStorageBudgetTests.UsedBytes_FileInASubfolder_IsCounted` |
| 15 | ✅ | The set-aside file counts from the moment it arrives | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_SetAsideFileCountsTowardsBackupStorage` |
| 16 | ✅ | A full backup folder does not stop the Reset of an unreadable file | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithAFullBackupFolder_StillRebuilds` |
| 17 | ✅ | A full disk does not stop the Reset of an unreadable file | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabaseWithNoFreeDiskSpace_StillRebuilds` |
| 18 | ✅ | Two unreadable files never overwrite each other | Unit test | `DatabaseInitializerTests.ResetAsync_TwoCorruptDatabases_KeepsBothFiles` |
| 19 | ✅ | An unwritable data directory still refuses, naming its obstacle, with the file untouched | Live (Docker) | `backup/09-a-reset-of-an-unreadable-database-refuses-when-the-data-directory-cannot-be-written.md`, every step exits `0` |
| 20 | ✅ | `preserveSchemaVersion=true` on an unreadable file proceeds with the baseline's own version rows | Unit test | `DatabaseInitializerTests.ResetAsync_CorruptDatabase_PreserveSchemaVersion_ProceedsWithTheBaselineVersionRows` |
| 21 | ✅ | The new payload kind is accepted by the baseline and the incremental replay alike | Unit test | `DatabaseInitializerOwnershipTests.NotificationMetadataKind_IsAcceptedByTheBaseline` and `.NotificationMetadataKind_IsAcceptedByTheIncrementalReplay`, which enumerate the enum and so cover the new member without an edit, red between declaring it and widening the `CHECK` (step 8); `.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` stays green |
| 22 | ✅ | A Reset of an unreadable file through the endpoint returns the application to healthy | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetRecoversToHealthy` |
| 23 | ✅ | The Reset leaves a notification naming the set-aside file, written after the wipe, with its text stored in every language | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetLeavesANotificationNamingTheSetAsideFile` |
| 24 | ✅ | The degraded page's repair action recovers an unreadable file too | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_RepairActionRecoversToHealthy` |
| 25 | ✅ | The endpoint answers the new refusal as `409`, naming its obstacle, and leaves the unreadable file where it was | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_ResetWhenTheSetAsideFolderCannotBeCreated_Returns409NamingTheObstacle`, against the real initializer: a file occupying the reserved folder's name blocks it deterministically on every platform. `ResetDatabase_WhenTheSourceIsUnreadable_DoesNotOfferTheOverride` and `.ResetDatabase_WhenTheSourceIsUnreadable_OffersReplacingTheFile` are removed, since a Reset can no longer produce that refusal; row 22 asserts what replaces it |
| 26 | ✅ | The `SourceUnreadable` guidance names a Reset, and still offers no override | Unit test | `BackupObstacleGuidanceTests.SourceUnreadable_OffersAReset`; `.SourceUnreadable_DoesNotOfferTheOverride` stays green |
| 27 | ✅ | Every new refusal obstacle has a cause and at least one remedy | Unit test | `ResetObstacleGuidanceTests.EveryObstacle_HasACauseAndARemedy`, written with the enum it enumerates (step 3) |
| 28 | ✅ | No document or comment describes a Reset as dropping tables, or an unreadable file as unresettable | Unit test | `RepositoryStructureTests.NothingDescribesAResetAsDroppingTables`, searching `CLAUDE.md`, `docs/` and `addon/` Markdown, and every `.cs` file under `src/` and `tests/`, excluding milestone plan docs and released changelogs, which record their own moment |
| 29 | ✅ | The `CLAUDE.md` passages on Reset, migration recovery and backups point to ADRs 027, 025 and 014 instead of restating them, and the pointer principle is written down | Unit test | `RepositoryStructureTests.ClaudeMdResetPassagesPointToTheirAdrs`, one row per passage: it links its ADR and carries none of that ADR's working terms (the audit-trail row is green today and guards it); `RepositoryStructureTests.ThePointerPrincipleIsWrittenDown`, for `CLAUDE.md` and the ADR README |
| 30 | ✅ | ADR 027 exists and is indexed (control: green before and after, a guard for the ADR this issue adds) | Unit test | `RepositoryStructureTests.EveryAdrIsIndexed`: every numbered file in `docs/architecture-decisions/` has a row in the README index and is listed in `Quotinator.slnx`. No such guard existed before (searched 2026-11-09) |
| 31 | ✅ | On a real volume, a Reset of a non-database file recovers, sets it aside and notifies | Live (Docker) | `backup/01-a-reset-sets-aside-a-database-it-cannot-read.md`, every step exits `0` |
| 32 | ✅ | The same for a truncated file | Live (Docker) | `backup/02-a-reset-sets-aside-a-truncated-database.md`, every step exits `0` |
| 33 | ✅ | A degraded start on a non-database file is recovered by the Reset its reason names | Live (Docker) | `startup-and-degradation/08`, every step exits `0`, including the new recovery step |
| 34 | ✅ | The same for a truncated file | Live (Docker) | `startup-and-degradation/09`, every step exits `0`, including the new recovery step |
| 35 | ✅ | A Reset of a readable database is still a full wipe (control: green before and after) | Live (Docker) | `database-lifecycle/03-reset-is-a-full-wipe.md`, every step exits `0` except step 8, which is #468 as the document states; steps 9 to 12, the Reset itself, all `0` |
| 36 | ❌ | The degraded page's *Reset the database* recovers the application and shows the notification | Manual | Step 13: the developer confirms the page returns healthy and the Notifications page lists the set-aside notification |
| 37 | ✅ | Every test step 2 classified as *Unchanged*, *Changes* or *Extended* passes, and the full suite is clean | Build | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: `0 Warning(s)  0 Error(s)`, all tests pass |
| 38 | ✅ | `capture-page.csx`, which verifies this issue, never waits forever and leaves nothing in the container log | Live (Docker) | 30 captures against a fresh container, all written, 9 after a fresh browser, `[Runtime - Exception]` count `0`; then `notifications-and-changelog/01` and `14` with every `[Runtime - Exception]` check `0` |