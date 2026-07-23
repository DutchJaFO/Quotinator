# #212 — Remove ImportBatches' `SELECT *`, making it visible to SqlSelectPresentationGuard

**Status:** Planning
**GitHub issue:** #212
**Tiers required:** T2
**Depends on:** none

---

## Spec requirements

1. `Sql.ImportBatches.SelectAll` and `Sql.ImportBatches.SelectByType`
   (`src/Quotinator.Data/Queries/Sql.cs`) select an explicit column list instead of `SELECT *`,
   built by reflecting over `ImportBatch`'s own properties (`ReflectedColumnMetadata.For(typeof(ImportBatch))`)
   rather than hand-typed — so the column list never needs a manual update when a property is added,
   removed, or renamed on `ImportBatch`, the same flexibility `SELECT *` currently provides "for free."
   Every `*Id`-suffixed column found this way is wrapped via `IdClauses.SelectColumn`.
2. The rewritten queries pass `SqlSelectPresentationGuard.FindUnwrappedSelectColumns` — genuinely,
   not vacuously (see Background for why the current pass is vacuous).
3. No other hand-written query in either project's `Sql.cs` still uses `SELECT *` — confirmed by a
   fresh grep as part of this issue, not assumed from the issue body's own claim.
4. `SqliteImportBatchRepository.GetAllAsync`/`GetByTypeAsync` (the only two call sites of these two
   queries) continue to return correct, fully-populated `ImportBatch` rows after the rewrite — no
   regression in ordering, filtering, or field mapping.
5. A property added to (or removed from) `ImportBatch` after this issue ships is picked up by
   `SelectAll`/`SelectByType` automatically, with zero code change to `Sql.ImportBatches` — proven by
   a test, not just asserted (see Verification checklist row 6).

---

## Background — why this issue exists

**Current state, confirmed by reading `src/Quotinator.Data/Queries/Sql.cs` directly (2026-07-23):**

```csharp
// lines 122-126
internal const string SelectAll =
    "SELECT * FROM ImportBatches WHERE IsDeleted = 0 ORDER BY ImportedAt DESC, ROWID DESC;";

internal const string SelectByType =
    "SELECT * FROM ImportBatches WHERE IsDeleted = 0 AND Type = @type ORDER BY ImportedAt DESC, ROWID DESC;";
```

These line numbers match the issue body's own claim (122-126) exactly — no drift from #207's other work
this session. Both are `internal const string` fields (not `static readonly`), which matters for Step 1
below: switching to `IdClauses.SelectColumn(...)` makes them method calls, so they can no longer be
`const` and must become `static readonly`, matching the sibling `SystemImportActions.SelectColumns`
field two nested classes below in the same file (`Sql.cs:189-190`).

**`ImportBatch`'s full column list**, confirmed by reading `src/Quotinator.Data/Entities/ImportBatch.cs`
and its base class `src/Quotinator.Data/Models/RecordBase.cs`, cross-checked against the physical
`CREATE TABLE` column order in `src/Quotinator.Core/Database/QuotinatorMigrations.cs:493-509`
(`BaselineSchema`):

`Id`, `Name`, `Type`, `Url`, `ImportedAt`, `ImportedBy`, `RecordCount`, `DateCreated`, `DateModified`,
`DateDeleted`, `IsDeleted`, `ConflictPolicy`, `Status`, `AppliedAt`.

`Id` (`Guid`, from `RecordBase`) is the only `*Id`-suffixed column — `ImportedBy` ends in `By`, not
`Id`, and does not need wrapping.

**Why the guard test that "should" already cover this is currently vacuous, not red.** ADR 012's
`SqlSelectPresentationGuard` (`src/Quotinator.Data/Diagnostics/SqlSelectPresentationGuard.cs`) is a
regex-based scanner: it captures the `SELECT ... FROM` span and looks for `*Id`-suffixed column
*references* left unwrapped. `SELECT * FROM ImportBatches` has no column reference at all in that
span — just a literal `*` — so `FindUnwrappedSelectColumns` returns an empty list today, and
`SqlConstant_PassesSelectPresentationGuard` (`tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs:75-84`)
already passes for both `ImportBatches.SelectAll` and `ImportBatches.SelectByType` right now — not
because `Id` is protected, but because the guard cannot see it at all. This is exactly the "invisible
to the guard" framing in the issue title, and it means a plain "assert
`FindUnwrappedSelectColumns(sql)` is empty" test would still pass before this fix and prove nothing —
per `docs/testing-policy.md`'s canary rule ("negative/absence assertions need a canary, not just a
red-before-fix run"), the red-before-fix test for this issue has to assert something that is actually
false today: that the query text contains no literal `SELECT *`.

