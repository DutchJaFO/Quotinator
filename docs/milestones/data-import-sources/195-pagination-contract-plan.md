# #195 — Standard pagination contract: PageResponse&lt;T&gt;, shared parsing and not-found helpers

**Status:** Planning
**GitHub issue:** #195
**Tiers required:** T1, T2
**Depends on:** #194 (numeric params published as string)

---

## Spec requirements (from the GitHub issue)

1. `PageResponse<T>` replacing the hand-written paginated DTOs; `PageSize` reports the effective size.
2. Shared page/pageSize parsing helper implementing #183's contract, with a distinct `detail` for
   page-out-of-range.
3. The 500 maximum and 20 default as single shared constants in `Quotinator.Constants`.
4. All three existing endpoints refactored onto the helper, accepting the behaviour changes.
5. Shared "not found" result helper extracted from `QuoteEndpoints`/`ConversationEndpoints`.
6. `README.md`, `addon/DOCS.md`, and `[Description]` attributes updated.

---

## Background — why this issue exists

Sub-issue of #183. `/quotes`, `/admin/audit`, and `/import/actions` each paginate differently — not
three copies of one clamp but three contracts (see #183 for the verified matrix). #183 settles the
single contract; this issue implements it and refactors all three onto it.

Depends on #194: this issue converts `/admin/audit` and `/import/actions` to `string?` binding, which
without #194's transformer fix would regress their published schema from `integer|string` to bare
`string`.

---

## Steps

### 1. Red tests

**Status:** Not started.

Write the failing tests before any change. Three of them assert behaviour that exists today and must
change, so they are red for the opposite reason to the rest — confirm each fails for the expected
reason, not incidentally: `Audit_PageSizeMalformed_Returns422NotBare400` (currently a bare 400),
`ImportActions_PageSizeAbove500_Returns422NotSilentClamp` (currently clamps to 200), and
`Quotes_PageSize150_NowSucceeds` (currently 422 under the old 100 max).

### 2. Constants

**Status:** Not started.

The 500 maximum and 20 default are single shared values used by every paginated endpoint — there is no
per-endpoint range. Per this project's string/constant centralisation policy they belong in
`Quotinator.Constants`, not duplicated per endpoint.

### 3. Shared parsing helper

**Status:** Not started.

Implement #183's contract exactly: `string?` binding + `int.TryParse`, and a 422 via `Results.Problem`
carrying a `detail` for each of malformed input, `page < 1`, `pageSize < 0`, `pageSize > 500`, and page
beyond the last page. `pageSize = 0` resolves to "all available items as one page" and deliberately
bypasses the 500 ceiling.

The page-out-of-range `detail` must be **distinct** from the others — a caller must be able to tell
"there is no page 10" from "pageSize is too large". Note this check can only run once the total is
known, so unlike the others it happens after the query, not during parameter parsing.

### 4. PageResponse&lt;T&gt; and the endpoint refactor

**Status:** Not started.

Replace the separate hand-written paginated DTOs with a generic `PageResponse<T>` and move all three
endpoints onto it and onto the helper, confirming each response's JSON shape is unchanged.

`PageSize` reports the **effective** page size, so a `pageSize=0` request reports the number actually
returned rather than echoing `0` — matching the contract's framing that `pageSize=0` is functionally
equivalent to `pageSize` = available items.

### 5. Shared not-found helper

**Status:** Not started.

Extract the `GetById` 404-or-200 ternary from `QuoteEndpoints.GetById` and
`ConversationEndpoints.GetById` — these two are genuine duplicates of each other — and reuse it in
both, so #184–#189's six `GetById` endpoints call it rather than re-writing it.

### 6. Documentation

**Status:** Not started.

`README.md`, `addon/DOCS.md`, and the `[Description]` attributes feeding OpenAPI/Scalar, per this
project's "Keeping API documentation in sync" rule. `/quotes`' `pageSize` currently documents "1–100",
which the new contract makes wrong.

### 7. Verify

**Status:** Not started.

Full suite green, 0 warnings. T1/T2 confirm the live contract.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Malformed `page`/`pageSize` returns 422 with a `detail` | Unit test | `Quotinator.Api.Tests.PageParsing_Malformed_Returns422WithDetail` — starts red |
| 2 | ❌ | `page < 1` returns 422 | Unit test | `PageParsing_PageBelowOne_Returns422` — starts red |
| 3 | ❌ | `pageSize < 0` returns 422 | Unit test | `PageParsing_PageSizeNegative_Returns422` — starts red |
| 4 | ❌ | `pageSize > 500` returns 422 | Unit test | `PageParsing_PageSizeAbove500_Returns422` — starts red |
| 5 | ❌ | `pageSize = 500` succeeds (boundary) | Unit test | `PageParsing_PageSizeExactly500_Succeeds` — starts red |
| 6 | ❌ | `pageSize = 0` returns all items and reports the effective page size | Unit test | `PageParsing_PageSizeZero_ReturnsAllItemsAndReportsEffectivePageSize` — starts red |
| 7 | ❌ | A page beyond the last returns 422 with a **distinct** detail | Unit test | `PageParsing_PageBeyondLastPage_Returns422WithDistinctDetail` — starts red |
| 8 | ❌ | Omitted `pageSize` uses the standard default of 20 | Unit test | `PageParsing_Omitted_UsesStandardDefaultOf20` — starts red |
| 9 | ❌ | `/admin/audit?pageSize=abc` returns 422, not a bare 400 | Unit test | `Audit_PageSizeMalformed_Returns422NotBare400` — starts red (currently a bare 400) |
| 10 | ❌ | `/admin/audit?page=0` returns 422, not silently page 1 | Unit test | `Audit_PageZero_Returns422NotSilentlyPageOne` — starts red (currently clamps) |
| 11 | ❌ | `/import/actions?pageSize=999` returns 422, not a silent clamp | Unit test | `ImportActions_PageSizeAbove500_Returns422NotSilentClamp` — starts red (currently clamps to 200) |
| 12 | ❌ | `/quotes?pageSize=150` now succeeds | Unit test | `Quotes_PageSize150_NowSucceeds` — starts red (currently 422 under the old 100 max) |
| 13 | ❌ | The not-found helper returns 404 for a missing entity | Unit test | `NotFoundResultHelper_EntityNull_ReturnsProblem404` — starts red |
| 14 | ❌ | The not-found helper returns 200 for a present entity | Unit test | `NotFoundResultHelper_EntityPresent_ReturnsOk200` — starts red |
| 15 | ❌ | The three endpoints' response JSON shape is unchanged | Unit test | Existing `GetAll_*` (Quotes), `Audit_*` (Admin), `GetActions_*` (Import) — regression |
| 16 | ❌ | The two `GetById` not-found paths still behave identically | Unit test | Existing `GetById_*` (Quotes), `GetById_*` (Conversations) — regression |
| 17 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 18 | ❌ | T1 — app starts in Visual Studio; the changed contract behaves as specified | Live (T1) | Developer to confirm in Visual Studio |
| 19 | ❌ | T2 — the live contract holds on all three endpoints | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + the matrix: `?pageSize=abc` → 422, `?pageSize=999` → 422, `?pageSize=500` → 200, `?pageSize=0` → all items, `?page=0` → 422, `?page=<beyond>` → 422 with a distinct detail, on `/quotes`, `/admin/audit`, and `/import/actions` |

---

## Notes

T1 and T2 are both required — this changes live HTTP status codes on three endpoints, and the bare-400
defect it fixes is specifically one that only reproduces through the real Kestrel binding path.
`WebApplicationFactory`'s in-memory TestServer does not always reproduce framework binding failures
identically (the same reason `POST /import`'s bodyless-request check is a T2-only item in `CLAUDE.md`'s
smoke list), so the unit tests alone cannot prove requirement 9.

Once this lands, `CLAUDE.md`'s T2 smoke-test list should gain the pagination-contract matrix from
verification row 19 — that list is living and only grows.
