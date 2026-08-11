# #294 — SQLite migration statement-journal temp file fails to open in HA add-on runtime

**Status:** In progress
**GitHub issue:** #294
**Tiers required:** T1, T2
**Depends on:** none (independent of #293 — different root cause, same live incident)

---

## Background

A real HA v1.8.2 → v1.8.3-beta upgrade failed twice in a row (identical error, two separate full
restarts) with `SQLite Error 14: 'unable to open database file'` inside the squashed Data migration
(#289). An identical reproduction (real v1.8.2-seeded database, same code) succeeded cleanly in local
Docker — the failure is specific to the HA add-on container runtime.

**Root cause (see `SqliteConnectionFactory.cs`'s own comment for the full writeup):** SQLite creates a
statement journal — a temp file, separate from the main WAL/rollback journal — for any statement that
could partially fail without aborting the whole transaction, even under WAL mode. Neither `TMPDIR` nor
`SQLITE_TMPDIR` is set anywhere in this project's Dockerfile, so SQLite's own fallback chain for where
to put that file is entirely environment-dependent, ending at the current working directory if nothing
else is available. The add-on's own `apparmor.txt` confirms two real gaps: `/app/** rixmr` grants no
write at all (the container's `WORKDIR`), and `/tmp/** rw` grants write but not lock (`k`, unlike
`/data/** rwk`). Independently corroborated via web search as a documented class of SQLite failure in
sandboxed/containerized environments generally, with `PRAGMA temp_store = MEMORY` as the standard fix.

**Not confirmed with 100% certainty against the exact production mechanism** — Docker Desktop's WSL2
backend has no AppArmor kernel support at all (confirmed live: `/sys/module/apparmor/parameters/enabled`
reads `N`, no `/sys/kernel/security/apparmor` securityfs), so the specific "write succeeds, lock fails"
gap `/tmp/** rw` describes cannot be reproduced locally — file-locking permission is an LSM concept with
no Docker-mount equivalent. **A faithful reproduction of the general failure class was achieved locally
on 2026-08-11** (see `docs/smoke-tests.md` Section 37): a real, unmodified v1.8.2 database, migrated by
pre-#294 code under a `--read-only` container (stricter than the real profile — full write denial, not
just lock denial), reproduces a genuine `SqliteException` (`SQLite Error 10: 'disk I/O error'`, not the
original's `Error 14`, but the same failure class) and the identical degraded-startup symptom
(`schemaVersion: 0`, `quotes: 0`, `503 unhealthy`) the live incident showed; the fixed code survives the
same test cleanly. This is strong evidence the fix addresses a genuine class of failure, but not proof
it's *the exact* mechanism HA hit. The developer's own retry of the live upgrade remains the actual
confirmation step for that.

## Approach

`PRAGMA temp_store = MEMORY` applied in `SqliteConnectionFactory.CreateConnection`'s existing
`StateChange` handler (the same mechanism already used for per-connection `UNICODE_CONTAINS`
registration), not in `DatabaseInitializer.EnableWal` — `temp_store` is a per-connection setting (unlike
`journal_mode=WAL`, which persists in the database file itself), so it must be re-applied on every
`Open()`, regardless of which repository or caller opens the connection, not just the one connection
`InitialiseAsync` happens to use for migrations.

**Broader audit (developer request):** every other file-write target in the codebase was checked against
the add-on's own AppArmor profile — backups (`/data/backups`), DataProtection keys (`/data/keys`),
manifest/source-cache/rule-override writes (all under the configured `dataDir` = `/data`), and the
existing import-file-upload staging path (`Path.GetTempPath()`, under `/tmp` — write-only needs, no
locking, so covered by the existing `/tmp/** rw` grant). No other gap found.

**`SQLITE_TMPDIR` defense-in-depth (added after this fix was already shipping in v1.8.3-beta):**
`Environment.SetEnvironmentVariable("SQLITE_TMPDIR", dataDir)` in `Program.cs`, set immediately after
`dataDir` is resolved and before any `SqliteConnection` is ever opened. `temp_store = MEMORY` above is
still the fix that actually ships — it sidesteps the whole directory-permission question for this
project's small dataset. This addition covers the case where a future `SqliteConnection` is created
outside `SqliteConnectionFactory` and misses that pragma; `dataDir` was chosen as the target because it
already carries full `rwk` permission in every deployment shape (`apparmor.txt`'s `/data/** rwk`),
unlike `/app` (no write) or `/tmp` (write, no lock). Ordering is safe without extra plumbing: SQLite's
own unix VFS re-reads `SQLITE_TMPDIR` via `getenv()` at temp-file-creation time rather than caching it
once at process start, so setting it once, early, before the first connection opens is sufficient — no
per-connection re-application needed (unlike `temp_store`, a session-level pragma).

## Steps

### 1. Apply `temp_store = MEMORY` on every connection

**Status:** Done.

### 2. Add a test proving it's re-applied per-connection, not just once

**Status:** Done. `SqliteConnectionFactoryTests.CreateConnection_OnOpen_SetsTempStoreToMemory` opens
two separate connections from the same factory and confirms `PRAGMA temp_store;` reports `2` (MEMORY)
on both — proving this isn't a one-time artifact of the first `Open()`. A second test
(`CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction`) is a regression guard confirming the
pre-existing `UNICODE_CONTAINS` registration still works alongside the new pragma in the same handler.

### 3. Set `SQLITE_TMPDIR` to `dataDir` as defense-in-depth

**Status:** Done (code change only — no dedicated automated test; see Notes).

### 4. Add a repeatable restricted-write migration-replay smoke test

**Status:** Done. `docs/smoke-tests.md` Section 37 — a real v1.8.2 image seeds a database under normal
conditions, then a `--read-only` container (current code) replays the migration against it. Live-run
once during this issue to confirm both directions (Step 1's Background section has the full result);
the documented procedure also includes the one-time revert-and-rerun steps to prove the test itself
would have caught the original bug, without requiring that revert on every future run.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `temp_store` is set to `MEMORY` on every connection the factory opens, not just the first | Unit test | `CreateConnection_OnOpen_SetsTempStoreToMemory` |
| 2 | ✅ | The existing `UNICODE_CONTAINS` per-connection registration still works | Unit test | `CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction` |
| 3 | ✅ | No other write target in the codebase is missing a required AppArmor permission | Live (review) | Full grep audit of every `File.Write`/`Directory.CreateDirectory`/temp-path call, cross-referenced against `apparmor.txt` — all resolve under `/data` or `/tmp` with sufficient permission for their actual needs |
| 4 | ✅ | No regression | Unit test | Full solution: 1078 (Data.Tests, +2) + 1462 (Core.Tests) + 671 (Api.Tests) tests, 0 failures, 0 warnings |
| 5 | ✅ | `SQLITE_TMPDIR` is set to `dataDir` before any `SqliteConnection` opens | Build (review) | `Program.cs` — set immediately after `dataDir` resolution; no dedicated automated test (see Notes) |
| 6 | ✅ | Real v1.8.2 → current migration replay survives a restricted-write (`--read-only`) environment; pre-#294 code fails the same test with a genuine `SqliteException` | Live (Docker) | `docs/smoke-tests.md` Section 37, live-run 2026-08-11 — GREEN: `healthy`, `quotes: 799`, `migration applied: Data v2 → v3, App v4 → v5`, no exception. RED (pre-#294 code, same test): `SQLite Error 10: 'disk I/O error'`, degraded `schemaVersion: 0`/`quotes: 0`/`503 unhealthy` |
| 7 | ⬜ | T1 — app starts cleanly with the fix in place | Live (T1) | Pending — not yet re-run since the `SQLITE_TMPDIR` addition |
| 8 | ⬜ | T2 — Docker smoke test | Live (T2) | Pending — not yet re-run since the `SQLITE_TMPDIR` addition |
| 9 | ⬜ | The actual live HA upgrade succeeds with this fix in place | Live (developer) | Pending — the real confirmation against the exact production mechanism |

---

## Notes

Item 9 is the real test of whether this hypothesis was correct. If the live upgrade still fails with the
same error after this fix ships, the hypothesis was wrong (or incomplete) and this issue reopens for
further investigation — e.g. actually reaching the HA host's kernel/AppArmor audit log, which this
session was unable to obtain.

**Why `SQLITE_TMPDIR` has no dedicated automated test:** it's a single `Environment.SetEnvironmentVariable`
call setting process-wide state in `Program.cs`. A `WebApplicationFactory`-based test asserting the env
var's value would set that same process-wide variable inside the shared test-host process — a real risk
of bleeding into other tests running in parallel in the same process (`docs/testing-policy.md`'s own
"parallel execution" convention), for a line with no independent logic to verify beyond "did this literal
call happen." Verified by build/review here instead, plus T1/T2 (rows 7–8) once re-run.

**Why item 6's reproduction used `--read-only` rather than a real AppArmor profile:** see
`docs/smoke-tests.md` Section 37's own opening paragraph for the full reasoning — Docker Desktop's WSL2
backend cannot load AppArmor profiles at all, and even a Linux host that could would need file-locking
denial specifically (an LSM concept), which no Docker mount option can express. `--read-only` denies
write entirely rather than only locking, which is stricter than the real profile — sufficient to prove
the fix handles "temp storage genuinely unavailable" as a class, not proof of the exact production
syscall that failed.
