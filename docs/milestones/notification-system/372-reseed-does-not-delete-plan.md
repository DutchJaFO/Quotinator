# #372 — Reseed should only import the designated files, not delete data first

**Status:** Planning — the plan below is complete; execute it in order
**GitHub issue:** #372
**Tiers required:** T1, T2
**Depends on:** nothing. **Blocks #302**, whose final T2 and T1 wait on this

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

**Status:** ⬜ Not started

Exit condition: every unit test in the verification table exists and **fails on its own assertion**,
and the new T2 document has been run against the current build and failed.

**This issue's reds are unusually easy to fake, and that is the thing to guard.** Most rows assert
that something is *preserved*. Against today's build, truncation deletes the rows, so a preservation
assertion fails for the right reason — but a row asserting "no conflict was raised" or "nothing was
imported" can pass vacuously against a build that imports nothing at all. Every such row needs its
positive control named in the same step, the way row 4's zero-files case needed one in #302.

**The T2 document is written and run now**, while `HEAD` is still the pre-work build — the only point
at which its canary costs nothing. Record the result in the document's own *Canary* section.

### 2. Give cold start and reseed their own method bodies

**Status:** ⬜ Not started — turns rows 8–9 green

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

### 3. Remove the deletion

**Status:** ⬜ Not started — turns rows 1–5 green

`TruncateDataAsync` is deleted along with its call in `OnReseedAsync`. Not corrected, not made
prefix-driven — reseed stops deciding what survives.

### 4. Settle the batch-removal and `Obsolete`-dismissal question

**Status:** ⬜ Not started — turns rows 6–7 green

`BatchIdsAsync` and `DismissAlertsForRemovedBatchesAsync` exist because reseed removed import batches;
with nothing removed, they have nothing to act on. **Establish where else #303's `Obsolete` dismissal
applies before deleting it** — an absence of callers is not the same as knowing it is unwanted, and
the reachable-from-Reset case has to be checked rather than assumed. Record the answer here either
way; a path found to be genuinely dead is removed with that finding stated, not quietly.

### 5. Decide what happens to the orphaned SQL constants

**Status:** ⬜ Not started — turns row 10 green

All 17 `Quotinator_*.DeleteAll` constants have no caller outside `TruncateDataAsync` and become dead
with it. `Sql.*` constants are enumerated by the guard tests, so a kept-but-unused constant is still
scanned on every run — keeping them is not free. Decide and record; do not leave them unexamined.

### 6. Repair the tests that assumed reseed re-applies everything

**Status:** ⬜ Not started — turns rows 11–12 green

Cross-check finding 4. `Reseed_AfterDismissal_WritesTheConfirmationAgain` expects six confirmations
across two reseeds, which cannot hold once the second imports nothing. Each such test is rewritten to
assert what the new behaviour makes true, not loosened until it passes — and each rewrite states which
of the two it is, since "the assertion was over-broad" and "the behaviour changed" are different
findings and only one of them is about this issue.

### 7. Update the four documentation surfaces and ADR 014

**Status:** ⬜ Not started — turns rows 13–14 green

Cross-check findings 1 and 2, all in the same commit as the behaviour change per CLAUDE.md.

### 8. Run the T2 documents green, then hand over T1

**Status:** ⬜ Not started — turns rows 15–17 green

