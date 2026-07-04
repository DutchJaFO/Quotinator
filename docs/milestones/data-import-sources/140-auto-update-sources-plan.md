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

## Architecture

### New component: `IBundledSourceUpdater` / `BundledSourceUpdater` (`Quotinator.Data`, DI-registered per `CLAUDE.md`'s DI policy)

Given the candidate `SeedFile`s from a bundled `SeedBatch` (unchanged — still built once by `SeedBatchesBuilder.Build` at DI-construction time, since that part is pure directory/manifest parsing with no network involved) plus a `forceRefresh` flag, returns an **effective** list of `SeedFile`s with `FilePath` resolved to the downloaded-cache copy where one exists and is being used, leaving the original bundled path untouched otherwise. Performs the actual downloads. Must be `async` (uses `IHttpClientFactory`, registered via `builder.Services.AddHttpClient()` — the standard .NET pattern, since there is no existing precedent in this codebase to follow instead).

This runs at the **start** of `QuotinatorDatabaseInitializer.OnInitialisedAsync`, `OnReseedAsync`, and `OnResetAsync` (all three, already `async Task`) — never inside the synchronous DI factory in `Program.cs`. The constructor-time `_batches` field stays as the fixed *candidate* list; a per-call resolution pass produces the actual files read for that specific operation, so the second and every subsequent `POST /reseed` call can see a different effective path than the first.

### Staleness tracking

The cached copy's own filesystem `LastWriteTimeUtc` is the staleness signal — no separate metadata sidecar file. Simpler, and avoids inventing a new on-disk format for a single timestamp. (Flagging this as a deliberate simplification, not obviously non-negotiable — revisit if it proves fragile, e.g. if a restored/copied persistent volume doesn't preserve mtimes.)

### New directory constant

`DataPaths.cs` gains `DownloadedSourcesFolder = "download"` — combined as `Path.Combine(dataDir, DataPaths.SourcesFolder, DataPaths.DownloadedSourcesFolder)` (reuses the existing `SourcesFolder = "sources"` name, consistent with how `ImportsFolder` is combined with `dataDir` elsewhere; the bundled-image path already uses the same `SourcesFolder` constant combined with `AppContext.BaseDirectory` instead).

### Endpoints

- `POST /api/v1/admin/database/reseed?forceSourceRefresh=true` and `POST /api/v1/admin/database/reset?forceSourceRefresh=true` — new optional query parameter, threaded through to `OnReseedAsync`/`OnResetAsync` → `BundledSourceUpdater`
- `POST /api/v1/admin/sources/refresh?force=true` — new endpoint in `AdminEndpoints.cs`; calls `BundledSourceUpdater` directly, no database interaction at all

---

## Notes

- **Force does not override `AutoUpdateSources=false`, and this must be logged explicitly, not silent.** An operator who has explicitly disabled all network checks has made a deliberate no-network declaration (air-gapped install, metered connection, etc.) — a `force` flag on a single call should not silently punch through that. Confirmed with the user: when a force-refresh is requested but blocked by `AutoUpdateSources=false`, log a distinct `Information`/`Warning` line saying so (e.g. `forceSourceRefresh requested but Quotinator__AutoUpdateSources is false — skipping network check`) — this must be visibly different from the "attempted the download and it failed" log line, so an operator reading logs can tell "blocked by config" apart from "tried and couldn't reach it" at a glance. Applies to both the query-parameter force path and the dedicated refresh endpoint's own `force` parameter.
- **`scripts/sources.json` retirement is explicitly out of scope for this issue** — the GitHub issue's own Notes section calls it a separate follow-up; do not fold it in here.
- **HA read-only constraint is fully resolved by the persistent-directory design**: `{dataDir}` is always the writable, persistent volume in every deployment shape (standalone Docker bind mount, HA supervisor's `/data` mount) — `{dataDir}/sources/download/` never touches the read-only bundled image path (`/app/data/sources/`), so no HA-specific branching is needed anywhere in the implementation.

---

## Step status

- [x] Post scope-expansion comment on #140 (write-path, refresh timing, TTL/override, force mechanism — see "Scope expansion" above)
- [ ] `schemas/manifest.schema.json` — add optional `refreshIntervalHours` integer property
- [ ] `SeedFile` record — add `RefreshIntervalHours` property; `ManifestSeedPlanner.ResolveUrls` reads it from the manifest entry
- [ ] `DataPaths.DownloadedSourcesFolder` constant added
- [ ] `Quotinator__AutoUpdateSources` config key (default `true`) wired into `Program.cs`
- [ ] `Quotinator__SourceUpdateIntervalHours` config key (default `24`) wired into `Program.cs`
- [ ] `IBundledSourceUpdater`/`BundledSourceUpdater` implemented, DI-registered, `IHttpClientFactory` registered via `AddHttpClient()`
- [ ] `QuotinatorDatabaseInitializer.OnInitialisedAsync`/`OnReseedAsync`/`OnResetAsync` call the updater at the start of each, before reading any bundled file
- [ ] `POST /api/v1/admin/database/reseed` and `/reset` gain `forceSourceRefresh` query parameter
- [ ] New `POST /api/v1/admin/sources/refresh` endpoint (admin key required, own `force` parameter, per-source summary response)
- [ ] Failure path never fails startup/reseed/reset — falls back to cached-then-stale, then bundled
- [ ] `README.md`, `addon/DOCS.md`, endpoint `[Description]` attributes updated
- [ ] `addon/config.yaml` + `addon/translations/{en,nl,de}.yaml` updated for both new config keys
- [ ] Unit tests for all of the above (see Verification)

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
