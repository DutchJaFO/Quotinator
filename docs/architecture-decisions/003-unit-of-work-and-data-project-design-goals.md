# ADR 003 — Unit of Work pattern and Quotinator.Data design goals

**Status:** Accepted  
**Date:** 2026-06-20  
**GitHub issue:** #78

---

## Context

`SqliteRepository<T>` opens a new connection per method call, making it impossible to wrap multiple repository operations in a single transaction. The import flow (#45) requires inserting an `ImportBatch` row and all associated records atomically.

Two options were evaluated:

- **Option A — Unit of Work:** `IUnitOfWork` owns one connection and transaction; repositories accept it when callers need atomicity.
- **Option B — Optional connection/transaction parameters:** Repository methods accept an optional `(IDbConnection?, IDbTransaction?)` pair.

A secondary concern was raised during evaluation: `Quotinator.Data` uses Dapper internally, and that dependency must not leak into consuming projects (`Quotinator.Core`, `Quotinator.Api`). Option B would expose `IDbConnection` and `IDbTransaction` — both from `System.Data`, but still infrastructure types — on every repository interface method, coupling callers to the connection abstraction unnecessarily.

---

## Decision

**Option A — Unit of Work** is adopted.

`IUnitOfWork` exposes only domain-level operations (`BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`, `Dispose`). It owns the connection and transaction internally. Repository methods accept an optional `IUnitOfWork` parameter; callers that do not need atomicity omit it and the repository opens its own connection as before.

**SQLite is the sole concrete database target.** `SqliteUnitOfWork` is the only implementation. Support for MS SQL or other backends is explicitly out of scope for this project. The abstraction is designed to accommodate a future backend without changing `IRepository<T>` or `IUnitOfWork`, but no implementation work is planned or should be anticipated.

---

## Design goals for Quotinator.Data (binding on all future work)

1. **No Dapper types on any public interface.** `IRepository<T>`, `IRestorableRepository<T>`, and `IUnitOfWork` must expose only C# standard types and project-owned types. Dapper and `Microsoft.Data.Sqlite` are implementation details of `SqliteRepository<T>` and `SqliteUnitOfWork` — they must not appear in method signatures, return types, or interface constraints visible to `Quotinator.Core` or `Quotinator.Api`.

2. **Interfaces are the contract; implementations are SQLite-specific.** Consumers depend on interfaces, not concrete classes. Concrete classes are registered in DI and never referenced directly outside `Quotinator.Data`.

3. **SQLite is the only supported backend.** Do not add conditional logic, provider abstractions, or configuration branches for other databases. If a future milestone adds MS SQL support, a new ADR will govern that decision and the scope of changes required.

4. **Connection lifecycle is owned by the repository or the Unit of Work — never by the caller.** Callers must not open, close, or dispose connections themselves. If atomicity is needed, callers create a `IUnitOfWork` via DI and pass it to repository methods; the Unit of Work owns the connection for that scope.

---

## Consequences

- `IUnitOfWork` and `SqliteUnitOfWork` are added to `Quotinator.Data` as part of issue #78.
- `IRepository<T>` methods gain an optional `IUnitOfWork?` parameter — existing callers pass nothing and behaviour is unchanged.
- `IRestorableRepository<T>` and `SqliteRestorableRepository<T>` are updated accordingly.
- DI registration is updated to include `IUnitOfWork` / `SqliteUnitOfWork`.
- All future repository additions must follow the design goals above. Any deviation requires a new or updated ADR.
