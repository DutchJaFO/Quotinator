# Notification system — Milestone Overview

**GitHub milestone:** [#14](https://github.com/DutchJaFO/Quotinator/milestone/14)
**Branch:** `feature/notification-system`
**Status:** In progress

---

## Description

Give the frontend user visibility into what's currently only reachable via the container log or curl:
what a reseed actually did per file, when a reseed leaves conflicts needing review, when new source
content is available to pick up, and what changed after an upgrade. v1.8.0 shipped a basic notification
mechanism (#278); this milestone makes it complete and useful for the persisted notifications the
migration, reset, reseed and import paths need.

---

## Verification tier definitions

| Tier | Environment | What it catches |
|------|-------------|-----------------|
| **T1 — VS/local** | Visual Studio on Windows | Razor runtime errors (not caught by `dotnet build`), Blazor circuit startup, UI rendering, manual API interaction |
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
| [#312](https://github.com/DutchJaFO/Quotinator/issues/312) | Notification schema: title/body, typed metadata, optional expiry, and app-version provenance | Waiting for release | T1 ✅ T2 ✅ | [312-notification-schema-foundation-plan.md](312-notification-schema-foundation-plan.md) |
| [#313](https://github.com/DutchJaFO/Quotinator/issues/313) | Api tests can silently assert against the startup wait page instead of the endpoint under test | Waiting for release | T1 ✅ T2 ⬜ | [313-api-test-startup-race-plan.md](313-api-test-startup-race-plan.md) |
| [#83](https://github.com/DutchJaFO/Quotinator/issues/83) | Research: notification system design | Waiting for release | T1 ✅ T2 ⬜ T3 ⬜ | [83-notification-system-design-research-plan.md](83-notification-system-design-research-plan.md) |
| [#81](https://github.com/DutchJaFO/Quotinator/issues/81) | Startup notification: import warnings and what's new after upgrade | Waiting for release | T1 ✅ T2 ✅ | [81-startup-whats-new-notification-plan.md](81-startup-whats-new-notification-plan.md) |
| [#302](https://github.com/DutchJaFO/Quotinator/issues/302) | Notification: confirm files that reseed cleanly with no review needed | In progress | T1 ⬜ T2 ⬜ | [302-clean-reseed-confirmation-notification-plan.md](302-clean-reseed-confirmation-notification-plan.md) |
| [#303](https://github.com/DutchJaFO/Quotinator/issues/303) | Notification + minimal review page: alert when a reseed leaves import actions pending review | Waiting for release | T1 ⬜ T2 ✅ | [303-pending-review-alert-and-review-page-plan.md](303-pending-review-alert-and-review-page-plan.md) |
| [#304](https://github.com/DutchJaFO/Quotinator/issues/304) | Notification + action: let the user trigger a reseed (content changed upstream, or after a Reset) | Waiting for release | T1 ✅ T2 ✅ | [304-reseed-notification-action-plan.md](304-reseed-notification-action-plan.md) |
| [#307](https://github.com/DutchJaFO/Quotinator/issues/307) | Changelog highlights: mark specific entries as notification-worthy | Waiting for release | T1 ✅ T2 ✅ | [307-changelog-notification-audience-key-plan.md](307-changelog-notification-audience-key-plan.md) |
| [#308](https://github.com/DutchJaFO/Quotinator/issues/308) | Notification: multi-line/rich message layout | Waiting for release | T1 ⬜ T2 ✅ | [308-notification-rich-layout-plan.md](308-notification-rich-layout-plan.md) |
| [#309](https://github.com/DutchJaFO/Quotinator/issues/309) | Move changelog content to database-backed System_Changelog table | Waiting for release | T1 ✅ T2 ✅ | [309-system-changelog-table-plan.md](309-system-changelog-table-plan.md) |
| [#305](https://github.com/DutchJaFO/Quotinator/issues/305) | Database integrity check: verify all expected tables exist at startup, not just row counts | Planning | T1 ⬜ T2 ⬜ | [305-database-integrity-check-plan.md](305-database-integrity-check-plan.md) |
| [#306](https://github.com/DutchJaFO/Quotinator/issues/306) | Changelog: empty 'Unreleased' section renders on the About page after a release tag | Planning | T1 ⬜ T2 ⬜ | [306-empty-unreleased-section-plan.md](306-empty-unreleased-section-plan.md) |
| [#319](https://github.com/DutchJaFO/Quotinator/issues/319) | Notification title and body are not translated | Waiting for release | T1 ✅ T2 ✅ | [319-notification-translations-plan.md](319-notification-translations-plan.md) |
| [#323](https://github.com/DutchJaFO/Quotinator/issues/323) | Source download: a stalled connection attempt outlives its request and fails every other source on the same host | Waiting for release | T1 ✅ T2 ✅ | [323-source-download-connection-stall-plan.md](323-source-download-connection-stall-plan.md) |
| [#324](https://github.com/DutchJaFO/Quotinator/issues/324) | Notification: report when a source update attempt fails | Planning | T1 ⬜ T2 ⬜ | [324-source-refresh-failure-notification-plan.md](324-source-refresh-failure-notification-plan.md) |
| [#325](https://github.com/DutchJaFO/Quotinator/issues/325) | Source download: no address-family fallback — a black-holed IPv6 path fails the download even though IPv4 works | Closed as not planned | T1 — T2 — | [325-address-family-fallback-plan.md](325-address-family-fallback-plan.md) |
| [#326](https://github.com/DutchJaFO/Quotinator/issues/326) | Startup crashes instead of degrading when the data directory is read-only and a migration is pending | Waiting for release | T1 ✅ T2 ✅ | [326-startup-degrades-on-unwritable-data-directory-plan.md](326-startup-degrades-on-unwritable-data-directory-plan.md) |
| [#327](https://github.com/DutchJaFO/Quotinator/issues/327) | Smoke tests: prove startup problems degrade rather than crash | In progress | T1 ⬜ T2 ⬜ | [327-startup-degradation-smoke-coverage-plan.md](327-startup-degradation-smoke-coverage-plan.md) |
| [#328](https://github.com/DutchJaFO/Quotinator/issues/328) | Smoke tests: verify bundled imports and endpoint behaviour against a real database | Planning | T1 ⬜ T2 ⬜ | [328-bundled-import-and-live-endpoint-coverage-plan.md](328-bundled-import-and-live-endpoint-coverage-plan.md) |
| [#329](https://github.com/DutchJaFO/Quotinator/issues/329) | Source refresh: no retry on a marginal connect, and sources download sequentially | Planning | T1 ⬜ T2 ⬜ | [329-source-refresh-retry-and-parallelism-plan.md](329-source-refresh-retry-and-parallelism-plan.md) |
| [#330](https://github.com/DutchJaFO/Quotinator/issues/330) | File metadata: sidecar and database record for every file we create or inspect | Planning | T1 ⬜ T2 ⬜ | [330-file-metadata-sidecar-and-record-plan.md](330-file-metadata-sidecar-and-record-plan.md) |
| [#331](https://github.com/DutchJaFO/Quotinator/issues/331) | Source refresh: conditional requests so an unchanged source is not re-downloaded | Planning | T1 ⬜ T2 ⬜ | [331-conditional-source-requests-plan.md](331-conditional-source-requests-plan.md) |
| [#339](https://github.com/DutchJaFO/Quotinator/issues/339) | Restructure the T2 suite into docs/automated-testing/, one document per test | In progress | T1 ⬜ T2 ✅ | [339-automated-testing-restructure-plan.md](339-automated-testing-restructure-plan.md) |
| [#348](https://github.com/DutchJaFO/Quotinator/issues/348) | Reset returns an unhandled 500 when no backup can be taken, and the five backup failure causes are indistinguishable | Waiting for release | T1 ⬜ T2 ✅ | [348-backup-outcomes-and-refusal-plan.md](348-backup-outcomes-and-refusal-plan.md) |
| [#349](https://github.com/DutchJaFO/Quotinator/issues/349) | Admin endpoints to list, delete and report status for database backups | Waiting for release | T1 ✅ T2 ✅ | [349-backup-management-endpoints-plan.md](349-backup-management-endpoints-plan.md) |
| [#350](https://github.com/DutchJaFO/Quotinator/issues/350) | A schema-version overshoot runs healthy instead of degrading, on a schema whose shape is unknown | Planning | T1 ⬜ T2 ⬜ | [350-overshoot-must-degrade-plan.md](350-overshoot-must-degrade-plan.md) |
| [#351](https://github.com/DutchJaFO/Quotinator/issues/351) | AuditOperation is a string-constant set where the project's convention is an enum | Planning | T1 ⬜ T2 ⬜ | [351-audit-operation-enum-plan.md](351-audit-operation-enum-plan.md) |
| [#352](https://github.com/DutchJaFO/Quotinator/issues/352) | Restore a stored backup, refusing one taken ahead of this build | Planning | T1 ⬜ T2 ⬜ | [352-restore-a-stored-backup-plan.md](352-restore-a-stored-backup-plan.md) |
| [#353](https://github.com/DutchJaFO/Quotinator/issues/353) | Upload a backup file | Planning | T1 ⬜ T2 ⬜ | [353-upload-a-backup-file-plan.md](353-upload-a-backup-file-plan.md) |
| [#360](https://github.com/DutchJaFO/Quotinator/issues/360) | Migration-generated identifiers are not valid UUIDs; route all id creation through one factory | Planning | T1 ⬜ T2 ⬜ | [360-guid-factory-plan.md](360-guid-factory-plan.md) |
| [#367](https://github.com/DutchJaFO/Quotinator/issues/367) | Notification actions give no feedback while they run | Waiting for release | T1 ✅ T2 ✅ | [367-executing-notification-state-plan.md](367-executing-notification-state-plan.md) |
| [#368](https://github.com/DutchJaFO/Quotinator/issues/368) | New import files are discovered but never imported, and nothing says so | Planning | T1 ⬜ T2 ⬜ | [368-unimported-files-are-discovered-but-never-imported-plan.md](368-unimported-files-are-discovered-but-never-imported-plan.md) |
| [#369](https://github.com/DutchJaFO/Quotinator/issues/369) | A review row whose batch is gone offers decisions that cannot be carried out | Planning | T1 ⬜ T2 ⬜ | [369-orphaned-review-rows-plan.md](369-orphaned-review-rows-plan.md) |
| [#370](https://github.com/DutchJaFO/Quotinator/issues/370) | An expected import conflict is signalled by throwing, once per conflicted row per render | Planning | T1 ⬜ T2 ⬜ | [370-conflict-signalled-by-throwing-plan.md](370-conflict-signalled-by-throwing-plan.md) |
| [#371](https://github.com/DutchJaFO/Quotinator/issues/371) | Notify that the database was created, and that migrations were applied | Planning | T1 ⬜ T2 ⬜ | — |
| [#372](https://github.com/DutchJaFO/Quotinator/issues/372) | Reseed should only import the designated files, not delete data first | In progress | T1 ⬜ T2 ⬜ | [372-reseed-does-not-delete-plan.md](372-reseed-does-not-delete-plan.md) |
| [#373](https://github.com/DutchJaFO/Quotinator/issues/373) | An import that re-states identical content reports it as modified | In progress | T1 ⬜ T2 ⬜ | [373-unchanged-is-not-modified-plan.md](373-unchanged-is-not-modified-plan.md) |
| [#374](https://github.com/DutchJaFO/Quotinator/issues/374) | A conflict rule cannot tell "already correct" from "cannot apply" | Planning | T1 ⬜ T2 ⬜ | [374-already-correct-is-not-cannot-apply-plan.md](374-already-correct-is-not-cannot-apply-plan.md) |
| [#375](https://github.com/DutchJaFO/Quotinator/issues/375) | A quote from a multi-season TV series cannot say which season it is from | Waiting for release | T1 ⬜ T2 ✅ | [375-season-between-series-and-source-plan.md](375-season-between-series-and-source-plan.md) |

---

## Dependency map

```
#312 ─── depends on #278; blocks #81, #302, #303, #304, #308 — Waiting for release
#83  ─── (none) — Waiting for release
#81  ─── depends on #278, #80, #309, #307, #312; soft-depends on #308 — Waiting for release
#304 ─── depends on #278, #156, #312, #319 — Waiting for release
#302 ─── depends on #278, #312 — In progress
#303 ─── depends on #278, #312 — Waiting for release
#307 ─── depends on #80; soft-depends on #309 — Waiting for release
#308 ─── depends on #278, #312; soft-depends on #302, #303, #304 — Waiting for release
#309 ─── (none) — Waiting for release
#305 ─── (none) — Planning
#306 ─── (none) — Planning
#319 ─── depends on #312; blocks #304, #302, #303, #308 — Waiting for release
#323 ─── (none) — Waiting for release
#324 ─── depends on #278, #312, #319; soft-consumes #329 — Planning
#325 ─── (none) — Closed as not planned
#326 ─── (none); blocks #327 — Waiting for release
#327 ─── depends on #326, #348 — In progress
#328 ─── (none) — Planning
#329 ─── blocks #324 — Planning
#330 ─── (none); blocks #331 — Planning
#331 ─── depends on #330 — Planning
#339 ─── depends on #347 (v1.9.0 milestone); blocks #327, #328 — In progress
#348 ─── (none); blocks #327 — Waiting for release
#349 ─── (none); soft-relates to #348 — Waiting for release
#350 ─── (none) — Planning
#352 ─── (none) — Planning
#360 ─── (none) — Planning
#353 ─── (none) — Planning
#351 ─── (none) — Planning
#313 ─── (none) — Waiting for release
#370 ─── (none) — Planning
#369 ─── depends on #303 — Planning
#372 ─── (none); blocks #302 — In progress
#373 ─── depends on #372; blocks #302 (via #372) — In progress
#375 ─── (none); blocks #374 — Waiting for release
#374 ─── depends on #375 — Planning
#367 ─── depends on #278, #312; blocks #308 — Waiting for release
#371 ─── (none) — Planning
#368 ─── depends on #303, #304 — Planning
```

---

## Order of operations

| # | Issue | Reason |
|---|-------|--------|
| 1 | **#313** ✅ | Waiting for release; sequenced first — test-harness reliability |
| 2 | **#323** ✅ | Waiting for release; independent |
| 3 | **#325** ⛔ | Closed as not planned |
| 4 | **#312** ✅ | Waiting for release; foundation for #81, #302, #303, #304, #308 |
| 5 | **#81** ✅ | Waiting for release |
| 6 | **#83** ✅ | Waiting for release |
| 7 | **#309** ✅ | Waiting for release |
| 8 | **#326** ✅ | Waiting for release |
| 9 | **#348** ✅ | Waiting for release — T1 outstanding |
| 10 | **#349** ✅ | Waiting for release |
| 11 | **#307** ✅ | Waiting for release |
| 12 | **#319** ✅ | Waiting for release; gateway for the producers below |
| 13 | **#304** ✅ | Waiting for release |
| 14 | **#302** 🚧 | In progress — blocked on #372 |
| 15 | **#303** ✅ | Waiting for release — T1 outstanding |
| 16 | **#367** ✅ | Waiting for release — moved up so #308 designs against the finished status set |
| 17 | **#308** ✅ | Waiting for release — T1 outstanding |
| 18 | **#375** ✅ | Waiting for release — T1 outstanding |
| 19 | **#374** | Planning — depends on #375 |
| 20 | **#373** 🚧 | In progress |
| 21 | **#372** 🚧 | In progress — #302 cannot finish until this lands |
| 22 | **#369** | Planning — depends on #303 |
| 23 | **#370** | Planning — sequenced with #369, same page |
| 24 | **#371** | Planning — before #351/#360, which each add migrations |
| 25 | **#350** | Planning |
| 26 | **#327** 🚧 | In progress — depends on #326 (done), #348 |
| 27 | **#328** | Planning |
| 28 | **#339** 🚧 | In progress — blocked on [#347](https://github.com/DutchJaFO/Quotinator/issues/347) in the **v1.9.0** milestone |
| 29 | **#329** | Planning — before #324, which consumes its statistics |
| 30 | **#330** | Planning — #331 depends on it |
| 31 | **#331** | Planning — depends on #330 |
| 32 | **#324** | Planning — after #329/#330/#331 |
| 33 | **#305** | Planning — independent |
| 34 | **#306** | Planning — independent |
| 35 | **#351** | Planning — independent, placed late |
| 36 | **#352** | Planning — after #349 |
| 37 | **#353** | Planning — after #352 |
| 38 | **#360** | Planning — before end-of-milestone migration consolidation |
| 39 | **#368** | Planning — depends on #303, #304 |

---

## PR merge plan

All thirty issues are self-contained on top of already-released infrastructure (#278, #80, #154,
#156) — none leave anything half-wired if merged independently, except #81 (which genuinely cannot
merge before #309 and #307), #319 (which cannot merge before #312), #331 (which cannot merge before
#330), #328 (which cannot merge before #339), and #327 (which cannot merge before #339 for its
structure, nor before #348 for the behaviour its corrupt- and truncated-database documents assert).
Those are real implementation-order dependencies, not just sequencing preferences.
Default assumption per `process.md`: the branch stays open until all issues in the milestone are done,
then one PR — no known reason to depart from that default.
