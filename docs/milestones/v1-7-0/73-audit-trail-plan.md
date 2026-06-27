# Issue #73 — Audit trail: record who did what on which record in which table

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `feature/v1-7-0`  
**Tiers required:** T1, T2

---

## Scope change (2026-06-27)

The original spec deferred this issue to the auth milestone because `PerformedBy` required an authenticated user. That dependency is removed.

**New design:** The `Agent` field is populated from the standard `User-Agent` request header via a scoped `ICallerContext` service. Callers that identify themselves (MagicMirror, smoke-test scripts, the Blazor UI circuit) are recorded. Callers that omit the header produce a null agent entry — the operation is still recorded. The authenticated `UserId` is added when auth lands, as a separate nullable column alongside `Agent`.

See the scope-change comment on [GitHub issue #73](https://github.com/DutchJaFO/Quotinator/issues/73#issuecomment-4816844699) for the full reasoning.

---

## Dependency analysis

### Actual project dependency graph

```
Quotinator.Data  (no project references)
  └─ exposes internals to Quotinator.Core via InternalsVisibleTo
       ↑ referenced by
Quotinator.Core  (→ Data; has Dapper + Microsoft.Data.Sqlite)
       ↑ referenced by
Quotinator.Api   (→ Core, → Data, → Changelog, → Constants)
```

`Quotinator.Data` sits at the bottom. Core and Api both depend on it. **Data cannot reference Core — there is no upward reference.** This determines where every audit type must live.

### Where audit types must live

| Type | Project | Reason |
|---|---|---|
| `ICallerContext` | `Quotinator.Data` | Must be reachable by Data's repository base class AND by Core's services AND by Api's middleware; only Data satisfies all three since both Core and Api reference it |
| `IAuditWriter` | `Quotinator.Data` | Same reasoning; `AuditWriter` (also in Data) implements it |
| `AuditEntry` entity | `Quotinator.Data/Entities/` | Data entities live here; Core and Api can reach it |
| `AuditWriter` | `Quotinator.Data` | Direct Dapper implementation — see circular reference rule below |
| `CallerContext` | `Quotinator.Data` | Scoped implementation; Api middleware sets it, Data's base class reads it |

The original plan doc placed `ICallerContext` and `IAuditWriter` in Core. That was wrong — Data cannot reference Core, so `AuditWriter` (a Data-layer class) could not implement a Core-defined interface.

### Circular reference rule — `AuditWriter` must NOT extend `SqliteRepository<T>`

`SqliteRepository<T>` will receive `IAuditWriter` in its constructor and call `WriteAsync` on every write operation. If `AuditWriter` also extended `SqliteRepository<T>`, its constructor would require `IAuditWriter` — which is itself. The DI container cannot resolve this.

**Rule: `AuditWriter` is a standalone direct-Dapper class. It never extends `SqliteRepository<T>` and never calls any repository.**

```
SqliteRepository<T>(IAuditWriter, ICallerContext, IDbConnectionFactory)
    └─ calls IAuditWriter.WriteAsync(...)
         └─ AuditWriter : IAuditWriter
               └─ direct Dapper INSERT — no repository, no recursion
```

### Reading audit entries

The `GET /api/v1/admin/audit` endpoint reads audit entries. It must NOT use `SqliteRepository<AuditEntry>` because:
1. `SqliteRepository<T>` carries `IAuditWriter` in its constructor, implying the audit reader could write audit entries — conceptually wrong.
2. It would require registering `SqliteRepository<AuditEntry>` in DI, which would again bring `IAuditWriter` into a read-only context.

**The audit read endpoint uses the read-model query pattern from #74** — a lightweight, read-only query class with no write capability and no `IAuditWriter` dependency.

---

## Design

### Audit scope

Two categories of operations are audited:

**1. Record-level write operations** — handled automatically by the repository base class:

| Operation | When |
|---|---|
| `Insert` | A single record is created |
| `Update` | A record is modified |
| `SoftDelete` | A record is marked deleted |
| `Restore` | A soft-deleted record is reinstated |
| `HardDelete` | A record is permanently removed |
| `Purge` | All soft-deleted records in a table are permanently removed |
| `Link` | A many-to-many join record is created |
| `Unlink` | A many-to-many join record is removed |
| `BulkInsert` | A batch of records is inserted (one summary entry per batch, not per row) |

Read operations are not audited — the request log (`log_requests: true`) covers read access to quote endpoints.

**2. Admin actions** — called directly on `IAuditWriter` by admin endpoint handlers (not via the repository):

| Operation | Endpoint | `TableName` | Notes |
|---|---|---|---|
| `Reseed` | `POST /api/v1/admin/database/reseed` | `"Database"` | Replaces all data with bundled source files |
| `Reset` | `POST /api/v1/admin/database/reset` | `"Database"` | Drops and recreates all tables |
| `Import` | Future import endpoint | `"Database"` | User-provided import file processed |
| `Backup` | Future backup endpoint | `"Database"` | Database backup created |

Admin actions use `TableName = "Database"` and `RecordId = null` — they are database-level operations, not row-level. Admin endpoint handlers call `IAuditWriter.WriteAsync` directly, injecting it via DI. They do not go through the repository base class.

> **Important distinction:** logging and audit are distinct features. The *request log* confirms that an endpoint was called (method, URL, status, duration) — it covers all routes including admin. The *audit trail* records what was done (operation, agent, affected record) — it covers write operations and admin actions only. The security rule is the same for both: never capture header values (`X-Api-Key`, `Authorization`, `Cookie`).

### `ICallerContext` — scoped caller identity

```csharp
// Quotinator.Data
public interface ICallerContext
{
    string? Agent { get; set; }
}
```

Registered as scoped in DI. Two setters populate it per request:

| Setter | When | Value |
|---|---|---|
| API middleware (Program.cs) | Every HTTP request | `User-Agent` header value, or `null` if absent |
| Blazor layout (`MainLayout.razor.cs`) | Every Blazor circuit request | `"ui"` |

Repository write methods receive `ICallerContext` via constructor injection and read `Agent` when creating an `AuditEntry`.

### `User-Agent` header

`ICallerContext.Agent` is populated from the standard HTTP `User-Agent` request header — not a custom header. RFC 7231 defines `User-Agent` as the correct place to identify the software making a request. RFC 6648 deprecated the `X-` prefix precisely to prevent custom headers proliferating when a standard already exists. Callers set it as they already do with any HTTP client:

```
User-Agent: magicmirror
User-Agent: smoke-test
User-Agent: home-assistant
```

No custom header to document. Callers that already set `User-Agent` (most HTTP clients do) are automatically identified.

### `AuditEntries` table

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Auto-increment (not RecordBase — audit entries are immutable, never soft-deleted) |
| `TableName` | TEXT NOT NULL | Name of the table the operation touched (e.g. `"Quotes"`) |
| `RecordId` | TEXT | Guid of the affected row as string; null for bulk/summary entries |
| `Operation` | TEXT NOT NULL | One of: `Insert`, `Update`, `SoftDelete`, `Restore`, `HardDelete`, `Purge`, `Link`, `Unlink`, `BulkInsert` |
| `Agent` | TEXT | Value from `User-Agent` header, `"ui"` for Blazor circuit, null otherwise |
| `PerformedAt` | TEXT NOT NULL | ISO 8601 UTC timestamp |

**Does not extend `RecordBase`** — audit entries are immutable append-only records. Soft-delete, DateModified, and IsDeleted are inappropriate here.

**Two indexes:** `(TableName, RecordId)` for record-level queries; `(PerformedAt)` for time-range queries.

**Auth milestone path:** add nullable `UserId TEXT` column in the auth migration alongside `Agent`. No rework of this design needed.

### `AuditEntry` model

```csharp
// Quotinator.Data — Entities/
public class AuditEntry
{
    public long Id { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string? RecordId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? Agent { get; init; }
    public DateTime PerformedAt { get; init; }
}

public static class AuditOperation
{
    // Record-level — written automatically by repository base class
    public const string Insert     = "Insert";
    public const string Update     = "Update";
    public const string SoftDelete = "SoftDelete";
    public const string Restore    = "Restore";
    public const string HardDelete = "HardDelete";
    public const string Purge      = "Purge";
    public const string Link       = "Link";
    public const string Unlink     = "Unlink";
    public const string BulkInsert = "BulkInsert";

    // Admin actions — written directly by admin endpoint handlers
    public const string Reseed  = "Reseed";
    public const string Reset   = "Reset";
    public const string Import  = "Import";
    public const string Backup  = "Backup";
}
```

### Repository integration

`IAuditWriter` (Quotinator.Data interface) has one method:

```csharp
Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction transaction);
```

**The repository base class (#74) receives `IAuditWriter` and `ICallerContext` in its constructor and calls `WriteAsync` automatically on every write operation.** Concrete repositories inherit this behaviour — they do not wire audit individually. This is why #73 must be complete before #74 begins: every repository built in #74–#77 depends on the audit infrastructure being in place from the start.

A failure to write the audit entry rolls back the triggering operation — audit integrity is not optional.

### Admin read endpoint

`GET /api/v1/admin/audit` — requires `X-Api-Key`. Protected by the existing admin rate limiter.

Query parameters: `table` (filter by TableName), `recordId` (filter by RecordId), `page`, `pageSize`.

Response: paginated `AuditEntry` list, newest first.

---

## Files to create or modify

| File | Change |
|---|---|
| `src/Quotinator.Data/Entities/AuditEntry.cs` | New — entity + `AuditOperation` string constants |
| `src/Quotinator.Data/Repositories/ICallerContext.cs` | New — scoped caller identity interface (in Data so Core, Data, and Api can all reach it) |
| `src/Quotinator.Data/Repositories/IAuditWriter.cs` | New — single `WriteAsync` method |
| `src/Quotinator.Data/Repositories/CallerContext.cs` | New — mutable scoped implementation; Api middleware and Blazor layout both set `Agent` |
| `src/Quotinator.Data/Repositories/AuditWriter.cs` | New — direct Dapper INSERT; must NOT extend `SqliteRepository<T>` (see circular reference rule) |
| `src/Quotinator.Data/Repositories/SqliteRepository.cs` | Extend constructor: add `IAuditWriter` + `ICallerContext`; call `WriteAsync` in write methods |
| `src/Quotinator.Data/Queries/Sql.cs` | Add `Audit` nested class: insert query (for AuditWriter) + select query (for read endpoint) |
| `src/Quotinator.Data/Database/DatabaseInitializer.cs` | New migration: `AuditEntries` table + two indexes |
| `src/Quotinator.Api/Program.cs` | Register `ICallerContext` and `IAuditWriter` (scoped); add `User-Agent` to `ICallerContext.Agent` middleware |
| `src/Quotinator.Api/Components/Layout/MainLayout.razor.cs` | Set `ICallerContext.Agent = "ui"` on `OnInitializedAsync` |
| `src/Quotinator.Api/Endpoints/AdminEndpoints.cs` | Add `GET /api/v1/admin/audit` endpoint using read-model query; add `IAuditWriter` calls to `reseed` and `reset` handlers |
| `tests/Quotinator.Data.Tests/Audit/AuditWriterTests.cs` | New — integration tests against real SQLite |
| `tests/Quotinator.Api.Tests/Endpoints/AdminAuditEndpointTests.cs` | New — endpoint tests |

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `AuditEntries` table created by DatabaseInitializer migration; two indexes present | Integration test | `AuditMigrationTests.AuditEntries_TableAndIndexes_ExistAfterMigration` |
| 2 | ⬜ | `ICallerContext.Agent` is populated from `X-Agent` header on API requests | Unit test | `CallerContextMiddlewareTests.Agent_SetFromXAgentHeader_WhenPresent` |
| 3 | ⬜ | `ICallerContext.Agent` is null when `X-Agent` header is absent | Unit test | `CallerContextMiddlewareTests.Agent_IsNull_WhenHeaderAbsent` |
| 4 | ⬜ | Blazor layout sets `ICallerContext.Agent` to `"ui"` | Unit test | `MainLayoutTests.CallerContext_Agent_IsUi_OnInit` |
| 5 | ⬜ | `AuditWriter.WriteAsync` inserts a row in the same transaction as the triggering operation; rollback of the operation rolls back the audit entry | Integration test | `AuditWriterTests.Write_InsertsAuditEntry_InSameTransaction` and `AuditWriterTests.Write_RollsBack_WhenOperationRollsBack` |
| 6 | ⬜ | `GET /api/v1/admin/audit` returns paginated entries filtered by `table` | Endpoint test | `AdminAuditEndpointTests.GetAudit_FiltersByTableName` |
| 7 | ⬜ | `GET /api/v1/admin/audit` returns paginated entries filtered by `recordId` | Endpoint test | `AdminAuditEndpointTests.GetAudit_FiltersByRecordId` |
| 8 | ⬜ | `GET /api/v1/admin/audit` requires `X-Api-Key`; returns 401 without it | Endpoint test | `AdminAuditEndpointTests.GetAudit_Returns401_WithoutApiKey` |
| 9 | ⬜ | `POST /api/v1/admin/database/reseed` writes a `Reseed` audit entry with `TableName="Database"` | Integration test | `AdminAuditEndpointTests.Reseed_WritesAuditEntry_WithDatabaseTableName` |
| 10 | ⬜ | `POST /api/v1/admin/database/reset` writes a `Reset` audit entry with `TableName="Database"` | Integration test | `AdminAuditEndpointTests.Reset_WritesAuditEntry_WithDatabaseTableName` |
| 11 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 12 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 13 | ⬜ | User starts app in VS; app starts without error | Live | T1 gate |
