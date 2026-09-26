# #348: Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable

**Status:** In progress (step 8)
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

**Execute step 8.** Steps 1 to 6 are the first pass (reached `Waiting for release` 2026-08-28). The
issue was reopened 2026-09-26 when a re-verification against the issue's own requirements found three
of them unmet in the code; steps 7 onward close them, and every design question they raise was settled
with the developer before this plan was written (see *Design*).

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
**Status:** ⬜ Not started

Found 2026-09-26: `ApplyMigrationsAsync` takes a backup, keeps only its path, and never checks whether it
succeeded, so a failed backup lets every pending migration run unprotected. Its restore handler is
filtered on that path, so a migration that then throws is not rolled back either.

Red first: a migration pending with no backup possible applies no migration, leaves both schema-version
counters unchanged, and returns a refusal naming the obstacle and the `Migration` step. Its positive
counterpart: with a backup possible, the same database migrates. `DatabaseOperationResult` gains the
refused step, as a `BackupGuardedStep` enum in `Quotinator.Data/Enums/`.

### 9. Startup reports a refusal instead of discarding it
**Status:** ⬜ Not started

Found 2026-09-26: `Program.cs` awaits `InitialiseAsync()` and discards its result, so a refused content
load leaves the app reporting healthy with nothing loaded and nothing said. Its handler for
`DatabaseBackupWriteException` is dead code: nothing in `src/` throws that type any more.

Red first, through `WebApplicationFactory` with a backup made impossible deterministically (a zero
quota): a refused migration marks the database unhealthy with a reason naming the variant, its remedies
and the Knowledgebase entry; a refused content load leaves the app healthy and raises the step 10
notification. Positive counterparts: with a backup possible, startup is healthy and raises no such
notification. The dead handler and `DatabaseBackupWriteException` are removed.

### 10. The refused content load raises a notification
**Status:** ⬜ Not started

`NotificationMetadataKind.BackupRefused` (payload: the obstacle and the refused step), with its migration
widening the CHECK, the baseline updated to match, and the schema-drift tests extended. The notification
carries the existing `Reseed` dismiss trigger. Title and body in all three languages. Deduplicated
against active rows while unresolved, like `Reseed`, since the refusal recurs on every start until
resolved.

### 11. Offer only the options that can run
**Status:** ⬜ Not started

`NotificationActionAvailability` gains the pre-flight answers (normal and with the reserve) and whether
removing the oldest backup would clear the obstacle; the executor offers each option under exactly the
conditions in *Design*. Every option is tested offered and withheld, per obstacle.

The offered options also reach `GET /api/v1/notifications`, as an `availableActions` list on each
notification. A requirement visible only in rendered HTML cannot be verified live (process.md), and this
is the requirement the whole reopening is about.

### 12. The reseed action backs up first
**Status:** ⬜ Not started

The executor's `Reseed` action composes the steps from the existing calls, adding none to
`IDatabaseInitializer`: `CheckBackupReadiness`, then `CreateBackupAsync`, then `ReseedAsync`.

- **Back up, then reseed:** reseeds only when the backup succeeded; a backup that fails after the check
  passed stops before the reseed and leaves the notification active with that obstacle.
- **Remove the oldest backup, then back up and reseed:** deletes through #349's audited path first.
- **Reseed without a backup:** runs only with the user's permission, given after being shown the
  obstacle and the Knowledgebase link; writes the missing backup to the log and the audit trail.

Every path that reseeds dismisses with `Reseed`, as today. Each is tested for its effect (a backup file
written before the reseed ran, content present, backup removed, audit and log written, notification
dismissed) and for its refusal (no reseed, nothing deleted, notification still active). #304's reseed
recommendation runs through the same action, so its existing tests are re-run and extended with a
backup written before the reseed.

### 13. Test the skip log line
**Status:** ⬜ Not started

Found 2026-09-26: requirement 5 asks for a log line **and** an audit entry. The audit entry is tested;
`LogResetProceedingWithoutBackup` is not, although the issue named that test. Red first: an overridden
Reset logs the skip; a Reset whose backup succeeds logs no skip line.

### 14. Write the Knowledgebase entry
**Status:** ⬜ Not started

`docs/knowledgebase/no-backup-could-be-taken.md`, from `entry-template.md`, listed in the folder's
README. Its symptoms quote the notification, the `503` reason and the `409` detail verbatim; a section
per `BackupOutcome` names the blocked options, why each is blocked, and the manual resolution. Held by a
repository test: every `BackupOutcome` member has its section, and the link the application renders
names a file that exists.

