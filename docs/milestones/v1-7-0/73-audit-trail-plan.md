# Issue #73 — Audit trail: record who did what on which record in which table

**Milestone:** v1.7.0  
**Status:** Open — deferred to auth milestone  
**Branch:** n/a

---

## Status note

This issue is explicitly deferred in its own spec. Implementation requires:
- Authentication to be in place (local user accounts, API key, or HA token — see auth milestone)
- Write endpoints (POST, PUT, DELETE) to be implemented

`RecordBase` already preserves state (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`) on every row, so nothing is lost by deferring — the `PerformedBy` column will simply remain empty until an authenticated user identity is available.

This plan doc exists to satisfy the workflow requirement that every issue in the milestone has a plan doc (or an explicit recorded reason for not having one). See `overview.md` for the reasoning.

---

## Design (from issue spec — for reference)

Narrow scope: the audit log answers only the **who** question. State is already on the row via `RecordBase`.

**`AuditEntries` table fields:**
- `Id` — RecordBase surrogate key
- `TableName` — which table the operation touched
- `RecordId` — Guid of the affected row
- `Operation` — enum: Insert, Update, SoftDelete, Restore, HardDelete, Purge, Link, Unlink
- `PerformedBy` — authenticated user identity (empty until auth milestone)
- `PerformedAt` — timestamp

No payload. No per-entity schema. Single table, two indexes (TableName + RecordId).

**Read endpoint:** `GET /api/v1/admin/audit?table=&recordId=&page=&pageSize=`

---

## Verification

_To be defined when the auth milestone is started and this issue is picked up._

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `AuditEntries` table created via DatabaseInitializer migration | Unit test | TBD |
| 2 | ⬜ | Repository write methods record an audit entry in the same transaction | Unit test | TBD |
| 3 | ⬜ | Read endpoint returns paginated audit entries filterable by table + recordId | Unit test | TBD |
| 4 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 5 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
