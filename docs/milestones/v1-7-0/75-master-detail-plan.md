# Plan: #75 — Add master/detail repository pattern to Quotinator.Data for parent/child table relationships

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/75  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Depends on

- **#74** (read-model query pattern) — must be complete before starting this issue

---

## Summary

`IRepository<T>` targets one flat table. Parent/child relationships (master/detail) require coordinated writes across two tables, usually in a single transaction. This issue formalises **both** patterns as first-class documented options in `Quotinator.Data`. Since `Quotinator.Data` is a generic reusable library, both have distinct valid use cases — the documentation must be clear about when to choose each.

---

## Decision (confirmed)

**Implement both Option A and Option B.** Each has real use cases where it is the better fit. Both must be documented with clear when-to-use guidance so consumers can make an informed choice.

---

## Pattern A — Separate repositories, composed in the service layer

**When to use:** parent and children may be written independently (e.g. add a line to an existing order, update a child without touching the parent, delete a child without deleting the parent).

**Mechanism:** add an optional `IDbTransaction? tx = null` parameter to `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`, and `RestoreAsync` on `IRepository<T>` and `SqliteRepository<T>`. The service layer opens a connection and transaction once, then passes the transaction to both repositories.

```csharp
// Service layer composes two repositories in one transaction
await using var conn = factory.CreateConnection();
conn.Open();
await using var tx = await conn.BeginTransactionAsync();
await orderRepo.InsertAsync(order, tx);
foreach (var line in lines)
    await lineRepo.InsertAsync(line, tx);
tx.Commit();
```

**Impact:** all existing callers that do not pass `tx` are unaffected (optional parameter, default `null`). Verify 0 warnings after the change.

---

## Pattern B — Aggregate root repository

**When to use:** parent and children are always written together and the child list is semantically owned by the parent (e.g. an import batch with its batch lines — a batch without lines is meaningless).

**Mechanism:** a concrete `ParentRepository : SqliteRepository<Parent>` overrides `InsertAsync` to write parent + children in one transaction. `Parent` carries `IList<Child>` as a navigation property used only for writes; reads go through read-model query services (per #74).

```csharp
public class ImportBatchRepository(IDbConnectionFactory factory)
    : SqliteRepository<ImportBatch>(factory)
{
    public override async Task InsertAsync(ImportBatch batch)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        await conn.InsertAsync(batch, tx);
        foreach (var line in batch.Lines)
            await conn.InsertAsync(line, tx);
        tx.Commit();
    }
}
```

Navigation properties are **write-only** — they are populated by the caller before passing to `InsertAsync`, and are never populated by repository read methods. Read back parent + children via a read-model query service.

---

## Approach

1. Add `IDbTransaction? tx = null` to `IRepository<T>` and `SqliteRepository<T>` method signatures (enables Pattern A).
2. Verify all existing callers compile and tests pass (the parameter is optional; nothing breaks).
3. Add a concrete aggregate root example (enables Pattern B) — use `ImportBatch` / `ImportBatchLine` if #58 is still in scope, otherwise a self-contained test-only pair.
4. Document both patterns in `docs/data-access.md` (created in #74), with:
   - Clear when-to-use guidance for each
   - Code examples for both
   - Note that navigation properties are write-only; reads use query services
5. Add integration tests for both patterns.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `IRepository<T>` and `SqliteRepository<T>` have optional `IDbTransaction?` parameter on mutating methods | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 2 | ❌ | All existing callers unaffected (parameter is optional, default null) | Live | Build clean; all existing tests still pass |
| 3 | ❌ | Pattern A integration test: two repositories share one transaction; both rows committed | Unit test | `MasterDetailPatternATests` — `InsertParentAndChildren_SeparateRepos_BothRowsExist` |
| 4 | ❌ | Pattern A integration test: rollback on failure leaves no orphaned parent | Unit test | `MasterDetailPatternATests` — `InsertParentAndChildren_RollbackOnChildFailure_NoOrphan` |
| 5 | ❌ | Pattern B aggregate root repository exists with overridden `InsertAsync` | Live | Concrete class exists in `Quotinator.Data`; overrides `InsertAsync` |
| 6 | ❌ | Pattern B integration test: aggregate insert commits parent + children in one transaction | Unit test | `MasterDetailPatternBTests` — `InsertAggregate_AllRowsExist` |
| 7 | ❌ | Pattern B integration test: rollback on child failure leaves no orphaned parent | Unit test | `MasterDetailPatternBTests` — `InsertAggregate_RollbackOnFailure_NoOrphan` |
| 8 | ❌ | Both patterns documented in `docs/data-access.md` with when-to-use guidance | Live | Section exists; covers both options with code examples; navigation-property write-only rule documented |
| 9 | ❌ | Full test suite green | Live | `dotnet test --configuration Release` — all tests pass |
