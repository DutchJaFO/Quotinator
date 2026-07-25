# #216 — Series/Universe name-filter case sensitivity (confirmed bug) + audit all Name/Title natural-key comparisons

**Status:** Planning
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

**Status:** Not started. (Renumbered from the original Step 1 — kept as its own step since it's the
issue's own confirmed, named bug.)

Wrap all three as `WHERE LOWER(Name) = LOWER(@name) AND IsDeleted = 0`, matching `IdClauses`-style
wrapping and #180's own `Sources.SelectIdByTitleAndType` precedent exactly. Fixes items 1 and 3 of the
original spec simultaneously for these three entities, per Background.

### 3b. Fix `lang` query-parameter case-sensitivity (Finding A)

**Status:** Not started.

Recommend normalizing `lang` to lowercase **once, centrally** (e.g. at the point every endpoint reads
the raw query-string value, or inside a single shared helper), rather than wrapping 6 separate SQL
`Language = @lang` fragments individually — cheaper, and matches this project's own preference for one
choke point over N scattered fixes (the same reasoning `GuidExtensions.ToCanonicalId()` and
`IdClauses` were built around). Confirm during implementation whether `LOWER()`-wrapping the SQL side
is still warranted as defense-in-depth even after centralizing the input normalization.

### 3c. Fix `SystemAudit.TableName` case-sensitivity (Finding B)

**Status:** Not started.

Wrap both `BuildWhere` and `DeleteByTable` as `LOWER(TableName) = LOWER(@table)`. The DELETE endpoint's
silent-no-op failure mode makes this a genuine correctness bug, not just a query-filter miss.

### 3d. Fix `SystemChangeLog.EntityType` case-sensitivity (Finding C)

**Status:** Not started.

Wrap `SelectByEntity`'s `EntityType = @entityType` the same way its own `EntityId` column on the same
query already is. No live endpoint exercises this today, so no regression risk beyond the unit-test
level — cheap, proactive fix while the pattern is already documented here.

### 4. Update CLAUDE.md

**Status:** Not started.

Extend the existing "GUID/enum/id comparisons are case-insensitive by default" section (or add an
adjacent one) to explicitly state Name/Title natural-key columns are covered by the same rule, and
document the `lang`/`TableName`/`EntityType` findings as further recurrences of the same pattern,
citing #216 and #180 as precedent, matching how the section already cites #154/#69/#180/#175. Also
note the LIKE/Unicode borderline finding was deliberately left unfixed here — accepted as-is per
developer decision, with the actual fix tracked separately as #222.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `series=`/`universe=` query filters match case-insensitively | Unit test | `QuoteEndpointsTests.GetRandom_MixedCaseSeriesFilter_MatchesCaseInsensitively` (or `/search`/`GetAll`) |
| 2 | ❌ | `Sql.Series.SelectIdByName`/`Sql.Universe.SelectIdByName` match case-insensitively at the SQL level | Unit test | `SelectIdByName_MixedCaseInput_MatchesExistingRow` (Series and Universe) |
| 3 | ❌ | `Sql.People.SelectIdByName` matches case-insensitively | Unit test | Equivalent test for People |
| 4 | ✅ | Comprehensive audit of every string/text property across every entity, not just the issue's 4 named queries | Live (review) | Done — see Step 2's findings table (Findings A/B/C + the LIKE/Unicode borderline case) |
| 5 | ❌ | `?lang=` case mismatch no longer silently falls back to the original language (Finding A) | Unit test + Live | New test on at least one translated-content endpoint; T2 `curl` with uppercase `lang=` against a known-translated quote |
| 6 | ❌ | `GET`/`DELETE /admin/audit?table=` match case-insensitively (Finding B) | Unit test | New test asserting a lowercase `table=` filters/deletes the same as the stored PascalCase value |
| 7 | ❌ | `SystemChangeLog.SelectByEntity`'s `EntityType` matches case-insensitively (Finding C) | Unit test | New test at the reader level (no live endpoint exists yet to test through) |
| 8 | ✅ | LIKE-based free-text search's ASCII-only case-folding either fixed for full Unicode parity or explicitly accepted as-is | Live (review) | Developer decision recorded 2026-07-25: accepted as-is for now (no translations currently exercise it); follow-up fix tracked separately as [#222](https://github.com/DutchJaFO/Quotinator/issues/222) in the v1.8.0 maintenance milestone |
| 9 | ❌ | CLAUDE.md updated | Live (review) | Manual diff review |
| 10 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 11 | ❌ | T1 — app starts in Visual Studio, mixed-case series/universe filter matches | Live (T1) | Developer to confirm in Visual Studio |
| 12 | ❌ | T2 — Docker smoke test: mixed-case `series=`/`universe=` filter, mixed-case `lang=`, mixed-case `table=` all resolve correctly against bundled data | Live (T2) | `docker build` + `curl "http://localhost:8080/api/v1/quotes/random?series=original%20trilogy"` (lowercase, against #181's now-correctly-cased "Original Trilogy") + `curl ".../quotes/{id}?lang=NL"` + `curl "http://localhost:8080/api/v1/admin/audit?table=quotes"` |

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
