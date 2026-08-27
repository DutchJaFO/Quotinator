# #348 — Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable

**Status:** Planning
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

**Write the verification checklist and the red tests named in the issue, before any more implementation.**

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

---

## Steps

### 1. Write the verification checklist

**Status:** ⬜ Not started

Every requirement in the issue gets a row, per `process.md`'s Planning step 5.

### 2. Write the red tests

**Status:** ⬜ Not started

The fourteen named in the issue, plus the two whose expectation changes deliberately
(`CreateBackup_InsufficientStorageSpace_SkipsWithWarningNotException` and
`InitialiseAsync_BackupWriteFails_SurfacesDistinctFailureReason`). Confirm each is genuinely red before
any of the existing uncommitted code is kept.

### 3. Finish the outcome type and the call sites

**Status:** ⬜ Partly written, uncommitted, unheld to any test

`BackupOutcome`, `DatabaseBackupResult` and `CreateBackup`'s attribution exist; the three call sites do
not compile against them yet.

### 4. Reset refusal, override, logging and audit

**Status:** ⬜ Not started

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

**Not yet written — this is step 1, and implementation does not resume until it exists.** Recorded as an
explicit gap rather than an empty table, so the doc's own state says what the next action is.
