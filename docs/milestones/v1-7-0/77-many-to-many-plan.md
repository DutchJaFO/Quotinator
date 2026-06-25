# Issue #77 — Add many-to-many relationship pattern to Quotinator.Data

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

A many-to-many relationship links N rows in one table to M rows in another through a junction table. `IRepository<T>` has no mechanism to manage junction rows, load an entity with its related collection, or add/remove individual links.

## Key design decision — RecordBase on junction tables (decided, non-negotiable)

Every table — including junction tables — uses `RecordBase`. Junction tables get a synthetic `Guid Id` as primary key and a `UNIQUE` constraint on the FK pair. This was decided in the issue spec and applies without exception. See `docs/architecture-decisions/002-recordbase-on-all-tables.md` for the reasoning.

## Proposed pattern

`ILinkRepository<TLeft, TRight, TJunction>` — a dedicated repository that operates on the junction table. Because junction tables use `RecordBase`, `TJunction` is a full entity type and the link repository delegates to `SqliteRestorableRepository` internally.

```csharp
public interface ILinkRepository<TLeft, TRight, TJunction>
    where TLeft    : RecordBase
    where TRight   : RecordBase
    where TJunction : RecordBase
{
    Task LinkAsync(Guid leftId, Guid rightId);
    Task UnlinkAsync(Guid leftId, Guid rightId);    // soft-delete via IRestorableRepository
    Task RestoreLinkAsync(Guid leftId, Guid rightId);
    Task<IReadOnlyList<TRight>> GetRightAsync(Guid leftId);
    Task<IReadOnlyList<TLeft>>  GetLeftAsync(Guid rightId);
}
```

For read-only projections that need both sides in one query, use a read-model query service (#74).

## Scope

1. Define `ILinkRepository<TLeft, TRight, TJunction>` and `SqliteLinkRepository` in `Quotinator.Data`
2. Junction table name resolved from `[Table]` attribute on `TJunction` — same as all other repositories
3. `LinkAsync` uses `INSERT OR IGNORE` on the FK pair, or restores a soft-deleted link if one exists
4. `UnlinkAsync` calls `SoftDeleteAsync` on the matching junction row
5. Add a concrete implementation when the first many-to-many relationship is needed
6. Tests must cover: link, unlink, restore link, get-right-by-left, get-left-by-right, duplicate link (idempotent), unlink when not linked (no-op)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `ILinkRepository<TLeft, TRight, TJunction>` defined in `Quotinator.Data` | Code review | Interface exists in correct namespace |
| 2 | ⬜ | `SqliteLinkRepository` implements it | Code review | Implementation class exists; delegates to `SqliteRestorableRepository` |
| 3 | ⬜ | `LinkAsync` is idempotent — duplicate link does not error | Unit test | Test class + method (TBD at implementation) |
| 4 | ⬜ | `UnlinkAsync` soft-deletes the junction row | Unit test | Test class + method (TBD at implementation) |
| 5 | ⬜ | `RestoreLinkAsync` restores a soft-deleted link | Unit test | Test class + method (TBD at implementation) |
| 6 | ⬜ | `GetRightAsync` returns correct related entities | Unit test | Test class + method (TBD at implementation) |
| 7 | ⬜ | `GetLeftAsync` returns correct related entities | Unit test | Test class + method (TBD at implementation) |
| 8 | ⬜ | Unlink when not linked is a no-op | Unit test | Test class + method (TBD at implementation) |
| 9 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 10 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 11 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
