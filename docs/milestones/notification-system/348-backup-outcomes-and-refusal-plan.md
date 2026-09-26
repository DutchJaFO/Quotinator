# #348: Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable

**Status:** Waiting for release
**GitHub issue:** #348
**Tiers required:** T1, T2
**Depends on:** #349

---

## Description

A backup exists to make a startup or a destructive admin action safe. When one could not be taken, the
application either proceeded silently without one or threw an undifferentiated exception that reached
the caller as an unhandled `500`, and neither said *which* of five obstacles occurred, although each has
a different remedy. The sharpest symptom: on a corrupt or truncated database `/health` told the operator
to run a Reset, and that exact call returned `500`.

Found by [#327](https://github.com/DutchJaFO/Quotinator/issues/327) while measuring whether a stated
recovery route can actually succeed, not merely whether it is reachable.

## Next action

**None until the release.** Every step is done and every verification row is ✅; the issue closes when a
tag ships it, per `docs/workflow/issue-closure.md`. Steps 1 to 6 are the first pass (reached `Waiting for
release` 2026-08-28). The issue was reopened 2026-09-26 when a re-verification against the issue's own
requirements found three of them unmet in the code; steps 7 to 18 closed them.

---

## Design

### The five variants, plus one

Read off `CreateBackup`'s control flow, not from the two that happened to surface first.

| Outcome | Cause | Resolvable inside the application? |
|---|---|---|
| `BudgetExceeded` | The backups folder would exceed its quota | Yes: remove old backups (#349's endpoints) |
| `InsufficientDiskSpace` | The volume has too little free space | Partly: removing backups reclaims some |
| `DestinationDirectoryNotWritable` | The backups folder cannot be created | No: restore write access outside the app |
| `DestinationFileNotWritable` | A file cannot be created in the backups folder | No: same remedy |
| `SourceUnreadable` | The database file itself cannot be read | No: no backup of it is possible by any means |
| `DiskFilledDuringBackup` | The volume filled while the copy ran | No: free space outside the app |

Anything unrecognised is `Unclassified`, carrying the underlying error: an unnamed variant is an
unanswered question, and a wrong name is worse than no name.

### Attribution is structural

The three statements the old `try` wrapped are attempted one at a time, so the failing statement names
the fault. By the time the copy runs, the destination is proven creatable and openable, which is what
makes a failure there the source's. A disk that fills during the copy fails at the same statement as an
unreadable source, and splits on the typed `SqliteException.SqliteErrorCode`, never a parsed message.

### Quota: two levels, not a prediction

`CreateBackup` used to decide yes or no from the source file's length, which only approximates what a
page copy produces.

| Level | Value | Meaning |
|---|---|---|
| Operating quota | `BackupQuotaPercent` of the budget, default 90% | What normal operation uses |
| Absolute ceiling | `MaxBackupStorageGb` | Never exceeded |

The reserve between them lets an operator at the normal quota take one more backup at the moment it is
needed most. Reaching into it takes the override, never a default. An out-of-range percentage is
reported loudly and the default used: not a silent clamp, and not a crash.

### Check, act, still catch

Developer direction, 2026-08-27: exceptions only where nothing else can detect the condition; anything
else is a response declaring success or failure and the reason. So a backup is **checked** first
(`CheckBackupReadiness`, no exception), then **attempted** (`CreateBackup` returns a result), and
exceptions are still **caught** as the backstop for what the check cannot see. A `200` means the
endpoint did what was asked; any other status carries the reason and the remedies, if any.

### Every guarded step refuses the same way (settled 2026-09-26)

Three steps write to the database and must never run without a backup unless the operator overrides:
the **migration** at startup, the **content load** at startup, and **Reset**. Each checks, refuses with
the obstacle, and leaves the database exactly as it was. `DatabaseOperationResult` says which step
refused, so a caller can tell a migration refusal from a content-load refusal.

### Where a refusal is reported (developer decision, 2026-09-26)

| Refused step | Schema afterwards | Reported by |
|---|---|---|
| Migration at startup | Behind the build | `/health` 503 and the degraded pages, naming the variant, its remedies, and the Knowledgebase entry. No notification: writing a row into a table the build has not migrated is the unprotected write the refusal exists to prevent |
| Content load at startup | Intact | An `ActionRequired` notification offering only the options that can run now, and linking the Knowledgebase entry for the ones that cannot |
| Reset | Intact | The `409` response, as before |

### A reseed offered to the user backs up first (developer direction, 2026-09-26)

A function does one thing (CLAUDE.md's side-effect policy), so `ReseedAsync` never takes a backup of its
own; a reseed that did would be deciding a second thing on the caller's behalf. The caller that offers
the user a reseed composes the steps instead: **check** whether a backup can be taken, **back up**, then
**reseed**. When the check says a backup cannot be taken, the user is told why and asked for permission
before the reseed runs without one, so they can choose to resolve the obstacle first.

That caller is the notification executor's `Reseed` action, which serves both the backup-refusal
notification and #304's reseed recommendation. Today it runs the reseed alone; step 12 makes it compose
the three steps.

### The notification's options (developer decision, 2026-09-26)

Each is offered only when the pre-flight check, asked at render time, says it can complete now. That is
requirement 7: the UI asks before it offers, rather than presenting an option that will then refuse.

| Option | Offered when | What it does |
|---|---|---|
| Back up, then reseed | `CheckBackupReadiness()` now reports `Succeeded` | Takes a backup; reseeds only if it succeeded |
| Remove the oldest backup, then back up and reseed | The obstacle is `BudgetExceeded` or `InsufficientDiskSpace`, a backup exists, and removing the oldest one would make the check succeed | Deletes that one backup through #349's audited delete path, then backs up and reseeds |
| Reseed without a backup | The check reports an obstacle the reseed can complete without: never `SourceUnreadable` or `DiskFilledDuringBackup` | Asks for permission first, naming the obstacle and linking the Knowledgebase entry; on permission, reseeds and records the missing backup in the log and the audit trail |

The refusal notification is resolved by content arriving, which is exactly what the existing `Reseed`
dismiss trigger means, so it reuses that trigger. The one new kind is a `BackupRefused` payload (the
obstacle and the refused step), with its CHECK-widening migration and baseline update per ADR 008 (new
kinds added where the design needs them, developer, 2026-09-26).

### The Knowledgebase explains what the notification cannot offer (developer decision, 2026-09-26)

One entry, *Content was not loaded, or a migration did not run, because no backup could be taken*, with
a section per outcome: which options are blocked for it, why, and the manual steps that resolve it. The
notification and the degraded Home page link to it; a link leaving the app opens in a new tab, per
CLAUDE.md's external-link rule.

---

## Steps

### 1. Write the verification checklist
**Status:** ✅ Done

### 2. Write the red tests
**Status:** ✅ Done

Written after the implementation they test, which this plan recorded at the time. Step 7 replaces the
mutation evidence gathered then with a red run, as `docs/testing-policy.md` requires.

### 3. Finish the outcome type and the call sites
**Status:** ✅ Done

`BackupOutcome`, `DatabaseBackupResult`, and `CreateBackup`'s structural attribution.

### 4. Reset refusal, override, logging and audit
**Status:** ✅ Done

The `409` with cause and remedies, the override, and `AuditOperation.BackupSkipped`. The first live pass
found two further defects the unit tests could not see: a Reset that dropped every table after a backup
abandoned mid-copy and returned `200`, and a pre-flight that trusted `Directory.CreateDirectory` on a
read-only mount. Both are fixed; `docs/automated-testing/backup/01` to `04` hold them.

### 5. The quota model
**Status:** ✅ Done

### 6. Remedy text per variant
**Status:** ✅ Done

`BackupObstacleGuidance`, in the Api layer so `Quotinator.Data` stays domain-agnostic per ADR 004.

### 7. One statement per test, and every test red first
**Status:** ✅ Done

Found 2026-09-26: steps 2 to 6 were written implementation-first, and their evidence is mutation, which
`docs/testing-policy.md` calls the recovery, not a substitute. And 28 of #348's 34 tests asserted more
than one statement, so a failure could not say which statement failed (developer, 2026-09-26: the Single
Responsibility Principle, applied to tests).

**The rules applied to each test:**

1. It asserts one statement. A test asserting several is split into one test per statement. An
   integration test may keep a precondition guard (the refusal helper's "the response is a 409") only as
   a separately messaged assertion, so its failure is still traceable to one thing.
2. It fails, on an assertion, against the state before the change it verifies.
3. A test that cannot fail against any state of this issue is flawed, or its statement is. It is fixed
   so it can fail, or deleted; never kept as a control that cannot fail.

**The red states.** A test is red against the state before the change it verifies, so there is no single
state:

| State | What it is | Used by |
|---|---|---|
| A | Every new #348 behaviour at its default: the check answers `Succeeded`, `CreateBackup` returns a default result and writes nothing, `ResetAsync` runs without its refusal, the quota percentage has no default and the limit is the ceiling, the out-of-range warning is silent, the endpoint neither forwards the override nor maps a refusal nor writes the skip, and the guidance returns nothing | Every test not listed below |
| B | Only the check at its default; the attempt real | The two "check agrees with the attempt" tests |
| C | The guidance as `7c7aebbe` wrote it, offering the override for an unreadable source | Both "unreadable source does not offer the override" tests |
| D | The guidance ignoring `overrideAlreadyTried`, as before `76d654a5` | Both "override already tried is not repeated" tests |
| E | Only the guidance at its default; the endpoint's refusal mapping real | The four endpoint tests whose statement is the refusal body's text |

`ResetDatabase_WhenNoBackupCanBeTaken_NamesTheObstacle` is red only on its precondition guard: the
obstacle field and the `409` are one mapping, so no state has one without the other.

**Removed, each because no state of this issue can make it fail:**

- `ResetDatabase_WhenNoBackupCanBeTaken_DoesNotRebuildTheDatabase` asserted the spy's own refusal. The
  guarantee it named is `DatabaseBackupQuotaTests.ResetAsync_WhenNoBackupCanBeTaken_NeverReachesTheDestructiveStep`,
  at the layer that performs it.
- `ResetDatabase_WhenBackupSucceeds_IsUnchanged` asserted a `200` and a completed reset, which predate
  this issue, and no skip record, which a do-nothing also produces.
- `CanCreateBackup_WhenNothingObstructsIt_ReportsThatItCan`, `UsageBelowTheQuota_ReportsThatABackupCanBeTaken`
  and `UsageAtTheQuota_WithTheReserveAllowed_CanStillTakeABackup` asserted a "yes" that a check answering
  yes to everything also gives. The reserve's effect is held by
  `DatabaseBackupPreflightTests.CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed`.
- Statements dropped while splitting, for the same reason: a failed backup reports no path, a successful
  one reports `Succeeded` and no error, a no-backup startup writes no backup file, a reset with the
  override ran and returned `200`, an unreadable source is not offered backup removal (no version ever
  offered it), and the override-tried response still offers something (held by
  `BackupObstacleGuidanceTests.OverrideAlreadyTried_KeepsTheOtherRemedies`).

**Evidence, 2026-09-26.** First red run, before the split: 43 failed on assertions, 3 by exception (a
body read before its status was checked), 6 passed; that is what exposed the removals above. After the
split: state A 54 of 54 failed, B 2 of 2, C 2 of 2, D 2 of 2, E 4 of 4, every one on an assertion and
none passing. Restored byte for byte; green at 0 warnings. A first attempt at the green run was invalid:
restoring with `Copy-Item` kept the backups' old write times, so the build reused the red binaries. The
restore now stamps the files, and every red state was rerun from clean with the same result.

`DestinationFileNotWritable` and `DiskFilledDuringBackup` have no unit test of their attribution: neither
condition can be produced in-process without a platform-specific permission or a size-capped volume.
Both are proven by `docs/automated-testing/backup/04` and `backup/03`, which step 18 runs.

### 8. The migration step refuses without a backup
**Status:** ✅ Done

Found 2026-09-26: `ApplyMigrationsAsync` takes a backup, keeps only its path, and never checks whether it
succeeded, so a failed backup lets every pending migration run unprotected. Its restore handler is
filtered on that path, so a migration that then throws is not rolled back either.

`ApplyMigrationsAsync` now refuses before touching the schema when the backup fails, leaving both recorded
versions as they were, and `InitialiseAsync` returns that refusal without reaching the content load.
`DatabaseOperationResult.RefusedStep` names the refused step (`BackupGuardedStep`: `Migration`,
`ContentLoad`, `Reset`), and each refusal logs its own `[Database - Backup]` line.

Tests, one statement each, in `DatabaseInitializerTests`:
`InitialiseAsync_MigrationPendingWithNoBackupPossible_IsRefused`, `..._NamesTheObstacle`,
`..._NamesTheMigrationStep`, `..._AppliesNoMigration`, and
`InitialiseAsync_ContentLoadWithNoBackupPossible_NamesTheContentLoadStep`; in `DatabaseBackupQuotaTests`,
`ResetAsync_WhenNoBackupCanBeTaken_NamesTheResetStep`.

Red run, 2026-09-26, against the unfixed code with the step accepted but not recorded: all six failed on
assertions, after the fixture was corrected. Its first version started from an empty database, whose
content load *also* refused for want of a backup; that refusal made `IsRefused` and `NamesTheObstacle`
pass without the migration refusing at all. The fixture now loads content first, so the migration is the
only guarded step pending. Green: Data 1,390, Core 1,722, Api 1,048, 0 warnings.

The plan named a positive counterpart, "with a backup possible, the same database migrates". It is not
written: before this change migrations always ran, so no state of the issue can make it fail.

Sixteen log strings in the touched files lost their dashes (developer rule, 2026-09-26), so three
automated documents that quote them were updated to the new text:
`startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md`,
`02-startup-backup-gating-and-storage-budget.md` and `06-schema-version-ahead-of-the-application.md`. A
changed test is executed after the change, so step 18 runs all three.

### 9. Startup reports a refusal instead of discarding it
**Status:** ✅ Done

Found 2026-09-26: `Program.cs` awaits `InitialiseAsync()` and discards its result, so a refused content
load leaves the app reporting healthy with nothing loaded and nothing said. Its handler for
`DatabaseBackupWriteException` is dead code: nothing in `src/` throws that type any more.

`Program.cs` now reads the result. A refused migration marks the database failed with
`BackupObstacleGuidance.MigrationRefusedReason`: the variant, its cause, its remedies, and
`KnowledgebaseLinks.NoBackupCouldBeTaken`, the one place the entry's address is held. The reason leaves
out the `allowNoBackup` remedy, which is a Reset parameter a startup migration does not have. The dead
handler and `DatabaseBackupWriteException` are removed.

Tests, one statement each, in `StartupBackupRefusalTests` through the real initializer and a real
database: a first start migrates and loads, the newest recorded migration is removed, and a second start
with a zero quota finds it pending. `Startup_MigrationRefusedForBackup_ReportsUnhealthy`,
`..._ReasonNamesTheObstacle`, `..._ReasonCarriesTheRemedies` and `..._ReasonLinksTheKnowledgebaseEntry`.
In `BackupObstacleGuidanceTests`, `MigrationRefusedReason_DoesNotOfferTheOverride`.

Red, 2026-09-26: the four startup tests against `Program.cs` discarding the result, all failing on
assertions. The guidance test against the reason's first draft, which joined the Reset remedies whole,
failing on its assertion. Green: Data 1,390, Api 1,053, 0 warnings.

Not written, each because no state of this issue can make it fail: the healthy counterpart (startup with
a backup possible was already healthy), and the content-load half, whose "stays healthy" was already true.
What a refused content load *raises* is step 10's, and its test lives there.

The rewritten startup file lost its dashes (developer rule, 2026-09-26), and one log line it emits is
quoted by `docs/knowledgebase/whats-new-notification-fails-to-seed-after-a-reset.md`, which was updated
and cleaned. `startup-and-degradation/05` quotes the data-directory reason as it was measured on
2026-08-27, so that quote stays. One dash is left in `Program.cs` on purpose, awaiting a decision: the
#279 announcement body, whose content hash covers its text, so rewording it would announce a v1.8.3
rename again on every existing installation.

### 10. The refused content load raises a notification
**Status:** ✅ Done

`NotificationMetadataKind.BackupRefused`, whose payload `BackupRefusedMetadataDto` carries the refused
step and the obstacle, both stored by name and both part of its identity. Data-owned migration 23 widens
the `MetadataKind` CHECK by rebuilding `System_Notification`, and the baseline matches. The notification
is `ActionRequired`, carries the existing `Reseed` dismiss trigger, has its title and body in all three
languages, and is deduplicated against active rows while unresolved.

It is written from inside the machinery that refused, per ADR 018: `DatabaseInitializer` calls a new
`OnContentLoadRefusedAsync(obstacle)` hook when it refuses the content load, and
`QuotinatorDatabaseInitializer` implements it. Not from `Program.cs`, which only sees the result
afterwards.

Tests, one statement each. In `DatabaseInitializerTests`:
`InitialiseAsync_ContentLoadWithNoBackupPossible_RaisesABackupRefusedNotification`,
`..._TheNotificationRequiresAction`, `..._TheNotificationClearsOnReseed`,
`..._TheNotificationNamesTheObstacle`, `..._TheNotificationNamesTheContentLoadStep`, and
`InitialiseAsync_ContentLoadRefusedOnTwoStarts_RaisesOneNotification`. In
`DatabaseInitializerOwnershipTests`, `NotificationMetadataKind_IsAcceptedByTheBaseline` and
`..._IsAcceptedByTheIncrementalReplay`, one case per enum member, so the next kind added without its
migration fails there.

Red, 2026-09-26, against the signature state (the kind, its payload type and a hook that does nothing, no
migration): the six notification tests and the two `BackupRefused` storage cases failed on assertions.
Four of the six first failed by exception, reading a single row from an empty set; they now assert over
the set, and failed again on assertions. The dedupe test was also run against a payload whose identity
never matches itself, and failed on its assertion. Green: Data 1,404, Core 1,728, Api 1,053, 0 warnings.

Not written: "no notification when a backup is possible", which no state of this issue can make fail.

Three existing guards required the new kind to be declared: `NotificationTableTests` (it renders no
detail table; its options and link are step 15's) and document
`notifications-and-changelog/14`, which names every kind. That document cannot produce `BackupRefused`,
whose trigger loads no content, so it names the issue whose document will; step 16 replaces that with a
link once the document exists. The touched files, including the three UI string files, lost their
dashes, except two kept on purpose: `NotificationTableTests` asserts the table's own "no value"
placeholder, an em dash rendered by `NotificationTable`, which step 15 touches; and the #279 announcement
body's translations, held with its English for the same decision as step 9's.

### 11. Offer only the options that can run
**Status:** ✅ Done

`NotificationActionOption` names each option: `BackUpThenReseed`, `RemoveOldestBackupThenReseed` and
`ReseedWithoutBackup` for a reseed, `ResetDatabase` for a reset, `KeepExisting` and `TakeIncoming` for an
import review. `INotificationActionExecutor.AvailableOptions` lists those that can run now, and
`CanExecute` is now "any option is available", so a row with none offers no action.

`NotificationActionAvailability` gains the pre-flight answer and the answer once the oldest backup is
removed, both read once per render by `GetAvailabilityAsync`. The second comes from
`CheckBackupReadiness(bytesFreedFirst:)`, a new parameter answering with the same comparisons as the
real check, so offering a removal cannot drift from what the backup will then find. The plan also named
the answer "with the reserve"; it is not read, because no option in *Design* uses it.

The rules, per *Design*: with a backup possible only `BackUpThenReseed` is offered, since the other two
would each give up a restore point for nothing. Otherwise `RemoveOldestBackupThenReseed` when removing the
oldest backup would clear the way, and `ReseedWithoutBackup` for each obstacle the pre-flight reports.
Those are exactly the obstacles a reseed can complete despite: an unreadable source, a disk that filled
mid-copy and an unrecognised failure are only reported by an attempt. `ResetDatabase` is offered only
with a backup possible, since a Reset refuses otherwise.

`GET /api/v1/notifications` publishes the list as `availableActions`, lowercase, read once per page, and
empty for a dismissed notification, as the page withholds its controls.

Tests, one statement each. In `NotificationActionExecutorTests`, every option offered and withheld:
`BackUpThenReseed_IsOffered_WhenABackupCanBeTaken`, `..._IsWithheld_WhenABackupCannotBeTaken` (per
obstacle), `RemoveOldestBackupThenReseed_IsOffered_WhenRemovingTheOldestClearsTheObstacle` (per
obstacle), `..._IsWithheld_WhenRemovingTheOldestWouldNotClearIt`, `..._WhenThereIsNoBackupToRemove`,
`..._WhenABackupCanAlreadyBeTaken`, `ReseedWithoutBackup_IsOffered_WhenABackupCannotBeTaken` (per
obstacle), `..._IsWithheld_WhenABackupCanBeTaken`, `ResetDatabase_IsOffered_WhenABackupCanBeTaken`,
`..._IsWithheld_WhenABackupCannotBeTaken` (per obstacle), `ImportReviewOption_IsOffered_WhileItsBatchExists`
and `..._IsWithheld_OnceItsBatchIsGone` (per option), `GetAvailabilityAsync_ReportsWhetherABackupCanBeTakenNow`
and `..._WeighsRemovingTheOldestBackup`. In `NotificationEndpointsTests`,
`GetNotifications_ListsTheOptionsTheExecutorOffers` and `..._DismissedNotification_ListsNoOptions`. In
`DatabaseBackupPreflightTests`, `CheckBackupReadiness_OverTheQuotaByLessThanWhatIsFreedFirst_ReportsSucceeded`,
`..._OverTheQuotaByMoreThanWhatIsFreedFirst_ReportsBudgetExceeded` and
`..._WithNoFreeDiskSpaceAndSpaceFreedFirst_ReportsSucceeded`.

Red, 2026-09-26, against three states. G, the signature state (no option offered, the parameter ignored,
the availability unchanged, the endpoint publishing nothing): every "offered" test, both availability
tests, the options endpoint test and the two "succeeds" pre-flight tests failed on assertions. F, every
option always offered, which is how the action behaved before this issue: all fourteen "withheld" tests
failed on assertions. H, any freed space clearing the quota: the "more than what is freed" test failed on
its assertion. The dismissed-notification test failed on its assertion against the endpoint before its
dismissal rule. Green: Data 1,411, Core 1,728, Api 1,084, 0 warnings.

`CanExecute_TriggerWithNoVolatileDependency_IgnoresAvailability` is deleted: it stated that Reset and
Reseed depend on nothing volatile, which this step makes false, and the offered and withheld tests above
hold what replaced it.

**Found 2026-09-26:** the executor's `DatabaseReset` action ignores what `ResetAsync` returns, so a refused
Reset still marks the database healthy and dismisses its notification. Fixed in step 12, which reworks the
executor's actions.

**#279's announcement is reworded, through Data-owned migration 24.** Its body carried a dash, and its
content hash is part of its identity, so new text alone would announce it a second time on every
installation. Migration 24 rewrites the stored row's body, its `contentHash` (`6FC95BB0`) and its Dutch
and German translations to the producer's current text, identified by the payload's `announcement` key as
migration 14 does. The English body moved to `OperationIdRenameAnnouncement.Body`, so tests can hold the
producer to the migration. Tests: in `NotificationLegacyBackfillMigrationTests`,
`Migration24_V183Announcement_IsRecognisedByTheRewordedProducer`, `..._CarriesTheRewordedBody` and
`Migration24_V183AnnouncementTranslation_CarriesTheRewordedBody` (per language), through the real chain
from migration 8; in `OperationIdRenameAnnouncementTests`, `Migration24_WritesTheContentHashTheProducerComputes`,
`..._WritesTheBodyTheProducerWrites` and `..._WritesTheTranslationTheProducerWrites` (per language). All
eight failed on assertions with migration 24 a no-op. The tests that quote v1.8.3's body keep it verbatim,
as the text that release wrote.

### 12. The reseed action backs up first
**Status:** ✅ Done

The executor's `Reseed` action composes the steps from existing calls, adding none to
`IDatabaseInitializer`, and `ExecuteAsync` now takes the option and returns a `NotificationActionResult`,
so a refusal is stated rather than thrown (ADR 022):

- **Back up, then reseed** (also what no option means): reseeds only when the backup succeeded. A backup
  that fails after the check passed stops before the reseed, leaves the notification active, and reports
  the obstacle.
- **Remove the oldest backup, then back up and reseed:** removes the oldest backup and no other, then
  backs up and reseeds.
- **Reseed without a backup:** the option is the user's permission; the skipped backup is logged
  (`LogReseedProceedingWithoutBackup`) and recorded as `AuditOperation.BackupSkipped`.

Taking and removing a backup go through #349's audited path, moved out of the backup endpoints into
`BackupOperations` so the endpoints and the executor share one copy rather than two that could drift. The
endpoints behave as before, held by their existing tests (107 green after the move).

The Reset found in step 11 is fixed: a refused Reset leaves the database unhealthy and its notification
active, and reports the obstacle.

Tests, one statement each, in `NotificationActionExecutorTests`: `Reseed_BackUpThenReseed_TakesABackupBeforeReseeding`,
`..._WhenTheBackupFails_DoesNotReseed`, `..._WhenTheBackupFails_LeavesTheNotificationActive`,
`..._WhenTheBackupFails_ReportsTheObstacle`, `..._RecordsTheBackupInTheAuditTrail`,
`Reseed_WithNoOptionNamed_TakesABackupBeforeReseeding`, `Reseed_RemoveOldestBackupThenReseed_RemovesTheOldestBackup`,
`..._KeepsTheNewerBackup`, `..._RecordsTheRemovalInTheAuditTrail`, `..._TakesABackupBeforeReseeding`,
`Reseed_ReseedWithoutBackup_Reseeds`, `..._AttemptsNoBackup`, `..._RecordsTheSkippedBackupInTheAuditTrail`,
`..._LogsTheSkippedBackup`, `Reseed_RemoveOldestBackupThenReseed_WhenTheRemovalFails_DoesNotReseed`, `DatabaseReset_WhenRefused_LeavesTheDatabaseUnhealthy`,
`..._LeavesTheNotificationActive` and `..._ReportsTheObstacle`. #304's reseed recommendation uses the same
trigger, so `Reseed_WithNoOptionNamed_TakesABackupBeforeReseeding` is its extension; its existing tests
still pass.

Red, 2026-09-26. Against the signature state (the new contract, the old behaviour: a reseed with no
backup, a Reset whose result is ignored), fourteen failed on assertions. The other three against the
defect each guards: `KeepsTheNewerBackup` against removing the newest backup instead of the oldest, and
`ReseedWithoutBackup_Reseeds` and `..._AttemptsNoBackup` against the option being ignored. All on
assertions. Green: Data 1,411, Core 1,728, Api 1,102, 0 warnings.

`Reseed_RemoveOldestBackupThenReseed_WhenTheRemovalFails_DoesNotReseed` holds the refusal when the oldest
backup cannot be removed, through a writer that refuses as a read-only folder does. Red against an
executor that ignores the removal's outcome, on its assertion.
### 13. Test the skip log line
**Status:** ✅ Done

Found 2026-09-26: requirement 5 asks for a log line **and** an audit entry. The audit entry is tested;
`LogResetProceedingWithoutBackup` is not, although the issue named that test.

Tests, one statement each, in `DatabaseBackupQuotaTests`: `ResetAsync_WithTheOverride_LogsTheSkippedBackup`
and `ResetAsync_WhenTheBackupSucceeds_LogsNoSkippedBackup`. The log call already existed, so each is red
against the defect it guards: the first against the call removed, the second against the call made
unconditionally. Both failed on their assertions. Green: Data 1,413, 0 warnings.

### 14. Write the Knowledgebase entry
**Status:** ✅ Done

`docs/knowledgebase/no-backup-could-be-taken.md`, from `entry-template.md`, listed in the folder's
README. Its symptoms quote the notification, the `503` reason and the `409` detail verbatim; a section
per `BackupOutcome` names the blocked options, why each is blocked, and the manual resolution. Held by a
repository test: every `BackupOutcome` member has its section, and the link the application renders
names a file that exists.

Written, listed in the folder's README and in `Quotinator.slnx`. The Reset refusal's title is quoted as
`AdminEndpoints` sends it, dash included: a symptom is quoted exactly so a search finds it, and this
issue does not change that response.

Tests, one statement each, in `RepositoryStructureTests.Knowledgebase.cs`:
`KnowledgebaseLink_NamesAnEntryThatExists`, per address `KnowledgebaseLinks` holds, and
`NoBackupCouldBeTaken_HasASectionForTheObstacle`, per obstacle. Red, 2026-09-26, on assertions: the first
with the entry absent, the second with the entry as the template leaves it.

### 15. Render the options and the Knowledgebase link
**Status:** ✅ Done

`NotificationTable` renders each option a reseed row offers as its own button, and none it withholds; an
import review keeps its own two-outcome control. Choosing an option shows a confirmation before anything
runs. For *Reseed without a backup* that confirmation names the obstacle and links the Knowledgebase
entry; the others show the existing warning. A `BackupRefused` row links the entry in its message cell, so
the read-only startup popup shows it too, and `StartupErrorModal` links it when the degraded reason names
it. Every link leaves the app, so each opens in a new tab. The Notifications page runs the chosen option
through `ExecuteAsync(..., option:)`. Labels and the confirmation text are in all three languages.

This project has no bUnit, so each decision the markup renders sits in a static helper the tests reach:
`OptionButtons`, `OptionLabelKeyFor`, `AsksPermission`, `PermissionText` and `KnowledgebaseLinkFor` on
`NotificationTable`, and `KnowledgebaseLinkIn` on `StartupErrorModal`. That the markup renders from them
is step 16's to show, with screenshots.

Tests, one statement each. In `NotificationTableTests`: `ReseedRow_RendersEveryOptionTheExecutorOffers`,
`ReseedRow_RendersNoOptionTheExecutorWithholds`, `ImportReviewRow_RendersNoOptionButtons`,
`ReseedOption_HasItsOwnLabel` (per option), `ReseedWithoutBackup_AsksPermission`,
`OptionThatBacksUp_DoesNotAskPermission` (per option), `PermissionText_NamesTheObstacle`,
`BackupRefusedRow_LinksTheKnowledgebaseEntry` and `ReseedRecommendedRow_LinksNoKnowledgebaseEntry`. In
`StartupErrorModalTests`: `RefusedMigrationReason_LinksTheKnowledgebaseEntry`,
`ReasonNamingNoEntry_LinksNothing` and `NoReason_LinksNothing`.

Red, 2026-09-26, on assertions. Against the signature state (no option, an empty label key, no
permission asked, no text, no link), every test stating what is shown failed. Against every option
rendered for every row, permission always asked and every row and reason linked, every test stating what
is withheld failed. Green: Api 1,125, 0 warnings.

The table's "no value" placeholder for an unknown type now uses the shared `LocalTimestamp.Absent` rather
than repeating it as a literal, so the table shows exactly what it did. One dash is left on purpose:
`Notifications.razor`'s page title, `Notifications — Quotinator`. Four other pages share that pattern, and
changing one would leave the titles inconsistent, so it waits on a decision about all five.

### 16. Automated (T2) documents, red first
**Status:** ✅ Done

`backup/06-a-startup-that-cannot-take-a-backup-loads-nothing.md` and
`backup/07-a-migration-that-cannot-take-a-backup-leaves-the-database-unmigrated.md`, both indexed and in
`Quotinator.slnx`. `notifications-and-changelog/14` now links the first from its `BackupRefused`
paragraph. The obstacle in both is a full budget rather than a zero quota: `MaxBackupStorageGb` is whole
gigabytes, and a sparse filler the size of the budget is the same condition an installation reaches on
its own.

The first document drives each of the three options through the page, and proves the remedy by removing
the filler by hand, after which only *Back up, then reseed* is offered and it loads the content. The
second upgrades from the published `1.8.3`, so the current build has real migrations pending.

**Canary**, run first against `4cfe1006` (the commit before step 8) as `quotinator:canary348`. The first
document fails at step 3 (no notification) and step 4 (no row, no link); its step 2 also fails on the log
line's wording alone. The second fails at steps 2 to 5: the start was healthy, the migration ran with no
backup (`5` to `9`), and nothing was left for the remedied start to back up. Both then pass in full
against `quotinator:local`, with no `[Runtime - Exception]` line at any stop. Screenshots checked by eye.

**Two application defects found by these runs, both fixed and held by tests red first:**

- **The obstacle was logged quoted**, `("BudgetExceeded")`. Serilog quotes a string property unless the
  template marks it literal, and the unit tests read the lines through a logger that does not, so they
  passed. The five lines naming an obstacle now use `{Obstacle:l}`, held by
  `BackupObstacleLogRenderingTests.ObstacleLine_RendersTheObstacleUnquoted` (per line), which renders
  through Serilog as `docs/logging.md` requires; `Reseed_ReseedWithoutBackup_LogsTheSkippedBackup` now
  renders through Serilog too. All six failed on assertions against the quoted templates.
  `Quotinator.Api.Tests` is granted Data's internals for this, beside Core.Tests and Data.Tests.
- **A reseed resolved its notification while it was still running.** Seeding applies one batch per
  file, and each successful batch dismissed every `Reseed`-trigger notification, so the notification read
  as resolved after the first file, with `13` of `795` quotes loaded. A notification is updated after
  the action that settles it has run, never during it (developer, 2026-09-26). A seed run no longer
  dismisses anything; its owner does once it has returned: the notification's action and the admin
  reseed endpoint already did, and a startup that loaded content now does
  (`ResolveReseedIfContentLoadedAsync`). Held by
  `DatabaseInitializerTests.ReseedAsync_LeavesTheRefusalForItsCallerToResolve` and
  `InitialiseAsync_ContentLoadedAfterAnEarlierRefusal_ResolvesTheRefusal`. No unit test had seen it,
  because the test fixture built the import path with a notification writer that did nothing; it now
  uses the real one, and the first test failed on its assertion against the unfixed code. The second
  failed on its assertion once the seed's dismissal was removed and before the startup's was added.

**Faults found in the documents themselves**, fixed and recorded in each *Determinism*: a one-element
list read without `@(...)`, an action waited on through something other than its own return, a leftover
filler that made the next cycle's Reset refuse, and a version read through `execute-sql.csx`, which
prints a row count rather than a value. An option is now waited on by the page's own running state, which
lasts exactly as long as the executor call.

**Two observations recorded in the documents, not asserted, and not changed by this issue:**

- A startup's backups (before loading content, before a migration) are measured against the absolute
  ceiling, not the 90% operating quota, while the notification's options consult the quota. Between 90%
  and 100% the two disagree: the startup proceeds into the reserve. The documents fill to the ceiling,
  where both refuse.
- The degraded popup's *last known-good state* counts read zero after a refused migration, although the
  database holds content: the counts are read only once initialisation completes. The same zeros appear in
  `startup-and-degradation/05`, so this predates #348.

### 17. Documentation
**Status:** ✅ Done

`docs/api-endpoints.md` and the endpoint's description state `availableActions`. The changelog's
unreleased section carries #348's new entries in all three languages: a highlight, what was added (the
refusal notification and its options, `availableActions`), what changed (the reworded announcement), and
what was fixed (the unprotected migration, the reseed resolving its notification early, the refused
Reset from a notification, and the quoted obstacle in the log). `CHANGELOG.md` is regenerated; the add-on
changelogs are regenerated only after a tag, per #236, and never show the unreleased section.

Both touched documents lost their dashes: the endpoint reference entirely, and the changelog's unreleased
section in all three languages. The changelogs' released history keeps its dashes for now: a shipped
release's notes are a record of what shipped, and rewriting them waits on the developer's decision.


### 18. Full verification
**Status:** ✅ Done

Build clean; the full solution suite green across three consecutive `-m:1` runs; the T2 smoke set plus
every `backup/` document, `startup-and-degradation/05`, and the notification documents the new kind
touches.

**Build and suite, 2026-09-26:** `dotnet build --configuration Release` at 0 warnings, 0 errors, then
three consecutive `-m:1` runs, each 4,381 tests passed across 11 projects and 0 warnings. The T2
documents below were then corrected, and `RepositoryStructureTests` reads those documents, so a fourth
run followed against their final text: 4,381 passed, 0 failed, 0 warnings.

**T2, against `quotinator:local` built from this branch.** Every step a document states as a browser
action ran unattended through `capture-page.csx`, and startup/05's console assertion through the browser
pane's console read. Green:

- the smoke set, except import/14 below;
- `backup/01`, `02`, `04`, `05`, `06`, `07`;
- `startup-and-degradation/05`: `/` names the fault with no stack trace, `/stats` renders all ten counts,
  `/notifications` is empty, and the console holds six errors, every one a `503`;
- `notifications-and-changelog/12`: the row reads *Running…* with a running animation and no controls, one
  reseed request, the alert `resolved` with the full `795` quotes already loaded the moment it closed, and
  a restart mid-run strands nothing;
- `notifications-and-changelog/13` and `14`: every kind on both surfaces, *Back up, then reseed* on the
  reseed recommendation, `thrown=0` at every stop.

**Faults found in the documents, fixed and recorded in each:**

- notif/12 step 3 waited on the `reseed complete` log line, because the alert used to close one second
  into the run. Step 10's fix made the action the only thing that closes it, so the step now waits on the
  alert and reads the quote count at that moment: `795`, where the old behaviour gives `13`.
- notif/14's `Capture-Row` assigned its script fragment to `$expand`, which PowerShell resolves to the
  `[switch]$Expand` parameter: every capture failed, and each failure left four `WebSocketException`s for
  the next stop to count. Renamed to `$expandJs`; the stops then read `thrown=0`.
- notif/14 step 5 expected `rows: 8` where its own steps produce nine.
- notif/14 step 8 listed notifications once, the moment health answered, although the what's-new
  producer runs detached; it now polls for that row, bounded at 30 s.

**Failing, and not caused by this issue**, each run against `quotinator:canary348` (the build before
#348's changes) to establish that:

- import/14 step 4 reports `12` undeclared date variants on both builds, the same six titles.
- import/14 step 5 is already recorded in that document as unable to run until #400.
- backup/03 step 1 reads the disk immediately after `--wait-listening`, so it measures a seed still in
  progress: `3.3M` free on the pre-#348 build and `824K` on this one, in consecutive runs. Its expectation
  holds only when the read lands after the seed.

T1 (row 28) passed on the developer's own run.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every backup attempt reports which obstacle it hit | Unit test | `DatabaseBackupOutcomeTests`: one `*_IsReportedAs*` test per obstacle it can produce in-process, with `SucceedingBackup_ReportsWhereItWrote` and `...WritesTheFileItReports`; `DestinationFileNotWritable` and `DiskFilledDuringBackup` by `backup/04` and `backup/03` |
| 2 | ✅ | An unrecognised failure reports as `Unclassified` and carries the underlying error | Unit test | `DatabaseBackupOutcomeTests.CopyFailureThatIsNotASqliteError_IsReportedAsUnclassified` and `..._CarriesTheUnderlyingError` |
| 3 | ✅ | The content load at startup refuses rather than proceeding unprotected, naming the variant | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentLoadWithNoBackupPossible_IsRefused`, `..._NamesTheObstacle`, `..._LoadsNothing`, and `...ContentLoadWithAnUnwritableBackupDestination_NamesTheObstacle` |
| 4 | ✅ | Reset refuses with a stated failure, never an unhandled 500, and does not rebuild | Unit test | `AdminEndpointsTests.ResetDatabase_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500`, `..._NamesTheObstacle`, `..._DescribesTheCause`, `..._OffersARemedy`; `DatabaseBackupQuotaTests.ResetAsync_WhenNoBackupCanBeTaken_Refuses`, `..._NamesTheObstacle`, `..._NeverReachesTheDestructiveStep` |
| 5 | ✅ | The override is offered and forwarded only where the action can complete without a backup | Unit test | `AdminEndpointsTests.ResetDatabase_WhenTheQuotaIsFull_OffersTheOverride`, `...WhenTheSourceIsUnreadable_DoesNotOfferTheOverride`, `...WhenTheSourceIsUnreadable_OffersReplacingTheFile`, `...WhenTheOverrideWasTriedAndStillRefused_DoesNotOfferItAgain`, `...WithOverride_ForwardsTheOverride`; `DatabaseBackupQuotaTests.ResetAsync_WithTheOverride_ReportsThatTheBackupWasSkipped` |
| 6 | ✅ | A skipped backup is recorded in the log **and** the audit trail | Unit test | `AdminEndpointsTests.ResetDatabase_WithOverride_WritesAnAuditEntryRecordingTheSkip` (red in step 7); `DatabaseBackupQuotaTests.ResetAsync_WithTheOverride_LogsTheSkippedBackup` and `ResetAsync_WhenTheBackupSucceeds_LogsNoSkippedBackup`; a reseed without a backup by step 12's `..._RecordsTheSkippedBackupInTheAuditTrail` and `..._LogsTheSkippedBackup` |
| 7 | ✅ | Every #348 test asserts one statement, and each is red against the state before its change | Unit test | Step 7: states A to E, every test failing on an assertion; the tests that could not fail removed |
| 8 | ✅ | A caller can ask whether a backup is possible without attempting one, and the answer agrees with an attempt | Unit test | `DatabaseBackupPreflightTests.CheckBackupReadiness_WhenTheBudgetIsExhausted_ReportsBudgetExceeded`, `..._AgreesWithTheAttempt`, and the same pair for an unwritable destination |
| 9 | ✅ | The operating quota is honoured, the reserve is reachable only by override, the ceiling never is | Unit test | `DatabaseBackupQuotaTests.UsageAtTheQuota_IsRefused_WithoutReachingIntoTheReserve`, `...UsageAtTheAbsoluteCeiling_IsRefusedEvenWithTheReserveAllowed`; `DatabaseBackupPreflightTests.CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed` |
| 10 | ✅ | The quota percentage is configurable and defaults to 90 | Unit test | `DatabaseBackupQuotaTests.ConfiguredQuotaPercent_IsTheLimitTheCheckRefusesOn` and `...QuotaPercent_DefaultsTo90` |
| 11 | ✅ | An out-of-range percentage is reported and the default used, never clamped, never fatal | Unit test | `DatabaseBackupQuotaTests.QuotaPercent_OutOfRange_UsesTheDefault` and `...QuotaPercent_OutOfRange_IsReported` |
| 12 | ✅ | Each variant states cause and remedy, and names no remedy that cannot work | Unit test | `BackupObstacleGuidanceTests`: `EveryObstacle_HasACause`, `EveryObstacle_HasARemedy`, `RecognisedObstacle_IsNotDescribedAsTheUnrecognisedFallback`, `BudgetExceeded_OffersTheOverride`, `BudgetExceeded_OffersRemovingBackupsThroughTheApplication`, `SourceUnreadable_DoesNotOfferTheOverride`, `OverrideAlreadyTried_DoesNotRepeatTheOverride`, `OverrideAlreadyTried_KeepsTheOtherRemedies` |
| 13 | ✅ | The two tests whose expectation changed were renamed to their new contract, not bent to pass | Unit test | Recorded in step 2: both renamed, each with a comment on what changed and why |
| 14 | ✅ | The corrupt and truncated databases are recoverable end to end | Automated (T2) | `backup/01` and `backup/02`, executed 2026-08-28, each ending with the remedy proven by a `200` |
| 15 | ✅ | A pending migration with no backup possible applies nothing and reports the obstacle and the step | Unit test | `DatabaseInitializerTests.InitialiseAsync_MigrationPendingWithNoBackupPossible_IsRefused`, `..._NamesTheObstacle`, `..._NamesTheMigrationStep`, `..._AppliesNoMigration`; the content-load and Reset steps by `...ContentLoadWithNoBackupPossible_NamesTheContentLoadStep` and `DatabaseBackupQuotaTests.ResetAsync_WhenNoBackupCanBeTaken_NamesTheResetStep` |
| 16 | ✅ | A startup migration refusal marks the database unhealthy, naming the variant, remedies and entry, and no remedy the startup cannot use | Unit test | `StartupBackupRefusalTests.Startup_MigrationRefusedForBackup_ReportsUnhealthy`, `..._ReasonNamesTheObstacle`, `..._ReasonCarriesTheRemedies`, `..._ReasonLinksTheKnowledgebaseEntry`; `BackupObstacleGuidanceTests.MigrationRefusedReason_DoesNotOfferTheOverride` |
| 17 | ✅ | A startup content-load refusal raises one `BackupRefused` notification that requires action, clears on reseed, and names the obstacle and the step | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentLoadWithNoBackupPossible_RaisesABackupRefusedNotification`, `..._TheNotificationRequiresAction`, `..._TheNotificationClearsOnReseed`, `..._TheNotificationNamesTheObstacle`, `..._TheNotificationNamesTheContentLoadStep`, `InitialiseAsync_ContentLoadRefusedOnTwoStarts_RaisesOneNotification` |
| 18 | ✅ | The new payload kind is accepted by the migration and the baseline alike | Unit test | `DatabaseInitializerOwnershipTests.NotificationMetadataKind_IsAcceptedByTheBaseline` and `..._IsAcceptedByTheIncrementalReplay`, per kind; the existing `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 19 | ✅ | Each option is offered exactly when it can run, and withheld otherwise, per obstacle | Unit test | Step 11's `NotificationActionExecutorTests`: every option offered and withheld, per obstacle; the availability's two answers; `DatabaseBackupPreflightTests`' three `...FreedFirst...` tests |
| 20 | ✅ | The offered options are visible over REST | Unit test | `NotificationEndpointsTests.GetNotifications_ListsTheOptionsTheExecutorOffers` and `..._DismissedNotification_ListsNoOptions` |
| 21 | ✅ | A reseed from the notification backs up first, and runs without one only with the user's permission | Unit test | Step 12's `NotificationActionExecutorTests`: effect and refusal for each option, including #304's reseed recommendation; a refused Reset stays unhealthy and active |
| 22 | ✅ | The Knowledgebase entry covers every obstacle, and the rendered link resolves to it | Unit test | `RepositoryStructureTests.KnowledgebaseLink_NamesAnEntryThatExists` and `NoBackupCouldBeTaken_HasASectionForTheObstacle` |
| 23 | ✅ | The notification renders offered options only, asks before reseeding without a backup, and both surfaces render the entry link | Unit test | Step 15's `NotificationTableTests` and `StartupErrorModalTests`; the rendered result by step 16's screenshots |
| 24 | ✅ | Startup content-load refusal, end to end | Automated (T2) | `backup/06`, red against the canary build, then green |
| 25 | ✅ | Startup migration refusal, end to end | Automated (T2) | `backup/07`, red against the canary build, then green |
| 26 | ✅ | Every test this issue adds or changes fails against its signature state, on an assertion | Unit test | Steps 7 to 15, each recording its red run |
| 27 | ✅ | Build clean and the full suite green across three `-m:1` runs | Build | Step 18 |
| 28 | ✅ | The application still starts | Live (T1) | The developer started `Quotinator.Api` in Visual Studio on 2026-09-26, against their existing database: a backup, Data v3 → v24 and App v5 → v9, then *Quotinator ready* serving 795 quotes |

---

## Design note: an out-of-range quota percentage must not crash

The issue calls an out-of-range value "a configuration error, not something to clamp silently". Taken
literally that suggests throwing, which would breach the never-crash contract this milestone is built
around. The project's existing precedent is the opposite extreme: an unrecognised `Quotinator:LogLevel`
falls back to `Information` silently. Neither is right here, and the precedent is not adopted because it
exists. Resolved: a warning naming the value and the accepted range, and the default used.
