# #140 — Auto-update bundled source files from manifest URL on startup

**Status:** In progress (step 29)
**Tiers required:** T1, T2, T3 (touches `DatabaseInitializer`/`QuotinatorDatabaseInitializer` → T1+T2; touches `addon/config.yaml` and `addon/translations/{en,nl,de}.yaml` → T3)
**GitHub issue:** #140
**Depends on:** #58 fix (manifest `url` field) — done; #62 (`AutoUpdateSources` follows the same config pattern) — done; #63 (`downloadUrl`/`github` manifest groundwork) — done
**Unblocks:** retirement of `scripts/sources.json` — done (see "Scope expansion round 3" below)

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

## Scope expansion (round 3 — T1 corruption bug found and fixed, resolved with the user 2026-07-04)

**What T1 live testing found:** the first real reseed against a running app dropped the quote count from 788 to 2. Root cause: `data/sources/vilaboim_movie-quotes.json` and `data/sources/NikhilNamal17_popular-movie-quotes.json` are not raw copies of their upstream repos — they are *converted*. The historical `scripts/seed.csx` (a standalone `dotnet-script` tool, never referenced by the running app) downloaded each source's raw upstream format and transformed it into Quotinator's canonical schema, critically generating a **stable UUID `id`** via SHA-256 of normalised quote+source text (upstream has no `id` field at all). But this plan's own `github` manifest object computes `downloadUrl` from that same raw upstream URL — so `SourceCacheUpdater` was downloading raw, unconverted JSON and overwriting the working canonical cache with it, which then failed to deserialize at seed time.

