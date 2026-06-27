# Plan: #109 — Search: field=author and field=character always return empty; type=person returns empty

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/109  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Summary

Three search parameters return empty results silently when the underlying data is absent:
- `field=author` — maps to `p.Name LIKE @like` (People table); bundled sources do not seed People rows, so the LEFT JOIN always produces NULL
- `field=character` — maps to `c.Name LIKE @like` (Characters table); only 2 characters seeded from the curated source
- `type=person` — filters `s.Type = 'Person'`; no quotes of this type exist in the bundled sources

This is also a test architecture gap: endpoint tests use `FakeQuoteService`, so these failure modes are invisible to the test suite.

---

## Decisions (confirmed)

1. **No fallbacks.** Do not fall back to searching quote text when author/character data is absent. Return empty results with a message.
2. **Return `[]` + an informative message.** The message must distinguish between:
   - *No data of this kind in the database* — e.g. "No authors in the database. Add author data to enable author search." → guides the user to add data
   - *Data exists but no match was found* — e.g. "No results found." → guides the user to refine the search term
3. **Goal:** give the user actionable information — either to refine their search or to add missing data.

---

## Response format

The current search endpoint returns `IReadOnlyList<QuoteResponse>` (plain array). Adding a `Message` field requires a wrapper.

The project already has `FilteredQuoteResult<T>` on the random endpoint (has `Status`, `Items`, `TotalMatching`, `Message`). A dedicated `SearchResult<T>` is the natural parallel:

```csharp
public sealed class SearchResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public string? Message { get; init; }   // null when results were found
}
```

**Open design question at implementation time:** reuse `FilteredQuoteResult<T>` (repurposing `TotalMatching = 0` for the empty case) or introduce a new `SearchResult<T>`? Prefer a dedicated type — `FilteredQuoteResult<T>` has `TotalMatching` which is semantically meaningless for a keyword search. Raise if there is a strong reason to share the type.

This is a **breaking response format change** for the search endpoint. The PR description must call this out explicitly, and `README.md`, `addon/DOCS.md`, and the `[Description]` attributes in `QuoteEndpoints.cs` must all be updated in the same commit.

---

## Root cause (confirmed)

The bundled sources (vilaboim, NikhilNamal17) do not carry author or character data. The SQL schema is correct; the data is missing. The message strategy above sidesteps the data strategy question entirely — the feature works correctly whether or not author/character data is seeded; it simply reports what it found.

---

## Approach

1. Introduce `SearchResult<T>` in `Quotinator.Core/Models/`.
2. Change `IQuoteService.Search(...)` to return `SearchResult<QuoteResponse>` instead of `IReadOnlyList<QuoteResponse>`.
3. In the service implementation, detect the "no data of this kind" condition vs "data exists but no match":
   - For `field=author`: if the People table is empty → "no authors" message
   - For `field=character`: if the Characters table is empty → "no characters" message
   - For `type=person`: if no person-type quotes exist → "no person quotes" message
   - Otherwise: standard "no results" message
4. Update the search endpoint handler to return `Results.Ok(result)` with the new wrapper.
5. Add i18n keys for each message variant to `UI.en-GB.json`, `UI.nl.json`, `UI.de.json` and use `IApiLocalizer`.
6. Add SQLite integration tests for all three cases (data absent, data present but no match, data present with match).
7. Add/update `FakeQuoteService.Search` to return `SearchResult<QuoteResponse>` in tests.
8. Update `README.md`, `addon/DOCS.md`, and `QuoteEndpoints.cs` `[Description]` attributes.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `SearchResult<T>` model exists in `Quotinator.Core/Models/` | Live | File exists; `Items` and `Message` properties present |
| 2 | ❌ | `IQuoteService.Search` returns `SearchResult<QuoteResponse>` | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 3 | ❌ | `field=author` with empty People table returns `[]` + "no authors" message | Unit test | `QuoteServiceSqliteTests` — `Search_FieldAuthor_EmptyPeopleTable_ReturnsMessageAndEmptyItems` |
| 4 | ❌ | `field=character` with empty Characters table returns `[]` + "no characters" message | Unit test | `QuoteServiceSqliteTests` — `Search_FieldCharacter_EmptyCharactersTable_ReturnsMessageAndEmptyItems` |
| 5 | ❌ | `type=person` with no person quotes returns `[]` + "no person quotes" message | Unit test | `QuoteServiceSqliteTests` — `Search_TypePerson_NoPeopleQuotes_ReturnsMessageAndEmptyItems` |
| 6 | ❌ | `field=author` with data present and matching returns results + `Message = null` | Unit test | `QuoteServiceSqliteTests` — `Search_FieldAuthor_DataPresent_ReturnsMatches` |
| 7 | ❌ | `field=author` with data present but no match returns `[]` + "no results" message | Unit test | `QuoteServiceSqliteTests` — `Search_FieldAuthor_DataPresent_NoMatch_ReturnsNoResultsMessage` |
| 8 | ❌ | All message strings live in `UI.*.json`; no inline English strings in service or endpoint | Live | `TranslationCompletenessTests` pass; grep for hardcoded strings finds nothing |
| 9 | ❌ | `README.md` and `addon/DOCS.md` reflect updated response format | Live | Both files show `SearchResult` wrapper with `items` + `message` |
| 10 | ❌ | Full test suite green | Live | `dotnet test --configuration Release` — all tests pass |
