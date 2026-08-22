# #83 — Research: notification system design

**Status:** Waiting for release
**GitHub issue:** #83
**Tiers required:** T1, T2, T3
**Depends on:** none

---

## Question (from the GitHub issue)

Before implementing the notification system (#81), research and document the design for the generic
notification infrastructure: what notification types are needed, what UI primitive to use, how
dismissed state should be persisted, whether notifications queue or display simultaneously, whether
severity levels are needed, and how this interacts with the HA companion app.

## Scope narrowing (confirmed with developer, 2026-08-12)

**Finding:** issue [#278](https://github.com/DutchJaFO/Quotinator/issues/278) — "Add a startup
notification system surfaced in the #263 modals" — shipped in the v1.8.0 maintenance milestone
(already released) and built a complete generic notification mechanism, independently of this issue
and with no reference to it (split from #267/#276, unrelated to the Notification system milestone).
`System_Notification` table, `NotificationEntity`, `NotificationType` enum, dismiss lifecycle with
expiry, `INotificationReader`/`INotificationWriter`, `GET /api/v1/notifications` +
`POST /api/v1/notifications/{id}/dismiss`, and `NotificationSummary`/`NotificationTable` components
embedded in `StartupSuccessModal`/`StartupErrorModal` plus a standalone `/notifications` page all
already exist and are live in production. This answers nearly every question #83 was filed to research,
after the fact and without the decision record #83's own "Output" section calls for.

Confirmed with the developer (2026-08-12): rather than write a retroactive ADR for decisions already
settled by shipped, working code, #83 stays open but **narrowed to the one question #278 did not
actually answer** — live HA ingress/companion-app rendering of the modal-based notification UI, which
#278 never triggered a T3 requirement for (none of its changes touched ingress middleware, `PathBase`,
or `addon/config.yaml`) and so was never confirmed against a real supervisor. Every other question below
closes as already-answered, informally documented via #278's own plan doc (no new ADR — matches this
project's existing precedent of research questions resolved by a findings comment rather than a formal
ADR when the answer is already settled elsewhere, e.g. #264/#265/#281/#282).

---

## Investigation steps

### 1. Notification types needed

**Status:** Done — answered by #278.

**Finding:** `NotificationType` (`Quotinator.Data.Enums`) has five members — `Information`, `Warning`,
`Error`, `Success`, `ActionRequired` — covering every case the issue's own examples name (startup
warnings, "what's new," and the extensibility for future producers like background-job completion or
admin alerts via the same mechanism). No gap found.

### 2. Right UI primitive

**Status:** Done — answered by #278.

**Finding:** neither a Bootstrap toast, a blocking modal-only design, nor a bare inline alert — #278
chose a **plain Bootstrap `modal`-styled `<div>` rendered inline in the page** (`class="modal show
d-block"`, no Bootstrap JS modal component, no `IJSRuntime` dependency), embedded via a shared
`NotificationTable` component inside `StartupSuccessModal.razor`/`StartupErrorModal.razor`, plus a
standalone `/notifications` page (`Components/Pages/Notifications.razor`) for full history outside the
transient startup view. This is closer to the issue's "notification tray" option than a toast — a
persistent, reviewable list rather than an auto-dismissing popup.

### 3. Dismissed-state persistence

**Status:** Done — answered by #278.

**Finding:** neither browser `localStorage`, a cookie, nor a per-user server preference (the issue's
own three options) — **server-side in the database itself**, via `NotificationEntity.IsDismissed`/
`DismissedAt` on the shared `System_Notification` table. This is a stronger mechanism than any of the
three named in the issue: dismissal is durable across devices/browsers and survives container restarts,
at the cost of being global rather than per-user — acceptable given this project has no authentication
in v1 (CLAUDE.md's "What NOT to do" — no auth in v1) and a single-operator homelab deployment model.

### 4. Queued or simultaneous display

**Status:** Done — answered by #278.

**Finding:** simultaneous, not FIFO-queued. `NotificationTable` renders every active notification
returned by `GetActiveNotificationsAsync()` at once, in one list. #278's own Step 7 revision (see its
plan doc) confirms this was a deliberate correction mid-implementation — an earlier per-type-filtered
design was found to hide notifications unrelated to the modal's own success/error state, and the fix was
to show everything active, unfiltered, in both modals.

### 5. Severity levels

**Status:** Done — answered by #278 (same finding as step 1).

**Finding:** yes — `NotificationType` doubles as the severity axis (`Information`/`Success` vs.
`Warning`/`Error`, plus `ActionRequired` for anything needing operator action). `NotificationTable`
renders a distinct badge per type (`TypeLabel`/`BadgeClass`).

### 6. HA companion app / ingress interaction

**Status:** Not started — blocked on the next beta add-on release (T3 can only be confirmed live,
per `docs/release-verification.md`'s T3 gate; no further planning work closes this).

The issue's own concern was specifically about **toasts** behaving differently inside the HA ingress
frame. #278's chosen primitive is not a toast — it is a plain server-rendered `<div>` styled as a modal,
using no `IJSRuntime`/JS-interop dispatch of any kind (confirmed: this codebase has zero existing
`IJSRuntime` usage anywhere, noted explicitly in #278's own Step 11). It renders as ordinary page content
within the same origin and frame as everything else already routed through ingress — the same class of
content as `/notifications`, `/stats`, `/about`, all of which are already confirmed working under
ingress via `DatabaseHealthGateMiddleware`'s `ExemptPrefixes` precedent. There is no `target="_blank"`
link involved (the HA-companion-app failure mode this project's own CLAUDE.md documents for external
links) since nothing in the notification UI navigates away from the app.

This makes a rendering problem unlikely on inspection, but #278 never actually triggered T3 (none of its
changes touched ingress middleware, `PathBase`, `UseForwardedHeaders`, DataProtection, SSL/Kestrel
config, or `addon/config.yaml`/`addon-beta/config.yaml` — the documented T3 trigger list), so it was
correctly scoped as T1+T2 only and has genuinely never been visually confirmed inside a real HA
supervisor ingress frame. This step's action: the next time a beta add-on is installed for any release
that includes the startup modals or `/notifications` page (which is every release from v1.8.0 onward),
visually confirm the modal and the `/notifications` page render correctly through ingress — no separate
release is needed solely for this, it rides along with the next natural T3 pass.

---

## Outcome tracking

| Possible outcome | Applies? | Notes |
|---|---|---|
| New issues in the current milestone | No | #278 already exists; nothing further to design or build for the generic mechanism itself |
| New milestone | No | n/a |
| Not feasible / rejected | No | n/a — the mechanism already shipped and works |
| Architecture decision required | No, by developer decision (2026-08-12) | The design is already settled by #278's shipped code; a retroactive ADR was considered and explicitly declined in favour of this narrowed research issue plus #278's own plan doc serving as the informal decision record |

---

## Notes

No T1/T2 tiers apply — steps 1–5 are documentation of an already-shipped, already-verified mechanism
(#278 itself carries its own T1/T2 verification). Step 6 is a live T3-only confirmation.

**Closing path:** once step 6's T3 confirmation happens (next beta add-on install), post findings as a
comment on GitHub issue #83 (per its own "Output" requirement) summarising steps 1–6, then close.