This issue's own document, plus a re-run of #302's `11-clean-reseed-confirmation.md`, whose step 2
measures a reseed against a populated database and is the reason #302 is blocked on this. T1 is the
developer's own.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A reseed against a populated database deletes no domain row | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_DeletesNothing` — per-table counts before and after, with the table set read from the schema's `Quotinator_` tables rather than a list, so a future entity is covered without anyone remembering |
| 2 | ❌ | That row-count assertion cannot pass against a reseed that does nothing | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_IsNotANoOp` — the positive control row 1 requires |
| 3 | ❌ | A row deleted since the last import is re-added, and only that row | Unit test | `DatabaseInitializerTests.Reseed_WithARowRemoved_ReAddsOnlyThatRow` |
| 4 | ❌ | Locally changed content raises a conflict rather than being overwritten | Unit test | `DatabaseInitializerTests.Reseed_WithLocallyChangedContent_StagesAConflictRatherThanOverwriting` — asserts both that the stored value survived and that an action was staged; either alone is satisfiable by doing nothing |
| 5 | ❌ | No orphaned `Quotinator_CharacterSource` rows survive a reseed | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_LeavesNoOrphanedCharacterSourceRows` — every link row still resolves to a live Character and Source |
| 6 | ❌ | Import batches survive a reseed | Unit test | `DatabaseInitializerTests.Reseed_PreservesImportBatches` |
| 7 | ❌ | The `Obsolete` dismissal either has a live trigger under test, or no longer exists | Unit test **or** removal | Whichever way step 4 resolves, the result verifies itself: a surviving path gets a test naming the trigger that still reaches it; a dead one is deleted, and deleted code cannot be called. Deliberately not "a finding is recorded" — that is the shape `process.md` refuses |
| 8 | ❌ | An explicit reseed does not consult whether content exists | Unit test | `DatabaseInitializerTests.Reseed_OnPopulatedDatabase_ImportsRegardlessOfExistingContent` — proven by mutation: restoring the count gate makes it fail |
| 9 | ❌ | Cold start still seeds only a database with no content | Unit test | `DatabaseInitializerTests.Initialise_OnPopulatedDatabase_SeedsNothing` — the gate stays where it belongs, and stays a *content* check |
| 10 | ❌ | Emptiness is decided on content, not on any table having rows | Unit test | `DatabaseInitializerTests.Initialise_WithNonContentRowsOnly_StillSeeds` — a database holding reference-shaped rows but no quotes is still seeded. Guards the broadening that [#310](https://github.com/DutchJaFO/Quotinator/issues/310) would otherwise turn into a silent skip on every new install |
| 11 | ❌ | No `Quotinator_*.DeleteAll` constant survives without a caller | Guard test **or** removal | Removed constants verify themselves by absence. Any kept constant is named by an assertion over `Sql.*` requiring a caller, so "kept for later" cannot pass silently — the guard tests already enumerate these constants, so an unused one is scanned on every run rather than being free |
| 12 | ❌ | #302's confirmations still describe what a reseed actually did | Unit test | `DatabaseInitializerTests.Reseed_AfterDismissal_...`, rewritten per step 6, each stating whether the old form was over-broad or the behaviour changed |
| 13 | ❌ | Reset then Reseed still produces a from-scratch database | Unit test | `DatabaseInitializerTests.ResetThenReseed_ProducesAFromScratchDatabase` — the composition that replaces what reseed used to do alone, and the only remaining route to that outcome |
| 14 | ❌ | Every surface describing reseed as deleting is corrected | Unit test | `RepositoryStructureTests`-style assertion over `AdminEndpoints`' own description text, so a future edit reintroducing "clears all data" fails rather than being caught by eye |
| 15 | ❌ | ADR 014's account of Reseed matches the code | Manual, then asserted | Revised in place; row 14's assertion covers the endpoint text, and the ADR is checked as part of step 7 rather than left to a reader |
| 16 | ❌ | A live reseed against a populated database preserves and reports correctly | Automated (T2) | new `docs/automated-testing/import-and-staged-actions/NN-reseed-preserves-existing-data.md` |
| 17 | ❌ | The new T2 document goes red before it goes green | Canary run | written and run at step 1 against the pre-work build, which is `HEAD` at that moment — no worktree needed |
| 18 | ❌ | #302's own document passes against the reseed that ships | Automated (T2) | re-run of `11-clean-reseed-confirmation.md`, unblocking #302's rows 38–39 |
| 19 | ❌ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 20 | ❌ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 21 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | reset, reseed, reseed again — the second adding nothing and reporting so |

**Rows 2 and 4 exist because this issue's assertions are unusually easy to satisfy by accident.** Most
rows here assert that something was *preserved*, and a build that imports nothing preserves everything.
Row 2 is row 1's positive control; row 4 asserts both halves of a conflict — the stored value surviving
*and* an action being staged — because either half alone is satisfied by inaction.

**Row 14 is an assertion rather than a documentation read**, per `process.md`'s rule that a row waiting
on a human to read something is a promise, not a verification. #307 held a finished issue open for
weeks on exactly that shape.