### 15. Render the options and the Knowledgebase link
**Status:** ⬜ Not started

`NotificationTable` renders each offered option as its own button and none that is withheld, beside a
link to the entry. *Reseed without a backup* opens a confirmation naming the obstacle and linking the
entry, and runs nothing until confirmed. `StartupErrorModal` renders the same link when the degraded
reason is a refusal. All tested as components; labels and confirmation text in all three languages.

### 16. Automated (T2) documents, red first
**Status:** ⬜ Not started

In `docs/automated-testing/backup/`, each run first against a canary build of the commit before step 8:

- *A startup that cannot take a backup loads nothing, and offers only options that can run.* Reset a
  database, restart with a zero quota: nothing loaded, the notification present with exactly the
  options the state allows; then each option executed through the page, and the remedy proven by the
  content arriving.
- *A migration that cannot take a backup leaves the database unmigrated, and says why.* Upgraded +
  Constrained: `503` naming the variant, schema version unchanged, the Knowledgebase link on the
  degraded Home page; the sabotage removed, and the same volume migrates.

Both indexed in `docs/automated-testing/README.md`.

### 17. Documentation
**Status:** ⬜ Not started

`docs/api-endpoints.md` and the endpoint's `[Description]` for `availableActions`; the changelog's
unreleased entry for #348 in all three languages.

### 18. Full verification
**Status:** ⬜ Not started

