# Data Import & Sources — Milestone Overview

**GitHub milestone:** [#10](https://github.com/DutchJaFO/Quotinator/milestone/10)
**Branch:** `feature/data-import-sources`
**Status:** In progress — session 2026-06-30: #58 post-closure regression fixed, #57 fully resolved in code, #63 implemented, all three T1+T2 verified. #140 unblocked by #63's manifest groundwork but not started. Session 2026-07-01: #62's `ImportBatchType` accuracy conflict resolved (four-value type + migration 5) plus all three remaining config keys (`IncludeDefaultSources`, `ImportsPath`, legacy `DataPath` warning) — #62 is now **fully resolved in code**, T1+T2 verified for all of it; reseed/reset preservation split out as follow-up #141 under this milestone. Session 2026-07-02 (first pass): #141 scoped down to genuinely whole-table system tables — `AuditEntries` now always survives a full Reset, `SchemaVersion`'s clear-and-replay-on-Reset is optional via `preserveSchemaVersion`. Session 2026-07-02 (second pass, same day): the user rejected the hardcoded exclusion-list mechanism from the first pass and directed a rework to a `System_`-prefix naming convention — `Quotinator.Data`'s `Sql.Schema.GetUserTables` now identifies protected tables generically via an escaped `LIKE 'System\_%' ESCAPE '\'` match, with zero hardcoded table names, so a consuming project can protect a new table just by naming it `System_*`. `SchemaVersion`/`AuditEntries` renamed to `System_SchemaVersion`/`System_AuditEntries` at both the SQL and C# class level (`AuditEntry`→`SystemAuditEntry`, `IAuditReader`/`IAuditWriter`→`ISystemAuditReader`/`ISystemAuditWriter`, etc.). A real bug was found and fixed during the rework: `System_AuditEntries` surviving Reset collided with `SchemaVersion`'s default wipe-and-replay, since migration004's replay recreated a stray `AuditEntries` duplicate that migration006's rename then collided with — fixed via `IsKnownMigrationError` plus stray-table cleanup (later removed entirely, see below). Full details in [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md). **T1 verified live** by the user 2026-07-02 (Visual Studio, existing legacy-named database, and — critically — a live `POST /api/v1/admin/database/reset` run that exercised and confirmed the collision fix). **T2 (Docker) still outstanding.** Session 2026-07-02 (third pass, same day): user follow-up requests during #141 live-testing led to two more changes tracked as separate issues under this milestone — **#62 corrected** (`ImportBatchType.System` no longer classifies bundled quote content like `quotinator-curated.json`, kept in the enum reserved for future `System_`-table content imports) and **new issue #143 filed and code-complete** (fresh databases now take a one-step baseline path instead of replaying 6 migrations; `Quotinator.Data` owns a self-contained migration list for its own tables, always applied first, tracked in a new independent `System_ConsumerSchemaVersion` table separate from the consumer's own migrations). Session 2026-07-03: **#143 T1 live-testing exposed the exception-based recovery mechanism itself as a design flaw** — catching a `SqliteException` and matching its message to infer "this is fine" means a genuinely different failure could be silently misclassified. Reworked in two parts: (1) root-cause fix — `DropAndRebuildAsync` (Reset) no longer wipes or replays `Quotinator.Data`'s own migration history at all, since Data's migrations only ever concern `System_`-prefixed tables a Reset never drops, so the rename collision that `IsKnownMigrationError` used to paper over is now structurally impossible; (2) the separate, narrower consumer-migration-3 "duplicate column" anomaly (a real historical bug, #106) is now a hard failure by design — any migration failure backs up first and, on any exception (no type/message filtering), restores that backup and rethrows, requiring an explicit Reset to resolve. This also closed a real, previously-unnoticed gap: Reset took no backup at all before this change. `CLAUDE.md` was also corrected — SQLite has no `IF EXISTS`/`IF NOT EXISTS` for `ALTER TABLE ... RENAME TO`/`ADD COLUMN` at any version, contradicting its prior claim. **#143 is now fully verified — T1 and T2 both confirmed** (Docker: fresh-container baseline path identical to T1, Reset inside the container shows the same no-exception behavior, all smoke tests pass). See [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md) for full detail. Immediately after, **#141's previously-outstanding T2 was completed the same session**: fresh Docker container confirmed `System_AuditEntries` survives and grows across a reseed and two resets (6 → 13 → 20 → 34 rows), `System_ConsumerSchemaVersion` correctly respects `preserveSchemaVersion` in both directions, and `System_SchemaVersion` (Data's own) stays untouched throughout per #143's amendment — **#141 is now fully verified, T1 and T2 both confirmed.** Nothing in this milestone has shipped in a release since v1.4.1 — all completed work below is "pending release," including #62, #141, and #143 in full.

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
| [#141](https://github.com/DutchJaFO/Quotinator/issues/141) | Reseed/reset must preserve System-classified data | 🟡 Fully verified 2026-07-03 (amended to `System_`-prefix naming convention), T1+T2 confirmed — pending release | T1 ✅ T2 ✅ | [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md) |
| [#143](https://github.com/DutchJaFO/Quotinator/issues/143) | Fresh-database baseline schema + Data/Engine migration ownership split | 🟡 Fully verified 2026-07-03 (amended for exception-free migrations), full suite green (630/630), T1+T2 confirmed — pending release | T1 ✅ T2 ✅ | [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md) |
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

### #62 — Folder-based seeder (fully resolved in code)
**Shipped in:** (next release)
T1 ✅ verified 2026-07-01 — two rounds:
  - `ImportBatchType` accuracy fix: VS run against an existing non-empty dev database — migration 4→5 applied cleanly, `dummy1.json`/`Dummy2.JSON` in the imports folder correctly reclassified `UserSeed` on reseed.
  - Three config keys: `Quotinator__IncludeDefaultSources=false` correctly skips bundled sources on a fresh DB; `Quotinator__ImportsPath` correctly redirects the imports scan to a custom folder; `Quotinator__DataPath` still set correctly logs the deprecation warning. Default behavior (no overrides) confirmed unchanged.

T2 ✅ verified 2026-07-01 — same two rounds, in Docker:
  - `docker build`/`docker run`; fresh container built schema straight to v5; `container-dummy.json` present in a pre-mounted volume confirmed `UserSeed` via direct DB query.
  - All three config keys confirmed via `-e` env vars against fresh containers, identical behavior to T1.

No T3 requirements for any part of this issue.

Full verification table: [62-folder-based-seeder-plan.md](62-folder-based-seeder-plan.md)

---

### #141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)
**Shipped in:** (next release)
T1 and T2 required — this changes the actual table-wipe logic behind `Reset` (`DropAndRebuildAsync`, `Sql.Schema.GetUserTables`).

**First pass (2026-07-02, morning)** — scoped down from the original issue text to genuinely whole-table system tables (`AuditEntries`, `SchemaVersion`) via a hardcoded exclusion list. T1 verified against a real running app (`AuditEntries` survived resets, `SchemaVersion` behaviour confirmed unchanged by default and preserved when `preserveSchemaVersion=true`). T2 blocked — no Docker daemon in that session.

**Second pass (2026-07-02, same day)** — the user rejected the hardcoded exclusion-list mechanism: `Quotinator.Data` should never need to know specific system table names. Reworked to a `System_`-prefix naming convention (`System_SchemaVersion`, `System_AuditEntries`) with a generic, escaped `LIKE 'System\_%' ESCAPE '\'` match in `Sql.Schema.GetUserTables` — any consuming project can protect a new table with zero changes to `Quotinator.Data`. C# classes renamed to match (`SystemAuditEntry`, `ISystemAuditReader`/`ISystemAuditWriter`, etc.). `SchemaVersion`'s rename is a conditional bootstrap check (fresh databases never see the legacy name); `AuditEntries`'s rename is a new migration006 (migration004 stays frozen). A real collision bug was found and fixed: `System_AuditEntries` surviving Reset (protected by the new pattern) collided with `SchemaVersion`'s default wipe-and-replay, since migration004's replay recreated a stray `AuditEntries` duplicate that migration006's rename then collided with — fixed via `IsKnownMigrationError` plus stray-table cleanup (later replaced entirely — see below). Full test suite green (617 tests, 0 warnings) after the rework. **T1 verified live 2026-07-02** — the user ran `POST /api/v1/admin/database/reset` (both default and `preserveSchemaVersion=true`) against a real app with an existing legacy-named database in Visual Studio, which exercised the exact migration004/migration006 collision the fix targets; the recovery path fired correctly, reset completed successfully both times, DB inspection confirmed the correct table/index names and no legacy tables remaining. A follow-up fix landed from this run: the recovery log message was misleadingly worded for this now-routine (not rare) case — split into an accurate, non-alarming message.

**Note:** the `IsKnownMigrationError` collision-avoidance mechanism above was superseded on 2026-07-03 under #143's exception-free-migrations amendment, which fixes the same underlying problem at the root instead — Reset no longer wipes or replays `System_SchemaVersion` at all, so the collision is now structurally impossible rather than caught-and-recovered. #141's own requirements (system-table survival, `preserveSchemaVersion` behavior) are unaffected by that change.

**T2 verified 2026-07-03** — fresh Docker container (no pre-existing volume): baseline path created `System_SchemaVersion`/`System_AuditEntries` directly; `System_AuditEntries` survived and grew monotonically across a reseed and two resets (6 → 13 → 20 → 34 rows); `System_ConsumerSchemaVersion` confirmed unchanged across a `preserveSchemaVersion=true` reset and then updated on the very next default reset, verified directly against the container's DB file via `Quotinator.Tools.DbInspector`; `System_SchemaVersion` (Data's own) stayed untouched throughout, per the amendment above. No exceptions at any point. **#141 is now fully verified — T1 and T2 both confirmed.**

Full detail: [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md).

Full verification table: [141-system-table-preservation-plan.md](141-system-table-preservation-plan.md)

---

### #143 — Fresh-database baseline schema + Data/Engine migration ownership split
**Shipped in:** (next release)
T1 and T2 required — this changes the actual migration/table-creation logic behind `InitialiseAsync`/`Reset`.

**First pass (2026-07-02–07-03)** — `Quotinator.Data` gained a self-contained `DataOwnedMigrations` list for its own tables, applied first and tracked independently in a new `System_ConsumerSchemaVersion` table separate from the consumer's own migrations. A fresh (zero-table) database now takes a one-step baseline path instead of replaying 6 historical migrations. T1 verified live 2026-07-03 (fresh-DB baseline path; existing-DB Reset transition).

**Amendment (2026-07-03, same day)** — live T1 testing of the first pass exposed `Exception thrown: 'Microsoft.Data.Sqlite.SqliteException'` firing on every Reset, caught and interpreted via message-matching (`IsKnownMigrationError`). Judged unacceptable: a genuinely different failure with the same message could be silently misclassified and swallowed. Reworked with two changes: (1) root-cause fix — traced the actual trigger to `DropAndRebuildAsync` (Reset) unconditionally wiping `System_SchemaVersion` even though Data's migrations only ever concern `System_`-prefixed tables a Reset never drops; Reset now never wipes or replays Data's own migration history at all, making the rename collision structurally impossible rather than caught-and-recovered; (2) the separate, narrower consumer-migration-3 "duplicate column" anomaly (real historical bug #106, possibly still affecting v1.5.x–v1.6.1 installations) is now a hard failure by explicit decision — any migration failure backs up first and, on any exception (no type/message filtering), restores that backup and rethrows, requiring an explicit Reset to resolve. This also closed a real, previously-unnoticed gap: Reset took no backup at all before this change. `CLAUDE.md`'s migration policy was corrected in the same pass — SQLite has no `IF EXISTS`/`IF NOT EXISTS` for `ALTER TABLE ... RENAME TO`/`ADD COLUMN` at any version (verified against sqlite.org), contradicting its prior claim.

**T1 re-verified live 2026-07-03** — `dotnet run` against the same dev database used for the first pass; `POST /api/v1/admin/database/reset` log showed a mandatory pre-reset backup, no `Data` migration phase line at all, and no `Exception thrown:` anywhere; direct DB inspection via `Quotinator.Tools.DbInspector` confirmed `System_SchemaVersion` was completely untouched by the reset while `System_ConsumerSchemaVersion` was cleanly cleared and replayed.

**T2 verified 2026-07-03** — `docker build` + fresh container (no pre-existing volume): baseline path fired identically to T1, all smoke tests passed (`/health`, `/version` → `schemaVersion: 4`, `/quotes/random`, `/quotes/search`), and a `POST /admin/database/reset` inside the container showed byte-for-byte identical log behavior to the local T1 run.

Full detail: [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md).

Full verification table: [143-migration-ownership-baseline-plan.md](143-migration-ownership-baseline-plan.md)

---

## Dependency map

```
#71 (generic repository) → prerequisite for #78 and #58; unblocks all future repository implementations
#78 (transaction support) → requires #71; prerequisite for #45 and #58 (seeder needs atomic batch inserts)
#57 (dedup) → Problems 1–3 closed by design via #61; Problem 4 (ImportBatch) required #58 — done
#61 (per-source files) → #62, #63, #68 depend on it
#63 (manifest) → #62 reads it; #64 references it; #140 needs its downloadUrl/github groundwork — done
#62 (folder seeder) → prerequisite for #64 per-source overrides; ImportBatchType accuracy fix (2026-07-01) unblocks #141
#141 (system table preservation on Reset) → requires #62's ImportBatchType fix (done); amended to a System_-prefix naming convention (System_AuditEntries/System_SchemaVersion) — fully verified, T1+T2 confirmed
#143 (migration ownership split + baseline schema) → requires #141's System_-prefix convention (done); fully verified, T1+T2 confirmed
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
| 7  | #62 | Folder-based seeder | Fully resolved in code 2026-07-01 — `ImportBatchType` fix (four-value type + migration 5) plus all three config keys (`IncludeDefaultSources`, `ImportsPath`, legacy `DataPath` warning), T1+T2 verified, pending release — see Scope changes in plan doc |
| 7a | #141 | Reseed/reset must preserve System-classified data | Amended 2026-07-02 to a `System_`-prefix naming convention after the user rejected the hardcoded exclusion-list mechanism; fully verified 2026-07-03, full test suite green, **T1+T2 both confirmed**, pending release |
| 7b | #140 | Auto-update bundled sources from manifest URL | Not started — schema/manifest/`SeedFile` groundwork (`downloadUrl`, `github` object) now done by #63; remaining scope is the HTTP GET + temp-file-substitution mechanism and the `Quotinator__AutoUpdateSources` config key |
| 7c | #143 | Fresh-database baseline schema + Data/Engine migration ownership split | Fully verified 2026-07-03 — code complete, amended for exception-free migrations after live T1 testing exposed the original design's exception-based recovery as a flaw; full test suite green (630/630), **T1+T2 both confirmed**, pending release |
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
- [#141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)](141-system-table-preservation-plan.md)
- [#143 — Fresh-database baseline schema + Data/Engine migration ownership split](143-migration-ownership-baseline-plan.md)
