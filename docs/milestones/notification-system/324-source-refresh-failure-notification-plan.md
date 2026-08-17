# #324 — Notification: report when a source update attempt fails

**Status:** Planning
**GitHub issue:** #324 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism), #312 (done, this branch — title/body, typed metadata, opt-in expiry, the relocated dedupe helper), #319 (hard, not started — translated title/body)

> **Next action: refine this plan once #319 lands.** The requirements and the verification checklist
> below are settled and taken from the issue. The Steps are deliberately unwritten: this producer writes
> new user-facing text, and #319 changes the shape that text is written in (`OriginalLanguage` column
> plus `System_NotificationTranslation`). Writing the steps against today's untranslated
> `INotificationWriter.WriteAsync` signature would mean writing them twice — the exact reason the
> milestone sequenced #319 ahead of every remaining producer (developer direction, 2026-08-16).

---

## Background

A failed source refresh is currently invisible outside the container log:

```
[22:30:23 WRN] [Database - SourceRefresh] could not reach
https://raw.githubusercontent.com/... — using local NikhilNamal17_popular-movie-quotes.json
```

The app serves the fallback copy and reports nothing. Under Home Assistant nobody reads the supervisor
log routinely, so a source failing to refresh for weeks is indistinguishable from one fully up to date —
quotes are still served, just from progressively staler cached content. Filed alongside #323, which fixes
one specific cause of these failures; this issue is about knowing they happened at all, whatever the
cause.

## Verified against the code before planning

- **`ResolveAsync` already returns everything needed.** `SourceRefreshResult`
  (`src/Quotinator.Data/Import/SourceRefreshResult.cs`) carries `Name`, `Url`, `Outcome`, an optional
  `Detail`, and `LastRefreshedAtUtc` — documented as the effective cache file's *actual* mtime, not
  "now", so "how stale is the content we are serving instead" needs no derivation.
- **`SourceRefreshOutcome` has exactly the four states the requirements assume** — `Updated`,
  `UpToDate`, `Failed`, `SkippedCollision` (`src/Quotinator.Data/Enums/SourceRefreshOutcome.cs`).
- **`NotificationType.Warning` is the right severity** — defined as "a non-fatal condition worth the
  operator's attention, but not an error" (`src/Quotinator.Data/Enums/NotificationType.cs`), which is
  exactly a degraded-but-serving state.
- **Two call sites reach `ResolveAsync`**: `QuotinatorDatabaseInitializer.RefreshSourcesAsync` (the
  admin force-refresh path) and `ResolveEffectiveBatchesAsync` (startup initialisation) —
  `src/Quotinator.Core/Database/QuotinatorDatabaseInitializer.cs:247,263`. Requirement 10 is satisfied by
  hooking the shared path, not by two separate producers.

## Open question for the refinement pass

**Dedupe key.** Requirement 6 asks that repeated failures across restarts not accumulate, while a *new*
failure after a period of success still surfaces. `NotificationContentHash`/`NotificationSeeding` give
content-based dedupe, which would suppress a genuinely new occurrence whose text happens to match. Decide
during refinement whether the key includes the last-success timestamp, or whether requirement 7's
supersede-on-success is sufficient on its own (a successful refresh dismisses the old notification, so
the next failure is genuinely new content).

---

## Steps

Not yet written — see the Next action note above.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | No notification when every source resolves successfully | Unit test | `SourceRefreshNotificationTests.Resolve_AllSourcesSucceed_WritesNoNotification` |
| 2 | ❌ | A `Failed` outcome writes a `Warning` notification naming that source and its `downloadUrl` | Unit test | `SourceRefreshNotificationTests.Resolve_OneSourceFails_WritesWarningNotificationNamingThatSource` |
| 3 | ❌ | The notification reports when the attempt happened and how old the fallback content is | Unit test | `SourceRefreshNotificationTests.Resolve_OneSourceFails_NotificationReportsFallbackAgeFromLastRefreshedAt` |
| 4 | ❌ | With no trusted cache, the notification says the bundled file is in use rather than reporting a null age | Unit test | `SourceRefreshNotificationTests.Resolve_OneSourceFails_NoTrustedCache_NotificationSaysBundledFileInUse` |
| 5 | ❌ | The failure reason from `SourceRefreshResult.Detail` is carried when present | Unit test | `SourceRefreshNotificationTests.Resolve_OneSourceFails_DetailIsIncludedWhenPresent` |
| 6 | ❌ | Failures on consecutive startups do not accumulate duplicate notifications | Unit test | `SourceRefreshNotificationTests.Resolve_FailsOnConsecutiveStartups_DoesNotAccumulateDuplicateNotifications` |
| 7 | ❌ | A later successful refresh supersedes the notification via `NotificationDismissTrigger` | Unit test | `SourceRefreshNotificationTests.Resolve_FailureThenSuccess_SupersedesEarlierNotification` |
| 8 | ❌ | `SkippedCollision` writes no notification | Unit test | `SourceRefreshNotificationTests.Resolve_SkippedCollision_WritesNoNotification` |
| 9 | ❌ | Title and body are translated per #319 | Unit test | `SourceRefreshNotificationTests.Resolve_OneSourceFails_NotificationTitleAndBodyAreTranslated` |
| 10 | ❌ | The notification is written from the initialisation path, not from `Program.cs` | Unit test | `SourceRefreshNotificationTests.Resolve_FailureDuringInitialisation_WritesFromInitializerNotEndpoint` |
| 11 | ❌ | Both trigger points produce it — startup init and `POST /admin/sources/refresh?force=true` | Unit test | `SourceRefreshNotificationTests.Resolve_ForceRefreshEndpointPath_AlsoWritesNotification` |
| 12 | ❌ | T1 — the notification renders on the startup dialog and `/notifications` with a real failed source | Live | Point a manifest entry at an unroutable address, start, confirm the notification appears with source name and fallback age |
