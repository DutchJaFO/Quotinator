---
# #140 — Auto-update bundled source files from manifest URL on startup

**Status:** Not started  
**GitHub issue:** #140  
**Depends on:** #58 fix (manifest `url` field), #62 (`AutoUpdateSources` follows same config pattern)  
**Unblocks:** retirement of `scripts/sources.json`

---

## Spec requirements

1. `Quotinator__AutoUpdateSources` config key (default `true`) — when `false`, skip all URL checks
2. On startup and reseed: for each manifest entry with a `url`, attempt `GET` with a short timeout (5 s)
3. On success: overwrite local source file; log at `Information` level
4. On failure: log a warning; continue with existing local file — never a startup failure
5. Bundled sources only — user import files have no URLs and are not affected
6. HA write-path constraint: `/app/data/sources/` is read-only under HA supervisor; downloaded files must go to `{dataDir}/sources/` on the persistent volume

---

## Write-path approach — temp directory

Downloaded files are written to a temp directory (`Path.GetTempPath()`), not to the image-bundled `data/sources/` directory (which is read-only under the HA supervisor). The temp path is passed to the seeder in place of the bundled path for that file. After seeding completes the temp files are deleted.

This works because seeding only runs when the database is empty (or on explicit reseed) — the download only needs to survive long enough to seed from. On container restart with a populated database, seeding is skipped and the temp files are never needed.

**Flow:**
1. For each manifest entry with a `url` (and `AutoUpdateSources=true`): attempt `GET` with 5 s timeout
2. On success: write to a temp file; substitute the temp path for the bundled path in the seeder's file list; log at `Information`
3. On failure: log a warning; keep the bundled path — seeder uses the existing local file
4. Seeding completes → delete all temp files

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `AutoUpdateSources=false` skips all URL checks | Unit test | To be named |
| 2 | ❌ | Successful download overwrites local file and logs at Info | Unit test | To be named |
| 3 | ❌ | Network failure logs warning and uses local file; startup succeeds | Unit test | To be named |
| 4 | ❌ | User import files are not affected | Unit test | To be named |
| 5 | ❌ | Config key present in `appsettings.json` and add-on translations | Live | App starts; config panel shows option |
