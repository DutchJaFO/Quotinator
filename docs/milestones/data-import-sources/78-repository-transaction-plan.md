# #78 — Repository: transaction and shared connection support

**Status:** Released
**GitHub issue:** #78  
**Depends on:** #71 (generic repository pattern)  
**Unblocks:** #58 (ImportBatches schema), #45 (import endpoint)

---

## Context

`SqliteRepository<T>` opens and closes a new connection on every method call. The import flow in #45 must insert an `ImportBatch` row and all associated records atomically. The current design cannot support this.

---

## Approach decision

**Option A — Unit of Work** is adopted. See [ADR 003](../../architecture-decisions/003-unit-of-work-and-data-project-design-goals.md) for the full rationale and binding design goals for `Quotinator.Data`.

Summary:
- `IUnitOfWork` exposes `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`, `DisposeAsync` — no Dapper or `IDbConnection` types visible to callers
- Repository methods accept an optional `IUnitOfWork?` — callers that need atomicity pass one; all others omit it and behaviour is unchanged
- `SqliteUnitOfWork` is the only concrete implementation; MS SQL is out of scope
- An ADR was written because this decision binds all future `Quotinator.Data` work

---

## Spec requirements

1. `IUnitOfWork` interface in `Quotinator.Data` with `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`, `DisposeAsync` — no Dapper or `IDbConnection` types on the public interface
2. `SqliteUnitOfWork` implements `IUnitOfWork` in `Quotinator.Data`
3. `IRepository<T>` methods (`GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`) each accept an optional `IUnitOfWork?` parameter — existing call sites require no changes
4. `SqliteRepository<T>` uses the connection and transaction from the supplied `IUnitOfWork` when provided; opens its own connection when not
5. `IRestorableRepository<T>` methods (`GetDeletedAsync`, `RestoreAsync`, `HardDeleteAsync`, `PurgeAsync`) each accept an optional `IUnitOfWork?` parameter
6. `SqliteRestorableRepository<T>` uses the supplied `IUnitOfWork` when provided
7. Commit path: all operations within one `IUnitOfWork` scope are persisted when `CommitAsync` is called
8. Rollback path: all operations within one `IUnitOfWork` scope are discarded when `RollbackAsync` is called or the unit of work is disposed without committing
9. Single-operation callers (no `IUnitOfWork` passed) work identically to before — no regression
10. `IUnitOfWork` registered in DI and resolvable at runtime

---

## Implementation steps

1. [x] Evaluate options and record decision
2. [x] Write ADR 003
3. [x] Write `SqliteRepositoryTransactionTests` with all expected tests (confirm red before implementing)
4. [x] Run full test suite to establish baseline (confirmed 300 green, 0 failures)
5. [x] Add `IUnitOfWork` interface to `Quotinator.Data`
6. [x] Add `SqliteUnitOfWork` to `Quotinator.Data`
7. [x] Update `IRepository<T>` methods to accept optional `IUnitOfWork?`
8. [x] Update `SqliteRepository<T>` to use supplied `IUnitOfWork` when provided
9. [x] Update `IRestorableRepository<T>` methods to accept optional `IUnitOfWork?`
10. [x] Update `SqliteRestorableRepository<T>` accordingly
11. [x] Register `IUnitOfWork` / `SqliteUnitOfWork` in DI
12. [x] Confirm all existing tests still pass (306 green, 0 failures)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `IUnitOfWork` interface exists with `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`, `DisposeAsync` | Unit test | `SqliteRepositoryTransactionTests.SqliteUnitOfWork_ImplementsIUnitOfWork` |
| 2 | ✅ | `IUnitOfWork` public interface exposes no Dapper or `IDbConnection` types | Unit test | `SqliteRepositoryTransactionTests.IUnitOfWork_HasNoDapperTypesOnPublicInterface` |
| 3 | ✅ | `IRepository<T>` methods accept optional `IUnitOfWork?` without breaking existing callers | Unit test | All existing `SqliteRepositoryTests` pass unchanged |
| 4 | ✅ | Commit path: operations within a `IUnitOfWork` are persisted on `CommitAsync` | Unit test | `SqliteRepositoryTransactionTests.InsertAsync_WithSharedConnection_CommitPersistsRecord` |
| 5 | ✅ | Rollback path: operations within a `IUnitOfWork` are removed on `RollbackAsync` | Unit test | `SqliteRepositoryTransactionTests.InsertAsync_WithSharedConnection_RollbackRemovesRecord` |
| 6 | ✅ | Multiple inserts within one transaction are atomic | Unit test | `SqliteRepositoryTransactionTests.MultipleInserts_WithinTransaction_AreAtomic` |
| 7 | ✅ | Dispose without commit rolls back all operations | Unit test | `SqliteRepositoryTransactionTests.Dispose_WithoutCommit_RollsBack` |
| 8 | ✅ | `IRestorableRepository<T>` methods accept optional `IUnitOfWork?` without breaking existing callers | Unit test | All existing `SqliteRestorableRepositoryTests` pass unchanged |
| 9 | ✅ | Single-operation callers (no `IUnitOfWork`) work identically to before | Unit test | All existing `SqliteRepositoryTests` and `SqliteRestorableRepositoryTests` pass |
| 10 | ✅ | `IUnitOfWork` registered in DI and resolvable at runtime | Live | `dotnet run --project src/Quotinator.Api` starts without exception; `curl http://localhost:5043/api/v1/health` returns `{"status":"healthy"}` — verified 2026-06-20 |
