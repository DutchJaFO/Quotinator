# #303 — Notification + minimal review page: alert when a reseed leaves import actions pending review

**Status:** Waiting for release
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

The review page in steps 8–11 is unaffected by that relocation.

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
   scoped as its own issue rather than folded in here. #303 delivers the link (step 11); the action
   itself is future work.

---

## Steps

### 1. Add every enum member this issue introduces

**Status:** ✅ Done

Four members, in one step and before the migration that constrains three of them:

- `NotificationMetadataKind.ImportReviewPending` — the payload shape (step 2).
- `NotificationDismissTrigger.ImportReviewResolved` — what supersedes the alert (step 6).
- `NotificationDismissReason.Obsolete` — why a superseded alert went inactive (step 7).
- `NotificationTable.NotificationDisplayStatus.Obsolete` — how that reads on the page. Delivered here
  with its own case and label, because `NotificationTable.razor`'s `default` branch renders **Active**:
  an unmapped member would have shown a superseded alert as active.

**Restructured mid-execution, 2026-09-01 — recorded because it was a planning failure.** These were
originally spread across steps 1, 5 and 6, with the migration widening all three CHECKs at step 2. That
left the schema accepting values whose C# members did not exist for the entire middle of the plan, and
it surfaced as a build warning the moment step 1's payload documentation referenced
`NotificationDismissReason.Obsolete` by cref. Which enums an issue needs is knowable before any code is
written; discovering it one step at a time is the same completeness gap #302's own deviation section
records.

### 2. Add the `ImportReviewPending` payload

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
also what step 6's dismissal matches on.

### 3. Widen the three CHECK constraints in one migration

**Status:** ✅ Done — migration 18, `NotificationImportReviewMigrations`

`MetadataKind` gains `ImportReviewPending`, `DismissTriggerKey` gains step 6's trigger, and
`DismissReason` gains `Obsolete` (step 7). All three ride one table rebuild rather than a rebuild each
— migration 15's own precedent, for the same reason (constraints on one table, copying every row three
times for no gain). The next free version in `DatabaseInitializer.DataOwnedMigrations`, **18** as of
2026-09-01.

`DataBaselineSql` is updated to match in the same commit, and both drift tests
(`...AcceptSameNotificationCheckConstraintValues`, `...ProduceIdenticalSystemNotificationSchema`) gain
the new values.

### 4. Add the alert's title and body in all three languages

**Status:** ✅ Done — `ImportReviewPendingTitle`, plus a bundled and a user body

Keys on `NotificationMessageKeys`, strings in `UI.en-GB.json`/`UI.nl.json`/`UI.de.json`, resolved via
`NotificationTranslations.Original`/`Build`.

**Split per origin, as #302 had to.** `bodyArgs` is one array applied to every language, so a localised
"bundled"/"your imported" cannot be an argument without rendering in one language for every reader.

The `Obsolete` display status needs its own label in the same three files, alongside the existing
Active/Expired/Dismissed/Resolved ones. Delivered in step 1, with the enum member it renders — an enum
value the page cannot draw is not finished, and `NotificationTable.razor`'s `default` case would have
shown it as **Active**.

**The body carries the total awaiting review, not the per-status list.** Stated rather than left
implicit: how many reviewable states actually have rows varies per file, and a variable-length list
cannot be built from `bodyArgs` without composing localised words outside the translation files — the
same constraint that split these bodies by origin. The per-status breakdown is in the payload, which is
where the review page reads it. Say so if the body itself should enumerate the states.

### 5. Write the alert from the staged branch

**Status:** ✅ Done — `AlertReviewPendingAsync`

In the `applyResult is not null` branch, one `ActionRequired` notification per staged file, through
`SeedWhileUnresolvedAsync`. **Not gated on `isReseed`** — see Scope changes 5.

Provenance via the existing `CurrentAppVersionIdAsync`, which already records a version when none
exists rather than writing null.

### 6. Dismiss the alert when its batch is resolved

**Status:** ✅ Done — `INotificationWriter.DismissByTriggerAndBatchAsync`, wired at apply and discard

