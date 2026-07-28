# #216 — Series/Universe name-filter case sensitivity (confirmed bug) + audit all Name/Title natural-key comparisons

**Status:** Waiting for release
**GitHub issue:** #216
**Tiers required:** T1, T2
**Depends on:** Nothing — independent of #211 (see Background for the deliberate scope split); no
dependency on #181/#153/#217

---

## Background

Found live while verifying unrelated #173 work (2026-07-22). `Sql.Series.SelectIdByName` and
`Sql.Universe.SelectIdByName` are consulted by `ISeriesNameResolver`/`IUniverseNameResolver`
(`src/Quotinator.Core/Repositories/SeriesNameResolver.cs`, `UniverseNameResolver.cs`), the
`resolveIdByName` delegate `EntityFilterParsing.ResolveAsync` (#196's shared entity-scoped filter
convention) calls for a `series=`/`universe=` query parameter on `GET /quotes/random`,
`.../search`, and `GET /quotes` (`QuoteEndpoints.cs`, 4 call sites). Both SQL constants do a plain
`WHERE Name = @name` — case-sensitive in SQLite by default. `?series=holy grail` resolves to
`NotFound` (200, empty `items`) against a stored `"Holy Grail"`, instead of matching.

**Deliberately separate scope from #211.** #211 (open research) investigates non-id string-column
case-sensitivity gaps, but its own enumerated column list (`Status`, `EntityType`, `ActionType`,
`Type`, `Language`, `TableName`, `Field`/`Action`) is entirely enum/status/discriminator-typed columns
— it does not mention any `Name`/`Title` natural-key column. This issue covers that distinct class
directly. Fix #216 first (confirmed bug, narrow scope), let #211 pick up whatever's left outside it.

**Verified directly against the codebase before planning (not assumed from the issue text alone):**

| Query | Case-insensitive today? | Where |
|---|---|---|
| `Sql.Series.SelectIdByName` | ❌ No — plain `WHERE Name = @name` | `Sql.cs:476` |
| `Sql.Universe.SelectIdByName` | ❌ No — plain `WHERE Name = @name` | `Sql.cs:530` |
| `Sql.People.SelectIdByName` | ❌ No — plain `WHERE Name = @name` | `Sql.cs:321` |
| `Sql.Sources.SelectIdByTitleAndType` | ✅ **Already fixed** — `LOWER(Title) = LOWER(@title) AND LOWER(Type) = LOWER(@type)` | `Sql.cs:375-376`, #180 |
| `Sql.Sources.SelectExistingByTitleAndType` | ✅ **Already fixed**, same pattern | `Sql.cs:387-388`, #180 |

**Correction to the issue's own scope list**: item 3 names `Sql.Sources.SelectIdByTitleAndType` (and
its sibling) as part of the audit, but both are already case-insensitive since #180 — confirmed by
reading the live SQL, not assumed from the issue text. The genuinely open gap for item 3 is narrower
than the issue describes: **People, Series, and Universe's own `SelectIdByName` queries only** —
Sources' natural-key matching is not part of this issue's remaining work.

**The fix point is shared between items 1 and 3, not two separate fixes.** `Sql.Series.SelectIdByName`
and `Sql.Universe.SelectIdByName` are the *same* SQL constants consulted by both the API query-filter
path (item 1, via `ISeriesNameResolver`/`IUniverseNameResolver`) and the import-time natural-key match
path (item 3, via `ImportActionPlanner.cs`'s several direct callers — confirmed 3 call sites for
`Series`, 2 for `Universe`, plus 3 for `Sql.People.SelectIdByName`). Wrapping these three SQL constants
once fixes both items simultaneously for these three entities.

