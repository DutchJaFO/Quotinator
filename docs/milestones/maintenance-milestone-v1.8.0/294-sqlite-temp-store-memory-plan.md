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

**Not confirmed with 100% certainty** — the failure isn't reproducible outside the real HA supervisor
environment, so this remains the best-evidenced hypothesis rather than a directly-proven root cause. The
developer's own retry of the live upgrade, once this fix ships, is the actual confirmation step.

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

## Steps

### 1. Apply `temp_store = MEMORY` on every connection

**Status:** Done.

### 2. Add a test proving it's re-applied per-connection, not just once

**Status:** Done. `SqliteConnectionFactoryTests.CreateConnection_OnOpen_SetsTempStoreToMemory` opens
two separate connections from the same factory and confirms `PRAGMA temp_store;` reports `2` (MEMORY)
on both — proving this isn't a one-time artifact of the first `Open()`. A second test
(`CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction`) is a regression guard confirming the
pre-existing `UNICODE_CONTAINS` registration still works alongside the new pragma in the same handler.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `temp_store` is set to `MEMORY` on every connection the factory opens, not just the first | Unit test | `CreateConnection_OnOpen_SetsTempStoreToMemory` |
| 2 | ✅ | The existing `UNICODE_CONTAINS` per-connection registration still works | Unit test | `CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction` |
| 3 | ✅ | No other write target in the codebase is missing a required AppArmor permission | Live (review) | Full grep audit of every `File.Write`/`Directory.CreateDirectory`/temp-path call, cross-referenced against `apparmor.txt` — all resolve under `/data` or `/tmp` with sufficient permission for their actual needs |
| 4 | ✅ | No regression | Unit test | Full solution: 1078 (Data.Tests, +2) + 1462 (Core.Tests) + 671 (Api.Tests) tests, 0 failures, 0 warnings |
| 5 | ⬜ | T1 — app starts cleanly with the fix in place | Live (T1) | Pending |
| 6 | ⬜ | T2 — Docker smoke test | Live (T2) | Pending |
| 7 | ⬜ | The actual live HA upgrade succeeds with this fix in place | Live (developer) | Pending — the real confirmation of the root-cause hypothesis |

---

## Notes

Item 7 is the real test of whether this hypothesis was correct. If the live upgrade still fails with the
same error after this fix ships, the hypothesis was wrong (or incomplete) and this issue reopens for
further investigation — e.g. actually reaching the HA host's kernel/AppArmor audit log, which this
session was unable to obtain.
