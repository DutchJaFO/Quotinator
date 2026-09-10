# #369 — A review row whose batch is gone offers decisions that cannot be carried out

**Status:** Waiting for release
**GitHub issue:** #369
**Tiers required:** T1, T2
**Depends on:** #303, #372, #389

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

11. **This issue produces knowledgebase material, and no `QTN-` code yet.** Both halves are
    deliberate. The condition it addresses has a concrete action attached — the batch is gone, and
    dismissing is the only thing left to do — which is exactly what the knowledgebase is meant to
    carry once it is surfaced to users as its own feature (developer, 2026-09-10). But code
    allocation still follows #333's mechanical sweep rather than preceding it, and the knowledgebase
    has no entries yet, so there is nothing to attach this issue to today. Recorded here so the
    condition is not lost when the sweep runs.

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
4. **A notification that depends on volatile data must know whether it can still execute, and must
   say so to the user in terms they can understand** (developer, 2026-09-10). Three obligations, not
   one:
   - **Verify.** `INotificationActionExecutor.CanExecute` currently sees only the trigger and answers
     `true` unconditionally for `ImportReviewResolved`, so the notification panel offers Keep/Take on
     a dead batch exactly as the review page does. It must be able to consult the metadata.
   - **Relay — through the state, not through bespoke text** (developer, 2026-09-10). This is what
     notification state is for. `NotificationDisplayStatus` gains a member meaning the action can no
     longer be carried out, and the Status badge renders it like any other. Withdrawing the control
     alone would leave an empty Action cell, indistinguishable from a row that never had an action.
   - **Name things humanly.** The row names the file it came from, not its batch id — the same
     requirement as decision 1 seen from the user's side rather than the data's, and the whole reason
     the producer writes the name into the metadata at creation.

---

## Steps

**Red and green are per step.** Step 1 writes every test and runs it red; each step below then re-runs
the rows it owns, observes them fail for their own reason, and leaves them green with every
already-green row still green. Steps execute strictly in order — a step run early borrows another
step's red.

### 1. Write every test first, and run them red

**Status:** ✅ Done — 14 unit tests red on their own assertions, 8 controls green; T2 document red against the pre-work canary

Exit condition: every unit test in the verification table exists and **fails on its own assertion**,
and the new T2 document has been run against a build from the commit before this work started and
failed there.

**Production code at the end of this step is signatures only, each returning today's behaviour** — so
every failure below is an assertion, never a build break: `NotificationActionAvailability`, the
executor's three-argument `CanExecute` and `GetAvailabilityAsync`, `NotificationTable.ExecutorCanRun`,
the `ActionUnavailable` member and `GetDisplayStatus`'s ignored flag, `INotificationReader.GetByMetadataKindAsync`
returning nothing, and `ImportReview`'s `FileNameFor`/`FileNamesFromNotifications`/`BatchIsGone`/
`CanDecide`/`DismissBatchAsync`. Build: 0 warnings, 0 errors.

**Red (14):** rows 1, 3, 4, 6, 7, 10, 12, 13, 15, 18, 20, 21, 22, 23.

**Green, and correctly so (8 controls):** rows 2, 5, 8, 9, 11, 14, 24, and `TranslationCompletenessTests`
in row 15. Each asserts behaviour that must not change, so passing before and after is what correct
looks like; a control that starts red is testing the wrong thing.

**Three corrections to this plan, found while writing the tests — recorded rather than absorbed:**

- **Rows 8 and 9 could not go red, and the plan said they would.** It claimed `DiscardBatchAsync` "has
  never been exercised with the parent missing". The coordinator's own
  `DiscardBatchAsync_StagedBatch_MarksEveryActionDiscardedWithoutTouchingDomainTables` writes actions
  against `"BATCH-1"` with no batch row at all, so it has been exercised that way all along — row 9 now
  names that existing test rather than a duplicate of it. Row 8, at service level against real SQLite
  whose fixture does write a batch row, deletes that row first and is kept as a control: it pins the
  property the page's dismiss relies on.
- **Row 14 is a control as well.** The stub ignores the new flag, so dismissed, expired and executing
  win trivially before the change — which is exactly the precedence the row asserts must survive it.
- **#303's `FileNameFor_UnknownBatch_FallsBackToTheId` was replaced here, not in step 6.** Its assertion
  (the id is shown) and row 6's (the id is never shown) cannot both be green, so keeping it until step 6
  would have turned an already-green test red during step 3 — which this plan's per-step rule forbids.

