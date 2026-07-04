# #45 — Import endpoint

**Status:** Planning
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

1. [ ] Define `ImportQuoteDto` and `ImportResultDto` in `Quotinator.Core`
2. [ ] Implement `IQuoteImportService` and `SqliteQuoteImportService`
3. [ ] Register in DI
4. [ ] Register `POST /api/v1/quotes/import` in `QuoteEndpoints.cs`
5. [ ] JSON array input parsing (canonical schema)
6. [ ] CSV input parsing (column mapping TBD — match canonical field names)
7. [ ] Multipart file upload handling
8. [ ] Conflict detection: same-ID and same-text matching
9. [ ] Apply conflict policy per record
10. [ ] `preview=true` path: wrap in a transaction, roll back after computing the result
11. [ ] Create `ImportBatch` row per run (needs #58)
12. [ ] Write `ImportBatchId` on all inserted records (needs #58)
13. [ ] `Authorization: Bearer` check against `Quotinator__AdminApiKey`
14. [ ] Rate limiting under admin policy
15. [ ] OpenAPI `[Description]` attributes on endpoint and parameters
16. [ ] Update `README.md` and `addon/DOCS.md` endpoint tables
17. [ ] Tests: happy path, conflict scenarios, preview mode, auth failure

---

## Notes

The `enrich=true` query parameter (auto-enrichment from external sources) is mentioned in the spec but depends on enrichment providers (#19, not in this milestone). Implement the parameter parse and return a `501 Not Implemented` response until #19 is available.

Auth design is not finalised for v3 yet. For now, `AdminApiKey` bearer token is the agreed mechanism (already used by admin endpoints). Do not add new auth mechanisms here.
