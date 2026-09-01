# #303 — Notification + minimal review page: alert when a reseed leaves import actions pending review

**Status:** In progress (step 1)
**GitHub issue:** #303
**Tiers required:** T1, T2
**Depends on:** #278, #302, #304, #312, #319

---

## Description

When a reseed leaves a file's records genuinely ambiguous, the staging engine (#154) already tracks it
precisely and a full REST review surface already exists — but none of it is reachable from the Blazor
UI or surfaced proactively. An operator only finds out by knowing to check `/import/actions` or by
reading logs.

This issue adds the alert half of that feedback (paired with #302's success half), plus a minimal
in-app way to act on it.

**Scope boundary, confirmed with the developer 2026-08-12:** this is explicitly *not* the full
side-by-side diff/merge editor #66 (Blazor: Import UI milestone) envisions. That stays #66's own,
separately-scoped work.


## Scope revision — where the notification is written from

**Recorded 2026-08-12, relocated here from `overview.md` 2026-08-22.** Same relocation as #302: the
notification write moves into `QuotinatorDatabaseInitializer`'s own per-file seeding loop rather than a
post-hoc read of `LastSeedReport` from `AdminEndpoints.cs`. The existing `else` branch
(`applyResult is not null` — the batch was staged awaiting review, already logged via
`Logger.LogFileStagedAwaitingReview`) is the exact hook point.

The review page in steps 7–10 is unaffected by that relocation.

## Scope changes

**Reviewed against the code 2026-09-01, after #302 shipped.** Three claims were stale, one was wrong,
and every pattern #302 established was missing.

1. **The issue's step 1 — "same `INotificationWriter` injection as #302" — is already done.** #304 put
   `INotificationReader`/`INotificationWriter`/`INotificationTextSource` on
   `QuotinatorDatabaseInitializer`'s constructor, and #302 added `IAppVersionTracker`/`IVersionService`.
   No work, no verification row.

2. **The "open dedupe-helper decision" this plan told the reader to check in #302's step 1 was settled
   by #312**, which relocated `NotificationSeeding` into `Quotinator.Data`.

3. **The lifecycle question is settled by #302, not open here.** Write through
   `NotificationSeeding.SeedWhileUnresolvedAsync`, no expiry. `Quotinator:NotificationDefaultExpiryHours`
   no longer exists.

4. **`applyResult` cannot produce a per-status count.** The issue's step 2 says
   "`applyResult.PendingActionIds` is already available at this exact point", and it is — but
   `ImportActionBatchStatusResponse` carries only `BatchId` and a flat `IReadOnlyList<Guid>`. There is
   no status on it. The counts come from the `actions` list already in scope, grouped by
   `ImportActionStatus`, exactly as #302's per-entity breakdown does.

5. **Developer decision, 2026-09-01: the alert fires on the first seed too**, unlike #302's
   confirmation. The `isReseed` flag exists and is deliberately *not* used here: a fresh install whose
   bundled content staged conflicts genuinely has something to review, and nothing else in the UI says
   so — the startup modal reports aggregate counts, not that actions are waiting. A success
   confirmation on a first install is clutter; an unresolved-review alert is not.

6. **Developer decision, 2026-09-01: the alert is dismissed when its review is resolved**, and that
   implies a review *action* on the notification in future — the equivalent of #304's Run button,
   scoped as its own issue rather than folded in here. #303 delivers the link (step 10); the action
   itself is future work.

---

## Steps

### 1. Add the `ImportReviewPending` metadata kind and its payload

**Status:** ✅ Done — `ImportReviewPendingMetadataDto`, `ImportReviewCountDto`

A new `NotificationMetadataKind.ImportReviewPending`, registered in
`NotificationMetadataKinds.PayloadTypes`, plus `ImportReviewPendingMetadataDto` carrying `FileName`,
`Origin`, `BatchId`, and a count per reviewable `ImportActionStatus` (`Pending`, `Blocked`, `Stale`),
omitting any status with no rows.

`Origin` is `FileResourceOrigin`, mapped through `SeedBatchOriginExtensions.ToFileResourceOrigin` —
the helper #302 extracted. It is required for the same reason #302 needed it: `FileName` is a bare
name, and the bundled and imports directories can both hold it.

**`BatchId` is part of `IdentityComponents`** (developer decision, 2026-09-01): the batch *is* the set
of pending reviews the alert describes, so two batches are two alerts even for the same file. It is
also what step 5's dismissal matches on.

### 2. Widen the three CHECK constraints in one migration

**Status:** 🔄 In progress

`MetadataKind` gains `ImportReviewPending`, `DismissTriggerKey` gains step 5's new trigger, and
`DismissReason` gains `Obsolete` (step 6). All three ride one table rebuild rather than a rebuild each
— migration 15's own precedent, for the same reason (constraints on one table, copying every row three
times for no gain). The next free version in `DatabaseInitializer.DataOwnedMigrations`, **18** as of
2026-09-01.

