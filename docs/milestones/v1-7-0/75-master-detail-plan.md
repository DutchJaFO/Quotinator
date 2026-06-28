# Plan: #75 — Add master/detail repository pattern to Quotinator.Data for parent/child table relationships

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/75  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Depends on

- **#73** (audit trail) — `IAuditWriter` and `ICallerContext` are required constructor parameters on every `SqliteRepository<T>` derivation; all write paths must route through the base class to write audit entries
- **#74** (read-model query pattern) — `IUnitOfWork` pattern established; `IRepository<T>` already has `IUnitOfWork? unitOfWork = null` on all mutating methods; Pattern A foundation exists

---

## Summary

`IRepository<T>` targets one flat table. Parent/child relationships (master/detail) require coordinated writes across two tables, usually in a single transaction. This issue formalises **both** patterns as first-class documented options in `Quotinator.Data`. Since `Quotinator.Data` is a generic reusable library, both have distinct valid use cases — the documentation must be clear about when to choose each.

---

## Decision (confirmed)

**Implement both Option A and Option B.** Each has real use cases where it is the better fit. Both must be documented with clear when-to-use guidance so consumers can make an informed choice.

---

## Pattern A — Separate repositories, composed in the service layer

**When to use:** parent and children may be written independently (e.g. add a line to an existing order, update a child without touching the parent, delete a child without deleting the parent).

**Mechanism:** `IRepository<T>` already has `IUnitOfWork? unitOfWork = null` on all mutating methods (established in #74). The service layer creates a `SqliteUnitOfWork`, begins a transaction, and passes it to both repositories. Both repositories write audit entries in the same transaction.

```csharp
// Service layer composes two repositories in one transaction
await using var uow = new SqliteUnitOfWork(factory);
await uow.BeginTransactionAsync();
await orderRepo.InsertAsync(order, uow);     // writes parent row + audit entry
foreach (var line in lines)
    await lineRepo.InsertAsync(line, uow);   // writes child row + audit entry
await uow.CommitAsync();
```

**No changes required to `IRepository<T>` or `SqliteRepository<T>`** — the `IUnitOfWork` parameter is already in place.

---

## Pattern B — Aggregate root repository

**When to use:** parent and children are always written together and the child list is semantically owned by the parent (e.g. an import batch with its batch lines — a batch without lines is meaningless).

**Mechanism:** a concrete `ParentRepository : SqliteRepository<Parent>` overrides `InsertAsync` to write parent + children in one transaction. `Parent` carries `IList<Child>` as a navigation property used only for writes; reads go through read-model query services (per #74).

**Audit trail rule:** all inserts must route through `base.InsertAsync(entity, uow)` or a child `SqliteRepository<T>.InsertAsync(entity, uow)` — never call Dapper directly (`conn.InsertAsync(...)`) in an override, as that bypasses the audit write.

```csharp
public class ImportBatchRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext,
    SqliteRepository<ImportBatchLine> lineRepo)
    : SqliteRepository<ImportBatch>(factory, auditWriter, callerContext)
{
    public override async Task InsertAsync(ImportBatch batch, IUnitOfWork? unitOfWork = null)
    {
        await using var uow = new SqliteUnitOfWork(Factory);
        await uow.BeginTransactionAsync();
        await base.InsertAsync(batch, uow);          // writes parent row + audit entry
        foreach (var line in batch.Lines)
            await lineRepo.InsertAsync(line, uow);   // writes child row + audit entry per child
        await uow.CommitAsync();
    }
}
```

Navigation properties are **write-only** — they are populated by the caller before passing to `InsertAsync`, and are never populated by repository read methods. Read back parent + children via a read-model query service.

---

## Approach

1. **Pattern A is already possible** — `IRepository<T>` has `IUnitOfWork? unitOfWork = null` on all mutating methods; no code changes to the base class are needed. Write integration tests to prove it works and document it.
2. Add a concrete aggregate root example for Pattern B — use `ImportBatch` / `ImportBatchLine` if #58 is still in scope, otherwise a self-contained test-only pair in `Quotinator.Data.Tests`.
3. Document both patterns in `docs/data-access.md` (created in #74), with:
   - Clear when-to-use guidance for each
   - Corrected code examples showing `IUnitOfWork` (not `IDbTransaction`) and correct constructor signatures
   - The audit-continuity rule: never call Dapper directly in an override
   - Note that navigation properties are write-only; reads use query services
4. Add integration tests for both patterns.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Pattern A integration test: two repositories share one `IUnitOfWork`; both rows committed | Integration test | `MasterDetailPatternATests.InsertParentAndChildren_SeparateRepos_BothRowsExist` |
| 2 | ⬜ | Pattern A integration test: rollback leaves no orphaned parent row | Integration test | `MasterDetailPatternATests.InsertParentAndChildren_RollbackOnChildFailure_NoOrphan` |
| 3 | ⬜ | Pattern A integration test: both write operations produce audit entries in `AuditEntries` | Integration test | `MasterDetailPatternATests.InsertParentAndChildren_BothOperationsAudited` |
| 4 | ⬜ | Pattern B aggregate root repository exists; constructor takes `IAuditWriter` and `ICallerContext`; overrides `InsertAsync` | Code review + Build | Class exists; constructor signature correct; builds clean |
| 5 | ⬜ | Pattern B override uses `base.InsertAsync(entity, uow)` / child repo `InsertAsync(entity, uow)` — no direct Dapper calls | Code review | No `conn.InsertAsync` / `conn.ExecuteAsync` calls inside the override body |
| 6 | ⬜ | Pattern B integration test: aggregate insert commits parent + children in one transaction | Integration test | `MasterDetailPatternBTests.InsertAggregate_AllRowsExist` |
| 7 | ⬜ | Pattern B integration test: rollback on child failure leaves no orphaned parent | Integration test | `MasterDetailPatternBTests.InsertAggregate_RollbackOnFailure_NoOrphan` |
| 8 | ⬜ | Pattern B integration test: aggregate insert produces audit entries for parent and all children | Integration test | `MasterDetailPatternBTests.InsertAggregate_AllOperationsAudited` |
| 9 | ⬜ | Both patterns documented in `docs/data-access.md` with when-to-use guidance and corrected code examples | Code review | Section exists; `IUnitOfWork` used throughout; audit-continuity rule documented; navigation-property write-only rule documented |
| 10 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 11 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 12 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
