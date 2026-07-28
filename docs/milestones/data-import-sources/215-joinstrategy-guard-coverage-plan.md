# #215 — Extend `IJoinStrategy<T>` auto-discovery to the id-case and SELECT-presentation guards

**Status:** Waiting for release
**GitHub issue:** #215
**Tiers required:** T1, T2
**Depends on:** none

---

## Spec requirements

1. Add `AllJoinStrategies_BuildSql_PassesIdCaseGuard` to `Quotinator.Data.Tests.Security.SqlQueryGuardTests`,
   mirroring `AllJoinStrategies_BuildSql_PassesAggregateGuard`'s reflection-based discovery exactly (same
   `IJoinStrategy<TResult>` assembly scan, same `BuildSql()` invocation via `AllJoinStrategyBuildSqlCases`)
   — checked against `SqlIdCaseGuard.FindViolations` instead of `SqlAggregateGuard.IsVulnerablePattern`.
2. Add `AllJoinStrategies_BuildSql_PassesSelectPresentationGuard` to the same class, same discovery
   mechanism, checked against `SqlSelectPresentationGuard.FindUnwrappedSelectColumns` instead.

---

## Background — why this issue exists

Found during #207's final coverage audit (2026-07-22). `SqlQueryGuardTests.cs` currently has three guard
mechanisms, each applied to two enumeration sources (`Sql`'s own constants, and dynamically-assembled
queries) — but the `IJoinStrategy<T>` auto-discovery enumeration is wired to only one of the three guards:

| Guard | Applied to `Sql` constants | Applied to assembled queries | Applied to `IJoinStrategy<T>` |
|---|---|---|---|
| `SqlAggregateGuard` (CVE-2025-6965) | `SqlConstant_PassesAggregateGuard` | `AssembledQuery_PassesAggregateGuard` | `AllJoinStrategies_BuildSql_PassesAggregateGuard` ✅ |
| `SqlIdCaseGuard` (ADR 012) | `SqlConstant_PassesIdCaseGuard` | `AssembledQuery_PassesIdCaseGuard` | *(missing)* |
| `SqlSelectPresentationGuard` (ADR 012) | `SqlConstant_PassesSelectPresentationGuard` | `AssembledQuery_PassesSelectPresentationGuard` | *(missing)* |

**Verified before starting** (per this project's standing rule — an issue's own body can be wrong):

- **Confirmed as claimed**: `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs:189-213` is the
  exact existing `AllJoinStrategies_BuildSql_PassesAggregateGuard` test and its `AllJoinStrategyBuildSqlCases`
  data source. The discovery logic (lines 199-213) is: reflect over `typeof(IJoinStrategy<>).Assembly`
  (i.e. `Quotinator.Data`), filter to non-abstract, non-interface types whose interfaces include a closed
  `IJoinStrategy<>`, `Activator.CreateInstance` each, invoke its `BuildSql()` via reflection, and yield
  `(typeName, sql)` pairs as `DynamicData` cases. The aggregate test itself (lines 189-197) is a plain
  `Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(sql), ...)`.
- **Confirmed as claimed**: `IJoinStrategy<TResult>` (`src/Quotinator.Data/Queries/IJoinStrategy.cs`) is a
  one-method interface (`string BuildSql()`). Searching the `Quotinator.Data` assembly for concrete
  implementations found exactly one: `WidgetWithOwnerStrategy`
  (`src/Quotinator.Data/Queries/WidgetWithOwnerStrategy.cs`), whose `BuildSql()` returns
  `Sql.Queries.WidgetWithOwner()` verbatim — no independent SQL assembly of its own. The issue's claim
  ("the only concrete implementation ... is still true") is confirmed current as of this session.
- **Confirmed guard API shapes** (all in `src/Quotinator.Data/Diagnostics/`):
  - `SqlAggregateGuard.IsVulnerablePattern(string sql) : bool` (`SqlAggregateGuard.cs:41`) — the exact
    method the existing aggregate test already calls.
  - `SqlIdCaseGuard.FindViolations(string sql) : IReadOnlyList<string>` (`SqlIdCaseGuard.cs:86`) — the
    exact method `SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard` already call, asserted
    via `Assert.IsEmpty(violations, ...)`.
  - `SqlSelectPresentationGuard.FindUnwrappedSelectColumns(string sql) : IReadOnlyList<string>`
    (`SqlSelectPresentationGuard.cs:83`) — the exact method
    `SqlConstant_PassesSelectPresentationGuard`/`AssembledQuery_PassesSelectPresentationGuard` already
    call, same `Assert.IsEmpty` shape.
  - `SqlAggregateGuard` itself lives at `src/Quotinator.Data/Diagnostics/SqlAggregateGuard.cs`, confirming
    the issue's informal name for it ("the CVE aggregate guard") maps to this exact class. See
    `docs/architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md` and `docs/sql-safety.md`.
