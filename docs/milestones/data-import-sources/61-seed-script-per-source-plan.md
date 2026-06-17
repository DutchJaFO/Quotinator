# #61 — Seed script: one file per source

**Status:** Partially done  
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
- [ ] CI smoke-test path updated from `data/quotes.json` to `data/sources/` — **verify `.github/workflows/` CI file**

---

## Remaining work

Check the CI workflow file for any assertion that still references `data/quotes.json`:

```bash
grep -r "quotes.json" .github/
```

Update to assert `data/sources/` exists and contains at least one `.json` file.