**Confirmed `SqlConstant_PassesSelectPresentationGuard`/`SqlConstant_PassesIdCaseGuard` need no new
`DynamicData` case added.** Both are parametrized via `AllNamedSqlConstants()`
(`SqlQueryGuardTests.cs:226-239`), which reflects over every `const`/`static readonly` string field
(and arrow-bodied `static string` property) on every nested type of `Sql`, including `ImportBatches`.
`ImportBatches.SelectAll`/`SelectByType` are already enumerated by these two generic tests today
(vacuously passing, as above) — once they become `static readonly` with an explicit column list, the
exact same two DynamicData-driven tests re-run against the new SQL text automatically, with no code
change to the test file itself. This means requirement 2 above is verified by *existing* test
infrastructure becoming meaningful, not by writing a new test case into it — see Step 1 for the
narrower, genuinely-red test that is still needed.

**Grep confirms `ImportBatches.SelectAll`/`SelectByType` are the only two literal `SELECT *` queries
in the codebase.** Ran `grep -rn '"SELECT \*'` and a second, case-insensitive `SELECT\s*\*` pass across
`src/` (both would also have caught `Quotinator.Core/Queries/Sql.cs`, which was searched too — it has
no `SELECT *` at all). Every other hit across both patterns is a comment or `<summary>`/`<remarks>` doc
reference (`Program.cs:325`, `RepositorySql.cs:20`, `EntityColumnMetadata.cs:9`,
`SqliteRepositoryBase.cs:32`) explaining why `SELECT *` is *not* used elsewhere — none is an actual
query string. **This confirms the issue's own "this may be the only remaining instance" claim** — no
new scope to flag.

**`SqliteImportBatchRepository`'s two call sites**, confirmed by reading
`src/Quotinator.Data/Repositories/SqliteImportBatchRepository.cs`:
`GetAllAsync` runs `Sql.ImportBatches.SelectAll` via `conn.QueryAsync<ImportBatch>(...)`;
`GetByTypeAsync` runs `Sql.ImportBatches.SelectByType` the same way, parameterised by
`type.ToString()`. Both map the result set onto `ImportBatch` by column name, so an explicit column
list works as a drop-in replacement as long as every property name is included and no alias is
misspelled — Dapper does not care about column order.

**Existing test coverage for these two methods, confirmed by grep across `tests/`:** `GetAllAsync` is
exercised only indirectly, via `SqliteImportActionService.ReverseBatchAsync`
(`src/Quotinator.Core/Services/SqliteImportActionService.cs:352`), which is covered end-to-end by the
large `ReverseBatchAsync_*` suite in `tests/Quotinator.Core.Tests/Services/SqliteImportActionServiceTests.cs`
— including cases that look up a batch by an upper-cased id string
(`ReverseBatchAsync_AlreadyReversed_ThrowsImportBatchNotFoundException`,
`ReverseBatchAsync_TopOfStack_ThenNextOldest_BothSucceedInOrder`), which already exercises the
`Id`/`Status`/`IsDeleted` fields the rewritten query must still populate correctly. `GetByTypeAsync`
has **zero** test coverage anywhere in the codebase today — it is never called from any `src/` code
path either (confirmed by grep: its only non-test, non-interface-declaration references are its own
implementation and two changelog entries recording its original addition). This is a pre-existing gap,
not something this issue's scope requires expanding to fix generally, but since this issue is rewriting
the exact SQL both methods run, Step 1 below adds direct repository-level coverage for both rather than
relying solely on `GetAllAsync`'s indirect coverage and leaving `GetByTypeAsync` completely unverified
by the very change that touches it.

