# #210 — Canonicalize Quotes.Id at capture, case-insensitive lookup

**Status:** Waiting for release
**GitHub issue:** #210
**Tiers required:** T1, T2
**Depends on:** none (parent tracking issue #207; shares `EntityIdCanonicalizer` with sibling sub-issue #209, which landed first)

## Scope expansion

While implementing this issue, the developer asked to also audit whether audit-log and other
non-masterdata endpoints had the same id-casing gap. That audit found several more case-sensitive
id comparisons beyond Quotes.Id, and the developer's explicit direction was: fix every one found,
and build a permanent, automated guard so this class of regression can never reappear — not a
one-time manual pass. This substantially widened the issue's scope beyond its original spec
requirements 1-4 below. The added work:

- A new systemic guard, `SqlIdCaseGuard` (`src/Quotinator.Data/Diagnostics/SqlIdCaseGuard.cs`),
  structurally mirroring the existing CVE-2025-6965 `SqlAggregateGuard` — a regex-based static
  analyzer that flags any comparison between an id-named column and a bound parameter that isn't
  wrapped `UPPER(...) = UPPER(...)` on both sides (or, for an `IN`/`NOT IN` clause, at least the
  column side). It distinguishes bare/prefixed/aliased/bracket-quoted columns and half-protected
  wraps (only one side wrapped — still flagged) from fully-protected ones, and `UPDATE ... SET`
  assignments (write-side, out of scope) from `WHERE` comparisons (read-side, in scope).
- Wired into three existing guard-test files via their existing `DynamicData` enumeration methods
  (no duplicated enumeration logic, matching how `SqlAggregateGuard` itself is already wired):
  `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs`,
  `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs`,
  `tests/Quotinator.Data.Tests/Repositories/RepositorySqlGuardTests.cs`.
- Every violation the guard found was fixed with `UPPER()` wrapping: 34 in
  `src/Quotinator.Core/Queries/Sql.cs` (Quotes, QuoteGenres, QuoteTranslations, SourceTranslations,
  Characters, CharacterSources, Sources, Series, Universe, Conversations, ConversationLines,
  StageDirections, StageDirectionTranslations, SoundCues, SoundCueTranslations), 5 in
  `src/Quotinator.Data/Queries/Sql.cs` (ImportBatches.UpdateRecordCount, SystemAudit.BuildWhere —
  the one genuinely live bug, `GET /admin/audit?recordId=` — SystemImportActions.MarkDecided/
  ClearDecision/MarkApplied, SystemChangeLog.SelectByEntity), 7 in
  `src/Quotinator.Data/Repositories/RepositorySql.cs` (the generic repository layer used by every
  entity, including Quote), plus `SqliteQuoteService.BuildFilterWhere`'s dynamic `seriesId`/
  `universeId` filter clauses (a genuinely new finding neither the manual audit nor the background
  research agent had caught — found only because the automated guard scanned the real assembled
  query, not a manually-curated inventory).
- Most of these fixes are defense-in-depth, not fixes to an actively-reachable bug — e.g.
  `SourceSeriesReferenceReader`/`SeriesUniverseReferenceReader`/`CharacterSourceLinkReader` already
  canonicalize their C#-side parameters correctly today (manually, or via `GuidHandler`'s automatic
  forcing on `Guid`-typed parameters). The point, per the developer's explicit instruction, is to
  never rely on "this happens to be safe today because the known caller already does the right
  thing" — the SQL itself must be safe regardless of what any future caller does.
