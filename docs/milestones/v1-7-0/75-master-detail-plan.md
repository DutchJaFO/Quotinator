# Plan: #75 — Add master/detail repository pattern to Quotinator.Data for parent/child table relationships

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/75  
**Milestone:** v1.7.0  
**Status:** 🟡 Code complete — pending release | T1 ✅ T2 ✅

---

## Depends on

- **#73** (audit trail) — `IAuditWriter` and `ICallerContext` are required constructor parameters on every `SqliteRepository<T>` derivation; `IAuditWriter` also receives a bulk overload in this issue
- **#74** (read-model query pattern) — `IUnitOfWork` is the established transaction mechanism; `IRepository<T>` already has `IUnitOfWork? unitOfWork = null` on all mutating methods

---

## Summary

Pattern A (separate repos, composed by the caller) and Pattern B (aggregate root) are not distinct patterns — they are the same unit-of-work coordination used at different call sites. The real deliverables are:

1. **`TransactionScope` static helper** — removes UoW boilerplate from every call site
2. **`InsertStrategy` enum** — `Bulk` (one SQL round-trip) or `Sequential` (per-row, identifies failing row)
3. **`InsertManyAsync` on `IRepository<T>`** — bulk or sequential insert + matching audit write in one pass
4. **`IAuditWriter` bulk overload** — required by `InsertManyAsync` in `Bulk` mode
5. **`AggregateRepository<TParent, TChild>` generic base** — encapsulates the parent+children pattern; exposes `ChildInsertStrategy` for subclasses to override

---

## `TransactionScope` static helper

Wraps the `SqliteUnitOfWork` lifecycle. Accepts an optional existing `IUnitOfWork`:
- If `existing` is provided — calls `work(existing)` and **does not commit** (the caller owns the transaction)
- If `existing` is null — creates a new `SqliteUnitOfWork`, calls `work(uow)`, commits

```csharp
public static class TransactionScope
{
    public static async Task ExecuteAsync(
        IDbConnectionFactory factory,
        Func<IUnitOfWork, Task> work,
        IUnitOfWork? existing = null)
    {
        if (existing != null)
        {
            await work(existing);
            return;
        }
        await using var uow = new SqliteUnitOfWork(factory);
        await uow.BeginTransactionAsync();
        await work(uow);
        await uow.CommitAsync();
    }
}
```

---

## `InsertStrategy` enum

```csharp
public enum InsertStrategy
{
    Bulk,        // one SQL round-trip for all rows + one bulk audit write — fastest
    Sequential   // loops through InsertAsync per row — each row's failure is identifiable
}
```

**When to choose Sequential:** the caller needs to know which specific row failed (e.g. a batch importer reporting per-line errors), or business logic must execute between each insert. Within a shared `IUnitOfWork`, a failure in sequential mode still rolls back all rows committed so far in that transaction — the difference is that the exception identifies the failing entity, not just "the batch failed."

---

## `InsertManyAsync` — layered across the class hierarchy

Defined on `SqliteRepositoryBase<T>` so every derived type benefits, including `AuditWriter` itself.

### `SqliteRepositoryBase<T>` — data INSERT only, no audit

```csharp
// Pure data insert — no audit wiring (safe for AuditWriter to inherit without recursion)
public virtual async Task InsertManyAsync(
    IEnumerable<T> entities,
    IUnitOfWork? unitOfWork = null,
    InsertStrategy strategy = InsertStrategy.Bulk)
{
    var list = entities.ToList();
    await TransactionScope.ExecuteAsync(Factory, async uow =>
    {
        if (strategy == InsertStrategy.Bulk)
            await uow.Connection.InsertAsync(list, uow.Transaction);
        else
            foreach (var entity in list)
                await uow.Connection.InsertAsync(entity, uow.Transaction);
    }, unitOfWork);
}
```

### `IRepository<T>` — interface declaration

```csharp
Task InsertManyAsync(IEnumerable<T> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk);
```

### `SqliteRepository<T>` — override adds audit write matched to strategy

