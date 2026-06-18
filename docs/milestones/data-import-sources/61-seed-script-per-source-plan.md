# #61 — Seed script: one file per source

**Status:** Complete  
**GitHub issue:** #61

---

## Spec requirements

1. `dotnet-script scripts/seed.csx` writes one JSON file per source dataset into `data/sources/`
2. File names follow the `{owner}_{repo}.json` convention
3. Existing `data/quotes.json` is deleted from the repo
4. `--dry-run` flag previews what would be written without writing
5. `--no-fetch` flag uses the local `scripts/cache/` directory instead of downloading
6. CI smoke-test asserts `data/sources/` is present and non-empty (not `data/quotes.json`)
7. `Quotinator.slnx` updated: old `data/quotes.json` removed, new source files added
8. `--output-dir DIR` flag redirects output to a specified directory (added to enable live testing)

---

## Step status

- [x] `seed.csx` writes one file per source to `data/sources/`
- [x] File names use `{owner}_{repo}.json` convention
- [x] `data/quotes.json` deleted from repo and `.gitignore`
- [x] `--dry-run` works
- [x] `--no-fetch` works
- [x] `Quotinator.slnx` updated with new source files
- [x] CI smoke-test path updated from `data/quotes.json` to `data/sources/`
- [x] `--output-dir DIR` flag added to seed script

---

## Verification steps

One verification step per requirement. Unit test preferred; live command where no test is possible.

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Writes one file per source to `data/sources/` | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — runs script into temp dir, validates against `source-flat.schema.json`, asserts IDs exactly match baseline |
| 2 | ✅ | File names use `{owner}_{repo}.json` | Unit test | Covered by `SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — expected filenames derived from `sources.json` and asserted present |
| 3 | ✅ | `data/quotes.json` deleted | Unit test | `RepositoryStructureTests.DataQuotesJson_DoesNotExistOnDisk` |
| 4 | ✅ | `--dry-run` works | Live | `dotnet-script scripts/seed.csx -- --dry-run` — expected output: `[dry-run] no files written.` |
| 5 | ✅ | `--no-fetch` works | Live | `dotnet-script scripts/seed.csx -- --no-fetch` — expected output: `using cache:` for both sources, no fetch lines |
| 6 | ✅ | CI smoke-test checks `data/sources/` | Live | CI pipeline passes — assertion in both `ci.yml` and `release.yml` |
| 7 | ✅ | `Quotinator.slnx` updated | Unit test | `RepositoryStructureTests.SlnxDataSourcesEntries_AllExistOnDisk`, `DataSourcesFiles_OnDisk_AreAllInSlnx`, `DataQuotesJson_IsNotInSlnx` |
| 8 | ✅ | `--output-dir DIR` redirects output | Unit test | Covered by `SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — explicitly passes `--output-dir <tempdir>` and asserts files land there, not in `data/sources/` |
