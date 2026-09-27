# #348: Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable

**Status:** In progress
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

**Execute step 26.** Steps 1 to 6 are the first pass (reached `Waiting for release` 2026-08-28). The
issue was reopened 2026-09-26 when a re-verification against the issue's own requirements found three
of them unmet in the code; steps 7 to 18 closed them. Steps 19 to 25 correct the quota model, which the
first pass built backwards (see *Quota: two levels* for the settled model); every design question they
raise is answered there.

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
| Operating quota | `BackupQuotaPercent` of the budget, default 90% | Above it, the backup is still taken and a warning notification raised |
| Absolute ceiling | `MaxBackupStorageGb` | A backup that would exceed it is refused |

**The reserve is what lets a backup still be taken while the user is warned** (developer, 2026-09-26).
Between the quota and the ceiling a backup proceeds, and a `Warning` notification says the backups folder
is in the reserve, so the user can delete older backups, raise the quota, or whatever else brings it
back under. Only a backup that would take the folder past the ceiling is refused, as `BudgetExceeded`.
No override is involved: Reset's override means only "proceed without a backup", for when even the
ceiling refuses.

**No exceptions** (developer, 2026-09-26): every path that takes a backup warns and/or refuses by this
rule, whether the application takes it (startup content load, migration, Reset, a reseed option) or the
user does (`POST /backups`).

**The warning clears once the folder is back under the quota** (developer, 2026-09-26), checked after
every deletion and at every completed startup; raising the quota therefore clears it at the next
startup.

**An option that takes a backup says so when the folder is already at the quota** (developer,
2026-09-26). Such a backup runs inside the reserve and may reach the ceiling while it is taken, since its
size is not known in advance, so the user is told before choosing it rather than finding out from a
refusal. The notification's availability reports this alongside the options it offers; the page shows
it beside every option that takes a backup, and the notifications response carries it with
`availableActions`, so no surface offers such a backup without the caution.

**A notification whose condition can change while the application runs is re-checked on request**
(developer, 2026-09-26). `POST /api/v1/notifications/refresh` runs every registered condition check, so
a warning clears (or is raised) without a restart: for example after backups were removed outside the
application. Each such kind supplies one check behind a common interface, registered once; the endpoint,
the completed startup and every deletion run the same checks rather than code of their own. The quota
warning is the only kind with a check today, and a later kind joins by registering one, with no change to
the endpoint. It is open to every user, not only administrators, and limited by the `admin` rate-limit
policy (developer, 2026-09-26), so it sits in the notifications group that carries that policy without
the admin key filter.

**Where the check runs.** Every backup is written by one method, `DatabaseInitializer.CreateBackup`, and
every deletion by `DatabaseBackupWriter.Delete`, so refusal lives in the first and the warning is
evaluated by one component, in `Quotinator.Data`, called from each place storage can have changed: the
end of a completed startup, the end of a Reset, the on-demand backup, and every deletion. Not at the
moment of the pre-migration backup: writing a notification into a table the build has not yet migrated
is the unprotected write a migration refusal exists to prevent, so the completed startup evaluates it
instead, after migrating. The first attempt (steps 5 and 11) read "reaching into the reserve takes the
override" and refused at the quota; steps 19 to 25 correct that.

An out-of-range percentage is reported loudly and the default used: not a silent clamp, and not a crash.

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

### 19. Readiness refuses only at the ceiling, on every path
**Status:** ✅ Done

`CheckBackupReadiness` measures against the ceiling, and `allowReserve` goes: nothing reaches the reserve
by override any more. Reset's `allowNoBackup` keeps its one meaning. The on-demand backup, Reset and the
notification's options then all take a backup inside the reserve instead of refusing it, and the options
offer *Back up, then reseed* there.

Tests first, one statement each, each red against the current code: a readiness check inside the reserve
reports `Succeeded`; Reset inside the reserve takes its backup and rebuilds; the on-demand backup inside
the reserve succeeds; the executor offers *Back up, then reseed* inside the reserve; and a backup that
would pass the ceiling is refused as `BudgetExceeded` on each of those paths. The tests that assert the
old model (`UsageAtTheQuota_IsRefused_WithoutReachingIntoTheReserve`,
`CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed`, and any step 11
availability test that withholds an option inside the reserve) are rewritten to the new statement, each
recorded here with what it asserted and what it asserts now.

The availability also reports whether the folder is at or above the quota, so step 22 can caution every
option that takes a backup. Tests: at the quota it reports the caution; below it, it does not.