```csharp
public override async Task InsertManyAsync(
    IEnumerable<T> entities,
    IUnitOfWork? unitOfWork = null,
    InsertStrategy strategy = InsertStrategy.Bulk)
{
    var list = entities.ToList();
    await TransactionScope.ExecuteAsync(Factory, async uow =>
    {
        if (strategy == InsertStrategy.Bulk)
        {
            await uow.Connection.InsertAsync(list, uow.Transaction);
            var entries = list.Select(e => BuildEntry(AuditOperation.Insert, e.Id)).ToList();
            await _auditWriter.WriteAsync(entries, uow.Connection, uow.Transaction);  // one bulk audit INSERT
        }
        else
        {
            foreach (var entity in list)
                await InsertAsync(entity, uow);  // reuses single-entry path — one audit entry per row
        }
    }, unitOfWork);
}
```

`SqliteRestorableRepository<T>` inherits the override for free.

---

## `IAuditWriter` bulk overload

```csharp
// New overload alongside the existing single-entry WriteAsync
Task WriteAsync(IReadOnlyList<AuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null);
```

The `AuditWriter` implementation delegates to `base.InsertManyAsync` (inherited from `SqliteRepositoryBase<AuditEntry>`) — one SQL round-trip for N entries, no recursion risk.

---

## `AggregateRepository<TParent, TChild>` generic base

Encapsulates the parent+children write covenant. `unitOfWork` is passed through to `TransactionScope` so the aggregate can participate in a caller's transaction.

```csharp
public abstract class AggregateRepository<TParent, TChild>(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteRepository<TParent>(factory, auditWriter, callerContext)
    where TParent : RecordBase
    where TChild  : RecordBase
{
    protected abstract IReadOnlyList<TChild> GetChildren(TParent parent);
    protected abstract SqliteRepository<TChild> ChildRepository { get; }

    // Override to Sequential when the concrete aggregate needs per-row error identification
    protected virtual InsertStrategy ChildInsertStrategy => InsertStrategy.Bulk;

    public override async Task InsertAsync(TParent parent, IUnitOfWork? unitOfWork = null)
    {
        await TransactionScope.ExecuteAsync(Factory, async uow =>
        {
            await base.InsertAsync(parent, uow);
            await ChildRepository.InsertManyAsync(GetChildren(parent), uow, ChildInsertStrategy);
        }, unitOfWork);
    }
}
```

