# Issue #76 — Add 1:1 relationship pattern to Quotinator.Data

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

A one-to-one relationship pairs exactly one row in a primary table with exactly one row in a secondary table. `IRepository<T>` maps `T` to one table — there is no mechanism to load or write a paired row, or enforce the at-most-one-child constraint.

## Two layouts to formalise

### Shared primary key (tight coupling)

Child shares the parent's `Id` as its own primary key and foreign key. Parent and child are always created and deleted together.

```sql
CREATE TABLE Quotes      (Id TEXT PRIMARY KEY, ...);
CREATE TABLE QuoteDetail (Id TEXT PRIMARY KEY REFERENCES Quotes(Id), ExtraField TEXT);
```

### Separate primary key (loose coupling)

Child has its own `Id` and a nullable foreign key back to the parent. Child can exist independently or be attached later.

```sql
CREATE TABLE Quotes      (Id TEXT PRIMARY KEY, ...);
CREATE TABLE QuoteDetail (Id TEXT PRIMARY KEY, QuoteId TEXT REFERENCES Quotes(Id), ExtraField TEXT);
```

## Scope

1. Define and document the convention for shared-PK vs separate-PK layouts in `Quotinator.Data`
2. Add a concrete implementation when the first 1:1 entity pair is needed in a milestone
3. Evaluate whether `IRepository` methods need an optional `IDbTransaction` parameter (shared with #75 concern)
4. Integration tests: insert both sides in one transaction; load child by parent id; rollback leaves neither row

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Shared-PK vs separate-PK convention documented | Code review | `docs/data-access.md` — both layouts described with guidance on when to use each |
| 2 | ⬜ | At least one concrete 1:1 implementation added | Code review | Repository class with `GetDetailAsync` and transactional insert |
| 3 | ⬜ | Insert both sides in one transaction succeeds | Unit test | Test class + method (TBD at implementation) |
| 4 | ⬜ | Rollback leaves neither row | Unit test | Test class + method (TBD at implementation) |
| 5 | ⬜ | Load child by parent id works | Unit test | Test class + method (TBD at implementation) |
| 6 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 7 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 8 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
