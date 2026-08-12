# #277 — Gate startup backups on each action's own real-work signal, not an inferred flag; add a storage pre-flight check

**Status:** Released
**GitHub issue:** #277
**Tiers required:** T1, T2
**Depends on:** none

---

## Background

Split from #267's investigation, tracked under parent #276. `RunInitialisedHookAsync`
(`src/Quotinator.Data/Database/DatabaseInitializer.cs`) takes a full pre-seed database backup on
every non-baseline startup, unconditionally — including an ordinary restart of an already-populated
database, where the content-seed step's own count-gates (`SeedIfEmptyInternalAsync`/
`ReSeedGenresIfEmptyAsync`) guarantee no real work happens at all. A first fix attempt (gate on
`IDatabaseInitializer.MigrationApplied`) was found to have a real gap: `DropAndRebuildAsync` (Reset)
sets schema-version counters directly via the baseline path, so `MigrationApplied` stays `null` on the
startup immediately following a Reset — even though Reset also wipes `Quotes` and never reseeds it, so
real seeding writes *do* happen on that same startup. A flag-based gate would have stripped backup
protection from exactly the highest-risk case.

**The corrected model:** every action in the three startup/reset flows (normal startup, fresh install,
Reset) reduces to the same shape — **can we perform it → back up → execute**. Gating each action's
backup on *that action's own* real-work determination (migrate already does this correctly today via
`dataPending`/`consumerPending`; content-seed needs the same treatment using its own count check,
evaluated fresh right before the backup decision) closes the Reset gap directly, since content-seed's
own count check correctly reads `0` right after a Reset.

**Storage pre-flight check — developer-confirmed design (2026-08-10).** `CreateBackup` has no check
today for available disk space before writing a backup file. Two independent AskUserQuestion options
were presented — (a) a real `DriveInfo`-backed free-space check via a new injectable interface, or
(b) a simpler self-imposed budget computed from the backups folder's own accumulated size, no new
interface — and the developer chose **both**: "real disk space and budget, because we should never
exceed our budget." So the pre-flight check has two independent conditions, either of which is enough
to skip:

1. **Budget** — the backups folder must never grow past a configurable cap (`MaxBackupStorageGb`,
   default `1`, sized from the current representative database size: ~8 MB × 10 backups = 80 MB,
   rounded up to a clean, convenient 1 GB). This is a hard, self-imposed ceiling — "we should never
   exceed our budget" — independent of how much real disk space happens to be free.
2. **Real free space** — even within budget, a genuinely full disk must not be written to. Checked via
   a new `IDiskSpaceProvider` abstraction (real `DriveInfo.AvailableFreeSpace`), so the check is
   unit-testable with a fake instead of requiring an actually-full test disk.

On either failure: log a `Warning` and proceed **without** a backup — backups are a safety feature, not
a durable archive (a user wanting real backups is responsible for storing them elsewhere), so failing
the whole startup over an inability to take a safety-net copy is wrong.

