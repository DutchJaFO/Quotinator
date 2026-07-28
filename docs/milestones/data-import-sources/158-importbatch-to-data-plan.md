# #158 — ImportBatch entity/repository/enums live in Quotinator.Engine instead of Quotinator.Data

**Status:** Waiting for release
**GitHub issue:** #158
**Tiers required:** T2
**Depends on:** #157

---

## Spec requirements (from the GitHub issue)

1. `ImportBatch`, `ImportBatchType`, `ImportBatchStatus` move to `Quotinator.Data/Entities/`.
2. `IImportBatchRepository`, `SqliteImportBatchRepository` move to `Quotinator.Data/Repositories/`.
3. `ImportBatchNotFoundException` moves to `Quotinator.Data/Import/`, alongside `ImportBatchStateException`.
4. `Sql.ImportBatches` moves back to `Quotinator.Data.Queries.Sql` (undoing that part of #157).
5. All call sites across `Quotinator.Engine`, `Quotinator.Api`, and every test project updated.
6. `InternalsVisibleTo` re-audited for both projects.
7. ADR 004 amended with the consumer-entity-interaction test as its stated governing heuristic, not just another named example.

---

## Steps

### 1. Write the red test

**Status:** ✅ Done — `ImportBatchBoundaryTests.ImportBatch_And_Repository_LiveInQuotinatorData` and `SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries` both confirmed red before any file moved.

New `Quotinator.Data.Tests` test `ImportBatch_And_Repository_LiveInQuotinatorData` — reflects over the `Quotinator.Data` assembly and asserts `ImportBatch`, `ImportBatchType`, `ImportBatchStatus`, and `IImportBatchRepository` are found there (not in `Quotinator.Engine`). Must be red before any file moves. Also update `Quotinator.Data.Tests.Queries.SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries`'s expected set to include `ImportBatches` again — this makes that test red too, in the other direction, until step 3 below moves `Sql.ImportBatches` back.

### 2. Move `ImportBatch`, `ImportBatchType`, `ImportBatchStatus` to `Quotinator.Data/Entities/`

**Status:** ✅ Done — moved via `git mv` to preserve history, namespace updated to `Quotinator.Data.Entities`, no other content changes.

### 3. Move `IImportBatchRepository`, `SqliteImportBatchRepository` to `Quotinator.Data/Repositories/`, and `Sql.ImportBatches` back to `Quotinator.Data.Queries.Sql`

**Status:** ✅ Done. Also found and fixed a related instance of the same misplacement while moving: `QuotinatorDapperConfiguration.RegisterDomainHandlers` (Engine) registered the Dapper enum handlers for `ImportBatchType`/`ImportBatchStatus` — moved to `DatabaseConfiguration.Configure()` (Data's base class), matching the exact precedent already set for `DuplicateResolutionPolicy`/`ImportActionStatus`/`ImportActionKind` (with the same comment explaining why: `Quotinator.Data.Tests`, which only calls the base `Configure()`, needs to read/write an `ImportBatch` row directly).

### 4. Move `ImportBatchNotFoundException` to `Quotinator.Data/Import/`

**Status:** ✅ Done — moved via `git mv`, namespace updated to `Quotinator.Data.Import`, alongside `ImportBatchStateException`. Its doc comment's `<see cref="IQuoteImportService.ApplyStagedBatchAsync"/>`/`<see cref="QuoteImportValidationException"/>` references (both Engine-only types, unreachable from Data) rewritten to plain `<c>` prose — `Quotinator.Data` cannot reference `Quotinator.Engine` at all, so no cref, qualified or not, could resolve.

### 5. Update call sites

**Status:** ✅ Done. Every reference across `Quotinator.Engine` (`QuotinatorDatabaseInitializer.cs`, `SqliteImportActionService.cs`, `SqliteQuoteImportService.cs`), `Quotinator.Api` (`Program.cs`), and 9 test files updated. One ambiguity handled explicitly: `QuotinatorDatabaseInitializer.TruncateDataAsync` (added by #157) needs both Engine's `Sql` (for `Quotes`/`Characters`/etc.) and Data's `Sql.ImportBatches.DeleteAll` in the same method — its one `ImportBatches` call was fully qualified (`Quotinator.Data.Queries.Sql.ImportBatches.DeleteAll`) rather than adding an ambiguous second `using Quotinator.Data.Queries;` for a single call site. `Program.cs` had three call sites fully-qualifying `Quotinator.Engine.Repositories.IImportBatchRepository` explicitly (pre-dating this move) — simplified to the plain type name now that the ambiguity those qualifications guarded against no longer applies.

### 6. Re-audit `InternalsVisibleTo`

**Status:** ✅ Done — no changes needed. `ImportBatch`/`IImportBatchRepository`/`SqliteImportBatchRepository`/`ImportBatchNotFoundException` are all `public`, so the move needed no new grants. `Sql.ImportBatches` moving back to `Quotinator.Data.Queries.Sql` (`internal`) continues to need `Quotinator.Engine`'s existing `InternalsVisibleTo` grant (unchanged, pre-dates #157).

### 7. Amend ADR 004 with the consumer-entity-interaction test

**Status:** ✅ Done. Added a "Revision — issue #158" subsection stating the governing heuristic explicitly, and corrected the #157 revision's own text (it had listed `ImportBatches` alongside the genuinely domain-specific tables — the exact mistake #158 caught, left in place with a visible correction note rather than silently edited away).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ImportBatch_And_Repository_LiveInQuotinatorData` is red before the move, green after | Unit test | `Quotinator.Data.Tests.ImportBatchBoundaryTests.ImportBatch_And_Repository_LiveInQuotinatorData` — red before, green after |
| 2 | ✅ | `SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries`'s expected set includes `ImportBatches` again and passes | Unit test | `Quotinator.Data.Tests.Queries.SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries` — red before, green after |
| 3 | ✅ | No call site left referencing a moved type through the old `Quotinator.Engine` namespace | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 4 | ✅ | Full test suite green | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,119/1,119 passed across all 9 test projects |
| 5 | ✅ | ADR 004 states the consumer-entity-interaction test explicitly | Live | Manual doc review — "Revision — issue #158" section added, plus a correction to #157's own revision text |
| 6 | ✅ | Docker build succeeds | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded |
| 7 | ✅ | Live: reseed/reset and import-batch-listing behaviour unchanged after the move | Live (T2) | Container smoke test — `POST /api/v1/admin/database/reseed` returned `200` with correct counts (796/479/7/45); `POST /api/v1/import` returned `200` with a valid `batchId` and correctly-resolved `skip` conflicts; `GET /api/v1/import/actions?batchId=` listed all 10 actions correctly; no errors in container logs |

---

## Notes

T1 not required — pure code-organisation change, same reasoning as #157.