- **Confirmed "not a live bug today" is actually true, by reading the SQL, not assuming it**:
  `Sql.Queries.WidgetWithOwner()` (`src/Quotinator.Data/Queries/Sql.cs:100-106`) produces:
  ```sql
  SELECT LOWER([w].[Id]) AS WidgetId, [w].[Label],
         [o].[Name] AS OwnerName
  FROM   [Widgets] [w]
  INNER JOIN [Owners] [o] ON [w].[OwnerId] = [o].[Id]
  WHERE  [w].[IsDeleted] = 0
  ```
  - `SqlSelectPresentationGuard.FindUnwrappedSelectColumns`: the only `SELECT`-list column ending in `Id`
    is `[w].[Id]`, already wrapped via `IdClauses.SelectColumn("[w].[Id]", "WidgetId")` →
    `LOWER([w].[Id]) AS WidgetId`, which `ProtectedColumnPattern` strips before scanning. `[w].[Label]`
    and `[o].[Name] AS OwnerName` don't end in `Id`. Zero violations.
  - `SqlIdCaseGuard.FindViolations`: there is no `@`-bound parameter anywhere in this query, so
    `IdComparisonPattern` (which requires a `@param` on the right-hand side) cannot match at all. The
    `WHERE [w].[OwnerId] = [o].[Id]` join condition is checked by `JoinComparisonPattern` instead, but
    that pattern's alias-matching group is `\w+\.` (a **bare** alias immediately followed by a literal
    dot) — it does not allow a bracket-quoted alias like `[w].`. Since every alias `Joins.Inner`/`Joins.Left`
    emit is bracket-quoted (`[w].[OwnerId] = [o].[Id]`, not `w.OwnerId = o.Id`), `JoinComparisonPattern`
    never matches this join condition at all, regardless of whether it's wrapped. Zero violations — but
    for a narrower reason than "already protected": the pattern doesn't recognize this bracket-quoted-alias
    join shape as an id-to-id comparison in the first place. This is a latent gap in `SqlIdCaseGuard` itself
    (it would silently pass an *unwrapped* bracket-quoted-alias join too, not just this one), but it is
    strictly a pre-existing property of the guard regex, unrelated to #215's own scope (test auto-discovery
    wiring) — noted here for the record, not proposed as a fix in this issue. See Notes.
  - This query is independently exercised today via `AssembledQueryCases()`
    (`SqlQueryGuardTests.cs:135`, `"Queries.WidgetWithOwner()"`) against both
    `AssembledQuery_PassesIdCaseGuard` and `AssembledQuery_PassesSelectPresentationGuard`, both of which
    currently pass in the full suite (`main` is green per this project's standing rule) — corroborating
    the manual trace above with the tests that already run this exact string through both guards today.
  - Conclusion: **the issue's "not a live bug today" claim is confirmed true.** The two new tests will
    pass immediately upon creation, using the identical SQL string an existing sibling test already
    exercises successfully via a different enumeration path.

**Label vs. content-shape observation**: this issue is labelled `bug` on GitHub, but its actual body
(`## Background` / `## What needs to be done` / `## Expected tests` / a `Definition of done` matching
`docs/workflow/issues.md`'s `enhancement` template verbatim — "start red" / "requirements implemented" /
"pass green" / "no regression" / "closing comment") is shaped exactly like the `enhancement` template, not
the `bug` template (which requires `## Description` / `## Reproduction steps` / `## Expected behaviour` /
`## Actual behaviour` / `## Failing tests` — none of which this issue has, and a reproduction/actual-vs-
expected-behaviour framing doesn't fit a change that adds coverage for a gap with no current wrong output
to reproduce). This plan doc is structured following the `enhancement` shape (Background / Steps /
Verification checklist) to match. The GitHub label itself is left unchanged — relabelling is out of scope
for a planning-only pass; flagging it here for whoever reviews this plan doc to action if desired.

**Red-to-green nuance**: `docs/workflow/issues.md`'s `enhancement` Definition of done says expected tests
"start red before implementation begins." This issue has no accompanying production-code change — the
Background finding above confirms the one existing `IJoinStrategy<T>` implementation already passes both
guards today. The two new tests will therefore be green on their very first run, not red-then-green. That
is the expected and correct outcome here (see Steps → 1), not a process violation: the "fix" this issue
delivers is coverage itself, not a behavioural change, so there is no red state to pass through.

---

## Approach

Add exactly two new `[TestMethod]`s to `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs`,
inserted immediately after the existing `AllJoinStrategies_BuildSql_PassesAggregateGuard` (current lines
189-197) and before the `AllJoinStrategyBuildSqlCases` data source method (current line 199) — so all three
`AllJoinStrategies_BuildSql_Passes*Guard` tests sit together, consuming the same unmodified
`AllJoinStrategyBuildSqlCases` via `[DynamicData(nameof(AllJoinStrategyBuildSqlCases))]`. No change to
`AllJoinStrategyBuildSqlCases` itself, `IJoinStrategy<T>`, `WidgetWithOwnerStrategy`, or any `Sql.cs`/guard
production code — this issue is test-file-only.

```csharp
/// <summary>
/// Same discovery as <see cref="AllJoinStrategies_BuildSql_PassesAggregateGuard"/>, checked against
/// <see cref="SqlIdCaseGuard"/> instead. See ADR 012 and #210, #215.
/// </summary>
[TestMethod]
[DynamicData(nameof(AllJoinStrategyBuildSqlCases))]
public void AllJoinStrategies_BuildSql_PassesIdCaseGuard(string typeName, string sql)
{
    var violations = SqlIdCaseGuard.FindViolations(sql);
    Assert.IsEmpty(violations,
        $"{typeName}.BuildSql() contains a case-sensitive id comparison: {string.Join(", ", violations)}. " +
        "Wrap both sides in UPPER(...) — see ADR 012.");
}

/// <summary>
/// Same discovery as <see cref="AllJoinStrategies_BuildSql_PassesAggregateGuard"/>, checked against
/// <see cref="SqlSelectPresentationGuard"/> instead. See ADR 012's "read-time presentation
/// normalization" revision and #215.
/// </summary>
[TestMethod]
[DynamicData(nameof(AllJoinStrategyBuildSqlCases))]
public void AllJoinStrategies_BuildSql_PassesSelectPresentationGuard(string typeName, string sql)
{
    var violations = SqlSelectPresentationGuard.FindUnwrappedSelectColumns(sql);
    Assert.IsEmpty(violations,
        $"{typeName}.BuildSql() selects {string.Join(", ", violations)} unwrapped — wrap in LOWER(...) AS " +
        "ColumnName in the SELECT column list. See ADR 012's \"read-time presentation normalization\" revision.");
}
```

The assertion-message wording deliberately copies the sibling `SqlConstant_Passes*Guard`/
`AssembledQuery_Passes*Guard` tests' own messages verbatim (including `SqlConstant_PassesIdCaseGuard`'s
pre-existing "Wrap both sides in UPPER(...)" wording at `SqlQueryGuardTests.cs:54`/`65`, even though
`SqlIdCaseGuard`'s own doc comments and `IdClauses.SelectColumn`'s implementation both use `LOWER(...)` as
the canonical wrapper — see Notes) for message-wording consistency across all three enumeration sources of
the same guard, rather than silently correcting a pre-existing message/implementation mismatch that is out
of scope for this issue.

