# #45 — Import endpoint

**Status:** Not started  
**GitHub issue:** #45  
**Depends on:** #58 (ImportBatch row), auth design (v3)  
**Unblocks:** #64 (per-import override), #65 (preview shape), #56 (audit log entries)

---

## Spec requirements

1. `POST /api/v1/quotes/import` — accepts a JSON array of quotes (canonical schema) or CSV, or multipart/form-data file upload
2. Query parameters: `conflictPolicy=skip|newest-wins|review`, `preview=true`
3. Response shape:
   ```json
   {
     "summary": {
       "imported": 12,
       "skipped": 3,
       "enriched": 0,
       "errors": [],
       "conflicts": {
         "sameId": [{ "incoming": {}, "existing": {}, "fieldDiffs": {} }],
         "sameText": [{ "incoming": {}, "existing": {} }]
       }
     }
   }
   ```
4. Server assigns IDs — any `id` field in the import payload is ignored
5. Atomic per-batch: a single malformed row does not abort the entire import
6. One `ImportBatch` row created per import run (needs #58)
7. Requires `AdminApiKey` auth (`Authorization: Bearer <key>`)
8. Rate limited under the admin rate-limit policy
9. `preview=true` performs all validation and conflict detection but does not write to the database (transaction rollback or dry-run path)

---

## Implementation steps

- [ ] Define `ImportQuoteDto` and `ImportResultDto` in `Quotinator.Core`
- [ ] Implement `IQuoteImportService` and `SqliteQuoteImportService`
- [ ] Register in DI
- [ ] Register `POST /api/v1/quotes/import` in `QuoteEndpoints.cs`
- [ ] JSON array input parsing (canonical schema)
- [ ] CSV input parsing (column mapping TBD — match canonical field names)
- [ ] Multipart file upload handling
- [ ] Conflict detection: same-ID and same-text matching
- [ ] Apply conflict policy per record
- [ ] `preview=true` path: wrap in a transaction, roll back after computing the result
- [ ] Create `ImportBatch` row per run (needs #58)
- [ ] Write `ImportBatchId` on all inserted records (needs #58)
- [ ] `Authorization: Bearer` check against `Quotinator__AdminApiKey`
- [ ] Rate limiting under admin policy
- [ ] OpenAPI `[Description]` attributes on endpoint and parameters
- [ ] Update `README.md` and `addon/DOCS.md` endpoint tables
- [ ] Tests: happy path, conflict scenarios, preview mode, auth failure

---

## Notes

The `enrich=true` query parameter (auto-enrichment from external sources) is mentioned in the spec but depends on enrichment providers (#19, not in this milestone). Implement the parameter parse and return a `501 Not Implemented` response until #19 is available.

Auth design is not finalised for v3 yet. For now, `AdminApiKey` bearer token is the agreed mechanism (already used by admin endpoints). Do not add new auth mechanisms here.
