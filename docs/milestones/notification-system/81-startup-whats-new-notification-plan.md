# #81 — Startup notification: what's new after upgrade

**Status:** In progress
**GitHub issue:** #81 (open)
**Depends on:** #278 (done, released v1.8.0), #80 (done, released — Changelog handling milestone),
#309 (code complete on this branch, Waiting for release — `IChangelogReader` exists and is tested),
#307 (code complete on this branch — `ChangelogReservedAudience.Notification`/`GetHighlightsFor` exist
and are tested; two documentation-confirmation rows remain open on its own plan doc but don't block this
issue, which only needs the code)

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

**Revised (2026-08-12), same session — depends on a prerequisite issue.** The producer below originally
joined every entry in `release.Highlights` into one message. Per developer direction, a release's full
highlights list isn't necessarily what belongs in a notification. #307 (split out this same session, then
revised again once it started) resolves this by reusing the already-shipped
`ChangelogUnreleased.AudienceHighlights` dictionary — no new field — with a reserved `"notification"` key:
this producer reads `release.GetHighlightsFor(ChangelogReservedAudience.Notification)` instead of the
full `Highlights` list.

**Revised (2026-08-14) — reads via `IChangelogReader`, not `IChangelogService` directly.** #309 (built
after this plan doc was first written) introduced `IChangelogReader.GetDocumentAsync(string? culture)` —
DB-first, falling back to the JSON-backed `IChangelogService` when the changelog database isn't ready or
available — and its own plan doc explicitly calls for every consumer, this one included, to go through
it instead of `IChangelogService` directly. This is the change: `GetForCulture(null)` (synchronous)
becomes `await changelogReader.GetDocumentAsync(null)` (async) — the lookup itself is otherwise
unchanged (still `Releases.FirstOrDefault(r => r.Version == currentVersion)`).

