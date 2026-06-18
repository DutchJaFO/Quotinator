# #71 — Generic repository pattern for database entities

**Status:** Not started  
**GitHub issue:** #71  
**Unblocks:** #58 (ImportBatches), all future entity repositories

---

## Spec requirements

1. `IRepository<T>` interface in `Quotinator.Data` with `GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`
2. `SqliteRepository<T>` base class in `Quotinator.Data` implementing `IRepository<T>` using Dapper/Dapper.Contrib
3. Repository methods open their own connection via `IDbConnectionFactory` — no shared-connection coupling
4. First concrete implementation: `IImportBatchRepository` / `SqliteImportBatchRepository` in `Quotinator.Core`
5. Register `SqliteImportBatchRepository` in DI
6. Existing inline inserts in `DatabaseInitializer` are not migrated in this issue

---

## Implementation steps

- [ ] Add `IRepository<T>` interface to `Quotinator.Data`
- [ ] Add `SqliteRepository<T>` abstract base class to `Quotinator.Data`
- [ ] Add `IImportBatchRepository` interface to `Quotinator.Core`
- [ ] Add `SqliteImportBatchRepository` to `Quotinator.Core`, inheriting `SqliteRepository<ImportBatch>`
- [ ] Register `SqliteImportBatchRepository` as `IImportBatchRepository` in DI (`Program.cs`)
- [ ] Unit tests (see verification table)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `IRepository<T>` interface exists with correct members | Unit test | `RepositoryPatternTests.IRepository_HasRequiredMembers` |
| 2 | ❌ | `SqliteRepository<T>` implements `IRepository<T>` | Unit test | `RepositoryPatternTests.SqliteRepository_ImplementsIRepository` |
| 3 | ❌ | `InsertAsync` writes a record and it can be read back via `GetByIdAsync` | Unit test | `RepositoryPatternTests.InsertAsync_ThenGetById_ReturnsRecord` |
| 4 | ❌ | `UpdateAsync` persists changes | Unit test | `RepositoryPatternTests.UpdateAsync_PersistsChanges` |
| 5 | ❌ | `SoftDeleteAsync` sets `IsDeleted = 1` and `DateDeleted`; record not returned by normal queries | Unit test | `RepositoryPatternTests.SoftDeleteAsync_SetsIsDeletedAndDateDeleted` |
| 6 | ❌ | `SqliteImportBatchRepository` is resolvable from DI as `IImportBatchRepository` | Unit test | `RepositoryPatternTests.DI_ResolvesImportBatchRepository` |

---

## Notes

Bulk seeding in `DatabaseInitializer` uses a shared connection across many inserts for performance. Repository methods that open their own connection are not suitable for bulk operations — those stay inline in `DatabaseInitializer` for now. Migration of existing inline code to repositories is a separate follow-up issue.
