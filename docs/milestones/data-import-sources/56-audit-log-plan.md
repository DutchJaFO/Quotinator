# #56 — Audit log

**Status:** Not started  
**GitHub issue:** #56  
**Depends on:** #58 (ImportBatch actor field)  
**Connects to:** #45, #55, #59

---

## Spec requirements

1. New `AuditLog` table: `Id`, `EntityType` (TEXT), `EntityId` (UUID), `Action` (TEXT), `ActorType` (TEXT — `"seeder"` | `"api"` | `"user"`), `ActorId` (TEXT, nullable — ImportBatchId or UserId), `OldValue` (TEXT JSON, nullable), `NewValue` (TEXT JSON, nullable), `Timestamp` (TEXT ISO 8601)
2. Index on `(EntityId, Timestamp DESC)`
3. Actions logged:
   - `created` — on import or seeder insert
   - `updated` — on write endpoint PUT
   - `deleted` — on write endpoint DELETE
   - `imported` — on import endpoint (same as `created` but with `ActorType = "api"`)
   - `enriched` — on enrichment provider write (field-level, `OldValue`/`NewValue`)
   - `completed` — when `IsComplete` set to true via management action (#55)
   - `verified_absent` — when a field is added to `NoValueKnown` (#55)
   - `batch_reset` — on soft-reset (#59)
   - `batch_restore` — on soft-restore (#59)
4. `GET /api/v1/quotes/{id}/history` — returns audit log entries for a quote, newest first
5. Equivalent history endpoints for `/characters/{id}/history`, `/sources/{id}/history`, `/people/{id}/history` (future; can defer)
6. History page in Blazor UI (v3 milestone)

---

## Implementation steps

1. [ ] Schema migration: create `AuditLog` table with index on `(EntityId, Timestamp DESC)`
2. [ ] `AuditEntry` C# record in `Quotinator.Core`
3. [ ] `IAuditLogger` interface in `Quotinator.Core`
4. [ ] `SqliteAuditLogger` in `Quotinator.Data`
5. [ ] Register `IAuditLogger` in DI as scoped
6. [ ] Wire into import endpoint (#45): log `imported` per record on success
7. [ ] Wire into write endpoints (when #16 is implemented): log `created`, `updated`, `deleted`
8. [ ] Wire into enrichment (when #19 is implemented): log `enriched` per field
9. [ ] Wire into completeness actions (#55): log `completed`, `verified_absent`
10. [ ] Wire into soft-reset (#59): log `batch_reset`, `batch_restore`
11. [ ] Register `GET /api/v1/quotes/{id}/history` endpoint in `QuoteEndpoints.cs`
12. [ ] Update `README.md` and `addon/DOCS.md` with history endpoint
13. [ ] Tests: audit entries written on create/update/import, history endpoint returns correct entries

---

## Notes

`OldValue` / `NewValue` store a JSON snapshot of the changed fields (not the full entity). For `created` and `deleted`, only `NewValue` / `OldValue` applies (the other is NULL). For `enriched`, both apply — one field at a time or one batch snapshot, depending on enrichment provider design.

The `ActorId` for seeder runs is the `ImportBatch.Id`. For API calls, it is the future user ID (or `NULL` until auth is in place).
