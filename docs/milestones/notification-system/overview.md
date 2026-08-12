# Notification system — Milestone Overview

**GitHub milestone:** [#14](https://github.com/DutchJaFO/Quotinator/milestone/14)
**Branch:** `feature/notification-system`
**Status:** Planning

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
| [#83](https://github.com/DutchJaFO/Quotinator/issues/83) | Research: notification system design | Planning | T3 ⬜ (live confirmation only, no other tier applies) | [83-notification-system-design-research-plan.md](83-notification-system-design-research-plan.md) |
| [#81](https://github.com/DutchJaFO/Quotinator/issues/81) | Startup notification: what's new after upgrade | Planning | T1 ⬜ T2 ⬜ | [81-startup-whats-new-notification-plan.md](81-startup-whats-new-notification-plan.md) |
| *(pending creation)* | Notification: confirm files that reseed cleanly with no review needed | Planning | TBD | No plan doc yet |
| *(pending creation)* | Notification + minimal review page: alert when a reseed leaves import actions pending review | Planning | TBD | No plan doc yet |
| *(pending creation)* | Notification + action: let the user trigger a reseed (content changed upstream, or after a Reset) | Planning | TBD | No plan doc yet |

The three pending-creation issues replace an earlier, stale "import diagnostics" issue drafted during
this same planning pass, before the developer redirected scope — see the Description above.

---

## Dependency map

```
#83  ─── (none) — narrowed scope; last open question is a live T3 confirmation, not a blocker for anything else
#81  ─── depends on #278 (done, released v1.8.0) for its notification mechanism; depends on #80 (done,
         released) for changelog highlights via IChangelogService
Reseed-available notification+action ─── depends on #278 (notification mechanism) and #156 (Reset no
         longer auto-seeds, which is exactly the gap it fills for the post-Reset trigger)
Per-file success notification        ─── depends on #278; reads IDatabaseInitializer.LastSeedReport (#221)
Per-file conflict notification + review page ─── depends on #278; reads LastSeedReport; its review page
         depends on the existing #154 staging model (ImportAction, IImportActionReader/Service) and the
         existing /import/actions REST endpoints — all already shipped, nothing new needed there
```

None of the three new issues depend on each other for their own correctness, but the reseed-available
action is what makes the other two's producers reachable from the UI for the first time (today a reseed
only happens via curl+admin key) — natural to build first, not a hard requirement.

#81 and #83 do not depend on #278's siblings above and remain independent of them.

---

## Order of operations

| Order | Issue | Reason |
|-------|-------|--------|
| 1 | **#83** | Narrowed to a single live T3 confirmation; can run whenever the next beta add-on install happens, independently of everything else |
| 2 | **#81** | What's-new-after-upgrade path; builds directly on #278's already-shipped notification mechanism and #80's already-shipped `IChangelogService` |
| 3 | **Reseed-available notification + action** *(pending creation)* | Gives the reseed action a Blazor-reachable entry point for the first time; the two producers below become observable through that path |
| 4 | **Per-file success notification** *(pending creation)* | Reads `LastSeedReport` after `POST /admin/database/reseed`; no dependency on the review page below |
| 5 | **Per-file conflict notification + minimal review page** *(pending creation)* | Same hook point as #4; adds the one piece of new UI this milestone needs, explicitly scoped smaller than #66's own future side-by-side diff view |

---

## PR merge plan

All five issues are self-contained on top of already-released infrastructure (#278, #80, #154, #156) —
none leave anything half-wired if merged independently, and none depend on each other for correctness
(only for a sensible build order — see Order of operations). Default assumption per `process.md`: the
branch stays open until all issues in the milestone are done, then one PR — no known reason to depart
from that default given how small this milestone is.
