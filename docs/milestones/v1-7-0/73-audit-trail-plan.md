# Issue #73 — Audit trail: record who did what on which record in which table

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `feature/v1-7-0`  
**Tiers required:** T1, T2

---

## Scope change (2026-06-27)

The original spec deferred this issue to the auth milestone because `PerformedBy` required an authenticated user. That dependency is removed.

**New design:** The `Agent` field is populated from an optional `X-Agent` request header via a scoped `ICallerContext` service. Callers that identify themselves (MagicMirror, smoke-test scripts, the Blazor UI circuit) are recorded. Callers that omit the header produce a null agent entry — the operation is still recorded. The authenticated `UserId` is added when auth lands, as a separate nullable column alongside `Agent`.

See the scope-change comment on [GitHub issue #73](https://github.com/DutchJaFO/Quotinator/issues/73#issuecomment-4816844699) for the full reasoning.

---

## Design

### Audit scope

Write operations only: Insert, Update, SoftDelete, Restore, HardDelete, Purge, Link, Unlink. Read operations are not audited — the existing request log (`log_requests: true`) covers read access. Bulk operations (e.g. database seed) write one summary entry per operation, not one per record.

### `ICallerContext` — scoped caller identity

```csharp
// Quotinator.Core
public interface ICallerContext
{
    string? Agent { get; set; }
}
```

Registered as scoped in DI. Two setters populate it per request:

| Setter | When | Value |
|---|---|---|
| API middleware (Program.cs) | Every HTTP request | `X-Agent` header value, or `null` if absent |
| Blazor layout (`MainLayout.razor.cs`) | Every Blazor circuit request | `"ui"` |

Repository write methods receive `ICallerContext` via constructor injection and read `Agent` when creating an `AuditEntry`.

### `X-Agent` header

Optional on all API requests. Callers that want their operations attributed set it:

```
X-Agent: magicmirror
X-Agent: smoke-test
X-Agent: home-assistant
```

**Header naming note:** `X-Agent` is a private extension header. RFC 6648 deprecated the `X-` prefix only for headers seeking IANA registration — not for private use. No conflict risk.

### `AuditEntries` table

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Auto-increment (not RecordBase — audit entries are immutable, never soft-deleted) |
| `TableName` | TEXT NOT NULL | Name of the table the operation touched (e.g. `"Quotes"`) |
| `RecordId` | TEXT | Guid of the affected row as string; null for bulk/summary entries |
| `Operation` | TEXT NOT NULL | One of: `Insert`, `Update`, `SoftDelete`, `Restore`, `HardDelete`, `Purge`, `Link`, `Unlink`, `BulkInsert` |
| `Agent` | TEXT | Value from `X-Agent` header, `"ui"` for Blazor circuit, null otherwise |
| `PerformedAt` | TEXT NOT NULL | ISO 8601 UTC timestamp |

**Does not extend `RecordBase`** — audit entries are immutable append-only records. Soft-delete, DateModified, and IsDeleted are inappropriate here.

**Two indexes:** `(TableName, RecordId)` for record-level queries; `(PerformedAt)` for time-range queries.

**Auth milestone path:** add nullable `UserId TEXT` column in the auth migration alongside `Agent`. No rework of this design needed.

### `AuditEntry` model

```csharp
// Quotinator.Core — Models/
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
    public const string Insert     = "Insert";
    public const string Update     = "Update";
    public const string SoftDelete = "SoftDelete";
    public const string Restore    = "Restore";
    public const string HardDelete = "HardDelete";
    public const string Purge      = "Purge";
    public const string Link       = "Link";
    public const string Unlink     = "Unlink";
    public const string BulkInsert = "BulkInsert";
}
```

### Repository integration

`IAuditWriter` (Quotinator.Data interface) has one method:

```csharp
Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction transaction);
```

Repository write methods call `IAuditWriter.WriteAsync` inside the same transaction as the operation they record. A failure to write the audit entry rolls back the operation — audit integrity is not optional.

### Admin read endpoint

`GET /api/v1/admin/audit` — requires `X-Api-Key`. Protected by the existing admin rate limiter.

Query parameters: `table` (filter by TableName), `recordId` (filter by RecordId), `page`, `pageSize`.

Response: paginated `AuditEntry` list, newest first.

---

## Files to create or modify

| File | Change |
|---|---|
| `src/Quotinator.Core/Models/AuditEntry.cs` | New — model + `AuditOperation` constants |
| `src/Quotinator.Core/Interfaces/ICallerContext.cs` | New — scoped caller identity interface |
| `src/Quotinator.Core/Interfaces/IAuditWriter.cs` | New — single write method |
| `src/Quotinator.Data/Audit/AuditWriter.cs` | New — Dapper implementation of `IAuditWriter` |
| `src/Quotinator.Core/Data/Sql.cs` | Add `Audit` nested class with insert + select queries |
| `src/Quotinator.Data/Database/DatabaseInitializer.cs` | New migration: `AuditEntries` table + two indexes |
| `src/Quotinator.Api/Program.cs` | Register `ICallerContext` (scoped); add `X-Agent` middleware |
| `src/Quotinator.Api/Components/Layout/MainLayout.razor.cs` | Set `ICallerContext.Agent = "ui"` on init |
| `src/Quotinator.Api/Endpoints/AdminEndpoints.cs` | Add `GET /api/v1/admin/audit` endpoint |
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
| 9 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 10 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 11 | ⬜ | User starts app in VS; app starts without error | Live | T1 gate |
