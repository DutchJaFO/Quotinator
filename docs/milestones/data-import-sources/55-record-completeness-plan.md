# #55 — Record completeness flag

**Status:** Not started  
**GitHub issue:** #55  
**Connects to:** #56 (audit log entries), #19 (enrichment, different milestone)

---

## Spec requirements

1. New `IsComplete` column (BIT DEFAULT 0) on `Quotes`, `Characters`, `Sources`, `People`
2. New `NoValueKnown` column (TEXT, JSON array, DEFAULT `'[]'`) on the same tables — stores field names where the value is confirmed absent (not just unknown)
3. Imported records always start with `IsComplete = false`
4. Enrichment providers skip records where `IsComplete = true`
5. Enrichment providers skip fields whose name is in `NoValueKnown`
6. Management UI actions: "Mark as complete" and "Mark field as no value known" (Blazor UI, v3 milestone)
7. Stats endpoint: returns counts of complete vs incomplete records

---

## Implementation steps

- [ ] Schema migration: add `IsComplete BIT NOT NULL DEFAULT 0` and `NoValueKnown TEXT NOT NULL DEFAULT '[]'` to all four tables
- [ ] Update C# entity models in `Quotinator.Core` to include `IsComplete` (bool) and `NoValueKnown` (string[], deserialized from JSON)
- [ ] Update all `INSERT` paths to write `IsComplete = 0` and `NoValueKnown = '[]'` explicitly (or rely on DEFAULT)
- [ ] Type handler or JSON serialisation for `NoValueKnown` string[] ↔ TEXT
- [ ] `IQuoteService.GetStatsAsync()` (or equivalent) — add `CompleteCount` / `IncompleteCount` to the stats response
- [ ] Update `GET /api/v1/version` stats block if it exposes record counts
- [ ] Enrichment hooks (deferred to #19): skip `isComplete=true`; skip fields in `noValueKnown`
- [ ] Blazor UI hooks (deferred to v3): "Mark complete" and "Mark field no-value" actions
- [ ] Tests: schema migration, DEFAULT values on insert, stats counts

---

## Notes

`NoValueKnown` stores field names as a JSON array in a TEXT column (`["character","author"]`). A Dapper type handler converts it to `string[]` on read. This avoids a separate join table for a small, rarely-populated list.

The management UI and enrichment integrations are deferred — this issue only delivers the schema and model changes.