- **Second round of scope expansion**: the developer then suggested going further — instead of only
  catching a wrong comparison after the fact, build helper methods that construct the correct
  comparison in the first place. Added `Quotinator.Data.Queries.IdClauses`
  (`Equals`/`In`/`NotIn`/`Join`), and rewrote every fixed query and factory method across both
  `Sql.cs` files and `RepositorySql.cs` to call it instead of hand-typing `UPPER(...)`. This required:
  - Converting every affected `const string` query to `static readonly string` (a method call isn't
    a compile-time constant), which surfaced a second, independent bug: the guard-test reflection
    (`EnumerateSqlConstants` in both `SqlQueryGuardTests.cs` files) only ever called `GetFields`, so
    it silently covered `const`/`static readonly` fields but not `static string` properties. Widened
    to also call `GetProperties`, which immediately found a real, previously-invisible bug:
    `Sql.SystemImportActions.SelectById` (`Quotinator.Data.Queries.Sql`) was declared as a property
    with an unwrapped `WHERE Id = @id`, used live by `SystemImportActionReader.GetByIdAsync`. Fixed,
    with a dedicated regression test.
  - The developer decided (asked explicitly, given ADR 012 originally argued JOINs don't need
    wrapping) that `IdClauses.Join` should wrap both sides too, reversing that stance — defense in
    depth outweighs the (negligible, at this project's scale) cost. Every existing JOIN condition and
    correlated-subquery predicate between two id columns was rewritten to call it, and
    `SqlIdCaseGuard` itself was extended with `JoinComparisonPattern`/`ProtectedJoinPattern` so an
    unwrapped join is now a guard-test failure too, not just a JOIN-to-parameter comparison.
  - Found and fixed one more live gap while touching `SqliteQuoteService`: `q.Id NOT IN
    @excludedIds` (the `/random` dedup exclusion clause) had no `UPPER()` wrapping at all, and — a
    third guard blind spot — the original `IdComparisonPattern` regex only recognised `=`/`IN`, not
    `NOT IN`, so this was invisible to the guard too. `IdClauses.NotIn` was added and the regex fixed.
  - The developer separately asked (2026-07-20, after seeing this pass surface the `EntityType`/
    `Status` string comparisons in `SystemImportActions.BuildWhere`) whether *non-id* string
    comparisons might have the same class of gap. That is out of scope for this issue by design —
    filed separately as
    [#211](https://github.com/DutchJaFO/Quotinator/issues/211) (research issue, `data-import-sources`
    milestone) rather than folded in here, since it is a distinct question (which columns, not just
    ids, need this treatment) that needs its own investigation before any implementation is planned.

See ADR 012 for the resulting policy statement — both the read-side guard and the `IdClauses`
construction helper are documented there, along with the reversed JOIN-wrapping stance.

---

## Spec requirements

1. Add the lowercase throwing/non-throwing forms (`CanonicalizeLowercase`/`TryCanonicalizeLowercase`) to
   `Quotinator.Data.Helpers.EntityIdCanonicalizer` — sibling sub-issue #209 adds the uppercase forms to
   the same class; whichever lands first creates the file with its own half.
2. Canonicalize a file-authored `SourceQuote.Id` to lowercase (matching `QuoteIdentity.StableId`'s own
   pinned, must-never-change lowercase convention) at the single earliest capture point in
   `ImportActionPlanner.PlanAsync`'s quote loop.
3. Audit and `UPPER()`-wrap every `Quotes.Id`/`QuoteId`-matching query that compares against a
   caller-or-file-supplied value, matching the same case-insensitive-by-default policy this project
   already applies to every other id column (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
   by default"). `Sql.Quotes.SelectById()` — the query behind `GET /api/v1/quotes/{id}` — currently has
   **no** case-insensitive wrapping at all, unlike Source/People's already-partially-mitigated
   equivalents; this is a live-user-facing gap, not just an internal-consistency one.
4. Audit every place a `Quotes.Id`-derived value is bound as a `Guid`-typed Dapper parameter (which
   `GuidHandler` force-uppercases) versus a plain `string` parameter (which doesn't) — a mismatch
   between the two for the *same* logical id is its own, independent source of inconsistency, separate
   from whatever casing the original file used.

---

## Background — why this issue exists

Found while planning sibling sub-issue #209 (Source/Person/StageDirection/SoundCue/Conversation
canonicalization — see #207, the parent tracking issue): a file-authored `Quotes.Id` has the identical
capture-time gap as Source/Person, confirmed by tracing the actual code, not assumed:

- `ImportActionPlanner.PlanAsync`'s quote loop uses `q.Id` directly and unconditionally
  (`seenQuotes[q.Id] = q;` line 87, `EntityId = q.Id` at lines 102/144/164) — never canonicalized.
- `SqliteQuoteService.GetById(string id, ...)` (line 50) binds `id` as a **plain `string`** parameter
  into `Sql.Quotes.SelectById()`, whose WHERE clause is `q.Id = @id` with **no `UPPER()` wrapping at
  all** (`Sql.cs` line 92-93) — confirmed by direct read, not inferred from the Source/People pattern.
  This means `GET /api/v1/quotes/{id}` today only succeeds if the URL's casing happens to exactly
  match however that specific quote's id was originally typed in its source file — worse than
  Source/People's situation, which at least has partial `UPPER()` read-side tolerance.
- `QuoteIdentity.StableId` (the auto-generated fallback, used only when a quote entry omits its own
  `id`) is deliberately, permanently lowercase — pinned by "a production-data regression test" per
  `EntityIdentity.cs`'s own comment. But that pin only governs the *auto-generated* path; a
  file-authored `id` (the overwhelmingly common case — nearly every quote entry in this project's own
  bundled/curated data supplies one) is never checked or conformed against it. So `Quotes.Id` is not
  actually the consistent lowercase convention the `QuoteIdentity.StableId` comment implies — it is, in
  practice, "whatever casing each import happened to use," exactly Source/Person's bug, just targeting
  lowercase instead of uppercase, and with a weaker read-side mitigation (none) than Source/People have.
- A further, independent inconsistency was found alongside this (not fully resolved during planning,
  flagged for the implementation step to investigate and enumerate all instances of, not designed to a
  specific fix here): some call sites bind a quote id as a `Guid`-typed parameter (e.g.
  `QuoteSeedWriter.InsertGenresAsync(connection, resolved, Guid.Parse(resolved.Id), ...)` —
  `GuidHandler` force-uppercases this), while others (`Sql.Quotes.Insert`'s own `Id = resolved.Id`)
  bind the same logical value as a plain `string` (no forcing). Two call sites touching the *same*
  quote's id can therefore each apply a *different* casing transform to it, independent of whatever the
  source file originally said.

---

## Approach

### `EntityIdCanonicalizer` — lowercase forms

`Guid.ToString("D")` is already lowercase by .NET's own default, so the lowercase form needs no explicit
transform, mirroring exactly how `QuoteIdentity.StableId` itself produces its output today:

```csharp
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeLowercase(string rawId) => Guid.Parse(rawId).ToString("D");

    public static bool TryCanonicalizeLowercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D"); return true; }
        canonical = null;
        return false;
    }
}
```

If sub-issue #209 has not landed yet, this sub-issue creates the file with only these two methods; #209
then adds its uppercase forms alongside them (or vice versa, whichever lands first).

### `PlanAsync`'s quote loop — capture-point fix (lowercase target)

Same shape as Source/Person's fix in #209, targeting `CanonicalizeLowercase` instead:

```csharp
foreach (var q in quotes)
{
    var canonicalQuoteId = EntityIdCanonicalizer.TryCanonicalizeLowercase(q.Id, out var canonical) ? canonical! : q.Id;
    // every later reference to q.Id in this iteration (seenQuotes[q.Id], EntityId = q.Id, resolved.Id,
    // and the SourceQuote instances threaded through QuoteFieldMerge) uses canonicalQuoteId instead.
```

`SourceQuote` is a `record`/init-only type — the exact mechanics of substituting `canonicalQuoteId` back
onto `q` (a `with` expression producing a corrected copy vs. threading the canonical string alongside `q`
through the rest of the loop) are an implementation-time detail, not a design question; whichever reads
more clearly once the surrounding loop body is in front of the implementer.

### Query audit — every `Quotes.Id`/`QuoteId`-matching site

Full inventory from a direct grep of `Sql.cs` (not exhaustive design of each fix here — the exact wrap
is mechanical once the inventory is confirmed correct; recorded so the implementation step has a checked
starting list rather than re-deriving it):

| Location | Current | Needs `UPPER()`? |
|---|---|---|
| `Quotes.SelectById()` (~line 92) | `WHERE q.Id = @id` | **Yes** — the live, user-facing `GET /quotes/{id}` gap |
| `Quotes.SelectRawById()` (~line 118) | `WHERE q.Id = @id` | Yes — internal merge/conflict-resolution use, same defense-in-depth precedent as Sources/People's internally-used-but-still-wrapped siblings |
| `Quotes.SelectCompletenessById`/`UpdateCompletenessById` (lines 33, 37) | `WHERE Id = @id` | Yes, matching Sources/People's already-wrapped siblings |
| `Quotes.UpdateOnNewestWins` (lines 43-45) | `WHERE Id=@id` | Yes |
| `QuoteGenres.DeleteForQuote`/`LoadForQuote`/the two Insert variants (lines 157-171) | `WHERE QuoteId = @id` / `@QuoteId` | Yes |
| `QuoteTranslations.DeleteForQuote`/Insert (lines 178-183) | `WHERE QuoteId = @id` | Yes |
| `ConversationLines`'s two quote-referencing queries (lines 536, 551, 555) | `WHERE cl.QuoteId = @quoteId` etc. | Yes |

Queries already confirmed correctly wrapped and needing no change: `Characters.CountActiveReferences`-
style siblings that reference `PersonId`/`SourceId` (lines 296, 363) — already `UPPER()`-wrapped from
prior work; not `QuoteId`-related, listed only to confirm they were checked, not missed.

`SelectBase`'s own internal JOINs (`Quotes` → `Sources`/`Characters`/`People`/translations/`Series`/
`Universe`) are FK joins between two internally-computed values, not a caller-supplied comparison — once
the capture-point fix makes both sides consistently canonical, these need no wrapping.

---

## Steps

### 1. `EntityIdCanonicalizer` lowercase forms + tests

**Status:** Done. `src/Quotinator.Data/Helpers/EntityIdCanonicalizer.cs` extended with
`CanonicalizeLowercase`/`TryCanonicalizeLowercase` alongside #209's uppercase forms (#209 landed
first). `tests/Quotinator.Data.Tests/Helpers/EntityIdCanonicalizerTests.cs` extended with
`CanonicalizeLowercase_UppercaseGuid_ReturnsLowercaseD`, `CanonicalizeLowercase_AlreadyCanonical_IsIdempotent`,
`CanonicalizeLowercase_Malformed_Throws`, `TryCanonicalizeLowercase_ValidGuid_ReturnsTrueWithCanonicalForm`,
`TryCanonicalizeLowercase_Malformed_ReturnsFalse`.

### 2. `PlanAsync`'s quote loop capture-point fix

**Status:** Done. `ImportActionPlanner.PlanAsync`'s quote loop (`src/Quotinator.Core/Database/ImportActionPlanner.cs`)
renames the loop variable to `rawQuote` and builds a canonicalized `SourceQuote` copy (`q`) at the top of
each iteration via `EntityIdCanonicalizer.TryCanonicalizeLowercase`. `SourceQuote` is a plain class with
init-only properties, not a record — no `with` expression support — so the copy is built via object
initializer, the same pattern `QuoteFieldMerge.ApplyMergedFields` already uses in this same file. Every
later reference to `q.Id` in the iteration is automatically canonical once this substitution is made.

### 3. `UPPER()`-wrap every `Quotes.Id`/`QuoteId` query

**Status:** Done — subsumed by the scope-expansion work above, in two passes. The first pass
hand-wrapped every row in the query-audit table (`Quotes.SelectById()`/`SelectRawById()`/
`SelectCompletenessById`/`UpdateCompletenessById`/`UpdateOnNewestWins`, `QuoteGenres.*`,
`QuoteTranslations.*`, `ConversationLines`'s two quote-referencing queries) as part of the systemic
`SqlIdCaseGuard` fix pass, not as a separate, narrower pass targeting only this table — the guard
scans every SQL constant/factory-method/assembled query in the codebase, so Quotes/QuoteId coverage
is a subset of that full pass, not a distinct step. The second pass (the `IdClauses` helper, see
"Second round of scope expansion" above) rewrote every one of those same comparisons again to call
`IdClauses.Equals`/`In`/`NotIn`/`Join` instead of the hand-typed `UPPER(...)` text, including every
JOIN condition in `Quotes.SelectBase`/`SelectRawById()` and the correlated-subquery predicates in
`ConversationLines`. Verified via `SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard`
(both test projects, reflection now covering fields *and* properties) and
`RepositorySqlFactory_PassesIdCaseGuard` — zero violations found on the final run, including under
the guard's own extended JOIN/`NOT IN` detection added during this same pass.

### 4. Audit `Guid`-typed vs. `string`-typed quote-id parameter bindings

**Status:** Done — no code change needed; findings recorded in Notes below. The only two call sites that
declare a `Guid quoteId` parameter (`QuoteSeedWriter.InsertTranslationsAsync`, `InsertGenresAsync`) never
bind that `Guid` struct directly to Dapper — both call `quoteId.ToString()` first, and `Guid.ToString()`'s
default format is already lowercase "D", identical to `EntityIdCanonicalizer.CanonicalizeLowercase`'s
output. Since Step 2 now guarantees the source string is canonically lowercase before
`Guid.Parse(resolved.Id)` is called, `Guid.Parse(...).ToString()` round-trips to the exact same string —
no forcing mismatch exists at either site. Every other quote-id-related call site in the codebase
(`SqliteQuoteService.GetById`, `QuoteSeedWriter.TryGetExistingFieldsAsync`, `ClearStaleAddTargetsAsync`'s
Quote branch, `RepositorySql.HardDelete("Quotes")`) binds the id as a plain `string`, never `Guid` —
confirmed by direct grep, not inferred from the two sites above.

### 5. Tests

**Status:** Done.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | 5 lowercase-form cases (Step 1) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` — corrected from this doc's original planning-stage placement in `SqliteImportActionServiceTests` (`GetById` is actually defined on `SqliteQuoteService`, verified by reading the code before writing the test, per this project's standing "verify plan doc against current code" rule). Inserts a quote via raw SQL with an explicit lowercase id (the generic repository's `Guid`-typed insert path would force uppercase and not actually exercise a lowercase-stored row), then confirms `GetById` resolves it via an uppercase URL id. |
| `Quotinator.Core.Tests.Security.SqlQueryGuardTests` (both projects) | `SqlConstant_PassesIdCaseGuard`, `AssembledQuery_PassesIdCaseGuard` — reflection widened to cover `static string` properties, not just fields |
| `Quotinator.Data.Tests.Repositories.RepositorySqlGuardTests` | `RepositorySqlFactory_PassesIdCaseGuard` |
| `Quotinator.Data.Tests.Diagnostics.SqlIdCaseGuardTests` | 23 cases covering the guard's own regex logic directly (bare/prefixed/aliased/bracket-quoted columns, half-protected wraps, `UPDATE SET` stripping, `IN`/`NOT IN` clauses, and both unwrapped and half-wrapped JOIN/correlated-subquery conditions) |
| `Quotinator.Data.Tests.Repositories.SystemImportActionWriterReaderTests` | `GetByIdAsync_LowercaseStoredId_StillResolves` — regression test for the property-reflection blind spot that let `Sql.SystemImportActions.SelectById`'s unwrapped `WHERE Id = @id` go undetected |

### 6. Verify

**Status:** Done — build, full unit-test suite, T1, and T2 all confirmed. `dotnet build --configuration
Release` → 0 Warning(s)/0 Error(s). `dotnet test --configuration Release --verbosity normal` → full
suite green (789 in Core.Tests, 509 in Data.Tests, 493 in Api.Tests, plus the smaller projects — no
regressions from the capture-point fix, the `UPPER()`-wrap pass, or the subsequent `IdClauses`
refactor). T2 (Docker) run twice — once after the initial casing fix, again after the full `IdClauses`
refactor (rebuilt image, decide/apply flow exercising the fixed `SystemImportActions.SelectById`,
`/random` exercising the fixed `NOT IN` clause). T1 (Visual Studio) confirmed by the developer — see
checklist rows 7-8.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `EntityIdCanonicalizer`'s lowercase forms canonicalize, are idempotent, and reject malformed input via both a throwing and non-throwing form | Unit test | `EntityIdCanonicalizerTests` (5 lowercase-form cases) |
| 2 | ✅ | A file-authored quote id canonicalizes to lowercase at capture | Unit test | `ImportActionPlannerTests.PlanAsync_UppercaseExplicitQuoteId_ResolvedIdIsCanonicalLowercase` |
| 3 | ✅ | Every `Quotes.Id`/`QuoteId`-matching query is case-insensitive — and, per the scope expansion, every id-matching query in the codebase | Unit test | `SqlIdCaseGuard` wired into `SqlQueryGuardTests` (both projects) + `RepositorySqlGuardTests`; zero violations on the final run |
| 4 | ✅ | `GET /quotes/{id}` resolves regardless of URL casing — the previously fully-unmitigated gap | Unit test | `SqliteQuoteServiceTests.GetById_UppercaseUrlIdAgainstLowercaseStoredQuote_StillResolves` |
| 5 | ✅ | `Guid`-typed vs. `string`-typed quote-id parameter bindings are consistent | Doc review + code review | Step 4's audit findings recorded above — no mismatch found, no code change needed |
| 6 | ✅ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` — all green |
| 7 | ✅ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed: clean startup, schema up to date (data v10, app v9), 796 quotes/479 sources/7 characters seeded. Exercised `/quotes/random`, `/quotes`, `/quotes/search?q=d`, `/conversations`, `/masterdata/sources`, `/masterdata/soundcues`, `/admin/audit` — all 200. `/quotes/search` response confirms the embedded `conversations[]` membership field still resolves correctly post-refactor. |
| 8 | ✅ | T2 — the case-insensitive lookup gap is fixed end to end, and stays fixed after the `IdClauses` refactor | Live (T2), run twice | **Pass 1** (initial casing fix): imported a quote with explicit id `F0000210-0000-4000-8000-000000000210`; response's own `id` field came back canonically lowercase (`f0000210-...`); `GET /api/v1/quotes/{id}` returned 200 with the identical quote for both the original uppercase URL casing and an all-lowercase URL casing. See CLAUDE.md step 6's "Quotes.Id case-insensitive lookup" subsection for the exact commands. **Pass 2** (after the `IdClauses` refactor, fresh image rebuild): baseline health/random/search re-run clean; imported the curated file under `review` policy (10 pending actions), decided one via a deliberately lowercase-cased action-id URL segment and the rest normally, applied the batch — 200, exercising `SystemImportActionReader.GetByIdAsync` → the fixed `Sql.SystemImportActions.SelectById` property end to end; `/random?n=10` re-run clean, exercising the fixed `NOT IN` dedup-exclusion clause. |

---

## Notes

Step 4's audit (`Guid`-typed vs. `string`-typed quote-id parameter bindings): the only two call sites
declaring a `Guid quoteId` parameter — `QuoteSeedWriter.InsertTranslationsAsync` and `InsertGenresAsync`
— call `quoteId.ToString()` before binding, never the raw `Guid` struct. `Guid.ToString()`'s default
format is lowercase "D", matching `EntityIdCanonicalizer.CanonicalizeLowercase`'s output exactly, so once
Step 2 guarantees the source string is canonically lowercase before `Guid.Parse(resolved.Id)` runs,
`Guid.Parse(...).ToString()` round-trips to the identical string. No inconsistency exists; no code change
was needed for this step.

T2 note: since `data/sources/quotinator-curated.json` and the two bundled converter-plugin sources are
seeded with lowercase `id` fields already matching `QuoteIdentity.StableId`'s convention, the T2 smoke
test for this issue specifically imports an uppercase-cased explicit quote id (not a bundled file) to
exercise the fix — see the Verify step and CLAUDE.md's pre-push checklist for the exact commands once run.
