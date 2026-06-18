# #63 — Import manifest

**Status:** Partially done  
**GitHub issue:** #63

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
- [ ] Unlisted files are **appended alphabetically** after listed ones — currently unlisted files are skipped
- [ ] User imports folder: auto-create `manifest.json` when missing
- [ ] `Quotinator__CreateMissingManifest` config key
- [ ] Warning log on auto-creation

---

## Remaining work

### Unlisted-file handling

In `DatabaseInitializer` (or the manifest reader): after processing listed files, scan the source directory for `.json` files not in the manifest and append them sorted alphabetically.

### Auto-manifest for imports

When scanning the imports directory and no `manifest.json` is found:
1. If `Quotinator__CreateMissingManifest` is `true` (default): write a `manifest.json` listing all discovered files alphabetically, log a warning
2. If `false`: process files alphabetically with no manifest written

### Config key

Add `Quotinator__CreateMissingManifest` to `appsettings.json` defaulting to `true`.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `data/sources/manifest.json` exists and lists bundled source files | Unit test | No test verifies manifest content |
| 2 | ❌ | `manifest.schema.json` validates the manifest format | Unit test | No test verifies this specifically |
| 3 | ❌ | Seeder reads manifest and processes files in declared order | Unit test | No test exists |
| 4 | ❌ | Unlisted files appended alphabetically after listed entries | Unit test | Not implemented |
| 5 | ❌ | Auto-create `manifest.json` in user imports folder when missing | Unit test | Not implemented |
| 6 | ❌ | `Quotinator__CreateMissingManifest` config key (default `true`); when `false`, no manifest written | Unit test | Not implemented |
| 7 | ❌ | Warning logged when manifest is auto-created | Unit test | Not implemented |