**Known timing characteristic, not a bug:** #309's own background changelog-database import runs as a
detached task that does not block `StartupPhaseState.MarkComplete()` (see #309's Step 6 notes). This
producer, like #279's and #289's, runs synchronously during startup, well before that background import
typically finishes — so in practice it will almost always read through `IChangelogReader`'s fallback
path (the JSON-backed `IChangelogService`) rather than the database, on every normal startup. This is
harmless: both paths return equivalent data (the fallback exists specifically so a consumer never needs
to care which one served a given call), and is noted here only so a future reader doesn't mistake it for
a defect if they observe the fallback path being hit consistently in logs.

**Also revised:** the message now supports multiple lines (one per flagged highlight) rather than one
joined sentence, since a release commonly has more than one highlight worth surfacing. Rendering
multiple lines legibly depends on `#308` (also split out this session) —
`NotificationTable`'s Message column currently has no line-break handling. This producer can still write
a `\n`-separated message before that issue lands (the notification would just render as run-together
text in the meantime), so it is a sequencing preference, not a hard implementation-order dependency.

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
var document = await changelogReader.GetDocumentAsync(null);
var release = document?.Releases.FirstOrDefault(r => r.Version == currentVersion);
var notificationHighlights = release?.GetHighlightsFor(ChangelogReservedAudience.Notification) ?? [];
if (notificationHighlights.Count > 0)
{
    var dedupeKey = $"WhatsNew:v{release!.Version}:";
    var message = $"{dedupeKey} What's new in v{release.Version}:\n" +
                   string.Join("\n", notificationHighlights);
    await NotificationSeeding.SeedOnceAsync(
        notificationReader, notificationWriter, NotificationType.Information, dedupeKey, message);
}
```

- **No release found, or a release with no flagged notification highlights** (local/dev build, a version
  not yet present in the shipped changelog, or a release whose highlights are all internal/technical and
  none were flagged as notification-worthy — see `#307`) → no notification written, no
  error. Satisfies the Definition of done's "No notification shown when there is nothing to report." This
  is a change from the original design (which treated every release as always having at least one
  highlight to show, since `Highlights` itself is never empty) — flagging is opt-in, so a release can
  legitimately have zero flagged entries.
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

## Design revision (2026-08-14) — catch up across every version missed, not just the currently running one

**Finding, confirmed with the developer:** the original design above only ever checked whether the
*currently running* version had flagged highlights. An operator who upgrades across several versions in
one go (e.g. skipping three patch releases between restarts) would only ever see the landing version's
notification — everything flagged in the skipped versions would never surface at all, since this
producer only ever runs at startup and only ever looked at one version.

**Behaviour, as directed by the developer:**
- On a genuine upgrade, walk every release strictly newer than the last version this app instance
  actively ran, up to and including the version now running, and write one notification per release
  that has flagged highlights (not one combined notification — each keeps its own version in its own
  dedupe key/message, so a multi-version catch-up produces several distinct notifications).
- On a **genuinely fresh install** (no version ever recorded before), show only the current version's
  notification — never the full historical backlog of every release that has ever had a flagged
  highlight.

### `System_AppVersion` — a new table tracking the last version that ran

A single-row table (`Quotinator.Data`-owned, `System_AppVersion`, Data-owned migration 4, `RecordBase`
per ADR 002) records the version string as of the last healthy startup. Read via
`IAppVersionTracker.GetLastActiveVersionAsync()` **before migrations run** on the following boot — a
missing table (a fresh install, or literally the first boot after this table was introduced) reads as
`null`, which is exactly the correct "fresh install" signal per the behaviour above; no separate
bootstrap flag is needed. Written via `RecordCurrentVersionAsync(version)` — an upsert (update the one
existing row if present, insert if not), called once startup is healthy.

**`Program.cs` sequencing:** `GetLastActiveVersionAsync()` is called synchronously, before
`dbInitializer.InitialiseAsync()` — fast (a single row against the main database, no separate connection
factory involved, unlike #309's changelog database), and captures the *old* value before anything
changes it. `RecordCurrentVersionAsync` is called synchronously too, right after `dbHealth.IsHealthy` —
also fast, matching #279's/#289's own synchronous read+write producers, so it does not reintroduce the
`StartupPhaseState.MarkComplete()` timing regression found twice already in this issue and in #309.
Only the *changelog-dependent* catch-up logic (`IChangelogReader.GetDocumentAsync`, the actual slow
part) stays in its own detached background task.

**Range matching uses `ChangelogDocument.Releases`' own newest-first array order, not semver parsing** —
`WhatsNewNotification.BuildSeeds` finds the array position of the last-active version and the current
version, then takes everything from the current version's index up to (but not including) the last-
active version's index. `.NET`'s own `Take(int)` treats a non-positive count as empty, which naturally
handles "no upgrade" (same version, `Take(0)`) and a downgrade (current version newer in the array,
negative count) the same way — nothing to report, no special-case code needed for either. A last-active
version that isn't found in the changelog at all (predates its history) falls back to just the current
version rather than guessing how far back to walk.

**`System_AppVersion` must always have content, the same "structurally required" reasoning CLAUDE.md's
endpoint side-effect policy applies elsewhere** — found live: `POST /admin/database/reset` (and the
identical `NotificationDismissTrigger.DatabaseReset` action-executor path) rebuilds this table empty
along with every other table (#156, no protected/excluded set). Left alone, it would stay empty until
the next full app restart, meaning a Reset immediately followed by a real version upgrade (without an
intervening restart) would lose the "last active version" signal and wrongly treat the upgrade as a
fresh install. Both Reset call sites now also call `appVersionTracker.RecordCurrentVersionAsync(...)`
immediately after a successful reset, in their own non-fatal try/catch (a version-recording failure must
never turn a successful Reset into a failed response — found live via a real test failure, see Step 5's
notes).

---

## Steps

### 1. Plan doc, slnx, overview.md
**Status:** ✅ Done

### 2. Write the producer in `Program.cs`
**Status:** ✅ Done

`WhatsNewNotification` (`Quotinator.Api.Startup`) splits the pure lookup/dedupe-key logic
(`BuildSeed(ChangelogDocument?, string currentVersion)`, returning a `Seed?` record struct) from the I/O
(`SeedAsync`, which calls `BuildSeed` then `NotificationSeeding.SeedOnceAsync`) — matching Step 3's own
design intent below. Wired into `Program.cs` as the third producer, alongside #279's and #289's, inside
the same `if (dbHealth.IsHealthy)` pattern.

**Startup-latency regression found and fixed, second occurrence of the exact pattern #309's Step 6 fixed
once already.** Awaiting `IChangelogReader.GetDocumentAsync(null)` inline (before
`StartupPhaseState.MarkComplete()` runs) reintroduced the identical race — this time breaking 87 tests
across `Quotinator.Api.Tests`, not just one, since every `WebApplicationFactory`-based test spins up its
own full startup sequence and the added latency shifted the whole suite's timing distribution. Fixed the
same way: the producer block now runs as a detached `Task.Run(...)` (its own internal try/catch,
identical non-fatal logging), matching #309's own precedent exactly. Confirmed the fix by running the
full solution test suite twice in a row.

### 3. Tests
**Status:** ✅ Done

`WhatsNewNotificationTests` (`Quotinator.Api.Tests`) — five tests against `WhatsNewNotification.SeedAsync`
using the existing `FakeNotificationReader`/`FakeNotificationWriter` (#279's own test doubles, no new
fakes needed): a matching release with flagged highlights writes exactly one `Information` notification
containing only the flagged text, not the full highlights list; no matching release writes nothing; a
matching release with zero flagged highlights writes nothing; nested version numbers (`1.9.1` vs
`1.9.10`) don't falsely dedupe against each other; an already-seeded version is a no-op.

Unlike #279/#289 (whose producers were verified only via T1/T2 live checks, per their own plan docs —
no unit test exists for `NotificationSeeding.SeedOnceAsync`'s call sites in `Program.cs` itself, since
`Program.cs` startup code has no existing unit-test harness in this codebase), this producer's *inputs*
(dedupe-key construction, release lookup, no-release-found handling) are pure functions of
`IChangelogService`/`IVersionService` output and are unit-testable in isolation if extracted into a
small internal static helper (matching `NotificationSeeding`'s own shape) rather than left as inline
`Program.cs` logic — decided during implementation, not before, consistent with keeping `Program.cs`
itself as thin as the two existing producers.

### 4. Live verification (T1, T2)
**Status:** T2 done; T1 needs the developer

**T2 — three real Docker runs, both the negative and positive paths, plus dismissal persistence.**

1. Built and ran the actual current image unmodified: `GET /api/v1/notifications` returned only the
   pre-existing #279 notification — no what's-new entry, correctly, since the real, shipped v1.8.3
   changelog entry has no `audienceHighlights.notification`-flagged highlights. Zero warnings/errors
   anywhere in the container log for the whole run — confirms the detached producer runs cleanly on the
   "nothing to report" path against real content, not just a unit-test double.
2. To prove the write path itself live (not just via unit test), temporarily added one
   `audienceHighlights.notification` entry to the v1.8.3 release in the local working copy of
   `data/changelog/changelog.en.json`, built the image from that modified copy, then immediately reverted
   the file via `git checkout` before the container was even started (confirmed clean via `git status`
   — this change was never committed). `GET /api/v1/notifications` on the resulting image showed the
   what's-new notification (`type: information`, message beginning `WhatsNew:v1.8.3:`) alongside the
   existing #279 entry — the write path works correctly against a real running app, not only in the unit
   tests' fakes.
3. Dismissed the what's-new notification via `POST /notifications/{id}/dismiss` (with
   `Quotinator__AdminApiKey` set for this run), then `docker restart`ed the same container (same
   filesystem, same persistent `quotinatordata.db`) — after restart, the notification's `isDismissed`
   remained `true` and no new duplicate was seeded, confirming `SeedOnceAsync`'s full-history dedupe scan
   (active + dismissed) correctly prevents re-seeding on every subsequent boot.

**T1 is the one item this session cannot complete** — per this project's standing rule that local
`dotnet run`/T1 verification is exclusively the developer's own action.

### 5. Multi-version catch-up: `System_AppVersion`, range-based `WhatsNewNotification.BuildSeeds`
**Status:** ✅ Done

`AppVersionMigrations.CreateAppVersionTable` (Data-owned migration 4, baseline updated in the same
commit), `AppVersionEntity`, `Sql.AppVersion` (`SelectCurrent`/`UpdateVersionById`), and
`IAppVersionTracker`/`AppVersionTracker` (`Quotinator.Data.Repositories`) — the read tolerates a missing
table via #293's exact idiom (`SqliteErrorCode == 1` + message match), matching every other "read before
the schema is guaranteed to exist" case in this codebase.

`WhatsNewNotification.BuildSeed`/`SeedAsync` became `BuildSeeds`/`SeedAsync` (plural) — array-position
range matching against `document.Releases` as described in the Design revision above, returning zero or
more `Seed`s instead of at most one.

**Two real gaps found and fixed while implementing this, both via genuine test failures, not
inspection:**
- **Three pre-existing tests hardcoded the Data-owned migration count as `3`** (schema-drift and
  legacy-upgrade tests in both `Quotinator.Data.Tests` and `Quotinator.Core.Tests`) — adding migration 4
  correctly broke all three; updated the literals to `4` rather than treating the failure as
  suspicious, since the count genuinely changed.
- **`POST /admin/database/reset`'s new `RecordCurrentVersionAsync` call was unguarded** — a real test
  (`AdminEndpointsTests`, which defaults to `NoOpDatabaseInitializer` and so never actually creates
  `System_AppVersion`) turned a successful Reset into a `500 InternalServerError`, since the unguarded
  call threw past the point where the response was already determined. Both Reset call sites
  (`AdminEndpoints.cs` and `NotificationActionExecutor.cs`) now wrap this call in the same non-fatal
  try/catch pattern `Program.cs`'s own startup wiring already uses — a version-recording failure must
  never turn a successful Reset into a failed response.

`AppVersionTrackerTests` (`Quotinator.Data.Tests`, real SQLite) — missing table and empty table both
read as `null`; a first write inserts; a second write updates the same row in place, not a duplicate.
`DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAppVersionSchema` (schema-drift parity,
matching the existing pattern for every other Data-owned table). `WhatsNewNotificationTests` gained six
new `BuildSeeds_*` tests covering fresh install, a multi-version upgrade (with an unflagged release and
an out-of-range release both correctly excluded), same-version (no-op), downgrade, a last-active version
missing from the changelog, and a current version missing from the changelog.

**Live-verified in Docker, twice.** First run (fresh install → restart, same version): zero
warnings/errors either boot, and the notification count stayed at just the pre-existing #279 entry
across the restart — proving the version write on first boot and the read-back on the second boot both
work, and that a same-version restart correctly produces no new catch-up notification. Second run (the
Reset endpoint specifically, with `Quotinator__AdminApiKey` set): `POST /admin/database/reset` returned
`200` with zero warnings/errors in the log — directly proving the try/catch fix found via the failing
unit test also holds against a real running app, not just the test harness.

Verified: full solution `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Full solution
`dotnet test --configuration Release` — all projects green (run twice).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A matching release's flagged notification highlights are written as a multi-line `Information` notification on startup | Unit test | `WhatsNewNotificationTests.Seed_MatchingReleaseWithFlaggedHighlights_WritesInformationNotification` |
| 2 | ✅ | No notification is written when the running version has no matching changelog release | Unit test | `WhatsNewNotificationTests.Seed_NoMatchingRelease_WritesNothing` |
| 3 | ✅ | No notification is written when a matching release exists but has zero notification-flagged highlights | Unit test | `WhatsNewNotificationTests.Seed_MatchingReleaseNoFlaggedHighlights_WritesNothing` |
| 4 | ✅ | Two different versions whose digits nest (e.g. `1.9.1` vs `1.9.10`) do not falsely dedupe against each other | Unit test | `WhatsNewNotificationTests.Seed_NestedVersionNumbers_DoNotFalselyDedupe` |
| 5 | ✅ | A version already seeded is not re-seeded on a later restart (dedupe holds across restarts) | Unit test | `WhatsNewNotificationTests.Seed_AlreadySeededVersion_IsNoOp` |
| 6 | ✅ | Dismissing the what's-new notification persists — it does not reappear on the next restart | Live (T2) | Docker: dismissed the temporarily-flagged v1.8.3 notification, `docker restart`ed the same container, confirmed `isDismissed: true` held and no duplicate was seeded |
| 7 | ❌ | T1 — app starts in Visual Studio with no error; `StartupSuccessModal` shows the what's-new notification when the running version has flagged changelog highlights | Live | Developer confirms in Visual Studio |
| 8 | ✅ | T2 — Docker build and smoke test | Live | Confirmed both directions: real (unmodified) v1.8.3 content correctly writes nothing (zero flagged highlights); a temporarily-flagged local copy (never committed) correctly writes the notification, visible via `GET /api/v1/notifications` |
| 9 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 10 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` (run twice to rule out the startup-latency flakiness found in Step 2) |
| 11 | ✅ | `GetLastActiveVersionAsync` returns `null` when `System_AppVersion` doesn't exist or is empty | Unit test | `AppVersionTrackerTests.GetLastActiveVersionAsync_TableMissing_ReturnsNull`, `_TableEmpty_ReturnsNull` |
| 12 | ✅ | `RecordCurrentVersionAsync` inserts on first call, updates the same row (not a duplicate) on later calls | Unit test | `AppVersionTrackerTests.RecordCurrentVersionAsync_FirstCall_InsertsRow`, `_CalledTwice_UpdatesInPlaceNotDuplicate` |
| 13 | ✅ | `System_AppVersion`'s baseline and incremental replay produce identical schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAppVersionSchema` |
| 14 | ✅ | A fresh install considers only the current version, never the historical backlog | Unit test | `WhatsNewNotificationTests.BuildSeeds_FreshInstall_OnlyConsidersCurrentVersion` |
| 15 | ✅ | An upgrade across multiple versions returns one seed per flagged release in range, excluding unflagged and out-of-range releases | Unit test | `WhatsNewNotificationTests.BuildSeeds_UpgradeAcrossMultipleVersions_ReturnsOneSeedPerFlaggedReleaseInRange` |
| 16 | ✅ | Same version as last active (no upgrade) and a downgrade both return nothing | Unit test | `WhatsNewNotificationTests.BuildSeeds_SameVersionAsLastActive_ReturnsNothing`, `_Downgrade_ReturnsNothing` |
| 17 | ✅ | A last-active version missing from the changelog falls back to current-version-only; a current version missing from the changelog returns nothing | Unit test | `WhatsNewNotificationTests.BuildSeeds_LastActiveVersionNotInChangelog_FallsBackToCurrentVersionOnly`, `_CurrentVersionNotInChangelog_ReturnsNothing` |
| 18 | ✅ | `POST /admin/database/reset` re-populates `System_AppVersion` immediately, and a failure to do so never turns a successful Reset into a failed response | Unit test + Live (T2) | `NotificationActionExecutorTests.ExecuteAsync_DatabaseReset_...` asserts the recorded version; Docker: `POST /admin/database/reset` with a real admin key returned `200` with zero warnings/errors |
| 19 | ✅ | The version-tracking round-trip works against a real running app: written on first boot, read back and correctly produces no spurious catch-up notification on a same-version restart | Live (T2) | Docker: fresh install → restart, notification count unchanged, zero warnings/errors either boot |

---

## Relationship to existing issues

- **#278** — the notification mechanism this issue is a producer for.
- **#80** — the changelog system this issue reads highlights from.
- **#83** — independent; narrowed to a live T3 question about the shared UI this issue also uses, not a
  dependency either direction.
- **#302, #303, #304** — split out of this issue's original "import warnings" scope, then redesigned
  2026-08-12 around notification writes living inside the seeding pipeline itself. Independent of this
  issue's own what's-new path.
- **#309** — hard dependency; implements ADR 005's/018's resolution (`System_Changelog`). This issue's
  producer reads changelog content through whatever service #309 introduces for querying it.
- **#307** — hard dependency; this issue's producer cannot be implemented until
  `ChangelogReservedAudience.Notification` and `ChangelogUnreleased.GetHighlightsFor(...)` exist.
- **#308** — soft dependency; improves how this issue's own multi-line message renders, but does not
  block writing it.