`DataBaselineSql` is updated to match in the same commit, and both drift tests
(`...AcceptSameNotificationCheckConstraintValues`, `...ProduceIdenticalSystemNotificationSchema`) gain
the new values.

### 3. Add the alert's title and body in all three languages

**Status:** ⬜ Not started

Keys on `NotificationMessageKeys`, strings in `UI.en-GB.json`/`UI.nl.json`/`UI.de.json`, resolved via
`NotificationTranslations.Original`/`Build`.

**Split per origin, as #302 had to.** `bodyArgs` is one array applied to every language, so a localised
"bundled"/"your imported" cannot be an argument without rendering in one language for every reader.

The `Obsolete` display status needs its own label in the same three files, alongside the existing
Active/Expired/Dismissed/Resolved ones.

### 4. Write the alert from the staged branch

**Status:** ⬜ Not started

In the `applyResult is not null` branch, one `ActionRequired` notification per staged file, through
`SeedWhileUnresolvedAsync`. **Not gated on `isReseed`** — see Scope changes 5.

Provenance via the existing `CurrentAppVersionIdAsync`, which already records a version when none
exists rather than writing null.

### 5. Dismiss the alert when its batch is resolved

**Status:** ⬜ Not started

`SqliteImportActionService.ApplyBatchAsync`'s `pending is null` branch is the hook: it is already the
single choke point `/import/` and `/import/actions/apply` both funnel through, and #304 dismisses its
own recommendation there for exactly that reason. A discarded batch resolves the review too, so
`DiscardBatchAsync` needs the same call.

**`DismissByTriggerAsync` cannot be reused as-is — it dismisses every row carrying the trigger.** Two
files each leaving actions to review produce two alerts; resolving one batch would clear both.
Dismissal has to be scoped to the notification whose payload names *this* `BatchId`, which is a new
capability on `INotificationWriter`, not an existing one.

### 6. Dismiss alerts whose batch has been removed

**Status:** ⬜ Not started

**Developer decision, 2026-09-01: when a batch is removed, its alerts are dismissed — they describe a
review that can no longer be applied.** This is not a tidy-up bolted onto step 5; it is what keeps
alerts from accumulating, and it is load-bearing precisely because `BatchId` is in the identity.

`ImportBatchEntity.Id` is `Guid.NewGuid()` (`RecordBase`), so it is random per construction, never
derived from content. Two consequences follow, and they are why this design closes:

- A reseed can never reproduce a previous batch id. `TruncateDataAsync` hard-deletes every
  `Import_Batch` row, so every prior alert is dismissed here, and the new batches raise new alerts.
  Nothing accumulates, and no alert survives pointing at a batch that is gone.
- "The resulting batch id determines whether it alters an existing notification or creates a new one"
  therefore only ever *alters* within a single batch's own lifetime — a batch that gains further
  actions before being resolved. A reseed always takes the create branch.

**A third `NotificationDismissReason` member, `Obsolete`** (developer decision, 2026-09-01): an
inactive notification has to explain itself without anyone running an audit to work out what happened.

Neither existing value can do that here. `Resolved` means the thing was actually dealt with — #304's
own definition — and a truncated batch was abandoned, not reviewed; `Dismissed` means the user set it
aside, which they did not. Recording either would tell the reader something untrue, which is the exact
defect `NotificationDismissReason` was introduced to fix: before #304 both cases collapsed into
`IsDismissed = 1`, and a user who had run an action was told they had declined it.

`Obsolete` means the condition the notification described no longer exists, so it could neither be
acted on nor be said to have been carried out. Named for the state rather than the cause (`Superseded`
would imply something replaced it, which is only true when the file is reseeded rather than removed
from the manifest).

Its CHECK widening rides migration 18 alongside the other two, and `NotificationDisplayStatus` gains a
matching member so the Status column reads it rather than falling back to "Dismissed".

### 7. Build the minimal review page

**Status:** ⬜ Not started

A new Blazor page listing every currently active (undecided) `Pending`/`Blocked`/`Stale`
`ImportAction` row across all batches — not scoped to one notification's file. Injects
`IImportActionReader`/`IImportActionService` directly, matching `Notifications.razor`'s precedent.

Code-behind partial class per CLAUDE.md's Blazor rules — no inline `@code`, no `@inject`.

### 8. Give each row a basic decide action

**Status:** ⬜ Not started

The field-level keep/replace/custom decision `POST /import/actions/{id}/decide` already accepts. No
side-by-side diff view, no bulk actions, no inline merge editor — all #66's scope.

### 9. Register the page in navigation and the health gate

**Status:** ⬜ Not started

`NavMenu.razor`, and the literal array in `DatabaseHealthGateMiddleware` that already lists
`"/notifications"`. The page must stay reachable during a degraded startup, which is exactly when an
operator needs to see what is unresolved.

### 10. Link the notification to the review page

**Status:** ⬜ Not started

