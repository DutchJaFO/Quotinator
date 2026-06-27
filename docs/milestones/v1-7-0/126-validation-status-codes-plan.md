# Issue #126 — Validation errors return 200 instead of 4xx

**Milestone:** v1.7.0  
**Status:** Code complete — pending release and T1 verification  
**Branch:** `feature/v1-7-0`  
**Tiers required:** T1

---

## Problem

Quote endpoints return HTTP 200 for validation failures (unrecognised genre, unrecognised type, decade not divisible by 10, impossible year range) because the handlers called `Results.Ok(FilteredQuoteResult.Invalid...)`. Clients cannot detect errors by HTTP status code alone.

---

## Changes made

- All validation errors in `GetRandom`, `GetAll`, and `GetSearch` handlers in `QuoteEndpoints.cs` now return the correct 4xx status code.
- `YearParameterSchemaTransformer` extracted from an anonymous lambda to a named `IOpenApiOperationTransformer` class in `src/Quotinator.Api/OpenApi/` (makes the path/param sets unit-testable).
- `YearParseError` helper updated to accept a `paramName` argument; all 12 call sites use `nameof()`.
- `n`, `page`, `pageSize`, `limit` changed from `Status400BadRequest` to `Status422UnprocessableEntity` for consistency with year params (both are semantic/value errors, not structural failures).
- `ApiMessages.YearParamNotInteger` added with `{0}` placeholder; error detail names the specific failing parameter.
- `decade` filter now accepts two-digit shorthand: `80` → 1980–1989, `00` → 2000–2009, `20` → 2020–2029.
- `ErrorDecadeInvalid` message updated in all three locales to document the two-digit form.

### Error taxonomy applied

| Error kind | Status | Examples |
|-----------|--------|---------|
| Structural failure (too long, suspicious chars) | 400 | `character=` + 201-char string; SQL-injection-looking value |
| Unknown language code | 400 | `lang=xx` |
| Semantic/value failure (wrong value, out of range) | 422 | Unrecognised genre/type, decade not ÷10, yearFrom > yearTo, n/page/pageSize/limit out of range or non-integer |

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Unrecognised `genre` → 422 in all three handlers | Unit test | `QuoteEndpointsTests.GetRandom/GetAll/Search_UnknownGenre_Returns422` |
| 2 | ✅ | Unrecognised `type` → 422 in all three handlers | Unit test | `QuoteEndpointsTests.GetRandom/GetAll/Search_UnknownType_Returns422` |
| 3 | ✅ | `decade` not divisible by 10 → 422 in all three handlers | Unit test | `QuoteEndpointsTests.GetRandom/GetAll/Search_DecadeNotDivisibleByTen_Returns422` |
| 4 | ✅ | `yearFrom > yearTo` → 422 in all three handlers | Unit test | `QuoteEndpointsTests.GetRandom/GetAll/Search_YearFromGreaterThanYearTo_Returns422` |
| 5 | ✅ | Oversized/suspicious filter values → 400 | Unit test | `QuoteEndpointsTests.GetRandom_CharacterTooLong/SuspiciousInput_Returns400` |
| 6 | ✅ | `n` non-integer or out of range → 422 | Unit test | `QuoteEndpointsTests.GetRandom_NZero/NTooLarge/NNotInteger_Returns422` |
| 7 | ✅ | `page`, `pageSize`, `limit` non-integer or OOR → 422 | Unit test | `QuoteEndpointsTests.GetAll_PageZero/PageNotInteger/PageSizeZero/PageSizeNotInteger`, `Search_LimitZero/LimitNotInteger_Returns422` |
| 8 | ✅ | Unknown `lang` → 400 | Unit test | `QuoteEndpointsTests.GetRandom/GetAll/Search_InvalidLang_ReturnsBadRequest` |
| 9 | ✅ | Valid filters with no matching data → 200 `NoResults` | Unit test | `QuoteEndpointsTests.GetRandom_ValidFilterNoMatches_ReturnsNoResultsEnvelope` |
| 10 | ✅ | Two-digit decade shorthand → correct year range | Unit test | `QuoteEndpointsTests.GetRandom/GetAll_DecadeShorthand2Digit40/80/00` |
| 11 | ✅ | `YearParameterSchemaTransformer` patches year params to integer type on correct paths only | Unit test | `YearParameterSchemaTransformerTests` (10 tests) |
| 12 | ✅ | Error message names the specific failing parameter | Unit test | `QuoteEndpointsTests.GetRandom_NNotInteger_Returns422` asserts detail contains `"n"` |
| 13 | ⬜ | T1 — manual: `GET /api/v1/quotes/random?type=Tv` → 422, message names `type` | T1 live | Run in VS; check status code and body in Scalar or browser |
| 14 | ⬜ | T1 — manual: `GET /api/v1/quotes/random?decade=80` → 200, results from 1980–1989 | T1 live | Run in VS; verify `quote.year` values in response are in 1980–1989 range |
| 15 | ⬜ | T1 — manual: `GET /api/v1/quotes/random?n=x` → 422, detail says `"n must be a whole number"` | T1 live | Run in VS; confirm message names the parameter |

**Rows 13–15 are required before `gh issue close 126` is called.**
