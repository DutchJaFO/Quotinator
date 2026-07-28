# #196 — Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter-parameter shape

**Status:** Waiting for release
**GitHub issue:** #196
**Tiers required:** T1, T2
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

**Correction found mid-implementation**: the plan's own finding 5 originally claimed `EntityFilterParsing`'s
messages would have to be generic because `IApiLocalizer` has no interpolation support. True as far as it
goes, but `ImportEndpoints.cs:174` already shows the sanctioned workaround — the caller applies
`string.Format` on top of the localized template (`ApiMessages.ImportActionAmbiguousFieldsUnresolved`
uses `{0}`). `EntityFilterParsing.ResolveAsync` now takes an `EntityFilterNames` (entity type, id param
name, name param name) and formats its three messages with the actual parameter/entity names, matching
that existing precedent, rather than staying fully generic.

Conventions and constants only — no routes, no repository wiring to a real database, no pagination.

---

## Steps

### 1. ApiTags.MasterData

**Status:** Done. Constant added; `ApiTagsTests.cs` created (first test in `Quotinator.Constants.Tests`).
Went through two revisions: a pairwise `AreNotEqual(ApiTags.X, ApiTags.MasterData)` per-tag test tripped
`MSTEST0032` ("condition is known to be always true" — comparing two `const string` fields is
compiler-provable). Replaced with a single `AllItemsAreUnique` over a hand-written array — but a
hardcoded array has its own real gap: it silently stops covering "all" tags the moment someone adds a
new one and forgets to update the list, exactly what "check all api tags" needs to not happen. Rewrote
again to reflect over every `public const string` field on `ApiTags` (same `GetFields`/`IsLiteral`
technique `SqlQueryGuardTests` already uses), so a future tag is automatically covered with no further
maintenance. Verified it's a genuine check, not vacuous: temporarily set `MasterData = "System"` and
confirmed `ApiTags_AllValues_AreDistinct` failed red, then reverted.

### 2. Tag descriptions in Program.cs (drive-by)

**Status:** Done. Added `MasterData` and `Conversations` entries to `document.Tags` in `Program.cs`,
matching the existing `System`/`Quotes`/`Admin`/`Import` style.

### 3. Masterdata routing convention

**Status:** Done. Documented `/api/v1/masterdata/` in `CLAUDE.md` right after "Route registration order",
stating why two route patterns coexist and recording `/api/v1/conversations`' exemption with its reason.

### 4. EntityFilterParsing shared helper

**Status:** Done. New `src/Quotinator.Api/Endpoints/Shared/EntityFilterParsing.cs`. `ResolveAsync(idValue,
nameValue, names, resolveIdByName, localizer)` returns a 4-outcome result
(`NoFilter`/`Resolved`/`NotFound`/`Error`) — `NotFound` is deliberately not an `IResult`/422, matching the
existing `FilteredResultStatus.NoResults` precedent (200 + empty items + informative message) rather than
treating "name doesn't exist" as bad input. `resolveIdByName` is caller-supplied; no real repository is
wired to it here since no consuming endpoint exists until #184–#189/#192.

Revised mid-implementation (see the Background correction above): messages are formatted via
`string.Format` on a localized `{0}`/`{1}` template using a new `EntityFilterNames` (entity type, id param
name, name param name), naming the actual parameters/entity involved — not fully generic as originally
planned.

### 5. Filter-parameter convention documentation

**Status:** Done. Documented in `CLAUDE.md` after "GUID/enum/id comparisons are case-insensitive by
default": naming, resolution behaviour, validation, matching, the explicit Search/RandomQuote exemption,
and the `string.Format`-templated message design.

### 6. Verify

