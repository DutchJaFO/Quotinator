# Issue #109 — Search: field=author and field=character always return empty; type=person returns empty

**Milestone:** v1.7.0  
**Status:** Complete — merged to main via PR #116  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

`/api/v1/quotes/search` returned empty results for:
- `field=author` — `p.Name LIKE @like` via LEFT JOIN on People; bundled sources carry no person data so all rows return NULL for `p.Name`. `NULL LIKE '%pattern%'` evaluates to NULL, never TRUE.
- `field=character` — same pattern via Characters table; only 2 characters are seeded from the curated source.
- `type=person` — no quotes of type `person` exist in the bundled sources.

Additional gaps found during planning:
- `/search` returned a bare array; `/random` returned a `FilteredQuoteResult<T>` envelope. The inconsistency hid `NoResults` from callers.
- `/search` did not validate `type` and `genre` values; `/random` did. Invalid values silently returned empty.
- No endpoint logging existed on any handler — impossible to confirm whether requests were reaching the endpoint.

---

## Scope

1. Change `/search` to return `FilteredQuoteResult<QuoteResponse>` envelope (consistent with `/random`)
2. Surface `NoResults` status + message when valid filters match nothing
3. Add `type`/`genre` validation to `/search` (returns `InvalidType`/`InvalidGenre` in envelope)
4. Add `ILogger<QuoteEndpoints>` with a log line to all four handlers (`GetRandom`, `GetById`, `Search`, `GetAll`)
5. Add SQLite integration tests for `SqliteQuoteService.Search()` covering the NULL LIKE data-gap scenarios

---

## Changes

| File | Change |
|------|--------|
| `src/Quotinator.Core/Services/IQuoteService.cs` | `Search()` return type → `FilteredQuoteResult<QuoteResponse>` |
| `src/Quotinator.Core/Data/SqliteQuoteService.cs` | Wrap result in `FilteredQuoteResult<T>`; status = `Ok` or `NoResults` |
| `tests/Quotinator.Api.Tests/Fakes/FakeQuoteService.cs` | `Search()` return type updated to match |
| `src/Quotinator.Api/Endpoints/QuoteEndpoints.cs` | Add `ILogger`, log lines on all 4 handlers; add `ValidateFilterParams` call to Search; handle `NoResults` |
| `docs/logging.md` | Added 4 new `[Api - *]` prefix entries |
| `tests/Quotinator.Api.Tests/Endpoints/QuoteEndpointsTests.cs` | Updated all Search tests to navigate envelope; added `NoResults`, `InvalidType`, `InvalidGenre` envelope tests |
| `tests/Quotinator.Core.Tests/Data/SqliteQuoteServiceSearchTests.cs` | New file — 14 SQLite integration tests for `Search()` |
| `README.md` | Updated Search row and filter parameters description |
| `addon/DOCS.md` | Updated Search row |

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `/search` returns `FilteredQuoteResult<T>` envelope with `status`, `items`, `totalMatching` | Unit test | `QuoteEndpointsTests.Search_MatchingQuery_ReturnsOkEnvelope` |
| 2 | ✅ | Valid query with no matches returns `NoResults` status and non-empty `message` | Unit test | `QuoteEndpointsTests.Search_NoResults_ReturnsNoResultsEnvelope` |
| 3 | ✅ | Unknown `type` returns `InvalidType` status in envelope | Unit test | `QuoteEndpointsTests.Search_UnknownType_ReturnsInvalidTypeEnvelope` |
| 4 | ✅ | Unknown `genre` returns `InvalidGenre` status in envelope | Unit test | `QuoteEndpointsTests.Search_UnknownGenre_ReturnsInvalidGenreEnvelope` |
| 5 | ✅ | `field=character` returns `Ok` when character data exists | SQLite test | `SqliteQuoteServiceSearchTests.Search_FieldCharacter_WithCharacterData_ReturnsOk` |
| 6 | ✅ | `field=character` returns `NoResults` when no quotes have a character (NULL LIKE data-gap) | SQLite test | `SqliteQuoteServiceSearchTests.Search_FieldCharacter_QuoteHasNoCharacter_ReturnsNoResults` |
| 7 | ✅ | `field=author` returns `Ok` when author data exists | SQLite test | `SqliteQuoteServiceSearchTests.Search_FieldAuthor_WithAuthorData_ReturnsOk` |
| 8 | ✅ | `field=author` returns `NoResults` when no quotes have an author (NULL LIKE data-gap) | SQLite test | `SqliteQuoteServiceSearchTests.Search_FieldAuthor_QuoteHasNoAuthor_ReturnsNoResults` |
| 9 | ✅ | `type=person` returns quotes where the type=person data exists | SQLite test | `SqliteQuoteServiceSearchTests.Search_TypePerson_ReturnsPerson` |
| 10 | ✅ | `type=anime` returns `NoResults` when no anime data exists | SQLite test | `SqliteQuoteServiceSearchTests.Search_TypeAnime_NoAnimeData_ReturnsNoResults` |
| 11 | ✅ | Entry logging on `/random` with `[Api - Random]` prefix | Code review | `QuoteEndpoints.cs:GetRandom` — `logger.LogInformation("[Api - Random] ...")` |
| 12 | ✅ | Entry logging on `/search` with `[Api - Search]` prefix | Code review | `QuoteEndpoints.cs:Search` — `logger.LogInformation("[Api - Search] ...")` |
| 13 | ✅ | Entry logging on `/{id}` with `[Api - GetById]` prefix | Code review | `QuoteEndpoints.cs:GetById` — `logger.LogInformation("[Api - GetById] ...")` |
| 14 | ✅ | Entry logging on `/` with `[Api - GetAll]` prefix | Code review | `QuoteEndpoints.cs:GetAll` — `logger.LogInformation("[Api - GetAll] ...")` |
| 15 | ✅ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
| 16 | ✅ | PR merged to main | — | PR #116 merged to main 2026-06-25 |

---

## Deferred scope

The underlying data gap (no person/character rows in bundled sources) remains. `field=author` and `field=character` will correctly return `NoResults` with a message rather than silently returning empty. Populating the data is a separate concern outside this issue's scope.

The HA "endpoint not reached" symptom was mitigated by adding logging. Full root cause investigation is deferred — an open issue or future session should capture live logs to confirm whether requests are reaching the handler at all when the problem occurs.
