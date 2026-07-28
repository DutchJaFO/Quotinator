# #193 — Generic listable repository capability + DI registrations

**Status:** Waiting for release
**GitHub issue:** #193
**Tiers required:** T1, T2
**Depends on:** none

---

## Spec requirements (from the GitHub issue)

1. `RepositorySql.SelectPage(tableName, orderBy)` — parameterised `LIMIT`/`OFFSET`, `IsDeleted = 0`,
   stable `ORDER BY`.
2. A generic active-row count (`RepositorySql.CountActive`), plus an explicit recorded decision on the
   six existing per-entity `CountActive` constants.
3. `GetPageAsync(int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? = null)`
   returning `(IReadOnlyList<T> Items, int TotalCount)` on `SqliteRepository<T>`.
4. `pageSize = 0` reaches SQLite as no limit, never a literal `LIMIT 0`.
5. `IListableRepository<T> : IRepository<T>`, implemented by `SqliteRepository<T>`.
6. DI registrations for all six entities.
7. **Sort order is a caller-supplied, ordered list of `(column, direction)` pairs, not a single column
   name** — added during planning review (2026-07-17). The six target entities don't share a natural
   sort column (`Source.Title` vs `Character`/`Person`/`Series`/`Universe.Name` vs `Conversation`,
   which has neither), and a caller may reasonably want descending order or a secondary tiebreaker of
   its own. New `SortColumn(string Name, bool Descending = false)` type; `orderBy` defaults to
   `[new SortColumn("DateCreated")]` when null/empty. `Id` is always appended last, ascending, as a
   tiebreaker regardless of what's requested.
8. **Sort column names are validated two ways, at two layers** — also added during planning review.
   An identifier-shaped-but-nonexistent column must produce a clear error naming the column, not a raw
   `SqliteException` from Dapper:
   - `SqliteRepository<T>.GetPageAsync` checks each name against `SqliteRepositoryBase<T>`'s
     reflection-derived `ValidColumnNames` (T's actual persisted properties) and throws
     `ArgumentException` naming the bad column, before any SQL is built.
   - `RepositorySql.SelectPage` separately checks each name against an identifier-format regex
     (`^[A-Za-z_][A-Za-z0-9_]*$`) — defense in depth for any future caller that invokes it directly,
     bypassing the existence check.

---

## Background — why this issue exists

Sub-issue of #183. `Quotinator.Data.Repositories` already carries a generic repository abstraction,
but nothing in it can list a page of rows — every existing pagination implementation is hand-rolled in
`Quotinator.Engine`'s or `Quotinator.Data`'s own `Sql.cs`. Six entities (#184–#189) need paginated
listing, and Series/Universe have no repository registration of any kind.

Data layer only: no endpoints, no response DTOs, no HTTP concerns. Parameter validation is #195's;
by the time a value reaches this layer it is already valid.

---

## Steps

### 1. `SortColumn`, `ValidColumnNames`, `RepositorySql` factory methods

**Status:** ✅ Done (structural — no behaviour of its own to test red-first).

- `SortColumn(string Name, bool Descending = false)` — new file
  `src/Quotinator.Data/Repositories/SortColumn.cs`.
- `SqliteRepositoryBase<T>` gained `ValidColumnNames` — a reflection-derived `HashSet<string>` of
  `T`'s persisted property names, excluding any `[Write(false)]`/`[Computed]`-marked property (none
  exist in the codebase today; the exclusion matches Dapper.Contrib's actual persistence rules rather
  than assuming property name = column name always holds).
- `RepositorySql.SelectPage(tableName, orderBy)` and `RepositorySql.CountActive(tableName)` added
  alongside the existing `SelectById`/`SoftDelete`/`SelectDeleted`/`Restore`/`HardDelete`/`Purge`/
  `SelectByForeignKey`/`SelectJunctionRow`/`SelectByIds`. `SelectPage` defaults to `[DateCreated]` when
  `orderBy` is null/empty, always appends `Id` as a tiebreaker, and validates each column name against
  an identifier-format regex before interpolating.

These three are pure structure (a value type, a reflected set, string-building factory methods) with
no independent behaviour to red/green — the real behaviour under test is `GetPageAsync`, covered by
Step 2.

### 2. Red tests

**Status:** ✅ Done.

Added the 13 `GetPageAsync` tests to `Quotinator.Data.Tests.Repositories.SqliteRepositoryTests` (real
SQLite, per this project's DB-integration-tests rule), the 1 `RepositorySqlTests` test, and the 4 new
`RepositorySqlGuardTests.RepositorySqlCases()` entries. Confirmed genuinely red first: a stub
`GetPageAsync` (`throw new NotImplementedException()`) made all 13 `GetPageAsync` tests fail, while the
5 guard-test cases and the `RepositorySqlTests` test — pure SQL-string/regex assertions with no
dependency on `GetPageAsync` — passed immediately (13 failed / 23 passed of 36).

### 3. GetPageAsync + IListableRepository

**Status:** ✅ Done.

Implemented on `SqliteRepository<T>` exactly as designed: validates each `orderBy` column against
`ValidColumnNames` before building SQL, `pageSize == 0` maps to `LIMIT -1` at the C# call site, mirrors
`GetByIdAsync`'s existing `unitOfWork`/plain-connection two-branch shape. All 36 tests green afterward
(`dotnet test --filter` scoped to `GetPageAsync|RepositorySqlTests|RepositorySqlGuardTests`).

Add `GetPageAsync` directly on `SqliteRepository<T>` so `SqliteRestorableRepository<T>` — which
already extends it — inherits the capability with no extra work, mirroring how
`IRestorableRepository<T>` already extends `IRepository<T>`. Add `IListableRepository<T>` and have
`SqliteRepository<T>` declare it, so callers depend on the capability by interface. Validates each
`orderBy` column against `ValidColumnNames` before calling `RepositorySql.SelectPage`, throwing
`ArgumentException` naming the specific bad column.

`pageSize = 0` means "every row as one page" (#183's contract) and must reach SQLite as `LIMIT -1`,
decided at the C# call site (never a literal `LIMIT 0` baked into the SQL text).

### 4. Decide the CountActive overlap

**Status:** ✅ Decided (not a judgment call — forced by dependency direction).

`Quotinator.Engine`'s `Sql.cs` has six per-entity `CountActive` constants (Quotes, Characters, People,
Sources, Series, Universe), used today by seeding stats and `LogDatabaseStatsAsync`. `RepositorySql`
cannot reuse them: `CLAUDE.md` documents `Quotinator.Engine` → `Quotinator.Data`, never the reverse, so
`Quotinator.Data`'s `RepositorySql` cannot call into `Quotinator.Engine`'s `Sql.cs`. The six existing
constants are left untouched, serving their current callers unchanged; `RepositorySql.CountActive` is
a genuinely independent generic count (Step 1).

### 5. DI registrations

**Status:** ✅ Done.

`SeriesEntity`/`UniverseEntity` registered as fresh `SqliteRepository<T>` singletons; `Source`/
`Character`/`Person`/`ConversationEntity` resolve to their existing `IRestorableRepository<T>`
singleton via an explicit interface cast in the factory lambda (`IRestorableRepository<T>` and
`IListableRepository<T>` are sibling interfaces, both `: IRepository<T>` — the compiler cannot
implicitly convert one to the other even though `SqliteRestorableRepository<T>` implements both at
runtime, so the cast is required, not optional). Full solution builds 0 warnings/0 errors; full test
suite green (9/9 projects).

In `Program.cs`, alongside the existing `IRestorableRepository<T>` block:
- `IListableRepository<SeriesEntity>` / `IListableRepository<UniverseEntity>` — their first repository
  of any kind. Class names are `SeriesEntity`/`UniverseEntity`, carrying `[Table("Series")]`/
  `[Table("Universe")]`.
- `IListableRepository<T>` for `Source`, `Character`, `Person`, `ConversationEntity` resolving to
  their existing `IRestorableRepository<T>` singleton — a second interface binding, not a second
  object.

### 6. Verify

**Status:** ✅ Done — T1 and T2 both confirmed.

Full suite green (9/9 projects), 0 warnings, 0 errors. T2 confirmed via Docker: container starts,
`/api/v1/health` returns `200`, startup logs show no DI resolution failure for any of the 6 new
`IListableRepository<T>` registrations; baseline endpoints (`/quotes/random`, `/quotes` paginated,
`/quotes/search`) unaffected. T1 confirmed in Visual Studio: clean startup, no DI exception,
`/api/v1/quotes` (including `?page=15`, beyond the dataset) still `200`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A page returns the requested count and the correct total | Unit test | `SqliteRepositoryTests.GetPageAsync_FirstPage_ReturnsRequestedCountAndTotal` |
| 2 | ✅ | Soft-deleted rows are excluded | Unit test | `GetPageAsync_ExcludesSoftDeletedRows` |
| 3 | ✅ | A partially-full last page returns the remainder, not an error | Unit test | `GetPageAsync_LastPagePartiallyFull_ReturnsRemainderNotAnError` |
| 4 | ✅ | `pageSize` exceeding available items returns all of them | Unit test | `GetPageAsync_PageSizeExceedsAvailableItems_ReturnsAllOfThem` |
| 5 | ✅ | `pageSize = 0` returns every row as one page (never zero rows) | Unit test | `GetPageAsync_PageSizeZero_ReturnsEveryRowAsOnePage` |
| 6 | ✅ | A page beyond the last returns empty items with the correct total | Unit test | `GetPageAsync_PageBeyondLastPage_ReturnsEmptyItemsWithCorrectTotal`. At this layer an out-of-range page is legitimately empty; #195 turns it into a 422 |
| 7 | ✅ | Order is stable across pages — no row repeated or skipped | Unit test | `GetPageAsync_StableOrderAcrossPages_NoRowRepeatedOrSkipped` |
| 8 | ✅ | `TotalCount` reports all active rows, ignoring paging | Unit test | `GetPageAsync_TotalCountIgnoresPaging_ReportsAllActiveRows` |
| 9 | ✅ | A caller-supplied sort column is honoured | Unit test | `GetPageAsync_CustomOrderByColumn_SortsByThatColumn` (`orderBy: [new SortColumn("Label")]`) |
| 10 | ✅ | Descending order is honoured | Unit test | `GetPageAsync_DescendingOrder_SortsInReverse` |
| 11 | ✅ | Multiple sort columns apply in order, secondary breaking ties on the primary | Unit test | `GetPageAsync_MultiColumnOrder_SortsByBothColumnsInOrder` |
| 12 | ✅ | An identifier-shaped but nonexistent column throws a clear error naming it, not a raw `SqliteException` | Unit test | `GetPageAsync_UnknownColumn_ThrowsArgumentExceptionNamingTheColumn` |
| 13 | ✅ | A SQL-injection-shaped column is rejected before any SQL runs | Unit test | `GetPageAsync_SqlInjectionShapedColumn_ThrowsArgumentException` |
| 14 | ✅ | `RepositorySql.SelectPage`'s own identifier-format guard holds for a direct caller, independent of `GetPageAsync`'s existence check | Unit test | `RepositorySqlTests.SelectPage_ColumnNameNotIdentifierShaped_ThrowsArgumentException` |
| 15 | ✅ | The new factory methods are covered by `RepositorySql`'s own guard | Unit test | `RepositorySqlGuardTests` — 4 new `RepositorySqlCases()` entries (default order, single column, descending, multi-column); **not** picked up by `SqlQueryGuardTests`'s reflected matrix, which only walks `Quotinator.Data.Queries.Sql`'s `const string` fields and cannot see `RepositorySql`'s interpolated-string methods |
| 16 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 9/9 test projects, all passed, 0 warnings, 0 errors |
| 17 | ✅ | T1 — app starts in Visual Studio with the new DI registrations resolving | Live (T1) | Developer confirmed (2026-07-17): clean startup, no DI exception, `/api/v1/quotes` (including `?page=15`, beyond the dataset) still `200` |
| 18 | ✅ | T2 — container starts and serves traffic with the new registrations | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + run + `curl -s http://localhost:8080/api/v1/health` → `200 {"status":"healthy"}`; startup logs show all 6 new `IListableRepository<T>` singletons resolved with no DI error. Also re-confirmed baseline endpoints unaffected: `/quotes/random`, `/quotes?page=1&pageSize=2`, `/quotes/search?q=love` all `200` with normal data — nothing consumes `IListableRepository<T>` yet, so this is regression coverage on the existing surface, not new-feature coverage |

---

## Notes

T1 and T2 are both required — this adds DI registrations resolved at startup, and a missing or
mis-typed binding fails at container build/startup rather than in any unit test.

This issue registers repositories that nothing consumes yet: #184–#189 are the first callers. That is
deliberate, matching #183's own no-new-routes boundary — but it does mean T1/T2 can only prove the
registrations resolve, not that they return correct data over HTTP. The `Quotinator.Data.Tests` suite
carries that burden instead, against a real SQLite database.
