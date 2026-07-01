# Data Import & Sources — Milestone Overview

**GitHub milestone:** [#10](https://github.com/DutchJaFO/Quotinator/milestone/10)
**Branch:** `feature/data-import-sources`
**Status:** In progress — session 2026-06-30: #58 post-closure regression fixed, #57 fully resolved in code, #63 implemented, all three T1+T2 verified. #140 unblocked by #63's manifest groundwork but not started. Session 2026-07-01: #62's `ImportBatchType` accuracy conflict resolved (four-value type + migration 5); reseed/reset preservation split out as follow-up #141 under this milestone. Nothing in this milestone has shipped in a release since v1.4.1 — all completed work below is "pending release."

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
| [#62](https://github.com/DutchJaFO/Quotinator/issues/62) | Folder-based seeder | 🟡 Partially done — `ImportBatchType` accuracy fixed, 3 config keys remain | Not yet assessed | [62-folder-based-seeder-plan.md](62-folder-based-seeder-plan.md) |
| [#141](https://github.com/DutchJaFO/Quotinator/issues/141) | Reseed/reset must preserve System-classified data | ⬜ Not started | Not yet assessed | (no plan doc yet — filed 2026-07-01) |
| [#140](https://github.com/DutchJaFO/Quotinator/issues/140) | Auto-update bundled sources from manifest URL | ⬜ Not started | Not yet assessed | [140-auto-update-sources-plan.md](140-auto-update-sources-plan.md) |
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

## Pending verification before close

These issues are code-complete but cannot be closed until a release ships and any remaining tiers are confirmed.

### #58 — ImportBatches schema (post-closure regression fix)
**Shipped in:** (next release)
T1 ✅ verified 2026-06-30 (VS run; SQL Object Explorer confirmed curated→`System`/`NULL`, vilaboim/NikhilNamal17→`Seed`/GitHub URL)
T2 ✅ verified 2026-06-30 (`docker build` succeeded; container log showed identical seed counts to T1; `/api/v1/health`, `/api/v1/version`, `/api/v1/quotes/random` smoke tests passed)
No T3 requirements for this fix.

Full verification table: [58-import-batches-schema-plan.md](58-import-batches-schema-plan.md)

---

### #57 — Seed script: dedup inconsistent
**Shipped in:** (next release)
No live tier applies — Problem 4's fix lives entirely in `QuotinatorDatabaseInitializer` (engine layer); fully covered by unit tests. See the plan doc's Tiers line for the full reasoning.

Full verification table: [57-seed-script-dedup-plan.md](57-seed-script-dedup-plan.md)

---

### #63 — Import manifest
**Shipped in:** (next release)
T1 ✅ verified 2026-06-30 (VS run; `imports\manifest.json` auto-created with `[Database - Init]` warning logged via Serilog; bundled-dir `github`-kind entries seed without regression; empty-file crash found and fixed during this verification pass)
T2 ✅ verified 2026-06-30 (`docker build`/`docker run`; smoke tests passed; `docker exec` into the running container confirmed auto-create works identically inside `/app/data/imports`)
No T3 requirements for this issue.

Full verification table: [63-import-manifest-plan.md](63-import-manifest-plan.md)

---

## Dependency map

```
#71 (generic repository) → prerequisite for #78 and #58; unblocks all future repository implementations
#78 (transaction support) → requires #71; prerequisite for #45 and #58 (seeder needs atomic batch inserts)
#57 (dedup) → Problems 1–3 closed by design via #61; Problem 4 (ImportBatch) required #58 — done
#61 (per-source files) → #62, #63, #68 depend on it
#63 (manifest) → #62 reads it; #64 references it; #140 needs its downloadUrl/github groundwork — done
#62 (folder seeder) → prerequisite for #64 per-source overrides; ImportBatchType accuracy fix (2026-07-01) unblocks #141
#141 (reseed/reset preservation) → requires #62's ImportBatchType fix (done); needs a table-classification mechanism, not yet designed
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
#140 (auto-update sources) → requires #58 fix + #63 (both done); remaining scope is HTTP GET + temp-file substitution
```

---

## Order of operations

| #  | Issue | Title | Status |
|----|-------|-------|--------|
| 1  | #61 | Seed script: one file per source | Closed ✅ |
| 2  | #71 | Generic repository pattern | Closed ✅ |
| 3  | #78 | Repository: transaction and shared connection support | Closed ✅ |
| 4  | #58 | ImportBatches schema | Closed ✅ — post-closure `Type`/`Url` regression fixed 2026-06-30, T1+T2 verified, pending release |
| 5  | #57 | Seed script: dedup inconsistent | All problems resolved in code 2026-06-30 (Problem 4 done, unit-tested, no tier required), pending release |
| 6  | #63 | Import manifest | Resolved in code 2026-06-30 — T1+T2 verified, pending release. Added `github`/`downloadUrl` manifest source kinds (see Manifest data fix note in plan doc) |
| 7  | #62 | Folder-based seeder | `ImportBatchType` accuracy fixed 2026-07-01 (four-value type + migration 5) — see Scope changes in plan doc. Still missing: `IncludeDefaultSources`, `ImportsPath` config keys, legacy `DataPath` warning |
| 7a | #141 | Reseed/reset must preserve System-classified data | Not started — filed 2026-07-01 as a follow-up to #62; needs a table-level classification mechanism, open question on `SchemaVersion` |
| 7b | #140 | Auto-update bundled sources from manifest URL | Not started — schema/manifest/`SeedFile` groundwork (`downloadUrl`, `github` object) now done by #63; remaining scope is the HTTP GET + temp-file-substitution mechanism and the `Quotinator__AutoUpdateSources` config key |
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
- [#141 — Reseed/reset must preserve System-classified data](https://github.com/DutchJaFO/Quotinator/issues/141) (no plan doc yet)
