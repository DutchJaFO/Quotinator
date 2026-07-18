# #196 — Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter-parameter shape

**Status:** In progress (step 1)
**GitHub issue:** #196
**Tiers required:** T1
**Depends on:** none

---

## Spec requirements (corrected during planning review 2026-07-18)

1. Add `MasterData` to `Quotinator.Constants.Api.ApiTags`.
2. Add missing OpenAPI tag descriptions for `MasterData` and `Conversations` in `Program.cs`'s
   `document.Tags` list — found during verification: `Conversations` never got one when it was added by
   #67-69; fixed as a drive-by alongside `MasterData`'s own.
3. Document `/api/v1/masterdata/` as a named routing convention in `CLAUDE.md`, including why two route
   patterns coexist.
4. Record that `/api/v1/conversations` deliberately keeps its route and tag.
5. Settle and document the entity-scoped filter-parameter convention: **both** an id-valued (`{entity}Id`)
   and a name-valued (`{entity}`) parameter, mutually exclusive. The name-valued form is *resolved* to the
   entity's id first, not applied as a direct contains-match — a name that resolves to nothing is a
   legitimate "zero possible results" case, reported informatively rather than by running a query that
   would also come back empty. `/quotes/search` and `/quotes/random`'s existing fuzzy
   `character`/`author`/`source` contains-match filters are explicitly exempt from this convention.
6. Because requirement 5 has real behaviour (resolution + mutual-exclusion validation), not just naming,
   build a reusable `EntityFilterParsing` helper (mirroring #195's `PaginationParsing`) with its own red
   tests, rather than treating the convention as documentation-only.
7. Document the convention in `CLAUDE.md` so #184–#189 and #192 follow rather than re-decide. No specific
   filter is wired to a real endpoint here — no consumer exists yet.

---

## Background — why this issue exists

Sub-issue of #183. Seven consumers each need the same two answers: where the route lives, and how an
entity-scoped filter is shaped. Neither exists today — `ApiTags` has only `System`/`Quotes`/`Admin`/
`Import`/`Conversations`, and no endpoint filters by a related entity's id at all. #192 is specifically
blocked on the filter question so it does not invent a second shape.

**Verified before starting** (per this project's standing rule — #183/#193/#194/#195 all had errors caught
this way): `ApiTags` and the `/quotes` filters match the issue's claims exactly; no ADR or `docs/decisions/`
note has settled the id-vs-name filter question; `CLAUDE.md`'s referenced sections exist. One claim was
wrong: the issue and original plan draft both listed "existing `ApiTags` coverage — regression, must stay
green" as an Expected Test, but `Quotinator.Constants.Tests` currently has **zero** test files (only
`MSTestSettings.cs`) — there is no existing coverage to regress against; the new test is the first in that
project.

**Design decision, made during plan review**: the issue itself frames id-valued vs. name-valued filters as
an either/or question to "settle", leaning toward id-valued in its own reasoning. Resolved instead as
**both, mutually exclusive** — a name-valued filter resolves to the target entity's id before any query
runs, and a name that doesn't resolve is reported as an informative zero-results case (200), not a 422.
`/quotes/search`/`/quotes/random` keep their existing fuzzy contains-match filters unchanged; this stricter
resolve-first convention is for new entity-scoped filters only.

Conventions and constants only — no routes, no repository wiring to a real database, no pagination.

---

## Steps

### 1. ApiTags.MasterData

**Status:** Not started.

Add the constant alongside the existing five, with a red test first per this project's rule. First real
test in `Quotinator.Constants.Tests` (project exists but has never had one).

### 2. Tag descriptions in Program.cs (drive-by)

**Status:** Not started.

Add `MasterData` and `Conversations` entries to `document.Tags` in `Program.cs`, matching the existing
`System`/`Quotes`/`Admin`/`Import` style. `Conversations` never got one — found during verification, fixed
here per explicit developer instruction rather than filed separately.

### 3. Masterdata routing convention

**Status:** Not started.

Document `/api/v1/masterdata/` in `CLAUDE.md` alongside the existing route rules, stating **why two
patterns coexist** rather than leaving it to read as drift: masterdata entities are the shared reference
data quotes and conversations are built from, and grouping them makes that relationship legible in the API
surface.

Record that `/api/v1/conversations` deliberately keeps its existing route and `ApiTags.Conversations`
tag — Conversations is a consumer of masterdata, not a masterdata entity, so #189's list endpoint does
not move.

### 4. EntityFilterParsing shared helper

**Status:** Not started.

