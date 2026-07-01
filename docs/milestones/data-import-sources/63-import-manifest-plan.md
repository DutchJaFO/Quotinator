# #63 — Import manifest

**Status:** Resolved in code — pending release
**GitHub issue:** #63
**Tiers required:** T1, T2

---

## Spec requirements

1. `data/sources/manifest.json` lists bundled source files with their seeding order
2. `manifest.json` has a `duplicateResolution` field at the manifest level (default: `newest-wins`)
3. `manifest.schema.json` validates the manifest format
4. Seeder reads the manifest and processes files in the declared order
5. Files in `data/sources/` (or the imports directory) that are NOT listed in the manifest are still processed — appended after the listed files in alphabetical order
6. When no manifest exists in the user's imports folder, one is auto-created from the discovered files
7. `Quotinator__CreateMissingManifest` config key controls auto-creation (default: `true`)
8. A warning is logged when a manifest is auto-created

---

## Step status

- [x] `data/sources/manifest.json` exists with ordered file list
- [x] `manifest.schema.json` exists with `duplicateResolution` property
- [x] Seeder reads manifest and follows the declared order
- [x] Schema validated in tests
- [x] Unlisted files are **appended alphabetically** after listed ones
- [x] User imports folder: auto-create `manifest.json` when missing
- [x] `Quotinator__CreateMissingManifest` config key
- [x] Warning log on auto-creation

---

## Implementation notes

All manifest reading/ordering/auto-create logic was extracted out of file-local static functions in `Program.cs` (`OrderedByManifest`, `ParseManifestPolicyNode`) into `Quotinator.Data.Import.ManifestSeedPlanner` (behind `IManifestSeedPlanner`). This was required for unit testability — the old functions could only be exercised via `WebApplicationFactory` integration tests, which aren't suited to pure parsing/ordering logic. `Program.cs`'s `BuildSeedBatches` now calls `IManifestSeedPlanner.PlanSeed(dir, configPolicy, allowAutoCreate)`, with `allowAutoCreate: false` for the bundled (read-only) sources directory and `allowAutoCreate: createMissingManifest` for the user imports directory.

**Manifest data fix (discovered during this session's verification pass):** `data/sources/manifest.json`'s `url` field previously held only the GitHub repo homepage, which is not a fetchable URL — a gap relevant to #140 (auto-update sources), which needs to `GET` a working raw-file URL. A manifest file entry is now exactly one of three kinds, distinguished by field presence: **local** (`file`/`name` only), **GitHub** (a `github: {owner, repo, path, branch}` object, from which `url`/`downloadUrl` are computed via the standard `github.com`/`raw.githubusercontent.com` conventions), or **external URI** (a plain `url`, optionally with a separate `downloadUrl`). `github` and `url` are mutually exclusive on the same entry, enforced both in the schema (`not: {required: ["github","url"]}`) and in `ManifestSeedPlanner`. The bundled `vilaboim` and `NikhilNamal17` entries were converted to the `github` kind. `SeedFile` gained an optional `DownloadUrl` carried through for #140's future consumption — it is never persisted (`ImportBatch.Url` only stores the provenance `Url`).

**Two bugs found and fixed during live T1 testing:**
1. **Crash on empty/invalid JSON source files** — `QuotinatorDatabaseInitializer.LoadQuotesFromFile` called `JsonNode.Parse` unguarded; an empty file (a real scenario once users can drop files into the auto-discovered imports folder) threw an unhandled `JsonReaderException` and crashed the app at startup. Fixed by catching `JsonException`, logging a `[Database - Seed]` warning naming the offending file, and treating it as zero quotes rather than propagating. Regression test: `ImportBatchesTests.Seeding_EmptyOrInvalidJsonSourceFile_IsSkippedWithoutCrashing`.
2. **Manifest auto-create warning logged through the wrong pipeline** — seed batches (including the manifest auto-create call) were originally computed eagerly before `builder.Build()`, using a separate bootstrap `LoggerFactory.Create(b => b.AddConsole())` instead of the app's Serilog pipeline. The warning printed in plain MEL format before the "Quotinator starting" banner instead of the standard `[HH:mm:ss INF] [Subsystem - Phase]` format alongside the rest of seeding. Fixed by moving the `BuildSeedBatches` call inside the `IDatabaseInitializer` DI factory lambda, resolving `IManifestSeedPlanner` and `ILogger<Program>` from the container (built after `builder.Build()`) — this is also a more DI-compliant pattern than the prior `new ManifestSeedPlanner(...)` call site exception.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `data/sources/manifest.json` exists and lists bundled source files | Unit test | `SourceDataIntegrityTests.Manifest_AllListedFilesExist`, `SourceDataIntegrityTests.SourceFiles_AllListedInManifest` |
| 2 | ✅ | `manifest.schema.json` validates the manifest format | Unit test | `SourceDataIntegrityTests.Manifest_ConformsToSchema`, `SourceDataIntegrityTests.Manifest_EntryWithBothGithubAndUrl_FailsSchemaValidation` |
| 3 | ✅ | Seeder reads manifest and processes files in declared order | Unit test | `ManifestSeedPlannerTests.PlanSeed_ManifestListsFiles_ReturnsListedFilesInDeclaredOrder` |
| 4 | ✅ | Unlisted files appended alphabetically after listed entries | Unit test | `ManifestSeedPlannerTests.PlanSeed_UnlistedFilesPresent_AppendsThemAlphabeticallyAfterListed` |
| 5 | ✅ | Auto-create `manifest.json` in user imports folder when missing | Unit test | `ManifestSeedPlannerTests.PlanSeed_NoManifestAllowAutoCreateTrue_WritesManifestListingDiscoveredFilesAlphabetically` |
| 6 | ✅ | `Quotinator__CreateMissingManifest` config key (default `true`); when `false`, no manifest written | Unit test | `ManifestSeedPlannerTests.PlanSeed_NoManifestAllowAutoCreateFalse_ReturnsAlphabeticalOrderNoFileWritten` |
| 7 | ✅ | Warning logged when manifest is auto-created | Unit test | `ManifestSeedPlannerTests.PlanSeed_NoManifestAllowAutoCreateTrue_LogsWarning` |
| 8 | ✅ | App starts cleanly in Visual Studio; imports-dir manifest auto-created with `[Database - Init]` warning logged | T1 gate | Delete `imports/manifest.json`, start app; confirm `imports\manifest.json` recreated and `[Database - Init]` warning logged via Serilog. Confirmed 2026-06-30 — schema v4, 788 quotes seeded |
| 9 | ✅ | `docker build`/`docker run` smoke test; auto-creation works inside the container | T2 gate | `docker build -f docker/Dockerfile -t quotinator:local .`; run; confirm `/api/v1/health`, `/api/v1/version`, `/api/v1/quotes/random` return expected output and `/app/data/imports/manifest.json` is auto-created with warning logged. Confirmed 2026-06-30 |
