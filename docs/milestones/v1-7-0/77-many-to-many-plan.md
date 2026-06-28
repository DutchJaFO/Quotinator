# Issue #77 — Add many-to-many relationship pattern to Quotinator.Data

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

A many-to-many relationship links N rows in one table to M rows in another through a junction table. `IRepository<T>` has no mechanism to manage junction rows, load an entity with its related collection, or add/remove individual links.

## Depends on

- **#73** (audit trail) — `SqliteLinkRepository` delegates to `SqliteRestorableRepository<TJunction>`, which requires `IAuditWriter` and `ICallerContext`; link/unlink/restore operations are automatically audited through the delegate
- **#74** (read-model query pattern) — `GetRightAsync`/`GetLeftAsync` use the two-query approach (see below); `JoinQueryRepository` is available for denormalised projections
- **#75** / **#76** — all relationship patterns use `IUnitOfWork` for transaction coordination; `IDbTransaction` is not used directly

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

## Implementation decisions

### Constructor
`SqliteLinkRepository<TLeft, TRight, TJunction>` accepts `IDbConnectionFactory`, `IAuditWriter`, and `ICallerContext` — passes all three to its internal `SqliteRestorableRepository<TJunction>`. No direct Dapper calls in link/unlink/restore paths.

### `LinkAsync` — no `INSERT OR IGNORE`
`INSERT OR IGNORE` bypasses the audit trail (it never calls `InsertAsync`). Instead:
1. Query the junction table for an existing row matching `(leftId, rightId)` — active or soft-deleted
2. If a soft-deleted row exists → call `RestoreAsync` (writes an audit entry)
3. If no row exists → call `InsertAsync` (writes an audit entry)
4. If an active row exists → no-op (idempotent)

### `GetRightAsync` / `GetLeftAsync` — two-query approach
Returns the full entity list via two queries: (1) load junction rows matching the given Id to get the related entity Ids, (2) load each related entity via its own `IRepository<T>`. This avoids coupling `ILinkRepository` to `JoinQueryRepository`. If a caller needs a denormalised join result, they use `JoinQueryRepository` directly.

## Scope

1. Define `ILinkRepository<TLeft, TRight, TJunction>` and `SqliteLinkRepository<TLeft, TRight, TJunction>` in `Quotinator.Data`
2. Junction table name resolved from `[Table]` attribute on `TJunction` — same as all other repositories
3. `LinkAsync` uses the check-then-restore-or-insert approach (not `INSERT OR IGNORE`) to preserve audit trail
4. `UnlinkAsync` calls `SoftDeleteAsync` on the matching junction row via the delegate repository
5. `RestoreLinkAsync` calls `RestoreAsync` on the matching junction row
6. `GetRightAsync` / `GetLeftAsync` use the two-query approach
7. Add a concrete implementation when the first many-to-many relationship is needed
8. Tests must cover: link, unlink, restore link, get-right-by-left, get-left-by-right, duplicate link (idempotent), unlink when not linked (no-op), link/unlink operations produce audit entries

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `ILinkRepository<TLeft, TRight, TJunction>` defined in `Quotinator.Data` | Code review | Interface exists in correct namespace |
| 2 | ⬜ | `SqliteLinkRepository<TLeft, TRight, TJunction>` implements it; constructor accepts `IAuditWriter` and `ICallerContext`; delegates to `SqliteRestorableRepository<TJunction>` | Code review | Class exists; constructor signature correct; no direct Dapper calls in link/unlink/restore paths |
| 3 | ⬜ | `LinkAsync` is idempotent — duplicate link is a no-op (not an error) | Integration test | Test class + method (TBD at implementation) |
| 4 | ⬜ | `LinkAsync` restores a soft-deleted junction row rather than inserting a duplicate | Integration test | Test class + method (TBD at implementation) |
| 5 | ⬜ | `LinkAsync` produces an audit entry (either Insert or Restore operation) | Integration test | Test class + method (TBD at implementation) |
| 6 | ⬜ | `UnlinkAsync` soft-deletes the junction row and produces a SoftDelete audit entry | Integration test | Test class + method (TBD at implementation) |
| 7 | ⬜ | `UnlinkAsync` when not linked is a no-op | Integration test | Test class + method (TBD at implementation) |
| 8 | ⬜ | `RestoreLinkAsync` restores a soft-deleted link and produces a Restore audit entry | Integration test | Test class + method (TBD at implementation) |
| 9 | ⬜ | `GetRightAsync` returns correct related entities (two-query approach) | Integration test | Test class + method (TBD at implementation) |
| 10 | ⬜ | `GetLeftAsync` returns correct related entities (two-query approach) | Integration test | Test class + method (TBD at implementation) |
| 11 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 12 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 13 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