New `src/Quotinator.Api/Endpoints/Shared/EntityFilterParsing.cs`. `ResolveAsync(idValue, nameValue,
resolveIdByName, localizer)` returns a 4-outcome result (`NoFilter`/`Resolved`/`NotFound`/`Error`) —
`NotFound` is deliberately not an `IResult`/422, matching the existing `FilteredResultStatus.NoResults`
precedent (200 + empty items + informative message) rather than treating "name doesn't exist" as bad
input. `resolveIdByName` is caller-supplied; no real repository is wired to it here since no consuming
endpoint exists until #184–#189/#192.

Both violation and malformed-id error details use new, generic (non-interpolated) `ApiMessages` keys —
`IApiLocalizer` has no format-argument support, confirmed by reading `ApiLocalizer.cs`, and this helper is
reused across every future entity, so messages can't name the specific failing parameter.

### 5. Filter-parameter convention documentation

**Status:** Not started.

Document in `CLAUDE.md`, placed after "GUID/enum/id comparisons are case-insensitive by default": naming
(`{entity}Id`/`{entity}` mutually exclusive), resolution behaviour (not a contains-match), validation (422
detail for mutual-exclusion or malformed id), matching (case-insensitive exact match once resolved to an
id), the explicit Search/RandomQuote exemption, and how a consumer wires its own repository as the
`resolveIdByName` delegate.

### 6. Verify

**Status:** Not started.

Full suite green, 0 warnings. T2 not required — reasoning extended from the original assessment: no route,
no schema change, no startup behaviour, and `EntityFilterParsing` takes its resolver as an injected
delegate, fully covered by unit tests against a fake resolver.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `ApiTags.MasterData` exists and is distinct from the other tags | Unit test | `ApiTagsTests.ApiTags_MasterData_IsDefinedAndDistinct` — first test in `Quotinator.Constants.Tests`, starts red |
| 2 | ❌ | All `ApiTags` values are mutually distinct | Unit test | `ApiTagsTests.ApiTags_AllValues_AreDistinct` — starts red |
| 3 | ❌ | `MasterData` and `Conversations` both have an OpenAPI tag description | Doc/code review | `Program.cs`'s `document.Tags` list |
| 4 | ❌ | `/api/v1/masterdata/` is documented in `CLAUDE.md`, including why two route patterns coexist | Doc review | `CLAUDE.md` contains the named routing convention alongside the existing route rules |
| 5 | ❌ | `/api/v1/conversations`' exemption is recorded with its reason | Doc review | `CLAUDE.md` states Conversations is a masterdata consumer, not a masterdata entity |
| 6 | ❌ | Supplying both an id-valued and name-valued filter returns 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_BothSupplied_ReturnsError` |
| 7 | ❌ | A well-formed id-valued filter resolves without calling the name resolver | Unit test | `EntityFilterParsingTests.ResolveAsync_IdOnlyWellFormed_ReturnsResolved` |
| 8 | ❌ | A malformed id-valued filter returns 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_IdOnlyMalformed_ReturnsError` |
| 9 | ❌ | A name-valued filter that resolves returns the resolved id | Unit test | `EntityFilterParsingTests.ResolveAsync_NameResolves_ReturnsResolved` |
| 10 | ❌ | A name-valued filter that does not resolve returns `NotFound` with an informative message, not a 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_NameDoesNotResolve_ReturnsNotFoundWithMessage` |
| 11 | ❌ | Neither filter supplied returns `NoFilter` with no error | Unit test | `EntityFilterParsingTests.ResolveAsync_NeitherSupplied_ReturnsNoFilter` |
| 12 | ❌ | The filter-parameter convention is documented — naming, resolution, validation, matching, the Search/RandomQuote exemption | Doc review | `CLAUDE.md` contains the convention |
| 13 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 14 | ❌ | T1 — app starts in Visual Studio | Live (T1) | Developer to confirm in Visual Studio |

---

## Notes

**T2 is not required** — this issue adds one constant, two tag descriptions, one self-contained helper with
no I/O of its own (its resolver is an injected delegate), and documentation. It introduces no route, no
schema change, no startup behaviour, and no bundled-data change, so there is nothing a container run would
exercise that the unit suite and T1 do not. The tag becomes observable in Scalar only once #184–#188
actually apply it, and `EntityFilterParsing` becomes live only once a consumer wires a real resolver to it —
both carry their own T2 in their own issues.

Verification rows 4, 5, and 12 are doc review rather than a test, which is the weakest kind of gate — the
convention's real proof is #184–#189 and #192 following it (including wiring a real `resolveIdByName`)
without re-deciding. If any of them ends up re-litigating the filter shape, this issue's documentation
failed regardless of its checkboxes.
