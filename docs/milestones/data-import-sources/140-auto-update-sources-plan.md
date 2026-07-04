# #140 — Auto-update bundled source files from manifest URL on startup

**Status:** Re-planned a second time, not yet implemented (as of 2026-07-04). See "Scope expansion (round 2)" for detail.
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
2. **Two default cache locations, not one.** The existing `{dataDir}/sources/download/` (the "internal" folder) remains the default for entries in the bundled sources manifest. A new `{dataDir}/imports/download/` ("external" folder) is the default for entries in the user imports manifest — same structure, mirrored under `imports/` instead of `sources/`. Both reuse the same `DataPaths.DownloadedSourcesFolder = "download"` subfolder-name constant already planned in step 5 (formerly step 4), combined with `SourcesFolder` or `ImportsFolder` respectively — no new constant needed.
3. **Per-entry override of which folder a specific entry uses.** A new optional manifest field, `downloadTarget`, accepts the literal string `"internal"` or `"external"` on any `files[]` entry (bundled or imports) with a `downloadUrl`/`github`. When omitted, the default is whichever folder matches the manifest the entry lives in (point 2). This lets, for example, a user-imports entry explicitly cache into the internal folder if that's ever genuinely wanted, without forcing every entry to accept the origin-based default.
4. **Collision detection is now a hard requirement, not an assumption.** Two different sources (different URLs, potentially from different manifests) can resolve to the same on-disk cache filename (e.g. both literally named `quotefile.json`) — silently overwriting one with the other's content would corrupt whichever source lost the race, with no indication anything went wrong. Every seed operation must build the full set of resolved target paths (folder + filename) across *all* candidate entries — bundled and imports together — before performing any downloads, and treat any path claimed by more than one distinct source as a collision (see step 9 for the exact handling).

A comment recording all four of these points must be posted on #140 before implementation begins (step 2) — same requirement as round 1, since this changes the design again past what round 1's own comment described.

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
**Status:** ⬜ Not started

Both optional, on any `files[]` entry with a `downloadUrl`/`github`. `refreshIntervalHours` (integer, hours) overrides `Quotinator__SourceUpdateIntervalHours` (step 7) for that source. `downloadTarget` (string enum `"internal"` | `"external"`) overrides which cache folder (step 5) that source uses, regardless of which manifest it's declared in.

### 4. Add `RefreshIntervalHours`/`DownloadTarget` to `SeedFile` and read them in `ManifestSeedPlanner`
**Status:** ⬜ Not started

`SeedFile` record gains `RefreshIntervalHours` (nullable int) and `DownloadTarget` (nullable enum) properties. `ManifestSeedPlanner.ResolveUrls` reads both new manifest fields (step 3) into them, alongside the existing `Url`/`DownloadUrl` resolution. `PlanSeed` already runs once per directory (bundled sources, user imports) — `SeedBatch`'s existing `Origin` (`SeedBatchOrigin.Bundled`/`UserImports`) is what a later step uses to pick the *default* target when an entry has no explicit `DownloadTarget`.

### 5. `DataPaths` — internal and external download folders
**Status:** ⬜ Not started

Reuses the existing `DownloadedSourcesFolder = "download"` constant (no new constant needed) combined with either parent folder:
- Internal: `Path.Combine(dataDir, DataPaths.SourcesFolder, DataPaths.DownloadedSourcesFolder)` → `{dataDir}/sources/download/` — default for entries from the bundled sources manifest
- External: `Path.Combine(dataDir, DataPaths.ImportsFolder, DataPaths.DownloadedSourcesFolder)` → `{dataDir}/imports/download/` — default for entries from the user imports manifest

