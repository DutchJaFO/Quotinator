# #214 — Guard test reflection doesn't cover static factory methods (`GetMethods`)

**Status:** Waiting for release
**GitHub issue:** #214
**Tiers required:** T1, T2
**Depends on:** None

---

## Spec requirements

1. Widen `EnumerateSqlConstants` (the private reflection helper duplicated in
   `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs` and
   `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs`) to also discover `static` methods on
   any `Sql.*` nested class that return `string` and take either zero parameters or only optional
   parameters — via `GetMethods(BindingFlags.NonPublic | BindingFlags.Static)`, mirroring how fields
   (`GetFields`) and arrow-bodied properties (`GetProperties`) are already discovered. Each discovered
   method is invoked with its parameters' own default values (or no arguments, for the zero-parameter
   case) to obtain its SQL text, exactly as the existing manual `AssembledQueryCases` entries for these
   same methods already do today.
2. Remove the now-redundant manually-enumerated 0-arg cases from `AssembledQueryCases` in both files
   (`Sql.Quotes.SelectById()`/`Sql.Quotes.SelectRawById()` in Core.Tests; `Sql.Queries.WidgetWithOwner()`
   in Data.Tests) — once the widened reflection discovers them automatically via
   `AllNamedSqlConstants`, keeping the manual case too would run the identical guard checks against the
   identical SQL string under two different `DynamicData` sources for no added protection. This is the
   concrete answer to the issue's requirement 3 ("confirm... duplicate scanning") — not just confirming
   it's harmless, but eliminating it.
3. For static factory methods that require at least one non-optional parameter — the large majority,
   and the ones the issue's requirement 2 is actually asking about — do **not** attempt to
   auto-invoke them with synthetic placeholder arguments. A single representative call cannot exercise
   every meaningfully distinct SQL shape a flag-driven method produces (see Background for the concrete
   methods that demonstrate this). Manual `DynamicData` enumeration remains the mechanism that actually
   guards every branch. Instead, add one new closed-inventory test per file —
   `ParameterizedSqlFactoryMethods_MatchDocumentedInventory` — mirroring the already-established
   `AggregateQueries_MatchDocumentedInventory` pattern, so that a newly-added parameterized method not
   yet added to the documented list (and, by implication, not yet added to `AssembledQueryCases`) fails
   the build immediately instead of being silently invisible to every guard.
4. Cross-reference every `static` method currently defined on any `Sql.*` nested class in both
   `src/Quotinator.Core/Queries/Sql.cs` and `src/Quotinator.Data/Queries/Sql.cs`, plus every factory
   method in `src/Quotinator.Data/Repositories/RepositorySql.cs`, against the corresponding manual
   `DynamicData` list, and record the result. (Confirmed during planning — see Background: no live gap
   exists today; every currently-defined method's SQL output is already exercised by some existing
   `DynamicData` case, directly or as an embedded fragment.)

---

## Background — why this issue exists

`EnumerateSqlConstants` — declared identically in both
`tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs:217-227` and
`tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs:229-239` — currently reads:

```csharp
private static IEnumerable<(string Name, string Sql)> EnumerateSqlConstants()
    => typeof(Sql)
        .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
        .SelectMany(t => t
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => (f.IsLiteral || f.IsInitOnly) && f.FieldType == typeof(string))
            .Select(f => ($"{t.Name}.{f.Name}", (string)f.GetValue(null)!))
            .Concat(t
                .GetProperties(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string) && p.GetMethod is not null)
                .Select(p => ($"{t.Name}.{p.Name}", (string)p.GetValue(null)!))));
```

It feeds `SqlConstant_PassesAggregateGuard`/`SqlConstant_PassesIdCaseGuard`/
`SqlConstant_PassesSelectPresentationGuard` via the public `AllNamedSqlConstants()` wrapper, and also
drives `AggregateQueries_MatchDocumentedInventory`'s own enumeration. It has no `GetMethods` call —
every `static` factory method that takes a parameter (or none) is invisible to it and is instead
covered only because a developer manually added an explicit call to a separate `DynamicData` source:
`AssembledQueryCases` in each `SqlQueryGuardTests.cs`, and `RepositorySqlCases` in
`tests/Quotinator.Data.Tests/Repositories/RepositorySqlGuardTests.cs`.

