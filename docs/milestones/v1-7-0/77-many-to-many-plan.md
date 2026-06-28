# Issue #77 — Add many-to-many relationship pattern to Quotinator.Data

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/77  
**Milestone:** v1.7.0  
**Status:** 🟡 Code complete — pending release | T1 ⬜ T2 ⬜

---

## Depends on

- **#73** (audit trail) — `SqliteLinkRepository` delegates all writes to `SqliteRestorableRepository<TJunction>`; link, unlink, and restore operations are audited automatically
- **#74** (read-model query pattern) — `IUnitOfWork` is the established transaction coordination mechanism
- **#75** / **#76** — all relationship patterns share the `RecordBase`-everywhere and `IUnitOfWork`-optional-parameter conventions

---

## Problem

`IRepository<T>` maps `T` to one table. There is no mechanism to manage junction rows, load an entity with its related collection, or add and remove individual links between entities.

---

## Key design decision — `RecordBase` on junction tables (non-negotiable)

Every table — including junction tables — uses `RecordBase`. Junction tables get a synthetic `Guid Id` as PK and a `UNIQUE` constraint on the FK pair. Reasoning: schema additions to existing tables require tested migrations; adding `RecordBase` at table-creation time is free. See issue body for the full argument.

```sql
CREATE TABLE WidgetTags (
    Id           TEXT    NOT NULL PRIMARY KEY,
    WidgetId     TEXT    NOT NULL REFERENCES Widgets(Id),
    TagId        TEXT    NOT NULL REFERENCES Tags(Id),
    DateCreated  TEXT,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0,
    UNIQUE (WidgetId, TagId)
);
```

---

## Phase A — New `Quotinator.Data.Example` project (retroactive refactor)

All example entity classes and canonical concrete repository implementations that currently exist as inline definitions inside test files must move to a new dedicated project.

### Rationale

Example entity classes (`Widget`, `WidgetLine`, `WidgetDetail`, etc.) are defined `public` to satisfy Dapper's expression-tree instantiation requirement. Defined inside `Quotinator.Data.Tests`, they sit in `Quotinator.Data.Tests.Repositories` — the same namespace as production-facing test assertions. This conflates "example domain model" with "test assertions." Moving them to `Quotinator.Data.Example` separates the two concerns and makes the examples independently useful as documentation.

### Project location and namespace

```
tests/Quotinator.Data.Example/
  Quotinator.Data.Example.csproj     → root namespace: Quotinator.Data.Example
  Common/                            → Quotinator.Data.Example.Common
    Widget.cs
  MasterDetail/                      → Quotinator.Data.Example.MasterDetail
    WidgetLine.cs
    WidgetWithLinesRepository.cs
  OneToOne/                          → Quotinator.Data.Example.OneToOne
    WidgetDetail.cs
    WidgetDetailFk.cs
    WidgetWithDetailRepository.cs
    WidgetWithFkDetailRepository.cs
  ManyToMany/                        → Quotinator.Data.Example.ManyToMany
    Tag.cs
    WidgetTag.cs
    WidgetTagLinkRepository.cs
```

`Widget` lives in `Common/` — it is the shared domain entity used by every example variant. Each subfolder contains only the types specific to that relationship pattern.

### Project file

References `Quotinator.Data` (for the base classes) and `Dapper.Contrib` (for `[Table]`/`[ExplicitKey]` attributes applied in entity classes). No `InternalsVisibleTo` entry is needed — the example project uses only public types from `Quotinator.Data`.

### What moves