`SqliteImportActionService.ApplyBatchAsync`'s `pending is null` branch is the hook: it is already the
single choke point `/import/` and `/import/actions/apply` both funnel through, and #304 dismisses its
own recommendation there for exactly that reason. A discarded batch resolves the review too, so
`DiscardBatchAsync` needs the same call.

**`DismissByTriggerAsync` cannot be reused as-is — it dismisses every row carrying the trigger.** Two
files each leaving actions to review produce two alerts; resolving one batch would clear both.
Dismissal has to be scoped to the notification whose payload names *this* `BatchId`, which is a new
capability on `INotificationWriter`, not an existing one.

### 7. Dismiss alerts whose batch has been removed

**Status:** ✅ Done — `DismissAlertsForRemovedBatchesAsync`, called before the reseed restages

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

### 8. Build the minimal review page

**Status:** ✅ Done — `/import-review`, selection in `ImportReview.AwaitingReview`

A new Blazor page listing every currently active (undecided) `Pending`/`Blocked`/`Stale`
`ImportAction` row across all batches — not scoped to one notification's file. Injects
`IImportActionReader`/`IImportActionService` directly, matching `Notifications.razor`'s precedent.

Code-behind partial class per CLAUDE.md's Blazor rules — no inline `@code`, no `@inject`.

### 9. Give each row a basic decide action

**Status:** ✅ Done — `ImportReview.DecisionRows`, keep/take per row

Two controls per row — keep existing, take incoming — each resolving the **whole action**.

**Developer decision, 2026-09-01: they resolve only the fields actually in conflict**, which is the
degenerate case of the git model this is eventually meant to become: `--ours`/`--theirs` resolve the
conflicted hunks and leave the rest of the merge alone. The action's own `AmbiguousFields` is that set.
Deciding every *decidable* field instead would overwrite fields nobody was asked about — including
nulling one the incoming file simply does not carry.

A `Blocked` action has no ambiguous fields: it is held because it would touch a protected field, not
because two values disagree. A whole-action decision therefore has nothing to resolve for it, and must
say so rather than reporting success for a no-op.

The page reads `IImportActionService.GetPagedAsync`, not `IImportActionReader`'s — the service returns
`ImportActionSummaryResponse`, which carries `AmbiguousFields`/`ExistingFields`/`IncomingFields`. The
reader returns raw entities, which do not. Step 8 used the reader and is corrected here.

No side-by-side diff view, no per-field control, no bulk actions — all #66's scope.

### 10. Give the notification the same basic options

**Status:** ✅ Done — `ImportReviewResolved` case in `NotificationActionExecutor`, `IImportActionService.DecideBatchAsync`

**Developer decision, 2026-09-01: the controls live in both places.** The alert carries the coarse,
whole-batch form of the same two options; the page carries them per action.

The alert is where an operator first learns of the conflict, so making them navigate before they can
act on "keep everything as it is" is friction for the common case. The page is where a mixed backlog
gets cleared one action at a time.

Runs through `INotificationActionExecutor`, the same mechanism #304's Run button uses, with the alert's
own `BatchId` payload naming what to act on.