**The fix, agreed with the user across several rounds of discussion:**
1. **First-party "converter plugins"** — compiled .NET class library projects (`Quotinator.Converters.Vilaboim`, `Quotinator.Converters.NikhilNamal17`), **not** external processes/scripts (an explicit security decision to avoid arbitrary command execution from JSON-declared commands). One plugin per source, since the two raw formats aren't even shaped alike (`vilaboim` is a regex-parsed array of bare strings; `NikhilNamal17` is a field-mapped JSON object array).
2. Plugins run **live, inside the already-shipped container** — ordinary compiled project references added to `Quotinator.Api.csproj`, no new runtime/SDK bundling in `docker/Dockerfile`.
3. A new manifest field, `converter` (string, matches a plugin's `Name`), on any `files[]` entry with a `downloadUrl`/`github`. Both bundled and user-imports manifests may declare one — safe specifically because only names matching a plugin actually compiled into the image are ever looked up; an unrecognised name fails closed.
4. The download pipeline becomes **download to temp → optional conversion (named plugin) → validate the result is canonical-schema → atomic move into the persistent cache path**. Validation was already a hard requirement independent of conversion — even a source with no `converter` declared is now rejected if its content doesn't deserialize as canonical `SourceQuote` schema, which is what actually closes the corruption hole.
5. **Validation also applies to existing cache-hit reads**, not only freshly-downloaded files — an already-corrupted cache file (e.g. from before this fix shipped) is rejected on next access and falls back / re-downloads automatically, self-healing without a manual forced refresh.
6. `scripts/seed.csx`/`scripts/sources.json` are retired. The running app itself, via the existing `POST /api/v1/admin/sources/refresh?force=true` endpoint, is now the tool a maintainer uses locally to regenerate `data/sources/*.json` before a commit. `scripts/SOURCES.md` was rewritten to describe the new plugin-based workflow.

**Correctness constraint (verified, not assumed):** the ported `QuoteIdentity.StableId` algorithm (`Quotinator.Core.Import`) was checked against the real, already-shipped production ID for a known quote/source pair and matches exactly — both converter plugins also have a dedicated ID-stability regression test doing the same against their respective committed baseline files (see Verification rows 20 below). Also confirmed empirically: `SourceQuoteFileReader.TryParse` correctly rejects the actual corrupted cache file this incident produced on the developer's own machine (`bin/Debug/net10.0/data/sources/download/vilaboim_movie-quotes.json`).

**Follow-up work explicitly deferred, not part of this plan:** a new GitHub issue is needed to redesign/rename the two converter plugins with more generic names (their current names are tied to the specific upstream repo, not to what they do) and to consider reserving some plugin slots for internal-only use. Tracked in project memory, not yet filed.

**Observability improvements found necessary during manual review of the fix (same session, same scope):**
- `GET /database/seed/preview` and `POST /sources/refresh` previously gave no way to tell a degraded/fallback source apart from a healthy one, or to know how stale a cached copy actually was. Both now return, per file: `refreshOutcome` (`updated`/`uptodate`/`failed`/`skippedcollision`) and `lastRefreshedAtUtc` (the cache file's real last-write time, not "now") when the file has a `downloadUrl`.
- Preview also could not distinguish a genuinely empty file from one that failed to parse at all (both looked identical: `quoteCount: 0`). Added `issue` (`missing`/`invalidjson`, machine-readable) and a `message` field localised via the existing `IApiLocalizer`/`ApiMessages` mechanism (same pattern already used by the search/random-quote endpoints) — two new keys, `ErrorSeedFileMissing`/`ErrorSeedFileInvalidJson`, added to all three `UI.*.json` locale files.
- Null JSON properties are now omitted from every API response, application-wide (`ConfigureHttpJsonOptions` + `JsonIgnoreCondition.WhenWritingNull`) — verified against `System.Text.Json` docs, not assumed.

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
**Status:** ✅ Done — `SourceCacheUpdaterTests` (12 tests, rows 1-7 and 9-12) and `SourceCacheWiringTests` (5 tests, rows 8 and 13-15) added and passing; `ManifestSeedPlannerTests` gained 2 tests for the new manifest fields. Full solution build is 0 Warning(s)/0 Error(s) and the full test suite passes (rows 16-17).

### 18. Extract `SourceQuoteFileReader` into `Quotinator.Core.Import`
**Status:** ✅ Done — pure refactor, no behaviour change. `QuotinatorDatabaseInitializer.LoadQuotesFromFile` becomes a thin wrapper. The one `JsonNode.Parse` shape-sniffing call (bare array vs. `{"quotes":[...]}` wrapper) is CLAUDE.md's own documented JSON-parsing-policy exception, relocated not newly introduced. Covered by `SourceQuoteFileReaderTests` (6 tests).

### 19. Add `QuoteIdentity`/`YearParsing`/`QuoteTypeNormalisation` shared helpers
**Status:** ✅ Done — ported verbatim from `scripts/seed.csx` into `Quotinator.Core.Import` (not a separate `Quotinator.Converters.Common` project — both plugins already reference `Quotinator.Core` for `SourceQuote`, so a separate project would buy no dependency-isolation benefit, only solution sprawl). `QuoteIdentity.StableId` is pinned against the real, already-shipped production ID for a known quote/source pair. Covered by `QuoteIdentityTests`, `YearParsingTests`, `QuoteTypeNormalisationTests` (22 tests).

### 20. Build `Quotinator.Converters.Vilaboim` plugin
**Status:** ✅ Done — implements `IQuoteSourceConverter`, `Name = "vilaboim"`. Parses the regex-based quoted-string raw format. Covered by `VilaboimMovieQuotesConverterTests` (6 tests), including the ID-stability regression test against the real committed `data/sources/vilaboim_movie-quotes.json`.

### 21. Build `Quotinator.Converters.NikhilNamal17` plugin
**Status:** ✅ Done — implements `IQuoteSourceConverter`, `Name = "nikhilnamal17"`. Parses the field-mapped JSON object-array raw format, with a custom `YearJsonConverter` handling the upstream's inconsistently-typed (number or string) year field. Covered by `NikhilNamal17PopularMovieQuotesConverterTests` (8 tests), including the ID-stability regression test against the real committed baseline file.

### 22. Add `IQuoteSourceConverter`/`SourceConversionException`; widen manifest plumbing
**Status:** ✅ Done — new interface + exception in `Quotinator.Data.Import` (schema-agnostic — file paths only, no `SourceQuote` reference, so no new `Quotinator.Core` dependency for `Quotinator.Data`). `SeedFile`, `ManifestFileEntryDto`, `ManifestSeedPlanner`, and `schemas/manifest.schema.json` all gained an optional `converter`/`Converter` field. `SourceCacheOptions` gained `Converters` (registry) and `ValidateCanonicalSchema` (delegate) — both nullable/optional so every existing call site and test kept compiling unchanged.

### 23. Modify `SourceCacheUpdater` pipeline: conversion + validation
**Status:** ✅ Done — `TryDownloadAndPrepareAsync` now does download-to-temp → optional conversion (named plugin, into a second temp file) → canonical-schema validation → atomic move. Validation also runs on existing cache-hit reads (both the `!allowNetwork` and fresh-within-TTL paths), not only freshly-downloaded files, so an already-corrupted cache self-heals. The real Core/Data dependency-direction problem (validation needs `SourceQuote`, which `Quotinator.Data` must not depend on) is resolved by injecting the validator as a `Func<string, bool>` delegate built at the composition root (`Program.cs`), not a new project reference. Covered by 8 new `SourceCacheUpdaterTests` cases, including `ResolveAsync_NoConverterButValidationFails_FallsBackWithoutCorruptingCache` — the direct regression test for the actual production bug.

### 24. Wire `Program.cs`/`Quotinator.Api.csproj`/`docker/Dockerfile`/`Quotinator.slnx`
**Status:** ✅ Done — converter registry (`Dictionary<string, IQuoteSourceConverter>`) and the validation delegate built in `Program.cs`; two new `ProjectReference`s on `Quotinator.Api.csproj`; two new `COPY` lines in the Dockerfile's restore layer (ordinary compiled project references — no new runtime/SDK bundling, resolving the earlier image-size concern); both new `src/`+`tests/` project pairs and their CVE folders registered in `Quotinator.slnx`.

### 25. Add `"converter"` entries to `data/sources/manifest.json`
**Status:** ✅ Done — the actual bug-fix data change: `"converter": "vilaboim"` / `"converter": "nikhilnamal17"` added to the two affected bundled entries.

### 26. Replace the seed.csx-shelling test; retire `scripts/seed.csx`/`sources.json`
**Status:** ✅ Done — `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` (which shelled out to `dotnet-script scripts/seed.csx`) replaced with `ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline`, an in-process equivalent using two new committed raw-format fixtures (`tests/Quotinator.Api.Tests/Solution/Fixtures/`, copied from the previously-gitignored `scripts/cache/`) — full 788-quote ID-set diffing against the real baseline files, no `dotnet-script` dependency. `scripts/seed.csx`, `scripts/sources.json`, `schemas/seed-sources.schema.json`, and `scripts/cache/` deleted. `scripts/SOURCES.md` rewritten for the new plugin-based workflow. `SeedScriptIntegrityTests.cs` renamed to `RawSourceFixtureIntegrityTests.cs`, adapted to validate the new fixtures against the (still-relevant) upstream format schemas instead of the deleted `scripts/cache/`.

### 27. Update `CLAUDE.md`/`README.md`/`docs/data-import.md` references
**Status:** ✅ Done — project structure trees, Data Sources section, Commands section, Key Files table, and the two remaining `seed.csx` mentions in `docs/data-import.md` all updated to describe the converter-plugin workflow instead.

### 28. Observability: `refreshOutcome`/`lastRefreshedAtUtc`/`issue`/`message`
**Status:** ✅ Done — `SourceRefreshResult` gained `LastRefreshedAtUtc`; `SeedFilePreview` gained `RefreshOutcome`, `LastRefreshedAtUtc`, and `Issue` (a new `SeedFileIssue` enum: `Missing`/`InvalidJson`). `LoadQuotesFromFile` now reports *why* a file contributed zero quotes, not just that it did. The preview/refresh endpoints surface these, with `issue` mapped to a localised `message` via `IApiLocalizer`/two new `ApiMessages` keys (`SeedFileMissing`, `SeedFileInvalidJson`) in all three `UI.*.json` locale files. Covered by 4 new tests across `SourceCacheUpdaterTests`/`SourceCacheWiringTests`.

### 29. Omit null JSON properties from all API responses
**Status:** ✅ Done — `builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)` in `Program.cs`, verified against `System.Text.Json` documentation rather than assumed. Applies application-wide; checked existing tests for any assertion on a null property's *presence* before applying — none found.

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
| 18 | ✅ | T1: real app, first startup/reseed performs a download, a subsequent `force=false` refresh does not re-download (fast no-op) | Live | Confirmed 2026-07-04 against a real running instance — see round 3 scope expansion above for the corruption found and fixed along the way |
| 19 | ✅ | T2: Docker container — `{dataDir}/sources/download/` persists across container restart when the volume is retained, and the standard smoke tests + admin endpoints (`seed/preview`, `sources/refresh`, `database/reseed`) all behave correctly inside the container | Live | Confirmed 2026-07-04 — `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container run with `-e Quotinator__DataDir=/data` and a host-mounted volume at `/data` (mounting at `/app/data` instead would hide the image's own baked-in `/app/data/sources`, a real interaction discovered during this run, not specific to this fix); first startup downloaded/converted both sources (788 quotes, matching the non-corrupted count); `seed/preview` and `sources/refresh` correctly returned `refreshOutcome`/`lastRefreshedAtUtc` (and correctly omitted both for `quotinator-curated.json`, which has no `downloadUrl`); `database/reseed` returned 788 quotes; after `docker restart`, schema/data came back unchanged with no re-download (within TTL) and the two cache files were confirmed still present with unchanged timestamps on the host-mounted volume; `imports/download/` not separately exercised (no `imports/manifest.json` entries with a `downloadUrl` configured in this test) |
| 20 | ✅ | Both converter plugins reproduce the exact committed production ID for a known quote/source pair (id-stability regression) | Unit test | `VilaboimMovieQuotesConverterTests.ConvertAsync_KnownQuoteSourcePair_MatchesCommittedBaselineId`, `NikhilNamal17PopularMovieQuotesConverterTests.ConvertAsync_KnownQuoteSourcePair_MatchesCommittedBaselineId`, `QuoteIdentityTests.StableId_KnownQuoteSourcePair_MatchesCommittedProductionId` |
| 21 | ✅ | A source with a registered converter is converted, then validated, then moved into place | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ConverterRegistered_ConvertsBeforeCaching` |
| 22 | ✅ | An unregistered converter name fails closed (falls back like a network failure, logs a `Warning` naming it) | Unit test | `SourceCacheUpdaterTests.ResolveAsync_UnregisteredConverterName_FailsClosedAndLogsWarning` |
| 23 | ✅ | A converter that throws `SourceConversionException` falls back exactly like a network failure | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ConverterThrows_FallsBackLikeNetworkFailure` |
| 24 | ✅ | No converter declared + downloaded content fails canonical-schema validation → falls back without corrupting the cache (the actual production bug, reproduced and fixed) | Unit test | `SourceCacheUpdaterTests.ResolveAsync_NoConverterButValidationFails_FallsBackWithoutCorruptingCache` |
| 25 | ✅ | An already-corrupted cache file (no new download involved) is rejected on next access and self-heals, both when network is allowed and when it isn't | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ExistingCacheFailsValidation_NetworkAllowed_TriggersFreshDownloadEvenIfFresh`, `...NetworkDisallowed_FallsBackToOriginalFile` |
| 26 | ✅ | The real corrupted cache file from the production incident fails validation under the shipped fix | Live (temporary test, removed after verification) | Confirmed 2026-07-04 against `bin/Debug/net10.0/data/sources/download/vilaboim_movie-quotes.json` |
| 27 | ✅ | `GET /database/seed/preview` surfaces per-file `refreshOutcome`/`lastRefreshedAtUtc` from the cache resolution | Unit test | `SourceCacheWiringTests.PreviewSeedAsync_AttachesRefreshOutcomeAndTimestampFromResolution` |
| 28 | ✅ | `GET /database/seed/preview` distinguishes a genuine parse failure (`issue`: `missing`/`invalidjson`) from a validly empty file | Unit test | `SourceCacheWiringTests.PreviewSeedAsync_MalformedFile_ReportsInvalidJsonIssue`, `...MissingFile_ReportsMissingIssue` |
| 29 | ✅ | `POST /sources/refresh` result's `lastRefreshedAtUtc` reflects the cache file's actual mtime, not "now" | Unit test | `SourceCacheUpdaterTests.ResolveAsync_FreshCache_LastRefreshedAtUtcReflectsActualFileAge`, `...NoCacheAndNetworkDisallowed_LastRefreshedAtUtcIsNull` |
| 30 | ✅ | Build clean after the full round-3 change set | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 31 | ✅ | All tests pass after the full round-3 change set | Live | `dotnet test --configuration Release --verbosity normal` — 8 test projects, 700 tests, all green |
| 32 | ✅ | New/changed UI translation keys (`ErrorSeedFileMissing`, `ErrorSeedFileInvalidJson`) are complete and non-empty in every locale | Unit test | `TranslationCompletenessTests.AllLanguageFiles_HaveExactlyTheSameKeysAsBaseline`, `...HaveNoEmptyValues` |

---

## Original plan (superseded 2026-07-04 — kept for history, do not implement as written)

The original plan (ephemeral temp-directory substitution) is preserved below for reference only. It does not match the current design and must not be used as an implementation guide.

> Downloaded files are written to a temp directory (`Path.GetTempPath()`), not to the image-bundled `data/sources/` directory (which is read-only under the HA supervisor). The temp path is passed to the seeder in place of the bundled path for that file. After seeding completes the temp files are deleted.
>
> This works because seeding only runs when the database is empty (or on explicit reseed) — the download only needs to survive long enough to seed from. On container restart with a populated database, seeding is skipped and the temp files are never needed.
