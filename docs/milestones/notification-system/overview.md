# Notification system — Milestone Overview

**GitHub milestone:** [#14](https://github.com/DutchJaFO/Quotinator/milestone/14)
**Branch:** `feature/notification-system`
**Status:** In progress

---

## Description

Give the frontend user visibility into what's currently only reachable via the container log or curl:
what a reseed actually did per file, when a reseed leaves conflicts needing review, when new source
content is available to pick up, and what changed after an upgrade. #83 was filed to research the
generic notification design before any implementation began; #81 was the original concrete feature
proposal.

**Major scope finding from this milestone's own planning pass (2026-08-12):** issue [#278](https://github.com/DutchJaFO/Quotinator/issues/278),
shipped in the already-released v1.8.0 maintenance milestone, built a complete generic notification
mechanism (`System_Notification` table, `NotificationType`, dismiss lifecycle with expiry,
`INotificationReader`/`Writer`, REST endpoints, `NotificationTable`/`NotificationSummary` components
wired into the startup modals, a `/notifications` page) — without #83 ever having run. #278 was scoped
and built independently, from #276/#267, with no visibility into this milestone. See #83's and #81's own
plan docs for the full cross-check and the resulting scope narrowing, confirmed with the developer
2026-08-12.

**Second scope revision, same session (2026-08-12):** #81's own "import warnings" path (as originally
written) predated this project's finished staging-import model (#154's `Pending`/`Blocked`/`Stale`
`ImportAction` states, declarative conflict rules, the `/import/actions` REST surface). The developer
redirected the milestone toward three concrete, infrastructure-grounded producers instead of the vaguer
"import diagnostics" issue originally drafted here: a success notification per file that reseeds
cleanly, an alert notification (plus a minimal in-app review/decide page) for files that leave conflicts
needing a decision, and a notification-gated action to trigger a reseed at all from the UI — reseeding
must never happen automatically in the background. See each new issue's own body for the full design
grounding (call sites, existing types, existing endpoints).

**Third scope revision, same session (2026-08-12):** two further developer-raised design gaps landed
before #302/#303/#304 had any code:

1. **Notifications need better layout than one plain line.** #278's `NotificationTable` has no
   line-break handling and every producer so far writes a single sentence — but #81's what's-new
   notification will often have multiple highlights worth showing. New issue, this milestone (see Issue
   List) — `#308`.
2. **Not every changelog highlight should become a notification.** #81's original design joined every
   `Highlights` entry into one message; the developer wants specific highlights flaggable as
   notification-worthy instead. This touches shipped #80 infrastructure (schema, generator, C# models),
   so it's its own issue rather than folded into #81 — `#307`, a hard dependency for
   #81's own producer.
3. **"A system of adding notifications without writing database updates — maybe use the import
   system."** On inspection, this cashes out concretely for #302/#303/#304: their notification writes
   move from a post-hoc read of `LastSeedReport`/`SourceCacheResolution` in `AdminEndpoints.cs`/
   `Program.cs` into `QuotinatorDatabaseInitializer`'s own per-file seeding loop and
   `OnInitialisedAsync` — the exact same transactional machinery that already creates `ImportBatch`/
   `ImportAction` rows, rather than a separate bolt-on call site. #304's post-Reset trigger is the one
   exception — Reset isn't an import operation, so that trigger stays at the endpoint-handler level.
   This also surfaced a real dependency-direction gap: the existing dedupe-write helper
   (`NotificationSeeding.SeedOnceAsync`) lives in `Quotinator.Api`, unreachable from
   `Quotinator.Core` where the seeding loop lives — relocating a shared version of it is now part of
   #302's own scope. #302/#303/#304's bodies were revised in place to reflect this (not closed and
   refiled — none had any code yet).