**Coordination note for merge with #214** (per task instructions — #214 also touches this file): this
issue's only change is the insertion of the two test methods shown above, at the single insertion point
described (between the existing aggregate join-strategy test and the `AllJoinStrategyBuildSqlCases` data
source method). It does not touch `AllNamedSqlConstants`, `AssembledQueryCases`, `EnumerateSqlConstants`,
or any other existing method in the file.

---

## Steps

### 1. Add the two new test methods

**Status:** ✅ Done — both methods added exactly as shown in Approach, inserted immediately before
`AllJoinStrategyBuildSqlCases`. Both passed on first run (confirmed via `dotnet test --filter
"FullyQualifiedName~AllJoinStrategies_BuildSql"` — 3/3 passed, including the pre-existing aggregate
test), exactly the expected no-red-state outcome per Background.

### 2. Full suite regression check

**Status:** ✅ Done — `dotnet build --configuration Release`: 0 warnings, 0 errors. `dotnet test
--configuration Release --verbosity normal`: every project green (`Quotinator.Data.Tests` 614/614, up
from 612; `Quotinator.Core.Tests` 972/972; `Quotinator.Api.Tests` 496/496; all others unaffected), 0
failures.

### 3. T1 and T2 verification

**Status:** ✅ Done — T2: `docker build -f docker/Dockerfile -t quotinator:local .` succeeded. Fresh
container startup: clean, schema v10, no errors. Baseline smoke suite (`/health`, `/version`,
`/quotes/random`, `/quotes/search?q=love`) all returned expected 200 responses. No new scenario needed —
test-file-only change with no runtime effect. T1: developer confirmed clean startup in Visual Studio —
"schema is up to date (data v10, app v10)", no errors.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `AllJoinStrategies_BuildSql_PassesIdCaseGuard` exists, discovers every concrete `IJoinStrategy<TResult>` implementation via the same reflection mechanism as the aggregate-guard test, and passes | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests.AllJoinStrategies_BuildSql_PassesIdCaseGuard` |
| 2 | ✅ | `AllJoinStrategies_BuildSql_PassesSelectPresentationGuard` exists, same discovery mechanism, passes | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests.AllJoinStrategies_BuildSql_PassesSelectPresentationGuard` |
| 3 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green (`Quotinator.Data.Tests` 614/614, `Quotinator.Core.Tests` 972/972, `Quotinator.Api.Tests` 496/496), 0 warnings, 0 errors |
| 4 | ✅ | T2 — Docker image builds and baseline smoke suite passes | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; health/version/random/search all returned expected responses |
| 5 | ✅ | T1 — app starts cleanly in Visual Studio (required unconditionally, same reasoning as row 4) | Live (T1) | Developer confirmed in Visual Studio — "schema is up to date (data v10, app v10)", clean startup, no errors |

