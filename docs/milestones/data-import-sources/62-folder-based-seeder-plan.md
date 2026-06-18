# #62 — Folder-based seeder

**Status:** Partially done  
**GitHub issue:** #62  
**Depends on:** #63 (manifest), #58 (ImportBatch rows — deferred)

---

## Spec requirements

1. Seeder scans `{bundledSourcesDir}` (image-bundled, read-only) and `{DataDir}/imports/` (user-supplied, writable) at startup
2. `Quotinator__DataDir` env var replaces `Quotinator__DataPath`
3. `Quotinator__IncludeDefaultSources` config key — controls whether bundled sources are loaded (default: `true`)
4. `Quotinator__ImportsPath` config key — overrides the imports folder path (default: `{DataDir}/imports`)
5. Startup warning logged when the legacy `Quotinator__DataPath` env var is still set
6. Startup log shows each source file being processed with its quote count
7. One `ImportBatch` row per source file written to the database (deferred pending #58)

---

## Step status

- [x] `Quotinator__DataDir` replaces `Quotinator__DataPath` across config, `Program.cs`, and `addon/config.yaml`
- [x] `HaFallbackDir()` updated to use `DataDir` semantics
- [x] Bundled sources folder scanned at startup
- [x] `{DataDir}/imports/` scanned at startup when it exists
- [x] `addon/config.yaml` updated
- [x] `docs/docker.md` updated
- [x] `CLAUDE.md` updated
- [ ] `Quotinator__IncludeDefaultSources` config key — bundled sources are always included currently
- [ ] `Quotinator__ImportsPath` config key — imports path is hardcoded to `{DataDir}/imports`
- [ ] Startup warning when `Quotinator__DataPath` is still set in the environment
- [ ] ImportBatch row per source file — deferred until #58

---

## Remaining work

### `Quotinator__IncludeDefaultSources`

Add to `appsettings.json` (default `true`). When `false`, skip the bundled sources folder entirely (useful for a fully custom data setup).

### `Quotinator__ImportsPath`

Add to `appsettings.json` (default: `Path.Combine(dataDir, "imports")`). When set, use that path instead.

### Legacy env var warning

At startup, check `Environment.GetEnvironmentVariable("Quotinator__DataPath")`. If set and non-empty, log a warning: "Quotinator__DataPath is deprecated; use Quotinator__DataDir instead."

### ImportBatch rows

Deferred to #58. When #58 is implemented, `DatabaseInitializer` should create one `ImportBatch` row per source file before seeding its quotes and reference the batch ID on all `INSERT` statements for that file's records.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `Quotinator__DataDir` replaces `Quotinator__DataPath` | Unit test | No test verifies config key behaviour |
| 2 | ❌ | Seeder scans bundled sources dir at startup | Unit test | No test verifies seeder behaviour |
| 3 | ❌ | Seeder scans `{DataDir}/imports/` when it exists; silently skips when missing | Unit test | No test exists |
| 4 | ❌ | `HaFallbackDir()` updated to use `DataDir` semantics | Unit test | No test verifies fallback logic |
| 5 | ❌ | `Quotinator__IncludeDefaultSources` config key (default `true`); when `false`, bundled sources skipped | Unit test | Not implemented |
| 6 | ❌ | `Quotinator__ImportsPath` config key (default `{DataDir}/imports`) | Unit test | Not implemented |
| 7 | ❌ | Startup warning logged when `Quotinator__DataPath` still set in environment | Unit test | Not implemented |
| 8 | ❌ | One `ImportBatch` row per source file (deferred to #58) | Unit test | Requires #58 |