**Tiers required — T2 only, not T1.** This change touches `src/Quotinator.Data/Queries/Sql.cs` and
`src/Quotinator.Data/Repositories/SqliteImportBatchRepository.cs` — no `.razor`/`.razor.cs`, no Blazor
service, no middleware, and (confirmed by grep) no reference from
`src/Quotinator.Core/Database/QuotinatorDatabaseInitializer.cs`, which only ever calls
`IImportBatchRepository.InsertAsync`/`UpdateAsync` (base-interface write methods), never
`GetAllAsync`/`GetByTypeAsync`. Per `docs/release-verification.md`'s T1 "When required" list (Blazor,
or `DatabaseInitializer`/migration/schema/reset logic), neither trigger applies, so T1 is not required.
T2 is always required for any code change regardless of trigger-matching (`docs/release-verification.md`
line 36), so it is declared here; the baseline smoke tests in CLAUDE.md's Pre-Push Checklist step 6
already cover it — no new scenario needs adding to that living checklist, since `ImportBatches` has no
dedicated HTTP listing endpoint and the only externally-observable effect of this change is that
`POST /import/actions/reverse` continues to behave exactly as it does today (already covered by that
checklist's "Reverse (undo)" section).

---

## Approach

**Revised during review (2026-07-23): use reflection-based column metadata, not a hand-typed list.**
The original draft of this plan followed `Sql.SystemImportActions.SelectColumns`'s hand-written-string
sibling pattern — but the developer flagged that `SELECT *`'s real advantage is that it never needs
updating when `ImportBatch`'s properties change, and any replacement must keep that same flexibility
rather than trading a guard gap for a manual-sync burden. This codebase already has the exact mechanism
for that: `Quotinator.Data.Repositories.ReflectedColumnMetadata` (`EntityColumnMetadata.cs`), built in
#210's own final round specifically to give `RepositorySql.cs`'s generic queries "an explicit, wrapped
column list instead of `SELECT *`" without hand-listing columns — it reflects `ImportBatch`'s
properties at runtime (cached per-`Type`, computed once) and infers id columns by the same `*Id`-suffix
convention `SqlSelectPresentationGuard` itself uses. `Sql.ImportBatches` is in the same assembly
(`Quotinator.Data`) as this mechanism and can use it directly.

**1. Promote `RepositorySql.BuildSelectColumns` from `private` to `internal`** (`RepositorySql.cs:24-26`)
so it can be shared instead of duplicated:

```csharp
// was: private static string BuildSelectColumns(IEntityColumnMetadata columns)
internal static string BuildSelectColumns(IEntityColumnMetadata columns)
    => string.Join(", ", columns.ValidColumnNames.Select(c =>
        columns.IdColumnNames.Contains(c) ? IdClauses.SelectColumn(c) : c));
```

No behaviour change — this one-line method already does exactly what `Sql.ImportBatches` needs; only
its accessibility changes.

**2. `Sql.ImportBatches.SelectAll`/`SelectByType`** (`src/Quotinator.Data/Queries/Sql.cs:115-133`) —
build their column list from `ReflectedColumnMetadata.For(typeof(ImportBatch))` once, at static
initialization, instead of listing columns by hand:

```csharp
// Column list shared by both SELECT queries below, built by reflecting over ImportBatch's own
// properties (not hand-typed) so this never needs updating when a property is added, removed, or
// renamed on ImportBatch — the same flexibility SELECT * provided, now combined with an explicit,
// guard-visible column list. Every *Id-suffixed column found this way is wrapped via
// IdClauses.SelectColumn. Not a const because this involves reflection + method calls, evaluated once
// per process (ReflectedColumnMetadata caches per-Type internally).
private static readonly string SelectColumns =
    RepositorySql.BuildSelectColumns(ReflectedColumnMetadata.For(typeof(ImportBatch)));

// ImportedAt has only whole-second precision, so two batches created within the same second
// (routine in tests, and possible in fast-successive real API calls) tie under ORDER BY
// ImportedAt DESC alone — SQLite does not guarantee a stable order for ties. ROWID DESC breaks
// the tie deterministically in insertion order (a consumer's own strict batch-undo stack may
// rely on this ordering being exact, not just "usually right" — found via a genuinely red test).
internal static readonly string SelectAll =
    $"SELECT {SelectColumns} FROM ImportBatches WHERE IsDeleted = 0 ORDER BY ImportedAt DESC, ROWID DESC;";

internal static readonly string SelectByType =
    $"SELECT {SelectColumns} FROM ImportBatches WHERE IsDeleted = 0 AND Type = @type ORDER BY ImportedAt DESC, ROWID DESC;";
```

`ReflectedColumnMetadata` is `internal sealed class` in `Quotinator.Data.Repositories` — `Sql.cs`
(`Quotinator.Data.Queries`) needs `using Quotinator.Data.Repositories;` added to reach both it and the
now-`internal` `RepositorySql.BuildSelectColumns`. Both are in the same assembly, so this is a plain
`internal` reference, not a project-boundary change.

The pre-existing ordering comment on `SelectAll` is preserved verbatim (it documents `ROWID DESC`, not
the column list, and is unaffected by this change).

**Why this, not the `RepositorySql`-generic-query path directly:** `RepositorySql.SelectPage`/`SelectById`
etc. are fully entity-agnostic (ADR 004) and only support the generic `WHERE`/`ORDER BY` shapes those
methods build themselves — they cannot express `SelectByType`'s `AND Type = @type` filter or `SelectAll`/
`SelectByType`'s shared `ImportedAt DESC, ROWID DESC` tie-break. `Sql.ImportBatches` stays a hand-written,
domain-specific query set (matching `Sql.SystemImportActions`'s sibling shape) for its custom `WHERE`/
`ORDER BY` clauses, while reusing `ReflectedColumnMetadata`/`BuildSelectColumns` — the column-list-building
half of `RepositorySql`'s machinery — for the part that genuinely is generic (which columns exist and
which of them are ids). This is a smaller, more targeted reuse than routing through `RepositorySql`'s
full generic-query builders, and it is the one that directly answers the developer's flexibility
requirement: the column list is never hand-typed anywhere.

**How this satisfies `SqlSelectPresentationGuard`:** `FindUnwrappedSelectColumns` strips every
`LOWER(...) AS alias` occurrence from the `SELECT ... FROM` span first, then flags any remaining
`*Id`-suffixed reference. After the rewrite, `Id` only ever appears as `LOWER(Id) AS Id` (stripped,
therefore invisible to the flagging pass) — no other selected column ends in `Id`, so nothing remains
to flag. `IdClauses.SelectColumn("Id")` with no explicit alias defaults the alias to the bare column
name (`IdClauses.cs:86-90`), producing exactly `LOWER(Id) AS Id`.

No change to `Sql.ImportBatches.UpdateRecordCount` or `DeleteAll` — neither is a `SELECT`, and
`UpdateRecordCount` already uses `IdClauses.Equals` for its `WHERE` clause (`Sql.cs:128-130`).

---

## Steps

### 1. Write the failing tests (red) and the new repository-level coverage

**Status:** Not started.

- `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs` — two new, standalone (non-`DynamicData`)
  test methods, genuinely red before the fix per this project's canary rule (see Background):
  - `ImportBatches_SelectAllAndSelectByType_DoNotUseSelectStar` — asserts
    `!Sql.ImportBatches.SelectAll.Contains("SELECT *")` and the same for `SelectByType`. Red today
    (both currently *are* `SELECT *`); green once Step 2 lands.
  - `ImportBatches_SelectAllAndSelectByType_WrapIdColumnViaLower` — asserts both queries' text contains
    `"LOWER(Id) AS Id"` (a positive presence assertion, not just an absence one, per
    `docs/testing-policy.md`'s canary guidance). Red today (the literal string isn't present in a
    `SELECT *` query); green once Step 2 lands.
- New file `tests/Quotinator.Data.Tests/Repositories/SqliteImportBatchRepositoryTests.cs` — direct
  repository-level round-trip coverage that does not exist today for either method (see Background):
  - `GetAllAsync_InsertedBatch_ReturnsAllPersistedFields` — insert an `ImportBatch` with every field
    populated (`Name`, `Type`, `Url`, `ImportedBy`, `RecordCount`, `ConflictPolicy`, `Status`,
    `AppliedAt`) via the repository's own `InsertAsync` (inherited from `SqliteRepository<T>`), call
    `GetAllAsync`, and assert every field round-trips correctly — including `Id` matching regardless of
    the casing used to construct the original `Guid` (canonical lowercase presentation, per ADR 012).
  - `GetAllAsync_TwoBatchesSameSecond_OrdersByRowidDescendingOnTie` — regression coverage for the
    `ROWID DESC` tie-break the SQL's own comment documents; not new behaviour, but was never directly
    exercised at the repository level before this issue touched the query.
  - `GetByTypeAsync_MixedTypes_ReturnsOnlyMatchingType` — insert one `Seed` and one `Import` batch,
    call `GetByTypeAsync(ImportBatchType.Seed)`, assert only the `Seed` row comes back with every field
    intact. This is the very first direct test `GetByTypeAsync` has ever had.
  These three are not bug-reproduction tests (the pre-fix `SELECT *` already returns correct data) —
  they are new coverage closing the pre-existing gap identified in Background, added because this issue
  is the one touching the exact SQL they exercise.
- `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs` — one more new test proving Spec
  requirement 5 (flexibility, not just correctness at a single point in time):
  - `ImportBatches_SelectColumns_ReflectsEveryImportBatchProperty` — reflect over
    `typeof(ImportBatch).GetProperties()` the same way `ReflectedColumnMetadata` itself does (excluding
    `[Write(false)]`/`[Computed]`), and assert every resulting property name appears somewhere in
    `Sql.ImportBatches.SelectAll`'s text (bare, or as a `LOWER(...) AS Name` alias for an id column).
    This is red today only in the sense that today's `SELECT *` query text trivially contains none of
    these names as literal tokens — the real point of this test is to keep passing automatically if
    `ImportBatch` ever gains, loses, or renames a property, without anyone needing to touch
    `Sql.ImportBatches` by hand. It is the test that actually proves the column list is reflection-driven
    rather than hand-typed, which a hand-typed list would also pass today but silently drift from
    tomorrow.

### 2. Implement the fix

**Status:** Not started.

Per the Approach section above:
1. Promote `RepositorySql.BuildSelectColumns` from `private` to `internal`.
2. Add `using Quotinator.Data.Repositories;` to `Sql.cs`.
3. Replace `Sql.ImportBatches`' hand-listed columns with
   `RepositorySql.BuildSelectColumns(ReflectedColumnMetadata.For(typeof(ImportBatch)))`, rewriting
   `SelectAll`/`SelectByType` to reference the resulting `SelectColumns` field, exactly as shown.

No changes to `SqliteImportBatchRepository.cs`, `ImportBatch.cs`, or any migration/baseline SQL — this is
a read-query-text-only change against an unchanged schema.

### 3. Canary-mutate the guard, confirm it actually catches an unwrapped Id, then revert

**Status:** Not started.

Per `docs/testing-policy.md`'s canary methodology: after Step 2 is green, temporarily change
`SelectColumns` to reference `Id` bare (unwrapped) instead of via `IdClauses.SelectColumn("Id")`, run
`SqlConstant_PassesSelectPresentationGuard` (the existing, generic `DynamicData`-driven test — no new
case needed, see Background), confirm it now fails specifically for the `ImportBatches.SelectAll`/
`SelectByType` cases with a clear assertion message, then revert the mutation (`git checkout` the file)
and reconfirm green. This proves the guard's coverage of `ImportBatches` is now genuine, not vacuous —
the actual thing this issue exists to fix. Record the outcome in this step's own Status line once done,
not in a separate Notes entry.

### 4. Full grep re-confirmation

**Status:** Not started.

Re-run `grep -rn '"SELECT \*'` and a case-insensitive `SELECT\s*\*` pass across both `src/Quotinator.Core/Queries/Sql.cs`
and `src/Quotinator.Data/Queries/Sql.cs` after the rewrite — confirm zero remaining literal `SELECT *`
query strings anywhere in either file (comments/doc references are not query strings and don't count).

### 5. Full suite and build verification

**Status:** Not started.

`dotnet build --configuration Release` (0 warnings/errors) and
`dotnet test --configuration Release --verbosity normal` (full suite green, 0 warnings/errors),
confirming no regression in the existing `ReverseBatchAsync_*` suite
(`tests/Quotinator.Core.Tests/Services/SqliteImportActionServiceTests.cs`) or the `ImportBatchesTests`
seeding/schema suite (`tests/Quotinator.Core.Tests/Repositories/ImportBatchesTests.cs`).

### 6. T2 verification

**Status:** Not started.

`docker build -f docker/Dockerfile -t quotinator:local .`, then run the "Reverse (undo)" section of
CLAUDE.md's Pre-Push Checklist step 6 smoke tests against the running container — no new scenario is
being added to that checklist (see Background's Tiers-required reasoning); this confirms the existing
scenario still passes unchanged with the rewritten query in place.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `ImportBatches.SelectAll`/`SelectByType` no longer use `SELECT *` | Unit test | `SqlQueryGuardTests.ImportBatches_SelectAllAndSelectByType_DoNotUseSelectStar` — starts red |
| 2 | ❌ | Both queries wrap `Id` via `LOWER(Id) AS Id` | Unit test | `SqlQueryGuardTests.ImportBatches_SelectAllAndSelectByType_WrapIdColumnViaLower` — starts red |
| 3 | ❌ | The column list is reflection-driven, not hand-typed — stays correct if `ImportBatch` ever gains, loses, or renames a property, with zero code change to `Sql.ImportBatches` | Unit test | `SqlQueryGuardTests.ImportBatches_SelectColumns_ReflectsEveryImportBatchProperty` — starts red |
| 4 | ❌ | Both queries genuinely pass `SqlSelectPresentationGuard` (not vacuously) | Unit test | `SqlQueryGuardTests.SqlConstant_PassesSelectPresentationGuard` (existing, `DynamicData`-driven — auto-covers `ImportBatches.SelectAll`/`SelectByType` once they're `static readonly`); canary-mutation confirmation per Step 3 |
| 5 | ❌ | Both queries still pass the id-case guard | Unit test | `SqlQueryGuardTests.SqlConstant_PassesIdCaseGuard` (existing, `DynamicData`-driven) |
| 6 | ❌ | No other hand-written `SELECT *` remains in either project's `Sql.cs` | Grep | `grep -rn '"SELECT \*'` and `grep -rniE 'SELECT\s*\*'` across `src/Quotinator.Core/Queries/Sql.cs` and `src/Quotinator.Data/Queries/Sql.cs` — zero query-string hits |
| 7 | ❌ | `GetAllAsync` returns every persisted `ImportBatch` field correctly | Unit test | `SqliteImportBatchRepositoryTests.GetAllAsync_InsertedBatch_ReturnsAllPersistedFields` |
| 8 | ❌ | `GetAllAsync` preserves the documented `ImportedAt DESC, ROWID DESC` tie-break | Unit test | `SqliteImportBatchRepositoryTests.GetAllAsync_TwoBatchesSameSecond_OrdersByRowidDescendingOnTie` |
| 9 | ❌ | `GetByTypeAsync` filters correctly and returns full field data | Unit test | `SqliteImportBatchRepositoryTests.GetByTypeAsync_MixedTypes_ReturnsOnlyMatchingType` |
| 10 | ❌ | No regression in `ReverseBatchAsync`'s existing indirect use of `GetAllAsync` | Unit test | `SqliteImportActionServiceTests` — full `ReverseBatchAsync_*` suite, e.g. `ReverseBatchAsync_TopOfStack_ThenNextOldest_BothSucceedInOrder`, `ReverseBatchAsync_AlreadyReversed_ThrowsImportBatchNotFoundException` |
| 11 | ❌ | No regression overall | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 12 | ❌ | T2 — Docker image builds and the existing Reverse (undo) smoke-test scenario still passes unchanged | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + CLAUDE.md Pre-Push Checklist step 6 "Reverse (undo)" section |

---

## Notes

Filed alongside #213, #214, #215 as sibling sub-issues of #207's final coverage audit (2026-07-22). This
issue is a **prerequisite for #213**: #213's own investigation independently found the same `SELECT *`
gap in these two queries and depends on this issue landing first. Revised during review (2026-07-23,
alongside this issue's own reflection-based rewrite): since `SelectColumns` now reflects `ImportBatch`'s
properties instead of listing them by hand, #213's rename from `ImportedBy` to `ImportedById` is picked
up automatically — #213 needs **zero** further code change to `Sql.ImportBatches` once this issue lands,
not even the one-line addition originally planned. See #213's plan doc Notes for the full ownership
split. #214 and #215 remain functionally independent of this issue.
