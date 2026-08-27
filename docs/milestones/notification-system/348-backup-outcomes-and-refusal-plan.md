# #348 — Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable

**Status:** In progress
**GitHub issue:** #348
**Tiers required:** T1, T2
**Depends on:** none — found by #327, blocks #327's two remaining degradation documents

---

## Description

A backup exists to make a startup or a destructive admin action safe. When one cannot be taken the
application does one of two wrong things depending on which obstacle it hit: it proceeds silently
without a backup, or it throws an undifferentiated exception that reaches the caller as an unhandled
`500`. Neither says *which* obstacle occurred, and the five obstacles have five different remedies.

The sharpest symptom: on a corrupt or truncated database `/health` returns `503` telling the operator to
run a database Reset, and that exact call returns `500`. The advertised remedy is the one that cannot
run.

Found by [#327](https://github.com/DutchJaFO/Quotinator/issues/327) while measuring its requirement 4 —
not just whether a stated recovery route is reachable, but whether it can succeed.

---

## Next action

**Write the red tests named in the checklist below, before any more implementation.**

Part of the shape is already written on the `feature/notification-system` branch, from before the work
was split out of #327: `BackupOutcome`, `DatabaseBackupResult`, and `CreateBackup` rewritten to attribute
each failure structurally. That code is uncommitted and the build is red — three call sites still expect
the old `string?`. It was written ahead of its tests, which is the wrong order; the checklist and the
red tests come first, and the existing code is then held to them rather than assumed correct because it
exists.

---

## Design

Settled with the developer 2026-08-27; see #327's plan doc for the measurement that produced it.

### The five variants

Read off `CreateBackup`'s control flow rather than from the two that happened to surface first.

| # | Cause | Today | Endpoint-resolvable? |
|---|---|---|---|
| 1 | Backups folder would exceed `MaxBackupStorageGb` | warn, `return null`, proceed | Yes, via [#349](https://github.com/DutchJaFO/Quotinator/issues/349) |
| 2 | Free disk space below the estimate | warn, `return null`, proceed | Partly — deleting backups reclaims some |
| 3 | `Directory.CreateDirectory(BackupsPath)` throws | `DatabaseBackupWriteException` | No — restore write access |
| 4 | `dest.Open()` throws | `DatabaseBackupWriteException` | No — same remedy as 3 |
| 5 | `connection.BackupDatabase(dest)` throws — source unreadable | `DatabaseBackupWriteException` | No — no backup is possible |

### Attribution is structural

The three statements inside the old `try` are sequential and independent, so each is attempted on its
own and the failing statement names the fault. By the time the copy runs, the destination is already
proven creatable and openable — which is what makes a failure there the source's.

One residual ambiguity is named rather than hidden: a disk that fills *during* the copy fails at the
same statement as an unreadable source. That one splits on `SqliteException.SqliteErrorCode` — a typed
code, not a parsed message. Anything unrecognised is `Unclassified`, carrying the underlying error,
because an unnamed variant is an unanswered question and a wrong name is worse than no name.

### Quota: two levels, not a prediction

The current arithmetic decides a hard yes/no from `estimatedBytes`, the *source file's* length. SQLite
copies pages, so that is an approximation of the result, not a measurement of it.

| Level | Value | Meaning |
|---|---|---|
| Operating quota | `BackupQuotaPercent`, default 90% | What normal operation uses |
| Absolute ceiling | `MaxBackupStorageGb` | Never exceeded |

The reserve between them is what makes the unpredictable size safe to live with: an operator at the
normal quota still has room for one more backup at the moment a Reset is about to run. Using it requires
the override, never a default.

### Refusal and override

Reset refuses rather than running its destructive step unprotected, returning a stated failure naming the
variant and its remedy — never an unhandled `500`. The override means the operator accepts
responsibility **and** the action can complete without a backup. A skipped backup is recorded twice: a
warning log line, and an audit entry, so nobody hunts for a backup that was never made.

### How a refusal travels — a result, not an exception

**Developer direction, 2026-08-27**, correcting this plan's first reading:

> *"We only use exceptions when there is no other method of detecting the issue. Anything else simply
> needs a response that declares success/failure + reason. This is why we use the status check to see if
> we can do a backup. We still handle exceptions as despite the status check we may still get
> exceptions."*

So the order is **check, then act, then still catch**:

1. **Check.** The pre-flight reports whether a backup can be taken, in the variant vocabulary. This is
   the normal path and it involves no exception, because a full backup folder is an ordinary operating
   condition rather than an exceptional one.
2. **Act.** `CreateBackup` returns a result declaring success or failure plus the reason. A caller that
   must refuse returns its own failure result; it does not throw to communicate a condition it already
   knows about.
3. **Still catch.** The check cannot foresee everything — the state can change between checking and
   acting, and a genuinely unforeseen failure is exactly what an exception is for. Exceptions remain
   handled, as the backstop rather than as the mechanism.

**The endpoint contract, same direction:** `200` means the endpoint did what was asked. Any other status
is an error whose **response content carries the reason and the potential solutions, if any**. A refusal
is therefore a non-2xx with the variant, its cause, and the remedies — naming which are actionable
through [#349](https://github.com/DutchJaFO/Quotinator/issues/349)'s endpoints and which are not.

This replaces the "typed exception caught at the endpoint boundary" this plan first proposed. That shape
would have made an expected, recoverable condition travel as an exception purely because the existing
startup path already threw — reasoning from what the code does rather than from what the condition is.

---

## Steps

### 1. Write the verification checklist

**Status:** ✅ Written — 14 rows, one per requirement, plus the design note the out-of-range case needed

Every requirement in the issue gets a row, per `process.md`'s Planning step 5.

### 2. Write the red tests

**Status:** ⬜ Not started

The fourteen named in the issue, plus the two whose expectation changes deliberately
(`CreateBackup_InsufficientStorageSpace_SkipsWithWarningNotException` and
`InitialiseAsync_BackupWriteFails_SurfacesDistinctFailureReason`). Confirm each is genuinely red before
any of the existing uncommitted code is kept.

### 3. Finish the outcome type and the call sites

**Status:** ✅ Attribution done and held to tests; the refusal it enables is step 4

`BackupOutcome`, `DatabaseBackupResult` and `CreateBackup`'s structural attribution are in, all three
call sites compile, and the solution builds at 0 warnings. `CreateBackup` is `internal` rather than
private so `Quotinator.Data.Tests` can put each variant to its own assertion directly; driving five
different failure states through a full initialisation would test the plumbing rather than the
attribution, and two of them need injected providers regardless.

**`DatabaseBackupOutcomeTests` — 5 tests, and each was shown able to fail.** They passed on first run,
because the implementation was written before them (the wrong order, recorded in this plan's Next
action). Passing was therefore not evidence of anything, so attribution was collapsed to a single
`Unclassified` for every failure path and the suite re-run: **all four variant tests failed and the
success control still passed**, which is what the discrimination claim actually rests on.

**One deliberate red is now outstanding, and it must not be "fixed" by editing its expectation.**
`DatabaseInitializerTests.InitialiseAsync_BackupWriteFails_SurfacesDistinctFailureReason` fails with
*"Expected exception of exact type DatabaseBackupWriteException but no exception was thrown"*. That is
correct and expected at this point: the destination-failure path no longer throws, and nothing has yet
replaced the throw with a refusal — so startup currently **proceeds unprotected**, which is exactly the
defect requirement 2 names. The test is the red for step 4; it goes green when the refusal lands, not
before, and not by changing what it asserts.


### 4. Reset refusal, override, logging and audit

**Status:** ⬜ Refusal and the pre-flight are in; the override's audit entry and the endpoint response are not

Includes a new `AuditOperation.BackupSkipped`.

### 5. The quota model

**Status:** ⬜ Not started

`BackupQuotaPercent` on `DatabaseOptions`, validated rather than clamped.

### 6. Remedy text per variant

**Status:** ⬜ Not started

Each states symptom, cause and remedy — the property #333's sweep needs to write a Knowledgebase entry
later. No `QTN-` code is allocated here; see the Scope boundary in the issue.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every backup attempt reports which of the five obstacles it hit | Unit test | `DatabaseBackupOutcomeTests.BudgetExceeded_IsReportedAsBudgetExceeded`, `...InsufficientDiskSpace_IsReportedAsInsufficientDiskSpace`, `...UnwritableBackupsDirectory_IsReportedAsDestinationDirectoryNotWritable`, `...CorruptSourceDatabase_IsReportedAsSourceUnreadable`, plus `...SucceedingBackup_ReportsSucceededAndTheFileItWrote` as the control. Each shown able to fail by collapsing attribution to one outcome — 4 of 5 failed, the control correctly did not |
| 2 | ❌ | An unrecognised failure reports as `Unclassified` and carries the underlying error, rather than being folded into the nearest named variant | Unit test | `DatabaseBackupOutcomeTests.UnrecognisedCopyFailure_IsReportedAsUnclassified_CarryingTheUnderlyingError` — not yet written. `ClassifyCopyFailure` returns `Unclassified` for any code other than 26/11/13, but an unexercised branch is not a verified one |
| 3 | ✅ | Startup refuses rather than proceeding unprotected, naming the variant | Unit test | `DatabaseInitializerTests.CreateBackup_InsufficientStorageSpace_RefusesToSeedRatherThanProceedUnprotected` and `...InitialiseAsync_BackupWriteFails_ReportsTheObstacleRatherThanThrowing` — both assert the result's `BackupObstacle` and that the database is left untouched. Reporting the variant *to the operator* as a degraded health reason is the Api layer's half and is row 12 |
| 4 | ❌ | Reset refuses, with a stated failure rather than an unhandled 500, and does not rebuild | Unit test | `AdminEndpointsTests.Reset_WhenNoBackupCanBeTaken_RefusesWithAStatedFailureRatherThanAnUnhandled500`, `...ResponseNamesTheCauseAndItsRemedy`, `...DoesNotRebuildTheDatabase` |
| 5 | ❌ | The override proceeds, and only where the action can complete without a backup | Unit test | `AdminEndpointsTests.Reset_WithOverride_ProceedsAndRebuilds` |
| 6 | ❌ | A skipped backup is recorded in the log **and** the audit trail | Unit test | `AdminEndpointsTests.Reset_WithOverride_LogsThatTheBackupWasSkipped`, `...WritesAnAuditEntryRecordingTheSkip`. `AuditOperation.BackupSkipped` needs no migration — `Audit_Entry.Operation` is `TEXT NOT NULL` with no CHECK constraint (verified 2026-08-27), so ADR 008's checklist does not apply |
| 7 | ❌ | A healthy database is entirely unaffected | Unit test | `AdminEndpointsTests.Reset_WhenBackupSucceeds_IsUnchanged` — the regression control for the whole change |
| 8 | ❌ | A caller can ask whether a backup is possible without attempting one, in the same variant vocabulary | Unit test | `DatabaseBackupPreflightTests.CanCreateBackup_WhenNothingObstructsIt_ReportsThatItCan`, `...WhenBudgetIsAlreadyExhausted_ReportsTheVariantAnAttemptWouldReport`, `...WhenTheDestinationIsNotWritable_ReportsTheVariantAnAttemptWouldReport` |
| 9 | ❌ | The operating quota is honoured, the reserve is reachable only by override, and the ceiling never is | Unit test | `DatabaseBackupQuotaTests.UsageBelowTheQuota_TakesTheBackupWithoutComment`, `...UsageAtTheQuota_IsReported_AndDoesNotUseTheReserveByDefault`, `...UsageAtTheQuota_WithOverride_UsesTheReserveAndTakesTheBackup`, `...UsageAtTheAbsoluteCeiling_IsRefusedEvenWithOverride` |
| 10 | ❌ | The quota percentage is configurable and defaults to 90 | Unit test | `DatabaseBackupQuotaTests.QuotaPercent_IsConfigurable_AndDefaultsTo90` |
| 11 | ❌ | An out-of-range percentage is reported loudly and the default used — never silently clamped, and never a crash | Unit test | `DatabaseBackupQuotaTests.QuotaPercent_OutOfRange_IsAConfigurationErrorNotSilentlyClamped`. See the design note below: throwing would breach the never-crash contract, so "not silently" is satisfied by a warning naming the value and the accepted range |
| 12 | ❌ | Each variant's message states symptom, cause and remedy | Live | Read per variant. The property #333's sweep needs in order to write a Knowledgebase entry without guesswork; no `QTN-` code is allocated here, per #333 requirement 8's precedent |
| 13 | ✅ | The two existing tests whose expectation changes are updated deliberately, not to make a red run pass | Unit test | Both rewritten with names stating the new contract, not edited assertions under old names: `CreateBackup_InsufficientStorageSpace_SkipsWithWarningNotException` → `..._RefusesToSeedRatherThanProceedUnprotected`, and `InitialiseAsync_BackupWriteFails_SurfacesDistinctFailureReason` → `..._ReportsTheObstacleRatherThanThrowing`. Each carries a comment saying what changed and why, so neither reads as a test bent to fit |
| 14 | ❌ | The corrupt and truncated databases that started this are actually recoverable end to end | Live | T2: with the fix in place, a corrupt database's Reset succeeds or refuses with a workable remedy — the measurement #327 made, re-run |

---

## Design note — an out-of-range quota percentage must not crash

The issue says an out-of-range value is "a configuration error, not something to clamp silently".
Taken literally that suggests throwing, which would breach the never-crash contract this milestone is
built around: a typo in one tuning value must not stop the application starting.

The project's existing precedent is the opposite extreme — an unrecognised `Quotinator:LogLevel` falls
back to `Information` **silently**, with no warning at all (`Program.cs`'s `switch`). Neither extreme is
right here, and the precedent is not adopted just because it exists.

**Resolved:** report it loudly — a warning naming the supplied value and the accepted range — and use
the default. That is not a silent clamp, and it is not a crash. The triage question in
`docs/knowledgebase.md` supports treating it this way: a wrong quota percentage does not prevent the
application or the API from functioning.
