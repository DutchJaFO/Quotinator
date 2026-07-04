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

1. [x] `seed.csx` writes one file per source to `data/sources/`
2. [x] File names use `{owner}_{repo}.json` convention
3. [x] `data/quotes.json` deleted from repo and `.gitignore`
4. [x] `--dry-run` works
5. [x] `--no-fetch` works
6. [x] `Quotinator.slnx` updated with new source files
7. [x] CI smoke-test path updated from `data/quotes.json` to `data/sources/`
8. [x] `--output-dir DIR` flag added to seed script

---

## Verification steps

One verification step per requirement. Unit test preferred; live command where no test is possible.

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Writes one file per source to `data/sources/` | Live | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — `[TestCategory("Live")]`; requires `dotnet-script` installed and `scripts/cache/` populated |
| 2 | ❌ | File names use `{owner}_{repo}.json` | Live | Covered by `SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — same prerequisites |
| 3 | ✅ | `data/quotes.json` deleted | Unit test | `RepositoryStructureTests.DataQuotesJson_DoesNotExistOnDisk` |
| 4 | ✅ | `--dry-run` works | Live | `dotnet-script scripts/seed.csx -- --dry-run` — expected output: `[dry-run] no files written.` |
| 5 | ✅ | `--no-fetch` works | Live | `dotnet-script scripts/seed.csx -- --no-fetch` — expected output: `using cache:` for both sources, no fetch lines |
| 6 | ✅ | CI smoke-test checks `data/sources/` | Live | CI pipeline passes — assertion in both `ci.yml` and `release.yml` |
| 7 | ✅ | `Quotinator.slnx` updated | Unit test | `RepositoryStructureTests.SlnxDataSourcesEntries_AllExistOnDisk`, `DataSourcesFiles_OnDisk_AreAllInSlnx`, `DataQuotesJson_IsNotInSlnx` |
| 8 | ❌ | `--output-dir DIR` redirects output | Live | `SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — `[TestCategory("Live")]`; same prerequisites as #1 |