**Five rows added** (20–24), each a behaviour the planned table left unasserted: the gone predicate
itself, the page's dismiss, the capability check without a payload, the availability read, and a
control showing the other triggers ignore availability.

**The T2 document is written and red — on its second run.** Its first run against the canary was void:
step 2's plain `DELETE` failed with `FOREIGN KEY constraint failed`, because a staged batch is referenced
from `Import_FileResourceBatch`, so no orphan existed and steps 3–4 described a healthy batch. The fix
does what the removed pre-#372 `TruncateDataAsync` actually did — `PRAGMA foreign_keys = OFF` before the
delete — so the state under test is the one a legacy database really holds. On the second run the
pre-work build failed every assertion that distinguishes the two builds; the document's own canary
table records each one.

### 2. Read the file name from notification metadata, including dismissed alerts

**Status:** ✅ Done — rows 1, 3 and 4 green; every other test in the solution unchanged

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

**Result.** Rows 1, 3 and 4 were re-run red at the start of the step and pass at its end; row 5's guards
pass with the new constant and strategy in their enumeration. Because the reader's constructor is shared
by every test that builds one, the whole suite ran at the end rather than only this step's rows: every
project green except the 11 rows later steps own, each still red for its own reason.

- **The read is keyed on metadata kind, not on import review.** `GetByMetadataKindAsync(kind)` rather
  than an import-review-specific method: the rule it serves — a notification's payload outlives what it
  names — applies to every kind, and nothing about the query is specific to one. Row 4's test is named
  for the method it actually tests.
- **No missing-table fallback, unlike its two siblings.** Those serve surfaces that stay reachable while
  the database is degraded; this read's only caller renders against a healthy one, so the fallback would
  guard a state it cannot reach. The reason is stated at the method.

### 3. Make "batch no longer exists" a first-class predicate on the row

**Status:** ✅ Done — rows 6 and 20 green; translation guard green; later steps' 9 rows still red

One `internal static` predicate — the batch id matches no live `Import_Batch` row — decided in one
place rather than inferred at each call site, so the page and its tests agree on one definition.

Rendered visibly, with a localised label in all three `UI.*.json` files. A row that is orphaned **and**
has no notification to name its file shows that same label in the **File** column: there is no durable
name for it, and saying so is the honest state. This settles the question the issue left open about a
batch staged before #303 shipped — it is in scope, and it falls out of this predicate rather than
needing a case of its own.

**Result.** Rows 6 and 20 were red at the step's start — the run that closed step 2, with nothing changed
between — and pass at its end; `TranslationCompletenessTests` stays green with the key in all three
files, and the 9 rows later steps own are still red. The label reads *Import batch no longer exists* and
sits as a second badge beside the action's stored status rather than replacing it: the action genuinely
is still `Pending` in the database, and the badge says why it can go no further.

### 4. Offer only dismiss on such a row

**Status:** ✅ Done — rows 7 and 21 green; later steps' 7 rows still red

Keep/Take removed, not disabled-and-still-shown — they are impossible, not unavailable. Dismiss calls
`DiscardBatchAsync`, which reads and writes `Import_Action` only and never touches the batch row, so it
is already correct against a missing parent. Rows 8 and 9 pin that as controls — step 1 records why
neither could be red.

**Result.** Rows 7 and 21 were red at the step's start and pass at its end; `DecideAndApply`'s existing
tests stay green, since a live batch's Keep/Take path is unchanged. `CanDecide` is the one gate for
Keep/Take, so the markup asks it rather than repeating the gone check beside it.

### 5. Let a notification action decline when its own metadata names something gone

**Status:** ✅ Done — rows 10, 12, 13, 15, 22, 23 and 25 green; only step 7's row 18 still red

`CanExecute` gains access to the notification's metadata so `ImportReviewResolved` can answer `false`
when the batch it names is gone.

**The user-facing half is a state, not a sentence bolted onto the Action cell.** `NotificationTable`
renders nothing there when `CanExecuteAction` is false, so withdrawal alone leaves an empty cell that
reads exactly like a row which never had an action. `NotificationDisplayStatus` gains a member —
`ActionUnavailable` — computed by `GetDisplayStatus` and rendered as a Status badge alongside
`Active`/`Executing`/the rest.

**`Executing` is the precedent this follows exactly.** `NotificationDisplayStatus` is derived at
render time and never persisted, and `Executing` is already computed from something outside the row
(`Executing.IsExecuting(notification.Id)`, a session service) rather than from a column. So
`GetDisplayStatus` takes the new fact the same way it takes `isExecuting`, and design decision 2's
"no new status and no schema change" is untouched — this is a display state, not an
`ImportActionStatus`.

