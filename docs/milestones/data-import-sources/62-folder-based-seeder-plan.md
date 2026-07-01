# #62 — Folder-based seeder

**Status:** All spec requirements resolved in code, T1+T2 verified — pending release (issue cannot close until then)
**GitHub issue:** #62
**Tiers required:** T1, T2 — this issue touches `Program.cs` startup config reading, same reasoning as #63.
**Depends on:** #63 (manifest) — done; #58 (ImportBatch rows) — done

---

## Spec requirements

1. Seeder scans `{bundledSourcesDir}` (image-bundled, read-only) and `{DataDir}/imports/` (user-supplied, writable) at startup
2. `Quotinator__DataDir` env var replaces `Quotinator__DataPath`
3. `Quotinator__IncludeDefaultSources` config key — controls whether bundled sources are loaded (default: `true`)
4. `Quotinator__ImportsPath` config key — overrides the imports folder path (default: `{DataDir}/imports`)
5. Startup warning logged when the legacy `Quotinator__DataPath` env var is still set
6. Startup log shows each source file being processed with its quote count
7. One `ImportBatch` row per source file written to the database, with a **type accurately reflecting where the file came from** (see Scope changes below)

---

## Step status

- [x] `Quotinator__DataDir` replaces `Quotinator__DataPath` across config, `Program.cs`, and `addon/config.yaml`
- [x] `HaFallbackDir()` updated to use `DataDir` semantics
- [x] Bundled sources folder scanned at startup
- [x] `{DataDir}/imports/` scanned at startup when it exists
- [x] `addon/config.yaml` updated
- [x] `docs/docker.md` updated
- [x] `CLAUDE.md` updated
- [x] CI's publish-output check verifies `data/sources/` presence (already correct — not previously tracked here)
- [x] One `ImportBatch` row per source file, typed accurately by both origin and URL presence (`System`/`Seed`/`UserSeed`/`Import`) — fixed 2026-07-01
- [x] `Quotinator__IncludeDefaultSources` config key — fixed 2026-07-01
- [x] `Quotinator__ImportsPath` config key — fixed 2026-07-01
- [x] Startup warning when `Quotinator__DataPath` is still set in the environment — fixed 2026-07-01

---

## Scope changes

The original issue spec said batches should be typed "`seed` for bundled sources, `import` for user files." That's superseded — #58 (implemented after this issue was written) defined `ImportBatchType` around URL presence instead (`Seed` = has a URL, `System` = bundled with no URL, `Import` reserved for the future bulk-import *endpoint*, #45). The actual code inherited that definition and typed purely by URL presence regardless of folder — meaning a user-imports-folder file with no URL was mislabeled `System` ("startup seeding from bundled sources"), which is wrong.

