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

## Key decision before implementation

**HA write path:** bundled sources in the image (`/app/data/sources/`) are read-only under the HA supervisor. Downloaded files must target a writable location. Options:

- Write to `{dataDir}/sources/` and scan both image-bundled and persistent-volume sources at startup
- Write to a temp location and swap atomically

Resolve this before writing any code — it affects the seeder's source discovery logic.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `AutoUpdateSources=false` skips all URL checks | Unit test | To be named |
| 2 | ❌ | Successful download overwrites local file and logs at Info | Unit test | To be named |
| 3 | ❌ | Network failure logs warning and uses local file; startup succeeds | Unit test | To be named |
| 4 | ❌ | User import files are not affected | Unit test | To be named |
| 5 | ❌ | Config key present in `appsettings.json` and add-on translations | Live | App starts; config panel shows option |