**Precedence:** `Dismissed` and its reason-derived variants win, then `Expired`, then `Executing`,
then `ActionUnavailable`, then `Active`. It applies only to a live, undismissed, unexpired row. It is
deliberately distinct from the existing `Obsolete`, which means *dismissed because the subject went
away*; this row is not dismissed and the operator has not decided anything yet.

**Name chosen, not left open:** `ActionUnavailable` breaks the one-word pattern of its siblings
because the one-word candidates each mean something else here — `Obsolete` is taken, `Superseded` is
the status this issue explicitly rejected, and `Stale` is an `ImportActionStatus` member. Overridable,
but not an open question.

The capability check must not become a per-row database query at render time — the page does one read
and passes it in, the same constraint step 2 works under.

**Result.** Rows 10, 12, 13, 15, 22 and 23 were red at the step's start and pass at its end; every other
test in `Api.Tests` stays green, and the only red test left in the solution is step 7's row 18.

- **The availability is a required parameter of the table**, read by each host behind its existing
  health gate — the Notifications page on every load, the startup popup once. `EditorRequired` makes a
  host that forgets it a build warning, which the 0-warnings gate turns into a failure.
- **Row 25 was added here, and run red against a stub first.** Whether a row reads `ActionUnavailable`
  is a composition — an action is wired **and** cannot run — that no planned row asserted; without it a
  purely informational notification could claim to have lost an action it never had.
- **The analyzer caught a real defect mid-step.** The page's availability field was declared and never
  assigned, and IDE0044 flagged it as a candidate for `readonly`. On the live page every import-review
  alert would have been judged against an empty availability and read "no longer possible" with its
  batch present. No unit test could see it, since the page cannot be rendered in tests here; T2 step 4
  would have. Fixed before the step closed.
- **The Active filter deliberately ignores the new state.** An alert whose action can no longer run is
  undismissed and still waiting on the operator, so it belongs under Active, where its own badge says
  why it cannot be acted on. The reason is stated at the filter.

### 6. Replace #303's id-fallback requirement, and correct #372's leftovers

**Status:** ✅ Done — no rows of its own; full suite green except step 7's row 18

`ImportReviewPageTests.FileNameFor_UnknownBatch_FallsBackToTheId` asserts the behaviour this issue
removes. It is replaced, not deleted quietly — the replacement asserts the name resolves from the
notification instead, and a second row covers the no-notification case.

`ImportReviewPendingMetadataDto`'s remarks and the unreferenced `Sql.SystemImportActions.DeleteAll` are
corrected in the same commit (cross-check finding 10).

**`NotificationDismissReason.Obsolete` gains no producer here.** Its removal site names this issue as
where one may reappear, and the answer is that it should not: dismissal is the operator's explicit act
via step 4, not something the page decides on their behalf. Stated so the next reader does not treat
the omission as an oversight.

**Result.** No rows of its own — the test replacement was carried out in step 1, for the reason
recorded there — so there was no start-of-step red to observe. The DTO's remarks now describe the
post-#372 lifecycle and say why `FileName` is recorded; the unreferenced constant is gone. The full suite
is unchanged: every project green except step 7's row 18. `Quotinator.Data.Tests` reports four fewer
tests than at step 2, and that was checked rather than assumed: `AllNamedSqlConstants` feeds exactly
four guard methods, so one constant fewer is four data rows fewer.

### 7. Boyscout pass over every file this issue touched

**Status:** ✅ Done — rows 16, 17 and 19 green; every verification row green

The closing step for the issue, covering the files this work created or touched and no others.

**`NotificationDisplayStatus` moves to its own file.** It is declared inside
`NotificationTable.razor.cs` (line 70) as a nested type, which ADR 016 and `CLAUDE.md`'s file-placement
rule both put in an `Enums/` folder instead. Nothing at the declaration states a reason for the
placement, so it is a deviation rather than a decision, and it is corrected here — this issue is adding
a member to that very enum. `src/Quotinator.Api/Enums/NotificationDisplayStatus.cs`, namespace
`Quotinator.Api.Enums`; the folder does not exist yet and this creates it.

**`NotificationFilterMode` moves with it.** Same violation, same shape, declared in
`Notifications.razor.cs` (line 30) — which is the page this issue's step 5 already touches, so it is
inside the pass rather than someone else's file.