A separate, unrelated process gap was also found and fixed the same session: the v1.8.0 maintenance
milestone had closed with zero open issues and no replacement was ever opened, leaving no current
maintenance milestone at all. Opened **v1.9.0** to close that gap generally; the two concrete bugs found
live this session (#305, #306) were assigned to *this* milestone instead, since both surfaced directly
from notification-system code paths.

**Fourth scope revision, same session (2026-08-12):** two ADRs were written to settle where this
milestone's remaining work belongs before designing it further —
[ADR 018](../architecture-decisions/018-system-content-in-quotinator-data.md) (system-level content —
notifications, changelog, future `Genre` — is `Quotinator.Data`-owned by default) and a revision to
[ADR 005](../architecture-decisions/005-quotinator-changelog-project-scope.md) (resolves its own
long-open "where do changelog JSON files live" question: a new `System_Changelog` table, refreshed at
startup from relocated JSON files). #309 implements that resolution and is a hard prerequisite for #307
and #81. #310 (Genre-as-table) is filed as a placeholder in v1.9.0, not built by this milestone.

**Fifth scope revision (2026-08-15) — the milestone's own goal, restated by the developer:** v1.8.0
shipped a *basic* notification system; this milestone is about making it **complete and useful**, scoped
specifically to the persisted notifications the database migration, reset, reseed and import paths need.
That reframing changed what counts as fixed: #278's schema (one flat `Message`, always-on expiry, no
structured metadata, no app-version linkage) had been treated as a constraint to build around, and is
instead the milestone's real bottleneck. #312 was filed to own that foundation and now leads the order of
operations.

Concrete requirements settled in the same session, all folded into #312:

- Notifications carry a **title and body**, not one flat string.
- A **typed metadata** column (free-form JSON plus a `MetadataKind` discriminator, so a consumer can tell
  what shape the payload is) — deliberately independent of `Type`, which is severity. Metadata also
  carries **parameters for a notification's associated action**, which today cannot be parameterised at
  all.
- **Expiry only when there is a real need** — no more silently expiring every notification on a timer.
- A table recording the **application name and version** (separate columns, never concatenated) used to
  access the database, kept as an append-only history; each notification records **the app version that
  added it**.
- Startup-dialog notifications are about **application state** — migrations executed, database health,
  files imported at startup.
- **Not every notification has to persist.** Transient notifications (progress for long-running
  UI-triggered tasks) are explicitly a later milestone; #312's only obligation is not to preclude them.

**Sixth scope revision (2026-08-16):** #312's own T1 pass found that notification title and body are
never translated — the `/notifications` page and startup popup render their chrome in Dutch while the
notification's own text stays English. #319 was filed to close that: an `OriginalLanguage` column plus a
`System_NotificationTranslation` table, mirroring how `Quotinator_QuoteTranslation` already solves the
same write-once/read-later problem for quotes, with language as a first-class column rather than
metadata. It sequences **before** the remaining producers (developer direction, 2026-08-16) — each of
them writes new user-facing text, and building them against the untranslated shape means building them
twice.