Last, so it points at a page that exists. The first-class review *action* on the notification is
deliberately not here — see Scope changes 6.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | One `ActionRequired` alert per file left awaiting review | Unit test | `DatabaseInitializerTests.Reseed_FileLeftAwaitingReview_WritesPendingReviewAlert` |
| 2 | ❌ | No alert for a file that applied cleanly | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_WritesNoPendingReviewAlert` |
| 3 | ❌ | The alert fires on the first empty-database seed, not only on a reseed | Unit test | `DatabaseInitializerTests.Initialise_FirstSeedWithConflicts_WritesPendingReviewAlert` |
| 4 | ❌ | Counts are per `ImportActionStatus`, covering Pending, Blocked and Stale | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_CountsEachReviewableStatus` |
| 5 | ❌ | A status with no rows is omitted rather than reported as zero | Unit test | `DatabaseInitializerTests.Reseed_StatusWithNoRows_IsAbsentFromTheAlert` |
| 6 | ❌ | Payload round-trips file name, origin, batch id and counts | Unit test | `ImportReviewPendingMetadataTests.Payload_RoundTripsAllFields` |
| 7 | ✅ | Two same-named files from different directories are two alerts | Unit test | `ImportReviewPendingMetadataTests.Identity_DiffersByOrigin` |
| 8 | ✅ | A different batch is a different alert, even for the same file and workload | Unit test | `ImportReviewPendingMetadataTests.Identity_DiffersByBatch` |
| 9 | ❌ | The alert records the app version that wrote it | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_AlertRecordsAppVersionProvenance` |
| 10 | ✅ | The new kind has a registered payload type | Unit test | `NotificationMetadataKindsTests` (existing guard) |
| 11 | ❌ | Migration 18 and the baseline accept the same `MetadataKind`, `DismissTriggerKey` and `DismissReason` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 12 | ❌ | Migration 18 and the baseline produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 13 | ❌ | Title and body exist non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 14 | ❌ | Resolving a batch dismisses that batch's alert, with reason `Resolved` | Unit test | `SqliteImportActionServiceTests.ApplyBatch_WhenFullyResolved_DismissesItsOwnReviewAlert` |
| 15 | ❌ | Resolving one batch does not dismiss another batch's alert | Unit test | `SqliteImportActionServiceTests.ApplyBatch_DoesNotDismissAnotherBatchesReviewAlert` |
| 16 | ❌ | Discarding a batch dismisses its alert too | Unit test | `SqliteImportActionServiceTests.DiscardBatch_DismissesItsOwnReviewAlert` |
| 17 | ❌ | A reseed dismisses every alert whose batch it truncated, with reason `Obsolete` | Unit test | `DatabaseInitializerTests.Reseed_DismissesAlertsForRemovedBatches` |
| 18 | ❌ | `Obsolete` is distinguishable from `Dismissed` and `Resolved` on a stored row | Unit test | `NotificationWriterTests.DismissedAsObsolete_ReadsBackAsObsolete` |
| 19 | ❌ | The Status column renders `Obsolete` rather than falling back to "Dismissed" | Unit test | `NotificationTableTests.GetDisplayStatus_ObsoleteReason_ReportsObsolete` |
| 20 | ❌ | A reseed's new alerts are distinct rows from the previous run's, not updates to them | Unit test | `DatabaseInitializerTests.Reseed_Twice_RaisesNewAlertsRatherThanReusingTheOld` |
| 21 | ❌ | Alerts do not accumulate across repeated reseeds — only the newest batch's are active | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_LeavesOnlyTheLatestBatchesAlertsActive` |
| 22 | ❌ | The review page lists every active `Pending`/`Blocked`/`Stale` action across all batches | Unit test | `ImportReviewPageTests.Lists_EveryActiveActionAcrossBatches` |
| 23 | ❌ | Deciding a row removes it from the active list | Unit test | `ImportReviewPageTests.DecidedRow_LeavesTheActiveList` |
| 24 | ❌ | The page is exempt in `DatabaseHealthGateMiddleware` | Unit test | `DatabaseHealthGateMiddlewareTests` (alongside the existing `/notifications` case) |
| 25 | ❌ | All four seeding variants behave correctly against real configuration | Automated (T2) | `automated-testing/import-and-staged-actions/NN-pending-review-alert.md` |
| 26 | ❌ | The alert reaches `/notifications` and the startup modal, and a clean seed produces none | Automated (T2) | same document — modal asserted after a restart, per #302's step 8 |
| 27 | ❌ | The page renders during a degraded startup rather than 500 | Automated (T2) | same document, degraded container |
| 28 | ❌ | Every dismiss reason is visible on the notifications page without consulting the audit trail | Live | T1: after a reseed supersedes an earlier alert, the inactive row reads `Obsolete`, not `Dismissed` |
| 29 | ❌ | The alert, the page and the link render correctly | Live | T1: stage a batch with conflicts, click through from the alert, decide a row |

**The four seeding variants (rows 20) are not optional.** #303 writes from the same seeding loop as
#302, where that matrix found a defect no single-variant test reached — no files, bundled only, user
imports only, both.
