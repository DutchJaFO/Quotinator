# #62 — Folder-based seeder

**Status:** Partially done — `ImportBatchType` accuracy fixed 2026-07-01, three config keys still not started
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
- [ ] `Quotinator__IncludeDefaultSources` config key — bundled sources are always included currently
- [ ] `Quotinator__ImportsPath` config key — imports path is hardcoded to `{DataDir}/imports`
- [ ] Startup warning when `Quotinator__DataPath` is still set in the environment

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

A GitHub issue comment should be posted on #62 documenting this scope change, per `process.md`'s deferral rule (the original issue text no longer matches what shipped).

**Not superseded, still open:** the three still-missing config keys below remain #62's own unstarted work — this fix only resolved the `ImportBatchType` accuracy problem.

---

## Remaining work

### `Quotinator__IncludeDefaultSources`

Add to `appsettings.json` (default `true`). When `false`, skip the bundled sources folder entirely (useful for a fully custom data setup).

### `Quotinator__ImportsPath`

Add to `appsettings.json` (default: `Path.Combine(dataDir, "imports")`). When set, use that path instead.

### Legacy env var warning

At startup, check `Environment.GetEnvironmentVariable("Quotinator__DataPath")`. If set and non-empty, log a warning: "Quotinator__DataPath is deprecated; use Quotinator__DataDir instead."

### Small drift cleanup found this session

`addon/config.yaml:37`'s comment still reads `"Quotinator__DataPath points to the supervisor-mounted persistent volume"` — the actual env var below it is `Quotinator__DataDir`. Update the comment when next touching this file (not fixed as part of this pass — noted here so it isn't lost).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `Quotinator__DataDir` replaces `Quotinator__DataPath` | Live | Confirmed in `Program.cs:161`; `HaFallbackDir()` uses `DataDir` semantics |
| 2 | ✅ | Seeder scans bundled sources dir at startup | Live | Confirmed via `BuildSeedBatches` in `Program.cs` |
| 3 | ✅ | Seeder scans `{DataDir}/imports/` when it exists; silently skips when missing | Live | Confirmed via `Directory.Exists(importsDir)` guard |
| 4 | ✅ | `HaFallbackDir()` updated to use `DataDir` semantics | Live | Confirmed in `Program.cs` |
| 5 | ❌ | `Quotinator__IncludeDefaultSources` config key (default `true`); when `false`, bundled sources skipped | Unit test | Not implemented |
| 6 | ❌ | `Quotinator__ImportsPath` config key (default `{DataDir}/imports`) | Unit test | Not implemented |
| 7 | ❌ | Startup warning logged when `Quotinator__DataPath` still set in environment | Unit test | Not implemented |
| 8 | ✅ | One `ImportBatch` row per source file, typed accurately (`System`/`Seed`/`UserSeed`/`Import`) | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes`, `Seeding_UserImportsOriginNoUrl_TypeIsUserSeed`, `Seeding_UserImportsOriginWithUrl_TypeIsStillUserSeed` |
| 9 | ✅ | Migration 5 widens the `Type` CHECK constraint without losing existing rows | Unit test | `ImportBatchesTests.Migration005_WideningTypeCheckConstraint_PreservesExistingRows` |
| 10 | ✅ | Schema migration version bumped to 5 | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |
