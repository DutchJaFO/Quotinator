# #193 — Generic listable repository capability + DI registrations

**Status:** Planning
**GitHub issue:** #193
**Tiers required:** T1, T2
**Depends on:** none

---

## Spec requirements (from the GitHub issue)

1. `RepositorySql.SelectPage(tableName)` — parameterised `LIMIT`/`OFFSET`, `IsDeleted = 0`, stable
   `ORDER BY`.
2. A generic active-row count, plus an explicit recorded decision on the six existing per-entity
   `CountActive` constants.
3. `GetPageAsync(int page, int pageSize, IUnitOfWork? = null)` returning
   `(IReadOnlyList<T> Items, int TotalCount)` on `SqliteRepository<T>`.
4. `pageSize = 0` reaches SQLite as no limit, never a literal `LIMIT 0`.
5. `IListableRepository<T> : IRepository<T>`, implemented by `SqliteRepository<T>`.
6. DI registrations for all six entities.

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

### 1. Red tests

**Status:** Not started.

Write the eight failing tests in `Quotinator.Data.Tests` against a real SQLite database (per this
project's DB-integration-tests rule — no fakes for repository behaviour), confirming each is genuinely
red before implementation.

### 2. RepositorySql factory methods

**Status:** Not started.

Add `SelectPage(tableName)` and the generic count alongside the existing `SelectById`/`SoftDelete`/
`SelectDeleted`/`Restore`/`HardDelete`/`Purge`. Table names come from the `[Table]` attribute and are
interpolated — safe for the reason `RepositorySql`'s own class remarks already document (developer
-controlled metadata, not user input; SQLite cannot parameterise identifiers).

The `ORDER BY` must be stable, or `LIMIT`/`OFFSET` can repeat or skip a row across pages — SQLite
gives no ordering guarantee without one.

### 3. Decide the CountActive overlap

**Status:** Not started.

`Quotinator.Engine`'s `Sql.cs` already has six per-entity `CountActive` constants (Quotes, Characters,
People, Sources, Series, Universe), used today by seeding stats and `LogDatabaseStatsAsync`. A generic
`RepositorySql` count would overlap all six. Decide — reuse, supersede, or leave them to their current
callers — and record the decision here rather than adding a second way to count by default.

### 4. GetPageAsync + IListableRepository

**Status:** Not started.

Add `GetPageAsync` directly on `SqliteRepository<T>` so `SqliteRestorableRepository<T>` — which
already extends it — inherits the capability with no extra work, mirroring how
`IRestorableRepository<T>` already extends `IRepository<T>`. Add `IListableRepository<T>` and have
`SqliteRepository<T>` declare it, so callers depend on the capability by interface.

`pageSize = 0` means "every row as one page" (#183's contract) and must reach SQLite as no limit
(`LIMIT -1` or an omitted clause), never a literal `LIMIT 0`.

### 5. DI registrations

**Status:** Not started.

In `Program.cs`, alongside the existing `IRestorableRepository<T>` block:
- `IListableRepository<SeriesEntity>` / `IListableRepository<UniverseEntity>` — their first repository
  of any kind. Class names are `SeriesEntity`/`UniverseEntity`, carrying `[Table("Series")]`/
  `[Table("Universe")]`.
- `IListableRepository<T>` for `Source`, `Character`, `Person`, `ConversationEntity` resolving to
  their existing `IRestorableRepository<T>` singleton — a second interface binding, not a second
  object.

### 6. Verify

**Status:** Not started.

Full suite green, 0 warnings. T1/T2 confirm the app still starts with the new registrations.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A page returns the requested count and the correct total | Unit test | `Quotinator.Data.Tests.GetPageAsync_FirstPage_ReturnsRequestedCountAndTotal` — starts red |
| 2 | ❌ | Soft-deleted rows are excluded | Unit test | `GetPageAsync_ExcludesSoftDeletedRows` — starts red |
| 3 | ❌ | A partially-full last page returns the remainder, not an error | Unit test | `GetPageAsync_LastPagePartiallyFull_ReturnsRemainderNotAnError` — starts red |
| 4 | ❌ | `pageSize` exceeding available items returns all of them | Unit test | `GetPageAsync_PageSizeExceedsAvailableItems_ReturnsAllOfThem` — starts red |
| 5 | ❌ | `pageSize = 0` returns every row as one page (never zero rows) | Unit test | `GetPageAsync_PageSizeZero_ReturnsEveryRowAsOnePage` — starts red |
| 6 | ❌ | A page beyond the last returns empty items with the correct total | Unit test | `GetPageAsync_PageBeyondLastPage_ReturnsEmptyItemsWithCorrectTotal` — starts red. At this layer an out-of-range page is legitimately empty; #195 turns it into a 422 |
| 7 | ❌ | Order is stable across pages — no row repeated or skipped | Unit test | `GetPageAsync_StableOrderAcrossPages_NoRowRepeatedOrSkipped` — starts red |
| 8 | ❌ | `TotalCount` reports all active rows, ignoring paging | Unit test | `GetPageAsync_TotalCountIgnoresPaging_ReportsAllActiveRows` — starts red |
| 9 | ❌ | The new factory methods are covered by the SQL aggregate guard | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests` — reflected matrix picks them up automatically; confirm the documented aggregate inventory still matches |
| 10 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 11 | ❌ | T1 — app starts in Visual Studio with the new DI registrations resolving | Live (T1) | Developer to confirm in Visual Studio |
| 12 | ❌ | T2 — container starts and serves traffic with the new registrations | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `curl -s http://localhost:8080/api/v1/health` |

---

## Notes

T1 and T2 are both required — this adds DI registrations resolved at startup, and a missing or
mis-typed binding fails at container build/startup rather than in any unit test.

This issue registers repositories that nothing consumes yet: #184–#189 are the first callers. That is
deliberate, matching #183's own no-new-routes boundary — but it does mean T1/T2 can only prove the
registrations resolve, not that they return correct data over HTTP. The `Quotinator.Data.Tests` suite
carries that burden instead, against a real SQLite database.
