# Issue #73 — Audit trail: record who did what on which record in which table

**Milestone:** v1.7.0  
**Status:** Code complete — pending release and T1/T2 verification  
**Branch:** `feature/v1-7-0`  
**Tiers required:** T1, T2

---

## Scope change (2026-06-27)

The original spec deferred this issue to the auth milestone because `PerformedBy` required an authenticated user. That dependency is removed.

**New design:** The `Agent` field is populated from the standard `User-Agent` request header via `ICallerContext`. Callers that identify themselves (MagicMirror, smoke-test scripts, the Blazor UI circuit) are recorded. Callers that omit the header produce a null agent entry — the operation is still recorded. The authenticated `UserId` is added when auth lands, as a separate nullable column alongside `Agent`.

---

## Design decisions

### Dependency graph and where types must live

```
Quotinator.Data  (no project references — bottom of the stack)
  └─ exposes internals to Core, Core.Tests, Data.Tests via InternalsVisibleTo
       ↑ referenced by
Quotinator.Core  (→ Data)
       ↑ referenced by
Quotinator.Api   (→ Core, → Data, → Changelog, → Constants)
```

All audit types live in `Quotinator.Data` because Data is the only layer reachable from Core, Data, AND Api without creating a circular project reference.

### Two-tier repository hierarchy avoids audit recursion

The repository stack has two layers:

```
SqliteRepositoryBase<T>          — connection factory + TableName; no audit
  ├── SqliteRepository<T>        — domain entities; audit on every write
  │     └── SqliteRestorableRepository<T>
  │           └── SqliteImportBatchRepository
  ├── AuditWriter                — extends base directly; INSERT via Dapper.Contrib; no recursion
  └── AuditReader                — extends base directly; uses Factory for read queries; no recursion
```

`SqliteRepository<T>` holds `IAuditWriter` and `ICallerContext` and calls `WriteAsync` on every write. `AuditWriter` extends `SqliteRepositoryBase<T>` — the non-auditing base — so the INSERT it issues does NOT re-enter the audit path. `AuditReader` similarly extends the base and has no write capability at all.

**Why Dapper.Contrib for `AuditWriter`:** `AuditEntry` carries `[Table("AuditEntries")]` and `[Key]` on its `long Id`. Dapper.Contrib generates the INSERT from those attributes, so no SQL string literal is required in `Sql.cs` for the INSERT. The read queries (`SelectPaged`, `CountPaged`) remain in `Sql.Audit` as before.

### `ICallerContext` — singleton with `AsyncLocal<string?>`

Repositories are registered as singletons. If `ICallerContext` were scoped, DI would throw a captive dependency error when a singleton consumed it. Instead, `CallerContext` is registered as a singleton and uses `AsyncLocal<string?>` internally — each async execution context (one per HTTP request) gets its own isolated `Agent` value, without a scoped lifetime.

### `AuditOperation` values — past tense

All operation string values use past tense (`"Inserted"`, `"Updated"`, etc.) for two reasons:
1. Past tense matches audit-log semantics: the entry records something that *was* done, not an instruction.
2. Values like `"Insert"` and `"Update"` triggered the SQL source-scan test (`AllSqlStringLiterals_AreInCentralisedFiles`) because the scanner looks for `"INSERT` and `"UPDATE` anywhere in source files. Past tense avoids the false positive without any scanner workaround.

### Audit entries live in the same database as application data

Considered and decided (2026-06-27): audit entries live in the primary SQLite database alongside all other tables. A `ResetAsync` drops all tables including `AuditEntries` — this is intentional. Reset is the user-friendly equivalent of deleting the database file and is expected to wipe everything, including audit history. Revisit if a future requirement calls for audit log retention across resets (e.g. compliance, multi-user environments).

The shared-database approach also enables the `WriteAsync(entry, connection, transaction)` overload, which allows audit INSERTs to participate in the same transaction as the triggering write. Cross-file SQLite transactions are not supported, so a separate audit database would require dropping this overload and making audit writes best-effort.

### Migration SQL lives in `Quotinator.Data`

`Migration004_AuditEntries` SQL is defined as a `public const` in `Quotinator.Data.Database.AuditMigrations`. `QuotinatorMigrations` (Core) references it as `AuditMigrations.Migration004_AuditEntries`. This keeps audit schema co-located with audit infrastructure instead of being scattered across two projects.