Both are always writable and persistent in every deployment shape (standalone Docker bind mount, HA supervisor's `/data` mount) — neither touches the read-only bundled image path (`/app/data/sources/`), so no HA-specific branching is needed anywhere else in this implementation.

### 6. Wire `Quotinator__AutoUpdateSources` config key
**Status:** ⬜ Not started

Bool, default `true`. Read once in `Program.cs` alongside the existing `IncludeDefaultSources`/`ImportsPath` keys from #62, passed to `SourceCacheUpdater` (step 8).

### 7. Wire `Quotinator__SourceUpdateIntervalHours` config key
**Status:** ⬜ Not started

Int, default `24`. Global fallback TTL used when a manifest entry has no `refreshIntervalHours` override (step 3).

### 8. Implement `ISourceCacheUpdater`/`SourceCacheUpdater`
**Status:** ⬜ Not started

New `Quotinator.Data` component, DI-registered per `CLAUDE.md`'s DI policy — renamed from the earlier `BundledSourceUpdater` concept since it's no longer bundled-only (round 2, point 1). Given the candidate `SeedFile`s from *both* the bundled and user-imports `SeedBatch`es (unchanged — still built once by `SeedBatchesBuilder.Build` at DI-construction time, since that part is pure directory/manifest parsing with no network involved) plus a `forceRefresh` flag, returns an **effective** list of `SeedFile`s with `FilePath` resolved to the downloaded-cache copy where one exists and is being used, leaving the original bundled/local path untouched otherwise. Performs the actual downloads. Must be `async` (uses `IHttpClientFactory`, registered via `builder.Services.AddHttpClient()` — the standard .NET pattern, since there is no existing precedent in this codebase to follow instead).

Staleness signal is the cached copy's own filesystem `LastWriteTimeUtc` — no separate metadata sidecar file. Simpler, and avoids inventing a new on-disk format for a single timestamp. (Flagging this as a deliberate simplification, not obviously non-negotiable — revisit if it proves fragile, e.g. if a restored/copied persistent volume doesn't preserve mtimes.)

### 9. Collision detection across resolved download targets
**Status:** ⬜ Not started

Part of `SourceCacheUpdater`'s resolution pass (step 8), run before any downloads: group every candidate entry (bundled and imports together) by its resolved target path (folder + filename). Any group with more than one distinct source (different URL, or the same URL declared in two places — either counts as distinct entries) is a collision.

