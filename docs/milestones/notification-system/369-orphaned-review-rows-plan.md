# #369 — A review row whose batch is gone offers decisions that cannot be carried out

**Status:** Planning
**GitHub issue:** #369
**Tiers required:** T1, T2
**Depends on:** #303, #372

---

## Description

`/import-review` and `GET /import/actions` list a `Pending` action whose `Import_Batch` row no longer
exists as work awaiting a decision. The row renders its raw batch id in the **File** column — the name
is read from `Import_Batch.Name`, and there is no row to read — and offers **Keep existing** /
**Take incoming**, neither of which can succeed.

Found in T1 on 2026-09-01, staging four conflicting files and reseeding twice. Confirmed against the
database:

```
055c95c5-…  1 action  ORPHAN                     5916365c-…  1 action  conflicting.json
46b6aab9-…  1 action  ORPHAN                     a0b1209c-…  1 action  conflicting-1.json
bcd9ba25-…  1 action  ORPHAN                     aaed69d1-…  1 action  conflicting-2.json
c5449117-…  1 action  ORPHAN                     e0ae51fd-…  1 action  conflicting - Copy.json
```

Eight rows, four actionable.

**This predates #303.** `Sql.SystemImportActions` carries no join to `Import_Batch`, so the REST
endpoint returns the same eight rows; the page made it legible, it did not cause it.

## Cross-check against authoritative sources, 2026-09-10

Per `docs/workflow/process.md`'s Planning step 3, in its stated order: ADRs, JSON schemas,
generator/script behaviour, C# models, project documentation.

### Architecture decisions

1. **ADR 014 governs this issue, and the issue body never mentions it.** It names
   `System_ImportActions` — today's `Import_Action`, per ADR 015's renaming — as one of the four
   audit-trail tables in its scope, and an action whose batch is gone is precisely the dangling
   reference it rules on. **No conflict, and the reason is load-bearing:** ADR 014 forbids an
   *automatic* purge or a stored flag for dangling rows, and this design stores nothing. "The batch is
   gone" is derived at render time from the absence of the row, and dismissal is an explicit operator
   action producing the already-existing `Discarded` status, not a sweep. Worth recording because one
   of the two alternatives the issue rejected — deleting `Import_Action` during reseed — would have
   breached ADR 014 head-on, and nothing in the issue said so.

2. **ADR 014 is already current on #372**, corrected 2026-09-04 to record that reseed no longer
   deletes domain rows. That is an independent confirmation of code finding 6 below, from the source
   process.md ranks first.

3. **ADR 017 governs step 2's new read.** It joins `System_Notification` to its translation table, so
   its SQL execution goes through `JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` rather than
   a raw `connection.QueryAsync` — with a domain-shaped reader above it, which the ADR explicitly
   allows for a batched or dictionary-shaped return. Absent this check the read would have been
   hand-rolled, which is the exact recurrence ADR 017 was written to stop.

4. **ADR 012, 016 and 018 each constrain a detail of the same step.** Batch-id matching is
   case-insensitive on both sides (the page's existing lookup is already `OrdinalIgnoreCase`, and the
   new one matches it); a join-result POCO carries the `Dto` suffix and any new enum its own `Enums/`
   folder; the reader lives in `Quotinator.Data` alongside the notification types it reads.

### JSON schemas, generators and scripts

5. **Neither engages.** This issue reads no file input, so ADR 021 does not apply, and it touches no
   generator. Stated rather than skipped, so the next reader knows the order was walked and not that
   two steps were forgotten.

### C# models and project documentation

6. **The mechanism the issue describes no longer exists.** #372 removed `TruncateDataAsync` entirely —
   a reseed deletes nothing, `Import_Batch` included. Exactly one path in `src/` removes a batch row:
   `SqliteImportActionService.ReverseBatchAsync` soft-deletes it as its final step, and that path
   requires every action to be `Applied` and leaves them `Applied` — a status `AwaitingReview`
   excludes. So **no current path produces a new orphaned review row.**

7. **What remains is a legacy population and a defensive requirement.** A database that ran a
   pre-#372 build keeps its orphaned actions permanently: nothing purges `Import_Action`, and the
   batch cannot come back. Beyond those, the requirement stands on its own terms (developer,
   2026-09-10, taken over rescoping to legacy-only and over closing the issue): a row whose batch
   resolves to nothing must be handled because the page cannot assume the record it refers to still
   exists — not because a specific code path is currently producing one.

8. **The issue's "the notification side is already correct" contrast is gone.** #372 deleted
   `DismissAlertsForRemovedBatchesAsync` and `BatchIdsAsync`; `QuotinatorDatabaseInitializer.cs`'s
   remark at the removal site records why, and names this issue as where a producer for
   `NotificationDismissReason.Obsolete` may reappear. That producer is deliberately **not** reinstated
   here — see step 6.

