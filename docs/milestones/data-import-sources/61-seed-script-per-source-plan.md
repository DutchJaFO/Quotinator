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

---

## Step status

- [x] `seed.csx` writes one file per source to `data/sources/`
- [x] File names use `{owner}_{repo}.json` convention
- [x] `data/quotes.json` deleted from repo and `.gitignore`
- [x] `--dry-run` works
- [x] `--no-fetch` works
- [x] `Quotinator.slnx` updated with new source files
- [x] CI smoke-test path updated from `data/quotes.json` to `data/sources/`

---

## Verification steps

One verification step per requirement. Unit test preferred; live command where no test is possible.

| # | Requirement | Method | Verification | Status |
|---|-------------|--------|--------------|--------|
| 1 | Writes one file per source to `data/sources/` | Live | `dotnet-script scripts/seed.csx -- --no-fetch` — expected output: `wrote: 99 quotes` and `wrote: 732 quotes`, files present in `data/sources/` | ✅ |
| 2 | File names use `{owner}_{repo}.json` | Live | Confirmed by req 1 output: `vilaboim_movie-quotes.json` and `NikhilNamal17_popular-movie-quotes.json` | ✅ |
| 3 | `data/quotes.json` deleted | Unit test | `RepositoryStructureTests.DataQuotesJson_DoesNotExistOnDisk` | ✅ |
| 4 | `--dry-run` works | Live | `dotnet-script scripts/seed.csx -- --dry-run` — expected output: `[dry-run] no files written.` | ✅ |
| 5 | `--no-fetch` works | Live | `dotnet-script scripts/seed.csx -- --no-fetch` — expected output: `using cache:` for both sources, no fetch lines | ✅ |
| 6 | CI smoke-test checks `data/sources/` | Live | CI pipeline passes — assertion in both `ci.yml` and `release.yml` | ✅ |
| 7 | `Quotinator.slnx` updated | Unit test | `RepositoryStructureTests.SlnxDataSourcesEntries_AllExistOnDisk`, `DataSourcesFiles_OnDisk_AreAllInSlnx`, `DataQuotesJson_IsNotInSlnx` | ✅ |
