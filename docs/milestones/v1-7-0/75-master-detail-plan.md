# Issue #75 — Add master/detail repository pattern to Quotinator.Data for parent/child table relationships

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

`IRepository<T>` targets one table. Parent/child relationships (master/detail) require coordinated writes across two tables, often in a single transaction. There is currently no mechanism for this.

## Two viable approaches (decide at implementation)

### Option A — Separate repositories, composed in the service layer

Use `IRepository<TParent>` and `IRepository<TChild>` independently. The service layer opens a shared transaction and passes it to both repositories.

Requires: optional `IDbTransaction` parameter added to `IRepository` method signatures, or a Unit of Work wrapper.

Best when: parent and children are sometimes written independently.

### Option B — Aggregate root repository (explicit, self-contained)

A specific repository class overrides `InsertAsync` to write parent + children in one transaction. The parent entity carries `IList<TChild>` as a navigation property.

Best when: parent and children are always written together.

## Scope

1. Decide which option (A, B, or both) to formalise as a supported pattern in `Quotinator.Data`
2. Add a concrete implementation when the first master/detail entity is needed
3. Document the pattern alongside #74 (read-model pattern)
4. Integration tests covering cross-table transaction: insert parent + children, verify both rows, verify rollback on failure

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Pattern decision (A, B, or both) documented in `docs/data-access.md` | Code review | Doc exists; decision and rationale recorded |
| 2 | ⬜ | At least one concrete implementation added | Code review | Repository or service-layer composition with shared transaction |
| 3 | ⬜ | Insert parent + children in one transaction succeeds | Unit test | Test class + method (TBD at implementation) |
| 4 | ⬜ | Rollback on child insert failure leaves no orphaned parent row | Unit test | Test class + method (TBD at implementation) |
| 5 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 6 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 7 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
