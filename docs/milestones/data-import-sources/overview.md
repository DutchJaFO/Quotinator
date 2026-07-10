# Data Import & Sources — Milestone Overview

**GitHub milestone:** [#10](https://github.com/DutchJaFO/Quotinator/milestone/10)
**Branch:** `feature/data-import-sources`
**Status:** In progress

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
| [#61](https://github.com/DutchJaFO/Quotinator/issues/61) | Seed script: one file per source | Released | — (pre-dates tier system) | [61-seed-script-per-source-plan.md](61-seed-script-per-source-plan.md) |
| [#71](https://github.com/DutchJaFO/Quotinator/issues/71) | Generic repository pattern | Released | — (pre-dates tier system) | [71-generic-repository-plan.md](71-generic-repository-plan.md) |
| [#78](https://github.com/DutchJaFO/Quotinator/issues/78) | Repository: transaction and shared connection support | Released | — (pre-dates tier system) | [78-repository-transaction-plan.md](78-repository-transaction-plan.md) |
| [#58](https://github.com/DutchJaFO/Quotinator/issues/58) | ImportBatches schema | Waiting for release | T1 ✅ T2 ✅ | [58-import-batches-schema-plan.md](58-import-batches-schema-plan.md) |
| [#57](https://github.com/DutchJaFO/Quotinator/issues/57) | Seed script: dedup inconsistent | Waiting for release | None required | [57-seed-script-dedup-plan.md](57-seed-script-dedup-plan.md) |
| [#63](https://github.com/DutchJaFO/Quotinator/issues/63) | Import manifest | Waiting for release | T1 ✅ T2 ✅ | [63-import-manifest-plan.md](63-import-manifest-plan.md) |
| [#62](https://github.com/DutchJaFO/Quotinator/issues/62) | Folder-based seeder | Waiting for release | T1 ✅ T2 ✅ | [62-folder-based-seeder-plan.md](62-folder-based-seeder-plan.md) |
| [#141](https://github.com/DutchJaFO/Quotinator/issues/141) | Reseed/reset must preserve System-classified data | Waiting for release | T1 ✅ T2 ✅ | [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md) |
| [#143](https://github.com/DutchJaFO/Quotinator/issues/143) | Fresh-database baseline schema + Data/Engine migration ownership split | Waiting for release | T1 ✅ T2 ✅ | [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md) |
| [#140](https://github.com/DutchJaFO/Quotinator/issues/140) | Auto-update bundled sources from manifest URL | Waiting for release | T1 ✅ T2 ✅ T3 ⬜ | [140-auto-update-sources-plan.md](140-auto-update-sources-plan.md) |
| [#144](https://github.com/DutchJaFO/Quotinator/issues/144) | Converter plugins: generic naming, internal-only slots, configuration options | Waiting for release | T1 ✅ T2 ✅ | [144-converter-plugin-review-plan.md](144-converter-plugin-review-plan.md) |
| [#64](https://github.com/DutchJaFO/Quotinator/issues/64) | Conflict resolution policy | Waiting for release | T1 ✅ T2 ✅ | [64-conflict-resolution-plan.md](64-conflict-resolution-plan.md) |
| [#45](https://github.com/DutchJaFO/Quotinator/issues/45) | Import endpoint | Waiting for release | T1 ✅ T2 ✅ | [45-import-endpoint-plan.md](45-import-endpoint-plan.md) |
| [#65](https://github.com/DutchJaFO/Quotinator/issues/65) | Import endpoint: preview/dry-run | Waiting for release | T1 ✅ T2 ✅ | [65-preview-dry-run-plan.md](65-preview-dry-run-plan.md) |
| [#55](https://github.com/DutchJaFO/Quotinator/issues/55) | Record completeness flag | Waiting for release | T1 ✅ T2 ✅ | [55-record-completeness-plan.md](55-record-completeness-plan.md) |
| [#56](https://github.com/DutchJaFO/Quotinator/issues/56) | Audit log (System_ChangeLog) | Waiting for release | T1 ✅ T2 ✅ | [56-audit-log-plan.md](56-audit-log-plan.md) |
| [#59](https://github.com/DutchJaFO/Quotinator/issues/59) | Admin: undo an applied import batch | Waiting for release | T1 ✅ T2 ✅ | [59-admin-soft-reset-plan.md](59-admin-soft-reset-plan.md) |
| [#67](https://github.com/DutchJaFO/Quotinator/issues/67) | Conversations schema | Planning | T1 ⬜ T2 ⬜ | [67-conversations-schema-plan.md](67-conversations-schema-plan.md) |
| [#68](https://github.com/DutchJaFO/Quotinator/issues/68) | Curated JSON conversations | Planning | T1 ⬜ T2 ⬜ | [68-curated-json-conversations-plan.md](68-curated-json-conversations-plan.md) |
| [#69](https://github.com/DutchJaFO/Quotinator/issues/69) | API conversations | Planning | T1 ⬜ T2 ⬜ | [69-api-conversations-plan.md](69-api-conversations-plan.md) |
| [#149](https://github.com/DutchJaFO/Quotinator/issues/149) | Import endpoint: manual conflict-review workflow | Waiting for release | T1 ✅ T2 ✅ | [149-manual-conflict-review-plan.md](149-manual-conflict-review-plan.md) |
| [#152](https://github.com/DutchJaFO/Quotinator/issues/152) | Review endpoint grouping: split Admin / Quote / Import | Waiting for release | T1 ✅ T2 ✅ | [152-endpoint-grouping-plan.md](152-endpoint-grouping-plan.md) |
| [#153](https://github.com/DutchJaFO/Quotinator/issues/153) | Declarative conflict-resolution file for recurring third-party source conflicts | Planning | Not yet assessed | No plan doc yet — deferred out of #149 |
| [#154](https://github.com/DutchJaFO/Quotinator/issues/154) | Unify import, preview, and seeding on one staging engine | Waiting for release | T1 ✅ T2 ✅ | [154-import-staging-plan.md](154-import-staging-plan.md) |
| [#155](https://github.com/DutchJaFO/Quotinator/issues/155) | Migration review: verify full incremental path from last-shipped v1.7.2 schema | Planning | Not yet assessed | No plan doc yet — deferred to just before milestone close |

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
#55 (completeness flag) → requires #64 (merge engine must never reset IsComplete/NoValueKnown on an update); connects to #56 (no-value-known)
#56 (audit log) → requires #58 for batch actor; connects to #45, #55, #59
#59 (soft-reset by batch) → redefined to depend on #154 (undoes an already-applied batch using #154's System_ImportActions log instead of the originally-planned FK-sharing-cascade approach); still requires #58 and #56
#67 (conversations schema) → requires #58 for batch FK; unblocks #68, #69
#68 (curated format) → requires #67, #61, #58 and #154 (conversations/stageDirections/soundCues are seeded through the same shared writer + System_ImportActions staging path as Quotes — plan doc scope correction)
#69 (API conversations) → requires #67, #68
#140 (auto-update sources) → requires #58 fix + #63; unblocks #144
#144 (converter plugin review) → requires #140 (done)
#149 (manual conflict-review workflow) → deferred out of #45; requires #56 (audit log) — done; unblocks #153
#152 (endpoint grouping review) → depended on #149's /api/v1/import group/tag existing first; moved the remaining /quotes/import(/preview) endpoints into that same group — done
#153 (declarative conflict-resolution file) → deferred out of #149; requires #149 (decide/undo/apply machinery and FieldMergeResolver to build on); also builds naturally on #154's staging model once it ships
#154 (unify import/preview/seeding on one staging engine) → emerged while planning #59; requires #149 (IConflictResolutionCoordinator, System_ImportConflicts as the template) and #56 (audit log); unblocks #59 (redefined) and #153 (natural fit, not required)
#155 (migration review before milestone close) → independent of the others; should be done last, immediately before this milestone closes
```

---

## Order of operations

| #  | Issue | Title | Status |
|----|-------|-------|--------|
| 1  | #61 | Seed script: one file per source | Released |
| 2  | #71 | Generic repository pattern | Released |
| 3  | #78 | Repository: transaction and shared connection support | Released |
| 4  | #58 | ImportBatches schema | Waiting for release |
| 5  | #57 | Seed script: dedup inconsistent | Waiting for release |
| 6  | #63 | Import manifest | Waiting for release |
| 7  | #62 | Folder-based seeder | Waiting for release |
| 8  | #141 | Reseed/reset must preserve System-classified data | Waiting for release |
| 9  | #140 | Auto-update bundled sources from manifest URL | Waiting for release |
| 10 | #143 | Fresh-database baseline schema + Data/Engine migration ownership split | Waiting for release |
| 11 | #64 | Conflict resolution policy | Waiting for release |
| 12 | #45 | Import endpoint | Waiting for release |
| 13 | #65 | Import endpoint: preview/dry-run | Waiting for release |
| 14 | #55 | Record completeness flag | Waiting for release |
| 15 | #56 | Audit log (System_ChangeLog) | Waiting for release |
| 16 | #152 | Review endpoint grouping: split Admin / Quote / Import | Waiting for release |
| 17 | #149 | Import endpoint: manual conflict-review workflow | Waiting for release |
| 18 | #154 | Unify import, preview, and seeding on one staging engine | Waiting for release |
| 19 | #59 | Admin: undo an applied import batch | Waiting for release |
| 20 | #67 | Conversations schema | Planning |
| 21 | #68 | Curated JSON conversations | Planning |
| 22 | #69 | API conversations | Planning |
| 23 | #144 | Converter plugins: generic naming, internal-only slots, configuration options | Waiting for release |
| 24 | #153 | Declarative conflict-resolution file for recurring third-party source conflicts | Planning |
| 25 | #155 | Migration review: verify full incremental path from last-shipped v1.7.2 schema | Planning |

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

| Issue | Ready for early merge? | Notes |
|-------|------------------------|-------|
| #45, #65 | Not evaluated for early merge — held for the full milestone | Fully done (T1 ✅ T2 ✅), but their own output is only reachable through the write path they introduce (`POST /api/v1/import`, moved from `/api/v1/quotes/import` by #152) — nothing else in the milestone calls them, and no existing behaviour depends on them being present, so there is no forcing reason to break from the default "merge the full milestone together" assumption. Revisit only if a later issue in this milestone (e.g. #59, #56) would otherwise sit blocked waiting on a merge. |
| #154 | Not evaluated for early merge — held for the full milestone | Fully done (T1 ✅ T2 ✅) — see [154-import-staging-plan.md](154-import-staging-plan.md). Same reasoning as #45/#65: its output is only reachable through the same write path. |

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
- [#149 — Manual conflict-review workflow](149-manual-conflict-review-plan.md)
- [#152 — Endpoint grouping review](152-endpoint-grouping-plan.md)
- [#154 — Unify import, preview, and seeding on one staging engine](154-import-staging-plan.md)
- [#59 — Admin: undo an applied import batch](59-admin-soft-reset-plan.md)
- [#67 — Conversations schema](67-conversations-schema-plan.md)
- [#68 — Curated JSON conversations](68-curated-json-conversations-plan.md)
- [#69 — API conversations](69-api-conversations-plan.md)
- [#140 — Auto-update bundled sources from manifest URL](140-auto-update-sources-plan.md)
- [#144 — Converter plugins: generic naming, internal-only slots, configuration options](144-converter-plugin-review-plan.md)
- [#141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)](141-system-table-preservation-plan.md)
- [#143 — Fresh-database baseline schema + Data/Engine migration ownership split](143-migration-ownership-baseline-plan.md)
