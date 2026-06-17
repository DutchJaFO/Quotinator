# Data Import & Sources — Milestone Overview

**GitHub milestone:** #10  
**Branch:** `feature/data-import-sources` (merged to main 2026-06-17)  
**Status:** In progress

---

## Description

Import pipeline infrastructure: per-source data files, startup seeder, import endpoint, ImportBatches provenance tracking, and database soft-reset. This is the foundation that the Blazor import UI (v3 milestone) builds on.

---

## Dependency map

```
#57 (dedup) → closed by design via #61
#61 (per-source files) → #62, #63, #68 depend on it
#63 (manifest) → #62 reads it; #64 references it
#62 (folder seeder) → prerequisite for #64 per-source overrides
#64 (conflict policy) → requires #63 for manifest field, #45 for per-run override, #58 for batch recording
#65 (preview) → requires #45 for the correct endpoint shape
#58 (ImportBatches) → unblocks #56, #59, #45 (batch row), #67, #68, #69
#45 (import endpoint) → unblocks remaining #64 requirements and #65 final shape
#55 (completeness flag) → independent; connects to #56 (no-value-known)
#56 (audit log) → requires #58 for batch actor; connects to #45, #55, #59
#59 (soft-reset by batch) → requires #58 and #56
#67 (conversations schema) → requires #58 for batch FK; unblocks #68, #69
#68 (curated format) → requires #67, #61
#69 (API conversations) → requires #67, #68
```

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #57 | Seed script: dedup inconsistent | Closed by design (#61 eliminates the concern) |
| 2 | #61 | Seed script: one file per source | Partially done — CI check still references old path |
| 3 | #63 | Import manifest | Partially done — unlisted-file sorting and auto-creation missing |
| 4 | #62 | Folder-based seeder | Partially done — `IncludeDefaultSources` and `ImportsPath` config keys missing |
| 5 | #64 | Conflict resolution policy | Partially done — naming mismatch (`overwrite` vs `newest-wins`), wrong default |
| 6 | #58 | ImportBatches schema | Not started |
| 7 | #45 | Import endpoint | Not started |
| 8 | #65 | Import endpoint: preview/dry-run | Partially done — wrong endpoint shape (needs #45) |
| 9 | #55 | Record completeness flag | Not started |
| 10 | #56 | Audit log | Not started |
| 11 | #59 | Admin: soft-reset by batch | Not started |
| 12 | #67 | Conversations schema | Not started |
| 13 | #68 | Curated JSON conversations | Not started |
| 14 | #69 | API conversations | Not started |

---

## Plan documents

- [#57 — Seed script dedup](57-seed-script-dedup-plan.md)
- [#61 — Seed script per source](61-seed-script-per-source-plan.md)
- [#63 — Import manifest](63-import-manifest-plan.md)
- [#62 — Folder-based seeder](62-folder-based-seeder-plan.md)
- [#64 — Conflict resolution policy](64-conflict-resolution-plan.md)
- [#58 — ImportBatches schema](58-import-batches-schema-plan.md)
- [#45 — Import endpoint](45-import-endpoint-plan.md)
- [#65 — Import endpoint: preview/dry-run](65-preview-dry-run-plan.md)
- [#55 — Record completeness flag](55-record-completeness-plan.md)
- [#56 — Audit log](56-audit-log-plan.md)
- [#59 — Admin soft-reset by batch](59-admin-soft-reset-plan.md)
- [#67 — Conversations schema](67-conversations-schema-plan.md)
- [#68 — Curated JSON conversations](68-curated-json-conversations-plan.md)
- [#69 — API conversations](69-api-conversations-plan.md)
