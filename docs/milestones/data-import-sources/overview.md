# Data Import & Sources — Milestone Overview

**GitHub milestone:** #10  
**Branch:** `feature/data-import-sources`  
**Status:** In progress — session resumed 2026-06-29; branch reset to current main (v1.7.2)

---

## Description

Import pipeline infrastructure: per-source data files, startup seeder, import endpoint, ImportBatches provenance tracking, and database soft-reset. This is the foundation that the Blazor import UI (v3 milestone) builds on.

---

## Dependency map

```
#71 (generic repository) → prerequisite for #78 and #58; unblocks all future repository implementations
#78 (transaction support) → requires #71; prerequisite for #45 and #58 (seeder needs atomic batch inserts)
#57 (dedup) → Problems 1–3 closed by design via #61; Problem 4 (ImportBatch) requires #58
#61 (per-source files) → #62, #63, #68 depend on it
#63 (manifest) → #62 reads it; #64 references it
#62 (folder seeder) → prerequisite for #64 per-source overrides
#64 (conflict policy) → requires #63 for manifest field, #45 for per-run override, #58 for batch recording
#65 (preview) → requires #45 for the correct endpoint shape
#58 (ImportBatches) → requires #71 and #78; unblocks #56, #59, #45 (batch row), #67, #68, #69
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

| #  | Issue | Title | Status |
|----|-------|-------|--------|
| 1  | #61 | Seed script: one file per source | Closed ✅ |
| 2  | #71 | Generic repository pattern | Closed ✅ |
| 3  | #78 | Repository: transaction and shared connection support | Closed ✅ |
| 4  | #58 | ImportBatches schema | Merged to main (PR #85, 2026-06-20) — issue not yet closed; closing checklist pending |
| 5  | #57 | Seed script: dedup inconsistent | Partially resolved — Problems 1–3 closed by #61; Problem 4 now unblocked (#58 done) |
| 6  | #63 | Import manifest | Partially done — unlisted-file sorting and auto-creation missing |
| 7  | #62 | Folder-based seeder | Partially done — `IncludeDefaultSources`, `ImportsPath` config keys and legacy warning missing; ImportBatch row now unblocked (#58 done) |
| 8  | #64 | Conflict resolution policy | Partially done — rename `overwrite` → `newest-wins`, change default, align config key; ImportBatch recording now unblocked (#58 done) |
| 9  | #45 | Import endpoint | Not started |
| 10 | #65 | Import endpoint: preview/dry-run | Partially done — existing startup preview is different feature; `?preview=true` on import needs #45 |
| 11 | #55 | Record completeness flag | Not started |
| 12 | #56 | Audit log | Not started |
| 13 | #59 | Admin: soft-reset by batch | Not started |
| 14 | #67 | Conversations schema | Not started |
| 15 | #68 | Curated JSON conversations | Not started |
| 16 | #69 | API conversations | Not started |

---

## PR merge plan

**Default assumption:** the full milestone is completed before merging to `main`.

### Issues already merged (previous partial merges)

| Issue | Merged | Notes |
|-------|--------|-------|
| #61 | ✅ | Self-contained — per-source file layout; no dependents called it at merge time |
| #71 | ✅ | Self-contained — generic repository infrastructure; nothing called it at merge time |
| #78 | ✅ | Self-contained — transaction support; nothing called it at merge time |
| #58 | ✅ | Merged via PR #85 (2026-06-20). Adds `ImportBatches` table, repository, and seeder wiring. Issue not yet closed — closing checklist pending. |

### Evaluation of remaining issues

All remaining issues are either partially done or not started. Evaluate each for early merge when complete — the default is to merge the full milestone together.

---

## Plan documents

- [#71 — Generic repository pattern](71-generic-repository-plan.md)
- [#78 — Repository: transaction and shared connection support](78-repository-transaction-plan.md)
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