Navigation properties (`GetChildren`) are **write-only** — populated by the caller, never populated by read methods. Reads go through `JoinQueryRepository` / `IJoinStrategy` (per #74).

---

## Approach

1. Add `InsertStrategy` enum to `Quotinator.Data`
2. Add `TransactionScope` static helper to `Quotinator.Data`
3. Add bulk `WriteAsync` overload to `IAuditWriter` and its `AuditWriter` implementation
4. Add `InsertManyAsync(entities, unitOfWork?, strategy?)` to `SqliteRepositoryBase<T>` (data only), `IRepository<T>` (interface), and `SqliteRepository<T>` (override with audit)
5. Add `AggregateRepository<TParent, TChild>` abstract base with `ChildInsertStrategy` virtual property to `Quotinator.Data`
6. Add a concrete aggregate example — `Widget`/`WidgetLine` test-only pair in `Quotinator.Data.Tests`; `WidgetLines` DDL in test setup only
7. Update `docs/data-access.md`: replace old Pattern A/B framing with unified `TransactionScope` + `InsertManyAsync` + `AggregateRepository` documentation; document `InsertStrategy` with when-to-use guidance
8. Add integration tests for all new types covering both `Bulk` and `Sequential` strategies

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `InsertStrategy` enum exists with `Bulk` and `Sequential` values | Build | `dotnet build --configuration Release` — 0 errors |
| 2 | ✅ | `TransactionScope.ExecuteAsync` — no existing UoW: creates its own, commits on success | Integration test | `TransactionScopeTests.ExecuteAsync_NoExisting_CreatesAndCommits` |
| 3 | ✅ | `TransactionScope.ExecuteAsync` — existing UoW provided: joins it, does not commit | Integration test | `TransactionScopeTests.ExecuteAsync_WithExisting_JoinsAndDoesNotCommit` |
| 4 | ✅ | `TransactionScope.ExecuteAsync` — exception when owner: rolls back, leaves no rows | Integration test | `TransactionScopeTests.ExecuteAsync_OnException_Rollback_LeavesNoRows` |
| 5 | ✅ | `SqliteRepositoryBase<T>` declares virtual `InsertManyAsync(entities, unitOfWork?, strategy?)` — data only, no audit | Build | `dotnet build --configuration Release` — 0 errors |
| 6 | ✅ | `IRepository<T>` declares `InsertManyAsync` with `strategy` parameter | Build | `dotnet build --configuration Release` — 0 errors |
| 7 | ✅ | `InsertManyAsync` Bulk strategy — all entities inserted in one SQL call | Integration test | `InsertManyAsyncTests.InsertManyAsync_Bulk_AllRowsPersisted` |
| 8 | ✅ | `InsertManyAsync` Sequential strategy — all entities inserted row by row | Integration test | `InsertManyAsyncTests.InsertManyAsync_Sequential_AllRowsPersisted` |
| 9 | ✅ | `SqliteRepository<T>` override: Bulk — one bulk data INSERT + one bulk audit INSERT | Integration test | `InsertManyAsyncTests.InsertManyAsync_Bulk_AuditEntriesWrittenForAll` |
| 10 | ✅ | `SqliteRepository<T>` override: Sequential — each row calls `InsertAsync`; produces one audit entry per row | Integration test | `InsertManyAsyncTests.InsertManyAsync_Sequential_AuditEntryPerRow` |
| 11 | ✅ | `InsertManyAsync` Sequential — failure on a specific row propagates that row's exception | Integration test | `InsertManyAsyncTests.InsertManyAsync_Sequential_FailingRowPropagatesException` |
| 12 | ✅ | `IAuditWriter` bulk `WriteAsync` overload — all entries persisted in one round-trip | Integration test | `InsertManyAsyncTests.AuditWriter_WriteAsync_BulkEntries_AllPersisted` |
| 13 | ✅ | `InsertManyAsync` passes `unitOfWork` through to `TransactionScope` correctly | Integration test | `InsertManyAsyncTests.InsertManyAsync_WithUow_JoinsTransaction` |
| 14 | ✅ | `AggregateRepository<TParent, TChild>` exists; `ChildInsertStrategy` defaults to `Bulk`; abstract members compile | Build + Integration test | `dotnet build` — 0 errors; `AggregateRepositoryTests.AggregateRepository_ChildInsertStrategy_DefaultsTo_Bulk` |
| 15 | ✅ | `AggregateRepository.InsertAsync` — existing UoW provided: joins caller's transaction | Integration test | `AggregateRepositoryTests.InsertAsync_WithExistingUow_JoinsCaller` |
| 16 | ✅ | `AggregateRepository.InsertAsync` — no UoW: commits parent + all children | Integration test | `AggregateRepositoryTests.InsertAsync_NoUow_CommitsParentAndChildren` |
| 17 | ✅ | `AggregateRepository.InsertAsync` — child failure: rolls back, no orphaned parent | Integration test | `AggregateRepositoryTests.InsertAsync_ChildFailure_RollsBackParent` |
| 18 | ✅ | `AggregateRepository.InsertAsync` — audit entries written for parent and all children | Integration test | `AggregateRepositoryTests.InsertAsync_AuditEntriesForParentAndAllChildren` |
| 19 | ✅ | `AggregateRepository` with `ChildInsertStrategy = Sequential` — constraint violation propagates; transaction rolled back | Integration test | `AggregateRepositoryTests.InsertAsync_Sequential_PerRowAuditAndFailureIdentified` |
| 20 | ✅ | `docs/data-access.md` updated — `InsertStrategy`, `TransactionScope`, `InsertManyAsync`, `AggregateRepository` documented; when-to-use for each strategy | Code review | Sections added; navigation-property write-only rule documented |
| 21 | ✅ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` — 0 Warning(s) 0 Error(s) |
| 22 | ✅ | All tests pass | Build | `dotnet test --configuration Release` — 526 passed, 0 failed |
| 23 | ✅ | App starts without error | T1 | App started in VS — schema v4, 788 quotes, startup banner confirmed clean |
