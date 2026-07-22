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
| [#79](https://github.com/DutchJaFO/Quotinator/issues/79) | Fix Highlights sections in CHANGELOG.md for 1.3.0 and 1.4.0 | Released | None required | No plan doc |
| [#58](https://github.com/DutchJaFO/Quotinator/issues/58) | ImportBatches schema | Released | T1 ✅ T2 ✅ | [58-import-batches-schema-plan.md](58-import-batches-schema-plan.md) |
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
| [#67](https://github.com/DutchJaFO/Quotinator/issues/67) | Conversations schema | Waiting for release | T1 ✅ T2 ✅ | [67-conversations-schema-plan.md](67-conversations-schema-plan.md) |
| [#68](https://github.com/DutchJaFO/Quotinator/issues/68) | Curated JSON conversations | Waiting for release | T1 ✅ T2 ✅ | [68-curated-json-conversations-plan.md](68-curated-json-conversations-plan.md) |
| [#69](https://github.com/DutchJaFO/Quotinator/issues/69) | API conversations | Waiting for release | T1 ✅ T2 ✅ | [69-api-conversations-plan.md](69-api-conversations-plan.md) |
| [#157](https://github.com/DutchJaFO/Quotinator/issues/157) | Sql.cs mixes domain-specific SQL into domain-agnostic Quotinator.Data | Waiting for release | T2 ✅ | [157-sql-domain-engine-split-plan.md](157-sql-domain-engine-split-plan.md) |
| [#158](https://github.com/DutchJaFO/Quotinator/issues/158) | ImportBatch entity/repository/enums live in Quotinator.Engine instead of Quotinator.Data | Waiting for release | T2 ✅ | [158-importbatch-to-data-plan.md](158-importbatch-to-data-plan.md) |
| [#149](https://github.com/DutchJaFO/Quotinator/issues/149) | Import endpoint: manual conflict-review workflow | Waiting for release | T1 ✅ T2 ✅ | [149-manual-conflict-review-plan.md](149-manual-conflict-review-plan.md) |
| [#152](https://github.com/DutchJaFO/Quotinator/issues/152) | Review endpoint grouping: split Admin / Quote / Import | Waiting for release | T1 ✅ T2 ✅ | [152-endpoint-grouping-plan.md](152-endpoint-grouping-plan.md) |
| [#165](https://github.com/DutchJaFO/Quotinator/issues/165) | Generalize record completeness to a 3-state model and hard-block modifying completed rows | Waiting for release | T1 ✅ T2 ✅ | [165-completeness-review-model-plan.md](165-completeness-review-model-plan.md) |
| [#162](https://github.com/DutchJaFO/Quotinator/issues/162) | Source: explicit file-carried id, decoupling matching from Title/Type/Date content | Waiting for release | T1 ✅ T2 ✅ | [162-source-explicit-id-plan.md](162-source-explicit-id-plan.md) |
| [#168](https://github.com/DutchJaFO/Quotinator/issues/168) | Quote's own Modify path never checks CompletenessGuard — a Complete quote can be silently overwritten by import | Waiting for release | T1 ✅ T2 ✅ | [168-quote-completeness-guard-plan.md](168-quote-completeness-guard-plan.md) |
| [#170](https://github.com/DutchJaFO/Quotinator/issues/170) | ImportActionNotDecidableException's message and doc comment are stale — still says "only Quote actions" | Waiting for release | T1 ✅ T2 ✅ | [170-not-decidable-wording-plan.md](170-not-decidable-wording-plan.md) |
| [#171](https://github.com/DutchJaFO/Quotinator/issues/171) | StageDirection: Modify/decidability | Waiting for release | T1 ✅ T2 ✅ | [171-stagedirection-modify-plan.md](171-stagedirection-modify-plan.md) |
| [#172](https://github.com/DutchJaFO/Quotinator/issues/172) | SoundCue: Modify/decidability | Waiting for release | T1 ✅ T2 ✅ | [172-soundcue-modify-plan.md](172-soundcue-modify-plan.md) |
| [#173](https://github.com/DutchJaFO/Quotinator/issues/173) | Person: explicit id, Modify/decidability, wire up dateOfBirth/dateOfDeath | Waiting for release | T1 ✅ T2 ✅ | [173-person-modify-plan.md](173-person-modify-plan.md) |
| [#169](https://github.com/DutchJaFO/Quotinator/issues/169) | Research: "universe/setting" concept linking Source and Character | Released | None (research) | [169-universe-setting-research-plan.md](169-universe-setting-research-plan.md) |
| [#179](https://github.com/DutchJaFO/Quotinator/issues/179) | Series/Universe schema: link related Sources, and Character↔Source many-to-many identity | Waiting for release | T1 ✅ T2 ✅ | [179-series-universe-schema-plan.md](179-series-universe-schema-plan.md) |
| [#183](https://github.com/DutchJaFO/Quotinator/issues/183) | List-endpoint shared infrastructure (parent of #193, #194, #195, #196) | Planning | — (parent) | [183-list-endpoint-infrastructure-plan.md](183-list-endpoint-infrastructure-plan.md) |
| [#194](https://github.com/DutchJaFO/Quotinator/issues/194) | Numeric query params published to the OpenAPI spec as string — transformer only covers year params | Waiting for release | T1 ✅ T2 ✅ | [194-numeric-param-schema-plan.md](194-numeric-param-schema-plan.md) |
| [#193](https://github.com/DutchJaFO/Quotinator/issues/193) | Generic listable repository capability + DI registrations for the six list entities | Waiting for release | T1 ✅ T2 ✅ | [193-listable-repository-plan.md](193-listable-repository-plan.md) |
| [#195](https://github.com/DutchJaFO/Quotinator/issues/195) | Standard pagination contract: PagedItems&lt;T&gt;, shared parsing and not-found helpers | Waiting for release | T1 ✅ T2 ✅ | [195-pagination-contract-plan.md](195-pagination-contract-plan.md) |
| [#196](https://github.com/DutchJaFO/Quotinator/issues/196) | Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter-parameter shape | Waiting for release | T1 ✅ T2 ✅ | [196-masterdata-conventions-plan.md](196-masterdata-conventions-plan.md) |
| [#184](https://github.com/DutchJaFO/Quotinator/issues/184) | Masterdata: GET /api/v1/masterdata/sources list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [184-sources-list-plan.md](184-sources-list-plan.md) |
| [#185](https://github.com/DutchJaFO/Quotinator/issues/185) | Masterdata: GET /api/v1/masterdata/characters list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [185-characters-list-plan.md](185-characters-list-plan.md) |
| [#186](https://github.com/DutchJaFO/Quotinator/issues/186) | Masterdata: GET /api/v1/masterdata/people list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [186-people-list-plan.md](186-people-list-plan.md) |
| [#187](https://github.com/DutchJaFO/Quotinator/issues/187) | Masterdata: GET /api/v1/masterdata/series list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [187-series-list-plan.md](187-series-list-plan.md) |
| [#188](https://github.com/DutchJaFO/Quotinator/issues/188) | Masterdata: GET /api/v1/masterdata/universes list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [188-universes-list-plan.md](188-universes-list-plan.md) |
| [#189](https://github.com/DutchJaFO/Quotinator/issues/189) | Conversations: GET /api/v1/conversations list endpoint | Waiting for release | T1 ✅ T2 ✅ | [189-conversations-list-plan.md](189-conversations-list-plan.md) |
| [#204](https://github.com/DutchJaFO/Quotinator/issues/204) | Masterdata: GET /api/v1/masterdata/stagedirections list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [204-stagedirections-list-plan.md](204-stagedirections-list-plan.md) |
| [#205](https://github.com/DutchJaFO/Quotinator/issues/205) | Masterdata: GET /api/v1/masterdata/soundcues list + get-by-id | Waiting for release | T1 ✅ T2 ✅ | [205-soundcues-list-plan.md](205-soundcues-list-plan.md) |
| [#180](https://github.com/DutchJaFO/Quotinator/issues/180) | Populate Series/Universe data via curated overlay file (review-only, staged) | Waiting for release | T1 ✅ T2 ✅ | [180-series-universe-population-plan.md](180-series-universe-population-plan.md) |
| [#190](https://github.com/DutchJaFO/Quotinator/issues/190) | Import files cannot express "leave this property alone" — absent and explicit-null are indistinguishable | Waiting for release | T1 ✅ T2 ✅ | [190-optional-fields-plan.md](190-optional-fields-plan.md) |
| [#191](https://github.com/DutchJaFO/Quotinator/issues/191) | Sources.Date is never populated — ResolveSourceAsync drops the quote's own date | Waiting for release | T1 ✅ T2 ✅ | [191-source-date-population-plan.md](191-source-date-population-plan.md) |
| [#206](https://github.com/DutchJaFO/Quotinator/issues/206) | Merge Quotinator.Engine into Quotinator.Core — collapse the three-project domain split to two | Waiting for release | T1 ✅ T2 ✅ | [206-core-engine-merge-plan.md](206-core-engine-merge-plan.md) |
| [#192](https://github.com/DutchJaFO/Quotinator/issues/192) | Expose series/universe on the quote read path — QuoteResponse fields and filters | Waiting for release | T1 ✅ T2 ✅ | [192-quote-series-universe-plan.md](192-quote-series-universe-plan.md) |
| [#209](https://github.com/DutchJaFO/Quotinator/issues/209) | Canonicalize explicit ids at capture — Source, Person, StageDirection, SoundCue, Conversation (sub-issue of #207) | Waiting for release | T1 ✅ T2 ✅ | [209-canonicalize-entity-ids-part-a-plan.md](209-canonicalize-entity-ids-part-a-plan.md) |
| [#210](https://github.com/DutchJaFO/Quotinator/issues/210) | Canonicalize Quotes.Id at capture, case-insensitive lookup — scope expanded mid-issue to a systemic `SqlIdCaseGuard` + `IdClauses` construction helper, then to unify Quote onto the same convention every other entity uses, then to flip the whole system-wide convention itself from uppercase to lowercase, then to add read-time presentation normalization, then generalized to a uniform SELECT-list wrap for every id column and ADR 012 squashed into current-state form, then closed the last gap by giving `RepositorySql`'s generic `SELECT *` queries an explicit, wrapped column list via a new `IEntityColumnMetadata` interface (sub-issue of #207) | In progress | T1 ⬜ T2 ⬜ | [210-canonicalize-quote-id-plan.md](210-canonicalize-quote-id-plan.md) |
| [#207](https://github.com/DutchJaFO/Quotinator/issues/207) | Canonicalize file-authored explicit ids at capture (parent — closes once #209/#210 both close) | In progress | — (parent) | [207-canonicalize-entity-ids-plan.md](207-canonicalize-entity-ids-plan.md) |
| [#174](https://github.com/DutchJaFO/Quotinator/issues/174) | Character: migrate to global identity via new Series/Universe schema (ADR + migration) | Planning | T1 ⬜ T2 ⬜ | [174-character-global-identity-plan.md](174-character-global-identity-plan.md) |
| [#175](https://github.com/DutchJaFO/Quotinator/issues/175) | Character: explicit id, Modify/decidability | Planning | T1 ⬜ T2 ⬜ | [175-character-modify-plan.md](175-character-modify-plan.md) |
| [#176](https://github.com/DutchJaFO/Quotinator/issues/176) | Conversation: Description-field Modify/decidability | Waiting for release | T1 ✅ T2 ✅ | [176-conversation-description-modify-plan.md](176-conversation-description-modify-plan.md) |
| [#163](https://github.com/DutchJaFO/Quotinator/issues/163) | Bulk-decide a staged import batch via file export/import, CSV and JSON (Phase 1 of #153) | Planning | T1 ⬜ T2 ⬜ | [163-bulk-decide-file-plan.md](163-bulk-decide-file-plan.md) |
| [#181](https://github.com/DutchJaFO/Quotinator/issues/181) | Minimal per-source conflict-resolution rule file + curated field-override preload | Planning | T1 ⬜ T2 ⬜ | [181-minimal-conflict-resolution-rule-file-plan.md](181-minimal-conflict-resolution-rule-file-plan.md) |
| [#153](https://github.com/DutchJaFO/Quotinator/issues/153) | Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2) | Planning | T1 ⬜ T2 ⬜ | [153-declarative-conflict-resolution-plan.md](153-declarative-conflict-resolution-plan.md) |
| [#154](https://github.com/DutchJaFO/Quotinator/issues/154) | Unify import, preview, and seeding on one staging engine | Waiting for release | T1 ✅ T2 ✅ | [154-import-staging-plan.md](154-import-staging-plan.md) |
| [#177](https://github.com/DutchJaFO/Quotinator/issues/177) | ImportBatches.Status never set to Applied via the staged decide→apply flow, breaking reversal | Planning | T1 ⬜ T2 ⬜ | No plan doc yet |
| [#155](https://github.com/DutchJaFO/Quotinator/issues/155) | Migration review: verify full incremental path from last-shipped v1.7.2 schema | Planning | T1 ⬜ T2 ⬜ | [155-migration-review-plan.md](155-migration-review-plan.md) |

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
#157 (Sql.cs domain/generic split) → discovered while implementing #69; no dependency on #69's own output, but touches the same Sql.cs file so is sequenced immediately after it to avoid a merge conflict with any later issue's SQL additions; unblocks #158
#158 (ImportBatch → Quotinator.Data) → discovered while implementing #157 (question of whether #157's Sql.ImportBatches placement was itself correct); requires #157 to land first since it undoes part of that move
#140 (auto-update sources) → requires #58 fix + #63; unblocks #144
#144 (converter plugin review) → requires #140 (done)
#149 (manual conflict-review workflow) → deferred out of #45; requires #56 (audit log) — done; unblocks #153
#152 (endpoint grouping review) → depended on #149's /api/v1/import group/tag existing first; moved the remaining /quotes/import(/preview) endpoints into that same group — done
#165 (generalized completeness/review model) → split out of #162 while planning it (2026-07-12): giving Source a Modify path exposed that IsComplete/NoValueKnown (#55) are write-once-at-insert only and needed to become a real, entity-agnostic hard-block mechanism before any entity could safely support Modify; requires #55's columns (unshipped, edited in place) and #154's staging engine; unblocks #162
#162 (Source field decidability) → decomposed out of #153 while planning it (2026-07-11); rescoped from Date-only to Title/Type/Date + explicit file-carried id after further scrutiny (2026-07-12, see issue for full history); requires #165 (completeness/Blocked model) and #149's decide/undo/apply machinery and #154's staging engine; prerequisite for #163's "mixed Quote/Source rows" coverage, not a phase of #153 itself
#168 (Quote's own CompletenessGuard gap) → found while investigating whether #162's Source fix cleared the way for extending Modify/decidability to other entities (2026-07-12); requires #165 (CompletenessGuard/Blocked mechanism, already shipped); does not block any other open issue in this milestone
#170 (ImportActionNotDecidableException stale wording) → no dependencies; independent bug, already wrong today; sequenced first among #170-#176 so none of the others need to touch this file's wording again
#171 (StageDirection Modify/decidability) → requires #162/#165/#168 (done); lowest-risk of the entity-Modify issues (already has explicit id, no linkage question); recommended first among the entity issues
#172 (SoundCue Modify/decidability) → requires #162/#165/#168 (done); benefits from #171 landing first (identical shape, shared translations-in-place sub-problem solved once)
#173 (Person: explicit id, Modify/decidability) → requires #162/#165/#168 (done); already global (no SourceId), simplest identity-entity issue; its "global entity, Name-keyed" Modify shape is the direct template for #175
#169 (research: universe/setting concept) → raised while scoping #174's Character merge algorithm (2026-07-12); closed 2026-07-14 with outcome "New issues in the current milestone + Architecture decision required" — its original "Not feasible/rejected" draft conclusion was corrected by the developer (conflated "not tagged in the schema" with "doesn't exist in the data"; the bundled dataset already contains real multi-Source franchises); unblocked #179
#179 (Series/Universe schema: link related Sources, Character↔Source many-to-many identity) → filed directly from #169's corrected findings (2026-07-14); lands the Universe→Series→Source hierarchy, the CharacterSources join replacing Character.SourceId, and the Source.Type-as-identity-anchor invariant, with zero data-merging risk of its own (existing Characters rows reshaped 1:1); requires #162/#165/#168 (done); unblocks #174, #180, and #185/#187/#188 (Character/Series/Universe need #179's schema to exist before they can be listed)
#183 (list-endpoint shared infrastructure) → filed 2026-07-15 while scoping #180's "list Series/Universe by id" follow-on need; split into #193/#194/#195/#196 on 2026-07-16 after review found its central premise wrong (the three "duplicate" pagination implementations are three different contracts) and surfaced two pre-existing defects that each need their own red tests. Parent/tracking issue — carries no implementation; see [183-list-endpoint-infrastructure-plan.md](183-list-endpoint-infrastructure-plan.md) for the sub-issue map. Downstream issues depend on the specific sub-issue that unblocks them, never on this parent
#194 (numeric params published as string) → requires nothing; unblocks #195 — #195 converts /admin/audit and /import/actions to `string?` binding, which without #194's transformer fix would regress their published schema from `integer|string` to bare `string`. Pre-existing defect: CLAUDE.md's rule says to register the endpoint *path* with the year-param transformer, which was done and is insufficient — the param *name* must be registered too, so `page`/`pageSize`/`n`/`limit` publish as `string` today
#193 (generic listable repository + DI) → requires nothing; unblocks #184, #185, #186, #187, #188, #189. Data layer only — no endpoints; registers repositories nothing consumes yet, deliberately, matching #183's no-new-routes boundary
#195 (pagination contract + helpers) → requires #194 (done); unblocks #184, #185, #186, #187, #188, #189 — sequenced before #196 because #189 needs only #193+#195, not #196, so finishing this first unblocks #189 immediately rather than waiting on both. Implements the contract settled with the developer (2026-07-16): pageSize has no maximum relative to available items — a partial page is normal, not a failure — so only a system-wide 500 ceiling errors; pageSize=0 means everything as one page; page beyond the last is a distinct 422
#196 (masterdata conventions) → requires nothing; unblocks #184-#188 (routing + ApiTags.MasterData) and #192 (filter convention only). T1+T2 (touches Program.cs's OpenAPI tag list; this project always runs T2 regardless). #184-#188 still need both #195 and #196 regardless of which lands first — the two have no dependency on each other
#184 (masterdata: Sources list+byId) → requires #193, #195, #196; independent of #179/#180 for its own implementation order (Source's `SeriesId` column predates the Series/Universe schema and was already nullable, so #184 does not need #179/#180 to have landed first to build its endpoint). Response shape corrected during cross-plan review (2026-07-18, alongside #185/#187): `SourceResponse.Series` is a resolved `MasterDataReference` (`{id, name}`, via the new `ISourceSeriesReferenceReader`), never a bare `SeriesId` — this note previously described the pre-redesign shape and was stale
#185 (masterdata: Characters list+byId) → requires #193, #195, #196 and #179 (response shape depends on the `CharacterSources` join table #179 introduced)
#186 (masterdata: People list+byId) → requires #193, #195, #196; independent of #179/#180
#187 (masterdata: Series list+byId) → requires #193, #195, #196 and #179 (Series table itself); gives #180's overlay-file work its first way to verify results via the API rather than DbInspector only
#188 (masterdata: Universe list+byId) → requires #193, #195, #196 and #179 (Universe table itself); same #180 verification benefit as #187
#189 (Conversations: GET / list endpoint) → requires #193 and #195; independent of #196 (keeps its existing route and ApiTags.Conversations tag — Conversations is a consumer of masterdata, not a masterdata entity) and of #179/#180; fills the gap where Conversations has `GET /{id}` but no list endpoint
#204 (masterdata: StageDirection list+byId) → requires #195, #196; independent of #193, which explicitly scoped itself to six entities (Source, Character, Person, Series, Universe, Conversation) and never included StageDirection despite it already having an `IRestorableRepository<T>` from #67/#68 — this issue adds the missing `IListableRepository<T>` binding itself. Filed 2026-07-19 after the #184-#189 batch's own implementation surfaced the gap
#205 (masterdata: SoundCue list+byId) → same rationale and same #193 gap as #204, for SoundCue instead of StageDirection
#180 (Populate Series/Universe data via curated overlay file, review-only, staged) → filed 2026-07-15 while verifying the import pipeline stays two-stage ahead of upcoming data-enrichment work; requires #179 (needs Universe/Series/Source.SeriesId to exist); explicitly does not build recurring-conflict automation of its own — that remains #153's job; independent of #174; independent of #183-#189 (does not require the list endpoints to exist, though #187/#188 make its results easier to verify once both land)
#190 (import files cannot express "leave this property alone") → found while implementing #180 (2026-07-16), whose overlay file is the first bundled file wanting to set one property and touch nothing else; pre-existing and cross-cutting, affecting every optional field on SourceEntry/PersonEntry/SourceStageDirection/SourceSoundCue/SourceConversation (#162/#171/#172/#173/#176) — #180 sidesteps it on its own enrichment path rather than changing five shipped entities' Modify behaviour, so nothing blocks on this; once it lands, #180's carry-the-existing-Date-through workaround becomes redundant and should be removed. Plan doc written 2026-07-19: scope expanded during planning review to also cover `SourceEntry.SeriesName` (same bug, one field over, on the explicit-id correction path — the issue's own background text only named `Date`), and to fix a pre-existing, unrelated `MergeOurs`/`MergeTheirs` gap on the natural-key branch as a drive-by once that branch is rewritten anyway. Implemented and T2-verified 2026-07-19 (`Optional<T>` lives in `Quotinator.Data.Import`, not Core — domain-agnostic, alongside `FieldMergeResolver`). A masterdata Sources id-lookup 404 was found live during T2, unrelated to this issue's own mechanism, and spun off as its own investigation rather than folded in. See [190-optional-fields-plan.md](190-optional-fields-plan.md).
#191 (Sources.Date never populated) → found live via T2 during #180 (2026-07-16); ResolveSourceAsync drops the quote's own date, so all 479 seeded Sources are null-dated despite 741/841 quotes carrying one; independent of #180 (which never sets a date) and of #190 (which governs what a *file* may say, not what an implicitly-created Source inherits). Both scope questions resolved with the developer (2026-07-19): no tie-break rule for the 16 conflicting-date cases — first-quote-wins rides along on ResolveSourceAsync's existing natural-key index for free, an authoritative answer deferred to the future Data Enrichment milestone; no backfill for already-seeded deployments — the fix covers newly-imported/newly-seeded Sources only, an operator on an existing deployment needs a full Reset. Plan doc: [191-source-date-population-plan.md](191-source-date-population-plan.md)
#207 (canonicalize file-authored explicit ids at capture) → found live via T2 during #190 (2026-07-19): a lowercase-authored explicit Source id imports and reads correctly via the Quote→Source join, but 404s via the masterdata Sources `GetById` endpoint, because `Sources.Id`/`Quotes.SourceId`/`CharacterSources.SourceId` are all written from the same uncanonicalized `ImportActionPlanner` in-memory value — accidentally self-consistent with each other, never actually canonical. Formalised as ADR 012, which requires the fix to go through a single reusable `Quotinator.Data.Helpers.EntityIdCanonicalizer` helper plus a cross-entity guard test, not an ad hoc per-call-site fix — a narrower fix (uppercase only the Source insert) was considered and rejected after tracing that it would break the Quote→Source join outright. Split into #209/#210 on 2026-07-19 (per `docs/workflow/issues.md`'s sub-issue convention) after a scope-expansion finding during planning: StageDirection/SoundCue/Conversation share the identical capture-time gap (previously undiscovered), and Quotes.Id has its own distinct target casing plus query-audit surface unrelated to what the other entities need. Parent/tracking issue — carries no implementation; see [207-canonicalize-entity-ids-plan.md](207-canonicalize-entity-ids-plan.md) for the sub-issue map
#209 (canonicalize Source/Person/StageDirection/SoundCue/Conversation explicit ids at capture) → requires nothing; independent of #210 (both extend the same `EntityIdCanonicalizer` class but neither blocks the other); independent of #190 itself (a data-consistency bug, not an absent-vs-null bug), though found during its T2 pass. Two findings surfaced during implementation, both fixed inline in the same issue rather than filed separately once their real severity was confirmed: (1) `StageDirections`/`SoundCues`/`Conversations`' `SelectExistingById` (and sibling queries) were case-sensitive, unlike `Sources`/`People`'s already-`UPPER()`-wrapped equivalents — fixed to match; (2) a Conversation's `lines[].stageDirectionId`/`soundCueId` cross-references, left un-canonicalized, broke `ConversationLines`' real `FOREIGN KEY` constraint against the now-canonicalized `StageDirections`/`SoundCues` tables the moment real bundled-file data was seeded — confirmed live (`SQLite Error 19`, 50 failing tests) before being fixed, not merely theorised
#210 (canonicalize Quotes.Id at capture, case-insensitive lookup) → requires nothing; independent of #209 for the same reason. Scope expanded mid-issue: while implementing, the developer asked whether audit-log and other non-masterdata endpoints had the same casing gap; the resulting audit found several more case-sensitive id comparisons beyond Quotes.Id, and the developer's direction was to fix every one found and build a permanent automated guard rather than a one-time manual pass ("never assume a comparison that works won't break in the future simply because the known references have the same logic"). Delivered as `Quotinator.Data.Diagnostics.SqlIdCaseGuard`, structurally mirroring the existing CVE-2025-6965 `SqlAggregateGuard`, wired into `SqlQueryGuardTests` (both `Quotinator.Core.Tests` and `Quotinator.Data.Tests`) and `RepositorySqlGuardTests` via their existing `DynamicData` enumeration methods. The guard's own scan found ~46 case-sensitive id comparisons across `Quotinator.Core`/`Quotinator.Data`'s `Sql.cs` files, `RepositorySql.cs` (the generic repository layer shared by every entity), and `SqliteQuoteService.BuildFilterWhere`'s dynamic filter clauses — including one genuinely new finding (`SqliteQuoteService`'s `seriesId`/`universeId` filters) that neither the manual audit nor a prior background research agent had caught. A second round of scope expansion followed the same session: the developer suggested going further than catching mistakes after the fact — build helper methods (`Quotinator.Data.Queries.IdClauses`: `Equals`/`In`/`NotIn`/`Join`) that construct the comparison correctly in the first place, then rewrote every fixed query and factory method to call it. That rewrite required converting affected queries from `const` to `static readonly` (a method call isn't a compile-time constant), which surfaced a second guard blind spot — the reflection-based test enumeration only scanned fields, so `Sql.SystemImportActions.SelectById` (declared as a property) had a real, live, unwrapped `WHERE Id = @id` that no test had ever caught; fixed, with the reflection widened and a dedicated regression test added. The developer also decided joins should be wrapped too, reversing ADR 012's original "joins don't need it" stance; the guard itself was extended to flag unwrapped JOIN/correlated-subquery conditions, which caught a third blind spot along the way (`NOT IN` wasn't recognised by the guard's own regex, and `SqliteQuoteService`'s `/random` dedup-exclusion clause was genuinely unwrapped). A separate question the developer raised in passing — whether non-id string comparisons (`Status`, `EntityType`, etc.) have the same class of gap — was filed as its own research issue, [#211](https://github.com/DutchJaFO/Quotinator/issues/211), rather than folded into #210. A third round of scope expansion followed once T1/T2 both passed and #210 reached "Waiting for release": the developer reviewed the live API and asked why Quote was the only entity whose id rendered lowercase ("we need to be consistent"). Investigation found the original safety concern for keeping Quote lowercase — protecting already-deployed databases with lowercase-stored rows from breaking — was already moot, since #210's own case-insensitive-read infrastructure (`SqlIdCaseGuard`/`IdClauses`) makes an old row resolve correctly regardless of what casing new writes use. Flipped `ImportActionPlanner`'s capture point to uppercase (going-forward only, no migration), which surfaced three real production bugs outside SQL entirely — a `Guid.ToString()` round-trip defaulting to lowercase in `QuoteSeedWriter`, a case-sensitive C# string equality in `QuotinatorDatabaseInitializer`'s duplicate tracking, and `ConversationLines.QuoteId` reproducing the exact FOREIGN KEY bug #209 already fixed for `StageDirectionId`/`SoundCueId` — none catchable by `SqlIdCaseGuard`/`IdClauses` since none were SQL defects. A fourth round of scope expansion followed one more review cycle later: the developer drew a distinction the third round had conflated — `UPPER(...)` in a comparison is a case-insensitivity mechanism only, not evidence the canonical stored/presented form is itself uppercase — and reopened the format choice itself, settling on lowercase system-wide (`Guid.ToString("D")`'s own default). While making that switch, testing directly (not assuming) surfaced two infrastructure-level findings that were not casing bugs themselves but had been masked by the previous convention: `GuidHandler` was never actually the "single global choke point" it claimed to be (Dapper's own built-in `typeMap` resolves a bare `Guid` parameter's `DbType` before ever consulting a registered `ITypeHandler`, silently skipping `GuidHandler.SetValue` for outbound parameters this whole project's history), and Dapper's list-parameter expansion does not reliably invoke a registered handler per element the way scalar binding does (found via `ConversationLineCountReaderTests`/`CharacterSourceLinkReader`, both silently matching zero rows). Introduced `GuidExtensions.ToCanonicalId` as the real single choke point, replacing ~35 scattered call sites; flipped `IdClauses` from `UPPER(...)` to `LOWER(...)` wrapping to match. A fifth round of scope expansion followed after the fourth shipped: the developer reported from live Postman output that `batchId`/`entityId`/`recordId` still rendered uppercase in `/import/actions`/`/admin/audit` responses for pre-existing data, next to already-lowercase `id` fields in the same response. Root cause: capture-time canonicalization and `IdClauses`' comparison-side case-insensitivity are both real but neither touches a `SELECT` that isn't filtering or joining on the column in question — a `Guid`-typed `Id` field renders lowercase for free via `System.Text.Json`'s own default `Guid` formatting, but a `string`-typed reference field (`BatchId`/`EntityId`/`ExistingBatchId`/`RecordId`) has no such safety net and serializes exactly the casing already on disk. Fixed with a third, distinct mechanism — `LOWER(...) AS ColumnName` wrapping directly in the shared `SELECT` column lists (`Sql.SystemImportActions.SelectColumns`, `Sql.SystemAudit.SelectPaged`) — proven both by a real-SQLite integration test using a deliberately mixed-case fixture and live via T2. A sixth round of scope expansion followed immediately: the developer reviewed the code directly (not just the live API) and found `Sql.SystemChangeLog.SelectByEntity` had the identical bug (`EntityId` unwrapped), missed because its reader has no HTTP endpoint yet despite being a real, DI-registered query — plus roughly twenty stale comments elsewhere in the codebase still asserting the previous uppercase convention as current fact, left behind by the fourth and fifth rounds. The developer's own framing after finding both: "guard checks should have caught those. The fact that these weren't caught tells me that your unit tests and guards are not good enough." Root-caused to a structural blind spot — `SqlIdCaseGuard` only ever scanned `WHERE`/`JOIN` comparisons, never `SELECT` column lists — and closed with two new mechanisms: `Quotinator.Data.Diagnostics.SqlSelectPresentationGuard` (scans every SQL constant/factory method for a known unwrapped string id-reference column) and `EntityIdPresentationClassificationTests` (reflects over every `string`-typed `Id`-suffixed entity property and fails on anything not yet explicitly classified, closing the exact "nobody noticed a new string id field existed" gap). `SystemChangeLog.InitiatedById` was deliberately left unwrapped and documented as exempt — it's polymorphic (UUID, HTTP route, or provider name), not always an id. A seventh round of scope expansion followed the same review cycle: the developer pointed directly at unwrapped `q.Id`/`ser.Id`/`uni.Id` columns in `SqliteQuoteService.SelectBase` and asked why the SELECT-list guard wasn't using the same technique as the JOIN guard — i.e. wrap every id column unconditionally, not just ones a registry classified as `string`-typed. Root cause: the sixth round's registry-based reasoning was the exact "safe because of how it's used today" assumption `IdClauses.Join` had already rejected for JOINs, and demonstrably fragile (`MasterDataReference.Id` is `string`-typed today despite backing a formerly-`Guid` column). Fixed with `IdClauses.SelectColumn(column, alias)` — `LOWER(column) AS alias`, used uniformly for every `*Id`-suffixed column in both `Sql.cs` files; `SqlSelectPresentationGuard` rewritten from registry-based to fully generic strip-then-scan (mirroring `SqlIdCaseGuard.FindViolations`'s own technique exactly), so `EntityIdPresentationClassificationTests` was deleted as no longer needed — a generic guard has no registry to keep in sync. `RepositorySql.cs`'s `SELECT *` queries were confirmed and documented as a genuine structural boundary (ADR 004's entity-agnostic design leaves no column list to rewrap), not silently left unaddressed. Separately, the developer set a standing policy — an ADR's own git history carries its revisions; the file itself should read as the current, settled design — so ADR 012's four accumulated "Revision" sections were squashed into one clean current-state document. See ADR 012 (rewritten; no longer has named "Revision" subsections — read it directly for current mechanisms). An eighth round of scope expansion followed once that "structural boundary" framing itself reached the developer's own review: pointing directly at `RepositorySql.SelectByIds`/`SelectPage` and `SqliteRepository.GetPageAsync`, the developer noted that `ValidColumnNames` (reflection-derived, already used to validate a caller-supplied `ORDER BY` column) proved the necessary column-list knowledge already existed one layer above `RepositorySql`'s static factory methods — the seventh round's "no column-list knowledge available" premise was simply wrong, not a genuine limit. The developer proposed the fix's actual shape directly: an interface exposing the column list, with id columns identified separately so a future entity with a non-standard foreign key name could override the default naming-convention inference. Delivered as `IEntityColumnMetadata` (`ValidColumnNames`/`IdColumnNames`) with `ReflectedColumnMetadata` as the default per-`Type`-cached implementation; `RepositorySql.BuildSelectColumns` now builds an explicit, `IdClauses.SelectColumn`-wrapped column list for every one of its six SELECT-producing factory methods, closing the last "the guard never had anything to scan here" gap — `RepositorySqlFactory_PassesSelectPresentationGuard`/`...PassesIdCaseGuard` now scan genuine explicit column lists instead of passing vacuously against empty `SELECT *`. Full suite green with zero regressions; T2 re-verified live across every generic-repository-backed masterdata endpoint. ADR 012 and CLAUDE.md updated to remove the now-resolved "structural boundary" claim
#206 (merge Quotinator.Engine into Quotinator.Core) → found while planning #192 (2026-07-19): a masterdata response DTO needing both a Core-owned type (MasterDataReference) and a Data-owned type (CompletenessStatus) cannot be expressed in Core or Engine alone, only in Api — the root cause of #192's original MasterDataReference-placement tangle. No dependencies of its own; a pure structural/namespace merge, behaviour-preserving. Unblocks #192 directly (removes the need for any Api-layer response DTO workaround) and is a soft prerequisite for any future issue that would otherwise hit the same Core/Data-both-needed wall
#192 (expose series/universe on the quote read path) → found while reviewing #180's T1 (2026-07-16): #179 built the schema and #180 populated it, but nothing reads it — QuoteResponse carries neither field and no endpoint filters on either, so the data has no read path from a quote at all; this is #169's original motivation ("a random Star Wars quote") finally reachable. Requires #180 (data to expose), **#196** (whose filter-parameter convention this consumes rather than re-deciding — #196 owns the id-valued-vs-name-valued decision, so sequencing #192 after it is what stops the quote endpoints' filters and the masterdata endpoints' filters drifting into two shapes; #196 not the #183 parent, per the sub-issue dependency rule), and now **#206** (found while planning #192 itself — without #206, QuoteResponse.Series/Universe has no clean home for MasterDataReference; #206 is sequenced first so #192 never needs the Api-layer workaround its earlier drafts explored). Does not require #193/#194/#195 — #192 adds no list endpoint, so it needs neither the repository capability nor the pagination contract. Distinct from #184/#187/#188, which enumerate Series/Universe as entities rather than enriching a quote
#174 (Character: migrate to global identity via new Series/Universe schema, ADR + migration) → rewritten 2026-07-14 to depend on #179 instead of copying Person's shape (that approach was found concretely wrong — see #169's closing comment); requires #179 (structural schema) in addition to #162/#165/#168 (done); owns the actual merge algorithm within #179's structural boundary — unblocks #175
#175 (Character: explicit id, Modify/decidability) → requires #174 (must land first — building Modify on the old per-Source model would be throwaway work); structurally a near-copy of #173 once Character is global
#176 (Conversation: Description-field Modify/decidability) → requires #162/#165/#168 (done); not technically blocked by #170-#175, sequenced last so its (smallest) additions have every other entity's now-proven pattern in place as precedent; line-editing explicitly deferred to a separate future issue
#163 (bulk-decide via file export/import, Phase 1 of #153) → decomposed out of #153 while planning it; requires #162 (so more than Quote rows are decidable) and #149/#154's existing decide machinery; unblocks #153 (Phase 2)
#181 (minimal per-source conflict-resolution rule file + curated field-override preload) → filed 2026-07-15 while preparing for the Data Enrichment milestone's known #147 conflicts; no hard dependency on #163 (hand-authored, not generated) or on #179/#174/#180's Character/Series work; ships a hand-authored precursor to #153's own rule-file format — #153's plan doc Steps 2 and 6 marked "Superseded by #181", to be confirmed rather than designed fresh once #153 itself is implemented
#153 (declarative conflict-resolution file, Phase 2) → deferred out of #149; requires #149 (decide/undo/apply machinery and FieldMergeResolver to build on), #154's staging model, #163 (Phase 1 — generalizes the per-action decisions #163's file format produces into persistent per-source rules), and now #181 for the rule-file format itself
#154 (unify import/preview/seeding on one staging engine) → emerged while planning #59; requires #149 (IConflictResolutionCoordinator, System_ImportConflicts as the template) and #56 (audit log); unblocks #59 (redefined), #162, #163, and #153
#177 (ImportBatches.Status never set to Applied via staged apply, breaking reversal) → found live via T2 during #171/#172 implementation (2026-07-13); entity-agnostic, no dependencies; must land before #155's migration review since #155 exercises the full apply/reverse surface
#155 (migration review before milestone close) → independent of the others in what it touches, but must always be the LAST issue worked in this milestone — it verifies the full incremental migration path against the last-shipped schema, so every other issue's schema/migration changes (including #174's ADR+migration and any #177 fix) must already be landed before this one runs, or the review is incomplete by construction
```

---

## Order of operations

| #  | Issue | Title | Status |
|----|-------|-------|--------|
| 1  | #61 | Seed script: one file per source | Released |
| 2  | #71 | Generic repository pattern | Released |
| 3  | #78 | Repository: transaction and shared connection support | Released |
| 4  | #58 | ImportBatches schema | Released |
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
| 20 | #67 | Conversations schema | Waiting for release |
| 21 | #68 | Curated JSON conversations | Waiting for release |
| 22 | #69 | API conversations | Waiting for release |
| 23 | #157 | Sql.cs mixes domain-specific SQL into domain-agnostic Quotinator.Data | Waiting for release |
| 24 | #158 | ImportBatch entity/repository/enums live in Quotinator.Engine instead of Quotinator.Data | Waiting for release |
| 25 | #144 | Converter plugins: generic naming, internal-only slots, configuration options | Waiting for release |
| 26 | #165 | Generalize record completeness to a 3-state model and hard-block modifying completed rows | Waiting for release |
| 27 | #162 | Source: explicit file-carried id, decoupling matching from Title/Type/Date content | Waiting for release |
| 28 | #168 | Quote's own Modify path never checks CompletenessGuard | Waiting for release |
| 29 | #170 | ImportActionNotDecidableException stale wording fix | Waiting for release |
| 30 | #171 | StageDirection: Modify/decidability | Waiting for release |
| 31 | #172 | SoundCue: Modify/decidability | Waiting for release |
| 32 | #173 | Person: explicit id, Modify/decidability | Waiting for release |
| 33 | #169 | Research: "universe/setting" concept linking Source and Character | Released |
| 34 | #179 | Series/Universe schema: link related Sources, and Character↔Source many-to-many identity | Waiting for release |
| 35 | #194 | Numeric query params published to the OpenAPI spec as string (sub-issue of #183) | Waiting for release |
| 36 | #193 | Generic listable repository capability + DI registrations (sub-issue of #183) | Waiting for release |
| 37 | #195 | Standard pagination contract: PagedItems&lt;T&gt;, shared helpers (sub-issue of #183) | Waiting for release |
| 38 | #196 | Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter shape (sub-issue of #183) | Waiting for release |
| 39 | #183 | List-endpoint shared infrastructure (parent — closes once #193–#196 all close) | Planning |
| 40 | #184 | Masterdata: GET /api/v1/masterdata/sources list + get-by-id | Waiting for release |
| 41 | #185 | Masterdata: GET /api/v1/masterdata/characters list + get-by-id | Waiting for release |
| 42 | #186 | Masterdata: GET /api/v1/masterdata/people list + get-by-id | Waiting for release |
| 43 | #187 | Masterdata: GET /api/v1/masterdata/series list + get-by-id | Waiting for release |
| 44 | #188 | Masterdata: GET /api/v1/masterdata/universes list + get-by-id | Waiting for release |
| 45 | #189 | Conversations: GET /api/v1/conversations list endpoint | Waiting for release |
| 46 | #204 | Masterdata: GET /api/v1/masterdata/stagedirections list + get-by-id | Waiting for release |
| 47 | #205 | Masterdata: GET /api/v1/masterdata/soundcues list + get-by-id | Waiting for release |
| 48 | #180 | Populate Series/Universe data via curated overlay file (review-only, staged) | Waiting for release |
| 49 | #206 | Merge Quotinator.Engine into Quotinator.Core — collapse the three-project domain split to two | Waiting for release |
| 50 | #192 | Expose series/universe on the quote read path — QuoteResponse fields and filters | Waiting for release |
| 51 | #190 | Import files cannot express "leave this property alone" — absent and explicit-null are indistinguishable | Waiting for release |
| 52 | #209 | Canonicalize explicit ids at capture — Source, Person, StageDirection, SoundCue, Conversation (sub-issue of #207) | Waiting for release |
| 53 | #210 | Canonicalize Quotes.Id at capture, case-insensitive lookup — scope expanded to a systemic `SqlIdCaseGuard` + `IdClauses` construction helper, then unified Quote onto the same convention as every other entity, then flipped the whole system-wide convention to lowercase, then added read-time presentation normalization, then generalized it to a uniform SELECT-list wrap for every id column regardless of C# type, and squashed ADR 012's revision history into a single current-state document (sub-issue of #207) | In progress |
| 54 | #207 | Canonicalize file-authored explicit ids at capture (parent — closes once #209/#210 both close) | In progress |
| 55 | #191 | Sources.Date is never populated — ResolveSourceAsync drops the quote's own date | Waiting for release |
| 56 | #174 | Character: migrate to global identity via new Series/Universe schema (ADR + migration) | Planning |
| 57 | #175 | Character: explicit id, Modify/decidability | Planning |
| 58 | #176 | Conversation: Description-field Modify/decidability | Waiting for release |
| 59 | #163 | Bulk-decide a staged import batch via file export/import, CSV and JSON (Phase 1 of #153) | Planning |
| 60 | #181 | Minimal per-source conflict-resolution rule file + curated field-override preload | Planning |
| 61 | #153 | Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2) | Planning |
| 62 | #177 | ImportBatches.Status never set to Applied via staged apply, breaking reversal | Planning |
| 63 | #155 | Migration review: verify full incremental path from last-shipped v1.7.2 schema | Planning |

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
- [#157 — Sql.cs domain/generic split](157-sql-domain-engine-split-plan.md)
- [#158 — ImportBatch → Quotinator.Data](158-importbatch-to-data-plan.md)
- [#140 — Auto-update bundled sources from manifest URL](140-auto-update-sources-plan.md)
- [#144 — Converter plugins: generic naming, internal-only slots, configuration options](144-converter-plugin-review-plan.md)
- [#141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)](141-system-table-preservation-plan.md)
- [#143 — Fresh-database baseline schema + Data/Engine migration ownership split](143-migration-ownership-baseline-plan.md)
- [#168 — Quote's own Modify path never checks CompletenessGuard](168-quote-completeness-guard-plan.md)
