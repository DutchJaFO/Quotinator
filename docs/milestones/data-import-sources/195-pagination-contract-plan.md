# #195 — Standard pagination contract: PagedItems&lt;T&gt;, shared parsing and not-found helpers

**Status:** Waiting for release
**GitHub issue:** #195
**Tiers required:** T1, T2
**Depends on:** #194 (numeric params published as string) — done

---

## Spec requirements (from the GitHub issue, corrected during planning review 2026-07-18)

1. A shared paginated-result type replacing the separate hand-written paginated types
   (`Quotinator.Core.Models.PagedResult<T>`, `Quotinator.Data.Models.SystemAuditPageResult`,
   `Quotinator.Core.Models.ImportActionPageResponse`, and — found during implementation, not planning
   — `Quotinator.Data.Models.SystemImportActionPageResult`, one layer below `IImportActionService`)
   **where the dependency graph allows it**. The
   shared type lives in `Quotinator.Data.Models` as `PagedItems<T>` — not `Quotinator.Api`, as
   originally proposed — because it's needed at the repository level too (#193's
   `IListableRepository<T>.GetPageAsync`), and this is a broadly reusable shape worth maximising reuse
   of. `Quotinator.Core.Models.PagedResult<T>` stays as-is: `Quotinator.Core` and `Quotinator.Data`
   have no dependency on each other (`CLAUDE.md`), so `IQuoteService.GetAll`'s Core-layer interface —
   which the v1 in-memory service also implements — cannot reference a Data-layer type. `PagedItems<T>`
   is **not** `sealed`, so a future consumer of `Quotinator.Data` can extend it rather than being forced
   into a parallel type. `PageSize` reports the **effective** page size everywhere it's constructed.
2. Shared page/pageSize parsing helper implementing #183's contract, with a distinct `detail` for
   page-out-of-range.
3. The 500 maximum and 20 default as single shared constants in `Quotinator.Constants`.
4. All three existing endpoints refactored onto the helper, accepting the behaviour changes — including
   `/admin/audit`'s and `/import/actions`' default `pageSize` changing from 50 to 20 (not previously
   listed among the accepted changes; found during planning verification).
5. `/admin/audit` and `/import/actions` registered in `NumericParameterSchemaTransformer
   .NumericParamsByPath` once converted to `string?` binding — not previously an explicit requirement;
   without it their published OpenAPI type regresses from `integer|string` to bare `string`, exactly
   what #194 exists to prevent (found during planning verification, referencing #194's own Notes).
6. Shared "not found" result helper extracted from `QuoteEndpoints`/`ConversationEndpoints`.
7. `README.md`, `addon/DOCS.md`, and `[Description]` attributes updated.

---

## Background — why this issue exists

Sub-issue of #183. `/quotes`, `/admin/audit`, and `/import/actions` each paginate differently — not
three copies of one clamp but three contracts (see #183 for the verified matrix). #183 settles the
single contract; this issue implements it and refactors all three onto it.

Depends on #194 (done): this issue converts `/admin/audit` and `/import/actions` to `string?` binding,
which without #194's transformer fix would regress their published schema from `integer|string` to
bare `string`.

**Verified before starting** (per this project's standing rule — #183, #194, and #193 all had
factual errors in their issue/plan text before work started): `/quotes`'s `page`/`pageSize` are
`string?`-bound, max 100, defaults already centralised in `QueryParamDefaults`. `/admin/audit`
(`AdminEndpoints.cs:75-96`) and `/import/actions` (`ImportEndpoints.cs:114-128`) both bind
`int page = 1, int pageSize = 50` (plain, non-nullable) with an identical `>200→200` clamp.
`QuoteEndpoints.GetById`/`ConversationEndpoints.GetById` are genuine identical-shape duplicates. The
issue's original text said "Depends on sub-issue B" instead of naming #194 by number — corrected.

**Second verification gap found while writing red tests, not during planning**: the "malformed
input produces a bare 400" framing (both in the original issue body and my own planning verification)
is inaccurate. `src/Quotinator.Api/Middleware/BadRequestExceptionHandler.cs` is a pre-existing global
`IExceptionHandler` that already catches every `BadHttpRequestException` from a parameter-binding
failure and maps it to 422 — confirmed live: `?pageSize=abc` on `/admin/audit` already returns 422
today, before any of this issue's changes. The real, still-genuine gap is that it falls through to
this handler's **generic** `ErrorNumericParameterInvalid` message ("Numeric parameters (yearFrom,
yearTo, year, decade, page, pageSize, n) must be whole numbers") rather than a **specific** detail
naming `pageSize`. The originally-written test `Audit_PageSizeMalformed_Returns422NotBare400` passed
immediately (false green) — replaced with
`Audit_PageSizeMalformed_Returns422WithSpecificDetailNotGenericFallback`, which asserts the detail
does *not* contain the generic fallback's wording, and genuinely starts red. This same correction
applies to `/import/actions` (same global handler), though no test asserted the wrong premise there.