**Real counts, confirmed by reading every `Sql.*` nested class in both projects:**

- `src/Quotinator.Core/Queries/Sql.cs`'s `Quotes` class defines exactly 7 `static` methods:
  `SelectById()`, `SelectRawById()` (both 0-arg), `SelectRandom(string)`, `SelectPaged(string)`,
  `SelectSearch(string, string)`, `CountRandom(string)`, `CountGetAll(string)`. No other nested class in
  this file (`SearchField`, `QuoteGenres`, `QuoteTranslations`, `SourceTranslations`,
  `CharacterTranslations`, `Characters`, `CharacterSources`, `People`, `Sources`, `Series`, `Universe`,
  `Conversations`, `ConversationLines`) declares a `static` method at all — every SQL string in those is
  a `const`/`static readonly` field. `Core.Tests`'s `AssembledQueryCases` (lines 147-200) manually
  invokes all 7 — the two 0-arg methods each get exactly one case (`"SelectById()"`,
  `"SelectRawById()"`), the 5 parameterized ones get the full 14-filter-case × N-field matrix
  (63 total `yield return` cases covering all 7 methods).
- `src/Quotinator.Data/Queries/Sql.cs` defines 9 `static` methods across `Joins`, `Queries`,
  `SystemAudit`, and `SystemImportActions`: `Joins.Inner`/`Joins.Left` (5 required params each),
  `Queries.WidgetWithOwner()` (0-arg), `SystemAudit.SelectPaged(bool, bool)`/`CountPaged(bool, bool)`
  (2 required params each) plus `SystemAudit`'s own private `BuildWhere(bool, bool)`,
  `SystemImportActions.SelectPaged(bool, bool, bool = false)`/`CountPaged(bool, bool, bool = false)`
  (2 required + 1 optional param each) plus `SystemImportActions`'s own private
  `BuildWhere(bool, bool, bool)` (3 required params). `Data.Tests`'s `AssembledQueryCases`
  (lines 132-154) directly invokes `WidgetWithOwner()` once, `SystemAudit.SelectPaged`/`CountPaged` for
  all 4 boolean-flag combinations each (8 cases), and `SystemImportActions.SelectPaged`/`CountPaged` for
  all 8 three-flag combinations each (16 cases) — 25 cases total. `Joins.Inner`/`Joins.Left` and both
  private `BuildWhere` methods have no case of their own, but their output is never executed standalone
  in production either — it is always embedded inside `WidgetWithOwner()`'s or
  `SystemAudit`/`SystemImportActions`'s `SelectPaged`/`CountPaged` output, which *is* directly invoked
  and guard-tested. This mirrors the codebase's existing accepted pattern of guard-testing a private
  fragment constant directly when one exists as its own field (e.g.
  `SystemAudit.CountPagedBase` — a `private const string` fragment — is itself individually enumerated
  by `EnumerateSqlConstants`'s `GetFields` call today, and is explicitly named in
  `AggregateQueries_MatchDocumentedInventory`'s documented set), so treating a private fragment
  *method*'s output as covered-by-embedding rather than needing its own standalone case is consistent,
  not a new exception.
- `src/Quotinator.Data/Repositories/RepositorySql.cs` defines 11 `static` factory methods
  (`SelectById`, `SoftDelete`, `SelectDeleted`, `Restore`, `HardDelete`, `Purge`, `SelectByForeignKey`,
  `SelectJunctionRow`, `SelectByIds`, `SelectPage`, `CountActive`) plus one private helper
  (`BuildSelectColumns`, invoked internally by every one of the 11). All 11 have at least one required
  parameter (`tableName`, always) — none are 0-arg or all-optional. `RepositorySqlGuardTests.cs`'s
  `RepositorySqlCases` (lines 69-99) manually invokes all 11 (`SelectPage` four times, for its four
  `orderBy` shapes), plus `Sql.SystemAudit.SelectPaged`/`CountPaged` again for all four flag
  combinations each (a second, independent manual coverage of the same two Data-layer methods already
  covered in `Data.Tests`'s own `AssembledQueryCases`).

**No live gap exists today.** Every one of the 7 + 9 + 11 = 27 `static` SQL-producing methods across
both `Sql.cs` files and `RepositorySql.cs` has its SQL output exercised by at least one existing
`DynamicData` case, directly or as an embedded fragment inside a directly-invoked caller — confirmed by
the enumeration above, not assumed. This matches the issue's own claim. The risk the issue is filed
against is *forward-looking*: a new method added to any of these files has no automatic mechanism
forcing it into a `DynamicData` list, exactly the same failure mode that already bit this project twice
for fields (`Sql.SystemImportActions.SelectById`, a property, invisible until `GetProperties` was added)
and for the guard's own regex (`NOT IN`, invisible until a later audit) — see ADR 012's history and
`docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md`.

**Why a single synthetic invocation cannot replace the manual matrix for parameterized methods** —
grounded in the two concrete private `BuildWhere` methods:

```csharp
// Sql.SystemImportActions.BuildWhere (src/Quotinator.Data/Queries/Sql.cs:257-264)
private static string BuildWhere(bool filterBatchId, bool filterStatus, bool filterEntityType)
{
    var parts = new List<string>(3);
    if (filterBatchId)    parts.Add(IdClauses.Equals("BatchId", "batchId"));
    if (filterStatus)     parts.Add("UPPER(Status) = UPPER(@status)");
    if (filterEntityType) parts.Add("UPPER(EntityType) = UPPER(@entityType)");
    return parts.Count > 0 ? " WHERE " + string.Join(" AND ", parts) : string.Empty;
}
```

Calling this once with all-`false` placeholder arguments (the only default-value invocation reflection
could synthesize automatically) produces `string.Empty` — a WHERE clause with nothing in it, containing
no id comparison to check at all. The one call shape that actually contains the id-column comparison
`SqlIdCaseGuard` exists to catch (`filterBatchId = true`) would never run. The same is true of
`Sql.SystemAudit.BuildWhere` and, one level up, `Sql.SystemImportActions.SelectPaged`/`CountPaged`
themselves (whose own SQL shape changes with every flag combination). A generic reflection-driven
argument synthesizer that tried to enumerate every boolean/enum combination automatically would be a
non-trivial fuzzing mechanism in its own right, disproportionate to this homelab project's stated
simplicity priority (see CLAUDE.md's Project Priorities). Manual `DynamicData` enumeration — already
proven to give full-branch coverage for exactly these methods — remains the correct mechanism for any
method with a required parameter.

---

## Approach

### Widened `EnumerateSqlConstants` (zero-parameter or all-optional methods)

Add a third `.Concat(...)` branch to each file's `EnumerateSqlConstants`, filtering out compiler-generated
property accessors (`IsSpecialName`) — without this filter, every arrow-bodied `string` property already
discovered via `GetProperties` (e.g. `SystemImportActions.SelectById`) would also surface a second time
as its synthesized `get_SelectById` method, duplicating (not adding) coverage under a confusing name:

```csharp
.Concat(t
    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
    .Where(m => !m.IsSpecialName
        && m.ReturnType == typeof(string)
        && m.GetParameters().All(p => p.IsOptional))
    .Select(m => (
        $"{t.Name}.{m.Name}()",
        (string)m.Invoke(null, m.GetParameters().Select(p => p.DefaultValue).ToArray())!)));
```

`GetParameters().All(p => p.IsOptional)` is vacuously `true` for a genuine 0-arg method, so this single
filter covers both cases the issue names ("zero-parameter or all-optional"). Building the invocation
arguments from each parameter's own `DefaultValue` (rather than `Type.Missing`/
`BindingFlags.OptionalParamBinding`) works uniformly for the 0-arg case (empty array) and a future
all-optional case with real default values, without relying on reflection's more fragile
optional-parameter-binding path.

Applied against the real codebase today, this newly discovers exactly 3 names, all already known safe:
`Quotes.SelectById()`, `Quotes.SelectRawById()` (Core), and `Queries.WidgetWithOwner()` (Data). No other
`static` method in either file is 0-arg or all-optional, confirmed by the Background enumeration.

### Redundant manual cases removed

Once the 3 methods above are discovered automatically via `AllNamedSqlConstants`, their manual
`AssembledQueryCases` entries are deleted:

- Core.Tests: the `"SelectById()"`/`"SelectRawById()"` `yield return` lines (lines 181-185 today).
- Data.Tests Security: the `"Queries.WidgetWithOwner()"` `yield return` line (line 135 today).

This is the only edit to `AssembledQueryCases` in either file — the filter-matrix cases for
`SelectRandom`/`SelectPaged`/`SelectSearch`/`CountRandom`/`CountGetAll` (Core) and
`SystemAudit`/`SystemImportActions` `SelectPaged`/`CountPaged` (Data) are untouched, since those methods
have required parameters and stay outside the widened reflection's scope. `RepositorySqlGuardTests.cs`
and `RepositorySqlCases` are not touched at all — cross-referenced in Background, zero methods there
qualify as 0-arg/all-optional today.

### Parameterized methods: closed-inventory test, not auto-invocation

New helper + test in both `SqlQueryGuardTests.cs` files:

```csharp
private static IEnumerable<string> EnumerateParameterizedSqlFactoryMethodNames()
    => typeof(Sql)
        .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
        .SelectMany(t => t
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => !m.IsSpecialName
                && m.ReturnType == typeof(string)
                && m.GetParameters().Any(p => !p.IsOptional))
            .Select(m => $"{t.Name}.{m.Name}"));

[TestMethod]
public void ParameterizedSqlFactoryMethods_MatchDocumentedInventory()
{
    // Every static SQL factory method taking at least one required parameter. Adding a new one here
    // is a signal, not an error on its own — but it must also get a DynamicData case in
    // AssembledQueryCases covering every meaningfully distinct call shape (see #214's plan doc for why
    // a single synthetic invocation can't safely stand in for that matrix).
    var documented = new HashSet<string> { /* real names, see Steps */ };

    var actual = EnumerateParameterizedSqlFactoryMethodNames().ToHashSet();

    CollectionAssert.AreEquivalent(documented.ToList(), actual.ToList(),
        "The set of static SQL factory methods requiring at least one parameter has changed. " +
        "Add the new method to AssembledQueryCases with a case for every meaningfully distinct call " +
        "shape, then update this documented list. See #214.");
}
```

This mirrors `AggregateQueries_MatchDocumentedInventory`'s already-established shape exactly (same
"documented `HashSet`, compare via `CollectionAssert.AreEquivalent`" idiom) rather than inventing a new
pattern. It is a **drift detector**, not a bug-fix test: it starts green (the documented set is written
to match the real, already-known method set from Background) and only turns red in the future, the
moment someone adds a new required-parameter method without updating the list — exactly the same nature
as `AggregateQueries_MatchDocumentedInventory` itself, which is why it does not appear as a "red before
implementation" row in the Definition of Done sense; see Steps for how this is sequenced.

This is the concrete resolution to the issue's requirement 2: **auto-invocation is rejected as unsafe
for any method with a required parameter** (Background's `BuildWhere` example shows why a single
synthetic call misses the exact branches the guards exist to check), and **manual `DynamicData`
enumeration remains authoritative** for those methods — but it is now backed by an automatic
"nothing new snuck in unnoticed" check, the same layered-defense idiom ADR 012 already uses elsewhere
(e.g. `IdClauses.Join` wrapping unconditionally as defense-in-depth even where today's callers are
already safe).

---

## Steps

### 1. Write the auto-discovery regression tests (red)

**Status:** ✅ Done — both tests added and confirmed genuinely red (`Assert.Contains` failed, the
expected name not yet present) before Step 3's widening.

One new `[TestMethod]` per file, calling the existing public `AllNamedSqlConstants()` directly (no new
helper needed for this one):

- `Quotinator.Core.Tests.Security.SqlQueryGuardTests.AllNamedSqlConstants_DiscoversZeroArgAndAllOptionalStaticFactoryMethods`
  — asserts the returned names include `"Quotes.SelectById()"` and `"Quotes.SelectRawById()"`. Must
  fail before Step 3 (today's `EnumerateSqlConstants` has no `GetMethods` branch, so neither name is
  produced).
- `Quotinator.Data.Tests.Security.SqlQueryGuardTests.AllNamedSqlConstants_DiscoversZeroArgAndAllOptionalStaticFactoryMethods`
  — asserts the returned names include `"Queries.WidgetWithOwner()"`. Same red-before-Step-3 reasoning.

### 2. Confirm both tests are genuinely red

**Status:** ✅ Done — `dotnet test --filter "FullyQualifiedName~AllNamedSqlConstants_DiscoversZeroArgAndAllOptionalStaticFactoryMethods"` confirmed both tests failed (1/1 failed in each project) against the
unwidened `EnumerateSqlConstants`.

### 3. Widen `EnumerateSqlConstants` in both files

**Status:** ✅ Done — the `GetMethods` branch from Approach added to both `EnumerateSqlConstants`
implementations, exactly as drafted. Both Step 1 tests now pass.

### 4. Confirm no new guard violations from the 3 newly-discovered methods

**Status:** ✅ Done — full `SqlQueryGuardTests` suite run in both projects (539 Core, 179 Data, all
passing) confirmed `SqlConstant_PassesAggregateGuard`/`SqlConstant_PassesIdCaseGuard`/
`SqlConstant_PassesSelectPresentationGuard` all pass for `Quotes.SelectById()`, `Quotes.SelectRawById()`,
and `Queries.WidgetWithOwner()` with zero violations.

### 5. Remove the redundant manual `AssembledQueryCases` entries

**Status:** ✅ Done — the `"SelectById()"`/`"SelectRawById()"` cases removed from Core.Tests's
`AssembledQueryCases` and the `"Queries.WidgetWithOwner()"` case removed from Data.Tests's
`AssembledQueryCases`, replaced with an explanatory comment. Full `SqlQueryGuardTests` suite stayed green
after the removal.

### 6. Add the parameterized-method closed-inventory test to both files

**Status:** ✅ Done — `EnumerateParameterizedSqlFactoryMethodNames` and
`ParameterizedSqlFactoryMethods_MatchDocumentedInventory` added to both files, with the documented sets
populated exactly as planned. Both tests passed immediately (drift detector, not a bug fix).

### 7. Full build and test verification

**Status:** ✅ Done — `dotnet build --configuration Release`: 0 warnings, 0 errors.
`dotnet test --configuration Release --verbosity normal`: every project green
(`Quotinator.Core.Tests` 972/972, `Quotinator.Data.Tests` 612/612, `Quotinator.Api.Tests` 496/496, all
others unaffected), 0 failures — including `AggregateQueries_MatchDocumentedInventory` in both projects
(unaffected, as expected).

### 8. T1 and T2 verification

**Status:** ✅ Done — T2: `docker build -f docker/Dockerfile -t quotinator:local .` succeeded. Fresh
container startup: clean, schema v10, no errors. Baseline smoke suite (`/health`, `/version`,
`/quotes/random`, `/quotes/search?q=love`, `/quotes/search?q=Casablanca&field=source`) all returned
expected 200 responses. No new scenario needed — test-file-only change with no runtime effect. T1:
developer confirmed clean startup in Visual Studio — "schema is up to date (data v10, app v10)", no
errors, matching this issue's own expectation of zero runtime effect.

Per `docs/release-verification.md`: T1 and T2 are both always required for any issue that touches code,
regardless of whether a specific trigger applies — declaring `Tiers required: T2` alone (this plan doc's
own original draft) is explicitly not a valid declaration. This issue is test-file-only with no
runtime/production-code change, so no new scenario-specific smoke command is added for either tier;
`docker build -f docker/Dockerfile -t quotinator:local .` succeeding plus CLAUDE.md's Pre-Push
Checklist → step 6 baseline smoke suite passing is the full T2 gate, and the developer starting the app
in Visual Studio and confirming a clean startup is the full T1 gate.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Widened reflection auto-discovers zero-arg/all-optional static factory methods in Core | Unit test | `Quotinator.Core.Tests.Security.SqlQueryGuardTests.AllNamedSqlConstants_DiscoversZeroArgAndAllOptionalStaticFactoryMethods` |
| 2 | ✅ | Widened reflection auto-discovers zero-arg/all-optional static factory methods in Data | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests.AllNamedSqlConstants_DiscoversZeroArgAndAllOptionalStaticFactoryMethods` |
| 3 | ✅ | The 3 newly-discovered methods pass all three existing guards with no new violations | Unit test | `SqlConstant_PassesAggregateGuard`/`SqlConstant_PassesIdCaseGuard`/`SqlConstant_PassesSelectPresentationGuard` (both projects) — green for `Quotes.SelectById()`, `Quotes.SelectRawById()`, `Queries.WidgetWithOwner()` |
| 4 | ✅ | Redundant manual `AssembledQueryCases` entries for the 3 methods removed, no coverage loss | Unit test | Full `SqlQueryGuardTests` suite (both projects) stays green after the removal in Step 5 |
| 5 | ✅ | Closed-inventory test for parameterized methods matches the real method set in Core | Unit test | `Quotinator.Core.Tests.Security.SqlQueryGuardTests.ParameterizedSqlFactoryMethods_MatchDocumentedInventory` |
| 6 | ✅ | Closed-inventory test for parameterized methods matches the real method set in Data | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests.ParameterizedSqlFactoryMethods_MatchDocumentedInventory` |
| 7 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green (`Quotinator.Core.Tests` 972/972, `Quotinator.Data.Tests` 612/612, `Quotinator.Api.Tests` 496/496), 0 warnings, 0 errors |
| 8 | ✅ | T2 — Docker image builds and baseline smoke suite passes (required unconditionally per `docs/release-verification.md`, not because this issue's own change looks like it needs it) | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; health/version/random/search all returned expected responses |
| 9 | ✅ | T1 — app starts cleanly in Visual Studio (required unconditionally, same reasoning as row 8) | Live (T1) | Developer confirmed in Visual Studio — "schema is up to date (data v10, app v10)", clean startup, no errors |

---

## Notes

This plan touches only two files' `EnumerateSqlConstants` bodies and `AssembledQueryCases` lists —
`tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs` and
`tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs` — plus adds one new private helper and one
new `[TestMethod]` to each. It does not touch `RepositorySqlGuardTests.cs`/`RepositorySqlCases`
(cross-referenced in Background; zero qualifying methods exist there today — every `RepositorySql`
factory method requires at least a `tableName` argument), `AllJoinStrategyBuildSqlCases` (sibling issue
#215's territory — `IJoinStrategy<T>` auto-discovery via `Activator.CreateInstance`, a completely
different mechanism from `EnumerateSqlConstants`), or `Sql.ImportBatches` (#212's territory) or
`ImportBatch.ImportedBy` (#213's territory). No overlap in edited lines with any of the three sibling
sub-issues of #207 is expected, but #215 edits the same physical files
(`Quotinator.Core.Tests`/`Quotinator.Data.Tests`'s `SqlQueryGuardTests.cs`) — coordinate at merge time
if both land close together; this plan's diff is confined to `EnumerateSqlConstants` and
`AssembledQueryCases` only, not `AllJoinStrategyBuildSqlCases`.

No ADR update is needed. ADR 012 already describes the guard-test wiring generically ("Wired into
`SqlQueryGuardTests` ... via `DynamicData` enumeration over every SQL constant, factory method, and
dynamically-assembled query") — this issue makes that sentence more literally true, it does not change
the architectural decision itself.

If `RepositorySql.cs` ever grows a genuinely 0-arg or all-optional factory method, extending
`RepositorySqlGuardTests.cs` with the same `GetMethods` widening would be the natural follow-up — not
built speculatively here, since no such method exists today (project convention: don't build ahead of a
concrete need).
