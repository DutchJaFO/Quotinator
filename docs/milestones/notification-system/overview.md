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
| [#81](https://github.com/DutchJaFO/Quotinator/issues/81) | Startup notification: what's new after upgrade | Waiting for release | T1 ✅ T2 ✅ | [81-startup-whats-new-notification-plan.md](81-startup-whats-new-notification-plan.md) |
| [#302](https://github.com/DutchJaFO/Quotinator/issues/302) | Notification: confirm files that reseed cleanly with no review needed | Planning | T1 ⬜ T2 ⬜ | [302-clean-reseed-confirmation-notification-plan.md](302-clean-reseed-confirmation-notification-plan.md) |
| [#303](https://github.com/DutchJaFO/Quotinator/issues/303) | Notification + minimal review page: alert when a reseed leaves import actions pending review | Planning | T1 ⬜ T2 ⬜ | [303-pending-review-alert-and-review-page-plan.md](303-pending-review-alert-and-review-page-plan.md) |
| [#304](https://github.com/DutchJaFO/Quotinator/issues/304) | Notification + action: let the user trigger a reseed (content changed upstream, or after a Reset) | Planning | T1 ⬜ T2 ⬜ | [304-reseed-notification-action-plan.md](304-reseed-notification-action-plan.md) |
| [#307](https://github.com/DutchJaFO/Quotinator/issues/307) | Changelog highlights: mark specific entries as notification-worthy | Waiting for release | T1 ✅ T2 ✅ | [307-changelog-notification-audience-key-plan.md](307-changelog-notification-audience-key-plan.md) |
| [#308](https://github.com/DutchJaFO/Quotinator/issues/308) | Notification: multi-line/rich message layout | Planning | T1 ⬜ T2 ⬜ | [308-notification-rich-layout-plan.md](308-notification-rich-layout-plan.md) |
| [#309](https://github.com/DutchJaFO/Quotinator/issues/309) | Move changelog content to database-backed System_Changelog table | Waiting for release | T1 ✅ T2 ✅ | [309-system-changelog-table-plan.md](309-system-changelog-table-plan.md) |
| [#305](https://github.com/DutchJaFO/Quotinator/issues/305) | Database integrity check: verify all expected tables exist at startup, not just row counts | Planning | T1 ⬜ T2 ⬜ | [305-database-integrity-check-plan.md](305-database-integrity-check-plan.md) |
| [#306](https://github.com/DutchJaFO/Quotinator/issues/306) | Bug: empty "Unreleased" section renders on the About page after a release tag | Planning | T1 ⬜ T2 ⬜ | [306-empty-unreleased-section-plan.md](306-empty-unreleased-section-plan.md) |
| [#319](https://github.com/DutchJaFO/Quotinator/issues/319) | Notification title and body are not translated | In progress | T1 ✅ T2 ⬜ | [319-notification-translations-plan.md](319-notification-translations-plan.md) |
| [#323](https://github.com/DutchJaFO/Quotinator/issues/323) | Source download: a stalled connection attempt outlives its request and fails every other source on the same host | Waiting for release | T1 ✅ T2 ✅ | [323-source-download-connection-stall-plan.md](323-source-download-connection-stall-plan.md) |
| [#324](https://github.com/DutchJaFO/Quotinator/issues/324) | Notification: report when a source update attempt fails | Planning | T1 ⬜ T2 ⬜ | [324-source-refresh-failure-notification-plan.md](324-source-refresh-failure-notification-plan.md) |
| [#325](https://github.com/DutchJaFO/Quotinator/issues/325) | Source download: no address-family fallback — a black-holed IPv6 path fails the download even though IPv4 works | Waiting for release | T1 ✅ T2 ✅ | [325-address-family-fallback-plan.md](325-address-family-fallback-plan.md) |
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
| [#351](https://github.com/DutchJaFO/Quotinator/issues/351) | `AuditOperation` is a string-constant set where the project's convention is an enum | Planning | T1 ⬜ T2 ⬜ | [351-audit-operation-enum-plan.md](351-audit-operation-enum-plan.md) |
| [#352](https://github.com/DutchJaFO/Quotinator/issues/352) | Restore a stored backup, refusing one taken ahead of this build | Planning | T1 ⬜ T2 ⬜ | [352-restore-a-stored-backup-plan.md](352-restore-a-stored-backup-plan.md) |
| [#353](https://github.com/DutchJaFO/Quotinator/issues/353) | Upload a backup file | Planning | T1 ⬜ T2 ⬜ | [353-upload-a-backup-file-plan.md](353-upload-a-backup-file-plan.md) |

---

## Dependency map

```
#312 ─── depends on #278 (the shipped mechanism it reshapes). Blocks #81, #302, #303, #304, #308 —
         every one of them either writes or renders a notification. Also absorbs the "relocate the
         dedupe helper into a project Quotinator.Core can reach" decision that #302, #303 and #304
         each separately defer to their own planning phase, and extends #81's own System_AppVersion
         table to an append-only Application/Version history so a provenance FK stays frozen
#83  ─── (none) — narrowed scope; last open question is a live T3 confirmation, not a blocker for anything else
#81  ─── depends on #278 (notification mechanism), #80 (IChangelogService) — both done, released;
         depends on #309 (hard — needs System_Changelog to be queryable), #307 (hard — cannot
         implement without the flagged-highlight field), and #312 (hard — its producer moves off the
         message-prefix dedupe onto typed metadata, and its own table changes shape underneath it);
         soft-depends on #308 (renders better once it lands, not blocked by it)
#304 ─── depends on #278 (notification mechanism), #156 (Reset no longer auto-seeds, which is
         exactly the gap it fills for the post-Reset trigger), and #312 (hard — needs the relocated
         dedupe helper for its Core-side trigger, and gains parameterised actions from #312's own
         metadata-aware INotificationActionExecutor)
#302 ─── depends on #278 and #312 (hard — the relocated dedupe helper, plus opt-in expiry, which
         removes the aging mechanism this issue currently assumes); writes from inside
         QuotinatorDatabaseInitializer's own seeding loop (#221's FileImportReport data, read from the
         same call site rather than after the fact)
#303 ─── depends on #278 and #312 (same as #302); same seeding-loop hook point as #302; its review page
         depends on the existing #154 staging model (ImportAction, IImportActionReader/Service) and the
         existing /import/actions REST endpoints — all already shipped, nothing new needed there
#307 ─── verified entirely by unit tests; it delivers a data contract and a lookup method and drives no
         UI of its own, so it carries no front-end verification row (2026-08-29). Whether a flagged
         highlight renders is #308's to assert, against #308's own code.
         Depends on #80 (extends its shipped schema/generator/models) and, per ADR 018, on #309's
         importer abstraction existing conceptually (not a hard build-order dependency — #307's schema
         field addition doesn't itself need System_Changelog to exist yet)
#308 ─── depends on #278 (extends its shipped NotificationTable component) and #312 (hard — renders
         the Title/Body split #312 introduces; #308's own body still claims a rendering-only fix with
         no storage change, which #312 supersedes). Also soft-depends on #302/#303/#304 — each adds a
         notification type with its own payload, and #308 defines the per-type layout for both the
         startup/popup dialogs and the notifications view, which cannot be settled before those types
         exist. Sequenced last for that reason (see Order of operations)
#309 ─── depends on ADR 005's revision and ADR 018 (design basis); Quotinator.Data takes a new
         dependency on Quotinator.Changelog as part of this issue; its fallback (when
         System_Changelog is missing/broken) reuses #293's exact narrow-exception-catch idiom and
         stays structurally compatible with #305's future general DB-integrity warning, without
         duplicating it
#305 ─── (none) — independent bug
#306 ─── (none) — independent bug
#319 ─── depends on #312 (hard — extends the table, the write API and the metadata contract it
         introduced; language is a first-class column, explicitly not a metadata payload field).
         Blocks #304, #302, #303 and #308 — each writes or renders notification text against a shape
         #319 changes. Migrates the three already-shipped producers (#279, #289, #81) as part of its
         own scope: the first two supply translations from i18ntext/UI.*.json, #81 from the
         per-language changelog files it already reads
#323 ─── (none) — independent bug, found live 2026-08-17 reading a startup log. Fixes the HTTP client
         registration only; does not change the import path. Not a blocker for #324: #324 reports a
         failed refresh whatever its cause, and this issue removes one specific cause
#324 ─── depends on #278 and #312 (the mechanism and the typed-metadata/opt-in-expiry shape) and on
         #319 (hard — it writes new user-facing text, and #319 changes the shape that text is stored
         in; building it first means building it twice). Consumes SourceRefreshResult.Failed, which
         nothing reads for user-facing purposes today. Soft-relates to #323 only in that #323 makes
         the failure it reports rarer
#325 ─── fix REVERTED 2026-08-20 as disproportionate; see its plan doc's "Reverted" section. What now
         carries it is #323's ConnectTimeout, raised here from 10s to 60s (request budget 30s → 90s to
         stay above it). The custom connect path is gone; the connector and its tests are kept in
         Quotinator.Data, unused, so the concepts need not be reinvented. Blocks nothing
#326 ─── (none) — independent bug, found while re-checking the smoke tests. Violates the never-crash
         rule: a read-only data directory plus a pending migration exits the process instead of
         degrading. Blocks #327, whose degradation scenarios include this one
#327 ─── depended on #326, which is now done. Now depends on #348: its corrupt- and truncated-database
         documents are written against the refusal behaviour that issue delivers, since a document
         asserting today's unhandled 500 would have to be rewritten a week later. Replaces the obsolete
         #293 reproduction, whose --read-only technique #294 made survivable. Inherits a corrected
         premise: #326 measured that WAL sidecar state, not a pending migration, decides whether a
         read-only mount degrades. #339 has already delivered its requirement 1 and its first scenario
         (the rewritten startup-and-degradation/05), so what remains is two documents, one overshoot
         document, and the in-process cases — which are written and green
#328 ─── (none) — covers two guarantees no unit test can reach: bundled content imports cleanly, and
         endpoints behave correctly against a real database rather than the stubs the endpoint tests
         deliberately use
#329 ─── depends on #323 and #325 only in that it revises their arrangement, not in build order: it
         revisits the ConnectTimeout #323 added and #325 raised to 60s, which is now the ENTIRE
         resilience of the download path — there is no retry and no connector behind it. Moved from
         13 to 11 on 2026-08-21 for that reason. Blocks #324 (hard for its multi-attempt
         reporting — #329 establishes the download statistics #324 becomes the first consumer of;
         #324's plain failure reporting does not need it). Adds the first NuGet dependency this
         milestone takes, Microsoft.Extensions.Http.Resilience
#330 ─── (none) — independent foundation. Establishes a per-file record (sidecar + Import_FileMetadata
         row, SHA-256 + MD5, first/last inspection) that the project has never had. References
         Import_FileResource (#251/#252) rather than extending it: that table is keyed by content
         version, this one by file identity. Blocks #331, which has nowhere to store HTTP validators
         without it
#331 ─── depends on #330 (hard — its ETag/Last-Modified are two more fields in #330's shape, and its
         staleness rule is #330's reconciliation rule). Reports into #329's statistics but does not
         depend on it; a 304 still needs the connection #329 makes reliable, so neither reduces the
         other's problem. Introduces SourceRefreshOutcome.Unchanged, which #324 may choose to surface
#339 ─── #347 (in the v1.9.0 milestone, not this one — the only cross-milestone dependency here, and it
         gates this milestone's close, not just this issue's). Restructures the T2 suite into
         docs/automated-testing/ and defines the run scopes.
         Blocks #327 and #328, which author their documents into that structure rather than into the
         monolith it removes. Revises ADR 010 in place (test-only scripts move to scripts/testing/)
         and resolves the live-only Definition-of-done gap in issues.md that #328 hits
#348 ─── (none) — found by #327 while measuring whether a stated recovery route can actually succeed.
         Blocks #327's two remaining degradation documents. A backup exists to make a startup or a
         destructive action safe, so a backup that cannot be taken becomes a reported failure with
         options rather than a silent skip (two variants today) or an unhandled 500 (three more). Also
         replaces the storage arithmetic, which decides a hard yes/no from a size SQLite's page-level
         copy does not guarantee, with a 90% operating quota plus a reserve to the absolute ceiling.
         Soft-relates to #349, which supplies the in-app remedy its messages point at — either order
#349 ─── depends on nothing; #348's remedy text names its endpoints once they exist and the manual
         option until then, so the two can land either way round. Adds list, delete, status, download
         and on-demand create under a new Backup tag — the first new endpoint group since ADR 020, so it
         is also that rule's first live application. Nothing here writes to the live database, which is
         what keeps it separable from #352 and #353 (both filed 2026-08-29 after the scope grew)
#350 ─── (none) — found by #327, which built the first coverage of the overshoot state and asserted the
         behaviour this issue reverses. Overturns #289's continue-and-notify design: an overshoot means
         this build does not know what the missing migrations did, so the schema's shape is unknown and
         the app must degrade rather than serve from it. Owns rewriting startup-and-degradation/06 and
         replacing the in-process test #327 added, so #327 does not touch either again. Names restoring
         an older backup as a second remedy alongside Reset, which #349 records as future work
#352 ─── (none) — the remedy #348's SourceUnreadable text and #350's overshoot text both name and
         neither can reach. Takes nothing from #349 but its `{name}` guard, so either order. Deliberately
         does *not* back up before restoring: that would bolt a second data-retention decision onto an
         endpoint with one job, feed the quota #348 refuses on, and duplicate a rollback SQLite already
         guarantees — #349's create endpoint is where an operator takes a restore point instead
#353 ─── (none) — the other half of #349's download: a restore point that can leave the container has to
         be able to come back. Its optional-restore flag was rejected for the same side-effect-policy
         reason as #352's pre-restore backup, which also removes any dependency on #352 in either
         direction
#351 ─── (none) — found while planning #349, which adds a thirteenth const string into the shape this
         issue replaces. Takes whatever members exist when it runs, so either order relative to #349.
         Its table rebuild is written against this milestone's own end-of-milestone migration
         consolidation, not as a permanent numbered step — the frozen boundary is the last released
         migration, which is what makes the conversion affordable here rather than deferred
#313 ─── (none) — independent test-harness bug, but sequenced first: until it landed, no test run in
         this milestone could be trusted, because Api tests asserted before the app finished starting
         (measured: 5 of 5). Blocks nothing structurally; blocked *confidence* in everything
```

None of #302/#303/#304 depend on each other for their own correctness, but #304 is what makes #302's
and #303's producers reachable from the UI for the first time (today a reseed only happens via
curl+admin key) — natural to build first, not a hard requirement. #302 and #303 share one new
`INotificationWriter` injection into `QuotinatorDatabaseInitializer` — whichever lands first does that
step, the other reuses it.

#83, #305, and #306 remain independent of everything else in this milestone.

---

## Order of operations

| # | Issue | Reason |
|---|-------|--------|
| 1 | **#313** ✅ | Done. Api tests were asserting before startup completed — measured at 5 of 5 runs, so every verification in this milestone was untrustworthy until it landed. Had to come first for that reason, not because of any dependency |
| 2 | **#323** ✅ | Independent bug; taken first by developer direction (2026-08-17) because it was found live and its fix is self-contained to the HTTP client registration. Blocks nothing |
| 3 | **#325** ✅ | Taken immediately after #323 as the same startup log's remaining half. Its fix was reverted on 2026-08-20 as disproportionate to a failure the application already handles by falling back to the local copy; #323's `ConnectTimeout`, raised to 60 s, carries it instead. See its plan doc's "Reverted" section |
| 4 | **#312** ✅ | Foundation: title/body, typed metadata, opt-in expiry, app-version provenance, and the relocated dedupe helper. Blocks #81, #302, #303, #304, #308 — building any of them first means building them twice |
| 5 | **#81** ✅ | What's-new-after-upgrade path; builds on #278's, #80's, #309's, #307's and #312's output |
| 6 | **#83** ✅ | Narrowed to a single live T3 confirmation; can run whenever the next beta add-on install happens, independently of everything else |
| 7 | **#309** ✅ | Done. T1 confirmed live (2026-08-19) surfaced four further defects — all fixed and verified; see steps 14–18 in its plan doc. T2 green the same day |
| 8 | **#326** ✅ | Done, `Waiting for release`. All 12 verification rows green including T1 and a T2 controlled pair. It also corrected its own premise, which #327 inherits: sidecar state decides whether a read-only mount degrades, **not** a pending migration |
| 9 | **#348** ✅ | Done, `Waiting for release` — T1 outstanding, the developer's own. Backup outcomes and refusal: five named obstacles, a reset that refuses rather than returning an unhandled 500, and a 90% operating quota with a reserve. Found by #327 while measuring whether a stated recovery route can actually succeed |
| 10 | **#349** ✅ | Done. Grew from three endpoints to five — list, delete, status, download, create — under their own `Backup` tag. Three defects were found by running it rather than by unit tests: an unhandled 500 on delete against a read-only mount, another on download caused by a pooled connection holding every backup file open, and no logging at all |
| 11 | **#307** ✅ | Done. All eight verification rows are green: its two documentation-confirmation rows became an assertion in `ChangelogSchemaTests` rather than a human read, and it carries no front-end row — it delivers a data contract and a lookup method, with the rendering of a flagged highlight verified by #308 against #308's own code |
| 12 | **#319** | Translated title/body. The gateway to the producers below: every one of them writes new user-facing text, and building any first means building the text twice |
| 13 | **#304** | Gives the reseed action a Blazor-reachable entry point for the first time; #302 and #303 below become observable through that path |
| 14 | **#302** | Writes from inside the seeding loop (see Dependency map); no dependency on the review page below |
| 15 | **#303** | Same hook point as #302; adds the one piece of new UI this milestone needs, explicitly scoped smaller than #66's own future side-by-side diff view |
| 16 | **#308** | Per-type layout across both surfaces. Last of the cluster, because it cannot settle those layouts before the producers above exist — and it owns proving that a flagged changelog highlight actually renders, which its own background already names |
| 17 | **#350** | Overshoot must degrade, not run healthy. Changes application behaviour, and #327's overshoot document is rewritten against the result. Reverses #289's shipped continue-and-notify design |
| 18 | **#327** | Rewrites the degradation smoke coverage around the never-crash feature. Unblocked: #326 is done, #348's refusal behaviour is in, and #339 has already delivered the structure these documents are authored into. The overshoot document and its in-process test belong to #350 |
| 19 | **#328** | Bundled-import and live-endpoint smoke coverage; authors into the same delivered structure |
| 20 | **#339** | **Moved down from 9 (2026-08-29).** Its structure is delivered and in use — what remains is one verification row, "every document can distinguish the feature working from the feature broken", blocked on [#347](https://github.com/DutchJaFO/Quotinator/issues/347) in the **v1.9.0** milestone. Leaving it at 9 made the sequence unworkable, since nothing after it could proceed while it waited on another milestone. Running it after #327/#328 is also better than before: those two add documents, and this row validates every document there is |
| 21 | **#329** | Retry and parallelism for source downloads. Before #324, which consumes its statistics |
| 22 | **#330** | File metadata foundation — sidecar + `Import_FileMetadata`. #331 below cannot start without it. Also owns giving a *backup* file its own record, which #349 deferred here rather than extending the audit schema |
| 23 | **#331** | Conditional requests, storing validators in #330's shape. Lands before #324 so the source-download subsystem is finished before anything reports on it |
| 24 | **#324** | The one notification issue that cannot join the cluster above: it reports on the source-download subsystem and consumes #329's statistics, so it stays after them and is written once rather than revised as #329/#330/#331 land |
| 25 | **#305** | Independent bug; can slot in anywhere |
| 26 | **#306** | Independent bug; can slot in anywhere |
| 27 | **#351** | `AuditOperation` to an enum with its CHECK constraint. Independent of everything above and slottable anywhere; placed last so #349's own new member is already in place when the conversion runs, and so its table rebuild is written with this milestone's full migration set visible |
| 28 | **#352** | Restore a stored backup. After #349, whose `{name}` guard and create endpoint it relies on being there — not for compilation, but so the remedy text and the operator's loop are written once |
| 29 | **#353** | Upload a backup file. Last of the backup cluster: it is the only endpoint that accepts an arbitrary file, and writing it after restore exists means its validation is written against a real consumer rather than a hypothetical one |

**Notifications come first from position 11 (developer direction, 2026-08-29).** The safe-start cluster
(#326, #348, #349) and the backup endpoints grew large enough to crowd out the milestone's own subject:
every notification producer was still unstarted at position 21 or below.

**#319 leads because every producer after it writes user-facing text**, and #308 comes before #307
because a notification feature is only verified by being seen: #307's flagged highlights prove nothing
until something renders them. Only #324 sits outside the cluster, because it reports on statistics #329
has not established yet.

**#339's blocker reaches past this issue.** Per the PR merge plan below, the branch stays open until every
issue in the milestone is done — so #347, in the v1.9.0 milestone, gates this milestone's close and not
only #339's. That is a cross-milestone dependency worth watching rather than discovering at close time.

---

## PR merge plan

All twenty-nine issues are self-contained on top of already-released infrastructure (#278, #80, #154,
#156) — none leave anything half-wired if merged independently, except #81 (which genuinely cannot
merge before #309 and #307), #319 (which cannot merge before #312), #331 (which cannot merge before
#330), #328 (which cannot merge before #339), and #327 (which cannot merge before #339 for its
structure, nor before #348 for the behaviour its corrupt- and truncated-database documents assert).
Those are real implementation-order dependencies, not just sequencing preferences.
Default assumption per `process.md`: the branch stays open until all issues in the milestone are done,
then one PR — no known reason to depart from that default.
