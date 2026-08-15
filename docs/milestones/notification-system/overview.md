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
| [#312](https://github.com/DutchJaFO/Quotinator/issues/312) | Notification schema: title/body, typed metadata, optional expiry, and app-version provenance | In progress (step 9) | T1 ⬜ T2 ⬜ | [312-notification-schema-foundation-plan.md](312-notification-schema-foundation-plan.md) |
| [#313](https://github.com/DutchJaFO/Quotinator/issues/313) | Api tests can silently assert against the startup wait page instead of the endpoint under test | Waiting for release | N/A (test harness + docs only — no src/ change) | [313-api-test-startup-race-plan.md](313-api-test-startup-race-plan.md) |
| [#83](https://github.com/DutchJaFO/Quotinator/issues/83) | Research: notification system design | Planning | T3 ⬜ (live confirmation only, no other tier applies) | [83-notification-system-design-research-plan.md](83-notification-system-design-research-plan.md) |
| [#81](https://github.com/DutchJaFO/Quotinator/issues/81) | Startup notification: what's new after upgrade | In progress | T1 ⬜ T2 ⬜ | [81-startup-whats-new-notification-plan.md](81-startup-whats-new-notification-plan.md) |
| [#302](https://github.com/DutchJaFO/Quotinator/issues/302) | Notification: confirm files that reseed cleanly with no review needed (revised — writes from inside the seeding loop) | Planning | TBD | No plan doc yet |
| [#303](https://github.com/DutchJaFO/Quotinator/issues/303) | Notification + minimal review page: alert when a reseed leaves import actions pending review (revised — writes from inside the seeding loop) | Planning | TBD | No plan doc yet |
| [#304](https://github.com/DutchJaFO/Quotinator/issues/304) | Notification + action: let the user trigger a reseed (content changed upstream, or after a Reset) (revised — content-change trigger moves into the seeding loop) | Planning | TBD | No plan doc yet |
| [#307](https://github.com/DutchJaFO/Quotinator/issues/307) | Changelog highlights: mark specific entries as notification-worthy | In progress | N/A (library code, no runtime path) | [307-changelog-notification-audience-key-plan.md](307-changelog-notification-audience-key-plan.md) |
| [#308](https://github.com/DutchJaFO/Quotinator/issues/308) | Notification: multi-line/rich message layout | Planning | TBD | No plan doc yet |
| [#309](https://github.com/DutchJaFO/Quotinator/issues/309) | Move changelog content to database-backed System_Changelog table | Waiting for release | TBD | [309-system-changelog-table-plan.md](309-system-changelog-table-plan.md) |
| [#305](https://github.com/DutchJaFO/Quotinator/issues/305) | Database integrity check: verify all expected tables exist at startup, not just row counts | Planning | TBD | No plan doc yet |
| [#306](https://github.com/DutchJaFO/Quotinator/issues/306) | Bug: empty "Unreleased" section renders on the About page after a release tag | Planning | TBD | No plan doc yet |

#302, #303, and #304 replace an earlier, stale "import diagnostics" issue drafted during this same
planning pass, before the developer redirected scope — see the Description above. #305–#309 were all
filed the same session, before any of them had code; #310 (Genre-as-table placeholder) is tracked in
v1.9.0, not here.

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

| Order | Issue | Reason |
|-------|-------|--------|
| 0 | **#313** ✅ | Done. Api tests were asserting before startup completed — measured at 5 of 5 runs, so every verification in this milestone was untrustworthy until it landed. Had to come first for that reason, not because of any dependency |
| 1 | **#312** | Foundation: title/body, typed metadata, opt-in expiry, app-version provenance, and the relocated dedupe helper. Blocks #81, #302, #303, #304, #308 — building any of them first means building them twice |
| 2 | **#81** | What's-new-after-upgrade path; builds on #278's, #80's, #309's, #307's and #312's output |
| 3 | **#304** | Gives the reseed action a Blazor-reachable entry point for the first time; #302 and #303 below become observable through that path |
| 4 | **#302** | Writes from inside the seeding loop (see Dependency map); no dependency on the review page below |
| 5 | **#303** | Same hook point as #302; adds the one piece of new UI this milestone needs, explicitly scoped smaller than #66's own future side-by-side diff view |
| 6 | **#305** | Independent bug; can slot in anywhere |
| 7 | **#306** | Independent bug; can slot in anywhere |
| 8 | **#83** | Narrowed to a single live T3 confirmation; can run whenever the next beta add-on install happens, independently of everything else |
| 9 | **#308** | **Moved from position 2 to last** (developer direction, 2026-08-15). It was placed early on the reasoning that rendering should precede the producers so their output displays correctly from the start. That was the wrong way round: #308 is not a CSS fix but a design of how *each notification type* is laid out across *both* surfaces — the startup/popup dialogs and the notifications view — and it cannot settle those layouts before the notification types that need them exist. #302/#303/#304 each introduce a producer with its own payload shape; designing their presentation while they are still unwritten means guessing. Landing last also means it can exploit everything #312 made available, including app-version provenance |

**#309 and #307 are not listed** — both are code-complete on this branch (#309 `Waiting for release`,
#307 with only two documentation-confirmation rows outstanding), so neither gates anything below.

**Migration consolidation, at the end of the milestone.** #81, #312 and #304 each add Data-owned
migrations, and #312 deliberately does not optimise migration count. Per the developer's direction
(2026-08-15), the accumulated migrations are consolidated into a smaller set before release, the same way
#155 and #289 each consolidated their own predecessors.

---

## PR merge plan

All ten issues are self-contained on top of already-released infrastructure (#278, #80, #154, #156) —
none leave anything half-wired if merged independently, except #81 which genuinely cannot merge before
#309 and #307 (real implementation-order dependencies, not just sequencing preferences).
Default assumption per `process.md`: the branch stays open until all issues in the milestone are done,
then one PR — no known reason to depart from that default given how small this milestone is.
