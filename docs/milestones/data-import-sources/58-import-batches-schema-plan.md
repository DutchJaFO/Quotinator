# #58 — ImportBatches schema

**Status:** Not started  
**GitHub issue:** #58  
**Unblocks:** #56, #59, #45 (batch row), #64 (policy recording), #67, #68, #69

---

## Spec requirements

1. New `ImportBatches` table tracking every seeding or import run
2. Columns: `Id` (UUID), `SourceFile` (TEXT), `ImportedAt` (TEXT ISO 8601), `ConflictPolicy` (TEXT), `RecordCount` (INTEGER), `Actor` (TEXT — `"seeder"` for startup, `"api"` for import endpoint)
3. New nullable `ImportBatchId` (UUID FK → `ImportBatches.Id`) column on `Quotes`, `Characters`, `Sources`, `People`
4. Pre-seed batch rows for existing data (vilaboim, NikhilNamal17 datasets — `Actor = "seeder"`, `ImportedAt = NULL` or epoch)
5. Seeder creates one `ImportBatch` row per source file before seeding, writes `ImportBatchId` on all inserts for that file
6. Schema migration version bump

---

## Implementation steps

- [ ] Bump schema version in `DatabaseInitializer` (or equivalent migration table)
- [ ] Add `ImportBatches` table to schema DDL with all columns and indexes
- [ ] Add nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column to `Quotes`, `Characters`, `Sources`, `People`
- [ ] Add `ImportBatch` C# record/class in `Quotinator.Core`
- [ ] Add `IImportBatchRepository` and `SqliteImportBatchRepository` in `Quotinator.Data`
- [ ] Register in DI
- [ ] Update `DatabaseInitializer.SeedBatch` to create one `ImportBatch` row per source file and pass the ID to all insert calls
- [ ] Pre-seed batch rows for existing bundled data (vilaboim, NikhilNamal17) with known file names
- [ ] Tests: schema created correctly, FK constraints hold, pre-seed rows present
- [ ] Integration test: seeding two source files produces two distinct `ImportBatch` rows

---

## Notes

The `Actor` field distinguishes startup seeding (`"seeder"`) from API imports (`"api"`). This enables soft-reset (#59) to target only records from a specific run.

`ImportedAt` for pre-seed rows can be `NULL` (unknown) or a fixed sentinel like the epoch to avoid confusing real import timestamps.
