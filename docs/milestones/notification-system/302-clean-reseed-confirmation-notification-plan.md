# #302 — Notification: confirm files that reseed cleanly with no review needed

**Status:** Planning
**GitHub issue:** #302
**Tiers required:** T1, T2
**Depends on:** #278, #304, #312, #319

---

## Description

A reseed already reports a per-file breakdown, but only in the API response and the log. This issue
adds the "everything's fine" half of that feedback to the UI: one `Success` notification per file that
reseeded with nothing left to review.

**This plan is ready to execute.** The design decision the issue deferred here has been answered (see
Scope changes below), and every step and verification row names something real.

## Scope revision — where the notification is written from

**Recorded 2026-08-12, relocated here from `overview.md` 2026-08-22.** The notification write moves
into the seeding pipeline itself rather than being a separate call bolted onto `AdminEndpoints.cs`
after `ReseedAsync()` returns. Per developer direction: new notification content comes from the same
mechanism that already handles import content, not one-off `INotificationWriter.WriteAsync` calls
scattered across unrelated call sites.

`QuotinatorDatabaseInitializer`'s own per-file seeding loop already carries the exact signal at the
exact moment — `_actionService.ApplyBatchAsync(batchIdStr, ...)` returning `null` means the batch
fully applied with zero pending actions. The notification is written from inside that branch, not
reconstructed afterward from a snapshot report.

## Scope changes

**Two of the issue body's five items are already delivered, and one names a mechanism that no longer
exists. Reviewed against the code 2026-08-30.**

1. **The issue's item 3 — where the shared dedupe-write helper lives — was settled by #312, not here.**
   `NotificationSeeding` now lives in `src/Quotinator.Data/Notifications/NotificationSeeding.cs`, and
   its own class documentation records the decision for #302, #303 and #304 together. It is reachable
   from `Quotinator.Core`, which already calls `SeedWhileUnresolvedAsync` from
   `QuotinatorDatabaseInitializer`. Covered by
   `Quotinator.Data.Tests.Notifications.NotificationSeedingTests`; no work and no verification row
   belongs to this issue.

2. **The issue's item 1 — injecting `INotificationWriter` into `QuotinatorDatabaseInitializer` — was
   delivered by #304.** `INotificationReader`, `INotificationWriter` and `INotificationTextSource` are
   already constructor parameters. The old "shared with #303: whichever lands first does this step"
   arrangement no longer applies to either issue.

3. **The issue's item 4 — "rely on the existing configured expiry to age them out" — cannot be done as
   written.** #312 removed `Quotinator:NotificationDefaultExpiryHours` entirely (recorded in
   `changelog.en.json`'s unreleased section), and omitting `expiresAt` now means *never expires*
   rather than *apply the default*. **Developer decision, 2026-08-30:** these notifications carry no
   expiry — the operator dismisses them.

   The issue's "no dedupe" half does not follow from that, and was reversed in the same session:
   dedupe runs through `NotificationSeeding.SeedWhileUnresolvedAsync` (see step 5). Without it,
   "never expires" would leak — a row per file per reseed with nothing to clear them. With it, at most
   one live row exists per distinct per-file result, which is what makes the no-expiry decision hold.

4. **Typed metadata is in scope, and it costs a migration.** The issue names no payload, but every
   #312-era notification carries a `NotificationMetadataKind`, and a new member needs a table-rebuild
   migration widening the `MetadataKind` CHECK — the shape #304 already paid for in migration 15.
   **Developer decision, 2026-08-30:** add the kind, the payload DTO and the migration, rather than
   writing with `metadata: null`. Without it the file name and counts would exist only inside body
   prose, which #308 cannot lay out and no action can consume.

---

## Steps

### 1. Tell the seeding loop whether this run is a reseed

**Status:** ⬜ Not started

`SeedIfEmptyInternalAsync` is shared by cold start (`OnInitialisedAsync` → `SeedIfEmptyAsync`) and by
reseed (`OnReseedAsync`), and nothing in it says which caller invoked it — so the issue's "reseed only,
never the first empty-database seed" rule cannot be read at the point the notification is written.
Thread an explicit flag through both call sites.