**Item 3's "explicit, documented decision" is not actually open — it's an existing, repeated project
convention being found not-yet-applied here.** CLAUDE.md's "Case-insensitive by default" section (and
its Source-specific precedent, #180) already settles the case-insensitive-vs-exact-match tradeoff
project-wide: case-insensitive by default, always, specifically to avoid the near-duplicate-entity risk
item 3 itself describes (`"Winston Churchill"` vs `"winston churchill"`). This issue is another instance
of "recurred in Sql.Sources/People's id lookups (#180) — audit sibling queries whenever one is fixed"
(CLAUDE.md's own stated pattern for this class of bug), not a fresh decision to make per column.

---

## Spec requirements (from the GitHub issue)

1. Fix the confirmed bug: wrap `Sql.Series.SelectIdByName` and `Sql.Universe.SelectIdByName` as
   `LOWER(Name) = LOWER(@name)`. Add a regression test covering a mixed-case `series=`/`universe=`
   query parameter on at least one of `/quotes/random`, `/quotes/search`, `GET /quotes`.
2. Audit every query parameter across every endpoint for the same gap — any name/title/text-valued
   filter parameter, not just id/enum-typed ones already covered by the existing rule.
3. Audit every natural-key Name/Title matching query used during **import** for the same gap:
   `Sql.People.SelectIdByName`, `Sql.Sources.SelectIdByTitleAndType` (already fixed, see Background),
   `Sql.Series.SelectIdByName`, `Sql.Universe.SelectIdByName`.
4. Update CLAUDE.md's "GUID/enum/id comparisons are case-insensitive by default" section to explicitly
   cover Name/Title natural-key columns — the current text is scoped to "GUID, enum, or other
   identifier comparison," ambiguous on whether a `Title`/`Name` column counts.

**Scope explicitly widened by the developer (2026-07-25), beyond the issue's own 4 named queries**:
"make sure you check all string/text properties across all entities" — items 2 and 3 above are not
satisfied by auditing only the queries the issue text happened to name. A full, systematic audit was
run: every entity in `Quotinator.Core/Entities` and `Quotinator.Data/Entities`, every string-typed
property on each, and every WHERE/JOIN comparison against each in both `Sql.cs` files,
`RepositorySql.cs`, `SqliteQuoteService.BuildFilterWhere`, and `EntityFilterParsing`/the
`I*NameResolver` classes. Results below, replacing Steps 2–3's original "TBD" framing with concrete
findings.

---

## Steps

### 2. Comprehensive audit — every string/text property, every entity

**Status:** Done (audit only; fixes are Steps 3a–3d below).

Full results (every entity's string properties checked against every WHERE/JOIN in both `Sql.cs`
files, `RepositorySql.cs`, `SqliteQuoteService.BuildFilterWhere`, `EntityFilterParsing`, and the
`I*NameResolver` classes):

**Confirmed bugs found beyond the issue's own 4 named queries:**

| # | Column | Query | External-input path | Risk |
|---|---|---|---|---|
| A | `QuoteTranslation`/`SourceTranslation`/`CharacterTranslation`/`StageDirectionTranslation`/`SoundCueTranslation`.`Language` (5 tables) | `Sql.Quotes.SelectBase`'s JOINs (`qt.Language=@lang`, `st.Language=@lang`, `ct.Language=@lang`), `StageDirections.SelectByIdWithTranslation`, `SoundCues.SelectByIdWithTranslation`, `SourceTranslations.CountForSource` — 6 unwrapped comparisons total | `?lang=` raw HTTP query parameter, present on nearly every read endpoint (`GET /quotes/{id}`, `/random`, `/all`, `/search`, `/conversations/{id}`); validated for shape only (`InputValidation.IsValidLang`, which **accepts uppercase**), never case-normalized before binding. Also bound from import-file translation-object keys (`QuoteSeedWriter.cs`). | **Highest-impact finding.** `?lang=NL` silently fails to match and falls back to the original language — reachable on essentially every read endpoint in the API. |
| B | `SystemAuditEntry.TableName` | `Sql.SystemAudit.BuildWhere`, `SystemAudit.DeleteByTable` | `?table=` on `GET /api/v1/admin/audit` (public) and `DELETE /api/v1/admin/audit` (admin) | **Genuine bug, worse failure mode on delete**: `?table=quotes` (lowercase — the natural spelling given the endpoint's own JSON casing conventions) silently filters to zero rows on GET, and silently deletes **nothing** on DELETE — looks like success, does nothing. |
| C | `SystemChangeLog.EntityType` | `Sql.SystemChangeLog.SelectByEntity` (`EntityId` on the same query is already wrapped; `EntityType` is not) | `ISystemChangeLogReader.GetHistoryAsync` is DI-registered but **has no HTTP endpoint today** | Low live risk (no current caller), but a live instance of "the exact pattern that recurs once a new endpoint is wired to an existing reader" — cheap to fix now while it's already found. |

**Borderline finding, needs a project decision, not obviously a "bug":**

| Column(s) | Query | Why it's borderline |
|---|---|---|
| `Quotes.QuoteText`, `Sources.Title`, `Characters.Name`, `People.Name` (search/filter) | `Sql.SearchField.*`, `SqliteQuoteService.BuildFilterWhere`'s `character=`/`author=`/`source=` fuzzy filters — all use SQLite's `LIKE`, no explicit `PRAGMA case_sensitive_like` set | SQLite's `LIKE` is case-insensitive **for ASCII only** by default — it does not extend to non-ASCII casing (accented Latin, Cyrillic, CJK, etc.), unlike this project's own `OrdinalIgnoreCase`/`ToUpperInvariant`-based case-insensitivity everywhere else. Given quote/character/source content is not English-only, a search for an accented title may not behave the same as an ASCII one. Not clearly a "bug" against SQLite's documented behaviour, but arguably inconsistent with this project's own stated case-insensitive-by-default guarantee. **Flagging per this project's "gap resolution is the developer's decision" rule — not deciding this here.** |

**LIKE/Unicode claim verified against official SQLite documentation** (developer asked for this
explicitly, 2026-07-25) — [sqlite.org/lang_expr.html](https://www.sqlite.org/lang_expr.html) confirms:
"SQLite only understands upper/lower case for ASCII characters by default. The LIKE operator is case
sensitive by default for unicode characters that are beyond the ASCII range" — matching the documented
example (`'a' LIKE 'A'` → true, `'æ' LIKE 'Æ'` → false). Only fixable via `PRAGMA case_sensitive_like`
(which goes the *wrong* direction — forces LIKE to be case-sensitive even for ASCII, not the fix
wanted here) or the ICU extension (not loaded by this project). Confirms the borderline finding above
is real, not a misreading of SQLite's behaviour.

**Developer decision recorded (2026-07-25):** accepted as a known limitation for now, not fixed as
part of this issue. Quotinator currently has no translations that would exercise non-ASCII partial-match
search in a way that's known to be broken, so the practical impact today is low. Tracked as a separate
follow-up, [#222](https://github.com/DutchJaFO/Quotinator/issues/222), filed to the v1.8.0 maintenance
milestone — that issue also records a lighter-weight fix option found while investigating this
(`SqliteConnection.CreateCollation`/`CreateFunction`, confirmed present on `Microsoft.Data.Sqlite`,
avoiding the multi-arch native ICU extension binary entirely) alongside the ICU-extension option.

**Every Quote endpoint query parameter re-checked directly against `QuoteEndpoints.cs`** (developer
asked whether this had actually been done, 2026-07-25 — it had only been done via a subagent's summary
before this point, now independently re-verified against the endpoint source directly):

| Endpoint | Every parameter | Disposition |
|---|---|---|
| `GetRandom` | `n`, `lang`, `type[]`, `genre[]`, `character`, `author`, `source`, `yearFrom`, `yearTo`, `year`, `decade`, `seriesId`, `series`, `universeId`, `universe` | `n`/`yearFrom`/`yearTo`/`year`/`decade` parsed as integers (no string comparison); `type[]` normalized via `Enum.TryParse(ignoreCase: true)` before binding; `genre[]` normalized via an `OrdinalIgnoreCase` dictionary before binding; `seriesId`/`universeId` already case-insensitive (`IdClauses`); `character`/`author`/`source` → the LIKE/Unicode borderline finding; `series`/`universe` → the confirmed bug (Step 3a); `lang` → Finding A |
| `Search` | Same as above, plus `q`, `field`, `limit` | `q` → the same LIKE/Unicode borderline finding; `field` is lower-cased by the endpoint itself (`field?.ToLowerInvariant()`) before being matched against fixed lowercase C# `switch` literals in `SqliteQuoteService.Search` — already safe, confirmed by reading that switch directly; `limit` parsed as integer |
| `GetById` | `lang` only | Finding A |
| `GetAll` | Same as `GetRandom` minus `n`/`character`/`author`/`source`, plus `page`/`pageSize` | `page`/`pageSize` parsed as integers; everything else already covered above |

No parameter was found outside what's already captured in Findings A/B/C and the LIKE/Unicode
borderline finding above — this re-check confirms the existing scope, it doesn't add a new gap.

**Checked and confirmed already safe** (verified, not assumed): `Character.SourceType` and
`ImportBatch.Type` are always bound from an enum's own `.ToString()` — internally generated, never raw
external casing. `System_ImportActions.Status`/`.EntityType`/`.BatchId` are already `UPPER()`-wrapped
(prior issue). `RepositorySql.cs`'s generic layer has no unwrapped string-value comparison anywhere.
`StageDirection`/`SoundCue` text is never used as a lookup key (id-only, by explicit design). Every
other payload-only column (`ExistingValue`, `Description`, `ImageUrl`, etc.) never appears in a
WHERE/JOIN at all.

### 3a. Fix Series/Universe/People `SelectIdByName` — case-insensitive

**Status:** Done. (Renumbered from the original Step 1 — kept as its own step since it's the issue's
own confirmed, named bug.)

Wrapped all three as `WHERE LOWER(Name) = LOWER(@name) AND IsDeleted = 0`, matching #180's own
`Sources.SelectIdByTitleAndType` precedent exactly (hand-written `LOWER()`, not `IdClauses.Equals` —
`IdClauses` is reserved for id columns; Name/Title columns follow Sources' own established pattern
instead). Fixes items 1 and 3 of the original spec simultaneously for these three entities, per
Background. `ISeriesNameResolver`/`IUniverseNameResolver`'s XML doc comments updated from "this exact
name" to "this name (case-insensitive, #216)".

Tests: `PlanUniverseAsync_ExistingByName_DifferingCasing_NoActionStaged`,
`PlanSeriesAsync_ExistingByName_DifferingCasing_NoActionStaged`,
`PlanPeopleAsync_NoIdMatch_DifferingCasing_FallsBackToNaturalKey_NoActionStaged` (all in
`ImportActionPlannerTests.cs`, real-SQLite, proves the import-time natural-key path) plus a new
`SeriesUniverseNameResolverTests.cs` (real-SQLite, proves `SeriesNameResolver`/`UniverseNameResolver`
themselves — the actual classes `EntityFilterParsing.ResolveAsync` calls for the `series=`/`universe=`
query filters — since `QuoteEndpointsTests.cs`'s existing series/universe coverage substitutes a Fake
resolver and would not have exercised the real SQL fix).

### 3b. Fix `lang` query-parameter case-sensitivity (Finding A)

**Status:** Done.

Implemented **both halves**, not input-normalization alone — confirmed during implementation that the
SQL side still needed its own wrap: a translation's `Language` column is never canonicalized at capture
(its value is whatever casing an import file's translation-object key used, per
`QuoteSeedWriter.InsertTranslationsAsync`), unlike an id column, so a stored `Language` value can
genuinely be mixed-case independent of what casing a caller's `?lang=` happens to use.

- Added `InputValidation.TryNormalizeLang(ref string? lang)` (`Quotinator.Core.Helpers`) — validates
  and lowercases in one step, the single choke point every `?lang=`-accepting endpoint now calls.
  `QuoteEndpoints.ValidateCommon` takes `ref lang` and calls it (used by `GetRandom`/`GetById`/
  `Search`/`GetAll`); `ConversationEndpoints.GetById`'s own inline check calls it too.
- Wrapped all 6 SQL `Language = @lang` fragments as `LOWER(...) = LOWER(@lang)`
  (`Sql.Quotes.SelectBase`'s three translation JOINs, `Sql.SourceTranslations.CountForSource`,
  `Sql.StageDirections.SelectByIdWithTranslation`, `Sql.SoundCues.SelectByIdWithTranslation`) —
  defense-in-depth per the project's established "wrap both sides, never rely on capture-time
  canonicalization alone" convention.
- Also wrapped the 3 `CASE WHEN ... THEN @lang ELSE ... END AS EffectiveLanguage` fragments as
  `LOWER(@lang)` — without this, a raw uppercase `@lang` would still echo back uncanonicalized in the
  response even after the JOIN condition itself matched correctly.

Tests: `InputValidationTests.TryNormalizeLang_*` (validation + lowercasing behaviour in isolation) and
`SqliteQuoteServiceTests.GetById_UppercaseLang_StillMatchesLowercaseStoredTranslation` (real-SQLite,
calls `SqliteQuoteService.GetById` directly with a raw uppercase `lang` — bypassing the endpoint-layer
normalization entirely — to prove the SQL-side fix holds on its own, not just in combination with the
input-side one).

### 3c. Fix `SystemAudit.TableName` case-sensitivity (Finding B)

**Status:** Done.

Wrapped both `BuildWhere` and `DeleteByTable` as `LOWER(TableName) = LOWER(@table)`. The DELETE
endpoint's silent-no-op failure mode makes this a genuine correctness bug, not just a query-filter miss.

Tests: `SystemAuditReaderTests.GetPagedAsync_LowercaseTableFilter_StillMatchesPascalCaseStoredRows`,
`SystemAuditWriterTests.ClearAsync_WithLowercaseTable_StillDeletesMatchingEntries` (both real-SQLite).

### 3d. Fix `SystemChangeLog.EntityType` case-sensitivity (Finding C)

**Status:** Done.

Wrapped `SelectByEntity`'s `EntityType = @entityType` the same way its own `EntityId` column on the
same query already is. No live endpoint exercises this today, so no regression risk beyond the
unit-test level.

Tests: `SystemChangeLogWriterReaderTests.GetHistoryAsync_MixedCaseEntityType_StillMatches` (real-SQLite).

### 4. Update CLAUDE.md

**Status:** Done.

Renamed the section header to "GUID/enum/id/Name/Title comparisons are case-insensitive by default"
and broadened its first paragraph to explicitly cover Name/Title natural-key columns (citing
`Sources.SelectIdByTitleAndType`/`Series`/`Universe`/`People`'s `SelectIdByName`), documented the
`lang`/`TableName`/`EntityType` findings as further recurrences in the "found and fixed piecemeal"
paragraph, and added a new paragraph recording the LIKE/Unicode exception as deliberate and pointing
to #222. `docs/database-conventions.md`'s cross-reference to the section's old title updated to match.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `series=`/`universe=` query filters match case-insensitively | Unit test | `SeriesUniverseNameResolverTests.SeriesNameResolver_DifferingCasing_StillResolvesId`/`UniverseNameResolver_DifferingCasing_StillResolvesId` (real-SQLite, exercises the actual resolver classes `EntityFilterParsing.ResolveAsync` calls) |
| 2 | ✅ | `Sql.Series.SelectIdByName`/`Sql.Universe.SelectIdByName` match case-insensitively at the SQL level | Unit test | Same as row 1, plus `ImportActionPlannerTests.PlanSeriesAsync_ExistingByName_DifferingCasing_NoActionStaged`/`PlanUniverseAsync_ExistingByName_DifferingCasing_NoActionStaged` (import-time natural-key path) |
| 3 | ✅ | `Sql.People.SelectIdByName` matches case-insensitively | Unit test | `ImportActionPlannerTests.PlanPeopleAsync_NoIdMatch_DifferingCasing_FallsBackToNaturalKey_NoActionStaged` |
| 4 | ✅ | Comprehensive audit of every string/text property across every entity, not just the issue's 4 named queries | Live (review) | Done — see Step 2's findings table (Findings A/B/C + the LIKE/Unicode borderline case) |
| 5 | ✅ | `?lang=` case mismatch no longer silently falls back to the original language (Finding A) | Unit test + Live | `InputValidationTests.TryNormalizeLang_*`, `SqliteQuoteServiceTests.GetById_UppercaseLang_StillMatchesLowercaseStoredTranslation` (real-SQLite, proves the positive-match case). Live T2 only confirms no regression (`?lang=NL` returns 200, falls back gracefully) — a live positive-match proof isn't currently possible against real data: no import path can persist a translation yet (`QuoteSeedWriter.InsertTranslationsAsync` has zero callers anywhere in the codebase — a prepared-but-not-yet-wired-up capability per developer confirmation 2026-07-25, out of scope for #216) — see Notes. |
| 6 | ✅ | `GET`/`DELETE /admin/audit?table=` match case-insensitively (Finding B) | Unit test | `SystemAuditReaderTests.GetPagedAsync_LowercaseTableFilter_StillMatchesPascalCaseStoredRows`, `SystemAuditWriterTests.ClearAsync_WithLowercaseTable_StillDeletesMatchingEntries` |
| 7 | ✅ | `SystemChangeLog.SelectByEntity`'s `EntityType` matches case-insensitively (Finding C) | Unit test | `SystemChangeLogWriterReaderTests.GetHistoryAsync_MixedCaseEntityType_StillMatches` |
| 8 | ✅ | LIKE-based free-text search's ASCII-only case-folding either fixed for full Unicode parity or explicitly accepted as-is | Live (review) | Developer decision recorded 2026-07-25: accepted as-is for now (no translations currently exercise it); follow-up fix tracked separately as [#222](https://github.com/DutchJaFO/Quotinator/issues/222) in the v1.8.0 maintenance milestone |
| 9 | ✅ | CLAUDE.md updated | Live (review) | Section renamed and broadened, see Step 4 |
| 10 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — Quotinator.Core.Tests (1118), Quotinator.Data.Tests (655), Quotinator.Api.Tests (511) all green, 0 warnings, 0 errors |
| 11 | ✅ | T1 — app starts in Visual Studio, mixed-case series/universe filter matches | Live (T1) | Confirmed 2026-07-25: after `POST /admin/database/reset` (genuine reseed, 799 quotes/464 sources), `GET /quotes/random?n=22&universe=james+bond` (lowercase) returned Dr. No's quotes ("My name is Bond, James Bond.", "Bond. James Bond.") alongside Goldfinger's — matching T2's result exactly. The developer's first T1 attempt (against a stale, already-seeded dev DB) initially showed only 2 Goldfinger quotes with no Dr. No — not a bug, see the T2 row and Notes below for why an already-seeded DB doesn't pick up updated bundled source content until reset. |
| 12 | ✅ | T2 — Docker smoke test: mixed-case `series=`/`universe=` filter, mixed-case `table=` resolve correctly against bundled data; `lang=` case-insensitivity confirmed with no regression | Live (T2) | `docker build` + fresh reseed (799 quotes/464 sources) — `curl ".../quotes/random?n=20&universe=james+bond"` (lowercase) returned `totalMatching: 5` including all 3 Dr. No quotes (this is the exact data-completeness gap the developer's own T1 run surfaced against a stale dev DB — a fresh reseed resolves it); `curl ".../quotes/random?n=20&series=sean+connery+era"` (lowercase) same 5 results; `curl ".../quotes/{id}?lang=NL"` returned 200 with a graceful original-language fallback (no translation exists anywhere in bundled data to positive-match against — see row 5); `curl ".../admin/audit?table=quotes"` (lowercase GET) matched the PascalCase-stored rows; `curl -X DELETE ".../admin/audit?table=quotes"` (lowercase) returned 204 and genuinely deleted the matching rows, confirmed via a follow-up GET showing only the purge sentinel remaining — this is the exact silent-no-op bug Finding B described |

---

## Notes

T1 and T2 both required per this project's blanket rule — this touches live query-parameter handling
and import-time matching.

Plan doc written 2026-07-25, prioritized ahead of #153 per developer direction, alongside #211.

**Scope expanded 2026-07-25, same day, per explicit developer direction** ("make sure you check all
string/text properties across all entities") — the original plan only audited the 4 queries the issue
text itself named. A full systematic audit (every entity, every string property, every WHERE/JOIN)
found 3 additional genuine bugs beyond the issue's own scope (`lang` query parameter — reachable on
nearly every read endpoint; `SystemAudit.TableName` — including a silent-no-op delete; and
`SystemChangeLog.EntityType`), plus one borderline finding (SQLite `LIKE`'s ASCII-only case-folding vs.
this project's Unicode-aware case-insensitivity everywhere else) that the developer decided to accept
as-is for now rather than fix here — tracked separately as
[#222](https://github.com/DutchJaFO/Quotinator/issues/222) in the v1.8.0 maintenance milestone. See
Step 2 for the full findings table.

**All code/doc work complete 2026-07-25** — Steps 3a–3d and 4 all Done, full test suite green
(Quotinator.Core.Tests 1118, Quotinator.Data.Tests 655, Quotinator.Api.Tests 511). T1 and T2 both
confirmed 2026-07-25 — every Verification checklist row is ✅.

**Red-green correction (2026-07-25)**: all four fixes (3a–3d) were originally implemented fix-first,
test-second — violating this project's red-green requirement for bug fixes. Caught when asked directly
whether the rule had been followed. Corrected retroactively rather than left as an unverified assumption:
reverted `Sql.cs` (Core and Data) to their pre-fix state (keeping the already-committed tests in place)
and reran the affected tests — `SeriesNameResolver_DifferingCasing_StillResolvesId`,
`UniverseNameResolver_DifferingCasing_StillResolvesId`, `PlanSeriesAsync_ExistingByName_DifferingCasing_NoActionStaged`,
`PlanUniverseAsync_ExistingByName_DifferingCasing_NoActionStaged`,
`PlanPeopleAsync_NoIdMatch_DifferingCasing_FallsBackToNaturalKey_NoActionStaged`,
`GetById_UppercaseLang_StillMatchesLowercaseStoredTranslation`,
`GetPagedAsync_LowercaseTableFilter_StillMatchesPascalCaseStoredRows`,
`ClearAsync_WithLowercaseTable_StillDeletesMatchingEntries`, and
`GetHistoryAsync_MixedCaseEntityType_StillMatches` all failed as expected, while every control test
(`ExactCasing`/`NoMatch`, and #180's pre-existing Sources natural-key test) correctly stayed green. For
`TryNormalizeLang` itself — brand-new API surface with no pre-fix equivalent to revert to — temporarily
stubbed out its lowercasing call instead; exactly the three `TryNormalizeLang_ValidCode_*` data rows
whose input actually changes case (`EN`, `En-Gb`, `ZH-HANS`) failed, while the already-lowercase `nl`
row and the null/invalid-code tests correctly stayed green. All files restored via `git checkout HEAD`
afterward and the full suite reconfirmed green, matching the already-committed state exactly (`git
status` showed no source diff).

**T2 finding, resolved as expected behaviour, not a bug (2026-07-25)**: while building a live fixture to
positive-match test the `lang=` fix, discovered that `QuoteSeedWriter.InsertTranslationsAsync` — the
only code that ever writes to `QuoteTranslations`/`SourceTranslations`/`CharacterTranslations` — has no
callers anywhere in the codebase. A quote's `translations` object is never persisted today, via either
the initial seed or the live `/import` path. Raised with the developer; confirmed this is expected —
translations are a prepared-but-not-yet-wired-up capability (the schema/model/read-path exist; the
import feature to actually populate them does not yet). Not a regression, not in scope for #216, and no
GitHub issue needed per the developer's explicit call. Documented here only so a future reader
encountering the same "why doesn't my translation import" question doesn't need to rediscover this.
