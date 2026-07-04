# Data Import & Sources — Milestone Overview

**GitHub milestone:** [#10](https://github.com/DutchJaFO/Quotinator/milestone/10)
**Branch:** `feature/data-import-sources`
**Status:** In progress, as of 2026-07-04 — see Issue List below for per-issue status and plan doc links.

---

## Description

Import pipeline infrastructure: per-source data files, startup seeder, import endpoint, ImportBatches provenance tracking, and database soft-reset. This is the foundation that the Blazor import UI (v3 milestone) builds on.

---

## Verification tier definitions

| Tier | Environment | What it catches |
|------|-------------|-----------------|
| **T1 — VS/local** | Visual Studio on Windows | Razor runtime errors (not caught by `dotnet build`), Blazor circuit startup, UI rendering, manual API interaction, `Program.cs` startup behaviour |
| **T2 — Docker** | `docker build` + `docker run` locally | Publish output completeness, container startup, Kestrel port binding, `data/sources/` presence in image |
| **T3 — HA add-on** | Live Home Assistant supervisor | Ingress routing, `X-Ingress-Path` middleware, supervisor volume mount at `/data`, DataProtection keys, SSL cert loading, cookie behaviour after container restart, supervisor log output |

Full tier definitions and classification rules: [`docs/release-verification.md`](../release-verification.md)

**An issue can only be closed after:**
1. It is included in a published release (beta or final as appropriate)
2. Every required tier for that issue is confirmed green
3. Explicit user confirmation is given to `gh issue close`

---

## Issue List

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#61](https://github.com/DutchJaFO/Quotinator/issues/61) | Seed script: one file per source | ✅ Closed | — (pre-dates tier system) | [61-seed-script-per-source-plan.md](61-seed-script-per-source-plan.md) |
| [#71](https://github.com/DutchJaFO/Quotinator/issues/71) | Generic repository pattern | ✅ Closed | — (pre-dates tier system) | [71-generic-repository-plan.md](71-generic-repository-plan.md) |
| [#78](https://github.com/DutchJaFO/Quotinator/issues/78) | Repository: transaction and shared connection support | ✅ Closed | — (pre-dates tier system) | [78-repository-transaction-plan.md](78-repository-transaction-plan.md) |
| [#58](https://github.com/DutchJaFO/Quotinator/issues/58) | ImportBatches schema | ✅ Closed — post-closure regression fixed, pending release | T1 ✅ T2 ✅ (regression fix, 2026-06-30) | [58-import-batches-schema-plan.md](58-import-batches-schema-plan.md) |
| [#57](https://github.com/DutchJaFO/Quotinator/issues/57) | Seed script: dedup inconsistent | 🟡 Code complete — pending release | None required | [57-seed-script-dedup-plan.md](57-seed-script-dedup-plan.md) |
| [#63](https://github.com/DutchJaFO/Quotinator/issues/63) | Import manifest | 🟡 Code complete — pending release | T1 ✅ T2 ✅ (2026-06-30) | [63-import-manifest-plan.md](63-import-manifest-plan.md) |
| [#62](https://github.com/DutchJaFO/Quotinator/issues/62) | Folder-based seeder | 🟡 Code complete — pending release | T1 ✅ T2 ✅ (2026-07-01) | [62-folder-based-seeder-plan.md](62-folder-based-seeder-plan.md) |
| [#141](https://github.com/DutchJaFO/Quotinator/issues/141) | Reseed/reset must preserve System-classified data | 🟡 Fully verified — pending release | T1 ✅ T2 ✅ | [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md) |
| [#143](https://github.com/DutchJaFO/Quotinator/issues/143) | Fresh-database baseline schema + Data/Engine migration ownership split | 🟡 Fully verified — pending release | T1 ✅ T2 ✅ | [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md) |
| [#140](https://github.com/DutchJaFO/Quotinator/issues/140) | Auto-update bundled sources from manifest URL | 🟡 Re-planned — not yet implemented | Not yet assessed | [140-auto-update-sources-plan.md](140-auto-update-sources-plan.md) |
| [#64](https://github.com/DutchJaFO/Quotinator/issues/64) | Conflict resolution policy | 🟡 Partially done | Not yet assessed | [64-conflict-resolution-plan.md](64-conflict-resolution-plan.md) |
| [#45](https://github.com/DutchJaFO/Quotinator/issues/45) | Import endpoint | ⬜ Not started | Not yet assessed | [45-import-endpoint-plan.md](45-import-endpoint-plan.md) |
| [#65](https://github.com/DutchJaFO/Quotinator/issues/65) | Import endpoint: preview/dry-run | 🟡 Partially done | Not yet assessed | [65-preview-dry-run-plan.md](65-preview-dry-run-plan.md) |
| [#55](https://github.com/DutchJaFO/Quotinator/issues/55) | Record completeness flag | ⬜ Not started | Not yet assessed | [55-record-completeness-plan.md](55-record-completeness-plan.md) |
| [#56](https://github.com/DutchJaFO/Quotinator/issues/56) | Audit log | ⬜ Not started | Not yet assessed | [56-audit-log-plan.md](56-audit-log-plan.md) |
| [#59](https://github.com/DutchJaFO/Quotinator/issues/59) | Admin: soft-reset by batch | ⬜ Not started | Not yet assessed | [59-admin-soft-reset-plan.md](59-admin-soft-reset-plan.md) |
| [#67](https://github.com/DutchJaFO/Quotinator/issues/67) | Conversations schema | ⬜ Not started | Not yet assessed | [67-conversations-schema-plan.md](67-conversations-schema-plan.md) |
| [#68](https://github.com/DutchJaFO/Quotinator/issues/68) | Curated JSON conversations | ⬜ Not started | Not yet assessed | [68-curated-json-conversations-plan.md](68-curated-json-conversations-plan.md) |
| [#69](https://github.com/DutchJaFO/Quotinator/issues/69) | API conversations | ⬜ Not started | Not yet assessed | [69-api-conversations-plan.md](69-api-conversations-plan.md) |

---

## Dependency map

```
#71 (generic repository) → prerequisite for #78 and #58; unblocks all future repository implementations
#78 (transaction support) → requires #71; prerequisite for #45 and #58 (seeder needs atomic batch inserts)
#57 (dedup) → Problems 1–3 closed by design via #61; Problem 4 (ImportBatch) required #58 — done
#61 (per-source files) → #62, #63, #68 depend on it
#63 (manifest) → #62 reads it; #64 references it; #140 needs its downloadUrl/github groundwork — done
#62 (folder seeder) → prerequisite for #64 per-source overrides; ImportBatchType accuracy fix unblocks #141
#141 (system table preservation on Reset) → requires #62's ImportBatchType fix
#143 (migration ownership split + baseline schema) → requires #141's System_-prefix convention
#64 (conflict policy) → requires #63 for manifest field, #45 for per-run override, #58 for batch recording
#65 (preview) → requires #45 for the correct endpoint shape
#58 (ImportBatches) → requires #71 and #78; unblocks #56, #57 (Problem 4 — done), #59, #45 (batch row), #64, #67, #68, #69
#45 (import endpoint) → unblocks remaining #64 requirements and #65 final shape
#55 (completeness flag) → independent; connects to #56 (no-value-known)
#56 (audit log) → requires #58 for batch actor; connects to #45, #55, #59
#59 (soft-reset by batch) → requires #58 and #56
#67 (conversations schema) → requires #58 for batch FK; unblocks #68, #69
#68 (curated format) → requires #67, #61
#69 (API conversations) → requires #67, #68
#140 (auto-update sources) → requires #58 fix + #63
```

---

## Order of operations

| #  | Issue | Title | Status |
|----|-------|-------|--------|
| 1  | #61 | Seed script: one file per source | Closed ✅ |
| 2  | #71 | Generic repository pattern | Closed ✅ |
| 3  | #78 | Repository: transaction and shared connection support | Closed ✅ |
| 4  | #58 | ImportBatches schema | Closed ✅ — pending release |
| 5  | #57 | Seed script: dedup inconsistent | Code complete — pending release |
| 6  | #63 | Import manifest | Code complete — pending release |
| 7  | #62 | Folder-based seeder | Code complete — pending release |
| 8  | #141 | Reseed/reset must preserve System-classified data | Fully verified — pending release |
| 9  | #140 | Auto-update bundled sources from manifest URL | Re-planned — not yet implemented |
| 10 | #143 | Fresh-database baseline schema + Data/Engine migration ownership split | Fully verified — pending release |
| 11 | #64 | Conflict resolution policy | Partially done |
| 12 | #45 | Import endpoint | Not started |
| 13 | #65 | Import endpoint: preview/dry-run | Partially done |
| 14 | #55 | Record completeness flag | Not started |
| 15 | #56 | Audit log | Not started |
| 16 | #59 | Admin: soft-reset by batch | Not started |
| 17 | #67 | Conversations schema | Not started |
| 18 | #68 | Curated JSON conversations | Not started |
| 19 | #69 | API conversations | Not started |

---

## PR merge plan

**Default assumption:** the full milestone is completed before merging to `main`.

### Issues already merged (previous partial merges)

| Issue | Merged | Notes |
|-------|--------|-------|
| #61 | ✅ | Self-contained — per-source file layout; no dependents called it at merge time |
| #71 | ✅ | Self-contained — generic repository infrastructure; nothing called it at merge time |
| #78 | ✅ | Self-contained — transaction support; nothing called it at merge time |
| #58 | ✅ | Merged via PR #85 (2026-06-20). Adds `ImportBatches` table, repository, and seeder wiring. Issue closed; a post-closure `Type`/`Url` regression was found and fixed 2026-06-30 (T1+T2 verified) but has not shipped in a release yet — see [58-import-batches-schema-plan.md](58-import-batches-schema-plan.md). |

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
- [#140 — Auto-update bundled sources from manifest URL](140-auto-update-sources-plan.md)
- [#141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)](141-system-table-preservation-plan.md)
- [#143 — Fresh-database baseline schema + Data/Engine migration ownership split](143-migration-ownership-baseline-plan.md)