Deliberately a parameter rather than reconstructing the decision afterward from `LastSeedReport`: the
issue's own scope revision above puts the write inside the clean-apply branch, and a snapshot read
after the loop is exactly the reconstruction that revision rejected.

### 2. Add the `ReseedFileApplied` metadata kind and its payload

**Status:** ⬜ Not started

A new `NotificationMetadataKind.ReseedFileApplied` member, registered in
`NotificationMetadataKinds.PayloadTypes`, plus `ReseedFileAppliedMetadataDto` carrying the file name
and the added/modified counts the clean-apply branch already computes. `ReleaseState` is
`NotApplicable` — a reseed is not about a release.

`IdentityComponents` is the file name plus both counts, and it is load-bearing: step 5 dedupes against
it, so this is what decides whether a reseed's result is "the same confirmation the operator is already
looking at" or a genuinely new one. A file whose counts changed between runs is a different result and
must notify separately.

Metadata is non-text data per `NotificationMetadataDto`'s own rule: the file name and counts are
identifiers and numbers a renderer consumes, never the prose — that lives in Title/Body, added by
step 4.

### 3. Widen the `MetadataKind` CHECK, the baseline, and the drift tests

**Status:** ⬜ Not started

SQLite cannot widen a CHECK in place, so this is a table rebuild — migration 17 in
`DatabaseInitializer.DataOwnedMigrations`, following migration 15's own precedent and structure.
`DataBaselineSql`'s `System_Notification` CHECK is updated to match in the same commit, and
`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` gains the new
value.

This milestone's end-of-milestone migration consolidation rewrites this migration along with the rest,
which is what keeps a rebuild affordable here rather than something to defer.

### 4. Add the notification's title and body in all three languages

**Status:** ⬜ Not started

Two new keys on `NotificationMessageKeys` and their strings in `UI.en-GB.json`, `UI.nl.json` and
`UI.de.json`, resolved at write time through `NotificationTranslations.Original` and
`NotificationTranslations.Build` — the arrangement #319 established, so the text is stored per language
rather than in whichever culture the host happened to default to. The body takes the file name and the
added/modified counts as arguments.

### 5. Write the per-file success notification from the clean-apply branch

**Status:** ⬜ Not started

In the `applyResult is null` branch, one `Success` notification per file, gated on step 1's flag.

**Written through `NotificationSeeding.SeedWhileUnresolvedAsync`, the same helper #304's producer
uses** — not a bare `INotificationWriter.WriteAsync`. The thing it suppresses is an identical per-file
result that is still active and undismissed: reseeding twice with nothing changed in between is the
same confirmation the operator is already looking at, not news. `SeedOnceAsync` is the wrong sibling
here — its full-history comparison would mean a file confirms cleanly once and then never again, even
after the operator dismissed it.

That choice is what bounds the row count, and it is why no expiry is needed: dedupe-while-active caps
the notification at one live row per distinct per-file result, and dismissal is the operator's own
deliberate act rather than a condition some other code has to resolve.

`expiresAt` stays `null` and no `dismissTrigger` is set. Leaving `dismissTrigger` unset is load-bearing,
not incidental: `POST /admin/database/reseed` calls
`DismissByTriggerAsync(NotificationDismissTrigger.Reseed)` *after* `ReseedAsync()` returns, so a row
carrying that trigger would be dismissed by the very reseed that wrote it.

### 6. Know the version before writing, rather than falling back to null

**Status:** ⬜ Not started

**Developer direction, 2026-08-30: the app version must be known before any notification is added.**
`appVersionId: null` is not an acceptable outcome here — the initializer establishes the version, then
writes.

Inject `IAppVersionTracker` and `IVersionService` (both reachable — `IVersionService` lives in
`Quotinator.Core.Services`, the same project as this initializer). Take `GetLastActiveAsync()`'s row
id; if nothing has ever been recorded, call `RecordCurrentAsync` first and use that row.

**This recording must stay on the reseed path and must never move into `OnInitialisedAsync`.**
`Program.cs` reads `GetLastActiveAsync()` *after* `InitialiseAsync()` and strictly before its own
`RecordCurrentAsync` — that read is #81's what's-new catch-up lower bound, and its own comment states
that recording the current version first "would overwrite the answer". A reseed runs long after that
sequence completed, so recording there disturbs nothing.

