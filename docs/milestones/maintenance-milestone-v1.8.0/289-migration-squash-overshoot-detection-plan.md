# #289 — Squash unshipped migrations since v1.8.2, add schema-version-overshoot detection + notification

**Status:** Released
**GitHub issue:** #289
**Tiers required:** T1, T2
**Depends on:** #288 (verification confirmed the 8 migrations were safe from a tagged-release standpoint; this issue supersedes #288's own "considered and rejected" conclusion by developer decision — see Background)

---

## Background

#288 evaluated squashing the 8 migrations added since v1.8.2 and correctly rejected it: ADR 015's
revision (from #254's own incident) established that "unreleased" is not the right test for whether a
migration is safe to edit — the real test is whether *any* database, including a developer's own local
one, has already applied it. A direct check of the local Visual Studio startup log showed
`schema is up to date (data v8, app v6)`, proving the local dev database had already applied every one
of the 8 candidate migrations via each issue's own T1 pass earlier in this milestone.

**Developer decision, overriding that rejection:** squash anyway, with the local dev database being
reset as part of this same work, and a general safety net added for any *other* database in the same
already-migrated state (a second developer's machine, a CI cache) that isn't being reset alongside this
one. This issue is that combined change.

## Approach

Two independent but tightly-coupled pieces:

1. **Squash** — Consumer migrations 5-6 and Data migrations 3-8, each into one new migration,
   reference-concatenated (not literal text duplication) so the original named constants stay in place,
   unedited and independently referenceable — several are executed directly by tests to build fixtures.
   This mirrors #155's Data-side technique exactly; #155's Consumer-side technique (literal merge with
   the originals deleted) was **not** used here, since reference concatenation carries strictly less
   transcription risk and nothing requires deleting the originals.
2. **Overshoot detection** — `DatabaseInitializer.ApplyMigrationsAsync` previously had no detection at
   all for "recorded version exceeds the app's own known migration count" (only the "less than" /
   pending case was checked). After the squash, a database that already applied the pre-squash
   migrations reads as this exact state. Detected as a new `bool SchemaVersionOvershootDetected`
   property, surfaced via a new `Program.cs` notification producer (the second concrete producer for
   #278's notification mechanism, after #279) using the mechanism's own purpose-built
   `NotificationType.ActionRequired` / `NotificationDismissTrigger.DatabaseReset`.

## Files touched

- `src/Quotinator.Core/Database/QuotinatorMigrations.cs` — new `Migration005_ConsolidatedSinceV182`
- `src/Quotinator.Data/Database/DataConsolidatedMigrations.cs` — new `SinceV182`
- `src/Quotinator.Data/Database/DatabaseInitializer.cs` — `DataOwnedMigrations` shrinks to 3 entries;
  overshoot detection + `Math.Max` fix in `ApplyMigrationsAsync`; backup timestamp precision fix
- `src/Quotinator.Data/Database/IDatabaseInitializer.cs` — new `SchemaVersionOvershootDetected` property
- `src/Quotinator.Data/Logging/LogMessages.cs` — new `LogSchemaVersionOvershoot`
- `src/Quotinator.Api/Program.cs` — new notification producer block
- `src/Quotinator.Data.Testing/NoOps/NoOpDatabaseInitializer.cs` — new property, always `false`
- `docs/architecture-decisions/015-domain-prefixed-table-naming.md` — third confirmation entry
- Test fixture updates: `AdminEndpointsTests.cs`, `NotificationActionExecutorTests.cs`,
  `StartupSummaryLoggerTests.cs` (new interface member), `DatabaseInitializerTests.cs`,
  `ImportBatchesTests.cs`, `DatabaseInitializerOwnershipTests.cs` (updated hardcoded version counts),
  `ProgramNotificationSeedingRegressionTests.cs` (new positive wiring test)

## Steps

### 1. Squash Consumer migrations 5-6 into one

**Status:** Done.

`Migration005_ConsolidatedSinceV182 = Migration005_ImportBatchConflictPolicyCheckConstraint +
Migration006_DomainPrefixRename;` — both original constants untouched, referenced by name.
`QuotinatorMigrations.All` shrinks from 6 to 5 entries.

### 2. Squash Data migrations 3-8 into one

**Status:** Done.

`DataConsolidatedMigrations.SinceV182` reference-concatenates all 6 original constants in their
original order. `DatabaseInitializerOwnershipTests.cs`, `NotificationReaderTests.cs`, and
`NotificationWriterTests.cs` execute several of the originals directly to build fixtures — confirmed
unaffected, since none of the 6 were deleted or edited. `DataOwnedMigrations` shrinks from 8 to 3 entries.

### 3. Add schema-version-overshoot detection

**Status:** Done.

`SchemaVersionOvershootDetected` computed right after reading `dataCurrent`/`consumerCurrent`, before
the existing `dataPending`/`consumerPending` check. Logged via a new `LogSchemaVersionOvershoot`
warning in both the early-return ("both up to date") branch and the migration-apply branch.

**Bug found and fixed while implementing this step:** the migration-apply branch unconditionally set
`DataSchemaVersion = DataOwnedMigrations.Count` / `SchemaVersion = _consumerMigrations.Count` after
calling `ApplyMigrationPhaseAsync` — correct when that side had pending work, but wrong when the *other*
side has genuine pending work while *this* side is simultaneously overshooting (this side's
`ApplyMigrationPhaseAsync` call writes nothing in that case, since `current >= migrations.Count`,
leaving the true recorded value at `dataCurrent`/`consumerCurrent`, not the smaller known count).
Fixed to `Math.Max(dataCurrent, DataOwnedMigrations.Count)` / `Math.Max(consumerCurrent,
_consumerMigrations.Count)`, which is correct in all three cases (pending work applied, already exact,
overshoot).

### 4. Wire the notification producer

**Status:** Done.

New block in `Program.cs`, directly after #279's own block, following its exact shape: guarded by
`dbHealth.IsHealthy && dbInitializer.SchemaVersionOvershootDetected`, own `try`/`catch` (a failure here
must never mark the app unhealthy), `NotificationSeeding.SeedOnceAsync` with a dedupe key built from the
actual detected versions (`SchemaVersionOvershoot:data-v{N}-app-v{M}`) — a repeat of the same
already-notified state stays deduped across restarts, but a genuinely different future overshoot (a
later squash producing different version numbers) still gets its own notification.
`NotificationType.ActionRequired` + `NotificationDismissTrigger.DatabaseReset` — `POST
/admin/database/reset` already calls `DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset)`,
so this clears itself automatically once the operator resets.

### 5. Update every test hardcoding the old migration counts

**Status:** Done.

`ImportBatchesTests.Schema_MigrationVersion_IsBumped` (6→5), and five call sites across
`DatabaseInitializerTests.cs`/`DatabaseInitializerOwnershipTests.cs` (8→3, 6→5). Full audit via a
targeted grep for `AreEqual([68], ...SchemaVersion...)` after the fix — no further hits.

**Second bug found live while re-running the full suite after the count fixes:**
`InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup` failed — not a version-number assertion, a
genuine regression. `CreateBackup`'s filename used second-precision timestamps
(`yyyyMMddTHHmmss`); Reset's own backup and the following real init's backup happened to land on the
*same* `fromVersion` for the first time after the squash (previously 6 vs 8, now both 5), and — landing
within the same wall-clock second in a fast unit test — collided on an identical filename.
`SqliteConnection.BackupDatabase` silently overwrites an existing file at that path rather than
erroring, so the second backup was never actually creating a distinct file. Fixed by widening the
timestamp format to `yyyyMMddTHHmmssfff` (milliseconds) — a general fix, not specific to this one
version-number coincidence; any two same-version backups within the same second could always have
collided this way.

### 6. Add the dedicated overshoot/notification tests

**Status:** Done.

- `DatabaseInitializerTests.InitialiseAsync_RecordedVersionExceedsKnownMigrations_TreatsAsUpToDateAndFlagsOvershoot`
  — seeds a fake extra version row, confirms no exception, the flag is set, and the real (higher)
  recorded version is reported.
- `DatabaseInitializerTests.InitialiseAsync_NoOvershoot_FlagStaysFalse` — sanity check on two ordinary
  startups.
- `ProgramNotificationSeedingRegressionTests.Startup_SchemaVersionOvershootDetected_SeedsActionRequiredNotification`
  — a stub `IDatabaseInitializer` reporting overshoot, run through a real `WebApplicationFactory`
  startup, confirms the actual `Program.cs` wiring (not just `NotificationSeeding.SeedOnceAsync` in
  isolation, already covered by `NotificationSeedingTests`) seeds the expected message. Filters on the
  specific #289 message text, since #279's own unconditional notification also seeds in the same run.

### 7. Byte-diff every constant that predates this issue

**Status:** Done.

The 6 Data migration source files (`ImportConflictMigrations.cs`, `ImportActionMigrations.cs`,
`DomainPrefixRenameMigrations.cs`, `FileResourceMigrations.cs`,
`FileResourceOriginGeneralizationMigrations.cs`, `NotificationMigrations.cs`) show zero diff against
`HEAD` — confirmed via `git diff --stat`, not by eye. `Migration005_ImportBatchConflictPolicyCheckConstraint`
and `Migration006_DomainPrefixRename` (Consumer side, list membership changed but content must not)
diffed byte-for-byte against `HEAD` — both identical.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Consumer migrations 5-6 squashed into one; none of the 4 already-shipped migrations edited | Unit test + Live (review) | `QuotinatorMigrations.All` has 5 entries; byte-diff of Migration005/006 bodies vs HEAD — identical |
| 2 | ✅ | Data migrations 3-8 squashed into one; none of the 6 originals edited or deleted | Unit test + Live (review) | `DataOwnedMigrations` has 3 entries; `git diff --stat` on all 6 source files — zero diff |
| 3 | ✅ | A recorded version ahead of the known migration count is detected, not silently misreported | Unit test | `InitialiseAsync_RecordedVersionExceedsKnownMigrations_TreatsAsUpToDateAndFlagsOvershoot` |
| 4 | ✅ | An ordinary, correctly-migrated database never flags an overshoot | Unit test | `InitialiseAsync_NoOvershoot_FlagStaysFalse` |
| 5 | ✅ | Overshoot detection seeds an ActionRequired/DatabaseReset notification via real Program.cs wiring | Unit test | `Startup_SchemaVersionOvershootDetected_SeedsActionRequiredNotification` |
| 6 | ✅ | Every test hardcoding the old migration counts (8/6) is updated to the new counts (3/5) | Unit test | Targeted grep confirms no remaining hits; full suite green |
| 7 | ✅ | The backup-filename timestamp collision (found live) is fixed | Unit test | `InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup` passes after the `fff` precision fix |
| 8 | ✅ | No regression | Unit test | `dotnet build`/`dotnet test` — full solution, 3302 tests, 0 warnings, 0 errors |
| 9 | ✅ | T1 — local dev database (already at data v8/app v6) resets cleanly and boots against the squashed migration list | Live (T1) | Developer confirmed via Visual Studio, 2026-08-10 — startup log: `schema is up to date (data v8, app v6)` + `schema version overshoot detected: recorded data v8 (known: v3), recorded app v6 (known: v5)`; the Blazor "Quotinator is ready" screen showed the Action Required notification live (screenshot: correct message, "Active" status, correct expiry); `POST /admin/database/reset` rebuilt cleanly to baseline (data v3, app v5, 0 rows); a subsequent restart reseeded all 4 bundled files back to 799 quotes and settled at schema v5 (data v3) with no further overshoot |
| 10 | ✅ | T2 — Docker smoke test against a fresh database and against the overshoot scenario | Live (T2) | Fresh install: `fresh database detected — creating schema directly at baseline (data v3, app v5)`. Overshoot: real v1.8.2 database migrated to the pre-squash state (data v8, app v6) via #288's own leftover volume — startup log shows `schema is up to date (data v8, app v6)` + `schema version overshoot detected: recorded data v8 (known: v3), recorded app v6 (known: v5)`, no exception, 799 quotes preserved; `GET /api/v1/notifications` shows the ActionRequired notification live; `POST /admin/database/reset` (with a real admin key) trues up `schemaVersion` to 5 and clears notifications |

---

## Notes

This issue directly overrides #288's own "rejected" conclusion — both are left as-is in the historical
record (#288's plan doc and closing comment are accurate for what was true at the time), and this plan
doc/issue documents the developer's subsequent decision to proceed anyway with the reset-plus-safety-net
mitigation. ADR 015 carries the authoritative "why was this safe this time" reasoning (its "Confirmed a
third time" addendum), not repeated in full here.

Two genuine bugs were found and fixed while implementing this issue, both live, both via direct
evidence rather than assumption: the `Math.Max` mixed-overshoot bookkeeping bug (step 3) and the backup
filename timestamp collision (step 5). Neither would have been caught by a review of the squash alone —
both surfaced only by actually running the full test suite and investigating every failure rather than
assuming version-number updates would be the only fix needed.
