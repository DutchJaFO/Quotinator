# Plan: #109 — Search: field=author and field=character always return empty; type=person returns empty

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/109  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Summary

Three search parameters return empty results even when matching data should exist:
- `field=author` — maps to `p.Name LIKE @like` (People table); the bundled sources do not seed People rows, so the LEFT JOIN always produces NULL
- `field=character` — maps to `c.Name LIKE @like` (Characters table); only 2 characters seeded from the curated source
- `type=person` — filters `s.Type = 'Person'`; no quotes of this type exist in the bundled sources

This is also a test architecture gap: the endpoint tests use `FakeQuoteService`, so these failure modes are invisible to the test suite.

---

## Root cause (confirmed)

The data strategy is the underlying issue: the bundled sources (vilaboim, NikhilNamal17) do not carry author or character data. The SQL schema is correct, but the data is missing.

---

## Decision needed before implementation

**The acceptance criteria in the issue are explicitly deferred pending a data strategy decision:**

> Expected behaviour: `field=author` should return quotes where the author name matches — requires either data to be present in the People table, or a design decision on whether to fall back to searching the quote text when no person data exists.

Before writing any code, the following must be decided:

1. **Author/character data strategy:** Should the seed script extract author/character data from the bundled sources (where the data is available in field names like `movie`, `type`)? Or is this out of scope for v1.7.x?
2. **Fallback behaviour for field=author:** If People table is empty, should the search silently return empty (current — correct but unhelpful) or return a 400 with a clear error?
3. **Integration test gap:** Add SQLite integration tests for `field=author`, `field=character`, and `type=person` regardless of how the data question is resolved.

**This plan doc will be updated once the data strategy is decided.**

---

## Approach (pending data strategy decision)

Option A — **Fix the test gap now, defer data strategy:**
- Add SQLite integration tests that seed test data into People and Characters tables and confirm the search works when data is present.
- Leave the bundled dataset as-is (accepting that `field=author` returns empty in production until author data is seeded).
- Close the issue when the test gap is fixed and the behaviour is documented.

Option B — **Fix data + tests:**
- Extend the seed script to extract author data from the bundled sources.
- Add integration tests.
- Close when both are done.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Data strategy decided | Decision | User confirms which option (A or B) to pursue; plan doc updated |
| 2 | ❌ | Integration test for `field=author` exists and passes | Unit test | `QuoteServiceSqliteTests` (or equivalent) — `SearchAsync_FieldAuthor_ReturnsMatchingQuotes` |
| 3 | ❌ | Integration test for `field=character` exists and passes | Unit test | `QuoteServiceSqliteTests` — `SearchAsync_FieldCharacter_ReturnsMatchingQuotes` |
| 4 | ❌ | Integration test for `type=person` exists and passes | Unit test | `QuoteServiceSqliteTests` — `SearchAsync_TypePerson_ReturnsMatchingQuotes` |
| 5 | ❌ | Behaviour when data is absent is documented (empty result or 400) | Live | `GET /api/v1/quotes/search?q=Churchill&field=author` against local instance with empty People table — response matches documented behaviour |
| 6 | ❌ | Full test suite green | Live | `dotnet test --configuration Release` — all tests pass |
