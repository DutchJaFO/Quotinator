# v1.8.0 — Milestone Overview

**GitHub milestone:** #18
**Type:** Maintenance milestone (catch-all for bugs and minor improvements, targeting v1.8.x releases)
**Previous maintenance milestone:** v1.7.0 (#17) — closed 2026-06-28

Unlike a feature milestone, issues here are not necessarily related to each other. That does not
change the branching rule — see `docs/workflow/process.md`'s "Step 2 — Create the feature branch": a
milestone always gets exactly one branch, covering every issue in it.

**This milestone's own history (2026-07-31):** #166, #197, #159, and #146 each got their own
branch/PR before that rule was written down — a direct cause of the GitHub Ruleset `BEHIND` friction
that prompted writing it. From #208 onward, every remaining issue (#151, #156, #222, #227, #232,
#236, #244, #245, #249, and #208 itself) shares `feature/v1.8.0-maintenance-batch`, per the corrected
rule. #249 was filed 2026-08-01 while planning #151 (see ADR 014) — a new dependency, not a
pre-existing part of this list. #227 was resequenced to run immediately next, ahead of every other
not-yet-implemented issue, the same day — see the Dependency map below for why. #227 was later
decomposed into six sub-issues (#251/#252/#253/#254/#255/#256, also 2026-08-01) once its own naming
decision shipped as ADR 015/016 — #253/#254 inherit #227's original "runs first" position; #251/#252
and #255/#256 do not carry that urgency.

---

## Issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#166](https://github.com/DutchJaFO/Quotinator/issues/166) | HA add-on: split into separate stable and beta sub-add-ons | Waiting for release | T1 ✅ T2 ✅ T3 ✅ | [166-ha-addon-stable-beta-split-plan.md](166-ha-addon-stable-beta-split-plan.md) |
| [#197](https://github.com/DutchJaFO/Quotinator/issues/197) | MSTest analyzer diagnostics (e.g. MSTEST0068) are invisible to the 0-warnings build policy — no .editorconfig exists | Waiting for release | N/A for T1 (test/build-tooling only — a VS boot never rebuilds `tests/`) T2 ✅ | No plan doc yet |
| [#159](https://github.com/DutchJaFO/Quotinator/issues/159) | Document repository-is-C#-only tooling policy as an ADR | Released | T1 ✅ | No plan doc — pure content fix, no implementation decisions required |
| [#146](https://github.com/DutchJaFO/Quotinator/issues/146) | Audit memory-only project conventions and move genuine ones into CLAUDE.md/docs | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — pure content fix, no implementation decisions required |
| [#208](https://github.com/DutchJaFO/Quotinator/issues/208) | Issue-creation process: always propose label + milestone in the same draft-review pass | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — pure content fix, no implementation decisions required |
| [#150](https://github.com/DutchJaFO/Quotinator/issues/150) | Audit: ensure all enum-valued POCO properties have matching DB CHECK constraints | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#151](https://github.com/DutchJaFO/Quotinator/issues/151) | Should System_-prefixed audit-trail tables purge rows referencing Reset-wiped entities? | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — decision recorded in [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) |
| [#249](https://github.com/DutchJaFO/Quotinator/issues/249) | Audit-trail bulk export + conflict-resolution data auto-purge (redesigned from the original "export to folder" framing) | Waiting for release | T1, T2 (both ✅) | [249-audit-trail-export-and-conflict-data-purge-plan.md](249-audit-trail-export-and-conflict-data-purge-plan.md) |
| [#156](https://github.com/DutchJaFO/Quotinator/issues/156) | Reset: use the fresh-database baseline script instead of drop-all-user-tables + replay, plus a system-reseed extension point | Waiting for release | T1 ✅ T2 ✅ | [156-reset-baseline-and-system-reseed-plan.md](156-reset-baseline-and-system-reseed-plan.md) |
| [#222](https://github.com/DutchJaFO/Quotinator/issues/222) | Unicode-aware case-insensitive LIKE matching (accented/non-ASCII characters) | Waiting for release | T1 ✅ T2 ✅ | [222-unicode-like-matching-plan.md](222-unicode-like-matching-plan.md) |
| [#148](https://github.com/DutchJaFO/Quotinator/issues/148) | OpenAPI: document response models for existing quote/admin endpoints | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#227](https://github.com/DutchJaFO/Quotinator/issues/227) | Import-table naming standardization + general import-file content provenance (FileResource / FileResourceLine) | Waiting for release | N/A (decision/scoping only — decomposed into #251/#252/#253/#254/#255/#256) | [227-domain-prefixed-naming-implementation-plan.md](227-domain-prefixed-naming-implementation-plan.md) (now reference research, not an active plan) |
| [#251](https://github.com/DutchJaFO/Quotinator/issues/251) | FileResource/FileResourceLine: general import-file content provenance (design + implementation) | Waiting for release | T1, T2 (both ✅) | [251-file-resource-provenance-plan.md](251-file-resource-provenance-plan.md) |
| [#252](https://github.com/DutchJaFO/Quotinator/issues/252) | Confirm whether #153's SourceFileOverride registry should be superseded by FileResource | Waiting for release | T1, T2 (both ✅) | [252-source-file-override-supersession-plan.md](252-source-file-override-supersession-plan.md) — depends on #251 (done) |
| [#253](https://github.com/DutchJaFO/Quotinator/issues/253) | Rename Quotinator.Data-owned tables and entities to Import_/Audit_/System_ domains | Waiting for release | T1 ✅ T2 ✅ | [253-data-owned-rename-plan.md](253-data-owned-rename-plan.md) |
| [#254](https://github.com/DutchJaFO/Quotinator/issues/254) | Rename Quotinator.Core-owned tables and entities to the Quotinator_ domain | Waiting for release | T1 ✅ T2 ✅ | [254-core-owned-rename-plan.md](254-core-owned-rename-plan.md) |
| [#255](https://github.com/DutchJaFO/Quotinator/issues/255) | Move enums to dedicated Enums/ folders (Data + Core) | Waiting for release | N/A | [255-enum-folder-moves-plan.md](255-enum-folder-moves-plan.md) |
| [#256](https://github.com/DutchJaFO/Quotinator/issues/256) | Fix Response/Dto/class-suffix violations (SeedFilePreview, *Dto renames, ChangelogRoot) | Waiting for release | N/A | [256-response-dto-suffix-fixes-plan.md](256-response-dto-suffix-fixes-plan.md) |
| [#178](https://github.com/DutchJaFO/Quotinator/issues/178) | Changelog: add an optional one-line quote to each release entry | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#232](https://github.com/DutchJaFO/Quotinator/issues/232) | Reduce OS-level vulnerabilities in Docker base image (Docker Scout scan) | Waiting for release | N/A (research, no code change) | No plan doc — findings recorded in `docs/security/README.md`'s "Docker base image (OS packages)" section |
| [#250](https://github.com/DutchJaFO/Quotinator/issues/250) | Periodically re-scan the Docker image with Docker Scout after fresh builds | Waiting for release | N/A (docs-only — defines the new milestone-scoped T4 tier; live-verified via a real Docker Scout scan) | No plan doc — pure content fix, no implementation decisions required |
| [#236](https://github.com/DutchJaFO/Quotinator/issues/236) | Release workflow: HA can see a config.yaml version bump before the matching Docker image is pushed | Waiting for release | N/A (process/documentation change only) | [236-release-workflow-version-race-plan.md](236-release-workflow-version-race-plan.md) |
| [#244](https://github.com/DutchJaFO/Quotinator/issues/244) | Hidden Roslyn code-style and .NET analyzer diagnostics are invisible to the 0-warnings build policy (IDE0xxx, CAxxxx) | Waiting for release | T1 ✅ T2 ✅ | [244-hidden-analyzer-diagnostics-plan.md](244-hidden-analyzer-diagnostics-plan.md) |
| [#245](https://github.com/DutchJaFO/Quotinator/issues/245) | Sources.Date stays NULL when a Source's only sources[] entry omits date (gap in #191's scope) | Waiting for release | T1 ✅ T2 ✅ | [245-source-date-backfill-plan.md](245-source-date-backfill-plan.md) |
| [#263](https://github.com/DutchJaFO/Quotinator/issues/263) | Make recovering from critical startup/database errors easier (Blazor UI, HA add-on experience) | Waiting for release | T1 ✅ T2 ✅ | [263-startup-ux-plan.md](263-startup-ux-plan.md) |
| [#264](https://github.com/DutchJaFO/Quotinator/issues/264) | Clarify ADR 016's Dto boundary for DB-stored JSON and dual-boundary classes | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — decision recorded in [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md)'s "Revision — issue #264" section |
| [#265](https://github.com/DutchJaFO/Quotinator/issues/265) | Admin audit endpoint returns AuditEntryEntity directly with no Response DTO layer | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — findings and recommendation recorded in the issue's own closing comment |
| [#267](https://github.com/DutchJaFO/Quotinator/issues/267) | Investigate using FileResource/ImportBatch history to avoid unconditional backup-before-seed on every startup | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — findings and recommendation recorded in the issue's own closing comment |
| [#269](https://github.com/DutchJaFO/Quotinator/issues/269) | Adopt a project-wide pattern for expensive logging arguments (CA1873) | Waiting for release | T1 ✅ T2 ✅ | [269-loggermessage-pattern-plan.md](269-loggermessage-pattern-plan.md) |
| [#271](https://github.com/DutchJaFO/Quotinator/issues/271) | Rename ActionPayload/ConverterOptions classes, add ImportActionFieldRow subclasses (ADR 016 revision) | Waiting for release | N/A | [271-actionpayload-converteroptions-rename-plan.md](271-actionpayload-converteroptions-rename-plan.md) |
| [#272](https://github.com/DutchJaFO/Quotinator/issues/272) | Add AuditEntryResponse/AuditChangeResponse DTOs — stop leaking SafeValue's raw/parsed wrapper over HTTP | Waiting for release | N/A | [272-audit-response-dto-plan.md](272-audit-response-dto-plan.md) |
| [#276](https://github.com/DutchJaFO/Quotinator/issues/276) | Startup backup safety-net improvements: correct backup gating + notification system (parent tracking issue for #277/#278) | Planning | N/A (parent — no code of its own) | No plan doc — tracking issue only |
| [#277](https://github.com/DutchJaFO/Quotinator/issues/277) | Gate startup backups on each action's own real-work signal, not an inferred flag; add a storage pre-flight check | Planning | Not yet determined | No plan doc yet |
| [#278](https://github.com/DutchJaFO/Quotinator/issues/278) | Add a startup notification system surfaced in the #263 modals | Waiting for release | T1 ✅ T2 ✅ | [278-startup-notification-system-plan.md](278-startup-notification-system-plan.md) |
| [#279](https://github.com/DutchJaFO/Quotinator/issues/279) | Standardise endpoint naming (WithName/WithSummary) across CRUD and action endpoints — includes breaking operationId renames | Planning | T1, T2 | [279-endpoint-naming-standardization-plan.md](279-endpoint-naming-standardization-plan.md) |
| [#280](https://github.com/DutchJaFO/Quotinator/issues/280) | Show a startup "please wait" page while the database is created/updated/seeded, with progress if feasible | Planning | Not yet determined | No plan doc yet |

---

## Dependency map

**#227 is fully decomposed, not implemented directly (2026-08-01).** Its naming decision shipped as
[ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)/
[ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md); the actual work
is six sub-issues — #251/#252 (FileResource/FileResourceLine, split out as unrelated undesigned
functionality), #253 (rename `Quotinator.Data`'s tables/entities), #254 (rename `Quotinator.Core`'s),
#255 (move enums to `Enums/` folders), #256 (`Response`/`Dto`/class-suffix fixes). #253/#254 are the
two that actually touch table names, entities, and SQL — every dependency below that used to point at
#227 now points at them.

**#253/#254 block the issues that actually touch renamed tables, entities, or SQL** — #249, #156, and
#245. Code written against today's names in any of those three would need to be rewritten the moment
#253/#254 land, so they wait. This is an implementation-order dependency (#253/#254 must actually be
merged first for these three), not just a release gate. #255/#256 (enum moves, Response/Dto fixes)
carry no such dependency — zero schema impact, nothing else needs to wait on them.

**#222 turned out independent too, on closer inspection while planning it (2026-08-01).** Its own SQL
changes touch only column aliases (`q.QuoteText`, `s.Title`, `c.Name`, `p.Name`) inside
`Sql.SearchField`, never the table names themselves — those live in a different query
(`Sql.Quotes.SelectSearch`'s `FROM`/`JOIN`) that #222 doesn't touch. No entity class involved is
`[Table]`-attributed either. Planned and implemented ahead of #253/#254 like #232/#236 before it.

**#244 should follow #253/#254 for conflict-avoidance, not a hard dependency** — its mechanical
IDE0xxx/CAxxxx fixes touch the same class definitions #253/#254 rename; doing #244 first would mean
#253/#254 have to re-touch already-modified files, the same reasoning that sequenced #197 early for a
different set of files.

**#232 and #236 turned out not to depend on #227 (or its sub-issues) at all** — #232 (Docker base
image research) and #236 (release-workflow process/CI change) touch neither the database schema nor
any renamed class, and both were confirmed independent and completed/planned out of order from the
original blanket claim here. Corrected 2026-08-01 after actually working both.

**#267 depends on #251 (done) for the data it investigates using** — it explicitly proposes using
`Import_FileResource`/`Import_FileResourceBatch` (#251's own tables) to build a real "is there actually
work to do" signal for the pre-seed backup. Not a hard implementation-order block (#251 already shipped,
so #267 is unblocked and workable now), just the reason it couldn't have been filed before #251 existed.

Separately, a release-level gate: **#249 must ship in the same release as #156**, per
[ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) — not
an implementation-order dependency between the two of them, so #249 and #156 can still be built/merged
in either order *relative to each other*, as long as both land after #253/#254 and both ship in the
same release.

**#279 depends on #278 (implementation-order, not just release-gate) — per explicit developer
direction (2026-08-09).** #279's endpoint-naming standardisation includes breaking `WithName`
(OpenAPI `operationId`) renames; #278's startup notification system is the intended vehicle for
surfacing that kind of change to operators. #279 must not land before #278 exists to announce it.

**#280 depends on #278 (implementation-order) — per explicit developer direction (2026-08-09).**
#280's bonus "current startup phase" progress display is meant to reuse #278's notification/status
infrastructure rather than invent a second, parallel status-reporting mechanism; #280 must not land
before #278 exists to build on.

None of the remaining issues block each other beyond these relationships.

---

## Order of operations

1. **#166** — HA add-on stable/beta split (largest feature in this batch; started first at the
   maintainer's explicit direction, ahead of the smaller items below)
2. **#197** — .editorconfig / MSTest analyzer severities — broad mechanical change across 76 call
   sites in 25 test files; doing this early avoids conflicts with tests added by later issues
3. **#159** — ADR: repository-is-C#-only tooling policy (docs-only; shipped in v1.8.0, closed out
   2026-07-31 after this milestone's own review caught it was never marked released)
4. **#146** — Audit memory-only conventions → CLAUDE.md/docs (docs-only; six genuine gaps found and
   migrated 2026-07-31 — rate limiting's undocumented Admin concurrency-1 policy, GUID hex-letter test
   fixtures, DB-integration-test requirement for seeder code, the Ruleset BEHIND merge gotcha, no
   smoke-tests-on-dev-db, and import-file minimalism)
5. **#208** — Issue-creation process: label + milestone in the same draft pass (process/docs-only)
6. **#150** — Audit enum-valued POCO properties for missing CHECK constraints (one known gap already found)
7. **#151** — System_-prefixed audit-trail table purge-on-Reset policy decision (docs-only; resolved
   2026-08-01 via [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) — dangling references are
   permanent by design; filed #249 as a release gate on #156)
8. **#227** — Domain-prefixed table naming + class-naming/enum-placement conventions (moved to first
   position among remaining work, 2026-08-01, per explicit developer direction — its rename touches
   every table, entity/response/DTO class, and SQL query string, so every issue below would otherwise
   be written against names it immediately invalidates. Naming decided via
   [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)/
   [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md), then fully
   decomposed the same day into #251/#252/#253/#254/#255/#256 rather than implemented directly — see
   the [reference doc](227-domain-prefixed-naming-implementation-plan.md) for the full mapping)
9. **#253** — Rename `Quotinator.Data`-owned tables/entities (migration, baseline, `Sql.cs`,
   `GetUserTables` pattern) — inherits #227's "runs first" position
10. **#254** — Rename `Quotinator.Core`-owned tables/entities (migration, baseline, `Sql.cs`) — same
    position as #253; sequencing note (not a hard dependency) in the Dependency map above
11. **#255** — Move enums to `Enums/` folders — independent, no urgency, can slot in anywhere
12. **#256** — Fix `Response`/`Dto`/class-suffix violations — independent, no urgency, can slot in
    anywhere
13. **#251** — Design + implement `FileResource`/`FileResourceLine` — independent of the rename
    entirely (split out as unrelated undesigned functionality), no urgency
14. **#252** — Confirm whether #153's `SourceFileOverride` should be superseded — depends on #251
15. **#249** — Audit-trail bulk export + date-range endpoints, plus config-driven auto-purge of
    conflict-resolution data (`Import_Action`) once a batch has nothing pending — redesigned 2026-08-05
    from the original "export to a dedicated folder before Reset" framing (must ship in the same
    release as #156, not necessarily built before it; filed while planning #151; targets the
    post-#253/#254 table names)
16. **#156** — Reset: baseline script instead of drop-all-user-tables + replay (also targets the
    post-#253/#254 table names, and its own `GetUserTables` exclusion-pattern question per ADR 015's
    Consequences is easier to resolve once #253 has landed)
17. **#222** — Unicode-aware case-insensitive LIKE matching (real correctness bug, medium effort;
    planned 2026-08-01 — confirmed independent of #227, worked out of order like #232/#236; opt-in
    `Quotinator:UnicodeAwareSearch` flag (default off) using a custom `CreateFunction`-registered
    `UNICODE_CONTAINS`, not `LIKE`/ICU, see
    [222-unicode-like-matching-plan.md](222-unicode-like-matching-plan.md))
18. **#148** — OpenAPI: document response models for quote/admin endpoints
19. **#178** — Changelog: optional one-line quote per release entry
20. **#232** — Docker Scout OS vulnerability research (resolved 2026-08-01: a fresh `--no-cache`
    rebuild alone dropped 23 reported vulnerabilities to 8, all in the base image, all currently
    unfixable upstream; documented as accepted residual risk in `docs/security/README.md`; chiseled
    base image evaluated and rejected for now — no code change)
21. **#236** — Release workflow config/image timing race (discovered live during #166's T3
    verification, 2026-07-30; appended here rather than reordered in since it has no dependency on
    the others; planned 2026-08-01 — split the version-bump PR into a before-tag PR and an after-tag,
    workflow-confirmed-green follow-up PR, see
    [236-release-workflow-version-race-plan.md](236-release-workflow-version-race-plan.md))
22. **#244** — Hidden IDE0xxx/CAxxxx analyzer diagnostics (discovered while reviewing #197's fix,
    2026-07-31; appended here rather than reordered in since it has no dependency on the others)
23. **#245** — Sources.Date gap for date-less explicit `sources[]` entries (discovered during the
    full T2 smoke-test pass, 2026-07-31; appended here rather than reordered in since it has no
    dependency on the others)
24. **#250** — Periodic Docker Scout re-scan added to the T2 smoke-test checklist (filed while closing
    #232, 2026-08-01; appended here rather than reordered in since it has no dependency on the others)
25. **#263** — Make recovering from critical startup/database errors easier — Blazor UI banner, HA
    add-on experience (filed 2026-08-02 during #254's own T1 pass, once `DatabaseHealthState`/
    `DatabaseHealthGateMiddleware`'s own first-pass safety net existed to build on; appended here
    rather than reordered in since it has no dependency on the others)
26. **#264** — Research: does ADR 016's `Dto` boundary extend to DB-stored JSON and dual-boundary
    classes (filed 2026-08-02 during #256's own pre-implementation review; appended here rather than
    reordered in since it has no dependency on the others)
27. **#265** — Research: should `GET /admin/audit` stop returning `AuditEntryEntity` directly (filed
    2026-08-02 during the same #256 review as #264; appended here rather than reordered in since it
    has no dependency on the others)
28. **#267** — Investigate using FileResource/ImportBatch history to reduce the unconditional pre-seed
    backup (filed 2026-08-04 during a T1 pass, once #251's own history tables existed to investigate
    using; appended here rather than reordered in since it has no hard dependency on the others)
29. **#269** — Adopt a project-wide pattern for expensive logging arguments, CA1873 (split out of
    #244's own Step 10, 2026-08-08; appended here rather than reordered in since it has no dependency
    on the others)
30. **#271** — Rename `*ActionPayload`/converter-options classes to `Dto`, add `ImportActionFieldRow`
    response/DTO subclasses (split out of #264's own investigation once its ADR 016 revision landed,
    2026-08-08; appended here rather than reordered in since it has no dependency on the others)
31. **#272** — Add `AuditEntryResponse`/`AuditChangeResponse` DTOs, unwrapping `SafeValue<T>`'s
    `raw`/`parsed`/`isValid` shape without dropping any `RecordBase` column (split out of #265's own
    investigation, 2026-08-08; appended here rather than reordered in since it has no dependency on
    the others)
32. **#276** — Parent tracking issue for #277/#278, split out of #267's own investigation once the
    corrected per-action backup-gating model was confirmed against the actual code, 2026-08-08;
    appended here rather than reordered in since it has no dependency on the others
33. **#277** — Gate startup backups on each action's own real-work signal (migrate's existing
    `dataPending`/`consumerPending` gate, content-seed's own count-gate), not an inferred cross-cutting
    flag; add a storage pre-flight check (default 1 GB) and distinguishable per-step failure messages
34. **#278** — Add a startup notification table (`Information`/`Warning`/`Error`/`Success`/
    `ActionRequired` types) read at startup and surfaced in #263's `StartupSuccessModal`/
    `StartupErrorModal` — independent of #277, no hard dependency either direction
35. **#279** — Standardise endpoint naming (`WithName`/`WithSummary`) across CRUD and action
    endpoints, absorbing #269's own `WithName`/log-tag duplication finding; depends on #278 landing
    first so its breaking `operationId` renames can be announced via the new notification system
    (2026-08-09)
36. **#280** — Show a startup "please wait" page while the database is created/updated/seeded, with
    progress if feasible (split out of #269's own T1 verification, 2026-08-09); depends on #278
    landing first so its bonus progress display can reuse the new notification/status infrastructure

---

## PR merge plan

#166, #197, #159, and #146 each used their own branch/PR, merged independently as each completed its
own T1/T2 verification. From #208 onward, the remaining small/independent issues share
`feature/v1.8.0-maintenance-batch` and merge together — see the branching-policy note above for why.