**Failure at any step needs a clear, distinguishable message, not a raw exception.** The storage
pre-flight check failing is non-fatal by design (warn, skip backup, proceed — never a `FailureReason`).
A failure actually *writing* the backup file, or a failure during the real `execute` step (migrate /
seed-mandatory / seed-content), each need their own distinguishable reason surfaced through
`DatabaseHealthState.FailureReason` (built by #263) — not one generic bubbled-up exception message for
both.

## Approach

### Content-seed real-work gate

New `protected virtual Task<bool> HasPendingContentSeedAsync(SqliteConnection connection)` on
`DatabaseInitializer` (Quotinator.Data, domain-agnostic — the base class has no knowledge of what a
consumer actually seeds). Default implementation conservatively returns `true` (always back up), since
a subclass that doesn't override it has no cheaper signal than "always protect." `RunInitialisedHookAsync`
calls this immediately after the existing `tookBaselinePath` check and, when it returns `false`, calls
`OnInitialisedAsync` directly with no backup/restore wrapper at all — mirroring the shape of the
existing `tookBaselinePath` shortcut.

`QuotinatorDatabaseInitializer` overrides it, mirroring `OnInitialisedAsync`'s own two count-gates
exactly:
```csharp
protected override async Task<bool> HasPendingContentSeedAsync(SqliteConnection connection)
{
    var quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
    if (quoteCount == 0) return true;                                          // SeedIfEmptyInternalAsync would do real work
    return await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll) == 0; // ReSeedGenresIfEmptyAsync would do real work
}
```

### Storage pre-flight check

- New `IDiskSpaceProvider`/`DiskSpaceProvider` (Quotinator.Data) — `GetAvailableFreeSpaceBytes(string path)`
  wrapping `DriveInfo`.
- New optional trailing constructor parameter on `DatabaseInitializer`:
  `IDiskSpaceProvider? diskSpaceProvider = null`, placed **after** the existing `baseline` parameter
  (not before it) so every one of the ~17 existing call sites across the codebase that construct
  `QuotinatorDatabaseInitializer`/`DatabaseInitializer` and stop at `baseline` keep compiling unchanged
  — only `Program.cs` (real DI registration + resolution, matching how every other dependency here is
  wired) and the new storage-specific tests in `DatabaseInitializerTests` pass a value explicitly.
  Defaults internally to a real `DiskSpaceProvider` when `null`.
- New `DatabaseOptions.MaxBackupStorageGb` (default `1`), overridable via
  `Quotinator:MaxBackupStorageGb`, following the `Quotinator:SourceRefreshTimeoutSeconds` precedent
  (`44ffb1e`).
- `CreateBackup` becomes nullable-returning (`string?`). Before writing, checks both conditions and
  skips (logs `Warning`, returns `null`) if either fails:
  1. **Budget**: sum of existing file sizes already in `BackupsPath`, plus the live database's current
     file size (the estimate for the new backup — a backup mirrors the live file), must not exceed
     `MaxBackupStorageGb`.
  2. **Real free space**: `IDiskSpaceProvider.GetAvailableFreeSpaceBytes(BackupsPath)` must be at least
     the live database's current file size.
- All three `CreateBackup` call sites (`RunInitialisedHookAsync`, `ApplyMigrationsAsync` — already
  nullable today, `DropAndRebuildAsync`) guard their own `RestoreBackup` call behind
  `backupPath is not null`.
- No pruning/retention of old backup files is introduced — out of scope for this issue (the pre-flight
  check only ever prevents a *new* backup from being written once the budget/space is exhausted; it
  never deletes existing ones). Flag as a candidate for a future issue if this becomes a real problem
  in practice, not decided here.

### Reset and fresh-install paths

- `DropAndRebuildAsync`'s existing unconditional backup call now goes through the same two pre-flight
  checks inside `CreateBackup` — correct, since Reset is the highest-risk operation and should still
  attempt a backup whenever the checks allow one, but must not crash startup if they don't.
- `ApplyBaselineAsync`'s fresh-install path is untouched — it never calls `CreateBackup` at all today
  (nothing to lose on a genuinely empty database), and nothing here changes that.

### Distinguishable failure reasons

- New `DatabaseBackupWriteException` (`Quotinator.Data.Database`) — thrown by `CreateBackup` when the
  SQLite backup API itself throws *after* both pre-flight checks passed (a real I/O failure, not a
  skip).
- `Program.cs`'s DB-init catch block gets a new `catch (DatabaseBackupWriteException ex)` **before**
  the existing generic `catch (Exception ex)`, each producing its own `FailureReason` text via
  `DatabaseHealthState.MarkFailed` — the existing generic catch's message stays as the "execute step
  failed" reason; the new catch gets a distinct, backup-specific message.
- The storage pre-flight check itself never throws and never sets `FailureReason` — `Warning`-level log
  only, by design (non-fatal, proceeds without a backup).

### New logging

Two new `[LoggerMessage]` entries in `Quotinator.Data.Logging.LogMessages` (`Warning` level, matching
the boyscout `[Subsystem - Phase]` prefix convention): one for budget-exceeded, one for insufficient
real disk space — both fire from `CreateBackup`, distinct text so a real operator can tell which
condition was hit.

### Test doubles

New `NoOpDiskSpaceProvider` in `Quotinator.Data.Testing/NoOps/` (returns `long.MaxValue` — "plenty of
space"), mirroring every other injectable interface's test-double precedent, for explicit/discoverable
use in tests that want to state "storage is never the concern here" without relying on the constructor
default.

---

## Steps

### 1. Content-seed real-work gate (`HasPendingContentSeedAsync`)
**Status:** ✅ Done

### 2. Storage pre-flight check (`IDiskSpaceProvider`, `MaxBackupStorageGb`, nullable `CreateBackup`)
**Status:** ✅ Done

### 3. Confirm Reset/fresh-install paths hold under the restructured model
**Status:** ✅ Done — `InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup` exercises Reset's own
backup through the same pre-flight checks; `ApplyBaselineAsync` untouched.

### 4. Distinguishable failure reasons (`DatabaseBackupWriteException`, `Program.cs` catch split)
**Status:** ✅ Done

### 5. `RunInitialisedHookAsync`'s own code comment updated for the corrected three-flow model
**Status:** ✅ Done

### 6. `Quotinator:MaxBackupStorageGb` wired in `Program.cs`, `IDiskSpaceProvider` DI-registered
**Status:** ✅ Done

### 7. Full verification (T1, T2)
**Status:** ✅ Done

**T1 confirmed (2026-08-10):** developer's own Visual Studio run, against a real populated database
(schema v6, data v8, 799 quotes, 461 sources, existing history) — clean startup, no errors. Log shows
`schema is up to date` with no `[Database - Backup]` line: a healthy restart against real data takes
no backup, confirming the core fix on production-shaped data, not just a synthetic test/Docker
scenario.

**T2 confirmed (2026-08-10):** `docker build` succeeded; five scenarios verified live against a real
persistent volume:
1. Fresh baseline install — no backup (log shows only the baseline-creation lines, no
   `[Database - Backup]`).
2. Healthy restart of an already-seeded database — `schema is up to date`, no backup, `/data/backups`
   never even created.
3. `POST /api/v1/admin/database/reset` — exactly one backup taken.
4. Restart immediately after that Reset — a second backup taken before seeding, confirming the exact
   case a `MigrationApplied`-based gate was found to miss (schema already up to date, but content-seed
   genuinely has real work to do).
5. `Quotinator:MaxBackupStorageGb=0` against an already-populated backups folder — the next Reset still
   succeeds (200, database rebuilt), but the backup itself is skipped with a
   `LogBackupSkippedBudgetExceeded` warning, not an exception; backup file count stays unchanged.

**Regression found and fixed during the full-suite check (2026-08-10):** `HasPendingContentSeedAsync`
queries the same domain tables `OnInitialisedAsync` itself would seed — if that query throws (e.g. the
existing `InitialiseAsync_SeedingFailsOnAlreadyMigratedDatabase_BacksUpFirstAndRethrows` test's
scenario, which drops `Quotinator_Quote` entirely to simulate a structurally broken database), the
exception propagated *before* `CreateBackup` was ever reached — the real-work determination itself was
skipping backup protection at exactly the moment it matters most. Fixed via
`SafeHasPendingContentSeedAsync`, a wrapper that catches any exception from the real check and treats
it as "assume pending, take the backup" rather than letting it propagate unprotected. Re-ran the full
suite after the fix: 1074 Data.Tests + 1445 Core.Tests + 664 Api.Tests, 0 failures — the pre-existing
test now passes again, and none of #277's own 8 new tests were affected by the fix (a healthy restart
with genuinely no pending work still correctly determines that without ever entering the try/catch
path at all).

**Implementation notes (test design, 2026-08-10):**
- `BackupFileCount()` (test helper) counts only `*.db` files, not `-shm`/`-wal` sidecars — a WAL-mode
  backup that later gets reopened for a restore (the `InitialiseAsync_ExecuteStepFails_...` test) can
  leave transient sidecar files behind; counting them inflated the apparent backup count from 1 to 3
  until this was diagnosed and fixed.
- `InitialiseAsync_MigrationPending_TakesBackup` cannot truncate `QuotinatorMigrations.All` to force a
  "pending migration" state — its own last entry is the domain-prefix rename to `Quotinator_Quote`, so
  any shorter prefix leaves the old table names in effect and every later query in the test fails with
  `no such table: Quotinator_Quote`. Fixed by appending a harmless extra no-op migration instead of
  truncating.
- `ThrowingAuditEntryWriter` (the execute-step-failure test double) must throw from the
  connection-taking `WriteAsync(entry, connection, transaction)` overload, not the standalone
  `WriteAsync(entry)` — `SeedIfEmptyInternalAsync`'s own successful-apply audit write passes the live
  `connection`, so the standalone overload is never actually invoked at that call site.
- `SimpleQuoteBatch()` (test helper) must include an explicit `id` and a `genres` array — an id-less
  quote and a database with 0 `Quotinator_QuoteGenre` rows makes `HasPendingContentSeedAsync`'s own
  genre count-gate perpetually read "pending" even after a successful seed, since there is no genre
  data to reseed from.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An already-seeded database restart takes no backup | Unit test | `DatabaseInitializerTests.InitialiseAsync_AlreadySeeded_TakesNoBackup` |
| 2 | ✅ | A database needing content-seed work takes a backup | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentSeedNeeded_TakesBackup` |
| 3 | ✅ | The startup immediately after a Reset (content-seed needed, `MigrationApplied` null) still takes a backup | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup` |
| 4 | ✅ | A database with a pending migration takes a backup | Unit test | `DatabaseInitializerTests.InitialiseAsync_MigrationPending_TakesBackup` |
| 5 | ✅ | Insufficient storage (budget or real free space) skips the backup with a warning, not an exception | Unit test | `DatabaseInitializerTests.CreateBackup_InsufficientStorageSpace_SkipsWithWarningNotException` |
| 6 | ✅ | Sufficient storage proceeds normally | Unit test | `DatabaseInitializerTests.CreateBackup_SufficientStorageSpace_ProceedsNormally` |
| 7 | ✅ | A backup-write failure surfaces its own distinct `FailureReason`, distinguishable from an execute-step failure | Unit test | `DatabaseInitializerTests.InitialiseAsync_BackupWriteFails_SurfacesDistinctFailureReason` |
| 8 | ✅ | An execute-step (migrate/seed) failure surfaces its own distinct `FailureReason` | Unit test | `DatabaseInitializerTests.InitialiseAsync_ExecuteStepFails_SurfacesDistinctFailureReason` |
| 9 | ✅ | Reset's own backup still goes through the same pre-flight checks | Unit test | Covered by the same `CreateBackup`-level tests (5, 6) — `DropAndRebuildAsync` calls the same method |
| 10 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 11 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` — 1074 Data.Tests + 1445 Core.Tests + 664 Api.Tests, 0 failures |
| 12 | ✅ | T1 (developer's own Visual Studio run) | Live | Clean startup against real populated database; no backup taken on healthy restart |
| 13 | ✅ | T2 (Docker smoke tests) | Live | 2026-08-10 — see Step 7 for the five scenarios verified |

---

## Relationship to existing issues

- **#267** — original investigation this issue was split from.
- **#276** — parent tracking issue for #277/#278.
- **#278** — sibling sub-issue of #276; both surface via `DatabaseHealthState`/the notification system, no hard dependency either direction.
- **#263** — built `DatabaseHealthState.FailureReason`, which this issue's requirement 4 extends with distinguishable reasons.