### `IAuditReader` and `AuditReader` — separate read interface

The audit read endpoint uses a dedicated `IAuditReader` / `AuditReader` pair instead of `SqliteRepository<AuditEntry>`. This keeps read and write concerns separate and avoids giving the read path an `IAuditWriter` dependency (conceptually wrong).

---

## Files created or modified

| File | Status | Change |
|---|---|---|
| `src/Quotinator.Data/Entities/AuditEntry.cs` | ✅ Created | Entity + `AuditOperation` constants (past-tense values); `[Table]`+`[Key]` for Dapper.Contrib |
| `src/Quotinator.Data/Database/AuditMigrations.cs` | ✅ Created | `CreateAuditEntriesTable` DDL — version-agnostic; Core assigns version number |
| `src/Quotinator.Data/Repositories/ICallerContext.cs` | ✅ Created | Caller identity interface |
| `src/Quotinator.Data/Repositories/CallerContext.cs` | ✅ Created | Singleton with `AsyncLocal<string?>` |
| `src/Quotinator.Data/Repositories/IAuditWriter.cs` | ✅ Created | `WriteAsync` (2 overloads) + `ClearAsync(table?)` |
| `src/Quotinator.Data/Repositories/SqliteRepositoryBase.cs` | ✅ Created | Non-auditing base: `IDbConnectionFactory` + `TableName`; extended by all repos including audit |
| `src/Quotinator.Data/Repositories/AuditWriter.cs` | ✅ Created | Extends base; `ICallerContext`; INSERT/DELETE via Dapper; purge entry after clear; no recursion |
| `src/Quotinator.Data/Repositories/IAuditReader.cs` | ✅ Created | Read-only paged query interface |
| `src/Quotinator.Data/Repositories/AuditReader.cs` | ✅ Created | Extends base; read queries from `Sql.Audit` |
| `src/Quotinator.Data/Models/AuditPageResult.cs` | ✅ Created | Paged result type (Data can't use Core's `PagedResult<T>`) |
| `src/Quotinator.Data/Queries/Sql.cs` | ✅ Modified | `Audit` class: `DeleteAll`, `DeleteByTable`, `SelectPaged`, `CountPaged` |
| `src/Quotinator.Data/Repositories/SqliteRepository.cs` | ✅ Modified | Extends base; `IAuditWriter` + `ICallerContext`; audit on Insert/Update/SoftDelete |
| `src/Quotinator.Data/Repositories/SqliteRestorableRepository.cs` | ✅ Modified | Passes params to base; audit on Restore/HardDelete/Purge |
| `src/Quotinator.Data/Repositories/SqliteImportBatchRepository.cs` | ✅ Modified | Passes params to base |
| `src/Quotinator.Data/Database/DatabaseInitializer.cs` | ✅ Modified | `IAuditWriter`+`ICallerContext` in constructor; writes audit on `ReseedAsync`/`ResetAsync` |
| `src/Quotinator.Core/Data/QuotinatorMigrations.cs` | ✅ Modified | References `AuditMigrations.CreateAuditEntriesTable` |
| `src/Quotinator.Api/Program.cs` | ✅ Modified | Registers `ICallerContext`, `IAuditWriter`, `IAuditReader`; factory passes them to `DatabaseInitializer`; middleware sets `Agent` from `User-Agent` |
| `src/Quotinator.Api/Endpoints/AdminEndpoints.cs` | ✅ Modified | Two groups: public (`GET /audit`, `GET /database/seed/preview`) + admin (`POST reseed`, `POST reset`, `DELETE /audit`) |
| `tests/Quotinator.Data.Tests/Helpers/AuditStubs.cs` | ✅ Created | `NoOpAuditWriter` (with `ClearAsync`), `NoOpCallerContext` |
| `tests/Quotinator.Data.Tests/Repositories/AuditWriterTests.cs` | ✅ Created | Integration tests incl. `ClearAsync` scenarios |
| `tests/Quotinator.Core.Tests/Helpers/AuditStubs.cs` | ✅ Created | Same stubs for Core test project |
| `tests/Quotinator.Api.Tests/Fakes/NoOpAuditStubs.cs` | ✅ Created | `NoOpAuditWriter` (with `ClearAsync`), `NoOpAuditReader`, `NoOpCallerContext` |
| `tests/Quotinator.Api.Tests/Endpoints/AdminAuditEndpointTests.cs` | ✅ Created | Endpoint tests incl. `DELETE /audit` and no-auth-for-GET checks |

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `AuditWriter.WriteAsync` (standalone) inserts a row | Integration test | `AuditWriterTests.WriteAsync_StandaloneOverload_PersistsEntry` |
| 2 | ✅ | `AuditWriter.WriteAsync` fields are correctly mapped | Integration test | `AuditWriterTests.WriteAsync_StandaloneOverload_FieldsAreCorrect` |
| 3 | ✅ | Null agent and null record ID persist as NULL | Integration test | `AuditWriterTests.WriteAsync_NullAgent_Persists` |
| 4 | ✅ | Connection overload writes within the same transaction | Integration test | `AuditWriterTests.WriteAsync_ConnectionOverload_PersistsInSameTransaction` |
| 5 | ✅ | `CallerContext` Agent is isolated per async task | Integration test | `AuditWriterTests.CallerContext_SetAgent_IsIsolatedPerTask` |
| 6 | ✅ | `SqliteRepository.InsertAsync` writes an audit entry | Integration test | `AuditWriterTests.SqliteRepository_Insert_WritesAuditEntry` |
| 7 | ✅ | `ClearAsync()` deletes all entries and leaves a purge sentinel | Integration test | `AuditWriterTests.ClearAsync_NoFilter_DeletesAllEntriesAndWritesPurgeEntry` |
| 8 | ✅ | `ClearAsync(table)` deletes only that table's entries | Integration test | `AuditWriterTests.ClearAsync_WithTable_DeletesOnlyMatchingTableEntriesAndWritesPurgeEntry` |
| 9 | ✅ | `GET /api/v1/admin/audit` is public — returns 200 without API key | Endpoint test | `AdminAuditEndpointTests.GetAudit_NoApiKey_Returns200` |
| 10 | ✅ | `GET /api/v1/admin/audit` returns 200 with correct shape | Endpoint test | `AdminAuditEndpointTests.GetAudit_CorrectKey_Returns200WithPageShape` |
| 11 | ✅ | Empty result returns zero totals | Endpoint test | `AdminAuditEndpointTests.GetAudit_EmptyResult_ReturnsZeroTotals` |
| 12 | ✅ | Items are returned when the reader has entries | Endpoint test | `AdminAuditEndpointTests.GetAudit_WithItems_ReturnsItems` |
| 13 | ✅ | `pageSize > 200` is clamped to 200 | Endpoint test | `AdminAuditEndpointTests.GetAudit_PageSizeOver200_ClampedTo200` |
| 14 | ✅ | `DELETE /api/v1/admin/audit` requires API key — 401 without | Endpoint test | `AdminAuditEndpointTests.DeleteAudit_NoKey_Returns401` |
| 15 | ✅ | `DELETE /api/v1/admin/audit` returns 204 with correct key | Endpoint test | `AdminAuditEndpointTests.DeleteAudit_CorrectKey_Returns204` |
| 16 | ✅ | `DELETE /api/v1/admin/audit?table=Quotes` forwards table param | Endpoint test | `AdminAuditEndpointTests.DeleteAudit_WithTable_PassesTableToClearAsync` |
| 17 | ✅ | `DELETE /api/v1/admin/audit` (no table) passes null to `ClearAsync` | Endpoint test | `AdminAuditEndpointTests.DeleteAudit_NoTable_PassesNullToClearAsync` |
| 18 | ✅ | `GET /admin/database/seed/preview` is public | Endpoint test | `AdminEndpointsTests.PreviewSeed_NoKey_Returns200` |
| 19 | ✅ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 20 | ✅ | All tests pass — 497 tests | Build | `dotnet test --configuration Release` |
| 21 | ⬜ | App starts in VS; audit entries written on reseed/reset | T1 gate | Start app; call `POST /api/v1/admin/database/reseed`; verify `GET /api/v1/admin/audit` returns entry |
| 22 | ⬜ | Docker image builds and behaves correctly | T2 gate | `docker build -f docker/Dockerfile -t quotinator:local .` |
