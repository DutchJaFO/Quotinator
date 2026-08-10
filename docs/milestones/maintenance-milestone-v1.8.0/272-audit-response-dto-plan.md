# #272 — Add AuditEntryResponse/AuditChangeResponse DTOs, stop leaking SafeValue's raw/parsed wrapper

**Status:** Waiting for release
**GitHub issue:** #272
**Tiers required:** N/A
**Depends on:** #265 (investigation this issue implements)

---

## Background

#265's investigation (verified live) found `GET /admin/audit` and `GET /admin/audit/export` are the
only two endpoints that serialize a database entity directly to JSON with no response-mapping layer.
The concrete, demonstrated problem: `AuditEntryEntity`/`ChangeEntity` extend `RecordBase`, whose
`DateCreated`/`DateModified`/`DateDeleted` are `SafeValue<DateTime?>` — a type with no
`[JsonConverter]` registered anywhere, so `System.Text.Json`'s default serialization exposes its
internal `{"raw": "...", "parsed": "...", "isValid": true}` plumbing directly in the API response.

Two corrections from the developer during #265/#272's review that shape this plan:
- **Enums stay enums.** `ChangeEntity.InitiatedByType`/`.Action` (`SafeValue<TEnum?>`) are typed on
  the response DTO as their actual C# enum (`InitiatorType?`/`ChangeAction?`) with
  `[JsonConverter(typeof(JsonStringEnumConverter))]` — confirmed no global `JsonStringEnumConverter`
  is registered in `Program.cs`, so this per-property attribute is required. This deliberately does
  *not* follow `ImportBatchEndpoints.ToResponse`'s existing pattern of flattening a `SafeValue<TEnum?>`
  to a lowercased `string` — that precedent predates this decision and stays as-is, not retrofitted.
- **No column is dropped.** Every `RecordBase` column stays on the response, unwrapped to its plain
  `DateTime?`/`bool` value instead of omitted — an API consumer needs visibility into a row
  modified/deleted outside the app's own normal write path, not have that silently hidden.

**One correction to the issue body itself, found while planning:** #272's own field list for
`AuditChangeResponse` omits `Id`, even though `ChangeEntity` inherits `Id` from `RecordBase` the same
way `AuditEntryEntity` does. `AuditChangeResponse` includes `Id`, matching the issue's own "carrying
every field `ChangeEntity` has today" framing and `AuditEntryResponse`'s parallel shape.

## Call-site scope

- **Two new files**: `src/Quotinator.Core/Models/AuditEntryResponse.cs`,
  `src/Quotinator.Core/Models/AuditChangeResponse.cs`.
- **`AdminEndpoints.cs`**: two new private static mapping methods, plus the `/audit` and
  `/audit/export` `MapGet` handlers.
- **`AuditExportResponse.cs`**: `Entries`/`Changes` member types change.
- **Tests**: `tests/Quotinator.Api.Tests/Endpoints/AdminAuditEndpointTests.cs` (existing file, reuses
  its `CreateFactory`/`StubAuditReader`/`StubChangeReader` fixtures).

---

## Steps

### 1. Add AuditEntryResponse and AuditChangeResponse

**Status:** ✅ Done

`AuditEntryResponse`: `Id`/`TableName`/`RecordId`/`Operation`/`Agent`/`PerformedAt`/`DateCreated`/
`DateModified`/`DateDeleted`/`IsDeleted` — `Id` as canonical `string` (`.ToCanonicalId()`, matching
`ImportBatchResponse`'s own precedent), the four `RecordBase` columns as plain `DateTime?`/`bool`.
`AuditChangeResponse`: `Id`/`EntityType`/`EntityId`/`InitiatedByType`/`InitiatedById`/`Action`/
`Field`/`OldValue`/`NewValue`/`OccurredAt`/`DateCreated`/`DateModified`/`DateDeleted`/`IsDeleted` —
`InitiatedByType`/`Action` typed as `InitiatorType?`/`ChangeAction?` with
`[JsonConverter(typeof(JsonStringEnumConverter))]` each.

### 2. Write the four required tests (red)

**Status:** ✅ Done — confirmed red: `dotnet test --filter` on all four returned 4 failed before Step 3.

Added to `AdminAuditEndpointTests.cs`: `GetAuditLog_ResponseShape_NoSafeValueWrapperInJson`,
`GetAuditLog_ResponseShape_PreservesDateModifiedWhenSet`,
`ExportAuditTrail_ResponseShape_NoSafeValueWrapperInJson`,
`ExportAuditTrail_ChangeResponseShape_ActionIsEnumNotString`. Confirmed red (fail to compile — the
new Response types don't exist yet) before Step 3.

### 3. Wire the mapping into both endpoints

**Status:** ✅ Done

Two private static mapping methods in `AdminEndpoints.cs` (matching `ImportBatchEndpoints.ToResponse`'s
own precedent — a private static method in the endpoints file, not a separate mapper class):
`ToAuditEntryResponse(AuditEntryEntity)`, `ToAuditChangeResponse(ChangeEntity)`. `GET /admin/audit`
maps `result.Items` into a new `PagedItems<AuditEntryResponse>` (same construction pattern
`ImportBatchEndpoints.cs`'s own list endpoint already uses) and its `.Produces<>()` changes to
`PagedItems<AuditEntryResponse>`. `GET /admin/audit/export` maps `entries`/`changes` before
constructing `AuditExportResponse`, whose `Entries`/`Changes` properties change to
`IReadOnlyList<AuditEntryResponse>`/`IReadOnlyList<AuditChangeResponse>`.

### 4. Full solution build and test sweep

**Status:** ✅ Done — 0 warnings, 0 errors; 629/629 tests passed (625 existing + 4 new).

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green,
including the four new tests now passing.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Four required tests exist and started red | Unit test | Ran before Step 3 existed — all four failed to compile |
| 2 | ✅ | `AuditEntryResponse`/`AuditChangeResponse` added, `RecordBase` columns unwrapped (not dropped), enums stay enums | Unit test | `GetAuditLog_ResponseShape_NoSafeValueWrapperInJson`, `GetAuditLog_ResponseShape_PreservesDateModifiedWhenSet` pass |
| 3 | ✅ | `GET /admin/audit`/`GET /admin/audit/export` return the new Response types, no `SafeValue` wrapper in JSON | Unit test | `ExportAuditTrail_ResponseShape_NoSafeValueWrapperInJson`, `ExportAuditTrail_ChangeResponseShape_ActionIsEnumNotString` pass |
| 4 | ✅ | No regression in existing audit endpoint tests | Unit test | Full `AdminAuditEndpointTests.cs` passes unchanged |
| 5 | ✅ | Build clean | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors; full test suite passes (629/629) |
