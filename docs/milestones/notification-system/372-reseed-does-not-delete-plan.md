# #372 — Reseed should only import the designated files, not delete data first

**Status:** In progress (step 8) — steps 1–7 done (steps 6–7 found already/newly complete 2026-09-04,
during [#374](https://github.com/DutchJaFO/Quotinator/issues/374)'s own step 12 cascade); every
checklist row is ✅ except row 21 (T1, the developer's own).

**Rows 1–9 carried ❌ until 2026-09-08 while this status line already claimed them green** — the table
was never updated after steps 2 and 3 turned them, so the doc contradicted itself for four days. Each
was re-verified before ticking rather than flipped to match the claim: the eight named tests were run
(`Reseed_OnPopulatedDatabase_DeletesNothing` and siblings, 8 passed 2026-09-08), and row 7's "or
removal" branch confirmed by reading the code — `BatchIdsAsync`/`DismissAlertsForRemovedBatchesAsync`
are gone, with the reasoning recorded at `QuotinatorDatabaseInitializer.cs`'s own comment.
**GitHub issue:** #372
**Tiers required:** T1, T2
**Depends on:** [#373](https://github.com/DutchJaFO/Quotinator/issues/373), found by this issue's own step 6. **Blocks #302**, whose final T2 and T1 wait on this

---

## Description

`POST /api/v1/admin/database/reseed` deletes every row of domain content and then imports the
designated files. The deletion is `TruncateDataAsync`, whose only caller it is. Reseed's one job is
importing those files; against a populated database that is an ordinary import — missing rows added,
differing rows raising conflicts to be resolved, matching rows producing no action. Starting from
scratch is Reset followed by Reseed, two explicit actions.

**This is CLAUDE.md's endpoint side-effect policy applied to the case it was never applied to.** #156
split Reset so it rebuilds the schema and does not reimport; `OnResetAsync`'s own remark cites the
policy by name. Reseed is the same defect mirrored — it imports *and* deletes — and nobody carried the
rule across.

**Found via #302** (2026-09-02). Its per-file confirmations made the deletion observable for the first
time, and it turned out `TruncateDataAsync` clears 14 of the 17 `Quotinator_` tables, silently keeping
`Series`, `Universe` and `CharacterSource`. All three postdate the list — the signature of a
hand-maintained enumeration, not three oversights. `CharacterSource` is a link table, so every reseed
leaves rows joining a Character and a Source that have both been deleted.

**A prefix-driven rewrite of the delete list was considered and rejected** (developer, 2026-09-02).
Deriving the table set from ADR 015's `Quotinator_` prefix would fix the drift, but it keeps reseed in
the business of deciding what data survives. The method is removed, not improved.

## Cross-check against authoritative sources, 2026-09-02

Per `docs/workflow/process.md`'s Planning step 3. Four findings, all folded into the steps below.

1. **ADR 014 states the behaviour this issue removes, as fact.** It records that "Reseed
   (`TruncateDataAsync`) ... only ever deletes rows from named *domain* tables", and reasons from that
   about "audit-trail rows referencing Reseed-wiped-and-reimported domain entities". Its **decision is
   unaffected** — a dangling reference still arises whenever content genuinely changes or is removed
   across a reimport — but its factual description of Reseed stops being true. Revised in place, per
   this project's convention that an ADR carries its context and effective result rather than a
   history of amendments.

2. **Four surfaces document reseed as deleting**, and CLAUDE.md requires them in the same commit as
   the behaviour change: `AdminEndpoints.cs`'s own `WithDescription`, `docs/api-endpoints.md`, and
   `addon/DOCS.md` + `addon-beta/DOCS.md` (which must stay mirrored, and whose reseed line is inside
   the curated example list, so it is one of the entries a behaviour change does require touching).

3. **The import machinery already works against populated data — this is not new capability.**
   `PreviewSeedAsync` calls `ImportActionPlanner.PlanAsync` against the live connection with no
   truncation, producing the same per-file reports, and is exposed as
   `GET /admin/database/seed/preview`. Reseed's own loop is that planner plus staging and applying.
   The implementation risk here is in what stops happening, not in what starts.

4. **#302's and #303's tests assume a reseed re-applies everything, and several stop holding.**
   `Reseed_AfterDismissal_WritesTheConfirmationAgain` expects six confirmations across two reseeds;
   once the second reseed imports nothing, there is nothing to confirm afresh. These are named in
   step 6 rather than discovered mid-implementation.

---

## Steps

**The step order enforces red-first; it is not left to memory.** Step 1 writes every test — unit and
automated — and runs them before any behaviour changes. No implementation step precedes its own test.

**Red and green are per step, not per issue** (developer, 2026-09-02). Every step names the rows it
owns, and both ends are proven:

- **At the start of the step**, re-run that step's rows and observe them fail. Not inferred from step
  1's run — re-run them. A row can be turned green as a side effect of an earlier step, and a step that
  begins already green is testing nothing.
- **At the end of the step**, those rows pass and the ones already green stay green.

This is the failure #302's own plan recorded and could not undo: its rows 1–12 went red together and
green together in one pass, so no individual row was ever observed failing for its own reason. Steps
here are executed strictly in order for the same reason — a step run early borrows another step's red.

### 1. Write every test first, and run them red

**Status:** ✅ Done — 4 unit tests red, 6 classified as controls; T2 document written and run red

Exit condition: every unit test in the verification table exists and **fails on its own assertion**,
and the new T2 document has been run against the current build and failed.

**The first attempt produced 8 of 10 passing, and the reason is the heart of this issue.** Every id in
this project is a hash of normalised content (ADR 014 states it), so truncate-then-reimport restores
byte-identical rows for everything the source files describe. Row counts match, ids match, links
resolve. An assertion over any of them passes against the deletion it exists to catch.

**Only content the files do not describe can tell the two behaviours apart.** The tests now insert one
locally authored quote before reseeding — a delete loses it, an import cannot re-create it. That single
change took `Reseed_OnPopulatedDatabase_DeletesNothing` and
`Reseed_WithARowRemoved_ReAddsOnlyThatRow` from passing to failing on their own assertions.

**A claim in the issue was wrong, and is corrected rather than quietly dropped.** The issue says every
reseed orphans `Quotinator_CharacterSource` rows. It does not: the reimport re-creates the same
Characters and Sources with the same content-derived ids, so the links resolve again. Orphans need
content whose id actually changes across a reimport, which no fixture here produces. Row 5 stays as a
regression guard — cheap, and the failure mode is silent — but carries a precondition asserting link
rows exist at all, so it cannot pass by having nothing to check. It is not evidence of a live bug and
must not be reported as one.

**Red at step 1 (4):** rows 1, 3, 4, 6 — `DeletesNothing`, `ReAddsOnlyThatRow`,
`StagesAConflictRatherThanOverwriting`, `PreservesImportBatches`.

**Not red, and correctly so (6).** Two categories, kept apart deliberately:

- **Controls and guards** — rows 2, 5, 9, 10, 13. These assert behaviour that must *not* change, so
  passing before and after is what correct looks like. A control that starts red is a control that is
  testing the wrong thing.
- **Row 8, which needed the step order fixed instead.** `ImportsRegardlessOfExistingContent` passes
  today only because truncation empties the database before the gate sees it. Splitting the gate first
  leaves it passing throughout — never observed red. Swapping steps 2 and 3 makes it genuinely red in
  between. See step 2.

**The T2 document is written and red.** `21-reseed-preserves-existing-data.md`, run against
`3e9bb19c`: the locally imported quote does not survive a reseed (`800` → `799`, zero search hits) and
import batches drop from `5` to `4`. Its own canary section carries the numbers.

**Writing it found two defects that reading it never would have.** `POST /quotes` does not exist — the
v1 API is read-only for quote content — and `Invoke-RestMethod -Form` is PowerShell 7+, absent from
this project's 5.1 shell. The first draft used both. A document written after the code would have been
composed against whatever the implementation happened to make convenient and never tested this way.

**This issue's reds are unusually easy to fake, and that is the thing to guard.** Most rows assert
that something is *preserved*. Against today's build, truncation deletes the rows, so a preservation
assertion fails for the right reason — but a row asserting "no conflict was raised" or "nothing was
imported" can pass vacuously against a build that imports nothing at all. Every such row needs its
positive control named in the same step, the way row 4's zero-files case needed one in #302.

**The T2 document is written and run now**, while `HEAD` is still the pre-work build — the only point
at which its canary costs nothing. Record the result in the document's own *Canary* section.

### 2. Remove the deletion

**Status:** ✅ Done — rows 1 and 6 green; rows 2, 3, 4, 8 red, which step 3 closes

**Start of step:** rows 1, 3, 4 and 6 confirmed red by re-running them, not inferred from step 1.
**End of step:** rows 1 (`DeletesNothing`) and 6 (`PreservesImportBatches`) green; rows 2, 3, 4 and 8
red. `TruncateDataAsync` and its call are gone; `BatchIdsAsync`'s pre-read went with them, since
nothing is removed for it to report.

**Two predictions in this step's own text were wrong, and the runs corrected them.**

- **Rows 3 and 4 were predicted green here; they cannot be.** Both need the import to actually run
  against a populated database, and the count gate still blocks it until step 3. They are red for the
  same reason row 8 is, not for a different one.
- **Row 2 stayed green when it should have gone red**, because the control was weak: it read
  `LastSeedReport` from the same initializer instance the cold start had already filled, so a reseed
  doing nothing at all still satisfied it. A second instance fixes it, and the row went red
  immediately. **A control that cannot fail is worse than no control** — it certifies the very
  vacuous pass it exists to prevent, and this one would have signed off step 3.

`TruncateDataAsync` is deleted along with its call in `OnReseedAsync`. Not corrected, not made
prefix-driven — reseed stops deciding what survives.

**This step is deliberately taken before the gate split, and the original plan had it the other way
round.** Measured at step 1: `Reseed_OnPopulatedDatabase_ImportsRegardlessOfExistingContent` passes
against today's build, because truncation empties the database and the count gate therefore lets the
import through. Split the gate first and it still passes — it can never be observed red, which is the
"borrowed red" the Steps preamble warns about.

Removing the deletion first makes it genuinely red: reseed then meets a populated database, the gate
returns early, and nothing is imported. `Reseed_OnPopulatedDatabase_IsNotANoOp` goes red alongside it
for the same reason. **This step therefore ends with two rows failing on purpose**, which step 3
closes — the only point in this issue where that is correct, and it is recorded here so a later reader
does not mistake it for an unfinished step.

### 3. Give cold start and reseed their own method bodies

**Status:** ✅ Done — rows 2, 3, 4, 8 green; all ten unit tests pass

**Start of step:** rows 2, 3, 4 and 8 confirmed red by the run that closed step 2.
**End of step:** all ten green. `SeedIfEmptyInternalAsync` keeps the count gate and delegates to a new
`ImportDesignatedFilesAsync`; `OnReseedAsync` calls that directly and never sees the gate.

**Row 4 had never actually been red, and only the green run revealed it.** Its failures up to this
point were `SQLite Error 1: 'no such column: Text'` — the column is `QuoteText`. An exception is a
weaker red than an assertion failure, because it looks identical for a test asserting the opposite,
which is the caveat #308's plan recorded about storage-backed tests. Proven properly by mutation
instead: adding `DELETE FROM Quotinator_Quote` back into the reseed fails it on
`Assert.AreEqual(localEdit, stored)`, and fails row 1 alongside it. Reverted, both green.

**Two guard tests in the T2 suite failed on this issue's own new document**, and both were right:
`curl.exe` is not PowerShell — `scripts/testing/http.csx` exists for the multipart gap 5.1 leaves — and
port `19521` was already published by document 20. Moved to `19522` and onto `http.csx`. The document
was written before either rule was consulted; the guards are what caught it rather than a reader.

`SeedIfEmptyInternalAsync` opens with `if (count > 0) return;`. That check is cold start's own job —
it is literally "seed if empty". An explicit reseed must never consult it: the check is not a
safeguard there, it suppresses the report the operator ran the reseed to get.

The two paths stop sharing one body. That sharing is why an `isReseed` flag existed at all, and was
only possible because truncation made a populated database look empty to the shared code.

**"Empty" means no seedable content — it does not mean no rows** (developer, 2026-09-02). The gate
stays a content check (`Quotinator_Quote`), and must **not** be broadened into "no `Quotinator_` table
has rows". Two reasons, and both will matter more later than they do today:

- **Reference content seeded by the baseline is not content this decides about.** A database created
  from scratch is still "empty" for seeding purposes even though the baseline has already populated
  whatever fixed reference rows the schema requires. Quotinator has no such table yet — genres are a
  closed enum — so nothing currently exercises the distinction. It arrives with
  [#310](https://github.com/DutchJaFO/Quotinator/issues/310)/[#268](https://github.com/DutchJaFO/Quotinator/issues/268),
  which make Genre a lookup table, and a gate broadened to "any row anywhere" would then read a
  brand-new database as already seeded and silently skip the seed entirely.
- **A user-updatable table is content, not reference data, however generic it looks.** `Universe` is
  the near-miss: it reads like a lookup table, but users change it, so it belongs on the content side.
  Being generic is not the test; being the operator's to edit is.

Row 1's preservation assertion still enumerates every `Quotinator_` table — that is a different
question (what a reseed must not delete) and the broad set is correct there. Only the emptiness gate
stays narrow.

### 4. Settle the batch-removal and `Obsolete`-dismissal question

**Status:** ✅ Done — row 7 green by removal

**Established, not assumed.** `NotificationDismissReason.Obsolete` had exactly one producer in the
whole codebase: `DismissAlertsForRemovedBatchesAsync`. `SqliteImportActionService`'s two
`DismissByTriggerAndBatchAsync` calls both pass `Resolved`, not `Obsolete`. Reset cannot reach it
either — it rebuilds from the baseline, wiping notifications outright, so there is nothing to mark.

**The producer is removed; the enum member stays, and that is a separate decision.** Databases upgraded
from an earlier build hold rows already carrying `Obsolete`, so the member, its CHECK constraint and
`NotificationTable`'s rendering of it all have to keep working — deleting it would need a migration and
would break the reading of history that already exists. It now has no producer, which is a stated fact
rather than a gap to fill. #369, which handles review rows whose batch is genuinely gone, is where one
may reappear.

`BatchIdsAsync` and `DismissAlertsForRemovedBatchesAsync` exist because reseed removed import batches;
with nothing removed, they have nothing to act on. **Establish where else #303's `Obsolete` dismissal
applies before deleting it** — an absence of callers is not the same as knowing it is unwanted, and
the reachable-from-Reset case has to be checked rather than assumed. Record the answer here either
way; a path found to be genuinely dead is removed with that finding stated, not quietly.

### 5. Decide what happens to the orphaned SQL constants

**Status:** ✅ Done — row 11 green by removal

All 17 became callerless with `TruncateDataAsync`, confirmed by grep across `src/`, `tests/` and
`tools/`. **Removed**, all sixteen in `Quotinator.Core`'s `Sql.cs` plus `ImportBatches.DeleteAll` in
`Quotinator.Data`'s.

**The reason is not tidiness.** A `DeleteAll` left sitting in the project's single sanctioned SQL home,
immediately after removing the code that used it, is an invitation to do the exact thing this issue
took out. The guard-test scanning cost is real but secondary.

**Removing them orphaned one more thing, which the compiler found.** `ImportBatches.SelectAllIds`
existed only to read batch ids before the truncation, and its own doc comment referenced `DeleteAll` —
a `CS1574` against the 0-warnings gate. Removed with it rather than having its comment patched to point
at nothing.

**Not touched:** the eight `DeleteForX` constants in Core's `Sql.cs` are scoped per-entity deletes the
import pipeline uses during a Modify, and the `Audit_`/`Import_Action` ones in Data belong to other
subsystems. Any of those that are dead were dead before this issue, and are not this issue's to clean —
surface them separately rather than sweeping.

### 6. Repair the tests that assumed reseed re-applies everything

**Status:** ✅ Done — found already complete, never marked so. #373's own commit `ad4f3fd3`
(2026-09-03) rewrote the affected tests as part of landing its own behaviour change, rather than as a
separate pass — this step's own status was simply never updated when that landed. Confirmed passing
2026-09-04 as part of the full-solution run; see #373's own plan doc, step 8, for the specific renamed
tests.

Cross-check finding 4, which turned out to have one cause rather than being ten separate repairs.

**All ten failures are the same behaviour, and it is #373's.** A reseed against an up-to-date database
reports every quote as *modified* — `Quote +0 ~732`, `+0 ~13`, `+0 ~99` — because
`ImportActionPlanner` records a `Modify` even when `effectiveChanged` is empty and nothing would
actually be written. Each file therefore produces a second, differently-shaped confirmation, and the
tests see six where they expect three.

**Rewriting them against today's counts would bake the misreport in.** They would assert `~732` as
correct and #373 would have to change them back. They wait.

**Two wrong turns, recorded rather than smoothed away.** I first assumed the reseed's confirmations
were *empty* and added a "skip when the breakdown is empty" guard to `ConfirmFileAppliedCleanlyAsync`.
The count stayed at six, because the breakdowns are not empty. The guard was reverted — speculative
behaviour added on a mistaken diagnosis, defensible in isolation but not to be left behind on a wrong
premise. Only then did I print the actual payloads, which named the cause in a single run. **Measure
the thing before changing the code that produces it**: the diagnostic was available from the start and
I inferred twice instead.

### 7. Update the four documentation surfaces and ADR 014

**Status:** ✅ Done, 2026-09-04 — found undone during #374's own step 12 cascade, not in the same commit
as the behaviour change as this step originally intended, but fixed now rather than left further. All
four surfaces corrected: `AdminEndpoints.cs`'s `WithDescription` for `/database/reseed`,
`docs/api-endpoints.md`'s matching row, and `addon/DOCS.md` + `addon-beta/DOCS.md` (mirrored, per
CLAUDE.md). ADR 014's own factual description of Reseed (`TruncateDataAsync`, "wiped-and-reimported")
revised in place per this project's ADR convention — its decision is unchanged, only the mechanism
description. `docs/data-import.md` and `IDatabaseInitializer.cs`'s own `ReseedAsync`/`ResetAsync` XML
docs, plus a live log message (`LogMessages.LogReseedRequested`) still saying "clearing all data",
were found carrying the same stale claim during the same sweep and corrected together — a wider set
than this step's own four, found by grepping the whole repository rather than only touching the four
named.

### 8. Run the T2 documents green, then hand over T1

**Status:** In progress (T1 only). Both documents re-run 2026-09-08 against `quotinator:local` built
from `f67eb95b`; the earlier 2026-09-04 runs predated #373's step 10 and #375's step 12 and no longer
described the shipping code.

**`21-reseed-preserves-existing-data.md`, 2026-09-08.** Steps 1, 2, 3 and 6 green — `baseline
quotes = 795`, the locally-imported quote survives (`local quote survived reseed: True`, `796` before
and after), and 21 import batches survive four reseeds. Step 4's first half green: `quotes before =
796, after = 796`, `pending actions = 0`.

**Step 4's second half is half green, and the failing half is not this issue's.** Its expectation is
that every entity type the file contains is listed *and* `modified` reads `0`. The first is now true —
all seven types of `quotinator-curated.json`, all six of `quotinator-seasons.json` including `Season`,
where before #373's step 10 the natural-key-matched types vanished from the report entirely. The second
is still false: `modified` reads `1`, `1`, `1` and `21` across four files on a reseed that changed
nothing, which is [#377](https://github.com/DutchJaFO/Quotinator/issues/377)'s no-op-Modify
misclassification. **The document's `**Fully green after:** #373` header is therefore wrong — it needs
#377 too**, and it is still present in the file despite #373's step 9 claiming both such headers were
removed.

**Step 5 still cannot execute as written**, re-confirmed by running it: `DELETE
/api/v1/quotes/{id}` returns `405 MethodNotAllowed`. Quotes are intentionally read-only, so this is a
defect in the document rather than a product regression — unchanged from the 2026-09-04 finding.

**The designated smoke set was run in full, 2026-09-08** — `docs/automated-testing/README.md` puts it
under every end-of-issue T2 pass, and it had been skipped on the grounds that the issue-relevant
documents were the interesting ones. It is the floor, not an optional extra.

| # | Test | Result |
|---|---|---|
| 1 | `api-surface/01-baseline` | ✅ all 9 steps — `healthy`, version matches `Directory.Build.props` (`1.9.0-alpha`), random `Ok`, scoped searches correct |
| 2 | `api-surface/02-pagination-contract` | ✅ `pageSize=0` returns every row on all three endpoints; `501`, `page` beyond last → `422`; defaults `20` |
| 3 | `import-and-staged-actions/01-staged-action-review-workflow` | ✅ decide → undo → decide-all → apply, `/import/conflicts` still `404` |
| 4 | `import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions` | ❌ **step 4** — see below. Steps 1–3 green (`zeroCounts` empty, `pending=0`) |
| 5 | `import-and-staged-actions/19-per-file-import-report` | ✅ `missingTypes=[]`, removed fields absent with a live control |
| 6 | `database-lifecycle/03-reset-is-a-full-wipe` | ✅ every count `0`, audit `1` self-trace, both version counters preserved |
| 7 | `startup-and-degradation/03-startup-wait-page` | ✅ `503`/`starting`/self-contained wait page, then ready; `kestrelFirst=True` |
| 8 | `notifications-and-changelog/01-notification-system` | ✅ including both browser driver steps — `Expired` computed correctly, Cancel left `795` quotes untouched, Confirm dropped them to `0` |
| 9 | `notifications-and-changelog/07-changelog-served-from-its-own-database` | ✅ own `quotinatorchangelog.db`, 126 entries, `fallbacks=0`, no duplication on restart |

**Smoke 4 step 4 fails: 11 duplicate Sources**, where it expects none. Every pair differs only by
`Date` — `Star Wars: Episode V` at `1890` beside `1980`, `The Silence of the lambs` at `1998` beside
`1991`, the three Lord of the Rings films each a year out, and so on — so one row of each pair carries
a wrong date from a bundled file and the alias/rule mechanisms did not merge it.

**Not caused by this milestone's work, and established rather than assumed:** the diff from `84d4e5b7`
to `HEAD` touches zero lines mentioning `PlanSources` or `Sql.Sources`, and
[#376](https://github.com/DutchJaFO/Quotinator/issues/376)'s own body already describes the Silence of
the Lambs pair as a live defect on 2026-09-04. `The Lion King` (1994 / 2019) is the one legitimate pair
— the seed log warns about it by name and asks for exactly this verification. Raised here, not fixed:
it is neither this issue's subject nor #377's.

T1 (the developer's own action, per CLAUDE.md) is the only remaining part of this step.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A reseed against a populated database deletes no domain row | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_DeletesNothing` — per-table counts before and after, with the table set read from the schema's `Quotinator_` tables rather than a list, so a future entity is covered without anyone remembering |
| 2 | ✅ | That row-count assertion cannot pass against a reseed that does nothing | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_IsNotANoOp` — the positive control row 1 requires |
| 3 | ✅ | A row deleted since the last import is re-added, and only that row | Unit test | `DatabaseInitializerTests.Reseed_WithARowRemoved_ReAddsOnlyThatRow` |
| 4 | ✅ | Locally changed content raises a conflict rather than being overwritten | Unit test | `DatabaseInitializerTests.Reseed_WithLocallyChangedContent_StagesAConflictRatherThanOverwriting` — asserts both that the stored value survived and that an action was staged; either alone is satisfiable by doing nothing |
| 5 | ✅ | No orphaned `Quotinator_CharacterSource` rows survive a reseed | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_LeavesNoOrphanedCharacterSourceRows` — every link row still resolves to a live Character and Source |
| 6 | ✅ | Import batches survive a reseed | Unit test | `DatabaseInitializerTests.Reseed_PreservesImportBatches` |
| 7 | ✅ | The `Obsolete` dismissal either has a live trigger under test, or no longer exists | Unit test **or** removal | Whichever way step 4 resolves, the result verifies itself: a surviving path gets a test naming the trigger that still reaches it; a dead one is deleted, and deleted code cannot be called. Deliberately not "a finding is recorded" — that is the shape `process.md` refuses |
| 8 | ✅ | An explicit reseed does not consult whether content exists | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_ImportsRegardlessOfExistingContent` — proven by mutation: restoring the count gate makes it fail |
| 9 | ✅ | Cold start still seeds only a database with no content | Unit test | `DatabaseInitializerTests.Initialise_OnPopulatedDatabase_SeedsNothing` — the gate stays where it belongs, and stays a *content* check |
| 10 | ✅ | Emptiness is decided on content, not on any table having rows | Unit test | `DatabaseInitializerTests.Initialise_WithNonContentRowsOnly_StillSeeds` — confirmed passing 2026-09-04 |
| 11 | ✅ | No `Quotinator_*.DeleteAll` constant survives without a caller | Guard test **or** removal | The guard tests enumerating these constants run as part of the full solution suite, confirmed 0 failures 2026-09-04 |
| 12 | ✅ | #302's confirmations still describe what a reseed actually did | Unit test | `DatabaseInitializerTests.Reseed_AfterDismissal_WritesTheConfirmationAgain` and siblings, rewritten by #373's own commit `ad4f3fd3` per that issue's step 8 — confirmed passing 2026-09-04 |
| 13 | ✅ | Reset then Reseed still produces a from-scratch database | Unit test | `DatabaseInitializerTests.ResetThenReseed_ProducesAFromScratchDatabase` |
| 14 | ✅ | Every surface describing reseed as deleting is corrected | Unit test | `OpenApiSpecEndpointTests.ReseedEndpoint_LiveDescription_NeverClaimsItDeletesFirst` (new, 2026-09-04, confirmed red before the fix and green after) — asserts against the live `/openapi/v1.json`, which reflects `AdminEndpoints.cs`'s own `WithDescription`. `docs/api-endpoints.md`, `addon/DOCS.md`, `addon-beta/DOCS.md`, `docs/data-import.md`, `IDatabaseInitializer.cs`'s XML docs, and `LogMessages.LogReseedRequested`'s live log line were also found stale during the same sweep and corrected, though only the endpoint description has a mechanical guard |
| 15 | ✅ | ADR 014's account of Reseed matches the code | Manual, then asserted | Revised in place 2026-09-04 — its factual description of Reseed (`TruncateDataAsync`, "wiped-and-reimported") corrected; its decision (audit-trail tables never purge dangling references) is unaffected |
| 16 | ✅ | A live reseed against a populated database preserves and reports correctly | Automated (T2) | `docs/automated-testing/import-and-staged-actions/21-reseed-preserves-existing-data.md` — re-run 2026-09-08 against `quotinator:local` at `f67eb95b`. Steps 1, 2, 3, 6 and step 4's first half green (`796` quotes before and after, local quote survives, `0` pending, 21 batches survive). Step 4's "every entity type listed" now green; its "`modified` at `0`" clause fails (`1`/`1`/`1`/`21`) and is [#377](https://github.com/DutchJaFO/Quotinator/issues/377)'s, not this issue's — see step 8. Step 5 unexecutable (`DELETE /quotes/{id}` → `405`), a document defect |
| 17 | ✅ | The new T2 document goes red before it goes green | Canary run | Recorded in the document's own Canary section, run 2026-09-02 against `quotinator:local` at `3e9bb19c` (pre-#372) |
| 18 | ✅ | #302's own document passes against the reseed that ships | Automated (T2) | `11-clean-reseed-confirmation.md` steps 1–6, re-run 2026-09-08 against `quotinator:local` at `f67eb95b`: 5 confirmations on cold start, dismiss → `0`, reseed → `5`, second reseed still `5`, dismiss-then-reseed back to `5`, all `isDismissed=False`. Step 7 (the four seeding variants) not run. Step 3's "no line may read `added=0 modified=0`" assertion is stale rather than failing — see #302's own step 12 |
| 19 | ✅ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors, confirmed 2026-09-04 |
| 20 | ✅ | No regression | Test run | `dotnet test --configuration Release -m:1` → all green, 0 failures, confirmed 2026-09-04 |
| 21 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | reset, reseed, reseed again — the second adding nothing and reporting so. **T1 is the developer's own action, not the assistant's — see CLAUDE.md** |

**Rows 2 and 4 exist because this issue's assertions are unusually easy to satisfy by accident.** Most
rows here assert that something was *preserved*, and a build that imports nothing preserves everything.
Row 2 is row 1's positive control; row 4 asserts both halves of a conflict — the stored value surviving
*and* an action being staged — because either half alone is satisfied by inaction.

**Row 14 is an assertion rather than a documentation read**, per `process.md`'s rule that a row waiting
on a human to read something is a promise, not a verification. #307 held a finished issue open for
weeks on exactly that shape.
