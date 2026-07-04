# #140 — Auto-update bundled source files from manifest URL on startup

**Status:** In progress (step 17)
**GitHub issue:** #140
**Depends on:** #58 fix (manifest `url` field) — done; #62 (`AutoUpdateSources` follows the same config pattern) — done; #63 (`downloadUrl`/`github` manifest groundwork) — done
**Unblocks:** retirement of `scripts/sources.json` (separate follow-up, not part of this issue)

---

## Cross-check against authoritative sources (mandatory before planning, per `process.md`)

Read `schemas/manifest.schema.json`, `ManifestSeedPlanner.cs`, `SeedBatchesBuilder.cs`, and `QuotinatorDatabaseInitializer.cs` before writing this plan. Found three real conflicts between the original plan doc and the actual code/issue text:

1. **Write-path design contradicted the issue's own text.** The issue's Notes section explicitly says downloaded files should go to `{dataDir}/sources/` (persistent volume) and be "scanned from there **in addition to** the image-bundled files." The original plan instead designed an ephemeral OS-temp-directory substitution, deleted after each seed — a different, unreconciled design.
2. **The fixed-batch-list architecture makes "on reseed" impossible under the original plan.** `SeedBatchesBuilder.Build()` runs once, synchronously, inside the `IDatabaseInitializer` DI factory (`Program.cs:277`) at first resolution. The resulting `SeedFile` list is stored in `QuotinatorDatabaseInitializer`'s `private readonly _batches` field and reused unchanged by `OnInitialisedAsync`, `OnReseedAsync`, *and* `OnResetAsync` for the process's entire lifetime — there is no code path today where a second `POST /reseed` call re-resolves anything.
3. **No natural async entry point exists at the point files are currently resolved.** The DI factory building the seed list is a synchronous `Func<IServiceProvider, T>`. `OnInitialisedAsync`/`OnReseedAsync`/`OnResetAsync` are already `async Task` — the download/refresh logic must live there instead, not at DI-construction time.

Also found: `CreateImportBatchAsync` sets the persisted `ImportBatch.Name` from `Path.GetFileName(seedFile.FilePath)` — a randomized temp path would have corrupted this stored provenance field permanently. Confirmed there is no existing `HttpClient`/`IHttpClientFactory` usage anywhere in the codebase — this is the first outbound network call Quotinator makes.

---

## Scope expansion (round 1, resolved with the user 2026-07-04 — comment posted)

The original issue text does not mention a refresh-staleness window, a per-source override, or a dedicated refresh endpoint. Discussion produced these decisions, which extend the issue's scope:

1. **Write-path:** persistent `{dataDir}/sources/download/` directory (not the ephemeral temp-file design) — writable at runtime in every deployment (standalone Docker and HA add-on alike, since `{dataDir}` is always the persistent volume), scanned as an override *in addition to* the bundled `data/sources/` files.
2. **Refresh timing:** a fresh staleness check runs on every seed operation (startup, `POST /reseed`, `POST /reset` — all three), not just once at startup. `Quotinator__AutoUpdateSources=false` remains the master switch to skip all checks entirely (e.g. air-gapped homelab installs with no internet) — this is what prevents startup from stalling when there is no network.
3. **TTL / staleness:** a downloaded copy is not re-fetched on every single seed operation — it is considered fresh for `Quotinator__SourceUpdateIntervalHours` (global default **24 hours**), overridable **per manifest entry** via a new optional `refreshIntervalHours` field.
4. **Force-refresh:** two independent mechanisms — a query parameter on the existing reseed/reset endpoints (refresh-then-seed in one call) **and** a new dedicated endpoint that refreshes source files on disk only, without touching the database, so an operator can control update timing independently of when they want to reseed.

A comment recording all four points must be posted on #140 before implementation begins, and the issue body itself should be read again alongside this doc so nobody mistakes the original text for the full scope.

---

## Scope expansion (round 2, resolved with the user 2026-07-04 — must be posted as a GitHub comment before implementation, see step 2)

A second round of discussion expanded the design further, past round 1:

1. **Applies to user-imports manifest entries too, not bundled sources only.** `ManifestSeedPlanner.PlanSeed` already scans `{dataDir}/imports/manifest.json` the same way it scans the bundled manifest, so a user can equally declare a `downloadUrl`/`github` entry there. Round 1 explicitly excluded this ("Scoped to bundled sources only"); that exclusion is now reversed — the same download/cache/TTL/collision mechanism applies uniformly to any manifest entry with a `downloadUrl`, regardless of which manifest it came from. Only the *default* cache location differs by origin (next point).
2. **Two default cache locations, not one.** `{dataDir}/sources/download/` (the "internal" folder) is the default for entries in the bundled sources manifest. A new `{dataDir}/imports/download/` ("external" folder) is the default for entries in the user imports manifest — same structure, mirrored under `imports/` instead of `sources/`. Both use the same new `DataPaths.DownloadedSourcesFolder = "download"` subfolder-name constant (step 5), combined with `SourcesFolder` or `ImportsFolder` respectively.
3. **Per-entry override of which folder a specific entry uses.** A new optional manifest field, `downloadTarget`, accepts the literal string `"internal"` or `"external"` on any `files[]` entry (bundled or imports) with a `downloadUrl`/`github`. When omitted, the default is whichever folder matches the manifest the entry lives in (point 2). This lets, for example, a user-imports entry explicitly cache into the internal folder if that's ever genuinely wanted, without forcing every entry to accept the origin-based default.
4. **Collision detection is now a hard requirement, not an assumption.** Two different sources (different URLs, potentially from different manifests) can resolve to the same on-disk cache filename (e.g. both literally named `quotefile.json`) — silently overwriting one with the other's content would corrupt whichever source lost the race, with no indication anything went wrong. Every seed operation must build the full set of resolved target paths (folder + filename) across *all* candidate entries — bundled and imports together — before performing any downloads, and treat any path claimed by more than one distinct source as a collision (see step 9 for the exact handling).

A comment recording all four of these points must be posted on #140 before implementation begins (step 2) — same requirement as round 1, since this changes the design again past what round 1's own comment described.

**Confirmed with the user, 2026-07-04 (kept here to prevent re-deriving or drifting from it later):**
- **Bundled vs. imports is a provenance/location distinction only, never a content-classification one.** `ImportBatchType.System` (reserved per #62's correction) can only ever come from bundled sources — there is no current user-imports use case for it, and this download mechanism doesn't change that. The internal/external split governs *where a downloaded copy is cached*, nothing about what `ImportBatchType` a seeded row ends up with.
- **Bundled and user-imports entries are mechanically identical in every respect except two: how they're provided (which manifest they're declared in) and where they're cached (internal vs. external, subject to the `downloadTarget` override).** TTL/staleness, `AutoUpdateSources`, `forceSourceRefresh`, collision detection, and the network GET/timeout/fallback behavior all apply uniformly regardless of origin — there is no other branch anywhere in this design based on bundled-vs-imports.

---

## Spec requirements (final, supersedes the original plan's list)

1. `Quotinator__AutoUpdateSources` config key (default `true`) — master switch; `false` skips all network checks entirely, seeding proceeds from whatever's already on disk (downloaded cache if present, else the original bundled/local file)
2. `Quotinator__SourceUpdateIntervalHours` config key (default `24`) — global default TTL before a cached download is considered stale enough to re-check
3. `schemas/manifest.schema.json` gains, on any `files[]` entry with a `downloadUrl`/`github`: an optional `refreshIntervalHours` (integer, hours) overriding the global TTL for that source, and an optional `downloadTarget` (`"internal"` or `"external"`) overriding which cache folder that source uses
4. **Applies to any manifest entry with a `downloadUrl`, bundled or user-imports alike** — not bundled sources only. The default cache folder depends on which manifest the entry came from (point 5); an explicit `downloadTarget` (point 3) overrides that default regardless of origin.
5. Two persistent cache directories, both surviving restarts and writable in every deployment shape including the HA add-on:
   - `{dataDir}/sources/download/` ("internal") — default for entries in the bundled sources manifest
   - `{dataDir}/imports/download/` ("external") — default for entries in the user imports manifest
6. **Collision detection.** Before any downloads run in a seed cycle, every candidate entry's resolved target path (folder + filename) is checked for uniqueness across the *entire* set (bundled and imports together). Any path claimed by more than one distinct source is a collision: skip the download for those entries, do not trust any pre-existing file at the shared path either, fall back directly to each entry's own original bundled/local file, and log a distinct `Error` naming every colliding source and the shared path.
7. On startup, `POST /api/v1/admin/database/reseed`, and `POST /api/v1/admin/database/reset`: for each candidate manifest entry with a `downloadUrl` (after collision detection, point 6), resolve the **effective** seed file before reading it:
   - `AutoUpdateSources=false` → skip the network check entirely; use the downloaded-cache copy if one exists, else the original bundled/local path
   - Otherwise, check the cached copy's staleness (its file `LastWriteTimeUtc`) against the per-entry or global TTL. Fresh enough and not forced → use the cached copy without any network call
   - Stale, missing, or force-requested → attempt `GET` with a 5 s timeout
     - Success → overwrite the resolved cache path, use it, log at `Information`
     - Failure (unreachable, timeout, non-200) → log a `Warning`, fall back to whatever already exists (stale cached copy if present, else the original bundled/local path) — **never a startup, reseed, or reset failure**