**`CLAUDE.md`'s Razor caveat applies to both moves.** `NotificationTable.razor`'s status `@switch`
references the enum unqualified, and a `.razor` file's reference to a moved type is not reliably caught
by the build. Every `.razor` and `_Imports.razor` touching either name is checked by hand, and the UI
is loaded to confirm the switch still renders.

**The XML doc comments in `NotificationTable.razor.cs` have come adrift and are re-attached.** The
summary describing `NotificationDisplayStatus` sits at lines 61–65 followed by a second summary, so it
lands on `Local` instead of the enum; the same doubling at 72–91 puts `GetDisplayStatus`'s summary onto
`ShowsRunControl`. The orphaned enum summary also says "three mutually-exclusive display states" for an
enum that has six today and gains a seventh here. CS1591 cannot catch this — a doc comment exists, it
is simply on the wrong member.

**`.editorconfig`'s scoped sections gain every file this issue creates or touches**, IDE0008 and
IDE0090 both, with the `var`→explicit-type and `new(...)` conversions run on them until the counts stop
dropping. The IDE0008 list already carries most of the existing files here; the IDE0090 list carries
only four in total, so most of this issue's files are new to it.

**`docs/logging.md`'s `[Subsystem - Phase]` rule** applies to any touched file that emits a log line.

**Result.** Row 18 was red at the step's start — as in every run since step 1 — and passes at its end.
The full suite is green in every project, and a non-incremental build reports 0 warnings, 0 errors.

- **Both enums live in `src/Quotinator.Api/Enums/`**, namespace `Quotinator.Api.Enums`, every member
  documented. `NotificationDisplayStatus`'s summary had said "three" states for six; it now documents
  all seven. The Razor caveat was checked by hand: only `NotificationTable.razor` and
  `Notifications.razor` name either enum, and both import the namespace. Whether the switch still renders
  live is row 19's T2 step, not something a build can show.
- **The two adrift summaries are re-attached.** The enum's moved with it. `GetDisplayStatus`'s was
  removed from above `ShowsRunControl` and rewritten above `GetDisplayStatus` itself, now naming the
  whole precedence — `Executing` and `ActionUnavailable` included — with every parameter documented.
- **Column names come from `nameof` in both touched `Sql.*` classes** — `Notifications` (a new query)
  and `SystemImportActions` (a removed constant). Table names, alias prefixes, the JSON path, `rowid` and
  Dapper parameter names stay literal, for the reasons `Sql.AppVersion` gives. Each class gained a
  `Table` constant, which is why `Quotinator.Data.Tests` reports eight more tests than after step 6:
  each constant feeds the same four guard methods that accounted for step 6's drop of four.
- **`.editorconfig`**: four files new to the IDE0008 list, 28 to the IDE0090 list. One `dotnet format`
  pass cleared all 96 IDE0090 warnings that exposed — in `Sql.cs`, `SqliteImportActionServiceTests.cs`
  and `NotificationReaderTests.cs` — and a non-incremental rebuild found none left and no
  `IDE0028`/`IDE0305` follow-ons. Every one of the 88 lines the formatter removed from the service tests
  was a `new Type(...)` simplification.
- **`.editorconfig` itself, and line endings — fixed at the close, having first been reported instead.**
  The IDE0008 list named `tests/Quotinator.Data.Tests/Repositories/TestNotificationReader.cs`, a path
  #312 moved into `Quotinator.Data.Testing`, and held 23 duplicate entries, mostly from the union-merge
  with #320's list: 249 entries became 225, with none missing and none repeated. And
  `SqliteImportActionServiceTests.cs` carried four CRLF lines against `.gitattributes`' `eol=lf`; the
  working copy is LF again, with its diff unchanged. Both are files this issue touched, so both were in
  scope all along — they were listed as surfaced, which is the treatment for an untouched file.
- **Log prefixes**: every log line in a touched file already carries a `[Subsystem - Phase]` prefix.
- **`EntityFilterOutcome` moved to `Enums/`, at the developer's direction, having first been surfaced.**
  `EntityFilterParsing.cs` declared it beside the helper — the same ADR 016 deviation, in a file this
  issue had not touched, and the only one left in `src/`. Row 18's guard had been scoped to code-behinds
  for that reason alone, so it was widened first, to every source file outside an `Enums/` folder, and
  renamed `EveryEnumLivesInAnEnumsFolder`: red on exactly
  `src\Quotinator.Api\Endpoints\Shared\EntityFilterParsing.cs`, then green once the enum moved to
  `src/Quotinator.Api/Enums/EntityFilterOutcome.cs`. Its three consumers gained the namespace and joined
  both `.editorconfig` lists, which exposed 41 `var` declarations — 31 in `QuoteEndpoints.cs` — cleared
  by `dotnet format` and read line by line. Full suite green afterwards, 4,090 of 4,090.

