# #65 — Import endpoint: preview / dry-run

**Status:** Waiting for release

**Tiers required:** T1, T2

**GitHub issue:** #65

**Depends on:** #45

---

## Spec requirements (reconciled — see #45's plan doc Scope changes)

The original spec described a `preview=true` query parameter on the import endpoint. #45's implementation instead ships preview as its own dedicated endpoint sharing #45's request/response contract:

1. `POST /api/v1/quotes/import/preview` accepts the identical `multipart/form-data` payload (`file` + optional `settings`) as `POST /api/v1/quotes/import`
2. Runs the full pipeline — file parsing/conversion, duplicate detection, all 5 conflict-resolution policies, row-level error tolerance
3. Returns the identical `ImportResultResponse` shape, with `preview: true` and `batchId: null`
4. Rolls back every write — no `ImportBatch` row, no quote/source/character/person rows, no `System_ImportConflicts` rows

There is no `conflicts.sameId`/`conflicts.sameText` split — #45's matching is purely Id-based (#64's engine), so a text-based `sameText` category never applies. `conflicts[]` mirrors a `System_ImportConflicts` row directly instead.

---

## What currently exists

`GET /api/v1/admin/database/seed/preview` — returns a list of source files that would be scanned at startup with estimated quote counts and cross-file duplicate detection. This satisfies a different need (startup source preview) and is **not** this issue's preview feature; the two coexist without conflict.

---

## Steps

### 1. Startup source preview
**Status:** ✅ Done (pre-existing) — `GET /api/v1/admin/database/seed/preview`, unaffected by this issue.

### 2. Live import preview endpoint
**Status:** ✅ Done — implemented as part of #45. `POST /api/v1/quotes/import/preview` shares #45's `SqliteQuoteImportService.ImportAsync(..., preview: true, ...)` call, which rolls back the shared `SqliteUnitOfWork` transaction at the end regardless of outcome. See `45-import-endpoint-plan.md` for the full implementation detail — this issue has no code of its own beyond the route registration shared with #45.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `POST /api/v1/quotes/import/preview` runs the full pipeline without committing | Unit test | `QuoteImportServiceTests.ImportAsync_Preview_LeavesZeroTrace`, `ImportAsync_PreviewWithConflict_NoConflictRowsPersisted` |
| 2 | ✅ | Response includes `summary` (`total`/`imported`/`updated`/`skipped`/`errors`) and `conflicts[]` (Id-based, mirroring `System_ImportConflicts`) — no `sameId`/`sameText` split | Unit test | `QuoteImportServiceTests.ImportAsync_PreviewWithConflict_NoConflictRowsPersisted` (asserts `Conflicts.Count`); `ImportEndpointTests.ImportPreview_CorrectKeyAndValidFile_Returns200WithPreviewTrue` |
| 3 | N/A | `sameId` conflicts include `fieldDiffs` — superseded; no `sameId`/`sameText` split exists (see Scope changes above) | N/A | Superseded by #45's Id-only matching design |
| 4 | N/A | `sameText` conflicts have no `fieldDiffs` — superseded, `sameText` never applies | N/A | Superseded |
| 5 | ✅ | Active conflict policy (#64) applied during preview; policy-skipped records counted in `skipped` | Unit test | `QuoteImportServiceTests.ImportAsync_Skip_KeepsExistingRowUnchanged`, `ImportAsync_Review_BehavesLikeSkip` (both run with `preview` covered by the same code path as `ImportAsync_PreviewWithConflict_NoConflictRowsPersisted`) |
| 6 | ✅ | No `ImportBatch` row created for a preview run | Unit test | `QuoteImportServiceTests.ImportAsync_Preview_LeavesZeroTrace` (asserts `ImportBatches` count is 0 and `BatchId` is null) |
| 7 | ✅ | T1 — app starts in VS without error; `/import/preview` usable | Live | VS run: `POST /api/v1/quotes/import/preview` (×2, `quotinator-curated.json`) returned `200` in 26ms/6ms. Confirmed 2026-07-05 — see `45-import-endpoint-plan.md` for the full T1 log |
| 8 | ✅ | T2 — Docker smoke test | Live | `POST /api/v1/quotes/import/preview` against the built image (CSV file, `converter: "csv"`, `duplicateResolution.default: "merge-theirs"`) returned `200` with `preview: true`, correct summary, and correctly kebab-cased `conflictPolicy: "merge-theirs"`. Confirmed 2026-07-05 — see `45-import-endpoint-plan.md` for the full T2 log |

**Full solution `dotnet test --configuration Release`: 814 tests, 0 warnings, 0 errors.**
