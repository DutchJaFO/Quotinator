# #196 — Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter-parameter shape

**Status:** Planning
**GitHub issue:** #196
**Tiers required:** T1
**Depends on:** none

---

## Spec requirements (from the GitHub issue)

1. Add `MasterData` to `Quotinator.Constants.Api.ApiTags`.
2. Document `/api/v1/masterdata/` as a named routing convention in `CLAUDE.md`, including why two
   route patterns coexist.
3. Record that `/api/v1/conversations` deliberately keeps its route and tag.
4. Settle and document the entity-scoped filter-parameter convention — naming, parsing, matching,
   application.
5. Document it in `CLAUDE.md` so #184–#189 and #192 follow rather than re-decide. No specific filter
   is implemented here.

---

## Background — why this issue exists

Sub-issue of #183. Seven consumers each need the same two answers: where the route lives, and how an
entity-scoped filter is shaped. Neither exists today — `ApiTags` has only `System`/`Quotes`/`Admin`/
`Import`/`Conversations`, and no endpoint filters by a related entity's id at all. #192 is
specifically blocked on the filter question so it does not invent a second shape.

Conventions and constants only — no routes, no repository, no pagination.

---

## Steps

### 1. ApiTags.MasterData

**Status:** Not started.

Add the constant alongside the existing five, with a red test first per this project's rule.

### 2. Routing convention

**Status:** Not started.

Document `/api/v1/masterdata/` in `CLAUDE.md` alongside the existing route rules, stating **why two
patterns coexist** rather than leaving it to read as drift: masterdata entities are the shared
reference data quotes and conversations are built from, and grouping them makes that relationship
legible in the API surface.

Record that `/api/v1/conversations` deliberately keeps its existing route and `ApiTags.Conversations`
tag — Conversations is a consumer of masterdata, not a masterdata entity, so #189's list endpoint does
not move. Without this stated, the next reader reasonably assumes an oversight.

### 3. Filter-parameter convention

**Status:** Not started.

Settle whether an entity-scoped filter is id-valued (`?sourceId=`) or name-valued (`?source=`), then
document naming, parsing/validation, matching, and how it reaches the query.

Check `docs/architecture-decisions/` first, per this project's authoritative-sources rule. The
existing `/quotes` `character`/`author`/`source` filters are name-valued case-insensitive *contains*
matches — the closer precedent, but weaker for an exact relationship lookup, where an id is exact and
unambiguous. If the decision diverges from those existing filters, say so explicitly in the
documentation rather than leaving one endpoint silently inconsistent with the convention.

Validation follows #183's contract (422 with a `detail`, never the framework binder's bare 400), and
any id-valued filter is case-insensitive from the start per `CLAUDE.md`'s "GUID/enum/id comparisons
are case-insensitive by default".

### 4. Verify

**Status:** Not started.

Full suite green, 0 warnings.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `ApiTags.MasterData` exists and is distinct from the other tags | Unit test | `Quotinator.Constants.Tests.ApiTags_MasterData_IsDefinedAndDistinct` — starts red |
| 2 | ❌ | Existing `ApiTags` coverage still passes | Unit test | Existing `Quotinator.Constants.Tests` — regression |
| 3 | ❌ | `/api/v1/masterdata/` is documented in `CLAUDE.md`, including why two route patterns coexist | Doc review | `CLAUDE.md` contains the named routing convention alongside the existing route rules |
| 4 | ❌ | `/api/v1/conversations`' exemption is recorded with its reason | Doc review | `CLAUDE.md` states Conversations is a masterdata consumer, not a masterdata entity |
| 5 | ❌ | The filter-parameter convention is documented — naming, parsing, matching, application | Doc review | `CLAUDE.md` contains the convention; any divergence from `/quotes`' existing name-valued filters is stated explicitly |
| 6 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 7 | ❌ | T1 — app starts in Visual Studio | Live (T1) | Developer to confirm in Visual Studio |

---

## Notes

**T2 is not required** — this issue adds one constant and documentation. It introduces no route, no
schema change, no startup behaviour, and no bundled-data change, so there is nothing a container run
would exercise that the unit suite and T1 do not. This is a deliberate assessment against
`docs/release-verification.md`'s criteria, not an omission. The tag becomes observable in Scalar only
once #184–#188 actually apply it, and those issues carry their own T2.

Verification rows 3–5 are doc review rather than a test, which is the weakest kind of gate — the
convention's real proof is #184–#189 and #192 following it without re-deciding. If any of them ends up
re-litigating the filter shape, this issue's documentation failed regardless of its checkboxes.
