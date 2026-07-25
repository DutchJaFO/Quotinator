# #211 — Research: evaluate non-id string comparisons in SQL queries for the same class of gap as #210

**Status:** In progress
**GitHub issue:** #211
**Tiers required:** T1, T2 (revised 2026-07-25 — the investigation itself needs none, but Steps 5–8's
`TextClauses`/`SqlTextCaseGuard` implementation is real code, unlike #169's pure-research precedent)
**Depends on:** none — but its own findings substantially overlap with #216, which shipped first
(see Background)

---

## Question (from the GitHub issue)

Beyond id/PK/FK columns (already covered by `SqlIdCaseGuard`, #210), does any other string-typed
column comparison in `Sql.cs` (both `Quotinator.Core.Queries` and `Quotinator.Data.Queries`) or
`RepositorySql.cs` have an unaudited case-sensitivity or normalization gap — a value that can arrive
from outside the codebase (an HTTP query parameter, a request body, a file-authored import entry)
compared against a stored value without the two being guaranteed to agree on casing?

---

## Background

**This issue's own scope was substantially completed as a side effect of #216.** #216 was filed
separately (Series/Universe/People `Name` natural-key case-sensitivity — a distinct column class from
this issue's own enumerated list, which is entirely enum/status/discriminator-typed columns). But
during #216's implementation the developer explicitly widened its audit scope ("make sure you check
all string/text properties across all entities"), and that comprehensive audit found and fixed three
of the exact columns this issue's own investigation list names: `Language` (5 translation tables),
`SystemAudit.TableName`, and `SystemChangeLog.EntityType`. #216's plan doc's Step 2 findings table
should be read alongside this one — it is the primary source for those three fixes, not duplicated
here.

This plan doc's job is to close the remaining gap between #216's findings and #211's own full
investigation list, and to make the explicit decision #211's item 3 asks for (systemic guard vs.
case-by-case reasoning) — not to redo #216's work.

---

## Investigation steps

### 1. Enumerate every WHERE/JOIN comparison against a non-id string column

**Status:** Done.

Full sweep of `src/Quotinator.Core/Queries/Sql.cs`, `src/Quotinator.Data/Queries/Sql.cs`, and
`src/Quotinator.Data/Repositories/RepositorySql.cs` (independent re-verification, not reused from
#216's own summary) found every non-id string-column comparison in the codebase. None were missed by
#216 or the prior fixes it built on:

| Column | Location | Status |
|---|---|---|
| `*Translations.Language` (5 tables) | `Sql.Quotes.SelectBase`'s 3 JOINs, `Sql.SourceTranslations.CountForSource`, `Sql.StageDirections`/`Sql.SoundCues`' `SelectByIdWithTranslation` | ✅ Fixed — `LOWER()`-wrapped (#216) |
| `SystemAudit.TableName` | `BuildWhere`, `DeleteByTable` | ✅ Fixed — `LOWER()`-wrapped (#216) |
| `SystemChangeLog.EntityType` | `SelectByEntity` | ✅ Fixed — `LOWER()`-wrapped (#216) |
| `SystemImportActions.Status`/`.EntityType` | `BuildWhere` | ✅ Already safe — `UPPER()`-wrapped (#154, predates both #211 and #216) |
| `Sources.Title`/`.Type` | `SelectIdByTitleAndType`, `SelectExistingByTitleAndType` | ✅ Already safe — `LOWER()`-wrapped (#180, predates #211) |
| `Series`/`Universe`/`People.Name` | `SelectIdByName` (×3) | ✅ Fixed — `LOWER()`-wrapped (#216) |
| `Characters.SourceType` | `SelectGlobalCandidateId` | ✅ Enum-backed exemption (see Step 4) |
| `ImportBatches.Type` | `SelectByType` | ✅ Enum-backed exemption (see Step 4) — also currently has **zero callers anywhere in `src/`**, so not externally reachable at all today regardless |
| `SystemImportActions.ActionType` | Never appears in any WHERE clause — SELECT-only | ✅ No comparison exists to audit |
| `SystemChangeLog.Field`/`.Action` | Never appears in any WHERE clause — SELECT-only (`Field`), enum-backed (`Action`, see Step 4) | ✅ No gap — `Field` has no comparison to audit; `Action` is exempt anyway |
| `SystemAuditEntry.Operation`, `ImportBatch`'s other columns, every `CompletenessStatus` column, `DuplicateResolutionPolicy`/`ConflictPolicy` columns | Grepped for WHERE-clause usage | ✅ None found — write/SELECT-only everywhere |
| `Quotes.QuoteText`, `Sources.Title`, `Characters.Name`, `People.Name` (search/filter, via `LIKE`) | `Sql.SearchField.*`, `SqliteQuoteService.BuildFilterWhere`'s `character=`/`author=`/`source=` fuzzy filters | ⚠️ Known, deliberate exception — SQLite `LIKE`'s own ASCII-only Unicode case-folding, tracked separately as #222 (not a new finding, not this issue's scope) |
| `RepositorySql.cs` (entire file) | — | ✅ No string-column comparisons at all — every comparison goes through `IdClauses` against `Id`/FK columns |

**No additional gap found beyond what #216 (and the prior #154/#180 fixes it built on) already
closed.**

### 2. For each column: external origin, existing defence, cross-query consistency

**Status:** Done — folded into the table above and Step 4 below, per column, rather than a separate
pass.

**Revised finding (2026-07-25, corrected after developer feedback):** the original pass here only
checked whether each *individual* column is handled consistently across every query that touches
*that* column — true for all of them (`Status` is always `UPPER()`-wrapped everywhere it's compared;
`TableName` is always `LOWER()`-wrapped everywhere it's compared, etc.). But that isn't the only
inconsistency worth catching. A direct re-check across the whole column *class* (every non-id,
non-enum string column compared against an externally-supplied value) found a genuine drift:

| Column | Wrap direction | Why |
|---|---|---|
| `SystemImportActions.Status`/`.EntityType` | `UPPER(...)` | Fixed in #154, before ADR 012 later flipped the project's canonical case-folding direction from uppercase to lowercase for ids |
| `Sources.Title`/`.Type` | `LOWER(...)` | Fixed in #180, after ADR 012's flip |
| `Series`/`Universe`/`People.Name`, `SystemChangeLog.EntityType`, `SystemAudit.TableName`, translation `Language` columns | `LOWER(...)` | Fixed in #216, after ADR 012's flip |

Two different case-folding directions are in live use for functionally identical comparisons, purely
as an artifact of *when* each one happened to be fixed relative to ADR 012's direction change — not a
deliberate distinction. This is the same class of drift #210 found for id columns (self-consistent by
accident, until something exposes the inconsistency), just one level up: instead of the same column
being wrapped differently in different queries, different columns of the same conceptual kind are
wrapped in different directions. Every one of these was also hand-written inline (`LOWER(Title) =
LOWER(@title)` typed out at each call site) rather than going through a shared helper — unlike id
columns, which always go through `IdClauses.Equals`/`Join`/`SelectColumn`, giving ADR 012's own
direction flip a single choke point to update instead of requiring a manual find-and-fix pass across
every affected file (which is exactly what happened for ids historically, and exactly what this
column class would need if its own direction were ever revisited again).

### 3. Systemic guard vs. case-by-case reasoning

**Status:** Done. **Decision revised twice (2026-07-25) — both a shared construction-time helper AND
a test-time guard are warranted.** The first revision (after the developer's initial feedback) covered
only the construction half: the project should use "the same approach as with Id" — a shared helper
method that emits the comparison, mirroring `IdClauses.Equals(column, param)`, rather than continuing
to hand-write `LOWER(x) = LOWER(@y)` at each call site, so a future project-wide convention change
(like ADR 012's own uppercase→lowercase flip) touches one place instead of requiring a fresh audit of
every scattered inline comparison — precisely the gap Step 2 found already happened once, silently,
for this exact column class.

The original conclusion also claimed no guard was needed, reasoning that a mechanical scan analogous
to `SqlIdCaseGuard` would have nothing to check for enum-backed columns since the compiler prevents
misuse. **The developer corrected this directly in a second round: "a guard check for these columns
is needed as well. we know that enums will not be a string property so that is easy to identify."**
The guard's value isn't limited to enum-backed columns — it's the same regression-prevention role
`SqlIdCaseGuard` already plays for ids: nothing currently stops a future developer from adding a new
`Sql.cs` query with a hand-written, unwrapped `Name = @name` or `Status = @status` and never noticing,
the same way `SystemImportActions.Status`/`.EntityType` drifted onto `UPPER()` while everything fixed
after ADR 012's flip used `LOWER()` (Step 2) — a guard makes that a build-time failure instead of a
silent, discoverable-only-by-manual-audit gap. The "enums aren't strings" framing makes the guard's
column-discovery mechanism tractable without a hand-maintained registry (see Step 7) — it does not
mean enum-backed columns need no guard coverage at all; see Step 7's own further correction (`Status`
needs guard coverage despite being enum-backed on its entity, because its *query parameter* is raw
external text with no enum safety net, a different question from how the column itself is stored).

**Recommendation:** add a new helper, sibling to `IdClauses` rather than added to it (keeps `IdClauses`
scoped to what its name and ADR 012 actually govern — id columns specifically), covering the
non-id, non-enum string columns found in Step 1's table: `Sources.Title`/`.Type`, `Series`/`Universe`/
`People.Name`, `SystemChangeLog.EntityType`, `SystemAudit.TableName`, `SystemImportActions.Status`/
`.EntityType`, and the translation `Language` columns. A single `Equals(column, paramName)` method
emitting `LOWER(column) = LOWER(@paramName)` (matching the current, post-ADR-012 canonical direction)
is sufficient — none of these columns need the `Join`/`SelectColumn`/`In` counterparts `IdClauses`
also provides, because (unlike ids) their *write* side is always internally generated with already-
consistent casing (a fixed C# string literal, or an enum's own `.ToString()`) — only the *comparison*
side ever needs to tolerate an externally-supplied differently-cased filter value; no equivalent to
id columns' file-authored-casing-variance-at-capture problem exists here, so no read-time
`SELECT`-list presentation wrapping is needed either. Migrating the existing ~8 call sites onto the
new helper and flipping `SystemImportActions.Status`/`.EntityType` from `UPPER()` to `LOWER()` for
consistency is a mechanical, low-risk refactor — matching how `IdClauses` itself was retrofitted onto
every existing id comparison in #210. **Implemented directly within this issue (developer direction,
2026-07-25: "this is the issue in which we are fixing this. filing a new issue for this would be
pointless.") — not spun out separately.** See Steps 5–7 below for the implementation, and Outcome
tracking for why "new issue" no longer applies.

### 4. Enum-backed column exemption — confirmed, not assumed

**Status:** Done. Verified directly against entity source and call sites, not inferred from
CLAUDE.md's own claim that the rule "already" covers enums. **Two genuinely different mechanisms are
in play here, not one — conflating them was a real risk the developer flagged directly ("be careful
with our predefined enums as they have a specific JSON conversion"), so each is verified separately
below rather than grouped under one blanket "enum-backed, therefore safe" claim:**

**Mechanism A — compiler-enforced, `SafeValue<TEnum?>` on the entity itself.** `SystemImportActions
.ActionType` and `SystemChangeLog.Action` are `SafeValue<ImportActionKind?>`/`SafeValue<ChangeAction?>`
on their entities, read/written via `RegisterEnumHandler<TEnum>` — the C# type system itself prevents
an arbitrarily-cased value from ever being assigned to the property, at every current *and future*
call site. Both also happen to never appear in a WHERE clause regardless, so the exemption is moot for
them specifically today, but the mechanism is the strong one.

**Mechanism B — convention-enforced, plain `string` binding via a consistently-called `.ToString()`.**
`Characters.SourceType` is the weaker case: the `Character` C# entity has **no `SourceType` property
at all** (confirmed by reading `Character.cs` directly — only `Name`/`ImportBatchId`/
`CompletenessStatus`/`NoValueKnown`); the column exists only in the database schema and is written via
a raw parameterised string (`ImportActionPlanner.cs`'s `sourceTypeStr = q.Type.ToString()`, bound as a
plain `string` into `Sql.Characters.InsertIfNotExists`'s `@SourceType`). The later read/compare
(`Sql.Characters.SelectGlobalCandidateId`'s `c.SourceType = @sourceType`) uses the identical
`.ToString()`-derived value. This is genuinely safe **today**, confirmed by tracing both the write and
read call sites to the same `sourceTypeStr` construction — but it is a discipline the code currently
happens to follow correctly everywhere, not a guarantee the compiler enforces the way Mechanism A's
`SafeValue<TEnum?>` does. A future call site that binds `Characters.SourceType` from a differently-
constructed string (not routed through `.ToString()` on the same enum) would silently reintroduce the
gap, with nothing catching it. `ImportBatches.Type` and the `type[]`/`genre[]` quote-filter parameters
(`NormaliseType`/`NormaliseGenre` in `SqliteQuoteService.BuildFilterWhere`) are the same Mechanism B
pattern — always round-tripped through `Enum.TryParse(ignoreCase: true)` + `.ToString()`, or (for
genres) `InputValidation.GenreApiToDb`'s `StringComparer.OrdinalIgnoreCase` dictionary — safe today by
the same convention-not-compiler argument.

**The specific caution the developer raised — verified, not assumed:** `.ToString()` on a plain C#
enum returns exactly the declared member name, independent of any `JsonConverter` — but only if the
enum has no custom per-member name mapping that would make its JSON wire form diverge from that
declared name. Checked `QuoteType` (`src/Quotinator.Core/Models/QuoteType.cs`) directly: no
`[JsonPropertyName]`-equivalent or `EnumMember`-style override on any value (`Unknown`, `Movie`, `Tv`,
`Anime`, `Book`, `Person`) — `.ToString()` and the JSON wire value are identical for every member. This
must be re-checked for any *other* enum a future Mechanism-B-style comparison is built against; it is
not a property that holds for enums in general, only confirmed here for `QuoteType` specifically.

**Conclusion:** CLAUDE.md's existing rule is confirmed actually applied for every enum-backed column
this investigation touched, but "enum-backed" is not a single guarantee — Mechanism A needs no further
action; Mechanism B is correct today and should be called out with a code comment (already true for
`Characters.SourceType`'s ADR 013 remarks) so a future change to that call site doesn't silently
reintroduce the gap, and any new enum used the same way must be checked for member-name overrides
before being trusted, the same way `QuoteType` was checked here.

### 5. Build `TextClauses` — the shared helper

**Status:** Done. Implemented exactly as designed — `src/Quotinator.Data/Queries/TextClauses.cs`,
one `Equals(column, paramName)` method.

Add `Quotinator.Data.Queries.TextClauses` (same namespace and file location style as `IdClauses` —
`src/Quotinator.Data/Queries/TextClauses.cs`), scoped explicitly to non-id, non-enum text columns so
it doesn't get confused with or folded into `IdClauses` (different governing convention — ADR 012
covers ids specifically):

```csharp
public static class TextClauses
{
    /// <summary><c>LOWER(column) = LOWER(@paramName)</c> — case-insensitive comparison for a
    /// non-id text column (Name/Title natural keys, Status/EntityType/TableName discriminators,
    /// Language codes). Not for id columns — use IdClauses for those.</summary>
    public static string Equals(string column, string paramName)
        => $"LOWER({column}) = LOWER(@{paramName})";
}
```

One method only — `Join`/`In`/`SelectColumn` counterparts are not needed, per Step 3's reasoning
(write side already consistent; no presentation-normalization problem exists for this column class).

### 6. Migrate every existing call site onto `TextClauses.Equals`

**Status:** Done. All ~11 call sites migrated (one more than the original ~8 estimate — the 3
translation JOINs in `Sql.Quotes.SelectBase` are 3 separate call sites, not one). Full suite
confirmed green after migration (no behaviour change), before the guard was even added.

Replace the hand-written comparisons with `TextClauses.Equals(...)` calls, and flip
`SystemImportActions.Status`/`.EntityType` from `UPPER()` to `LOWER()` in the same pass for
consistency with every other column in the class:

- `Sql.Sources.SelectIdByTitleAndType` / `SelectExistingByTitleAndType` (`Title`, `Type`)
- `Sql.Series.SelectIdByName`, `Sql.Universe.SelectIdByName`, `Sql.People.SelectIdByName` (`Name`)
- `Sql.SystemChangeLog.SelectByEntity` (`EntityType`)
- `Sql.SystemAudit.BuildWhere` / `DeleteByTable` (`TableName`)
- `Sql.SystemImportActions.BuildWhere` (`Status`, `EntityType`) — direction flip, not just a rewrite
- `Sql.Quotes.SelectBase`'s 3 translation JOINs, `Sql.SourceTranslations.CountForSource`,
  `Sql.StageDirections`/`Sql.SoundCues.SelectByIdWithTranslation` (`Language`)

No behaviour change for any already-`LOWER()`-wrapped comparison — purely a construction-time
consolidation. The `SystemImportActions.Status`/`.EntityType` flip is the only one with a real
(cosmetic-only) SQL text change, and needs its existing tests re-run to confirm no regression
(`?status=pending`/`?entityType=` mixed-case tests from #154 should still pass unchanged, since
`LOWER()` and `UPPER()` produce the same match result — only the losing/winning side of the
comparison changes, not which rows match).

### 7. Build `SqlTextCaseGuard` (developer direction, 2026-07-25: a guard check is needed too)

**Status:** Done. Implemented as designed, plus one real bug found and fixed during verification:
the first version false-positived on every `UPDATE ... SET Column = @param` assignment (9 real
queries in the codebase), since a generic `column = @param` regex can't tell a write-side assignment
from a read-side comparison without help — fixed by porting `SqlIdCaseGuard`'s own
`StripUpdateSetClause` technique, which the first draft of this guard forgot to mirror. Confirmed via
a genuine retroactive red-green check (not assumed): reverted `Series.SelectIdByName` back to
`Name = @name` with the guard's own code left in place — `SqlConstant_PassesTextCaseGuard` failed
exactly as expected, then passed again once restored.

**Design**, using the developer's own identification approach ("enums will not be a string
property so that is easy to identify") — reflection over `[Table(...)]`-decorated entity classes
(the same marker every entity in `Quotinator.Core.Entities`/`Quotinator.Data.Entities` already
carries), collecting every public property whose *declared C# type* is exactly `string`/`string?`
(a `SafeValue<TEnum?>`-typed property is a different .NET type entirely and is skipped by this check
automatically — no enum-specific logic needed) and whose name doesn't end in `Id` (governed by
`SqlIdCaseGuard`/`SqlSelectPresentationGuard` instead). This avoids a hand-maintained column registry
the same way both sibling guards already do (`SqlSelectPresentationGuard`'s own remarks document why
an earlier hand-maintained-registry version of that guard was replaced).

**A real limitation found while designing this, not assumed away**: reflecting on entity *storage*
type only catches half the risk. `SystemImportActions.Status` is `SafeValue<ImportActionStatus?>` on
its entity (correctly enum-backed for storage) — reflection alone would skip it. But its query
parameter (`SystemImportActionReader.GetPagedAsync(string? status, ...)`) is a raw external string
with **no enum round-trip at all** before reaching `Sql.SystemImportActions.BuildWhere`'s
`@status` — unlike `Characters.SourceType`/`ImportBatches.Type`/the `type[]`/`genre[]` filters (Step
4's Mechanism B), where the bound parameter is always `.ToString()`'d from a parsed enum first. The
entity's own storage type says nothing about whether its *filter parameter* was ever validated against
that same enum. `Status` needs the wrap (already being added in Step 6) precisely because of this,
and needs to be added to the guard's coverage **explicitly** — reflection cannot discover it, since
there is nothing string-typed on the entity for reflection to find. `Sql.SystemImportActions
.EntityType` does not have this problem (it's a genuine `string` on `SystemImportAction`, so reflection
finds it directly).

`Characters.SourceType` is the mirror case discussed in Step 4 (no entity property at all) but does
**not** need a guard entry or a wrap — verified safe because the bound parameter is always
`.ToString()`'d from an already-parsed `QuoteType` enum *before* it ever reaches this specific query,
unlike `Status`'s genuinely-raw, unvalidated external input.

**Implementation**: `Quotinator.Data.Diagnostics.SqlTextCaseGuard`, mirroring `SqlIdCaseGuard`'s shape
but with the column-name set supplied by the caller instead of baked into the regex as a name-suffix
pattern (a dynamic column-name alternation can't be a compile-time-constant `[GeneratedRegex]`
pattern, unlike `\w*Id`):

- `DiscoverTextColumnNames(params Type[] entityTypes)` — the reflection step above, returning a
  `HashSet<string>`. Takes `Type[]` generically so this method itself never references any specific
  entity type and stays in `Quotinator.Data` without violating ADR 004 — each test project supplies
  its own locally-relevant entity types (Core.Tests passes Core's entities for Core's `Sql.cs`;
  Data.Tests passes Data's entities for Data's `Sql.cs`/`RepositorySql.cs`).
- A small, explicitly-justified `AdditionalColumnNames` array (mirroring `SqlSelectPresentationGuard
  .ExemptColumnNames`'s own precedent, just additive instead of subtractive) — one entry, `Status`,
  with the reasoning above as its comment.
- `FindViolations(string sql, IReadOnlyCollection<string> knownTextColumnNames)` — a generic
  "bare identifier `=`/`IN` a bound parameter, not already `LOWER()`-wrapped" regex (no `*Id` suffix
  requirement, unlike `SqlIdCaseGuard`), filtered down to only the supplied column names. No
  column-to-column (JOIN) variant needed — confirmed via Step 1 that no Name/Title/Status-class column
  is ever compared to another column, only to a bound parameter.

Wired into the same `SqlQueryGuardTests`/`RepositorySqlGuardTests` `DynamicData` enumeration the
other two guards already use, in both `Quotinator.Core.Tests` and `Quotinator.Data.Tests`, following
the established `SqlConstant_Passes*Guard`/`AssembledQuery_Passes*Guard` naming pattern.

### 8. Tests and documentation

**Status:** Done. `TextClausesTests.cs` (2 tests), `SqlTextCaseGuardTests.cs` (16 tests covering
`FindViolations` and `DiscoverTextColumnNames`), plus the `DynamicData`-driven
`SqlConstant_PassesTextCaseGuard`/`AssembledQuery_PassesTextCaseGuard` wired into both
`Quotinator.Core.Tests` and `Quotinator.Data.Tests`' own `SqlQueryGuardTests`, and
`RepositorySqlFactory_PassesTextCaseGuard` in `RepositorySqlGuardTests` (currently vacuous —
`RepositorySql.cs` has no non-id text comparisons today — added for the same reason the other two
guards are already wired there: automatic coverage the moment a future generic name-based lookup is
added). CLAUDE.md's case-insensitivity section extended with `TextClauses`/`SqlTextCaseGuard`
paragraphs; `docs/database-conventions.md`'s cross-reference needed no change (heading text
unchanged this time). Full solution suite: Core.Tests 1304 (was 1118), Data.Tests 751 (was 655),
Api.Tests 511 (unchanged) — all green, 0 warnings, 0 errors.

- Add unit tests for `TextClauses.Equals` and `SqlTextCaseGuard` itself (a synthetic violation case
  and a synthetic clean case, mirroring how `SqlIdCaseGuard`/`SqlSelectPresentationGuard` are tested
  directly, not only through the codebase-wide `DynamicData` scan).
- No new *behavioural* tests needed beyond what already exists per column (#154/#180/#216's own tests
  already prove each comparison matches case-insensitively) — re-running the full suite after the
  migration is the regression check for behaviour, not new test-writing; the guard tests above are
  new coverage for the *mechanism*, not the behaviour.
- Update CLAUDE.md's "GUID/enum/id/Name/Title comparisons are case-insensitive by default" section
  (from #216) to mention `TextClauses.Equals`/`SqlTextCaseGuard` as the construction-time helper and
  guard for this column class, alongside `IdClauses`/`SqlIdCaseGuard` for ids — closing the exact gap
  Step 2/3 found (hand-written comparisons with no shared choke point, and now a guard to catch a
  future regression the same way the id-column guards already do).
- Update `docs/database-conventions.md`'s cross-reference alongside CLAUDE.md's.

---

## Outcome tracking

| Possible outcome | Applies? | Notes |
|---|---|---|
| New issues in the current milestone | **No** (revised 2026-07-25, developer direction: "this is the issue in which we are fixing this. filing a new issue for this would be pointless.") | The shared-helper and guard work is implemented directly within #211 itself (Steps 5–8 above), not spun out to a separate issue. |
| New milestone | No | Not applicable. |
| Not feasible / rejected | No | Not applicable. |
| Architecture decision required | No | Step 3's recommendation is a direct extension of ADR 012's own existing "case-insensitive by default" convention to a sibling column class, not a new architectural fork — no separate ADR needed; CLAUDE.md/`docs/database-conventions.md` get a cross-reference update instead (Step 8). |

No genuine case-sensitivity *bug* remains after #216 — but the investigation surfaced a real
maintainability gap (Step 2's cross-column direction drift) that #216 itself didn't fix and #211's own
question was well-positioned to catch, since it asks about this exact column class specifically. Fixed
directly within this issue (Steps 5–8), not filed separately.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Investigation complete, no residual case-sensitivity gap beyond #216 | Live (review) | Steps 1–4, independently re-verified via a background agent sweep of both `Sql.cs` files and `RepositorySql.cs` |
| 2 | ✅ | `TextClauses.Equals` implemented | Unit test | `TextClausesTests.Equals_WrapsBothColumnAndParamInLower`/`Equals_AliasedColumn_WrapsColumnInLower` |
| 3 | ✅ | Every hand-written comparison migrated onto `TextClauses.Equals`; `SystemImportActions.Status`/`.EntityType` flipped `UPPER()`→`LOWER()` | Unit test | Full suite green post-migration before the guard even existed — no behaviour change |
| 4 | ✅ | `SqlTextCaseGuard` correctly flags an unwrapped text-column comparison and does not false-positive on `UPDATE ... SET` assignments, protected comparisons, or out-of-scope columns | Unit test | `SqlTextCaseGuardTests.cs` (16 tests) |
| 5 | ✅ | `SqlTextCaseGuard.DiscoverTextColumnNames` includes plain string properties, excludes `*Id`-suffixed and enum-backed ones | Unit test | `DiscoverTextColumnNames_PlainStringProperties_AreIncluded`/`_IdSuffixedProperty_IsExcluded`/`_NonStringProperties_AreExcluded` |
| 6 | ✅ | Guard wired into the codebase-wide `DynamicData` scan in both `Quotinator.Core.Tests` and `Quotinator.Data.Tests`, plus `RepositorySqlGuardTests` | Unit test | `SqlConstant_PassesTextCaseGuard`/`AssembledQuery_PassesTextCaseGuard` (both projects), `RepositorySqlFactory_PassesTextCaseGuard` |
| 7 | ✅ | Guard genuinely catches a real regression, not just synthetic test strings | Live (review) | Retroactive red-green: reverted `Series.SelectIdByName` to `Name = @name`, confirmed `SqlConstant_PassesTextCaseGuard` failed, restored, confirmed it passed again |
| 8 | ✅ | CLAUDE.md and `docs/database-conventions.md` updated | Live (review) | See Step 8 |
| 9 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — Core.Tests 1304, Data.Tests 751, Api.Tests 511, all green, 0 warnings, 0 errors |
| 10 | ❌ | T1 — app starts in Visual Studio | Live (T1) | Developer to confirm in Visual Studio |
| 11 | ❌ | T2 — Docker smoke test | Live (T2) | Not yet run |

---

## Notes

T1/T2 required (revised 2026-07-25) — Steps 5–8 are real code, not pure research (see header).

**Next steps:**
1. ~~Implement Steps 5–8~~ Done.
2. Post the findings above as a comment on #211 once implementation is done (draft the comment text,
   present it in chat, wait for approval per this project's draft-then-review rule, then post).
3. Tick #211's own Definition of Done checkboxes.
4. T1/T2, then close #211 once both pass — same flow as #216.

Written 2026-07-25, after #216 shipped (Waiting for release). Investigation is complete as of this
writing — independently re-verified rather than assumed from #216's own summary, per this project's
"verify, don't assume" standard. Step 3's conclusion was revised the same day after direct developer
feedback ("we should not blindly wrap in UPPER()/LOWER(). we should use the same approach as with Id
... be careful with our predefined enums as they have a specific JSON conversion") — the first draft
of this plan doc concluded no further work was needed; both the cross-column drift (Step 2) and the
Mechanism A/B enum distinction (Step 4) were found only after that feedback prompted a second, more
careful pass, not in the original investigation. A second correction followed immediately: the first
revision proposed the `TextClauses` work as a separate follow-up issue, which the developer rejected
directly ("this is the issue in which we are fixing this. filing a new issue for this would be
pointless.") — Steps 5–7 (now 5–8) and the Outcome table were updated to implement within #211 itself.
A third correction followed the same day: the developer directly reversed Step 3's "no guard needed"
conclusion ("a guard check for these columns is needed as well. we know that enums will not be a
string property so that is easy to identify") — Step 7 (`SqlTextCaseGuard`) was added, along with the
`Status`-specific nuance the reflection-only approach would otherwise have missed silently.
