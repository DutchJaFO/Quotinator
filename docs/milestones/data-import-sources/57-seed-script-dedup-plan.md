# #57 — Seed script: dedup inconsistent

**Status:** Partially resolved — awaiting #58  
**GitHub issue:** #57  
**Closed by:** #61

---

## Original concern

The seed script accumulated duplicates because the old `data/quotes.json` was regenerated from multiple sources without consistent deduplication. Two different datasets could emit the same quote text with different IDs.

## Resolution

Problems 1–3 are eliminated by #61: each source writes its own file; cross-source deduplication and conflict resolution happen in `DatabaseInitializer` at seeding time, driven by the conflict-resolution policy (#64).

Problem 4 (ImportBatch entries) cannot be implemented until #58 lands. Once #58 is done, the seed script must create one `ImportBatch` row per source dataset and link all records from that source to it via `ImportBatchId`.

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1–3 | ✅ | Dedup/conflict/overwrite concerns eliminated | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` — one file per source, no combined output |
| 4 | ❌ | Seed script creates one ImportBatch per source | Unit test | Requires #58 — test to be written when #58 lands |
