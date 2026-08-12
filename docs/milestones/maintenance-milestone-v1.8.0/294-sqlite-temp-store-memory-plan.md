# #294 — SQLite migration statement-journal temp file fails to open in HA add-on runtime

**Status:** Released
**GitHub issue:** #294
**Tiers required:** T1, T2, T3
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

**Confirmed live in production, 2026-08-12.** The developer's own real HA supervisor upgrade to
`v1.8.3-beta2` completed cleanly — the startup log shows `migration applied: Data v2 → v3, App v4 → v5`
(the exact migration that failed in the original incident) applying without error, full `799 quotes`
stats, `Data: /data` (the real persistent volume). The hypothesis held.

Before that live confirmation, the exact production mechanism (`/tmp/** rw` — write succeeds, lock
fails) was never directly reproducible: Docker Desktop's WSL2 backend has no AppArmor kernel support at
all (confirmed live: `/sys/module/apparmor/parameters/enabled` reads `N`, no
`/sys/kernel/security/apparmor` securityfs), and file-locking permission is an LSM concept with no
Docker-mount equivalent. **A faithful reproduction of the general failure class was achieved locally on
2026-08-11** instead (see `docs/smoke-tests.md` Section 37): a real, unmodified v1.8.2 database, migrated
by pre-#294 code under a `--read-only` container (stricter than the real profile — full write denial, not
just lock denial), reproduced a genuine `SqliteException` (`SQLite Error 10: 'disk I/O error'`, not the
original's `Error 14`, but the same failure class) and the identical degraded-startup symptom
(`schemaVersion: 0`, `quotes: 0`, `503 unhealthy`) the live incident showed; the fixed code survived the
same test cleanly. That was strong evidence ahead of the real-world retry above, which is what actually
settled it.

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

**`useMemoryTempStore` is a `SqliteConnectionFactory` constructor parameter, not an unconditional
default (revised 2026-08-11, after two rounds of self-review).** `Quotinator.Data` is explicitly
domain-agnostic, reusable infrastructure ([ADR 004](../../architecture-decisions/004-quotinator-data-project-boundaries.md))
— hardcoding `PRAGMA temp_store=MEMORY` unconditionally there would bake in a Quotinator-specific
judgment call (RAM cost is negligible *for this project's* small dataset) into code a future, larger
consumer of `Quotinator.Data` couldn't opt out of. The parameter defaults to `false` (the library stays
unopinionated); `Quotinator.Api`'s own `Program.cs` passes `useMemoryTempStore: true` explicitly at its
DI registration site, with a comment there explaining why it's safe for *this* app's dataset. This also
made the fix directly and fully unit-testable both ways — `true` sets `MEMORY`, `false`/omitted leaves
SQLite's own default in place — which a hardcoded, unconditional pragma could only ever prove one side
of.

**`SQLITE_TMPDIR` defense-in-depth was tried, then removed (2026-08-11).** The original plan set
`Environment.SetEnvironmentVariable("SQLITE_TMPDIR", dataDir)` in `Program.cs` as a hedge against a
future `SqliteConnection` created outside `SqliteConnectionFactory` and missing the pragma. Measured
against this project's own bar — a change earns its place only if its effect can be proven via test, not
because the reasoning sounds plausible — it failed: proving SQLite's native `getenv()` call actually
picks up a process-wide environment variable would mean either mutating shared test-host process state
(a real risk of bleeding into other tests running in parallel — `docs/testing-policy.md`'s own "parallel
execution" convention) or spawning a child process per test, heavier machinery nothing else in this
suite uses, for a line with no independent decision logic to test in the first place (it's just
`dataDir`, passed straight through). Removed rather than kept unproven.

## Steps

### 1. Add `useMemoryTempStore` to `SqliteConnectionFactory`, applied on every connection when true

**Status:** Done. Defaults to `false`; `Quotinator.Api`'s `Program.cs` passes `true` at its DI
registration site.

### 2. Add tests proving both the `true` and `false`/default paths genuinely work

**Status:** Done. `SqliteConnectionFactoryTests.CreateConnection_OnOpen_UseMemoryTempStoreTrue_SetsTempStoreToMemory`
opens two separate connections from a factory constructed with `true` and confirms `PRAGMA temp_store;`
reports `2` (MEMORY) on both — proving this isn't a one-time artifact of the first `Open()`.
`CreateConnection_OnOpen_UseMemoryTempStoreFalseOrDefault_LeavesTempStoreAtSqliteDefault` proves the
opposite: both the default constructor call and an explicit `false` leave `temp_store` at `0` (SQLite's
own default) — the genuine, testable opt-out this project's own "prove it or it doesn't stay" bar
required. A third test (`CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction`) is a regression
guard confirming the pre-existing `UNICODE_CONTAINS` registration still works alongside the new pragma
in the same handler, independent of the flag.

### 3. Set `SQLITE_TMPDIR` to `dataDir` as defense-in-depth

**Status:** Reverted. Tried, then removed — see "Approach" above for why. No longer present in the
codebase; kept here only so the step numbering below stays stable.

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
| 1 | ✅ | `temp_store` is set to `MEMORY` on every connection when `useMemoryTempStore: true` | Unit test | `CreateConnection_OnOpen_UseMemoryTempStoreTrue_SetsTempStoreToMemory` |
| 2 | ✅ | `temp_store` stays at the SQLite default when `useMemoryTempStore` is `false` or omitted | Unit test | `CreateConnection_OnOpen_UseMemoryTempStoreFalseOrDefault_LeavesTempStoreAtSqliteDefault` |
| 3 | ✅ | The existing `UNICODE_CONTAINS` per-connection registration still works, independent of the flag | Unit test | `CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction` |
| 4 | ✅ | No other write target in the codebase is missing a required AppArmor permission | Live (review) | Full grep audit of every `File.Write`/`Directory.CreateDirectory`/temp-path call, cross-referenced against `apparmor.txt` — all resolve under `/data` or `/tmp` with sufficient permission for their actual needs |
| 5 | ✅ | No regression | Unit test | Full solution, live-run 2026-08-11: 1077 (Data.Tests) + 1462 (Core.Tests) + 670 (Api.Tests) tests, 0 failures, 0 warnings |
| 6 | ✅ | Real v1.8.2 → current migration replay survives a restricted-write (`--read-only`) environment; pre-#294 code fails the same test with a genuine `SqliteException` | Live (Docker) | `docs/smoke-tests.md` Section 37, live-run 2026-08-11 — GREEN: `healthy`, `quotes: 799`, `migration applied: Data v2 → v3, App v4 → v5`, no exception. RED (pre-#294 code, same test): `SQLite Error 10: 'disk I/O error'`, degraded `schemaVersion: 0`/`quotes: 0`/`503 unhealthy` |
| 7 | ✅ | T1 — app starts cleanly with the fix in place | Live (T1) | Clean VS boot, 2026-08-11 23:28 — `schema is up to date`, live source refresh succeeded for both GitHub sources, `799 quotes` full stats, no errors; `/quotes/random` served repeatedly without issue |
| 8 | ✅ | T2 — Docker smoke test | Live (Docker) | Re-run 2026-08-11 against the `useMemoryTempStore` redesign: basic boot/health/version sanity, Section 37's full real-migration-replay GREEN check (`healthy`, `quotes: 799`, `migration applied: Data v2 → v3, App v4 → v5`, no exception), and a basic import — all clean |
| 9 | ✅ | The actual live HA upgrade succeeds with this fix in place | Live (T3) | Real HA supervisor upgrade to v1.8.3-beta2, 2026-08-12 06:17 — startup log shows `migration applied: Data v2 → v3, App v4 → v5` (the exact migration that failed in the original incident) completing cleanly, `799 quotes` full stats, `Data: /data` (the real persistent volume), no errors |

---

## Notes

Item 9 was the real test of whether this hypothesis was correct — it passed. The developer's own real
HA supervisor upgrade to v1.8.3-beta2 completed the previously-failing migration cleanly (see Background
above for the full log detail). No further investigation needed.

**Why `SQLITE_TMPDIR` was removed rather than kept as an untested extra:** see "Approach" above for the
full reasoning — this project's own bar is that a change earns its place by having its effect provably
tested, not by sounding like reasonable defense-in-depth. `SQLITE_TMPDIR` couldn't clear that bar without
either mutating shared test-host process state or adding child-process test machinery this suite doesn't
otherwise use, for a line with no independent decision logic to test in the first place.

**Why item 6's reproduction used `--read-only` rather than a real AppArmor profile:** see
`docs/smoke-tests.md` Section 37's own opening paragraph for the full reasoning — Docker Desktop's WSL2
backend cannot load AppArmor profiles at all, and even a Linux host that could would need file-locking
denial specifically (an LSM concept), which no Docker mount option can express. `--read-only` denies
write entirely rather than only locking, which is stricter than the real profile — sufficient to prove
the fix handles "temp storage genuinely unavailable" as a class, not proof of the exact production
syscall that failed.
