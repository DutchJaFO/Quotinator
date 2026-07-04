# #140 — Auto-update bundled source files from manifest URL on startup

**Status:** Re-planned, not yet implemented (as of 2026-07-04). See "Scope expansion" for detail; [comment posted on #140](https://github.com/DutchJaFO/Quotinator/issues/140#issuecomment-4881368528).
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

## Scope expansion (resolved with the user 2026-07-04 — must be posted as a GitHub comment before implementation)

The original issue text does not mention a refresh-staleness window, a per-source override, or a dedicated refresh endpoint. Discussion produced these decisions, which extend the issue's scope:

1. **Write-path:** persistent `{dataDir}/sources/download/` directory (not the ephemeral temp-file design) — writable at runtime in every deployment (standalone Docker and HA add-on alike, since `{dataDir}` is always the persistent volume), scanned as an override *in addition to* the bundled `data/sources/` files.
2. **Refresh timing:** a fresh staleness check runs on every seed operation (startup, `POST /reseed`, `POST /reset` — all three), not just once at startup. `Quotinator__AutoUpdateSources=false` remains the master switch to skip all checks entirely (e.g. air-gapped homelab installs with no internet) — this is what prevents startup from stalling when there is no network.
3. **TTL / staleness:** a downloaded copy is not re-fetched on every single seed operation — it is considered fresh for `Quotinator__SourceUpdateIntervalHours` (global default **24 hours**), overridable **per manifest entry** via a new optional `refreshIntervalHours` field.
4. **Force-refresh:** two independent mechanisms — a query parameter on the existing reseed/reset endpoints (refresh-then-seed in one call) **and** a new dedicated endpoint that refreshes source files on disk only, without touching the database, so an operator can control update timing independently of when they want to reseed.

A comment recording all four points must be posted on #140 before implementation begins, and the issue body itself should be read again alongside this doc so nobody mistakes the original text for the full scope.

---

## Spec requirements (final, supersedes the original plan's list)

1. `Quotinator__AutoUpdateSources` config key (default `true`) — master switch; `false` skips all network checks entirely, seeding proceeds from whatever's already on disk (downloaded cache if present, else the bundled file)
2. `Quotinator__SourceUpdateIntervalHours` config key (default `24`) — global default TTL before a cached download is considered stale enough to re-check
3. `schemas/manifest.schema.json` gains an optional per-entry `refreshIntervalHours` (integer, hours) — overrides the global default for that specific source; only meaningful alongside `downloadUrl`/`github`
4. New persistent directory `{dataDir}/sources/download/` — holds auto-downloaded copies of bundled sources only, keyed by original filename (e.g. `{dataDir}/sources/download/vilaboim_movie-quotes.json`); survives restarts; writable in every deployment shape including the HA add-on
5. On startup, `POST /api/v1/admin/database/reseed`, and `POST /api/v1/admin/database/reset`: for each bundled manifest entry with a `downloadUrl`, resolve the **effective** seed file before reading it:
   - `AutoUpdateSources=false` → skip the network check entirely; use the downloaded-cache copy if one exists, else the bundled path
   - Otherwise, check the cached copy's staleness (its file `LastWriteTimeUtc`) against the per-entry or global TTL. Fresh enough and not forced → use the cached copy without any network call
   - Stale, missing, or force-requested → attempt `GET` with a 5 s timeout
     - Success → overwrite `{dataDir}/sources/download/<file>`, use it, log at `Information`
     - Failure (unreachable, timeout, non-200) → log a `Warning`, fall back to whatever already exists (stale cached copy if present, else the bundled path) — **never a startup, reseed, or reset failure**
6. `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` each gain a `forceSourceRefresh` query parameter (default `false`) — bypasses the TTL check for that call only; still respects `AutoUpdateSources=false` (an explicit "no network" declaration is not overridden by a force flag — see Notes)
7. New `POST /api/v1/admin/sources/refresh` endpoint — refreshes bundled source files on disk only (writes to `{dataDir}/sources/download/`), does **not** reseed or touch the database; accepts its own `force` query parameter; requires the admin API key; returns a per-source summary (updated / skipped-fresh / failed)
8. Scoped to bundled sources only — user import files in `{dataDir}/imports/` have no `downloadUrl` and are never touched by any of this
9. `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes, `addon/config.yaml`, and `addon/translations/{en,nl,de}.yaml` all updated for the two new config keys and the new endpoint

---

## Steps

### 1. Post scope-expansion comment on #140
**Status:** ✅ Done — [comment posted](https://github.com/DutchJaFO/Quotinator/issues/140#issuecomment-4881368528) 2026-07-04, recording the write-path, refresh timing, TTL/override, and force-mechanism decisions from "Scope expansion" above.

### 2. Add `refreshIntervalHours` to `schemas/manifest.schema.json`
**Status:** ⬜ Not started

Optional per-entry integer property (hours) on each `files[]` entry — overrides `Quotinator__SourceUpdateIntervalHours` (step 6) for that specific source. Only meaningful alongside `downloadUrl`/`github`.

### 3. Add `RefreshIntervalHours` to `SeedFile` and read it in `ManifestSeedPlanner`
**Status:** ⬜ Not started

`SeedFile` record gains a `RefreshIntervalHours` (nullable int) property. `ManifestSeedPlanner.ResolveUrls` reads the new manifest field (step 2) into it, alongside the existing `Url`/`DownloadUrl` resolution.

### 4. Add `DataPaths.DownloadedSourcesFolder` constant
**Status:** ⬜ Not started

`DataPaths.cs` gains `DownloadedSourcesFolder = "download"`, combined as `Path.Combine(dataDir, DataPaths.SourcesFolder, DataPaths.DownloadedSourcesFolder)` → `{dataDir}/sources/download/`. Reuses the existing `SourcesFolder = "sources"` name (the bundled-image path already combines the same constant with `AppContext.BaseDirectory` instead of `dataDir`). This directory is always writable and persistent in every deployment shape (standalone Docker bind mount, HA supervisor's `/data` mount) — it never touches the read-only bundled image path (`/app/data/sources/`), so no HA-specific branching is needed anywhere else in this implementation.

### 5. Wire `Quotinator__AutoUpdateSources` config key
**Status:** ⬜ Not started

Bool, default `true`. Read once in `Program.cs` alongside the existing `IncludeDefaultSources`/`ImportsPath` keys from #62, passed to `BundledSourceUpdater` (step 7).

### 6. Wire `Quotinator__SourceUpdateIntervalHours` config key
**Status:** ⬜ Not started

Int, default `24`. Global fallback TTL used when a manifest entry has no `refreshIntervalHours` override (step 2).

### 7. Implement `IBundledSourceUpdater`/`BundledSourceUpdater`
**Status:** ⬜ Not started

New `Quotinator.Data` component, DI-registered per `CLAUDE.md`'s DI policy. Given the candidate `SeedFile`s from a bundled `SeedBatch` (unchanged — still built once by `SeedBatchesBuilder.Build` at DI-construction time, since that part is pure directory/manifest parsing with no network involved) plus a `forceRefresh` flag, returns an **effective** list of `SeedFile`s with `FilePath` resolved to the downloaded-cache copy where one exists and is being used, leaving the original bundled path untouched otherwise. Performs the actual downloads. Must be `async` (uses `IHttpClientFactory`, registered via `builder.Services.AddHttpClient()` — the standard .NET pattern, since there is no existing precedent in this codebase to follow instead).

Staleness signal is the cached copy's own filesystem `LastWriteTimeUtc` — no separate metadata sidecar file. Simpler, and avoids inventing a new on-disk format for a single timestamp. (Flagging this as a deliberate simplification, not obviously non-negotiable — revisit if it proves fragile, e.g. if a restored/copied persistent volume doesn't preserve mtimes.)

### 8. Call the updater from `OnInitialisedAsync`/`OnReseedAsync`/`OnResetAsync`
**Status:** ⬜ Not started

Runs at the **start** of all three (already `async Task`) — never inside the synchronous DI factory in `Program.cs`. The constructor-time `_batches` field stays as the fixed *candidate* list; a per-call resolution pass produces the actual files read for that specific operation, so the second and every subsequent `POST /reseed` call can see a different effective path than the first.

### 9. `forceSourceRefresh` query parameter on reseed/reset
**Status:** ⬜ Not started

`POST /api/v1/admin/database/reseed?forceSourceRefresh=true` and the same on `/reset` — bypasses the TTL check for that call only, threaded through to `OnReseedAsync`/`OnResetAsync` → `BundledSourceUpdater`. Does **not** bypass `Quotinator__AutoUpdateSources=false` — an explicit no-network declaration wins over a per-call force flag. When a force is requested but blocked by the config switch, log a distinct `Information`/`Warning` line saying so (e.g. `forceSourceRefresh requested but Quotinator__AutoUpdateSources is false — skipping network check`) — visibly different from "attempted the download and it failed," so operators can tell "blocked by config" apart from "tried and couldn't reach it."

### 10. New `POST /api/v1/admin/sources/refresh` endpoint
**Status:** ⬜ Not started

New endpoint in `AdminEndpoints.cs` — calls `BundledSourceUpdater` directly, no database interaction at all. Accepts its own `force` query parameter (same not-overriding-`AutoUpdateSources`-false and distinct-logging rule as step 9). Requires the admin API key. Returns a per-source summary (updated / skipped-fresh / failed).

### 11. Failure path never fails startup/reseed/reset
**Status:** ⬜ Not started

Any network failure (unreachable, timeout, non-200) logs a `Warning` and falls back to whatever already exists — stale cached copy if present, else the bundled path.

### 12. Update `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes
**Status:** ⬜ Not started

### 13. Update `addon/config.yaml` and `addon/translations/{en,nl,de}.yaml`
**Status:** ⬜ Not started

For both new config keys, per the #62 precedent.

### 14. Unit tests
**Status:** ⬜ Not started

See Verification table below for the full list.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `AutoUpdateSources=false` skips all network checks; seeds from cached-if-present else bundled | Unit test | To be named |
| 2 | ❌ | Fresh cached copy (within TTL) is used without a network call | Unit test | To be named |
| 3 | ❌ | Stale cached copy (past TTL) triggers a `GET`; success overwrites the cache and logs `Information` | Unit test | To be named |
| 4 | ❌ | Per-entry `refreshIntervalHours` overrides the global default | Unit test | To be named |
| 5 | ❌ | Network failure logs a `Warning` and falls back to the most recent available copy; the seed operation still succeeds | Unit test | To be named |
| 6 | ❌ | `forceSourceRefresh=true` bypasses the TTL check | Unit test | To be named |
| 7 | ❌ | `forceSourceRefresh=true` does **not** bypass `AutoUpdateSources=false`, and logs a distinct message explaining the force was blocked by config (not a network failure) | Unit test | To be named |
| 8 | ❌ | `POST /api/v1/admin/sources/refresh` updates the cache without touching the database | Unit test | To be named |
| 9 | ❌ | User import files are never affected (no `downloadUrl`, `SeedBatchOrigin.UserImports` skipped entirely) | Unit test | To be named |
| 10 | ❌ | A second `POST /reseed` call re-evaluates staleness independently of the first (proves the fixed-batch-list problem is actually solved) | Unit test | To be named |
| 11 | ❌ | `Reset` also triggers the same refresh-check logic as Reseed | Unit test | To be named |
| 12 | ❌ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 13 | ❌ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` |
| 14 | ❌ | T1: real app, first startup performs a download, second startup within the TTL does not | Live | To be run |
| 15 | ❌ | T2: Docker container — `{dataDir}/sources/download/` persists across container restart when the volume is retained | Live | To be run |

---

## Original plan (superseded 2026-07-04 — kept for history, do not implement as written)

The original plan (ephemeral temp-directory substitution) is preserved below for reference only. It does not match the current design and must not be used as an implementation guide.

> Downloaded files are written to a temp directory (`Path.GetTempPath()`), not to the image-bundled `data/sources/` directory (which is read-only under the HA supervisor). The temp path is passed to the seeder in place of the bundled path for that file. After seeding completes the temp files are deleted.
>
> This works because seeding only runs when the database is empty (or on explicit reseed) — the download only needs to survive long enough to seed from. On container restart with a populated database, seeding is skipped and the temp files are never needed.