9. **The durable copy of the file name is on a dismissed notification.** Pre-#372, the batches that
   got orphaned are precisely the ones whose `ImportReviewPending` alert was marked `Obsolete`. So the
   lookup cannot go through `INotificationReader.GetActiveNotificationsAsync`, which excludes dismissed
   rows; `GetPagedAsync` includes them but is paginated and carries no metadata-kind filter. Neither
   existing read serves this — step 2 adds one.

10. **Two documentation leftovers from #372 sit inside this issue's own blast radius**, and are
    corrected here because this work reads the type they describe:
    `ImportReviewPendingMetadataDto`'s remarks still state that "a reseed truncates `Import_Batch`, and
    every alert whose batch went with it is dismissed with the `Obsolete` reason", and
    `Sql.SystemImportActions.DeleteAll` has no reference anywhere in the repository.

11. **No `QTN-` code is allocated here**, per #333 requirement 8's precedent (#348, #350): the
    mechanical sweep comes before any code assignment, and the new label is a UI string rather than an
    emitted message. The knowledgebase has no entries yet, so there is none to add this issue to.

## Design (developer decisions, 2026-09-01 and 2026-09-10)

Taken over both alternatives originally proposed — deleting `Import_Action` during reseed, and adding
a status meaning "superseded". Neither is needed, and neither costs a migration:

1. **A notification's metadata is self-sufficient at creation time, because the records it names may
   not survive it** (developer, 2026-09-10). `ImportReviewPendingMetadataDto` already writes
   `FileName` next to `BatchId` for this reason, so the name is recoverable when the batch is not.
   The page reads it from the notification — dismissed or not — rather than from `Import_Batch`.
2. **An unresolvable batch id is the signal that the action is no longer possible.** No new status —
   the fact is already known at render time.
3. **Such a row offers only dismiss.** `IImportActionService.DiscardBatchAsync` already discards a
   whole batch without touching a domain table, and every action in an orphaned batch is equally
   impossible, so they go together.
4. **The same test applies to the action a notification offers, not only to the page**
   (developer, 2026-09-10): an action must be able to tell from the metadata alone whether it can
   execute. `INotificationActionExecutor.CanExecute` currently sees only the trigger and answers
   `true` unconditionally for `ImportReviewResolved`, so the notification panel offers Keep/Take on a
   dead batch exactly as the review page does. This is the same defect one surface over, and it is
   fixed in the same issue rather than left to be found again.

---

## Steps

**Red and green are per step.** Step 1 writes every test and runs it red; each step below then re-runs
the rows it owns, observes them fail for their own reason, and leaves them green with every
already-green row still green. Steps execute strictly in order — a step run early borrows another
step's red.

### 1. Write every test first, and run them red

**Status:** ⬜ Not started

Exit condition: every unit test in the verification table exists and **fails on its own assertion**,
and the new T2 document has been run against a build from the commit before this work started and
failed there.

Rows 2, 5 and 11 are controls — they assert behaviour that must not change, so passing before and
after is what correct looks like. A control that starts red is testing the wrong thing.

Producing the orphan is itself part of the T2 document's setup and has no API route post-#372: stage a
batch, then delete its `Import_Batch` row with `scripts/testing/execute-sql.csx` against the
container's database. That is the honest way to reach a state no endpoint creates, and it is why the
document belongs in the suite rather than being folded into a unit test.

### 2. Read the file name from notification metadata, including dismissed alerts

**Status:** ⬜ Not started

Neither existing `INotificationReader` read serves this (cross-check finding 9). Add one returning
every `ImportReviewPending` metadata row regardless of dismissal, with its SQL in `Sql.Notifications`
per the string-centralisation policy. One read for the page, not one per row — the same N+1 rule the
existing batch lookup already follows.

**Its SQL execution goes through `JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>`, not a raw
`connection.QueryAsync`** — the read joins `System_Notification` to its translation table, which is
ADR 017's trigger. The domain-shaped reader sits above that and does the dictionary shaping, which
ADR 017 explicitly allows.

The mapping from those rows to a batch-id → file-name dictionary is an `internal static` method on the
page, so it can be asserted without rendering the component — this project has no bUnit, the same
reason `AwaitingReview` and `FileNameFor` are already shaped that way.

### 3. Make "batch no longer exists" a first-class predicate on the row

**Status:** ⬜ Not started

One `internal static` predicate — the batch id matches no live `Import_Batch` row — decided in one
place rather than inferred at each call site, so the page and its tests agree on one definition.

