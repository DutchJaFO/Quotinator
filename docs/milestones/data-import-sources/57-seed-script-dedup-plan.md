# #57 — Seed script: dedup inconsistent

**Status:** All problems resolved in code — pending release
**GitHub issue:** #57
**Closed by:** #61
**Tiers required:** T1 only — pure data-layer logic, no Docker/HA-specific surface

---

## Original concern

The seed script accumulated duplicates because the old `data/quotes.json` was regenerated from multiple sources without consistent deduplication. Two different datasets could emit the same quote text with different IDs.

## Spec requirements (Problems 1–4)

1. Each source produces its own output file — no combined/concatenated `quotes.json` that could silently merge or drop records
2. Cross-source duplicate quotes (same ID appearing in more than one source file) are detected and resolved according to the conflict-resolution policy, not silently overwritten or duplicated
3. Duplicate handling is deterministic and consistent regardless of source file processing order
4. The seeder creates one `ImportBatch` row per source dataset and links every record from that source to it via `ImportBatchId`, with `ImportBatches.RecordCount` matching the actual number of linked rows

---

## Step status

- [x] One file per source (no combined output) — #61
- [x] Cross-source duplicate detection and resolution via conflict-resolution policy — #61
- [x] Deterministic, order-independent duplicate handling — #61
- [x] One `ImportBatch` row per source, all records linked via `ImportBatchId`, `RecordCount` accurate — #58 + this session

---

## Resolution

Problems 1–3 are eliminated by #61: each source writes its own file; cross-source deduplication and conflict resolution happen in `DatabaseInitializer` at seeding time, driven by the conflict-resolution policy (#64).

Problem 4 (ImportBatch entries) required #58 to land first. With #58's `Type`/`Url` fix in place, the seeder creates one `ImportBatch` row per source dataset and links every record from that source to it via `ImportBatchId`; `ImportBatches.RecordCount` is verified to match the actual linked row count per batch.

**Note (2026-06-30):** this fix is committed on `feature/data-import-sources` with T1 verified (unit tests, no Docker/HA-specific behaviour involved). It has not shipped in any release yet — "pending release", not "done".

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | One file per source, no combined output | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` |
| 2 | ✅ | Cross-source duplicates resolved per conflict-resolution policy | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` |
| 3 | ✅ | Deterministic, order-independent duplicate handling | Unit test | `RepositoryStructureTests.SeedScript_WithNoFetch_ProducesFilesMatchingBaseline` |
| 4 | ✅ | One ImportBatch per source, records linked via ImportBatchId, RecordCount accurate | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_QuotesLinkToOwningBatchAndRecordCountMatches` |
