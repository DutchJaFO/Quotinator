# #78 — Repository: transaction and shared connection support

**Status:** Not started  
**GitHub issue:** #78  
**Depends on:** #71 (generic repository pattern)  
**Unblocks:** #58 (ImportBatches schema), #45 (import endpoint)

---

## Context

`SqliteRepository<T>` opens and closes a new connection on every method call. The import flow in #45 must insert an `ImportBatch` row and all associated records atomically. The current design cannot support this.

---

## Approach decision (decide before implementing)

Evaluate both options and record the chosen approach in a **Decision** section below. Write an ADR in `docs/architecture-decisions/` if the decision involves a non-obvious trade-off.

**Option A — Unit of Work**  
`IUnitOfWork` owns one connection and transaction. Repositories accept it as an optional dependency. Callers that need atomicity create a unit of work, pass it to each repository call, and commit or roll back at the end.

**Option B — Optional connection/transaction parameter**  
Repository methods accept an optional `(IDbConnection, IDbTransaction?)` pair. Single-operation callers omit them; multi-step callers supply their own.

---

## Decision

*(Record chosen approach and reasoning here before implementing)*

---

## Spec requirements

1. Multiple repository calls can be wrapped in a single transaction
2. Single-operation callers (no transaction needed) continue to work unchanged — no breaking change to existing call sites or tests
3. `IRepository<T>`, `SqliteRepository<T>`, `IRestorableRepository<T>`, and `SqliteRestorableRepository<T>` updated to support the chosen approach
4. Commit path: all operations within a transaction are persisted together
5. Rollback path: a failed or cancelled transaction leaves no partial state in the database
6. DI registration updated if the chosen approach requires it

---

## Implementation steps

- [ ] Evaluate options and record decision above
- [ ] Write ADR if warranted
- [ ] Update `IRepository<T>`
- [ ] Update `SqliteRepository<T>`
- [ ] Update `IRestorableRepository<T>` and `SqliteRestorableRepository<T>` if affected
- [ ] Add `SqliteRepositoryTransactionTests` with commit and rollback tests
- [ ] Confirm all existing `SqliteRepositoryTests` and `SqliteRestorableRepositoryTests` still pass
- [ ] Update DI registration if needed

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Multiple repository calls can share a connection and transaction | Unit test | `SqliteRepositoryTransactionTests.InsertAsync_WithSharedConnection_CommitPersistsRecord` |
| 2 | ❌ | Rollback removes all operations in the transaction | Unit test | `SqliteRepositoryTransactionTests.InsertAsync_WithSharedConnection_RollbackRemovesRecord` |
| 3 | ❌ | Multiple inserts within one transaction are atomic | Unit test | `SqliteRepositoryTransactionTests.MultipleInserts_WithinTransaction_AreAtomic` |
| 4 | ❌ | Single-operation callers work unchanged (no regression) | Unit test | All existing `SqliteRepositoryTests` and `SqliteRestorableRepositoryTests` pass |