For every entry in a colliding group: do not download, and do not trust any file already sitting at the shared path either (there's no way to know which source last wrote it) — fall back directly to that entry's own original bundled/local file instead. Log one `Error` naming every colliding source (name + URL) and the shared path, so the operator can fix their manifest (rename the conflicting `file`, or set an explicit `downloadTarget` to separate them). This re-runs on every seed operation, since manifests can change between calls — never a startup, reseed, or reset failure, same as a network failure.

### 10. Call the updater from `OnInitialisedAsync`/`OnReseedAsync`/`OnResetAsync`
**Status:** ⬜ Not started

Runs at the **start** of all three (already `async Task`) — never inside the synchronous DI factory in `Program.cs`. The constructor-time `_batches` field (both bundled and imports) stays as the fixed *candidate* list; a per-call resolution pass produces the actual files read for that specific operation, so the second and every subsequent `POST /reseed` call can see a different effective path than the first.

### 11. `forceSourceRefresh` query parameter on reseed/reset
**Status:** ⬜ Not started

`POST /api/v1/admin/database/reseed?forceSourceRefresh=true` and the same on `/reset` — bypasses the TTL check for that call only, threaded through to `OnReseedAsync`/`OnResetAsync` → `SourceCacheUpdater`. Does **not** bypass `Quotinator__AutoUpdateSources=false` — an explicit no-network declaration wins over a per-call force flag. When a force is requested but blocked by the config switch, log a distinct `Information`/`Warning` line saying so (e.g. `forceSourceRefresh requested but Quotinator__AutoUpdateSources is false — skipping network check`) — visibly different from "attempted the download and it failed," so operators can tell "blocked by config" apart from "tried and couldn't reach it."

### 12. New `POST /api/v1/admin/sources/refresh` endpoint
**Status:** ⬜ Not started

New endpoint in `AdminEndpoints.cs` — calls `SourceCacheUpdater` directly (both internal and external caches), no database interaction at all. Accepts its own `force` query parameter (same not-overriding-`AutoUpdateSources`-false and distinct-logging rule as step 11). Requires the admin API key. Returns a per-source summary (updated / skipped-fresh / failed / skipped-collision).

### 13. Failure path never fails startup/reseed/reset
**Status:** ⬜ Not started

Any network failure (unreachable, timeout, non-200) logs a `Warning` and falls back to whatever already exists — stale cached copy if present, else the original bundled/local path. Same guarantee for a collision (step 9): never a hard failure, always a safe fallback.

### 14. Update `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes
**Status:** ⬜ Not started

### 15. Update `addon/config.yaml` and `addon/translations/{en,nl,de}.yaml`
**Status:** ⬜ Not started

For both new config keys, per the #62 precedent.

### 16. Unit tests
**Status:** ⬜ Not started

See Verification table below for the full list.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `AutoUpdateSources=false` skips all network checks; seeds from cached-if-present else the original bundled/local file | Unit test | To be named |
| 2 | ❌ | Fresh cached copy (within TTL) is used without a network call | Unit test | To be named |
| 3 | ❌ | Stale cached copy (past TTL) triggers a `GET`; success overwrites the cache and logs `Information` | Unit test | To be named |
| 4 | ❌ | Per-entry `refreshIntervalHours` overrides the global default | Unit test | To be named |
| 5 | ❌ | Network failure logs a `Warning` and falls back to the most recent available copy; the seed operation still succeeds | Unit test | To be named |
| 6 | ❌ | `forceSourceRefresh=true` bypasses the TTL check | Unit test | To be named |
| 7 | ❌ | `forceSourceRefresh=true` does **not** bypass `AutoUpdateSources=false`, and logs a distinct message explaining the force was blocked by config (not a network failure) | Unit test | To be named |
| 8 | ❌ | `POST /api/v1/admin/sources/refresh` updates both caches without touching the database | Unit test | To be named |
| 9 | ❌ | A user-imports manifest entry with a `downloadUrl` is downloaded and cached, same as a bundled entry (reverses the old bundled-only scope) | Unit test | To be named |
| 10 | ❌ | A bundled entry with no `downloadTarget` override defaults to the internal folder; a user-imports entry with no override defaults to the external folder | Unit test | To be named |
| 11 | ❌ | An explicit per-entry `downloadTarget` routes that entry to the named folder regardless of which manifest it came from | Unit test | To be named |
| 12 | ❌ | Two entries whose resolved target paths collide are both skipped (no download, no read from the shared path), a distinct `Error` names both sources and the shared path, and no silent overwrite occurs | Unit test | To be named |
| 13 | ❌ | A second `POST /reseed` call re-evaluates staleness independently of the first (proves the fixed-batch-list problem is actually solved) | Unit test | To be named |
| 14 | ❌ | `Reset` also triggers the same refresh-check and collision-detection logic as Reseed | Unit test | To be named |
| 15 | ❌ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 16 | ❌ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` |
| 17 | ❌ | T1: real app, first startup performs a download, second startup within the TTL does not | Live | To be run |
| 18 | ❌ | T2: Docker container — both `{dataDir}/sources/download/` and `{dataDir}/imports/download/` persist across container restart when the volume is retained | Live | To be run |

---

## Original plan (superseded 2026-07-04 — kept for history, do not implement as written)

The original plan (ephemeral temp-directory substitution) is preserved below for reference only. It does not match the current design and must not be used as an implementation guide.

> Downloaded files are written to a temp directory (`Path.GetTempPath()`), not to the image-bundled `data/sources/` directory (which is read-only under the HA supervisor). The temp path is passed to the seeder in place of the bundled path for that file. After seeding completes the temp files are deleted.
>
> This works because seeding only runs when the database is empty (or on explicit reseed) — the download only needs to survive long enough to seed from. On container restart with a populated database, seeding is skipped and the temp files are never needed.