**T2 green run, 2026-09-10 — steps 1–4 passed; steps 5–6 not yet run.** Against `quotinator:local` built
from the finished tree, steps 1–4 produced every expected value: the orphan established (`1 row(s)
affected`, `batch lookup -> 404`, `pending actions = 1`); the review page naming `conflicting.json`,
never the batch id, stating the batch no longer exists, offering Dismiss and not Keep/Take; the
notifications page reading *Action no longer possible* with no Decide control. Read from the rendered
page as well, not only the HTML: the alert's badge is visible as `badge bg-secondary`, and the review
row's cells read *Pending / Import batch no longer exists · Quote · Modify · conflicting.json · quoteText
· Dismiss*.

**Screenshots.** With the Browser pane reopened, real screenshots confirmed both pages before anything was
dismissed: the review row with its grey *Import batch no longer exists* badge beside *Pending* and Dismiss
as its only control, and the alert's grey *Action no longer possible* badge beside an empty Action cell.
Row 17 is verified by that screenshot.

**Step 5 failed, and found a defect older than this issue.** Clicking Dismiss raised
`ImportBatchStateException: Batch '…' has already been applied and cannot be discarded`, and the row
stayed. The orphaned batch holds two actions: the conflicting quote, `Pending`, and its Source,
`Unchanged` — which the planner stages directly as `Applied` (#373, 2026-09-03; #377 and #376 do the same
for their own no-op kinds). `DiscardBatchAsync`'s guard dates from #154 (2026-07-07), when `Applied` could
only mean a real apply had happened, and it refuses any batch containing one. `ImportActionKind`'s own
documentation says how *apply* treats a staged-`Applied` no-op and says nothing about *discard*. So a
whole-batch discard fails for any staged review batch whose file restates something already stored —
orphaned or not — and the REST discard endpoint goes through the same method.

Row 8 missed it because its fixture never held a staged-`Applied` no-op, and the page's own test used a
fake service; this is the gap the live tier exists for. By the developer's decision the fix went to its
own issue, fixed before this one closes: #389, which this issue now depends on.

**T2 re-run on the #389 build, 2026-09-10 — steps 1–6 passed.** A fresh container, since the earlier one
held an image without the fix. Steps 1–4 gave the same values as before. Step 5: Dismiss left the page
reading *Nothing is waiting for review.* (screenshot), `pending actions = 0`, the `Quote Modify`
`Discarded`, the `Source Unchanged` no-op still `Applied` — #389's behaviour, which step 5's expectation
now names — and the alert `isDismissed=True reason=resolved`. Step 6, under **All**: the 8 rows the API
reports, every badge a word, the resolved alert **Done** and the others **Active** (screenshot, and the
badge text read back from the page). Rows 16, 17 and 19 are verified.