**Done 2026-09-27.** `CheckBackupReadiness` and `CreateBackup` now ask one function,
`BackupStorageBudget.WouldPassTheCeiling`: what the folder holds plus the backup's estimated size (the
database file's length, also shared) against the ceiling. So the check refuses exactly where the attempt
does, which it did not before: it compared only what was already there against the quota. `allowReserve`
and `BackupStorageBudget.LimitBytes` are gone; Reset's override now means only "proceed without a
backup". The availability's `BackupCaution` is the backup status reader's own `ReserveInUse`, so the
caution and the published status cannot disagree.

Tests, one statement each. New, each red against the code before this step on its assertion:
`DatabaseBackupPreflightTests.CheckBackupReadiness_InsideTheReserve_ReportsSucceeded`,
`..._WhenTheBackupWouldPassTheCeiling_ReportsBudgetExceeded` and `..._AgreesWithTheAttempt` (the quota set
to 100% so only the backup's own size can refuse), `DatabaseBackupQuotaTests.CreateBackupAsync_InsideTheReserve_TakesTheBackup`,
`ResetAsync_InsideTheReserve_ReachesTheDestructiveStep`, and
`NotificationActionExecutorTests.GetAvailabilityAsync_AtTheQuota_CautionsTheBackup` (against the property
unset). `GetAvailabilityAsync_BelowTheQuota_DoesNotCautionTheBackup` was red against a caution that is
always set.

Rewritten to the corrected model, each red on its assertion against the code before this step:

| Asserted | Now asserts |
|---|---|
| `CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed`: the reserve answers differently when an override asks | replaced by `..._InsideTheReserve_ReportsSucceeded` above |
| `CheckBackupReadiness_OverTheQuotaByLessThanWhatIsFreedFirst_ReportsSucceeded`: freeing a backup clears the quota | `..._PastTheCeilingByLessThanWhatIsFreedFirst_ReportsSucceeded`: freeing a backup makes room under the ceiling |
| `..._OverTheQuotaByMoreThanWhatIsFreedFirst_ReportsBudgetExceeded`: freeing too little leaves it over the quota | `..._PastTheCeilingByMoreThanWhatIsFreedFirst_ReportsBudgetExceeded`: freeing too little leaves no room under the ceiling |
| `DatabaseBackupQuotaTests.UsageAtTheQuota_IsRefused_WithoutReachingIntoTheReserve` | replaced by `CreateBackupAsync_InsideTheReserve_TakesTheBackup` above |
| `PublishedUsage_AgreesWithTheLimitAReadinessCheckRefusesOn`: two statements, the published quota and the refusal agree | `PublishedUsage_AgreesWithTheCeilingAReadinessCheckRefusesOn`: one statement, the refusal agrees with the published room under the ceiling, at four points across both limits |

Removed: `UsageAtTheAbsoluteCeiling_IsRefusedEvenWithTheReserveAllowed` (superseded by the ceiling tests
above, which can fail where it could not); `ConfiguredQuotaPercent_IsTheLimitTheCheckRefusesOn` (the quota
no longer refuses; step 21's warning is where a configured quota takes effect, and tests it there);
`BackupStorageBudgetTests.LimitBytes_*`, both, with the function. A rewrite of
`QuotaPercent_OutOfRange_UsesTheDefault` onto `QuotaBytes` was red against a budget without its range
check, then removed as a duplicate of `BackupStorageBudgetTests.QuotaBytes_OutOfRangePercentage_UsesTheDefaultShare`.
Both test fixtures now create the database first: whether a backup would pass the ceiling depends on what
it adds, and an absent file adds nothing.

The out-of-range quota is still reported by the readiness check, which no longer uses the quota; step 21
moves that report to the warning's check, which does.

Text stating the old model, corrected: the Reset endpoint's description and its `docs/api-endpoints.md`
row, and the comments on `DatabaseOptions.BackupQuotaPercent`, `BackupStorageUsage`,
`BackupStatusResponse` and `BackupStorageBudget`. The Reset refusal's title is now *Reset refused: no
backup could be taken*, followed into the Knowledgebase entry that quotes it; `backup/01`'s dated record
of the old title stays as measured. Every file touched is dash-free, except the page-size range `(0–500)`
in `AdminEndpoints.cs`: the same wording sits in nine endpoint files, so it waits on one decision for all
nine, as the page titles do.

Green: Data 1,411 (two fewer: four quota tests and two budget tests removed, four added), Core 1,730, Api
1,132, 0 warnings. **One unexplained failure, recorded rather than dismissed:** in the first Api run after
this step, `ChangelogDatabaseWiringTests`' two tests each timed out waiting 30 s for startup; both pass
alone, and a full Api rerun passed all 1,132. Their factory runs the real startup against the real data
directory, which includes a network source refresh. The first run's message was lost to an output filter,
so the cause is not established; every run from here keeps its full log.
### 20. The quota warning's notification kind
**Status:** ✅ Done

A `BackupQuotaReached` payload (the bytes used, the quota and the ceiling), a `BackupQuotaRestored`
dismiss trigger, and a resolution recording that the folder came back under the quota. All three are
CHECK-constrained columns of `System_Notification`, so one table rebuild (Data migration 25) widens them
together, with the baseline updated to match, per ADR 008. The title and body keys in all three
languages name what is used against both limits and the remedies: the backup list and delete endpoints,
and `Quotinator:BackupQuotaPercent`.

Tests: the ownership tests' per-kind DynamicData picks the new kind up; the trigger and the resolution
each get the baseline and incremental-replay acceptance tests the kind has.

**Done 2026-09-27.** `NotificationMetadataKind.BackupQuotaReached` with `BackupQuotaReachedMetadataDto`
(the bytes used, the quota and the ceiling; nothing identifies it, so one warning is open at a time),
`NotificationDismissTrigger.BackupQuotaRestored`, and `NotificationResolution.UnderQuota`, whose label
reads *Backups back under their quota*. Data migration 25 (`NotificationBackupQuotaMigrations`) rebuilds
`System_Notification` once, widening all three CHECKs, and the baseline matches. The title and body keys
are in all three languages; the body takes the three sizes already formatted.

**Where this migration's SQL lives follows the 26 sibling `Database/*Migrations.cs` classes, not a
document** (raised 2026-09-27). CLAUDE.md's Schema migration policy still says migration SQL stays inside
`DatabaseInitializer` as `private const string Migration00N_...`, which now holds none of them; the CHECK
rebuild worked example `database-conventions.md` cites, `Migration004_ImportBatchTypeUserSeed`, no longer
exists as a member either. **Deferred** (developer, 2026-09-27): this milestone's unreleased migrations are
being rewritten at its close, so the placement and the two stale documentation references are settled
there rather than here.

Tests: `NotificationDismissTrigger_IsAcceptedByTheBaseline`/`..._ByTheIncrementalReplay` and
`NotificationResolution_IsAcceptedByTheBaseline`/`..._ByTheIncrementalReplay`, enumerated from their enums
as the kinds already were, since the hand-written resolution list could not fall behind by itself. Red
against the members added with the CHECKs unwidened: exactly the six new-member cases failed, all 28
existing ones passed. Three existing guards then required the kind to be declared, as they are meant to:
its layout (no detail table), a sample payload, and a mention in `notifications-and-changelog/14`, which
names `backup/08` as where it is produced. Api 1,144, Data 1,431, Core 1,730, 0 warnings.

**The empty identity was claimed but not proved, and is now proved** (found 2026-09-27, reviewing the
payload against `NotificationMetadataDto`'s own contract rather than against a sibling payload). The
contract states that a payload returns an empty sequence "when the common fields already say everything",
and excludes "detail that describes the notification without identifying it", which is what the three
sizes are. `AboveTheQuotaOnTwoChecks_RaisesOneWarning` never varied them, so it passed either way. The
production path does vary them: `BackupOperations.CreateAsync` writes a backup and then runs the check, so
a size held in the identity would re-announce the warning on every backup taken from the reserve.
`AboveTheQuotaAndGrowing_RaisesOneWarning` fills to 92%, checks, fills to 95%, checks, and expects one
warning; run red against `IdentityComponents => [UsedBytes]`, where it failed and the older test still
passed. Data 1,446.
### 21. Condition checks raise and clear the warning, on every path and on request
**Status:** ✅ Done

A condition-check interface in `Quotinator.Data`, and one component running every registered check. The
quota check is the first: above the quota it raises the warning once while unresolved
(`NotificationSeeding.SeedWhileUnresolvedAsync`); at or below it, it resolves any open warning by its
trigger. The checks run at the end of a completed startup, at the end of a Reset, after the on-demand
backup, after every deletion through `DatabaseBackupWriter.Delete`, and on
`POST /api/v1/notifications/refresh`. The endpoint answers `200` with what each check did (raised,
cleared, unchanged), per kind; it is named `RefreshNotifications`, tagged `Notifications`, and described
in `docs/api-endpoints.md` and its `[Description]` attributes in the same commit.

Tests, one statement each, red first: a completed startup whose backups sit in the reserve raises the
warning; one below the quota raises none; the warning is raised once across two such startups; a Reset
in the reserve raises it; the on-demand backup in the reserve raises it; a reseed option's backup in the
reserve raises it; a deletion that brings the folder under the quota clears it; a deletion that leaves it
above does not; a startup under a raised quota clears it; the notification names the bytes used, the
quota and the ceiling. For the endpoint: it answers without an admin key; a folder brought under the quota outside the
application clears the warning, one pushed above raises it, and the
response reports what each check did; every registered check runs, proven with a second, test-only
check alongside the quota's.

**Done 2026-09-27.** In `Quotinator.Data`: `INotificationConditionCheck`, `NotificationConditionChecks`
(runs every registration in order) and `BackupQuotaCheck`, with `ByteSize` to write the three sizes, which
used past the quota raises once while unresolved (`Warning`) and resolves as `UnderQuota` below it. The
out-of-range quota report moved from the readiness check, which no longer uses the quota, into this one,
where the quota takes effect. The checks run from four places:

| Where | Why there |
|---|---|
| `Program.cs`, after a completed, healthy startup | covers the pre-migration and pre-content-load backups, once the schema is current |
| `DatabaseInitializer.ResetAsync`, through the new `OnResetCompletedAsync` hook the Core initializer overrides | the one point every Reset passes through, the endpoint's and the notification action's alike |
| `BackupOperations.CreateAsync` and `RemoveAsync` | the audited path both the Backups API and the reseed options use; deletion goes through it rather than through `DatabaseBackupWriter.Delete` directly |
| `POST /api/v1/notifications/refresh` (`RefreshNotifications`) | no admin key, the `admin` rate-limit policy; answers `checks`, each `kind` and `outcome` |

Tests, one statement each. `BackupQuotaCheckTests` (11) against a real database and a real folder:
raised, reported raised, none below, one across two checks, cleared, reported cleared, recorded as
`UnderQuota`, kept while still above, the sizes named, a configured quota consulted, and an out-of-range
quota reported. `NotificationConditionChecksTests` (2) and `ByteSizeTests` (2). Through the real host,
`BackupQuotaWarningTests` (10): startup in the reserve, startup under a raised quota, Reset, the on-demand
backup, a deletion, and five for the endpoint (no admin key, cleared and raised after a change outside the
application, the response per kind, and a second, test-only check also run). The reseed option by
`NotificationActionExecutorTests.Reseed_BackUpThenReseed_RunsTheConditionChecks`.

Red, each on its assertion. Against the stubs (a check that did nothing, a runner that ran nothing, no
call site, no route) 13 Data and 11 Api tests failed; the two that passed there were run red against a
check that always raised (`BelowTheQuota_RaisesNothing`) and one that always cleared
(`StillAboveTheQuota_KeepsTheWarning`). **The first red run of the Reset, on-demand backup and deletion
tests was void:** every admin call answered `401`, since the admin key passed with `UseSetting` never
reaches the request-time key check. With the key passed as the other endpoint tests pass it, each was run
red again with its own call removed, and failed.

`AdminEndpointsTests.ResetDatabase_CorrectKey_CallsDismissByTriggerWithDatabaseReset` asserted exactly one
dismissal; a Reset now also runs the quota check, which below the quota dismisses by its own trigger. Its
statement is the one its name makes, so it now asserts the `DatabaseReset` dismissal is among the calls,
run red against the endpoint dismissing by another trigger. Api 1,155, Data 1,445, Core 1,730, 0 warnings.
### 22. Render and document the warning
**Status:** ✅ Done

The notification table's per-kind layout for the new kind (its payload says nothing its body does not,
so it opens no detail), and a Knowledgebase entry for the warning, linked from it. The caution from
step 19: beside every option that takes a backup (*Back up, then reseed*, *Remove the oldest backup,
then back up and reseed*, and *Reset the database*, whose Reset takes one too) when the folder is at the quota, in all three languages, and in the
notifications response next to `availableActions`. Tests, one statement each, red first: each such
option carries the caution at the quota; neither carries it below; an option that takes no backup
(*Reseed without a backup*) never carries it; the response carries it at the quota and not below. The
`no-backup-could-be-taken` entry's `BudgetExceeded` section, `BackupStorageUsage`'s quota comment, and
any other text stating the old model are corrected to the ceiling.

**Done.** Which options take a backup is stated once, `NotificationActionOptions.TakesABackup`, and both
surfaces ask it: the page through `NotificationTable.CautionsTheBackup`, the response through
`NotificationResponse.BackupCaution`, set when the availability reports the caution and any option the
row offers takes a backup. The page shows the caution (`NotificationsBackupCaution`, three languages)
under the option buttons, in the confirmation of a cautioned option, and beside a Reset row's single
action, since a Reset takes a backup too. The quota warning links a new Knowledgebase entry,
*Backups have reached their quota* (`KnowledgebaseLinks.BackupsHaveReachedTheirQuota`), and its layout
opens no detail, as step 20 recorded. `docs/api-endpoints.md` documents `backupCaution`, the reserve on
`POST /admin/backups/create`, and the checks a deletion runs.

Corrected to the ceiling: the `BudgetExceeded` section of `no-backup-could-be-taken`, `BackupOutcome`'s
summary, `BackupObstacleGuidance`'s cause and two remedies (the cause now names the ceiling, so
`AdminEndpointsTests.ResetDatabase_WhenNoBackupCanBeTaken_DescribesTheCause` asserts "ceiling", run red
against the old wording), the `DatabaseInitializer` comment that said the readiness check enforced the
quota, the Backup tag description, and the `DELETE /admin/backups/{name}` row. `BackupStorageUsage`'s
comment already stated the reserve. **`backup/05` stated the old model too:** it filled the folder to 95%
and expected a refusal, which no longer happens. It now fills to the ceiling, and is retitled *A full
backups folder is resolvable from inside the application*; its file name is unchanged, so no link
breaks. It runs at step 28.

Tests, one statement each. `NotificationTableTests`: each of the three options is cautioned at the quota
and not below, *Reseed without a backup* never is, and the quota warning links its entry.
`NotificationEndpointsTests`: the response cautions a row offering a backup at the quota, not below it,
and not a row offering none. Red: against the stubs the four positive tests failed; the six negative ones
passed there and were run red against a caution that was always on, in the page helper and in the
response; the link test failed against the mapping without the new kind. Api 1,167, 0 warnings.

### 23. Automated (T2) document for the reserve, red first
**Status:** ✅ Done

`backup/08`: fill the backups folder into the reserve, then prove a backup is still taken and the
warning raised, on the on-demand path and at startup; delete a backup to bring it under and prove the
warning clears; raise it again, remove a backup file from outside the application, and prove
`POST /notifications/refresh` clears it with no restart; fill past the ceiling and prove the backup is
refused. Run red against `7f83e92a` (the
first model) before green.

**Done 2026-09-27.** `backup/08`, *Backups continue from the reserve, with a warning, until the ceiling
refuses one*: Fresh with `--bind`, port 18388, eleven steps each with one expected result. Step 2 is the
positive control, since every step below asserts a warning and a build that always raised one would
satisfy them all. Step 3 proves the band reached rather than assuming it, and checks a backup still fits
below the ceiling against step 2's own measured cost, so neither figure is a prediction. Step 5 reports
the count rather than a boolean, because `0` and `2` are different failures. Step 6b captures the page,
which is the caution's only proof outside bUnit. Step 10 is the remedy proven, per the suite's rule that
a document provoking a fault ends by showing the remedy works.

**Red against an image built from `7f83e92a` inverted all four observations the document turns on:** step
3 answered `canBackUp=False` while `fits=True` with 81 MB below the ceiling, step 4's backup was refused
`409 BudgetExceeded`, no `backupquotareached` row existed, and `POST /notifications/refresh` answered
`404`. Green on `quotinator:local` through all eleven steps, screenshot included.

**The red run corrected the document.** Step 3's *On failure* blamed `canBackUp=False` on overshooting
the ceiling alone, which `fits=True` disproves, so it now separates that from a build refusing at the
quota. `Remove-Item -Force` on the host filler is refused outright by a sandboxed runner, stopping the
step before anything executes, so the document uses `[System.IO.File]::Delete`.

**The pass found a defect, now step 24.** Past the *ceiling* the check raises
the same body it raises inside the reserve. Measured at 1.02 GB against a 1.00 GB ceiling with
`canBackUp=False`: *"Backups are still being taken, from the reserve below the ceiling of 1.00 GB, but
once the folder reaches the ceiling a backup will be refused."* Both clauses are false there. The refusal
itself is correctly reported on the path that asks for a backup (`409` with the obstacle and its
remedies), so no rule is broken, but a healthy startup past the ceiling attempts no backup and leaves
this warning as the only notification an operator sees. One message covers two states and is accurate in
one. `backup/08` records it and asserts nothing about it yet, per the suite's rule that a document states
instructions rather than a verdict; step 24 makes the warning valid only inside the band
it describes, and adds the assertions then.
### 24. The warning is valid only in the band it describes
**Status:** ✅ Done

`BackupQuotaCheck` tests one threshold, `used < quota`, so everything at or above the quota is one state.
Above the max it therefore raises the reserve's own body, which says backups are still being taken and
that a refusal will happen once the ceiling is reached. Step 23 measured both clauses false at 1.02 GB
against a 1.00 GB ceiling.

**The condition is the band, not the threshold:** the warning is valid while `quota <= used < max`. Above
the max it is not valid, so the generic check removes it rather than keeping or raising it, and the
notification the operator sees there is the error from the attempt that was refused (step 25). Below the
buffer it is removed, which is already what happens. A warning saying storage is inside the reserve and
an error saying the max is exceeded cannot both be open from one action (developer, 2026-09-27).

No new notification kind, no rename, no band in the payload's identity, and nothing informational to
add: a folder below the buffer has no warning open, which is the state a fresh install is already in.

Tests, one statement each, red first: the warning is kept inside the band, removed below the buffer as
today, and removed above the max. `backup/08` gains the step it records but does not yet assert, reading
the notification past the ceiling rather than only the refusal.

**Done 2026-09-27.** One comparison became the band: `used < quota || used >= ceiling` removes the
warning, and only `quota <= used < ceiling` raises it. `WouldPassTheCeiling` is `used + backup > ceiling`,
so from `used >= ceiling` every real backup is refused, which is exactly where the warning's claim stops
being true.

**The body needed no change**, which is the sign the fix was in the right place: it is now only ever shown
inside the band it was written for. Inside the reserve a backup can still be refused when the estimate is
close to the room left, and that is step 25's error rather than a boundary this check should move.

`BackupQuotaCheckTests.AboveTheMax_RemovesTheWarning` and `AboveTheMax_RaisesNothing`, both red against
the single threshold, where the warning was kept and raised respectively. The existing tests already
covered kept-inside-the-band and removed-below-the-buffer. `backup/08` step 9 now asserts `open=0` beside
the `409`, and the two summaries stating the old rule were corrected. Data 1,448, Api 1,167, 0 warnings.

**`backup/08` has not been re-run since this change**, so its Observed effect records the run that found
the defect. Step 28 re-runs it.

### 25. Exceeding the max generates an error
**Status:** ✅ Done

**The reachability question step 25 was to answer first is answered, and the answer is a flaw.** After
`connection.BackupDatabase(dest)` succeeds, `CreateBackup` returns `Success` with no post-write budget
check of any kind, so a backup whose real size exceeds the estimate it was permitted on leaves the folder
above the max with nothing reporting it. The pre-write check uses the source file's length, which is an
approximation; the max is ours rather than the hardware's, so nothing physical stops the write the way a
full volume would (developer, 2026-09-27). Step 24 then removes the warning there, so the state that most
needs reporting currently reports nothing at all.

**Exceeding the max always generates an error.** A new `Error` notification for a folder at or above it,
with its own kind, trigger and resolution, and the check reconciles all three bands in one place:

| Band | What the check leaves open |
|---|---|
| below the buffer | neither |
| buffer to max | the warning |
| at or above the max | the error |

So the error is a state rather than an event: it is raised whether the folder arrived there by a refused
attempt, by a backup that overshot its estimate, or by files written from outside the application. A
refused attempt therefore needs no notification of its own, which is what the earlier plan had.

**The completed write is not failed retroactively, and the oversized backup is not deleted.** Both were
considered: a hard limit would have stopped the write, but ours did not, and the file that exists is a
complete and valid restore point. Discarding it, or reporting it as a failure to a caller that would then
refuse to reseed, destroys the thing the reserve exists to protect in order to satisfy a soft limit. The
end-of-series check raises the error instead, and the remedies already tell the operator to remove old
backups.

Texts in all three languages. Tests, one statement each, red first: the error raised at and above the max,
not below it, the warning and the error never open together, and each removed when its band no longer
applies. `backup/08` step 9 reads the error rather than only `open=0`.

**Done 2026-09-28.** `BackupMaxExceededCheck` is a second `INotificationConditionCheck`, not a third
branch of the first: each notification carries its own verification, so the error's kind owns its own
check and the two bands cannot overlap by construction, the warning being valid only below the maximum
and the error only at or above it. `NotificationMetadataKind.BackupMaxExceeded` with
`BackupMaxExceededMetadataDto`, `NotificationDismissTrigger.BackupBackUnderMax` and
`NotificationResolution.UnderMax`, migration 26 widening the same three CHECKs in one rebuild with the
baseline to match, and title and body keys in all three languages.

`BackupMaxExceededCheckTests` (12): raised at the max and above it, as an `Error`, once across two checks,
nothing below it, cleared and recorded as `UnderMax`, kept while still above, the sizes named, and
`EachBand_LeavesOnlyItsOwnNotificationOpen` over three bands. Red: the six enum-acceptance cases against
migration 26 removed and the baseline un-widened, where the 56 existing cases still passed; the nine check
tests across two mutations, six against a check that never raises and three against one that always does;
the cross-band case against the quota check's own threshold restored, where only the above-the-max row
failed. The four UI guards supplied their own red, each naming what was missing: the layout declaration,
the payload sample, the notif/14 mention and the resolution's translation key.

**A red run was void and nearly cost a sound test.** Removing migration 26 by a needle written with CRLF
matched nothing in an LF file, so the migration stayed and the incremental-replay cases passed; read
straight, that said those tests could not fail. The mutation is now verified to match before the run, and
every case failed once it did. This is the third void red of this milestone, each one a mutation that
silently did not apply.

Data 1,466, Api 1,167, Core 1,730, 0 warnings.

### 26. The checks run once at the end of a series
**Status:** ⬜ Not started

Step 21 calls `conditionChecks.RunAsync()` inside `BackupOperations.CreateAsync` *and* `RemoveAsync`, so
*Remove the oldest backup, then back up and reseed* re-evaluates every registered check three times in
one action. Re-verification belongs at the end of a series of operations, once, for the cost of it
(developer, 2026-09-27).

The per-operation calls come out; each composite action runs the checks once when it finishes. The
boundaries to cover are the notification actions, the admin endpoints that take or remove a backup, the
Reset, and startup, with `POST /notifications/refresh` unchanged since a caller asking for a refresh is
already asking for exactly one. Tests assert the count of evaluations per action, not only that one
happened, since "ran at least once" is what the current wiring already satisfies.
### 27. Documentation
**Status:** ⬜ Not started

The changelog's #348 entries in all three languages describe the warning rather than a refusal at the
quota. The issue's requirement 8 still says the reserve is reached by override; its correction is drafted
for the developer's approval, not edited unasked.

### 28. Full verification
**Status:** ⬜ Not started

Build clean; the full suite green across three `-m:1` runs; every `backup/` document, notif/12 to 14,
and the smoke set; T1 by the developer.

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
| 9 | ❌ | Every path takes a backup inside the reserve and refuses only one that would pass the ceiling | Unit test | Step 19: readiness, Reset, the on-demand backup and the executor, each inside the reserve and past the ceiling |
| 10 | ✅ | The quota percentage is configurable and defaults to 90 | Unit test | `DatabaseBackupQuotaTests.ConfiguredQuotaPercent_IsTheLimitTheCheckRefusesOn` and `...QuotaPercent_DefaultsTo90` |
| 11 | ✅ | An out-of-range percentage is reported and the default used, never clamped, never fatal | Unit test | `DatabaseBackupQuotaTests.QuotaPercent_OutOfRange_UsesTheDefault` and `...QuotaPercent_OutOfRange_IsReported` |
| 12 | ✅ | Each variant states cause and remedy, and names no remedy that cannot work | Unit test | `BackupObstacleGuidanceTests`: `EveryObstacle_HasACause`, `EveryObstacle_HasARemedy`, `RecognisedObstacle_IsNotDescribedAsTheUnrecognisedFallback`, `BudgetExceeded_OffersTheOverride`, `BudgetExceeded_OffersRemovingBackupsThroughTheApplication`, `SourceUnreadable_DoesNotOfferTheOverride`, `OverrideAlreadyTried_DoesNotRepeatTheOverride`, `OverrideAlreadyTried_KeepsTheOtherRemedies` |
| 13 | ✅ | The two tests whose expectation changed were renamed to their new contract, not bent to pass | Unit test | Recorded in step 2: both renamed, each with a comment on what changed and why |
| 14 | ✅ | The corrupt and truncated databases are recoverable end to end | Automated (T2) | `backup/01` and `backup/02`, executed 2026-08-28, each ending with the remedy proven by a `200` |
| 15 | ✅ | A pending migration with no backup possible applies nothing and reports the obstacle and the step | Unit test | `DatabaseInitializerTests.InitialiseAsync_MigrationPendingWithNoBackupPossible_IsRefused`, `..._NamesTheObstacle`, `..._NamesTheMigrationStep`, `..._AppliesNoMigration`; the content-load and Reset steps by `...ContentLoadWithNoBackupPossible_NamesTheContentLoadStep` and `DatabaseBackupQuotaTests.ResetAsync_WhenNoBackupCanBeTaken_NamesTheResetStep` |
| 16 | ✅ | A startup migration refusal marks the database unhealthy, naming the variant, remedies and entry, and no remedy the startup cannot use | Unit test | `StartupBackupRefusalTests.Startup_MigrationRefusedForBackup_ReportsUnhealthy`, `..._ReasonNamesTheObstacle`, `..._ReasonCarriesTheRemedies`, `..._ReasonLinksTheKnowledgebaseEntry`; `BackupObstacleGuidanceTests.MigrationRefusedReason_DoesNotOfferTheOverride` |
| 17 | ✅ | A startup content-load refusal raises one `BackupRefused` notification that requires action, clears on reseed, and names the obstacle and the step | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentLoadWithNoBackupPossible_RaisesABackupRefusedNotification`, `..._TheNotificationRequiresAction`, `..._TheNotificationClearsOnReseed`, `..._TheNotificationNamesTheObstacle`, `..._TheNotificationNamesTheContentLoadStep`, `InitialiseAsync_ContentLoadRefusedOnTwoStarts_RaisesOneNotification` |
| 18 | ✅ | The new payload kind is accepted by the migration and the baseline alike | Unit test | `DatabaseInitializerOwnershipTests.NotificationMetadataKind_IsAcceptedByTheBaseline` and `..._IsAcceptedByTheIncrementalReplay`, per kind; the existing `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 19 | ❌ | Each option is offered exactly when it can run, and withheld otherwise, per obstacle | Unit test | Step 11's `NotificationActionExecutorTests`: every option offered and withheld, per obstacle; the availability's two answers, re-earned against the ceiling in step 19; `DatabaseBackupPreflightTests`' three `...FreedFirst...` tests |
| 20 | ✅ | The offered options are visible over REST | Unit test | `NotificationEndpointsTests.GetNotifications_ListsTheOptionsTheExecutorOffers` and `..._DismissedNotification_ListsNoOptions` |
| 21 | ✅ | A reseed from the notification backs up first, and runs without one only with the user's permission | Unit test | Step 12's `NotificationActionExecutorTests`: effect and refusal for each option, including #304's reseed recommendation; a refused Reset stays unhealthy and active |
| 22 | ✅ | The Knowledgebase entry covers every obstacle, and the rendered link resolves to it | Unit test | `RepositoryStructureTests.KnowledgebaseLink_NamesAnEntryThatExists` and `NoBackupCouldBeTaken_HasASectionForTheObstacle` |
| 23 | ✅ | The notification renders offered options only, asks before reseeding without a backup, and both surfaces render the entry link | Unit test | Step 15's `NotificationTableTests` and `StartupErrorModalTests`; the rendered result by step 16's screenshots |
| 24 | ✅ | Startup content-load refusal, end to end | Automated (T2) | `backup/06`, red against the canary build, then green |
| 25 | ✅ | Startup migration refusal, end to end | Automated (T2) | `backup/07`, red against the canary build, then green |
| 26 | ✅ | Every test this issue adds or changes fails against its signature state, on an assertion | Unit test | Steps 7 to 15, each recording its red run |
| 27 | ❌ | Build clean and the full suite green across three `-m:1` runs | Build | Step 28 (first passed at step 18, before the quota correction) |
| 28 | ❌ | The application still starts | Live (T1) | The developer starts `Quotinator.Api` in Visual Studio after step 28 (first passed 2026-09-26, before the quota correction) |
| 29 | ✅ | A backup that leaves the folder above the quota raises one warning, on every path | Unit test | Step 21: startup, Reset, the on-demand backup and a reseed option, each in the reserve; once across two startups; none below the quota |
| 30 | ✅ | The warning clears once the folder is back under the quota, and only then | Unit test | Step 21: a deletion under the quota clears it, one leaving it above does not, a startup under a raised quota clears it |
| 31 | ✅ | The warning kind, its trigger and its resolution are accepted by the migration and the baseline alike | Unit test | `DatabaseInitializerOwnershipTests`: `NotificationMetadataKind_`, `NotificationDismissTrigger_` and `NotificationResolution_IsAcceptedByTheBaseline`/`..._ByTheIncrementalReplay`, per member; the schema-drift parity test |
| 32 | ✅ | Every option that takes a backup is cautioned when the folder is at the quota, on the page and over REST | Unit test | Step 19 and step 22: the availability, `NotificationTableTests`, `NotificationEndpointsTests` |
| 33 | ✅ | The reserve, end to end: a backup taken and warned, cleared by a deletion and by a refresh without a restart, refused past the ceiling | Automated (T2) | `backup/08`, red against `7f83e92a`, then green |
| 34 | ✅ | `POST /notifications/refresh` runs every registered condition check, needs no admin key, and reports what each did | Unit test | Step 21: answered without an admin key; the quota warning cleared and raised on request; a second, test-only check also run; the response per kind |
| 35 | ✅ | The warning is kept inside its band, and removed both below the buffer and above the max | Unit test | Step 24: one test per case, and `backup/08` reading the notification past the ceiling |
| 36 | ✅ | A folder at or above the max always has an error open, however it got there, and never alongside the warning | Unit test | Step 25: raised at and above the max, absent below it, never open with the warning, and `backup/08` step 9 |
| 37 | ❌ | A composite action evaluates the conditions once, not once per operation inside it | Unit test | Step 26: the count of evaluations per action, not merely that one happened |

---

## Design note: an out-of-range quota percentage must not crash

The issue calls an out-of-range value "a configuration error, not something to clamp silently". Taken
literally that suggests throwing, which would breach the never-crash contract this milestone is built
around. The project's existing precedent is the opposite extreme: an unrecognised `Quotinator:LogLevel`
falls back to `Information` silently. Neither is right here, and the precedent is not adopted because it
exists. Resolved: a warning naming the value and the accepted range, and the default used.