The same constraint is why #304's producer passes `null` and cannot simply be corrected in passing: it
runs inside `OnInitialisedAsync`, before the version is recorded, and there is no position in that
sequence where it could learn the version without displacing #81's read.

**#304's null is out of scope here and #302 does not wait on it** (developer direction, 2026-08-30).
Every row in this issue's verification table is satisfiable against the code as it stands today,
because this issue writes only from the reseed path, where the version is already recorded. Nothing
below is a dependency — recorded only so the next reader does not re-derive why the startup-path
producer differs from this one.

The startup sequence is being revisited in a future milestone, which may dissolve the constraint rather
than require working around it: if seeding moves to run after serving begins, it also runs after the
current version is recorded, and the initializer's own producers gain the same provenance this step
gives #302.

### 7. Write nothing for a reseed that touches zero files

**Status:** ⬜ Not started

E.g. no configured sources. No notification at all, not an empty one. `SeedIfEmptyInternalAsync`'s
existing `effectiveBatches.Count == 0` early return already produces this; the step is to prove it
holds rather than to add code.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | One `Success` notification per file that reseeds with nothing left to review | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_WritesOneSuccessNotificationPerFile` |
| 2 | ❌ | No per-file notification on the first empty-database seed | Unit test | `DatabaseInitializerTests.Initialise_FirstEmptyDatabaseSeed_WritesNoPerFileNotification` |
| 3 | ❌ | No per-file notification for a file left awaiting review | Unit test | `DatabaseInitializerTests.Reseed_FileLeftAwaitingReview_WritesNoSuccessNotification` |
| 4 | ❌ | No notification for a reseed that touches zero files | Unit test | `DatabaseInitializerTests.Reseed_NoConfiguredFiles_WritesNoNotification` |
| 5 | ❌ | An identical per-file result is not written again while the first notification is still active | Unit test | `DatabaseInitializerTests.Reseed_TwiceWithNoChange_DoesNotRewriteTheActiveNotification` |
| 6 | ❌ | After the operator dismisses it, the next reseed confirms the same file again | Unit test | `DatabaseInitializerTests.Reseed_AfterDismissal_WritesTheConfirmationAgain` |
| 7 | ❌ | A file whose added/modified counts changed notifies separately rather than being suppressed | Unit test | `DatabaseInitializerTests.Reseed_WithDifferentCounts_WritesASecondNotification` |
| 8 | ❌ | The notification has no expiry and is not dismissed by the reseed that wrote it | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_NotificationHasNoExpiryAndSurvivesReseedDismissal` |
| 9 | ❌ | The notification records the app version, recording one first when none exists | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_NotificationRecordsAppVersionProvenance` |
| 10 | ❌ | No notification is ever written with a null `AppVersionId` | Unit test | `DatabaseInitializerTests.Reseed_WithNoRecordedVersion_RecordsOneBeforeWriting` |
| 11 | ❌ | Payload round-trips the file name and both counts through the `Metadata` column | Unit test | `ReseedFileAppliedMetadataTests.Payload_RoundTripsFileNameAndCounts` |
| 12 | ❌ | Two different files produce different identities | Unit test | `ReseedFileAppliedMetadataTests.Identity_DiffersByFileAndCounts` |
| 13 | ❌ | The new kind has a registered payload type | Unit test | `NotificationMetadataKindsTests` (existing guard; fails on an unregistered member) |
| 14 | ❌ | Migration 17 and the baseline accept the same `MetadataKind` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 15 | ❌ | Migration 17 and the baseline produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 16 | ❌ | Title and body exist non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 17 | ❌ | A real reseed against a real database writes one notification per cleanly-applied file, readable through the API | Automated (T2) | `docs/automated-testing/notifications-and-changelog/11-clean-reseed-confirmation.md` |
| 18 | ❌ | The notifications render in the startup modal and on `/notifications` after a live reseed | Live | T1: run a reseed from `/notifications`, confirm one `Success` notification per cleanly-applied file in both surfaces |