**T1, 2026-09-10 — passed.** The developer started the app in Visual Studio from the finished tree:
`1.9.0-alpha`, both schemas up to date (data v22, app v9), 795 quotes, *Quotinator ready*, and no
warning or error line anywhere in the startup log.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An orphaned row's file name resolves from notification metadata | Unit test | `ImportReviewPageTests.FileNameFor_BatchGone_ResolvesTheNameFromNotificationMetadata` |
| 2 | ✅ | A live batch row still wins over the notification copy | Unit test | `ImportReviewPageTests.FileNameFor_KnownBatch_ReportsTheFileItWasImportedFrom` — existing control, stayed green |
| 3 | ✅ | The metadata lookup includes alerts already dismissed | Unit test | `ImportReviewPageTests.FileNamesFromNotifications_IncludesDismissedAlerts` |
| 4 | ✅ | The new reader returns `ImportReviewPending` metadata regardless of dismissal | Unit test | `NotificationReaderTests.GetByMetadataKindAsync_ReturnsDismissedAlertsToo` — named for the kind-keyed method it tests (see step 2) |
| 5 | ✅ | The new query and its join strategy pass the SQL guards | Unit test | `SqlQueryGuardTests` — existing enumeration; its `AllJoinStrategyBuildSqlCases` discovers the new `IJoinStrategy` by reflection, so no new row is needed. Control, stayed green |
| 6 | ✅ | An orphaned row with no notification renders the unresolved label, not the batch id | Unit test | `ImportReviewPageTests.FileNameFor_BatchGoneAndNoNotification_RendersUnresolved` — replaces `FileNameFor_UnknownBatch_FallsBackToTheId` |
| 7 | ✅ | An orphaned row offers no Keep/Take even when it has ambiguous fields | Unit test | `ImportReviewPageTests.CanDecide_BatchGone_IsFalseDespiteAmbiguousFields` |
| 8 | ✅ | Discard succeeds against a batch whose row is missing | Unit test | `SqliteImportActionServiceTests.DiscardBatchAsync_BatchRowMissing_MarksActionsDiscarded` — real SQLite; control, green before and after (see step 1) |
| 9 | ✅ | The coordinator discards an orphaned batch without touching a domain table | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_StagedBatch_MarksEveryActionDiscardedWithoutTouchingDomainTables` — existing; its fixture has never had a batch row, so it already is this case (see step 1) |
| 10 | ✅ | A notification action declines when its metadata names a batch that is gone | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewWhoseBatchIsGone_IsFalse` |
| 11 | ✅ | A notification action still runs when its batch is live | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewWithLiveBatch_IsTrue` — control |
| 12 | ✅ | The panel asks the executor with the row's own metadata, not the trigger alone | Unit test | `NotificationTableTests.ExecutorCanRun_ImportReviewWhoseBatchIsGone_IsFalse` — through the `internal static` seam; `ShowsRunControl`'s existing `executorCanRun: false` case covers the propagation |
| 13 | ✅ | Such a row reports `ActionUnavailable` rather than `Active` | Unit test | `NotificationTableTests.GetDisplayStatus_ImportReviewWhoseBatchIsGone_IsActionUnavailable` |
| 14 | ✅ | Dismissed, expired and executing each still win over the new state | Unit test | `NotificationTableTests.GetDisplayStatus_ActionUnavailable_YieldsToDismissedExpiredAndExecuting` — control (see step 1) |
| 15 | ✅ | Every new label exists in every locale | Unit test | `NotificationTableTests.EveryDisplayStatus_HasATranslationKey` (red at step 1 for `ActionUnavailable`) and `TranslationCompletenessTests` (control, stayed green with all three new keys) |
| 16 | ✅ | Live: an orphaned row shows its file name, offers only dismiss, and dismissing clears it | Live (T2) | `docs/automated-testing/import-and-staged-actions/27-orphaned-review-row-offers-only-dismiss.md` |
| 17 | ✅ | Live: the same row's notification shows the `ActionUnavailable` badge and names the file | Live (T2) | Same document, its own section |
| 18 | ✅ | Every enum in a source project lives in `Enums/` — the two display enums included | Unit test | `RepositoryStructureTests.EveryEnumLivesInAnEnumsFolder` — asserts no enum outside an `Enums/` folder anywhere under `src/`, so the rule holds for the next file too rather than for these by hand; began scoped to `.razor.cs` files and was widened once the last other deviation moved (step 7) |
| 19 | ✅ | Live: the Status column still renders its states as words after the enum move | Live (T2) | Same document, step 6 — the Razor caveat means the build cannot prove this |
| 20 | ✅ | A batch is gone exactly when no live batch matches its id, compared case-insensitively | Unit test | `ImportReviewPageTests.BatchIsGone_OnlyWhenNoLiveBatchMatches` — added in step 1 |
| 21 | ✅ | Dismissing a gone row discards its whole batch and applies nothing | Unit test | `ImportReviewPageTests.DismissBatch_DiscardsTheWholeBatch` — added in step 1 |
| 22 | ✅ | An import-review action without its payload cannot run | Unit test | `NotificationActionExecutorTests.CanExecute_ImportReviewWithoutItsPayload_IsFalse` — added in step 1 |
| 23 | ✅ | The availability reports every live batch and no other | Unit test | `NotificationActionExecutorTests.GetAvailabilityAsync_ReportsEveryLiveBatchAndNoOther` — added in step 1 |
| 24 | ✅ | Reset and Reseed ignore availability | Unit test | `NotificationActionExecutorTests.CanExecute_TriggerWithNoVolatileDependency_IgnoresAvailability` — added in step 1; control |
| 25 | ✅ | "Action no longer possible" is reported only for an action that exists and cannot run | Unit test | `NotificationTableTests.ActionIsUnavailable_OnlyForAWiredActionThatCannotRun` — added in step 5, red against its stub before implementing |
