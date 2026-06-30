# #57 — Seed script: dedup inconsistent

**Status:** All problems resolved in code — pending release  
**GitHub issue:** #57  
**Closed by:** #61  
**Tiers required:** T1 only — pure data-layer logic, no Docker/HA-specific surface

---

## Original concern

The seed script accumulated duplicates because the old `data/quotes.json` was regenerated from multiple sources without consistent deduplication. Two different datasets could emit the same quote text with different IDs.

## Resolution

Problems 1–3 are eliminated by #61: each source writes its own file; cross-source deduplication and conflict resolution happen in `DatabaseInitializer` at seeding time, driven by the conflict-resolution policy (#64).

Problem 4 (ImportBatch entries) required #58 to land first. With #58's `Type`/`Url` fix in place, the seeder creates one `ImportBatch` row per source dataset and links every record from that source to it via `ImportBatchId`; `ImportBatches.RecordCount` is verified to match the actual linked row count per batch.

**Note (2026-06-30):** this fix is committed on `feature/data-import-sources` with T1 verified (unit tests, no Docker/HA-specific behaviour involved). It has not shipped in any release yet — "pending release", not "done".

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1–3 | ✅ | Dedup/conflict/overwrite concerns eliminated | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — one file per source, no combined output |
| 4 | ✅ | Seed script creates one ImportBatch per source and links all its records via ImportBatchId, with accurate RecordCount | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_QuotesLinkToOwningBatchAndRecordCountMatches` |
