# #59 — Admin: soft-reset by batch

**Status:** Planning
**GitHub issue:** #59  
**Depends on:** #58 (ImportBatches), #56 (audit log)

---

## Spec requirements

1. `POST /api/v1/admin/import-batches/{id}/reset` — soft-deletes all records that belong to a specific import batch
2. Records shared by another batch (same UUID in multiple batches) are skipped
3. Records manually created (no `ImportBatchId`) are skipped
4. Response: `{ "reset": N, "skipped": M, "reason": "..." }`
5. `POST /api/v1/admin/import-batches/{id}/reset?preview=true` — returns what would be reset/skipped without modifying the database
6. `modifiedAfterImport` count in the response — records that were changed after the batch was imported (detected via audit log timestamps)
7. `POST /api/v1/admin/import-batches/{id}/restore` — reverses a soft-delete (restores records)
8. `batch_reset` and `batch_restore` audit log entries written on each operation (#56)
9. Requires `AdminApiKey` auth
10. Rate limited under admin policy

---

## Implementation steps

1. [ ] Add `DeletedAt TEXT` (nullable) column to `Quotes`, `Characters`, `Sources`, `People` via schema migration — this is the soft-delete flag
2. [ ] Update all read queries to filter `WHERE DeletedAt IS NULL`
3. [ ] `POST /api/v1/admin/import-batches/{id}/reset` endpoint
  - [ ] Validate batch exists
  - [ ] Find all records where `ImportBatchId = {id}` AND not referenced by any other batch AND `DeletedAt IS NULL`
  - [ ] Detect `modifiedAfterImport` via audit log (`Action IN ('updated','enriched')` after batch `ImportedAt`)
  - [ ] Set `DeletedAt = NOW()` on eligible records
  - [ ] Write `batch_reset` audit entries (#56)
  - [ ] Return summary
4. [ ] `?preview=true` path: transaction rollback, return count without committing
5. [ ] `POST /api/v1/admin/import-batches/{id}/restore` endpoint
  - [ ] Set `DeletedAt = NULL` on records from that batch
  - [ ] Write `batch_restore` audit entries
  - [ ] Return summary
6. [ ] `Authorization: Bearer` check against `Quotinator__AdminApiKey`
7. [ ] Rate limiting under admin policy
8. [ ] Update `README.md`, `addon/DOCS.md`, OpenAPI descriptions
9. [ ] Tests: reset skips shared/manual records, preview returns counts, restore works, audit entries written

---

## Notes

"Shared by another batch" means: the same `SourceId` / `CharacterId` / `PersonId` / `QuoteId` appears in records from a different `ImportBatch`. Because the FK chain (Quotes → Sources, Characters, People) is directional, the reset must work bottom-up: reset Quotes first, then Characters/Sources/People if no other Quotes reference them.