Rendered visibly, with a localised label in all three `UI.*.json` files. A row that is orphaned **and**
has no notification to name its file shows that same label in the **File** column: there is no durable
name for it, and saying so is the honest state. This settles the question the issue left open about a
batch staged before #303 shipped — it is in scope, and it falls out of this predicate rather than
needing a case of its own.

### 4. Offer only dismiss on such a row

**Status:** ⬜ Not started

Keep/Take removed, not disabled-and-still-shown — they are impossible, not unavailable. Dismiss calls
`DiscardBatchAsync`, which reads and writes `Import_Action` only and never touches the batch row, so it
is already correct against a missing parent. It has never been exercised that way, which is what rows 8
and 9 prove.

### 5. Let a notification action decline when its own metadata names something gone

**Status:** ⬜ Not started

`CanExecute` gains access to the notification's metadata so `ImportReviewResolved` can answer `false`
when the batch it names is gone, and `NotificationTable` hides the run control for that row.

The capability check must not become a per-row database query at render time — the page does one read
and passes it in, the same constraint step 2 works under.

### 6. Replace #303's id-fallback requirement, and correct #372's leftovers

**Status:** ⬜ Not started

`ImportReviewPageTests.FileNameFor_UnknownBatch_FallsBackToTheId` asserts the behaviour this issue
removes. It is replaced, not deleted quietly — the replacement asserts the name resolves from the
notification instead, and a second row covers the no-notification case.

`ImportReviewPendingMetadataDto`'s remarks and the unreferenced `Sql.SystemImportActions.DeleteAll` are
corrected in the same commit (cross-check finding 10).

**`NotificationDismissReason.Obsolete` gains no producer here.** Its removal site names this issue as
where one may reappear, and the answer is that it should not: dismissal is the operator's explicit act
via step 4, not something the page decides on their behalf. Stated so the next reader does not treat
the omission as an oversight.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | An orphaned row's file name resolves from notification metadata | Unit test | `ImportReviewPageTests.FileNameFor_BatchGone_ResolvesTheNameFromNotificationMetadata` |
| 2 | ❌ | A live batch row still wins over the notification copy | Unit test | `ImportReviewPageTests.FileNameFor_KnownBatch_ReportsTheFileItWasImportedFrom` — existing control, stays green |
| 3 | ❌ | The metadata lookup includes alerts already dismissed | Unit test | `ImportReviewPageTests.FileNamesFromNotifications_IncludesDismissedAlerts` |
| 4 | ❌ | The new reader returns `ImportReviewPending` metadata regardless of dismissal | Unit test | `NotificationReaderTests.GetImportReviewMetadataAsync_ReturnsDismissedAlertsToo` |
| 5 | ❌ | The new query and its join strategy pass the SQL guards | Unit test | `SqlQueryGuardTests` — existing enumeration; its `AllJoinStrategyBuildSqlCases` discovers the new `IJoinStrategy` by reflection, so no new row is needed. Control, stays green |
| 6 | ❌ | An orphaned row with no notification renders the unresolved label, not the batch id | Unit test | `ImportReviewPageTests.FileNameFor_BatchGoneAndNoNotification_RendersUnresolved` — replaces `FileNameFor_UnknownBatch_FallsBackToTheId` |
| 7 | ❌ | An orphaned row offers no Keep/Take even when it has ambiguous fields | Unit test | `ImportReviewPageTests.CanDecide_BatchGone_IsFalseDespiteAmbiguousFields` |
| 8 | ❌ | Discard succeeds against a batch whose row is missing | Unit test | `SqliteImportActionServiceTests.DiscardBatchAsync_BatchRowMissing_MarksActionsDiscarded` — real SQLite |
| 9 | ❌ | The coordinator discards an orphaned batch without touching a domain table | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_BatchRowMissing_MarksEveryActionDiscarded` |
| 10 | ❌ | A notification action declines when its metadata names a batch that is gone | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewWhoseBatchIsGone_IsFalse` |
| 11 | ❌ | A notification action still runs when its batch is live | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewWithLiveBatch_IsTrue` — control |
| 12 | ❌ | The panel asks the executor with the row's own metadata, not the trigger alone | Unit test | `NotificationTableTests.ExecutorCanRun_ImportReviewWhoseBatchIsGone_IsFalse` — needs an `internal static` seam; `ShowsRunControl`'s existing `executorCanRun: false` case already covers the propagation |
| 13 | ❌ | The unresolved label exists in every locale | Unit test | `TranslationCompletenessTests` — existing, covers the new key automatically |
| 14 | ❌ | Live: an orphaned row shows its file name, offers only dismiss, and dismissing clears it | Live (T2) | `docs/automated-testing/import-and-staged-actions/27-orphaned-review-row-offers-only-dismiss.md` |
| 15 | ❌ | Live: the same row's notification offers no Keep/Take | Live (T2) | Same document, its own section |