**Status:** Unit suite green (1339/1339, 0 warnings, 0 errors) and full build clean. T1 confirmed by the
developer — clean Visual Studio startup, no errors. T2 confirmed: `docker build` succeeded, the container
started cleanly (`/api/v1/health` healthy, `/api/v1/version` correct), and `GET /openapi/v1.json` on the
built image shows both `MasterData` and `Conversations` with their new descriptions. This project always
runs T2, not only when a documented trigger applies — worth noting here since this issue's own change to
`Program.cs` would have hit `docs/release-verification.md`'s stated "touches Program.cs startup" trigger
regardless, so an earlier "T2 not required" assessment was wrong on the stated criteria too, not just on
the always-run practice.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ApiTags.MasterData` exists and all `ApiTags` values are mutually distinct | Unit test | `ApiTagsTests.ApiTags_ReflectionFindsDeclaredConstants` + `ApiTags_AllValues_AreDistinct` — reflection-based, first tests in `Quotinator.Constants.Tests` |
| 2 | ✅ | `MasterData` and `Conversations` both have an OpenAPI tag description | Doc/code review | `Program.cs`'s `document.Tags` list |
| 3 | ✅ | `/api/v1/masterdata/` is documented in `CLAUDE.md`, including why two route patterns coexist | Doc review | `CLAUDE.md` contains the named routing convention alongside the existing route rules |
| 4 | ✅ | `/api/v1/conversations`' exemption is recorded with its reason | Doc review | `CLAUDE.md` states Conversations is a masterdata consumer, not a masterdata entity |
| 5 | ✅ | Supplying both an id-valued and name-valued filter returns 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_BothSupplied_ReturnsError` |
| 6 | ✅ | A well-formed id-valued filter resolves without calling the name resolver | Unit test | `EntityFilterParsingTests.ResolveAsync_IdOnlyWellFormed_ReturnsResolved` |
| 7 | ✅ | A malformed id-valued filter returns 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_IdOnlyMalformed_ReturnsError` |
| 8 | ✅ | A name-valued filter that resolves returns the resolved id | Unit test | `EntityFilterParsingTests.ResolveAsync_NameResolves_ReturnsResolved` |
| 9 | ✅ | A name-valued filter that does not resolve returns `NotFound` with an informative message, not a 422 | Unit test | `EntityFilterParsingTests.ResolveAsync_NameDoesNotResolve_ReturnsNotFoundWithMessage` |
| 10 | ✅ | Neither filter supplied returns `NoFilter` with no error | Unit test | `EntityFilterParsingTests.ResolveAsync_NeitherSupplied_ReturnsNoFilter` |
| 11 | ✅ | The filter-parameter convention is documented — naming, resolution, validation, matching, the Search/RandomQuote exemption | Doc review | `CLAUDE.md` contains the convention |
| 12 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 10/10 projects, 1339/1339 passed, 0 warnings, 0 errors |
| 13 | ✅ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed: clean startup, no errors |
| 14 | ✅ | T2 — the built image starts cleanly and the tag descriptions are live | Live (T2) | `docker build` + `docker run`: `/api/v1/health` healthy, `/api/v1/version` correct, `GET /openapi/v1.json` shows `MasterData`/`Conversations` descriptions |

---

## Notes

**T2 was run despite an earlier "not required" assessment.** The original reasoning (no route, no schema
change, no startup behaviour) missed that this issue *does* touch `Program.cs` (the two tag-description
entries) — `docs/release-verification.md`'s own stated trigger list includes "any change that touches...
`Program.cs` startup". More fundamentally, this project's standing practice is to always run T2 regardless
of whether a specific documented trigger applies — corrected here and going forward. `EntityFilterParsing`
itself still isn't exercised by T2 (it's not wired to any endpoint yet), and the `MasterData` tag has no
endpoints under it yet either — both become fully live once #184–#189/#192 wire them up, and carry their
own T2 in those issues.

Verification rows 3, 4, and 11 are doc review rather than a test, which is the weakest kind of gate — the
convention's real proof is #184–#189 and #192 following it (including wiring a real `resolveIdByName`)
without re-deciding. If any of them ends up re-litigating the filter shape, this issue's documentation
failed regardless of its checkboxes.
