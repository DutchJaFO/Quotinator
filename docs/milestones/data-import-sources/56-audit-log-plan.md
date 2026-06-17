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

- [ ] Schema migration: create `AuditLog` table with index on `(EntityId, Timestamp DESC)`
- [ ] `AuditEntry` C# record in `Quotinator.Core`
- [ ] `IAuditLogger` interface in `Quotinator.Core`
- [ ] `SqliteAuditLogger` in `Quotinator.Data`
- [ ] Register `IAuditLogger` in DI as scoped
- [ ] Wire into import endpoint (#45): log `imported` per record on success
- [ ] Wire into write endpoints (when #16 is implemented): log `created`, `updated`, `deleted`
- [ ] Wire into enrichment (when #19 is implemented): log `enriched` per field
- [ ] Wire into completeness actions (#55): log `completed`, `verified_absent`
- [ ] Wire into soft-reset (#59): log `batch_reset`, `batch_restore`
- [ ] Register `GET /api/v1/quotes/{id}/history` endpoint in `QuoteEndpoints.cs`
- [ ] Update `README.md` and `addon/DOCS.md` with history endpoint
- [ ] Tests: audit entries written on create/update/import, history endpoint returns correct entries

---

## Notes

`OldValue` / `NewValue` store a JSON snapshot of the changed fields (not the full entity). For `created` and `deleted`, only `NewValue` / `OldValue` applies (the other is NULL). For `enriched`, both apply — one field at a time or one batch snapshot, depending on enrichment provider design.

The `ActorId` for seeder runs is the `ImportBatch.Id`. For API calls, it is the future user ID (or `NULL` until auth is in place).