**Field-name conflict found and resolved with the developer**: quotes' response already uses
`totalCount`; audit's and import/actions' both use `totalMatching` for the identical concept. A single
shared type can only have one name, so unifying necessarily changes 2 of 3 endpoints' JSON regardless
of which wins. Decided: `TotalCount` is canonical (matches quotes' existing field and #193's
`GetPageAsync` tuple naming). `TotalMatching` is not added — nothing today needs a value distinct from
`TotalCount`, and the type isn't closed off from gaining an additional field later if a real need for
both simultaneously ever arises.

---

## Steps

### 1. `PagedItems<T>` — new shared type in `Quotinator.Data.Models`

**Status:** ✅ Done.

New file `src/Quotinator.Data/Models/PagedItems.cs`:

```csharp
public record PagedItems<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

Not `sealed` — a consumer of `Quotinator.Data` may need to extend it rather than being forced into a
parallel type.

### 2. Retrofit #193's `IListableRepository<T>.GetPageAsync`

**Status:** ✅ Done.

All 13 `GetPageAsync` tests updated for the new `PagedItems<T>` return shape and confirmed green,
including a new assertion on `GetPageAsync_PageSizeZero_ReturnsEveryRowAsOnePage` proving `PageSize`
reports the effective count (5), not the literal `0` requested.

Change the return type from `Task<(IReadOnlyList<T> Items, int TotalCount)>` (shipped in #193) to
`Task<PagedItems<T>>`. Compute the **effective** `pageSize` before constructing the result
(`pageSize == 0 ? items.Count : pageSize`), so the "effective size" contract holds everywhere
`PagedItems<T>` is built, not only at the three HTTP endpoints. Update the 13 `GetPageAsync` tests in
`SqliteRepositoryTests.cs` for the new return shape. This is the one place #195 touches already-shipped
#193 code — the full `Quotinator.Data.Tests` suite must stay green afterward.

### 3. Retire `SystemAuditPageResult` and `ImportActionPageResponse`

**Status:** ✅ Done — plus one additional type found and retired that wasn't in the original plan.

- `ISystemAuditReader.GetPagedAsync` returns `Task<PagedItems<SystemAuditEntry>>` directly. Deleted
  `SystemAuditPageResult.cs`.
- **`ISystemImportActionReader.GetPagedAsync`** (`Quotinator.Data.Repositories`, one layer below
  `IImportActionService`) was also returning its own separate, structurally-identical
  `SystemImportActionPageResult` — a fourth redundant type not caught during planning verification,
  found while implementing this step. Retrofitted the same way: returns
  `Task<PagedItems<SystemImportAction>>` directly. Deleted `SystemImportActionPageResult.cs`.
- `IImportActionService.GetPagedAsync` returns `Task<PagedItems<ImportActionSummaryResponse>>`.
  Deleted `ImportActionPageResponse.cs`.
- Updated all consumers to compile: `NoOpSystemAuditReader`, `AdminAuditEndpointTests.cs`,
  `FakeImportActionService`, `ImportActionEndpointsTests.cs`, `SqliteImportActionServiceTests.cs`.
- **Side effect confirmed live, not just theorised**: `ImportEndpoints.cs`'s `/actions` handler passes
  the service result straight through to `Results.Ok` with no wrapper of its own (unlike
  `AdminEndpoints.cs`, which still builds its own anonymous object) — so the moment
  `IImportActionService`'s return type changed, `/import/actions`' wire JSON switched from
  `totalMatching` to `totalCount` immediately, ahead of Step 7's endpoint refactor. Caught by
  `GetActions_ReturnsPageShape` failing (`KeyNotFoundException` on `"totalMatching"`); fixed the
  assertion to `"totalCount"`. `/admin/audit` has not changed yet — its anonymous wrapper still
  produces `totalMatching` until Step 7.
- Full suite confirmed green afterward (9/9 projects, all passed).

**Live bug found by T2 (step 10), not by any unit test — fixed back here since it belongs to this
step's own retrofit**: this step changed `SystemAuditReader`/`SystemImportActionReader`'s *return
type* to `PagedItems<T>` but left their underlying SQL untouched — both still passed `pageSize`
straight into `LIMIT @pageSize` with no `pageSize == 0` handling, the exact `LIMIT 0`-returns-zero-rows
bug this whole issue exists to prevent. Confirmed live: `GET /admin/audit?pageSize=0` returned
`"totalCount":17` but zero items in the array. No unit test caught it because `AdminAuditEndpointTests`
uses a stub reader that echoes back whatever it's given — a stub cannot exercise a real `LIMIT` clause
bug. Fixed both readers with the same `pageSize == 0 ? -1 : pageSize` / effective-`PageSize`-reporting
pattern as `SqliteQuoteService`/#193, and added real-SQLite regression tests neither reader had before:
`SystemAuditReaderTests.cs` (new) and a `GetPagedAsync` region in the existing
`SystemImportActionWriterReaderTests.cs`. Re-verified live afterward — see step 10.

### 4. Constants

**Status:** ✅ Done.

`QueryParamDefaults.PageSizeMax = 500`. The 500 maximum and 20 default are single shared values used
by every paginated endpoint — no per-endpoint range.

### 5. Shared parsing helper

**Status:** ✅ Done.

`src/Quotinator.Api/Endpoints/Shared/PaginationParsing.cs`. All 10 unit tests (`PaginationParsingTests`)
green, including the boundary (`pageSize=500` succeeds), `pageSize=0` (succeeds with 0, effective-size
reporting is the caller's job post-query), and the beyond-last-page distinct-detail cases.

Implement #183's contract exactly: `string?` binding + `int.TryParse`, and a 422 via `Results.Problem`
carrying a `detail` for each of malformed input, `page < 1`, `pageSize < 0`, `pageSize > 500`, and page
beyond the last page. `pageSize = 0` resolves to "all available items as one page" and deliberately
bypasses the 500 ceiling.

The page-out-of-range `detail` must be **distinct** from the others. This check can only run once the
total is known, so unlike the others it happens after the query, not during parameter parsing.

### 6. Register the OpenAPI transformer paths

**Status:** ✅ Done.

Added `api/v1/admin/audit` and `api/v1/import/actions` to `NumericParameterSchemaTransformer
.NumericParamsByPath`, each with `page`/`pageSize` defaulting to 1/20. Found and fixed a test that
would otherwise have silently gone wrong: `Page_OnUnrelatedPath_NotPatched` asserted `page` on
`api/v1/admin/audit` was *not* patched — retargeted to `api/v1/admin/backup` (a genuinely unrelated
path) and 8 new tests added confirming both paths are now patched with the correct defaults. 31/31
`NumericParameterSchemaTransformerTests` green.

Add `api/v1/admin/audit` and `api/v1/import/actions` to `NumericParameterSchemaTransformer
.NumericParamsByPath`, each with `page`/`pageSize` defaulting via `QueryParamDefaults`. Without this,
converting these two endpoints to `string?` binding regresses their published type from
`integer|string` to bare `string`.

### 7. Refactor the three endpoints

**Status:** ✅ Done — all three.

`QuoteEndpoints.GetAll` now uses `PaginationParsing.TryParse`/`ValidatePageBeyondLast`. Max 100 → 500
via the existing `QueryParamDefaults.PageSizeMax` (no `[Description]` text change yet — step 9).

**Found and fixed a real bug while wiring this up, not caught during planning**: `SqliteQuoteService
.GetAll` passed `pageSize` straight into `LIMIT @pageSize` with no `pageSize == 0` handling — a literal
`LIMIT 0` would have returned zero rows, the exact failure mode #193's `GetPageAsync`/this issue's own
contract both explicitly warn against. Fixed with the same `pageSize == 0 ? -1 : pageSize` /
effective-`PageSize`-reporting pattern #193 already uses. `FakeQuoteService.GetAll` (test double) had
the identical bug via LINQ `.Take(pageSize)` — `Take(0)` means "take nothing", not "take everything" —
fixed the same way. Neither had any test coverage before this issue: added
`SqliteQuoteServiceTests.cs` (new, `Quotinator.Engine.Tests`, real SQLite, 3 tests) for the real
service; `QuoteEndpointsTests.cs`'s existing `GetAll_PageSizeZero_Returns422` renamed to
`_Succeeds` and now exercises the fake's fixed path. Full `QuoteEndpointsTests` suite (72 tests) green.

`AdminEndpoints`'s `/audit` and `ImportEndpoints`'s `/actions` both converted `page`/`pageSize` to
`string?`, added `IApiLocalizer`, and now call `PaginationParsing.TryParse`/`ValidatePageBeyondLast`.
`/audit` drops its anonymous-object wrapper and returns the `PagedItems<SystemAuditEntry>` from step 3
directly; `/actions` already just passed its service's return value straight through, so no wrapper to
drop there. Both `.WithDescription(...)` texts' "Maximum pageSize is 200" corrected to 500 while
editing these lines (full `[Description]` sweep is still step 9). Full `AdminAuditEndpointTests` (12)
and `ImportActionEndpointsTests` (26) suites green; full solution build 0 warnings/errors.

- `QuoteEndpoints.GetAll`: swap inline parsing for the shared helper. **No response-type change** —
  `IQuoteService.GetAll` still returns `Core.PagedResult<QuoteResponse>` directly (its shape already
  matches `PagedItems<T>`'s field names, so the JSON is unaffected). Max 100 → 500.
- `AdminEndpoints`'s `/audit`: `string?` binding, shared helper, return `PagedItems<SystemAuditEntry>`
  directly via `Results.Ok` — drops the current anonymous-object wrapper. Default 50 → 20, max 200 → 500.
- `ImportEndpoints`'s `/actions`: same shape, returns `PagedItems<ImportActionSummaryResponse>`
  directly. Same 50→20, 200→500 changes.

### 8. Shared not-found helper

**Status:** ✅ Done (implemented ahead of doc order — no dependency on steps 6/7).

`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`. `QuoteEndpoints.GetById`/
`ConversationEndpoints.GetById` both now call `NotFoundResult.OkOrNotFound`. All 12 relevant tests
green: the 2 new `NotFoundResultTests` plus the existing `GetById_*` regression suite for both
entities, unaffected.

Extract the `GetById` 404-or-200 ternary from `QuoteEndpoints.GetById` and
`ConversationEndpoints.GetById` — genuine duplicates of each other — and reuse it in both, so
#184–#189's six `GetById` endpoints call it rather than re-writing it.

### 9. Documentation

**Status:** ✅ Done.

`[Description]` attributes updated/added: `/quotes`' `pageSize` (1–100 → 0–500, notes the pageSize=0
meaning); `/admin/audit` and `/import/actions` gained `page`/`pageSize` `[Description]`/`[DefaultValue]`
attributes for the first time (previously had none at all). `.WithDescription(...)` "Maximum pageSize
is 200" corrected to 500 on both (done inline during step 7). `README.md`/`addon/DOCS.md` confirmed to
cite no numeric pagination limits — no change needed.

### 10. Verify

**Status:** T1 and T2 both done.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → after the step-3 fix, 9/9 projects, 1311/1311 passed, 0 warnings, 0 errors.

T2 confirmed via Docker, full matrix on all three endpoints: malformed → 422 (specific detail, not the
generic `BadRequestExceptionHandler` fallback), `pageSize=999` → 422, `pageSize=500` → 200,
`pageSize=0` → every row as one page with `PageSize` reporting the effective count (`/quotes`: 796
items; `/admin/audit`: 17; `/import/actions`: 1444), `page=0` → 422, page-beyond-last → 422 with a
distinct detail, omitted `pageSize` → 20 (not audit/import's old 50). `GET /openapi/v1.json` confirmed
`page`/`pageSize` publish `["null","integer"]` with the correct defaults on all three paths.
`totalMatching` confirmed absent from both audit and import responses; `totalCount` present on both.

**T2 caught a live bug unit tests missed** (documented fully in step 3): `pageSize=0` on `/admin/audit`
initially returned zero items despite `totalCount:17` — the exact regression this issue exists to
prevent, in the one place a stub-backed unit test structurally cannot catch it. Fixed, covered by new
real-SQLite tests, and re-verified live before this row was marked done.

Follow-on work after this row was first marked done: endpoint-level test coverage was audited across
all three endpoints and found uneven (e.g. no endpoint anywhere exercised page-beyond-last through an
actual HTTP call, only through the shared parser's own unit tests) — 14 tests added to bring
`QuoteEndpointsTests.cs`, `AdminAuditEndpointTests.cs`, and `ImportActionEndpointsTests.cs` to parity.
The `GET /openapi/v1.json` type check was also automated: `OpenApiSpecEndpointTests.cs` fetches the
real spec through `WebApplicationFactory` and asserts the type via `JsonDocument`, replacing the
`curl | grep` check originally added to CLAUDE.md's smoke-test section (grepping a pretty-printed,
multi-line JSON body proved fragile — the first version of that command never matched anything).
Verified the new test is a genuine check, not a false positive, by temporarily removing
`NumericParameterSchemaTransformer`'s DI registration and confirming all six cases failed red before
reverting. A "Standard pagination contract" section was added to CLAUDE.md documenting the required
eight-case test matrix so a future paginated endpoint starts with full coverage instead of needing the
same audit repeated.

**T1 confirmed** by the developer in Visual Studio: clean startup (schema up to date, both bundled
sources refreshed successfully), and the exact pagination cases exercised live matched the contract —
`pageSize=0` returns 200 with every row on `/quotes`, `/admin/audit`, and `/import/actions`;
`pageSize=999999999999` and an empty `pageSize=` both return 422; a normal `pageSize=0` request against
`/import/actions` (with `status`/`entityType`/`page` also set) returned 200 with items.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Malformed `page`/`pageSize` returns 422 with a `detail` | Unit test | `PageParsing_Malformed_Returns422WithDetail` |
| 2 | ✅ | `page < 1` returns 422 | Unit test | `PageParsing_PageBelowOne_Returns422` |
| 3 | ✅ | `pageSize < 0` returns 422 | Unit test | `PageParsing_PageSizeNegative_Returns422` |
| 4 | ✅ | `pageSize > 500` returns 422 | Unit test | `PageParsing_PageSizeAbove500_Returns422` |
| 5 | ✅ | `pageSize = 500` succeeds (boundary) | Unit test | `PageParsing_PageSizeExactly500_Succeeds` |
| 6 | ✅ | `pageSize = 0` returns all items and reports the effective page size | Unit test | `PageParsing_PageSizeZero_SucceedsWithZero` (parser) + `SqliteQuoteServiceTests`/`SqliteRepositoryTests` (effective size at the data layer) |
| 7 | ✅ | A page beyond the last returns 422 with a **distinct** detail | Unit test | `ValidatePageBeyondLast_PageBeyondLastPage_Returns422WithDistinctDetail` |
| 8 | ✅ | Omitted `pageSize` uses the standard default of 20 | Unit test | `PageParsing_Omitted_UsesStandardDefaultOf20` |
| 9 | ✅ | `/admin/audit?pageSize=abc` returns 422 with a specific detail, not the generic `BadRequestExceptionHandler` fallback message | Unit test | `Audit_PageSizeMalformed_Returns422WithSpecificDetailNotGenericFallback` |
| 10 | ✅ | `/admin/audit?page=0` returns 422, not silently page 1 | Unit test | `Audit_PageZero_Returns422NotSilentlyPageOne` |
| 11 | ✅ | `/import/actions?pageSize=999` returns 422, not a silent clamp | Unit test | `ImportActions_PageSizeAbove500_Returns422NotSilentClamp` |
| 12 | ✅ | `/quotes?pageSize=150` now succeeds | Unit test | `Quotes_PageSize150_NowSucceeds` |
| 13 | ✅ | Omitted `pageSize` on `/admin/audit`/`/import/actions` now returns 20 items, not 50 | Unit test | `Audit_PageSizeOmitted_DefaultsTo20NotFifty`, `ImportActions_PageSizeOmitted_DefaultsTo20NotFifty` |
| 14 | ✅ | The not-found helper returns 404 for a missing entity | Unit test | `OkOrNotFound_EntityNull_ReturnsProblem404` |
| 15 | ✅ | The not-found helper returns 200 for a present entity | Unit test | `OkOrNotFound_EntityPresent_ReturnsOk200` |
| 16 | ✅ | Quotes' response JSON shape is unchanged; audit's/import's change only `totalMatching` → `totalCount` | Unit test | Existing `GetAll_*` (Quotes) regression; `Audit_*`/`GetActions_*` updated to assert `totalCount`, not `totalMatching` |
| 17 | ✅ | The two `GetById` not-found paths still behave identically | Unit test | Existing `GetById_*` (Quotes), `GetById_*` (Conversations) — regression |
| 18 | ✅ | #193's `GetPageAsync` retrofit (tuple → `PagedItems<T>`) has no behavioural regression | Unit test | `Quotinator.Data.Tests.Repositories.SqliteRepositoryTests` — all 13 `GetPageAsync` tests updated and green |
| 19 | ✅ | `page`/`pageSize` on `/admin/audit` and `/import/actions` publish as `integer` in the OpenAPI spec | Unit/Live | Unit: `NumericParameterSchemaTransformerTests` (8 new tests); Live (T2): confirmed via `GET /openapi/v1.json` on the built image |
| 20 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 9/9 projects, 1331/1331 passed, 0 warnings, 0 errors (1311 at initial verify; +14 endpoint-coverage-parity tests, +6 `OpenApiSpecEndpointTests`) |
| 21 | ✅ | T1 — app starts in Visual Studio; the changed contract behaves as specified | Live (T1) | Developer confirmed in Visual Studio: clean startup, `pageSize=0`/`pageSize=999999999999`/empty `pageSize=` all behave per contract on all three endpoints |
| 22 | ✅ | T2 — the live contract holds on all three endpoints | Live (T2) | Full matrix confirmed on `/quotes`, `/admin/audit`, `/import/actions` — see step 10. Found and fixed a live `pageSize=0` bug on `/admin/audit`/`/import/actions` in the process (step 3) |

---

## Notes

T1 and T2 are both required — this changes live HTTP status codes and response detail text on three
endpoints. Unlike #194's year-param transformer bug (a genuine bare-400, only provable live), this
issue's malformed-input case already returns 422 via the pre-existing `BadRequestExceptionHandler`
safety net — so requirement 9's real gap (a generic fallback message instead of the specific one) is
fully provable by a unit test, which `Audit_PageSizeMalformed_Returns422WithSpecificDetailNotGenericFallback`
does. T1/T2 remain required for the other behavioural changes (the new 500 ceiling, the 50→20 default
change, page-beyond-last, and the OpenAPI type/registration fix), which do depend on the live contract.

Once this lands, `CLAUDE.md`'s T2 smoke-test list should gain the pagination-contract matrix from
verification row 22 — that list is living and only grows.

`Quotinator.Core.Models.PagedResult<T>` is intentionally left unchanged by this issue (see Spec
requirement 1) — it is not a gap, but a hard architectural boundary (`Quotinator.Core`/`Quotinator.Data`
have no dependency on each other, and the v1 in-memory `IQuoteService` implementation depends on Core
staying Data-free).