8. `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` each gain a `forceSourceRefresh` query parameter (default `false`) — bypasses the TTL check for that call only; still respects `AutoUpdateSources=false` (an explicit "no network" declaration is not overridden by a force flag — see step 11)
9. New `POST /api/v1/admin/sources/refresh` endpoint — refreshes both internal and external caches on disk only, does **not** reseed or touch the database; accepts its own `force` query parameter; requires the admin API key; returns a per-source summary (updated / skipped-fresh / failed / skipped-collision)
10. `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes, `addon/config.yaml`, and `addon/translations/{en,nl,de}.yaml` all updated for the two new config keys and the new endpoint

---

## Steps

### 1. Post scope-expansion comment on #140 (round 1)
**Status:** ✅ Done — [comment posted](https://github.com/DutchJaFO/Quotinator/issues/140#issuecomment-4881368528) 2026-07-04, recording the write-path, refresh timing, TTL/override, and force-mechanism decisions from "Scope expansion (round 1)" above.

### 2. Post scope-expansion comment on #140 (round 2)
**Status:** ✅ Done — [comment posted](https://github.com/DutchJaFO/Quotinator/issues/140#issuecomment-4881512146) 2026-07-04, recording all four points from "Scope expansion (round 2)" above.

### 3. Add `refreshIntervalHours` and `downloadTarget` to `schemas/manifest.schema.json`
**Status:** ✅ Done — both fields added to `files.items.properties` as optional (`refreshIntervalHours`: integer, `downloadTarget`: string enum `internal`/`external`).

### 4. Add `RefreshIntervalHours`/`DownloadTarget` to `SeedFile` and read them in `ManifestSeedPlanner`
**Status:** ✅ Done — `SeedFile` gained both properties; `ManifestSeedPlanner`'s `listed` selector reads `refreshIntervalHours`/`downloadTarget` via a new `ParseDownloadTarget` helper. Covered by `ManifestSeedPlannerTests.PlanSeed_ManifestEntryHasRefreshIntervalHoursAndDownloadTarget_ParsedIntoSeedFile` and `...OmitsRefreshIntervalHoursAndDownloadTarget_BothNull`.

### 5. `DataPaths` — internal and external download folders
**Status:** ✅ Done — added `DataPaths.DownloadedSourcesFolder = "download"`, combined in `Program.cs` as `internalDownloadDir`/`externalDownloadDir`.

### 6. Wire `Quotinator__AutoUpdateSources` config key
**Status:** ✅ Done — read in `Program.cs` as `autoUpdateSources` (default `true`), passed into the `QuotinatorDatabaseInitializer` DI factory.

### 7. Wire `Quotinator__SourceUpdateIntervalHours` config key
**Status:** ✅ Done — read in `Program.cs` as `sourceUpdateIntervalHours` (default `24`), passed into `SourceCacheOptions`.

### 8. Implement `ISourceCacheUpdater`/`SourceCacheUpdater`
**Status:** ✅ Done — `Quotinator.Data.Import.ISourceCacheUpdater`/`SourceCacheUpdater` implemented, DI-registered via `AddHttpClient(SourceCacheUpdater.HttpClientName, ...)` (5 s timeout) and `AddSingleton<ISourceCacheUpdater>` in `Program.cs`. Covered by `SourceCacheUpdaterTests` (Verification rows 1-12).

### 9. Collision detection across resolved download targets
**Status:** ✅ Done — implemented inside `SourceCacheUpdater.ResolveAsync`. Covered by `SourceCacheUpdaterTests.ResolveAsync_CollidingTargetPaths_SkipsBothAndLogsError` (row 12).

### 10. Call the updater from `OnInitialisedAsync`/`OnReseedAsync`/`OnResetAsync`
**Status:** ✅ Done — `QuotinatorDatabaseInitializer` gained `ISourceCacheUpdater`/`autoUpdateSources` constructor params and a `ResolveEffectiveBatchesAsync` helper; `_batches` itself is never mutated. `SeedIfEmptyAsync`/`SeedIfEmptyInternalAsync`/`ReSeedGenresIfEmptyAsync` widened to take an `effectiveBatches` parameter. `IDatabaseInitializer.ReseedAsync(bool forceSourceRefresh = false)`/`ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false)` widened, rippling into the base `DatabaseInitializer` class and `NoOpDatabaseInitializer`. Covered by `SourceCacheWiringTests.ReseedAsync_CalledTwice_InvokesUpdaterIndependentlyEachTime` (row 13) and `...ThreadsThroughToUpdater` tests (row 14).

### 11. `PreviewSeedAsync` reflects the cache, without ever downloading
**Status:** ✅ Done — `PreviewSeedAsync` calls `ResolveEffectiveBatchesAsync(forceRefresh: false, allowNetworkOverride: false)`, always forcing `allowNetwork=false` regardless of `_autoUpdateSources`. Covered by `SourceCacheWiringTests.PreviewSeedAsync_AutoUpdateSourcesTrue_NeverAllowsNetwork` (row 15).

### 12. `forceSourceRefresh` query parameter on reseed/reset
**Status:** ✅ Done — `POST /database/reseed` and `POST /database/reset` both gained a `forceSourceRefresh` query parameter, threaded through to `SourceCacheUpdater`. The distinct "blocked by config" log line is implemented in `SourceCacheUpdater.ResolveAsync`. Covered by `SourceCacheUpdaterTests.ResolveAsync_ForceRefreshTrueButAllowNetworkFalse_DoesNotBypassConfigAndLogsDistinctMessage` (row 7).

### 13. New `POST /api/v1/admin/sources/refresh` endpoint
**Status:** ✅ Done — added to `AdminEndpoints.cs`, calling a new `IDatabaseInitializer.RefreshSourcesAsync(bool force = false)` method (added to the interface, base `DatabaseInitializer`, `NoOpDatabaseInitializer`, and overridden in `QuotinatorDatabaseInitializer` to delegate to `ISourceCacheUpdater` directly — no database interaction). Requires the admin API key (routed through `adminGroup`). Covered by `SourceCacheWiringTests.RefreshSourcesAsync_DoesNotAffectRowCountsOrTouchDatabase` (row 8).

### 14. Failure path never fails startup/reseed/reset
**Status:** ✅ Done — `SourceCacheUpdater.TryDownloadAsync` catches all exceptions and non-success status codes, logging a `Warning` and returning `false`; callers always fall back to the best available file. Covered by `SourceCacheUpdaterTests.ResolveAsync_NetworkFailure_FallsBackToStaleCacheAndLogsWarning` (row 5).

### 15. Update `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes
**Status:** ✅ Done — REST API Endpoints tables in both files updated (reseed/reset `forceSourceRefresh`, new `sources/refresh` row, auto-update behaviour note); `[Description]`/`WithDescription` text on the endpoints in `AdminEndpoints.cs` updated accordingly.