| Source (current) | Destination |
|-----------------|-------------|
| `Widget` in `SqliteRepositoryTests.cs` | `Common/Widget.cs` |
| `WidgetLine` in `AggregateRepositoryTests.cs` | `MasterDetail/WidgetLine.cs` |
| `WidgetDetail` in `OneToOneRepositoryTests.cs` | `OneToOne/WidgetDetail.cs` |
| `WidgetDetailFk` in `OneToOneRepositoryTests.cs` | `OneToOne/WidgetDetailFk.cs` |
| Concrete `SharedPkRepo` (private inner class) | `OneToOne/WidgetWithDetailRepository.cs` (public sealed) |
| Concrete `SeparateFkRepo` (private inner class, had Func) | `OneToOne/WidgetWithFkDetailRepository.cs` (public sealed, Func removed — fixed implementation) |
| (new for #77) `Tag`, `WidgetTag` | `ManyToMany/Tag.cs`, `ManyToMany/WidgetTag.cs` |
| (new for #77) Concrete `WidgetTagLinkRepository` | `ManyToMany/WidgetTagLinkRepository.cs` (public sealed) |

### Test files after migration

- Remove inline entity definitions from all test files
- Add `using Quotinator.Data.Example.Common;` (and pattern-specific namespaces) to each affected test file
- Add `<ProjectReference>` to `Quotinator.Data.Example` in `Quotinator.Data.Tests.csproj`
- `AggregateRepositoryTests.cs` — its private `WidgetWithLinesRepository` took a `Func<Widget, IReadOnlyList<WidgetLine>>` and `InsertStrategy` for test flexibility. This private class stays in the test file (referencing `Widget` and `WidgetLine` from the example project). The example project's `WidgetWithLinesRepository` is the canonical, non-parameterised form.
- `OneToOneRepositoryTests.cs` — `SharedPkRepo` and `SeparateFkRepo` are replaced by the public example classes; no private inner class needed.
- Add `Quotinator.Data.Example` to `Quotinator.slnx` as a new project node

---

## Phase B — `ILinkRepository` implementation

### `ILinkRepository<TLeft, TRight>`

`TJunction` is not on the interface — it is an implementation detail of the concrete class. Consumers inject `ILinkRepository<Widget, Tag>` without caring which junction table backs it.

```csharp
public interface ILinkRepository<TLeft, TRight>
    where TLeft  : RecordBase
    where TRight : RecordBase
{
    Task LinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task UnlinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task RestoreLinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<TRight>> GetRightAsync(Guid leftId, IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<TLeft>>  GetLeftAsync(Guid rightId, IUnitOfWork? unitOfWork = null);
}
```

### `SqliteLinkRepository<TLeft, TRight, TJunction>`

Abstract base. Delegates all junction-table writes to an internal `SqliteRestorableRepository<TJunction>` — no direct Dapper calls in link/unlink/restore paths. All three table names resolved from `[Table]` attributes at static construction time.

**Abstract members (provided by concrete subclass):**

```csharp
protected abstract string    LeftFkColumn  { get; }
protected abstract string    RightFkColumn { get; }
protected abstract TJunction CreateJunction(Guid leftId, Guid rightId);
```

`LeftFkColumn` and `RightFkColumn` are also used via reflection to extract FK Guid values from loaded `TJunction` rows, avoiding two additional abstract accessors.

### `LinkAsync` — check-then-restore-or-insert (not `INSERT OR IGNORE`)

`INSERT OR IGNORE` bypasses `InsertAsync` and produces no audit entry.

1. Query the junction table for any row (active **or** soft-deleted) — uses `RepositorySql.SelectJunctionRow`
2. Result is null → `InsertAsync(CreateJunction(leftId, rightId))` — audit: Insert
3. Result has `IsDeleted = 1` → `RestoreAsync(row.Id)` — audit: Restore
4. Result has `IsDeleted = 0` → no-op (idempotent)

### `UnlinkAsync`

1. Query for a row (active or soft-deleted)
2. Active (`IsDeleted = 0`) → `SoftDeleteAsync(row.Id)` — audit: SoftDelete
3. Not found or already soft-deleted → no-op

### `RestoreLinkAsync`

1. Query for a row
2. Soft-deleted (`IsDeleted = 1`) → `RestoreAsync(row.Id)` — audit: Restore
3. Not found or already active → no-op

### `GetRightAsync` / `GetLeftAsync` — two-query approach

Two SQL round-trips regardless of N:

1. Load all active junction rows for the given ID — reuses `RepositorySql.SelectByForeignKey` (FK filter, `IsDeleted = 0` on junction)
2. Extract related entity IDs; load all in one statement — uses new `RepositorySql.SelectByIds`

Soft-deleted `TRight`/`TLeft` entities are naturally excluded by `IsDeleted = 0` on the entity table.

### Cascade deletion — out of scope

`SqliteLinkRepository` does not cascade soft-deletes to junction rows when a linked entity is removed. Whether junction rows should follow is use-case-specific. The concrete entity repository documents and implements its own cascade strategy.

---

## New `RepositorySql` factory methods

```csharp
// Finds a junction row by FK pair — no IsDeleted filter (LinkAsync must see soft-deleted rows too).
internal static string SelectJunctionRow(string tableName, string leftFkColumn, string rightFkColumn)
    => $"SELECT * FROM [{tableName}] WHERE [{leftFkColumn}] = @leftId AND [{rightFkColumn}] = @rightId";

// Loads a set of entities by primary key list — Dapper expands @ids from IEnumerable<string>.
internal static string SelectByIds(string tableName)
    => $"SELECT * FROM [{tableName}] WHERE [Id] IN @ids AND [IsDeleted] = 0";
```

Both get a case in `RepositorySqlGuardTests`.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| **Phase A — example project** | | | | |
| 1 | ✅ | `Quotinator.Data.Example` project exists; builds clean; root namespace is `Quotinator.Data.Example` | Build | `dotnet build --configuration Release` — 0 errors, 0 warnings |
| 2 | ✅ | `Widget` moved to `Quotinator.Data.Example.Common`; removed from `SqliteRepositoryTests.cs` | Code review | `tests/Quotinator.Data.Example/Common/Widget.cs` exists; no duplicate in test namespace |
| 3 | ✅ | `WidgetLine` moved to `Quotinator.Data.Example.MasterDetail`; removed from `AggregateRepositoryTests.cs` | Code review | `tests/Quotinator.Data.Example/MasterDetail/WidgetLine.cs` exists; no duplicate in test namespace |
| 4 | ✅ | `WidgetDetail`, `WidgetDetailFk` moved to `Quotinator.Data.Example.OneToOne`; removed from `OneToOneRepositoryTests.cs` | Code review | Files exist in `OneToOne/`; no duplicate definitions in test namespace |
| 5 | ✅ | `WidgetWithDetailRepository`, `WidgetWithFkDetailRepository` moved to example project as public sealed classes | Code review | Files exist in `OneToOne/`; private inner classes removed from test file |
| 6 | ✅ | All existing tests still pass after entity migration | Build | `dotnet test --configuration Release` — 142 Quotinator.Data.Tests passed, 0 warnings |
| **Phase B — `ILinkRepository`** | | | | |
| 7 | ✅ | `RepositorySql.SelectJunctionRow` passes CVE aggregate guard | Unit test | `RepositorySqlGuardTests.RepositorySqlFactory_PassesAggregateGuard["SelectJunctionRow"]` |
| 8 | ✅ | `RepositorySql.SelectByIds` passes CVE aggregate guard | Unit test | `RepositorySqlGuardTests.RepositorySqlFactory_PassesAggregateGuard["SelectByIds"]` |
| 9 | ✅ | `ILinkRepository<TLeft, TRight>` exists in `Quotinator.Data`; 5 methods; all accept `IUnitOfWork?` | Build | `dotnet build --configuration Release` — 0 errors |
| 10 | ✅ | `SqliteLinkRepository<TLeft, TRight, TJunction>` abstract base exists; 3 abstract members; delegates writes to `SqliteRestorableRepository<TJunction>` | Build | `dotnet build --configuration Release` — 0 errors |
| 11 | ✅ | `Tag`, `WidgetTag`, `WidgetTagLinkRepository` added to `Quotinator.Data.Example.ManyToMany` | Code review | Files exist in `tests/Quotinator.Data.Example/ManyToMany/` |
| 12 | ✅ | `LinkAsync` inserts a new junction row when none exists | Integration test | `LinkRepositoryTests.LinkAsync_InsertsJunctionRow` |
| 13 | ✅ | `LinkAsync` is idempotent — duplicate link is a no-op | Integration test | `LinkRepositoryTests.LinkAsync_IsIdempotent_WhenAlreadyLinked` |
| 14 | ✅ | `LinkAsync` restores a soft-deleted junction row rather than inserting a duplicate | Integration test | `LinkRepositoryTests.LinkAsync_RestoresSoftDeletedRow` |
| 15 | ✅ | `LinkAsync` (new) produces an Insert audit entry | Integration test | `LinkRepositoryTests.LinkAsync_NewLink_WritesInsertAuditEntry` |
| 16 | ✅ | `LinkAsync` (restore) produces a Restore audit entry | Integration test | `LinkRepositoryTests.LinkAsync_Restore_WritesRestoreAuditEntry` |
| 17 | ✅ | `UnlinkAsync` soft-deletes the junction row | Integration test | `LinkRepositoryTests.UnlinkAsync_SoftDeletesJunctionRow` |
| 18 | ✅ | `UnlinkAsync` produces a SoftDelete audit entry | Integration test | `LinkRepositoryTests.UnlinkAsync_WritesSoftDeleteAuditEntry` |
| 19 | ✅ | `UnlinkAsync` when no junction row exists is a no-op | Integration test | `LinkRepositoryTests.UnlinkAsync_IsNoOp_WhenNotLinked` |
| 20 | ✅ | `RestoreLinkAsync` restores a soft-deleted junction row | Integration test | `LinkRepositoryTests.RestoreLinkAsync_RestoresSoftDeletedRow` |
| 21 | ✅ | `RestoreLinkAsync` produces a Restore audit entry | Integration test | `LinkRepositoryTests.RestoreLinkAsync_WritesRestoreAuditEntry` |
| 22 | ✅ | `GetRightAsync` returns all active `TRight` entities linked to the given left ID | Integration test | `LinkRepositoryTests.GetRightAsync_ReturnsLinkedTags` |
| 23 | ✅ | `GetRightAsync` excludes soft-deleted links | Integration test | `LinkRepositoryTests.GetRightAsync_ExcludesSoftDeletedLinks` |
| 24 | ✅ | `GetLeftAsync` returns all active `TLeft` entities linked to the given right ID | Integration test | `LinkRepositoryTests.GetLeftAsync_ReturnsLinkedWidgets` |
| 25 | ✅ | `GetLeftAsync` excludes soft-deleted links | Integration test | `LinkRepositoryTests.GetLeftAsync_ExcludesSoftDeletedLinks` |
| 26 | ✅ | `LinkAsync` within a caller `IUnitOfWork` — rollback leaves no junction row | Integration test | `LinkRepositoryTests.LinkAsync_WithUoW_RollsBackOnAbort` |
| 27 | ✅ | `docs/data-access.md` updated with many-to-many section | Code review | Section present; layout, abstract members, cascade note all documented |
| 28 | ✅ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` — 0 Warning(s) 0 Error(s) |
| 29 | ✅ | All tests pass | Build | `dotnet test --configuration Release` — 142 Quotinator.Data.Tests passed |
| 30 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |

### T1 / T2 / T3

| Tier | Required | Items |
|------|----------|-------|
| T1 | ✅ Required | App starts in VS without error; startup banner shows no exceptions |
| T2 | ✅ Required | `docker build -f docker/Dockerfile -t quotinator:local .` succeeds |
| T3 | ➖ Not required | No HA-specific behaviour — pure `Quotinator.Data` infrastructure |