Resolved 2026-07-01: `ImportBatchType` now has four values —
- **System** — fixed/predetermined bundled data, no URL (`quotinator-curated.json`)
- **Seed** — our own bundled external datasets, has a URL/`github` object (vilaboim, NikhilNamal17)
- **UserSeed** — any file scanned from `{DataDir}/imports/` at startup, regardless of URL
- **Import** — reserved for the future bulk import endpoint (#45), untouched by this fix

A new `SeedBatchOrigin` enum (`Bundled`/`UserImports`) was added to `Quotinator.Data.Import.SeedBatch` so the seeder knows which folder a batch came from, independent of the loose `Label` string used only for logging. Origin always wins over URL presence — a user-imports-folder file that happens to declare its own `url`/`github` manifest entry still gets `UserSeed`, not `Seed`.

This required a new schema migration (**migration 5**) — the `ImportBatches.Type` column has a `CHECK (Type IN ('Seed','Import','System'))` constraint from the already-applied migration 3, which per `CLAUDE.md`'s policy can never be edited. Migration 5 recreates the table with a widened constraint (`+ 'UserSeed'`), preserving existing rows.

**Bug found and fixed during this work:** the recreate-table migration initially failed with `FOREIGN KEY constraint failed` when re-run against a non-empty database (`Quotes.ImportBatchId` etc. reference `ImportBatches(Id)`). `Microsoft.Data.Sqlite` defaults `PRAGMA foreign_keys` to **ON** per connection (unlike the raw SQLite C library, which defaults to OFF) — contrary to what earlier session notes assumed. `PRAGMA foreign_keys` is also a no-op inside a transaction, so it can't be toggled from within a migration's own SQL text. Fixed generically in `Quotinator.Data.Database.DatabaseInitializer.ApplyMigrationsAsync` — foreign key enforcement is now suspended for the duration of applying pending migrations (not just this one), restored afterward. This benefits any future migration that needs to recreate a table, not just this one.

Scope-change comment posted on #62 documenting this, per `process.md`'s deferral rule (the original issue text no longer matches what shipped).

**T1 verified 2026-07-01** — VS run against an existing, non-empty dev database (schema v4 → v5 migration confirmed live, not just in a unit test): startup log showed `schema updated at version 5` with a pre-migration backup taken automatically, no errors. `ImportBatches` table inspected directly (SQL Server Object Explorer): `quotinator-curated.json → System`, `vilaboim`/`NikhilNamal17 → Seed` unchanged. Triggered `POST /api/v1/admin/database/reseed` with two empty files (`dummy1.json`, `Dummy2.JSON`) present in the imports folder — both correctly classified `UserSeed` (previously would have been `System`). The empty-file warning/skip behavior from the #63 session's fix was also re-confirmed working (no crash).

**T2 verified 2026-07-01** — `docker build` succeeded; fresh container built schema straight to v5 with no errors; `/api/v1/health`, `/api/v1/version` (`schemaVersion: 5`), `/api/v1/quotes/random` all correct. Confirmed `UserSeed` classification inside the container too: mounted a host volume (`-v` with `MSYS_NO_PATHCONV=1` and a Windows-style path — Git Bash's `/tmp` doesn't map to a real path Docker Desktop on Windows can bind-mount, which is what caused an initial false negative) with a file already present in `imports/` before container start (seed batches are scanned once at startup — a file added via `docker exec` after the container is running has no effect on reseed, since `IDatabaseInitializer` is a singleton and its batch list is captured once). After reseed, queried the container's `quotinatordata.db` file directly from the host (via a throwaway, now-reusable `Quotinator.Tools.DbInspector` tool, read-only-mode SQLite connection) and confirmed `container-dummy.json → UserSeed`.

## Three config keys — resolved 2026-07-01

`BuildSeedBatches` (a `Program.cs` local static function, previously untestable — the same shape and reason `ManifestSeedPlanner` was extracted for #63) was extracted into `Quotinator.Data.Import.SeedBatchesBuilder.Build(...)`, adding an `includeDefaultSources` gate directly. A small new `Quotinator.Data.Import.LegacyConfigWarnings.WarnIfDataPathStillSet(...)` class covers the deprecation warning. `ImportsPath`'s override is a plain one-line `??`-style resolution in `Program.cs`, mirroring the already-established, untested-individually `Quotinator:BackupPath` pattern in the same file — no `appsettings.json` entries were needed for any of the three, matching how `Quotinator:DataDir`, `Quotinator:BackupPath`, and `Quotinator:CreateMissingManifest` are already handled (code-defaulted, not declared in `appsettings.json`).

**Live testing found a real pitfall worth recording:** early live-verification attempts appeared to show `Quotinator__IncludeDefaultSources=false` being silently ignored (bundled sources kept seeding). This was **not a code bug** — it was caused by stale background `dotnet` processes from improperly-terminated prior test runs still holding the old database file open and/or still bound to the test port, serving old behavior while a new, differently-configured process failed to start cleanly. A debug print of the resolved config value confirmed the config read itself was always correct (`includeDefaultSources=False` when the env var was set). Once ports/processes were verified clean before each run (`netstat`/`taskkill` by exact PID, never a broad `taskkill /IM dotnet.exe`), all three config keys behaved correctly on the first clean attempt, in both VS and Docker.

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `Quotinator__DataDir` replaces `Quotinator__DataPath` | Live | Confirmed in `Program.cs`; `HaFallbackDir()` uses `DataDir` semantics |
| 2 | ✅ | Seeder scans bundled sources dir at startup | Live | Confirmed via `SeedBatchesBuilder.Build` in `Program.cs` |
| 3 | ✅ | Seeder scans `{DataDir}/imports/` when it exists; silently skips when missing | Live | Confirmed via `Directory.Exists(importsDir)` guard |
| 4 | ✅ | `HaFallbackDir()` updated to use `DataDir` semantics | Live | Confirmed in `Program.cs` |
| 5 | ✅ | `Quotinator__IncludeDefaultSources` config key (default `true`); when `false`, bundled sources skipped | Unit test + Live | `SeedBatchesBuilderTests.Build_IncludeDefaultSourcesTrue_BundledBatchIncluded`, `Build_IncludeDefaultSourcesFalse_BundledBatchExcluded`, `Build_IncludeDefaultSourcesFalse_ImportsBatchStillIncluded`; confirmed live in VS + Docker 2026-07-01 |
| 6 | ✅ | `Quotinator__ImportsPath` config key (default `{DataDir}/imports`) | Live | Confirmed live in VS (custom Windows temp path) + Docker (volume-mounted path) 2026-07-01 — one-line resolution, no dedicated unit test needed (matches the untested `BackupPath` precedent) |
| 7 | ✅ | Startup warning logged when `Quotinator__DataPath` still set in environment | Unit test + Live | `LegacyConfigWarningsTests.WarnIfDataPathStillSet_ValueSet_LogsWarning`, `_ValueNull_DoesNotLog`, `_ValueEmptyString_DoesNotLog`; confirmed live in VS + Docker 2026-07-01 |
| 8 | ✅ | One `ImportBatch` row per source file, typed accurately (`System`/`Seed`/`UserSeed`/`Import`) | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes`, `Seeding_UserImportsOriginNoUrl_TypeIsUserSeed`, `Seeding_UserImportsOriginWithUrl_TypeIsStillUserSeed` |
| 9 | ✅ | Migration 5 widens the `Type` CHECK constraint without losing existing rows | Unit test | `ImportBatchesTests.Migration005_WideningTypeCheckConstraint_PreservesExistingRows` |
| 10 | ✅ | Schema migration version bumped to 5 | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |
| T1 | ✅ | All of the above confirmed live in Visual Studio | Live (VS) | Confirmed 2026-07-01 — default behavior unchanged, `IncludeDefaultSources=false` skips bundled sources, custom `ImportsPath` scanned correctly, `DataPath` deprecation warning logged |
| T2 | ✅ | All of the above confirmed live in Docker | Live (Docker) | Confirmed 2026-07-01 — `docker build`/`docker run` for each scenario, identical behavior to T1 |

---

## Small drift cleanup found this session (still open, unrelated to this fix)

`addon/config.yaml:37`'s comment still reads `"Quotinator__DataPath points to the supervisor-mounted persistent volume"` — the actual env var below it is `Quotinator__DataDir`. Update the comment when next touching this file — not part of this pass, noted here so it isn't lost.