Build clean; the full solution suite green across three consecutive `-m:1` runs; the T2 smoke set plus
every `backup/` document, `startup-and-degradation/05`, and the notification documents the new kind
touches.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every backup attempt reports which obstacle it hit | Unit test | `DatabaseBackupOutcomeTests`: one `*_IsReportedAs*` test per obstacle it can produce in-process, with `SucceedingBackup_ReportsWhereItWrote` and `...WritesTheFileItReports`; `DestinationFileNotWritable` and `DiskFilledDuringBackup` by `backup/04` and `backup/03` |
| 2 | ✅ | An unrecognised failure reports as `Unclassified` and carries the underlying error | Unit test | `DatabaseBackupOutcomeTests.CopyFailureThatIsNotASqliteError_IsReportedAsUnclassified` and `..._CarriesTheUnderlyingError` |
| 3 | ✅ | The content load at startup refuses rather than proceeding unprotected, naming the variant | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentLoadWithNoBackupPossible_IsRefused`, `..._NamesTheObstacle`, `..._LoadsNothing`, and `...ContentLoadWithAnUnwritableBackupDestination_NamesTheObstacle` |
| 4 | ✅ | Reset refuses with a stated failure, never an unhandled 500, and does not rebuild | Unit test | `AdminEndpointsTests.ResetDatabase_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500`, `..._NamesTheObstacle`, `..._DescribesTheCause`, `..._OffersARemedy`; `DatabaseBackupQuotaTests.ResetAsync_WhenNoBackupCanBeTaken_Refuses`, `..._NamesTheObstacle`, `..._NeverReachesTheDestructiveStep` |
| 5 | ✅ | The override is offered and forwarded only where the action can complete without a backup | Unit test | `AdminEndpointsTests.ResetDatabase_WhenTheQuotaIsFull_OffersTheOverride`, `...WhenTheSourceIsUnreadable_DoesNotOfferTheOverride`, `...WhenTheSourceIsUnreadable_OffersReplacingTheFile`, `...WhenTheOverrideWasTriedAndStillRefused_DoesNotOfferItAgain`, `...WithOverride_ForwardsTheOverride`; `DatabaseBackupQuotaTests.ResetAsync_WithTheOverride_ReportsThatTheBackupWasSkipped` |
| 6 | ❌ | A skipped backup is recorded in the log **and** the audit trail | Unit test | `AdminEndpointsTests.ResetDatabase_WithOverride_WritesAnAuditEntryRecordingTheSkip` (red in step 7) and step 13's log test |
| 7 | ✅ | Every #348 test asserts one statement, and each is red against the state before its change | Unit test | Step 7: states A to E, every test failing on an assertion; the tests that could not fail removed |
| 8 | ✅ | A caller can ask whether a backup is possible without attempting one, and the answer agrees with an attempt | Unit test | `DatabaseBackupPreflightTests.CheckBackupReadiness_WhenTheBudgetIsExhausted_ReportsBudgetExceeded`, `..._AgreesWithTheAttempt`, and the same pair for an unwritable destination |
| 9 | ✅ | The operating quota is honoured, the reserve is reachable only by override, the ceiling never is | Unit test | `DatabaseBackupQuotaTests.UsageAtTheQuota_IsRefused_WithoutReachingIntoTheReserve`, `...UsageAtTheAbsoluteCeiling_IsRefusedEvenWithTheReserveAllowed`; `DatabaseBackupPreflightTests.CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed` |
| 10 | ✅ | The quota percentage is configurable and defaults to 90 | Unit test | `DatabaseBackupQuotaTests.ConfiguredQuotaPercent_IsTheLimitTheCheckRefusesOn` and `...QuotaPercent_DefaultsTo90` |
| 11 | ✅ | An out-of-range percentage is reported and the default used, never clamped, never fatal | Unit test | `DatabaseBackupQuotaTests.QuotaPercent_OutOfRange_UsesTheDefault` and `...QuotaPercent_OutOfRange_IsReported` |
| 12 | ✅ | Each variant states cause and remedy, and names no remedy that cannot work | Unit test | `BackupObstacleGuidanceTests`: `EveryObstacle_HasACause`, `EveryObstacle_HasARemedy`, `RecognisedObstacle_IsNotDescribedAsTheUnrecognisedFallback`, `BudgetExceeded_OffersTheOverride`, `BudgetExceeded_OffersRemovingBackupsThroughTheApplication`, `SourceUnreadable_DoesNotOfferTheOverride`, `OverrideAlreadyTried_DoesNotRepeatTheOverride`, `OverrideAlreadyTried_KeepsTheOtherRemedies` |
| 13 | ✅ | The two tests whose expectation changed were renamed to their new contract, not bent to pass | Unit test | Recorded in step 2: both renamed, each with a comment on what changed and why |
| 14 | ✅ | The corrupt and truncated databases are recoverable end to end | Automated (T2) | `backup/01` and `backup/02`, executed 2026-08-28, each ending with the remedy proven by a `200` |
| 15 | ❌ | A pending migration with no backup possible applies nothing and reports the obstacle and the step | Unit test | Step 8's test, and its positive counterpart that migrates when a backup is possible |
| 16 | ❌ | A startup migration refusal marks the database unhealthy, naming the variant, remedies and entry | Unit test | Step 9's `WebApplicationFactory` test, and its healthy counterpart |
| 17 | ❌ | A startup content-load refusal leaves the app healthy and raises one `BackupRefused` notification | Unit test | Step 9's `WebApplicationFactory` test, and its counterpart raising none when a backup is possible |
| 18 | ❌ | The new payload kind is accepted by the migration and the baseline alike | Unit test | The schema-drift and CHECK-value tests in `DatabaseInitializerOwnershipTests`, extended |
| 19 | ❌ | Each option is offered exactly when it can run, and withheld otherwise, per obstacle | Unit test | Step 11's executor tests: every option, offered and withheld |
| 20 | ❌ | The offered options are visible over REST | Unit test | `NotificationEndpointsTests`: `availableActions` lists exactly what the executor offers |
| 21 | ❌ | A reseed from the notification backs up first, and runs without one only with the user's permission | Unit test | Step 12's executor tests: effect and refusal for each option, including #304's reseed recommendation |
| 22 | ❌ | The Knowledgebase entry covers every obstacle, and the rendered link resolves to it | Unit test | Step 14's repository test |
| 23 | ❌ | The notification renders offered options only, asks before reseeding without a backup, and both surfaces render the entry link | Unit test | `NotificationTableTests` and a `StartupErrorModal` component test |
| 24 | ❌ | Startup content-load refusal, end to end | Automated (T2) | Step 16's first document, red against the canary build |
| 25 | ❌ | Startup migration refusal, end to end | Automated (T2) | Step 16's second document, red against the canary build |
| 26 | ❌ | Every test this issue adds or changes fails against its signature state, on an assertion | Unit test | Steps 7 to 15, each recording its red run |
| 27 | ❌ | Build clean and the full suite green across three `-m:1` runs | Build | Step 18 |

---

## Design note: an out-of-range quota percentage must not crash

The issue calls an out-of-range value "a configuration error, not something to clamp silently". Taken
literally that suggests throwing, which would breach the never-crash contract this milestone is built
around. The project's existing precedent is the opposite extreme: an unrecognised `Quotinator:LogLevel`
falls back to `Information` silently. Neither is right here, and the precedent is not adopted because it
exists. Resolved: a warning naming the value and the accepted range, and the default used.