**This is the interim shape, not the destination.** The notification will eventually point at an
item-by-item resolution UX (#66) rather than carrying the decision itself; these two options exist so
#303 is workable in the meantime, given the expectation that most conflicts are fixed by correcting the
incoming file and reseeding rather than resolved by hand.

### 11. Register the page in navigation and the health gate

**Status:** ✅ Done — `NavMenu.razor`, and `/import-review` in the exempt list

`NavMenu.razor`, and the literal array in `DatabaseHealthGateMiddleware` that already lists
`"/notifications"`. The page must stay reachable during a degraded startup, which is exactly when an
operator needs to see what is unresolved.

### 12. Link the notification to the review page

**Status:** ✅ Done — a *Review each change* link beside the alert's own options

Last, so it points at a page that exists. The first-class review *action* on the notification is
deliberately not here — see Scope changes 6.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | One `ActionRequired` alert per file left awaiting review | Unit test | `DatabaseInitializerTests.Reseed_FileLeftAwaitingReview_WritesPendingReviewAlert` |
| 2 | ✅ | No alert for a file that applied cleanly | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_WritesNoPendingReviewAlert` |
| 3 | ✅ | The alert fires on the first empty-database seed, not only on a reseed | Unit test | `DatabaseInitializerTests.Initialise_FirstSeedWithConflicts_WritesPendingReviewAlert` |
| 4 | ✅ | Counts are per `ImportActionStatus`, covering Pending, Blocked and Stale | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_CountsEachReviewableStatus` |
| 5 | ✅ | A status with no rows is omitted rather than reported as zero | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_CountsEachReviewableStatus` (asserts every entry has a non-zero count) |
| 6 | ✅ | The alert names the batch and file it reports | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_AlertNamesItsBatchAndFile` |
| 7 | ✅ | Payload round-trips file name, origin, batch id and counts | Unit test | `ImportReviewPendingMetadataTests.Payload_RoundTripsAllFields` |
| 8 | ✅ | Two same-named files from different directories are two alerts | Unit test | `ImportReviewPendingMetadataTests.Identity_DiffersByOrigin` |
| 9 | ✅ | A different batch is a different alert, even for the same file and workload | Unit test | `ImportReviewPendingMetadataTests.Identity_DiffersByBatch` |
| 10 | ✅ | The alert records the app version that wrote it | Unit test | `DatabaseInitializerTests.Reseed_StagedFile_AlertRecordsAppVersionProvenance` |
| 11 | ✅ | The new kind has a registered payload type | Unit test | `NotificationMetadataKindsTests` (existing guard) |
| 12 | ✅ | Migration 18 and the baseline accept the same `MetadataKind`, `DismissTriggerKey` and `DismissReason` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 13 | ✅ | Migration 18 and the baseline produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 14 | ✅ | Title and body exist non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 15 | ✅ | Resolving a batch dismisses that batch's alert, with reason `Resolved` | Unit test | `SqliteImportActionServiceTests.ApplyBatch_WhenFullyResolved_DismissesItsOwnReviewAlert` |
| 16 | ✅ | Resolving one batch does not dismiss another batch's alert | Unit test | `SqliteImportActionServiceTests.ApplyBatch_DoesNotDismissAnotherBatchesReviewAlert` |
| 17 | ✅ | Discarding a batch dismisses its alert too | Unit test | `SqliteImportActionServiceTests.DiscardBatch_DismissesItsOwnReviewAlert` |
| 18 | ✅ | A reseed dismisses every alert whose batch it truncated, with reason `Obsolete` | Unit test | `DatabaseInitializerTests.Reseed_DismissesAlertsForRemovedBatches` |
| 19 | ✅ | `Obsolete` is distinguishable from `Dismissed` and `Resolved` on a stored row | Unit test | `NotificationWriterTests.DismissedAsObsolete_ReadsBackAsObsolete` |
| 20 | ✅ | The Status column renders `Obsolete` rather than falling back to "Dismissed" | Unit test | `NotificationTableTests.GetDisplayStatus_ObsoleteReason_ReportsObsolete` |
| 21 | ✅ | A reseed's new alerts are distinct rows from the previous run's, not updates to them | Unit test | `DatabaseInitializerTests.Reseed_Twice_RaisesNewAlertsRatherThanReusingTheOld` |
| 22 | ✅ | Alerts do not accumulate across repeated reseeds — only the newest batch's are active | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_LeavesOnlyTheLatestBatchesAlertsActive` |
| 23 | ✅ | The review page lists every active `Pending`/`Blocked`/`Stale` action across all batches | Unit test | `ImportReviewPageTests.Lists_EveryActiveActionAcrossBatches` |
| 24 | ✅ | Deciding a row removes it from the active list | Unit test | `ImportReviewPageTests.DecidedRow_LeavesTheActiveList` |
| 25 | ✅ | A row whose stored status cannot be parsed is not shown as awaiting review | Unit test | `ImportReviewPageTests.UnparseableStatus_IsNotTreatedAsAwaitingReview` |
| 26 | ✅ | A whole-action decision covers only the conflicted fields, not every decidable one | Unit test | `ImportReviewPageTests.Decision_CoversOnlyTheAmbiguousFields` |
| 27 | ✅ | Taking the incoming side sets the opposite choice on the same fields | Unit test | `ImportReviewPageTests.Decision_TakingIncoming_SetsTheOppositeChoice` |
| 28 | ✅ | An action with nothing in conflict produces no decision rather than an empty success | Unit test | `ImportReviewPageTests.Decision_ActionWithNoAmbiguousFields_ProducesNoRows` |
| 29 | ✅ | The notification offers the same keep/take options at batch scope | Unit test | `NotificationActionExecutorTests.ImportReviewResolved_KeepExisting_DecidesEveryActionInTheBatch` |
| 30 | ✅ | The notification's action refuses to pick a side, or a batch, on the operator's behalf | Unit test | `NotificationActionExecutorTests.ImportReviewResolved_WithoutAChoice_Throws`, `...WithoutItsPayload_Throws` |
| 31 | ✅ | The trigger is executable, so the alert renders its controls | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewResolved_ReturnsTrue` |
| 32 | ✅ | The page is exempt in `DatabaseHealthGateMiddleware` | Unit test | `DatabaseHealthGateMiddlewareTests.Unhealthy_ExemptPath_CallsNext("/import-review")` |
| 33 | ✅ | A staged file raises an alert naming its batch, file, origin and per-status counts | Automated (T2) | `automated-testing/import-and-staged-actions/20-pending-review-alert.md` steps 1–2 |
| 34 | ✅ | The alert reaches `/notifications`, the startup modal after a restart, and the review page | Automated (T2) | same document, steps 3–4 |
| 35 | ✅ | Resolved and obsolete are distinguishable in one history, and alerts stay bounded | Automated (T2) | same document, steps 5–6 |
| 36 | ✅ | `/import-review` behaves as `/notifications` does on a degraded container | Automated (T2) | same document, step 7 — both currently `500`, a pre-existing defect recorded below |
| 37 | ❌ | Every dismiss reason is visible on the notifications page without consulting the audit trail | Live | T1: with the fixture staged, reseed twice; the inactive rows read `Obsolete` and `Resolved`, not both `Dismissed` |
| 38 | ❌ | The alert, its options, the page and the link render correctly | Live | T1: `dotnet-script scripts/testing/stage-import-conflict.csx -- --imports src/Quotinator.Api/bin/Debug/net10.0/data/imports`, restart, then use an option from the alert, click through, and decide a row |
| 39 | ✅ | The page names the file a conflict came from, not its batch id | Unit test | `ImportReviewPageTests.FileNameFor_KnownBatch_ReportsTheFileItWasImportedFrom` |
| 40 | ✅ | An action whose batch no longer exists still shows something traceable | Unit test | `ImportReviewPageTests.FileNameFor_UnknownBatch_FallsBackToTheId` |
| 41 | ✅ | The nav entry has an icon, like every other entry | Live | Screenshot, 2026-09-01: the clipboard-check icon renders in the sidebar beside *Import review* |
| 42 | ✅ | Two staged files raise two alerts, each naming its own file | Automated (T2) | `stage-import-conflict.csx --count 2` against a container: `pending actions = 2`, `active alerts = 2`, `conflicting-1.json` and `conflicting-2.json` |

**T1 needs a staged conflict, because the bundled files cannot produce one** (developer, 2026-09-01).
Neither T1 nor T2 can reach this issue's behaviour with bundled content alone: a first seed inserts
everything as an Add, so nothing disagrees with anything, and `Quotinator__DefaultConflictPolicy=Review`
does not help because the manifest's per-file policy overrides it.

`scripts/testing/stage-import-conflict.csx` writes a user-imports file re-stating a real bundled quote's
id with different text under a `review` policy. The user-imports batch seeds after the bundled ones, so
it meets content already stored — the only shape that stages a decision. Verified against a container
before being written into these rows: one Pending action, one alert, `origin=User`.

Delete the two files it writes (`conflicting.json`, `manifest.json`) from the imports directory to
return to a clean seed.

**T1 found the page was showing a batch id where a file name belongs** (developer, 2026-09-01). The
column was correct and useless: an operator cannot act on a GUID, and what they need to know is which
file to go and fix — which is the whole workflow this issue assumes. `Import_Batch.Name` already holds
that file name, so the page resolves it in one read for the whole table rather than per row. Rows 39
and 40 pin both the mapping and the fallback.

**"Two files to import, but only one is mentioned" — the fixture, not the producer** (developer,
2026-09-01). One alert per staged file is the design, and row 42 now proves it: two files staged
against two different bundled quotes raise two alerts, each naming its own file.

What the T1 run actually hit was two traps in the fixture, both now closed by
`stage-import-conflict.csx --count`:

- **A hand-added second copy of the file does not stage anything.** Two files pointing at the *same*
  quote produce one conflict, not two: the first file's change is applied, and the second then agrees
  with what is now stored. Each conflicting file needs a different target quote.
- **A missing manifest is worse than it looks.** `ManifestSeedPlanner.TryWriteAutoManifest` writes an
  auto-manifest with *no* `duplicateResolution`, so every file falls back to the configuration default
  — `newest-wins` (`ManifestPolicy.HardcodedDefault`, and `ConflictPolicyParser.Parse(null)`). That
  does not stage nothing; it **applies the incoming value over the stored one without asking**. The T1
  log shows exactly this: "auto-created manifest.json listing 2 file(s)", then
  `Quote[new=0 modified=1 … pending=0]` for both — `modified=1` is the change landing, not being
  skipped. No error, no alert, and the data quietly different.

  Recorded because it was got wrong twice while writing this section: `skip` is what
  `data/sources/manifest.json` sets at *its own* top level, for the bundled directory. It has never
  governed the imports directory, and the difference is not cosmetic — one discards the incoming value,
  the other keeps it.

**The nav entry also shipped without an icon** (developer, 2026-09-01). Step 11 added the `NavLink`
with a class name but no matching rule, and every other entry has one — so it rendered as bare text.
`NavMenu.razor.css` now carries a `bi-clipboard-check` data-URI in the same white 16×16 form as its
neighbours. Confirmed by screenshot rather than by the class name being present in the markup: the
class was already there while the icon was missing, which is exactly the failure a text assertion
cannot see.

**T2 pass, 2026-09-01 — green, and it found four things no unit test could.**

1. **The bundled content cannot produce a conflict at all.** A first seed inserts everything as an Add,
   so there is nothing to disagree with; `Quotinator__DefaultConflictPolicy=Review` changes nothing
   because the manifest's per-file policy overrides it. The document now bind-mounts a user-imports
   file re-stating a bundled quote id with different text — the only shape that stages a decision, and
   incidentally the `origin=User` path.
2. **The discard route is `/actions/discard?batchId=`**, a query parameter rather than a route segment.
   The segment form returns `404`, which reads like "no such batch" rather than "no such endpoint".
3. **`obsolete` needs two reseeds, not one.** The first raises a fresh alert while the step-5 alert is
   already `resolved`; only the second truncates a batch whose alert is still active. The document's
   first draft checked after one reseed and would have asserted the wrong outcome.
4. **A pre-existing defect, not this issue's** — see below.

**`/notifications` returns `500` on a read-only data directory, and always has.** Found while checking
that `/import-review` degrades gracefully: both interactive pages fail identically, while `/about` and
`/stats` answer `200`. The health-gate exemption is not the problem — both paths are exempt and the
gate lets them through. `@rendermode InteractiveServer` needs DataProtection to encrypt its component
descriptor, and that cannot create `/data/keys` on a read-only mount:

```
System.Security.Cryptography.CryptographicException: An error occurred while trying to encrypt the provided data.
 ---> System.IO.IOException: Read-only file system : '/data/keys'
```

This contradicts what #326's exemption was understood to deliver: the route is reachable, the page is
not. It also bears directly on the open question CLAUDE.md records under "DataProtection keys" — that
the negative effects of a non-persistent key ring have never been explored, and that an ADR waits on
read-only-mode evidence (#332, #336). This is that evidence.

Out of scope for #303 and left unfixed here: it predates this issue, it is the same on a page #303 did
not touch, and fixing it is a DataProtection design decision rather than a notification one. Row 36
asserts parity with `/notifications` instead of `200`, so it passes today and keeps passing when the
underlying fault is fixed.

**The four seeding variants (rows 20) are not optional.** #303 writes from the same seeding loop as
#302, where that matrix found a defect no single-variant test reached — no files, bundled only, user
imports only, both.
