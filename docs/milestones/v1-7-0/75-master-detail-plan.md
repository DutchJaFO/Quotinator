# Plan: #75 — Add master/detail repository pattern to Quotinator.Data for parent/child table relationships

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/75  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Depends on

- **#73** (audit trail) — `IAuditWriter` and `ICallerContext` are required constructor parameters on every `SqliteRepository<T>` derivation; `IAuditWriter` also receives a bulk overload in this issue
- **#74** (read-model query pattern) — `IUnitOfWork` is the established transaction mechanism; `IRepository<T>` already has `IUnitOfWork? unitOfWork = null` on all mutating methods

---

## Summary

Pattern A (separate repos, composed by the caller) and Pattern B (aggregate root) are not distinct patterns — they are the same unit-of-work coordination used at different call sites. The real deliverables are:

1. **`TransactionScope` static helper** — removes UoW boilerplate from every call site
2. **`InsertManyAsync` on `IRepository<T>`** — bulk insert + bulk audit write in one pass
3. **`IAuditWriter` bulk overload** — required by `InsertManyAsync`
4. **`AggregateRepository<TParent, TChild>` generic base** — encapsulates the parent+children pattern; delegates to `TransactionScope` and `InsertManyAsync`

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

## `InsertManyAsync` on `IRepository<T>` and `SqliteRepository<T>`

Single-round-trip bulk insert for a collection of entities, followed by a single bulk audit write. No per-entity loops.

```csharp
// IRepository<T>
Task InsertManyAsync(IEnumerable<T> entities, IUnitOfWork? unitOfWork = null);

// SqliteRepository<T> — one data INSERT, one audit INSERT
public async Task InsertManyAsync(IEnumerable<T> entities, IUnitOfWork? unitOfWork = null)
{
    var list = entities.ToList();
    var entries = list.Select(e => BuildEntry(AuditOperation.Insert, e.Id)).ToList();

    await TransactionScope.ExecuteAsync(Factory, async uow =>
    {
        await uow.Connection.InsertAsync(list, uow.Transaction);           // bulk data
        await _auditWriter.WriteAsync(entries, uow.Connection, uow.Transaction); // bulk audit
    }, unitOfWork);
}
```

---

## `IAuditWriter` bulk overload

```csharp
// New overload alongside the existing single-entry WriteAsync
Task WriteAsync(IReadOnlyList<AuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null);
```

The implementation uses a single `ExecuteAsync` call with the entry list as Dapper parameters — one SQL round-trip for N entries.

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

    public override async Task InsertAsync(TParent parent, IUnitOfWork? unitOfWork = null)
    {
        await TransactionScope.ExecuteAsync(Factory, async uow =>
        {
            await base.InsertAsync(parent, uow);
            await ChildRepository.InsertManyAsync(GetChildren(parent), uow);
        }, unitOfWork);
    }
}
```

Navigation properties (`GetChildren`) are **write-only** — populated by the caller, never populated by read methods. Reads go through `JoinQueryRepository` / `IJoinStrategy` (per #74).

---

## Approach

1. Add `TransactionScope` static helper to `Quotinator.Data`
2. Add bulk `WriteAsync` overload to `IAuditWriter` and its implementations
3. Add `InsertManyAsync` to `IRepository<T>` and `SqliteRepository<T>`
4. Add `AggregateRepository<TParent, TChild>` abstract base to `Quotinator.Data`
5. Add a concrete aggregate example — use a self-contained test-only `Widget`/`WidgetLine` pair in `Quotinator.Data.Tests` (same Widget table already exists); add `WidgetLines` table DDL in tests only
6. Update `docs/data-access.md` (created in #74): replace the old Pattern A/B framing with the unified `TransactionScope` + `AggregateRepository` documentation; include when-to-use guidance and the navigation-property write-only rule
7. Add integration tests for all new types

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `TransactionScope.ExecuteAsync` creates its own `IUnitOfWork` when none is provided; commits on success | Integration test | `TransactionScopeTests.ExecuteAsync_NoExisting_CreatesAndCommits` |
| 2 | ⬜ | `TransactionScope.ExecuteAsync` joins an existing `IUnitOfWork` and does not commit | Integration test | `TransactionScopeTests.ExecuteAsync_WithExisting_JoinsAndDoesNotCommit` |
| 3 | ⬜ | `TransactionScope.ExecuteAsync` rolls back on exception when it owns the transaction | Integration test | `TransactionScopeTests.ExecuteAsync_OnException_Rollback_LeavesNoRows` |
| 4 | ⬜ | `IAuditWriter` bulk `WriteAsync` overload exists and writes all entries in one round-trip | Integration test | `AuditWriterTests.WriteAsync_BulkEntries_AllPersisted` |
| 5 | ⬜ | `IRepository<T>` declares `InsertManyAsync` | Build | Compilation proves interface member exists |
| 6 | ⬜ | `SqliteRepository<T>.InsertManyAsync` inserts all entities in one bulk call | Integration test | `SqliteRepositoryTests.InsertManyAsync_AllRowsPersisted` |
| 7 | ⬜ | `SqliteRepository<T>.InsertManyAsync` writes one bulk audit entry per entity in one round-trip | Integration test | `SqliteRepositoryTests.InsertManyAsync_AuditEntriesWrittenForAll` |
| 8 | ⬜ | `SqliteRepository<T>.InsertManyAsync` passes `unitOfWork` through to `TransactionScope` | Integration test | `SqliteRepositoryTests.InsertManyAsync_WithUow_JoinsTransaction` |
| 9 | ⬜ | `AggregateRepository<TParent, TChild>` exists in `Quotinator.Data`; abstract members compile | Build | Compilation proves class and abstract members exist |
| 10 | ⬜ | `AggregateRepository.InsertAsync` passes `unitOfWork` through; joins caller's transaction when provided | Integration test | `AggregateRepositoryTests.InsertAsync_WithExistingUow_JoinsCaller` |
| 11 | ⬜ | `AggregateRepository.InsertAsync` commits parent + all children when no `unitOfWork` provided | Integration test | `AggregateRepositoryTests.InsertAsync_NoUow_CommitsParentAndChildren` |
| 12 | ⬜ | `AggregateRepository.InsertAsync` rolls back entirely on child failure — no orphaned parent | Integration test | `AggregateRepositoryTests.InsertAsync_ChildFailure_RollsBackParent` |
| 13 | ⬜ | `AggregateRepository.InsertAsync` produces audit entries for parent and all children (via `InsertManyAsync`) | Integration test | `AggregateRepositoryTests.InsertAsync_AuditEntriesForParentAndAllChildren` |
| 14 | ⬜ | `docs/data-access.md` updated — `TransactionScope`, `InsertManyAsync`, `AggregateRepository` documented with examples; old Pattern A/B framing replaced | Code review | Section exists; navigation-property write-only rule documented |
| 15 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 16 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 17 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