### 16. Update `addon/config.yaml` and `addon/translations/{en,nl,de}.yaml`
**Status:** ✅ Done — added `auto_update_sources` (bool, default `true`) and `source_update_interval_hours` (int, default `24`) to `options`/`schema`/`env_vars` in `config.yaml`, and matching `configuration` entries to `en.yaml`, `nl.yaml`, `de.yaml`.

### 17. Unit tests
**Status:** In progress — `SourceCacheUpdaterTests` (12 tests, rows 1-7 and 9-12) and `SourceCacheWiringTests` (5 tests, rows 8 and 13-15) added and passing; `ManifestSeedPlannerTests` gained 2 tests for the new manifest fields. Full solution build is 0 Warning(s)/0 Error(s) and the full test suite passes (rows 16-17). Rows 18-19 (T1/T2) remain live/manual steps, not part of this coding session.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `AutoUpdateSources=false` skips all network checks; seeds from cached-if-present else the original bundled/local file | Unit test | `SourceCacheUpdaterTests.ResolveAsync_AllowNetworkFalse_NoCacheYet_UsesOriginalFile`, `...AllowNetworkFalse_CacheExists_UsesCachedCopy` |
| 2 | ✅ | Fresh cached copy (within TTL) is used without a network call | Unit test | `SourceCacheUpdaterTests.ResolveAsync_FreshCache_DoesNotCallNetwork` |
| 3 | ✅ | Stale cached copy (past TTL) triggers a `GET`; success overwrites the cache and logs `Information` | Unit test | `SourceCacheUpdaterTests.ResolveAsync_StaleCache_DownloadsAndOverwritesCache` |
| 4 | ✅ | Per-entry `refreshIntervalHours` overrides the global default | Unit test | `SourceCacheUpdaterTests.ResolveAsync_PerEntryRefreshIntervalOverridesGlobalDefault` |
| 5 | ✅ | Network failure logs a `Warning` and falls back to the most recent available copy; the seed operation still succeeds | Unit test | `SourceCacheUpdaterTests.ResolveAsync_NetworkFailure_FallsBackToStaleCacheAndLogsWarning` |
| 6 | ✅ | `forceSourceRefresh=true` bypasses the TTL check | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ForceRefreshTrue_BypassesFreshTtlCheck` |
| 7 | ✅ | `forceSourceRefresh=true` does **not** bypass `AutoUpdateSources=false`, and logs a distinct message explaining the force was blocked by config (not a network failure) | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ForceRefreshTrueButAllowNetworkFalse_DoesNotBypassConfigAndLogsDistinctMessage` |
| 8 | ✅ | `POST /api/v1/admin/sources/refresh` updates both caches without touching the database | Unit test | `SourceCacheWiringTests.RefreshSourcesAsync_DoesNotAffectRowCountsOrTouchDatabase` |
| 9 | ✅ | A user-imports manifest entry with a `downloadUrl` is downloaded and cached, same as a bundled entry (reverses the old bundled-only scope) | Unit test | `SourceCacheUpdaterTests.ResolveAsync_UserImportsEntryWithDownloadUrl_DownloadedAndCachedSameAsBundled` |
| 10 | ✅ | A bundled entry with no `downloadTarget` override defaults to the internal folder; a user-imports entry with no override defaults to the external folder | Unit test | `SourceCacheUpdaterTests.ResolveAsync_NoDownloadTargetOverride_DefaultsByOrigin` |
| 11 | ✅ | An explicit per-entry `downloadTarget` routes that entry to the named folder regardless of which manifest it came from | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ExplicitDownloadTargetOverride_RoutesRegardlessOfOrigin` |
| 12 | ✅ | Two entries whose resolved target paths collide are both skipped (no download, no read from the shared path), a distinct `Error` names both sources and the shared path, and no silent overwrite occurs | Unit test | `SourceCacheUpdaterTests.ResolveAsync_CollidingTargetPaths_SkipsBothAndLogsError` |
| 13 | ✅ | A second `POST /reseed` call re-evaluates staleness independently of the first (proves the fixed-batch-list problem is actually solved) | Unit test | `SourceCacheWiringTests.ReseedAsync_CalledTwice_InvokesUpdaterIndependentlyEachTime` |
| 14 | ✅ | `Reset` also triggers the same refresh-check and collision-detection logic as Reseed | Unit test | `SourceCacheWiringTests.ResetAsync_ForceSourceRefreshTrue_ThreadsThroughToUpdaterSameAsReseed`, `ReseedAsync_ForceSourceRefreshTrue_ThreadsThroughToUpdater` |
| 15 | ✅ | `GET /api/v1/admin/database/seed/preview` reflects an existing cached copy when present, but never triggers a network call or updates staleness state | Unit test | `SourceCacheWiringTests.PreviewSeedAsync_AutoUpdateSourcesTrue_NeverAllowsNetwork` |
| 16 | ✅ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 17 | ✅ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` — all 6 test projects green |
| 18 | ❌ | T1: real app, first startup performs a download, second startup within the TTL does not | Live | To be run |
| 19 | ❌ | T2: Docker container — both `{dataDir}/sources/download/` and `{dataDir}/imports/download/` persist across container restart when the volume is retained | Live | To be run |

---

## Original plan (superseded 2026-07-04 — kept for history, do not implement as written)

The original plan (ephemeral temp-directory substitution) is preserved below for reference only. It does not match the current design and must not be used as an implementation guide.

> Downloaded files are written to a temp directory (`Path.GetTempPath()`), not to the image-bundled `data/sources/` directory (which is read-only under the HA supervisor). The temp path is passed to the seeder in place of the bundled path for that file. After seeding completes the temp files are deleted.
>
> This works because seeding only runs when the database is empty (or on explicit reseed) — the download only needs to survive long enough to seed from. On container restart with a populated database, seeding is skipped and the temp files are never needed.