Expect notifications and related items to keep evolving in future milestones — a shipped feature is
extensible and revisable, which is what new issues and milestones are for.

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
| [#313](https://github.com/DutchJaFO/Quotinator/issues/313) | Api tests can silently assert against the startup wait page instead of the endpoint under test | Waiting for release | N/A (test harness + docs only — no src/ change) | [313-api-test-startup-race-plan.md](313-api-test-startup-race-plan.md) |
| [#83](https://github.com/DutchJaFO/Quotinator/issues/83) | Research: notification system design | Waiting for release | T3 ⬜ (live confirmation only, no other tier applies) | [83-notification-system-design-research-plan.md](83-notification-system-design-research-plan.md) |
| [#81](https://github.com/DutchJaFO/Quotinator/issues/81) | Startup notification: what's new after upgrade | Waiting for release | T1 ✅ T2 ✅ | [81-startup-whats-new-notification-plan.md](81-startup-whats-new-notification-plan.md) |
| [#302](https://github.com/DutchJaFO/Quotinator/issues/302) | Notification: confirm files that reseed cleanly with no review needed (revised — writes from inside the seeding loop) | Planning | TBD | No plan doc yet |
| [#303](https://github.com/DutchJaFO/Quotinator/issues/303) | Notification + minimal review page: alert when a reseed leaves import actions pending review (revised — writes from inside the seeding loop) | Planning | TBD | No plan doc yet |
| [#304](https://github.com/DutchJaFO/Quotinator/issues/304) | Notification + action: let the user trigger a reseed (content changed upstream, or after a Reset) (revised — content-change trigger moves into the seeding loop) | Planning | TBD | No plan doc yet |
| [#307](https://github.com/DutchJaFO/Quotinator/issues/307) | Changelog highlights: mark specific entries as notification-worthy | In progress | N/A (library code, no runtime path) | [307-changelog-notification-audience-key-plan.md](307-changelog-notification-audience-key-plan.md) |
| [#308](https://github.com/DutchJaFO/Quotinator/issues/308) | Notification: multi-line/rich message layout | Planning | TBD | No plan doc yet |
| [#309](https://github.com/DutchJaFO/Quotinator/issues/309) | Move changelog content to database-backed System_Changelog table | Waiting for release | T1 ✅ T2 ✅ | [309-system-changelog-table-plan.md](309-system-changelog-table-plan.md) |
| [#305](https://github.com/DutchJaFO/Quotinator/issues/305) | Database integrity check: verify all expected tables exist at startup, not just row counts | Planning | TBD | No plan doc yet |
| [#306](https://github.com/DutchJaFO/Quotinator/issues/306) | Bug: empty "Unreleased" section renders on the About page after a release tag | Planning | TBD | No plan doc yet |
| [#319](https://github.com/DutchJaFO/Quotinator/issues/319) | Notification title and body are not translated | Planning | T1 ⬜ T2 ⬜ | [319-notification-translations-plan.md](319-notification-translations-plan.md) |
| [#323](https://github.com/DutchJaFO/Quotinator/issues/323) | Source download: a stalled connection attempt outlives its request and fails every other source on the same host | Waiting for release | T2 ✅ | [323-source-download-connection-stall-plan.md](323-source-download-connection-stall-plan.md) |
| [#324](https://github.com/DutchJaFO/Quotinator/issues/324) | Notification: report when a source update attempt fails | Planning | T1 ⬜ T2 ⬜ | [324-source-refresh-failure-notification-plan.md](324-source-refresh-failure-notification-plan.md) |
| [#325](https://github.com/DutchJaFO/Quotinator/issues/325) | Source download: no address-family fallback — a black-holed IPv6 path fails the download even though IPv4 works (fix reverted as over-engineered; a longer connect budget carries it) | Waiting for release | T1 ✅ T2 ✅ | [325-address-family-fallback-plan.md](325-address-family-fallback-plan.md) |
| [#326](https://github.com/DutchJaFO/Quotinator/issues/326) | Startup crashes instead of degrading when the data directory is read-only and a migration is pending | Waiting for release | T1 ✅ T2 ✅ | [326-startup-degrades-on-unwritable-data-directory-plan.md](326-startup-degrades-on-unwritable-data-directory-plan.md) |
| [#327](https://github.com/DutchJaFO/Quotinator/issues/327) | Smoke tests: prove startup problems degrade rather than crash | Planning | T2 ⬜ | No plan doc yet |
| [#328](https://github.com/DutchJaFO/Quotinator/issues/328) | Smoke tests: verify bundled imports and endpoint behaviour against a real database | Planning | T2 ⬜ | No plan doc yet |
| [#329](https://github.com/DutchJaFO/Quotinator/issues/329) | Source refresh: no retry on a marginal connect, and sources download sequentially | Planning | T1 ⬜ T2 ⬜ | No plan doc yet |
| [#330](https://github.com/DutchJaFO/Quotinator/issues/330) | File metadata: sidecar and database record for every file we create or inspect | Planning | T1 ⬜ T2 ⬜ | No plan doc yet |
| [#331](https://github.com/DutchJaFO/Quotinator/issues/331) | Source refresh: conditional requests so an unchanged source is not re-downloaded | Planning | T1 ⬜ T2 ⬜ | No plan doc yet |

#302, #303, and #304 replace an earlier, stale "import diagnostics" issue drafted during this same
planning pass, before the developer redirected scope — see the Description above. #305–#309 were all
filed the same session, before any of them had code; #310 (Genre-as-table placeholder) is tracked in
v1.9.0, not here. #319 was filed later (2026-08-16), out of #312's own T1 pass. #323 and #324 were filed
later still (2026-08-17), both out of reading a normal development startup log: #323 is the bug that log
exposed, #324 is the missing user-facing visibility that made it invisible outside the log in the first
place. #329 came out of #309's own T1 run (2026-08-19), from the same subsystem again: one source's
connect exhausted its budget while a second path on the *same* host succeeded nine seconds later, on a
460 Mbps connection — a single marginal connect treated as terminal, with no retry and no parallelism.
#330 and #331 were filed the same day, out of reviewing #329: a refresh re-downloads byte-identical
content because nothing records what we already hold (#331), and answering that properly needs a
per-file record the project has never had (#330). The source-download subsystem has now produced five
of this milestone's issues — #323, #325, #329, #330, #331 — none of them anticipated when it opened.

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
#307 ─── depends on #80 (extends its shipped schema/generator/models) and, per ADR 018, on #309's
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
#327 ─── depended on #326, which is now done, so it is unblocked. Replaces the obsolete #293
         reproduction, whose --read-only technique #294 made survivable. Inherits a corrected premise:
         #326 measured that WAL sidecar state, not a pending migration, decides whether a read-only
         mount degrades — so #327's named scenario must pin how the seeding container is stopped or it
         reproduces only by luck
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

**Revised 2026-08-15**, after the developer restated the milestone's goal: v1.8.0 shipped a *basic*
notification system, and this milestone is about making it complete and useful. That reframing made
#278's schema — one flat `Message`, always-on expiry, no structured metadata, no app-version linkage —
the milestone's real bottleneck rather than a fixed constraint to build around. #312 was filed to own
that foundation, and now leads the order: five of the remaining issues write or render notifications and
all of them are cheaper once it lands.

**#308 moved from second to last in the same revision.** Rendering was placed early so producers'
output would display correctly from the start. The developer corrected that: #308 has to define how
*each notification type* is laid out across *both* surfaces — the startup/popup dialogs and the
notifications view — and the remaining producers each bring a type with its own payload. Designing
those layouts before the types exist is guessing. It also gains from landing last, since #312 made
app-version provenance and typed payloads available to render from.

**Revised 2026-08-21**, after #326's implementation and #325's revert. Two of the changes are
corrections rather than decisions: #326 is done, and the dependency map's description of #329 had
become false — it said #329 would remove #323's `ConnectTimeout` and leave #325's connector running per
attempt, and neither is true now that the connector is gone and the timeout is the download path's only
protection.

The one real decision was **#329, moved from 13 to 11**. The revert left source downloads with no retry
and a connect budget raised to 60 s as an acknowledged stopgap, while #326's T2 showed the failures it
guards against are intermittent rather than persistent. It was placed *after* #327 and #328 rather than
ahead of them (developer direction): those two close the verification gap that let #326's crash ship
unnoticed, and an unverified never-crash guarantee is the more expensive thing to leave open than a
stopgap that is currently holding. #330 and #331 were reviewed and left where they are — nothing this
session touched file metadata or conditional requests.

| Order | Issue | Reason |
|-------|-------|--------|
| 1 | **#313** ✅ | Done. Api tests were asserting before startup completed — measured at 5 of 5 runs, so every verification in this milestone was untrustworthy until it landed. Had to come first for that reason, not because of any dependency |
| 2 | **#323** ✅ | Independent bug; taken first by developer direction (2026-08-17) because it was found live and its fix is self-contained to the HTTP client registration. Blocks nothing |
| 3 | **#325** ✅ | Taken immediately after #323 as the same startup log's remaining half. Its fix was reverted on 2026-08-20 as disproportionate to a failure the application already handles by falling back to the local copy; #323's `ConnectTimeout`, raised to 60 s, carries it instead. See its plan doc's "Reverted" section |
| 4 | **#312** ✅ | Foundation: title/body, typed metadata, opt-in expiry, app-version provenance, and the relocated dedupe helper. Blocks #81, #302, #303, #304, #308 — building any of them first means building them twice |
| 5 | **#81** ✅ | What's-new-after-upgrade path; builds on #278's, #80's, #309's, #307's and #312's output |
| 6 | **#83** ✅ | Narrowed to a single live T3 confirmation; can run whenever the next beta add-on install happens, independently of everything else |
| 7 | **#309** ✅ | Done. T1 confirmed live (2026-08-19) surfaced four further defects — all fixed and verified; see steps 14–18 in its plan doc. T2 green the same day. Next: **#326** |
| 8 | **#326** ✅ | Done, `Waiting for release`. All 12 verification rows green including T1 and a T2 controlled pair. It also corrected its own premise, which #327 inherits: sidecar state decides whether a read-only mount degrades, **not** a pending migration |
| 9 | **#327** | Rewrites the degradation smoke coverage around the never-crash feature. #326 is done, so it is unblocked. Must pin the WAL sidecar state explicitly — its named scenario does not reproduce reliably otherwise, and #326's plan doc carries the measurement |
| 10 | **#328** | Bundled-import and live-endpoint smoke coverage; independent of everything above |
| 11 | **#329** | **Moved from 13 to 11 (developer direction, 2026-08-21).** #325's revert left the download path with no retry at all, and its only protection is a connect budget raised from 10 s to 60 s as an explicit stopgap — `SourceCacheUpdater`'s own docs say #329 is expected to tune it. #326's T2 showed both sources failing and then answering in ~300 ms six minutes later, which is exactly the intermittency retry addresses. Placed after the two smoke-test issues rather than ahead of them: they close the verification gap that let #326 ship undetected, and that gap is the more expensive one to leave open. Still before #324, which consumes its statistics |
| 12 | **#307** | Two documentation-confirmation rows outstanding — see its plan doc |
| 13 | **#319** | Translated title/body. Placed here (developer direction, 2026-08-16) because every producer below writes new user-facing text: building them against the untranslated shape means writing each one twice. Also migrates the three producers already shipped. Does not constrain #329 above, which writes no user-facing text |
| 14 | **#330** | File metadata foundation — sidecar + `Import_FileMetadata`. Independent of everything above it, and #331 below cannot start without it |
| 15 | **#331** | Conditional requests, storing validators in #330's shape. Lands before #324 so the source-download subsystem is finished before anything reports on it |
| 16 | **#324** | Reports on the finished source-download subsystem: a failed refresh, a source that needed more than one attempt (#329's statistics), and optionally #331's `Unchanged`. Placed last in this cluster deliberately — reporting on it while #329/#330/#331 are still landing means revising it each time. Still after #319, for that rule's own reason: it writes new user-facing text. It ships **without** a diagnostic code: the Knowledgebase (#333) is v1.9.0, i.e. after this milestone, and retrofits codes onto every message that predates it — the same sequencing #326 already follows |
| 17 | **#304** | Gives the reseed action a Blazor-reachable entry point for the first time; #302 and #303 below become observable through that path |
| 18 | **#302** | Writes from inside the seeding loop (see Dependency map); no dependency on the review page below |
| 19 | **#303** | Same hook point as #302; adds the one piece of new UI this milestone needs, explicitly scoped smaller than #66's own future side-by-side diff view |
| 20 | **#305** | Independent bug; can slot in anywhere |
| 21 | **#306** | Independent bug; can slot in anywhere |
| 22 | **#308** | **Moved from position 2 to last** (developer direction, 2026-08-15). It was placed early on the reasoning that rendering should precede the producers so their output displays correctly from the start. That was the wrong way round: #308 is not a CSS fix but a design of how *each notification type* is laid out across *both* surfaces — the startup/popup dialogs and the notifications view — and it cannot settle those layouts before the notification types that need them exist. #302/#303/#304 each introduce a producer with its own payload shape; designing their presentation while they are still unwritten means guessing. Landing last also means it can exploit everything #312 made available, including app-version provenance |

**Migration consolidation, at the end of the milestone.** #81, #312, #319 and #304 each add Data-owned
migrations, and #312 deliberately does not optimise migration count. Per the developer's direction
(2026-08-15), the accumulated migrations are consolidated into a smaller set before release, the same way
#155 and #289 each consolidated their own predecessors.

---

## PR merge plan

All twenty-two issues are self-contained on top of already-released infrastructure (#278, #80, #154,
#156) — none leave anything half-wired if merged independently, except #81 (which genuinely cannot
merge before #309 and #307), #319 (which cannot merge before #312) and #331 (which cannot merge before
#330). Those are real implementation-order dependencies, not just sequencing preferences.
Default assumption per `process.md`: the branch stays open until all issues in the milestone are done,
then one PR — no known reason to depart from that default.