---

## Notes

**`SqlIdCaseGuard`'s `JoinComparisonPattern` gap (found during this plan's own verification, not proposed
as a fix here):** the pattern that's supposed to catch an unwrapped id-to-id JOIN condition
(`\w+\.\[?(\w*Id)\]?...`) only matches a **bare** table alias immediately followed by a dot (e.g.
`s.Id`), not a bracket-quoted one (e.g. `[w].[Id]`) — the shape every query built via `Sql.Joins.Inner`/
`Sql.Joins.Left` actually produces. This means an unwrapped, genuinely unsafe bracket-quoted-alias join
condition would currently pass `SqlIdCaseGuard` undetected, not because it's protected, but because the
guard's regex doesn't recognize the shape at all. `WidgetWithOwnerStrategy`/`Sql.Queries.WidgetWithOwner()`
happens to pass regardless (see Background), so this gap is not a live bug for #215's own scope — but it
is a real latent hole in `SqlIdCaseGuard` itself that a future bracket-quoted-alias join with a genuinely
unwrapped id comparison would slip through. Recommend filing this as its own follow-up issue against
`SqlIdCaseGuard`'s `JoinComparisonPattern`/`ProtectedJoinPattern` regexes rather than folding a guard-regex
fix into #215's test-only scope.

**Pre-existing `UPPER(...)` vs. `LOWER(...)` message inconsistency (also found, also not this issue's to
fix):** `SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard`'s existing assertion messages
say "Wrap both sides in UPPER(...)", but ADR 012's actual canonical wrapper (and `IdClauses.SelectColumn`'s
own implementation) is `LOWER(...)`. This plan's Approach deliberately copies that existing wording
verbatim into the two new tests for consistency with their siblings, rather than silently fixing a stale
message string that's outside #215's stated scope. Worth a trivial follow-up cleanup at some point.

T1 and T2 are both required unconditionally per `docs/release-verification.md`'s "Always" rule for each
tier, independent of any trigger match — this change touching no `.razor`/Blazor/middleware/migration code
means neither tier has a *targeted* scenario to add, not that either tier is skipped.
