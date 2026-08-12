# #81 — Startup notification: what's new after upgrade

**Status:** Planning
**GitHub issue:** #81 (open)
**Depends on:** #278 (done, released v1.8.0), #80 (done, released — Changelog handling milestone)

---

## Background

The frontend user has no visibility into what changed after an upgrade. #80 already built a JSON-driven
changelog system with per-release `Highlights`; nothing currently surfaces those highlights to the user
automatically — `About.razor` shows the full changelog only when the user navigates there themselves.

## Scope narrowing (confirmed with developer, 2026-08-12)

The original issue proposed two independent paths sharing one UI primitive: "what's new" and import
warnings/errors from `IDatabaseInitializer`. This plan doc covers **only the what's-new path**. #81's
own comment (posted before #278 existed) already flagged that the import-warning path has grown its own
prerequisite — per-record import diagnostics (`ImportLines`/`ImportErrors`-style tables) that don't
exist yet, since the audit trail records which tables were written to, not why individual source lines
were rejected. That path is being split into a new issue (see this milestone's `overview.md`) rather
than block the what's-new path, which has no such gap and is fully buildable today.

## Authoritative-source cross-check

Checked before designing anything below:

- **#278 (released v1.8.0)** already built the entire notification mechanism this issue's own "Proposed
  design" section describes needing to build: a dismissible component (`NotificationSummary`/
  `NotificationTable`, embedded in `StartupSuccessModal`/`StartupErrorModal`), and a writer/reader pair
  (`INotificationWriter`/`INotificationReader`) backed by `System_Notification`. Nothing here needs a new
  table, entity, migration, or Razor component — this issue is purely a new **producer** for the existing
  mechanism, the same shape as #279's and #289's producers already in `Program.cs`.
- **`Quotinator.Api.Startup.NotificationSeeding.SeedOnceAsync`** (added by #279, reused by #289) is
  exactly the "write once, dedupe by a key expected to appear in the message" helper this issue needs for
  "show highlights for the current version if unseen; mark as seen on dismiss" — dismissal is already
  handled by the existing mechanism (server-side `IsDismissed`, per #83's step 3 finding), so **no new
  cookie or `localStorage` "last-seen version" marker is needed at all** — the issue's own proposed
  design predates #278 and named a cookie/localStorage because no server-side persistence existed yet at
  the time it was written. Writing one `Information` notification per version, deduped by that version
  string, is a strictly better mechanism than a client-side marker: it survives browser/device changes
  and needs no new column anywhere.
- **`IVersionService.Version`** (`Quotinator.Core.Services`) gives the running build's version string
  (informational version, `+githash` suffix already stripped). **`IChangelogService.GetForCulture(null)`**
  (`Quotinator.Changelog.Services`) gives the English-fallback `ChangelogDocument`, whose `Releases`
  (newest first) each carry `Version` and `Highlights`. Both are already DI-registered and already
  consumed together in `About.razor.cs` — no new registration needed.
- **ADR check:** no new table, entity, or enum — ADR 002/008/015/016 (RecordBase, CHECK constraints,
  domain-prefixed naming, class suffixes/enum placement) have nothing to apply to. No scope mismatch
  found.

No scope mismatch found between the narrowed issue and current authoritative sources — proceeding with
the what's-new path as designed below.

---

## Design

### Dedupe-key correctness (found during design review)

`SeedOnceAsync`'s dedupe check is `history.Items.Any(n => n.Message.Contains(dedupeKey))` — the dedupe
key must be a literal substring of the message that will actually be written, not a separately-formatted
string. **Bare version numbers are not safe as a dedupe key on their own**: `"1.9.1"` is a substring of
`"1.9.10"`, so a naive `dedupeKey: release.Version` risks a false-positive dedupe between two different
patch versions whose digits happen to nest. The producer wraps the version in an unambiguous delimiter —
`dedupeKey: $"WhatsNew:v{release.Version}:"` (colon on both sides) — and the message text includes that
exact bracketed form verbatim, not just the bare version number.

### Producer (third one, alongside #279's and #289's in `Program.cs`)

Added to the same non-fatal `if (dbHealth.IsHealthy)` block pattern #279/#289 already use — a failure to
write this notification must never mark the app unhealthy, matching both existing producers' own
reasoning (announcing something is inherently non-critical, unlike schema init).

```
var currentVersion = versionService.Version;
var release = changelogService.GetForCulture(null)?.Releases
    .FirstOrDefault(r => r.Version == currentVersion);
if (release is not null)
{
    var dedupeKey = $"WhatsNew:v{release.Version}:";
    var message = $"{dedupeKey} What's new in v{release.Version}: " +
                   string.Join(" ", release.Highlights);
    await NotificationSeeding.SeedOnceAsync(
        notificationReader, notificationWriter, NotificationType.Information, dedupeKey, message);
}
```

- **No release found** (local/dev build, or a version not yet present in the shipped changelog) → no
  notification written, no error. Satisfies the Definition of done's "No notification shown when there
  is nothing to report."
- **Release found with only the standard internal-only highlight** ("Internal improvements — no
  user-facing changes.", per CLAUDE.md's Pre-Push Checklist template) → still written. A release always
  has at least one highlights entry by that same rule, so this is not a special case.
- **Already seen** — `SeedOnceAsync`'s own history scan finds the prior write (dedupe key present in an
  existing notification's message, active or dismissed) and no-ops. This is what makes "mark as seen on
  dismiss" work for free: once the user dismisses the notification (existing `POST
  /notifications/{id}/dismiss` / the `/notifications` page's Dismiss button), it never reappears, and if
  they never dismiss it, it simply stays visible — also satisfying the requirement, since an unseen
  highlight should keep showing until acknowledged.

### Where this runs relative to #83

Independent — #83's remaining question (T3 ingress rendering of the modal/table UI) affects how this
notification is *displayed*, not whether it's correctly written. This issue does not block on #83, and
#83 does not block on this issue.

---

## Steps

### 1. Plan doc, slnx, overview.md
**Status:** ✅ Done

### 2. Write the producer in `Program.cs`
**Status:** Not started

### 3. Tests
**Status:** Not started

Unlike #279/#289 (whose producers were verified only via T1/T2 live checks, per their own plan docs —
no unit test exists for `NotificationSeeding.SeedOnceAsync`'s call sites in `Program.cs` itself, since
`Program.cs` startup code has no existing unit-test harness in this codebase), this producer's *inputs*
(dedupe-key construction, release lookup, no-release-found handling) are pure functions of
`IChangelogService`/`IVersionService` output and are unit-testable in isolation if extracted into a
small internal static helper (matching `NotificationSeeding`'s own shape) rather than left as inline
`Program.cs` logic — decided during implementation, not before, consistent with keeping `Program.cs`
itself as thin as the two existing producers.

### 4. Live verification (T1, T2)
**Status:** Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A matching release's highlights are written as an `Information` notification on startup | Unit test | `WhatsNewNotificationTests.Seed_MatchingRelease_WritesInformationNotificationWithHighlights` (name TBD at implementation — extracted helper per Step 3) |
| 2 | ❌ | No notification is written when the running version has no matching changelog release | Unit test | `WhatsNewNotificationTests.Seed_NoMatchingRelease_WritesNothing` |
| 3 | ❌ | Two different versions whose digits nest (e.g. `1.9.1` vs `1.9.10`) do not falsely dedupe against each other | Unit test | `WhatsNewNotificationTests.Seed_NestedVersionNumbers_DoNotFalselyDedupe` |
| 4 | ❌ | A version already seeded is not re-seeded on a later restart (dedupe holds across restarts) | Unit test | `WhatsNewNotificationTests.Seed_AlreadySeededVersion_IsNoOp` |
| 5 | ❌ | Dismissing the what's-new notification persists — it does not reappear on the next restart | Live | Docker: seed a matching release, confirm notification appears in `GET /api/v1/notifications`, dismiss via `POST /api/v1/notifications/{id}/dismiss`, restart the container, confirm it does not reappear |
| 6 | ❌ | T1 — app starts in Visual Studio with no error; `StartupSuccessModal` shows the what's-new notification when the running version has changelog highlights | Live | Developer confirms in Visual Studio |
| 7 | ❌ | T2 — Docker build and smoke test | Live | `docker build -f docker/Dockerfile -t quotinator:local .`; confirm `GET /api/v1/notifications` includes the what's-new entry when the built version matches a changelog release |
| 8 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 9 | ❌ | Full test suite green | Build | `dotnet test --configuration Release` |

---

## Relationship to existing issues

- **#278** — the notification mechanism this issue is a producer for.
- **#80** — the changelog system this issue reads highlights from.
- **#83** — independent; narrowed to a live T3 question about the shared UI this issue also uses, not a
  dependency either direction.
- **New issue (import warnings/errors, not yet filed)** — split out of this issue's original scope, per
  its own comment. See `overview.md`'s Order of operations.
