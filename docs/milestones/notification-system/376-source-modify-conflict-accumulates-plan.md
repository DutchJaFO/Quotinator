# #376 — A Source-level Modify conflict stages a new Pending action on every reseed

**Status:** In progress (step 5)
**GitHub issue:** #376
**Tiers required:** T1, T2
**Depends on:** (none)

---

## Description

`ImportActionPlanner` stages three statuses that are never applied and never resolve on their own —
`Pending`, `Blocked`, `Stale`. A row carrying one of them keeps its stored values unchanged, so the
next reseed compares against exactly the same stored values, reaches exactly the same conclusion, and
stages a brand-new duplicate action on top of the one still awaiting review. The count grows by one
per reseed, unbounded.

#374 fixed this for `Quote` — first for the Add branch's `Pending`, then the same branch's `Blocked`,
then the Modify branch, then `Stale` — via `Sql.Quotes.SelectHasUnresolvedActionById`, consulted
before staging. That query hardcodes `EntityType = 'Quote'`, and nothing outside the Quote branch
consults anything like it.

### Measurement

Taken during planning, per #377's lesson that a plan resting on a prediction is a false plan. Two
throwaway `DatabaseInitializerTests`-shaped diagnostics, run against the current branch and deleted
afterwards.

**The defect reproduces, exactly as described.** A two-file fixture — file A a quote establishing a
date-less `Fixture Film` Source, file B a `sources[]` entry declaring `"date": "1999"` for the same
title, both under `review` with no rule — puts `PlanSourcesAsync`'s natural-key branch on the
date-backfill path and stages a `Pending` Modify:

```
cold: Source/Pending=1   reseed 1: Source/Pending=2   reseed 2: Source/Pending=3
```

**The issue's own reproduction steps no longer reproduce.** Seeding the real bundled corpus through
`ManifestSeedPlanner.PlanSeed(data/sources)` and reseeding twice stages **zero** `Pending`, `Blocked`
or `Stale` actions for any entity type, on all three passes:

```
cold:     Source/Applied=599   Quote/Applied=841    (no Pending/Blocked/Stale of any type)
reseed 1: Source/Applied=1199  Quote/Applied=1682
reseed 2: Source/Applied=1799  Quote/Applied=2523
```

The Silence of the Lambs disagreement the issue names has since been declared away: a dated
`SourceAliasRule` (`nikhilnamal17-source-aliases.json:38`, `1998` → `1991`), a matching
`ConflictResolutionRule` (`nikhilnamal17-conflict-rules.json:246`), and an explicit `sources[]`
declaration (`quotinator-series-universe.json:399`) now cover it between them. The issue body's steps
2–4 are stale and cannot be used as evidence for anything; the defect stands on the synthetic
reproduction instead. **The `3 → 4 → 5` figure in the issue is likewise stale** and must not be
carried into the plan or the closing comment as though it were current.

The `Applied` rows growing 599 → 1199 → 1799 is separate and out of scope — that is `Import_Action`
retaining an applied history #372 deliberately does not truncate, not an unresolved conflict.

### The site inventory

Every place an unresolved status is staged, and whether anything guards it. Line numbers are current
as of `36fa96ad`.

| Entity | Method / branch | `Blocked` | `Stale` | `Pending` | Guarded |
|---|---|---|---|---|---|
| Quote | `PlanAsync`, Add | `:373` | `:390` | `:389` | ✅ `:325` |
| Quote | `PlanAsync`, Modify | `:554` | `:574` | `:587` | ✅ `:424` |
| Source | `ResolveSourceAsync`, date backfill | `:843` | — | — | ❌ |
| Source | `PlanSourcesAsync`, explicit-id | `:1199` | `:1217` | `:1224` | ❌ |
| Source | `PlanSourcesAsync`, natural-key | `:1361` | `:1378` | `:1385` | ❌ |
| Person | `PlanPeopleAsync` | `:1549` | — | `:1556` | ❌ |
| Character | `PlanCharactersAsync` | `:1752` | — | `:1759` | ❌ |
| Universe | `PlanUniverseAsync` | `:1900` | `:1917` | `:1924` | ❌ |
| Series | `PlanSeriesAsync` | `:2099` | `:2116` | `:2123` | ❌ |
| Season | `PlanSeasonsAsync` | `:2292` | `:2293` | `:2294` | ❌ |
| StageDirection | `PlanStageDirectionsAsync` | `:2418` | — | `:2425` | ❌ |
| SoundCue | `PlanSoundCuesAsync` | `:2520` | — | `:2527` | ❌ |
| Conversation | `PlanConversationsAsync` | `:2632` | — | `:2639` | ❌ |

**Eleven unguarded sites across nine entity types.** The issue names two of them (`PlanSourcesAsync`'s
pair) and does not mention `ResolveSourceAsync`'s own `Blocked` date backfill, which is Source-level
too and sits in a different method.

### Executability review

Run before step 1, on the developer's instruction to confirm the plan can be executed as written. Four
things it changed, each folded into the step it affects; nothing it found blocks the plan.

- **Migration 22 is free** — `DataOwnedMigrations`' highest version is 21 (#377). `TextClauses` is
  `public` in `Quotinator.Data.Queries` and already used throughout `Quotinator.Core`'s own `Sql`, so
  step 4 needs no visibility change.
- **No exhaustive `switch` on `ImportActionKind` exists**, so a new member cannot break a consumer by
  omission. `ImportActionReportBuilder`'s switch falls through to `_ => counts`, which means a missing
  bucket arm shows up as `Incoming` no longer equalling its parts — the exact signal #373 added
  `Incoming` to produce — rather than as a crash. Step 6 supplies the arm regardless.
- **Every entity's fixture follows one recipe** (step 1), which was not obvious before reading the
  tail methods: `PlanStageDirectionsAsync`, `PlanSoundCuesAsync`, `PlanConversationsAsync` and
  `PlanCharactersAsync` match by explicit id only, with no natural-key fallback.
- **Two existing invariant assertions are already short a term** (step 6).

### What the cross-check found

- **The issue names a test that does not exist.** It cites
  `DatabaseInitializerTests.Reseed_Repeatedly_WithAResolvableFile_PendingCountNeverGrows` as the
  Quote-level guarantee to match. The real name is
  `Reseed_Repeatedly_WithACaseOnlyPendingModify_PendingCountNeverGrows`, with
  `..._WithABlockedCollision_BlockedCountNeverGrows` and
  `..._WithAStaleRuleConflict_StaleCountNeverGrows` alongside it. Those three are the shape this
  issue's tests copy.
- **ADR 008 governs part of this.** `Import_Action.ActionType` is enum-backed and carries a
  `CHECK (ActionType IN (…))` constraint, so decision 3 below is a schema change with a migration,
  a baseline update and a drift test — not a DTO edit. `docs/architecture-decisions/` carries nothing
  on staging accumulation itself; ADR 014 governs dangling audit references, a different concern.
- **`docs/testing-policy.md` forbids the shipped test reading `data/sources/`** — every
  reseed-stability sibling uses a temp-directory fixture, and so must these. The corpus measurement
  above stays a planning diagnostic, not a test.
- **`Import_Action.EntityType` is a discovered text column** for `SqlTextCaseGuard`
  (`DiscoverTextColumnNames` picks up every `string`-typed property on `ImportActionEntity`). The
  existing query passes only because `EntityType = 'Quote'` is a literal; the moment it becomes
  `EntityType = @entityType` the guard requires `TextClauses.Equals`. `EntityId` needs
  `IdClauses.Equals`, as it already has.

---

## Decisions

All four taken by the developer, 2026-09-09, against the measurement above rather than around it.

### 1. Fix the class, not the two sites the issue names

One shared, entity-type-parameterised check consulted at all eleven sites, replacing
`Sql.Quotes.SelectHasUnresolvedActionById` rather than sitting beside it.

The reason is #374's own history: the same defect was found and fixed four separate times, each time
in whichever single mechanism had just been observed live, because each fix was scoped to the report
rather than to the class. Its own XML doc records three of those recurrences. `CLAUDE.md` states the
rule directly — *"when fixing an instance of this bug, grep the same file/module for sibling
comparisons of the same kind and fix them together"* — and this is the fifth recurrence of a class
whose siblings are now enumerated above.

### 2. The check runs at the point of staging, and Quote is brought into line

#374's Quote check runs at the top of the branch, before rule resolution — so a quote already
carrying an unresolved action can never be re-resolved even after an operator adds a covering rule to
the rule file. Only a decision through `POST /import/actions/…/decide`, which changes the action's own
status, releases it. That is a latent defect in #374, not a property worth copying ten more times.

The check therefore fires only when the action about to be staged is itself unresolved, so a row that
has *become* resolvable since the last reseed is staged and applied normally. Quote's two call sites
move to the same placement. Step 1's second control is what proves this rather than asserts it: if it
comes back green at the Quote site today, this reasoning is wrong and must be corrected here before
step 5 begins.

### 3. A guarded row is reported, not silently absent — as its own `ImportActionKind`

Staging nothing at all makes the row vanish from that file's report, because
`ImportActionReportBuilder` derives `Incoming` from the staged actions themselves. That is what
happens for Quote today, and it means a reseed confirmation quietly under-reports by the number of
already-known conflicts — the reader cannot tell a shrinking `Incoming` from content the file stopped
mentioning.

**This is #373's problem, and it gets #373's answer.** *"Silent reuse is exactly what left a whole
entity type absent from the report"* — and both times this project has hit it, it staged a row rather
than plumbing a side-channel count: `ImportActionKind.Unchanged` (#373, migration 20) and
`ImportActionKind.ResolvedToExisting` (#377, migration 21), each with a widening CHECK migration built
from the same table-rebuild template. A third member, `AlreadyReported`, follows both exactly.

Staged `Applied`, terminal, and never re-applied — identical to `ResolvedToExisting`'s own contract, so
nothing re-stamps `DateModified`, re-attributes `ImportBatchId`, or writes an `Audit_Change` row. It
accumulates in `Import_Action` as applied history at the same rate `Unchanged` already does, which is
the accumulation #372 deliberately keeps; the counts this issue holds flat are `Pending`/`Blocked`/
`Stale`.

The rejected alternative was a side-channel tally returned alongside `PlanAsync`'s action list. It
avoids a migration, and it was rejected because it puts a number in the report that no row backs —
the exact shape #373 replaced, and one that cannot be reconciled against `Import_Action` afterwards.

**`Incoming` gains a term.** `Incoming = Added + Modified + Unchanged + ResolvedToExisting + Skipped`
is asserted today (e.g. `Reseed_SourceModifyResolvesToExactlyExistingValues_NotCountedAsModified`) and
becomes `… + AlreadyReported`. #383's totals line gains a seventh column and the detail table gains a
seventh header, in all three translation files.

### 4. The issue body is commented on, not edited

Its reproduction steps, its `3 → 4 → 5` figure and its test name are all wrong now. A comment carries
the correction; the body stays as the record of what was genuinely observed on 2026-09-04. Drafted for
approval before posting, per `process.md`'s draft-then-act rule.

---

## Steps

### 1. Confirm every site is reachable and settle each fixture shape

**Status:** ✅ Done

**Reordered ahead of the tests during execution.** This step was drafted second, which could not work:
a fixture cannot be written until it is known which field of an entity can actually differ, and for
several entities that is not the field the entry DTO makes obvious. Renumbered rather than worked out
of order.

Outcome per entity, read from each `ToFieldMap` overload and each method's own matching strategy —
every site is reachable, and the fixture shape each one needs is now fixed:

| Entity | Fields actually diffed | Matched by | Fixture makes this field differ |
|---|---|---|---|
| Source | title, type, date, seriesId, seasonId | id, then natural key | `date` |
| Person | name, dateOfBirth, dateOfDeath | id, then name | `dateOfBirth` |
| Character | **name only** | **explicit id only** | `name` |
| Universe | **name only** | id, then name | `name`, via **explicit id** |
| Series | name, universeId | id, then name | `universeName` |
| Season | number, title, subtitle, seriesId | id, then (seriesId, number) | `title` |
| StageDirection | text, imageUrl | **explicit id only** | `text` |
| SoundCue | text, soundFileUrl, imageUrl | **explicit id only** | `text` |
| Conversation | description | **explicit id only** | `description` |

Three findings that change how the fixtures are written:

1. **Universe diffs only `name`, which is also its natural key** — so a natural-key fixture can never
   produce a Universe Modify at all. Its conflict is reachable only through the explicit-id branch,
   where the id matches and the name differs. Character has the same shape and is id-matched anyway.
2. **`ResolveSourceAsync`'s `Blocked` date backfill needs a `Complete` row, which no import file can
   produce.** #382 established that `CompletenessStatus` is human-set only — every table defaults to
   `Incomplete`, `ComputeNextStatus` can only reach `NeedsReview`, and no entry DTO carries a
   completeness field. That fixture therefore sets `CompletenessStatus` directly by SQL as test setup,
   which is honest about what it is rather than pretending a file can reach it.
3. **A stale comment claims the opposite of the code.** `ImportActionPlanner.cs:2357` still says
   StageDirection/SoundCue/Conversation are *"Add-only and id-keyed … no Modify/merge semantics"* —
   #68's original note. All three now have a full Modify branch with `Blocked` and `Pending` exits.
   The comment is corrected in step 5, where those sites are touched anyway.

### 2. Write the accumulation tests and confirm each is red

**Status:** ✅ Done

**Measured: ten red, control A green, control B red.** Every accumulation test grows 1 → 2 on the
first reseed, uniformly across all nine entity types. Control A passes before the fix, which is what
makes it a control. Control B is red, which **confirms decision 2 rather than falsifying it** — a
Quote already carrying an unresolved action stays unresolved on the next reseed even once the rule
file covers it.

**Three fixture defects were found by running them, all in the fixtures rather than the code.** Each
is recorded at its own test, because each is a trap the next person writing one of these will hit:

1. **`PersonEntryDto.Id` is `required`**, and an entry omitting it does not skip that entry — it
   throws inside `SourceQuoteFileReader.TryParse`, which catches `JsonException` and discards **the
   whole file**. The first Person fixture produced zero actions and zero rows, with nothing pointing
   at the cause. *Reportable, not fixed here:* one malformed entry silently costing an entire file is
   a diagnosability gap of its own.
2. **#374 settles each Source variant once per pass** (`stagedVariantIds`), so in the Blocked-backfill
   fixture the *dated* quote has to come first. With the undated one leading, the variant is already
   settled and the dated quote never reaches the backfill path at all — the fixture staged an
   `Unchanged` Source and nothing else.
3. **Control A had to count distinct entities, not rows.** Before the fix the row count is 3 (the
   known conflict duplicated, plus the new one) and after it is 2, so a row-count assertion cannot be
   green on both sides — it would be measuring the accumulation instead of asking the control's own
   question.

All in `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs`, modelled on
`Reseed_Repeatedly_WithACaseOnlyPendingModify_PendingCountNeverGrows` — cold start, two reseeds,
assert the count staged at cold start never grows. Temp-directory fixtures only, never
`data/sources/`.

**One fixture recipe covers every entity type: two files in one batch, both `review`, the second
restating an entity the first created with exactly one field different, and no covering rule.** The
first file's entry is Added and applied; the second's disagreement stages an unresolved action which
is never applied, so the stored row keeps the first file's values and every later reseed reaches the
same conclusion again. This is what the Source measurement above already demonstrates, and it
generalises because the disagreement lives *between two files*, not between the file and itself — a
single file restating its own content produces `Unchanged` on every reseed, which is why the naive
one-file fixture proves nothing.

`PlanCharactersAsync`, `PlanStageDirectionsAsync`, `PlanSoundCuesAsync` and `PlanConversationsAsync`
match by explicit id only, with no natural-key fallback, so both files must state the same explicit
`id` for those four. `PlanPeopleAsync`, `PlanSeriesAsync`, `PlanUniverseAsync` and `PlanSeasonsAsync`
have a natural key and can be written either way; use the natural key, since that is the shape a
curated file actually takes.

The list below is the estimate; the exact set settles against step 1, and any named test
not written gets its reason recorded here rather than dropped silently.

| Site | Test | Expected before the fix |
|---|---|---|
| Source, natural-key | `Reseed_Repeatedly_WithASourceModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Source, explicit-id | `Reseed_Repeatedly_WithAnExplicitIdSourceModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Source, date backfill | `Reseed_Repeatedly_WithABlockedSourceDateBackfill_BlockedCountNeverGrows` | ❌ red — 1 → 2 |
| Source, stale rule | `Reseed_Repeatedly_WithAStaleSourceRule_StaleCountNeverGrows` | ❌ red — 1 → 2 |
| Series | `Reseed_Repeatedly_WithASeriesModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Universe | `Reseed_Repeatedly_WithAUniverseModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Season | `Reseed_Repeatedly_WithASeasonModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Person | `Reseed_Repeatedly_WithAPersonModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Character | `Reseed_Repeatedly_WithACharacterModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 |
| Conversation + StageDirection + SoundCue | `Reseed_Repeatedly_WithAConversationModifyConflict_PendingCountNeverGrows` | ❌ red — 1 → 2 for all three |

Two controls, without which a fix that simply stops staging anything passes every row above:

| Control | Test | Expected before **and** after |
|---|---|---|
| A genuinely new conflict is still staged on a later reseed | `Reseed_WithANewSourceConflict_StillStagesIt` | ✅ green — measured before the fix |
| A conflict that becomes resolvable between reseeds is staged and applied, not skipped | `Reseed_AfterARuleResolvesAKnownConflict_AppliesItInsteadOfSkipping` | ❌ red — confirms decision 2 |

The second control is what decision 2 rests on, and it came back red: a Quote already carrying an
unresolved action is still unresolved after a reseed in which the rule file covers it. The branch-top
placement really does hold a resolvable conflict shut, so decision 2 stands as written rather than
needing the rewrite it was prepared to take.

### 3. Add `ImportActionKind.AlreadyReported` and its widening migration

**Status:** ✅ Done

Nothing needed bumping beyond the list itself: both schema-version counters derive from
`DataOwnedMigrations.Count`, so no hardcoded `21` existed to find. `Quotinator.Data.Tests` is green at
1,358 tests, including the CHECK-value round-trip now asserting `AlreadyReported` on both the baseline
and incremental paths.

Per decision 3, following `ImportActionUnchangedMigrations` (#373, version 20) and
`ImportActionResolvedToExistingMigrations` (#377, version 21) as the template — a `Import_Action`
table rebuild, since SQLite has no `ALTER TABLE … MODIFY CHECK`. All in `Quotinator.Data`, which owns
this table:

1. `src/Quotinator.Data/Enums/ImportActionKind.cs` — the new member, with an XML summary saying what
   it is distinct *from* (`Unchanged`, `ResolvedToExisting`, and producing no action at all), the way
   both siblings already do.
2. `src/Quotinator.Data/Database/ImportActionAlreadyReportedMigrations.cs` — a new file, version 22,
   widening the CHECK to `('Add', 'Modify', 'Unchanged', 'ResolvedToExisting', 'AlreadyReported')`.
3. `DatabaseInitializer.DataOwnedMigrations` — register version 22.
4. `DatabaseInitializer.DataBaselineSql` (`:216`) — the same widened CHECK, in the same commit, per
   `CLAUDE.md`'s baseline rule.
5. The schema-drift tests that compare baseline against incremental replay, including the
   CHECK-constraint-behaviour one — `PRAGMA table_info` does not capture a CHECK structurally.

Never edit versions 20 or 21.

### 4. Add the shared query and retire the Quote-only one

**Status:** ✅ Done

The two Quote call sites now go through a `HasUnresolvedActionAsync` helper rather than an inline
`ExecuteScalarAsync`, which is what makes step 5's nine further sites a call rather than a copy. Their
*placement* is unchanged here — that is step 5's own change, kept separate so this step is provably
behaviour-neutral: the full Core suite reports exactly the eleven failures step 2 measured, no more.

**One thing the plan did not anticipate:** `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory`
keeps a hand-maintained list of every `Sql.*` constant permitted to contain an aggregate, and renaming
the query moved its entry. The list is what stops an unreviewed `COUNT`/`GROUP BY` appearing (the
CVE-2025-6965 guard), so the entry was moved rather than dropped.

`Sql.Quotes.SelectHasUnresolvedActionById` becomes
`Sql.ImportActions.SelectHasUnresolvedActionByEntity` in `src/Quotinator.Core/Queries/Sql.cs` — a new
nested class, since the query is no longer about one entity:

```
SELECT COUNT(*) FROM Import_Action
WHERE {TextClauses.Equals("EntityType", "entityType")}
  AND {IdClauses.Equals("EntityId", "entityId")}
  AND Status IN ('Pending', 'Blocked', 'Stale');
```

`TextClauses.Equals` on `EntityType` is required, not optional — see *What the cross-check found*.
Replaced rather than added beside, so there is one definition of "unresolved" and not two that can
drift the way the status list already drifted three times inside the single Quote query.

The `Status IN (…)` list stays a literal — it is not external input, and the existing query's own
literal already passes `SqlTextCaseGuard`. Adding the nested class puts it in the
`SqlQueryGuardTests`/`RepositorySqlGuardTests` `DynamicData` enumeration automatically.

### 5. Consult it at every site, and stage `AlreadyReported` instead of nothing

**Status:** ⬜ Not started

One private helper on `ImportActionPlanner`, called immediately before each unresolved staging and
only when the status about to be staged is unresolved (decision 2). When it fires, the site stages an
`AlreadyReported`/`Applied` action for that entity id rather than `continue`-ing silently (decision 3).
Three placement constraints:

- **After the index assignment.** `sourceIndex[…]`, `seriesIndex[…]`, `seasonIndex[…]` and their
  siblings are read by later entities in the same pass; skipping past them breaks resolution for every
  quote referencing the skipped entity. Every site assigns its index before it stages.
- **Not before the `Unchanged` early exit.** A row with an unresolved action that now genuinely
  matches must still report `Unchanged`; only the unresolved staging is replaced.
- **Quote's two existing checks move here too**, from the top of their branches — decision 2. Their
  `#374` comments are rewritten to name the reversal and cite both issues, so a reader who greps `#374`
  lands on the change rather than on the changed text.

Each site gets a short comment naming #376 and pointing at the shared helper — not a restatement of
the mechanism at eleven separate places.

**One duplicate this deliberately does not remove.** Actions are staged per *file*
(`QuotinatorDatabaseInitializer` calls `PlanAsync` then `StageAsync` inside its own `foreach (SeedFile …)`),
so the database check does see an earlier file's staged actions — which is exactly why the two-file
fixture recipe in step 1 works — but it cannot see a second entry for the same entity *within the file
currently being planned*. #378 solved that class for the Quote Add branch with an in-pass
`HashSet<string>`. It stays out of scope here: an in-file duplicate stages a constant number of rows,
identical on every reseed, so it is not the unbounded growth this issue exists to stop. Recorded rather
than left unstated, so the next reader does not mistake it for an oversight.

### 6. Carry the count through the report, the confirmation and the detail table

**Status:** ⬜ Not started

Following what #377 did for `ResolvedToExisting`, end to end:

1. `EntityTypeActionCounts.AlreadyReported` (`src/Quotinator.Data/Import/FileImportReport.cs`) and the
   matching bucket arm in `ImportActionReportBuilder.Build`.
2. `ReseedEntityCountDto.AlreadyReported` (`src/Quotinator.Data/Notifications/`), `[JsonPropertyName("alreadyReported")]`
   — a plain `int`, so every notification written before this deserialises to `0`, exactly as
   `Incoming` documents for its own #373 introduction.
3. `QuotinatorDatabaseInitializer`'s confirmation writer, which builds the DTO from the report.
4. `NotificationTable.razor.cs` — a seventh column, a seventh cell in #383's totals row, and
   `|| c.AlreadyReported > 0` added to the row filter's OR-list, which exists so a row that arrived is
   never hidden by having no outcome the table happens to name.
5. `UI.en-GB.json`, `UI.de.json`, `UI.nl.json` — `NotificationsDetailAlreadyReportedColumn`, all three
   in the same commit per the localisation checklist.
6. Every existing assertion of the form `Incoming == Added + Modified + …` gains the new term.
   **Two of the three are already short a term before this issue touches them**:
   `DatabaseInitializerTests:1109` and `:1464` omit `ResolvedToExisting` and pass only because their
   fixtures produce none, while #377's own `Reseed_SourceModifyResolvesToExactlyExistingValues_NotCountedAsModified`
   states it in full. Both gain `ResolvedToExisting` as well as `AlreadyReported` — the same assertion,
   written correctly, not extra scope. These are edits to passing tests, so each is checked by reading
   rather than by re-running until green.

### 7. T2 document

**Status:** ⬜ Not started

The bundled corpus stages nothing unresolved (see *Measurement*), so a live document cannot use it as
its fixture. The route that works is the one #382 established: a `{dataDir}/imports/` folder carrying
its own `manifest.json`, a quotes file and a `sources[]` file, which seeds through
`QuotinatorDatabaseInitializer` — the path that passes a `ConflictRuleLookup` and runs the full
reseed pipeline. Reseed three times via `POST /api/v1/admin/database/reseed` and read the count back
through `GET /api/v1/import/actions?pageSize=0`, which is the issue's own reproduction shape with a
fixture that still reproduces.

Two documents, not one, per #382's own finding that a stateful document fails twice with only the
first failure real: the positive (an already-reported conflict does not accumulate) and the negative
(a genuinely new conflict on a later reseed is still staged). The negative must be green on **both**
images or it is a post-fix artefact rather than a regression guard.

Confirm red against a `docker build` of the commit before step 5, per `process.md`'s Implementation
step 1, then tear down container, image and worktree. Add both to
`docs/automated-testing/README.md`'s index and to `Quotinator.slnx`; `Smoke: no`.

### 8. Full build and test run

**Status:** ⬜ Not started

`dotnet build --configuration Release` and
`dotnet test --configuration Release --verbosity normal -m:1`, both `0 Warning(s)  0 Error(s)`. Run
after step 7, so it covers the new documents' index and solution entries —
`RepositoryStructureTests` asserts both.

`.editorconfig`: measure `IDE0008`/`IDE0090` for any file this issue touches that is not already in
the scoped lists before adding it, per the ratchet's own rule.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A Source natural-key Modify conflict stages once and never accumulates | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithASourceModifyConflict_PendingCountNeverGrows` — cold == reseed1 == reseed2 |
| 2 | ❌ | Same, Source explicit-id branch | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAnExplicitIdSourceModifyConflict_PendingCountNeverGrows` |
| 3 | ❌ | Same, `ResolveSourceAsync`'s Blocked date backfill | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithABlockedSourceDateBackfill_BlockedCountNeverGrows` |
| 4 | ❌ | Same, a Source-level stale rule | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAStaleSourceRule_StaleCountNeverGrows` |
| 5 | ❌ | Same, Series | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithASeriesModifyConflict_PendingCountNeverGrows` |
| 6 | ❌ | Same, Universe | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAUniverseModifyConflict_PendingCountNeverGrows` |
| 7 | ❌ | Same, Season | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithASeasonModifyConflict_PendingCountNeverGrows` |
| 8 | ❌ | Same, Person | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAPersonModifyConflict_PendingCountNeverGrows` |
| 9 | ❌ | Same, Character | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithACharacterModifyConflict_PendingCountNeverGrows` |
| 10 | ❌ | Same, Conversation / StageDirection / SoundCue | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAConversationModifyConflict_PendingCountNeverGrows` |
| 11 | ✅ | Every test in rows 1–10 was genuinely red before step 5 | Live | Run at `bd7026c9` + the tests only — all ten fail, each growing 1 → 2 on the first reseed; control A green, control B red |
| 12 | ❌ | A genuinely new conflict is still staged on a later reseed | Unit test | `DatabaseInitializerTests.Reseed_WithANewSourceConflict_StillStagesIt` — green before and after |
| 13 | ❌ | A conflict that becomes resolvable between reseeds is applied, not skipped — at every site including Quote | Unit test | `DatabaseInitializerTests.Reseed_AfterARuleResolvesAKnownConflict_AppliesItInsteadOfSkipping` |
| 14 | ✅ | Every site is resolved reachable-or-not, and each fixture shape fixed, before any fixture was written | Live | Step 1 — per-entity table of diffed fields, matching strategy and the field each fixture makes differ |
| 15 | ❌ | One shared query, no Quote-only duplicate left behind | Live | `grep -rn "SelectHasUnresolvedActionById" src/` → no matches; `Sql.ImportActions.SelectHasUnresolvedActionByEntity` is the only definition |
| 16 | ❌ | The shared query is case-insensitive on both `EntityType` and `EntityId` | Unit test | `SqlQueryGuardTests` — `SqlTextCaseGuard` and `SqlIdCaseGuard` both clean over the new nested class |
| 17 | ❌ | Migration 22 widens the CHECK and the baseline matches it exactly | Unit test | The consumer/data schema-drift pair — baseline-created schema and incrementally-replayed schema identical, including CHECK-accepted values |
| 18 | ❌ | An `AlreadyReported` row is staged `Applied` and never re-applied | Unit test | `ImportActionPlannerTests` — status `Applied`, kind `AlreadyReported`; no `Audit_Change` row and no `DateModified` change for that entity |
| 19 | ❌ | The already-reported row appears in the reseed confirmation instead of vanishing | Unit test | `DatabaseInitializerTests` — the entity type's `ReseedEntityCountDto.AlreadyReported` is 1 and its `Incoming` does not drop between reseeds |
| 20 | ❌ | The breakdown still adds up with the new term | Unit test | Every existing `Incoming == Added + Modified + Unchanged + ResolvedToExisting + Skipped` assertion, updated and green |
| 21 | ❌ | The detail table renders a seventh column and totals it | Unit test | `NotificationTable` column/totals tests — header from `NotificationsDetailAlreadyReportedColumn`, totals cell equals the summed value |
| 22 | ❌ | The new key exists and is non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 23 | ❌ | Live: an already-reported Source conflict does not accumulate across three reseeds | T2 | `docs/automated-testing/import-and-staged-actions/<N>-…md` — red on the pre-fix image (count grows), green on the post-fix image (count flat) |
| 24 | ❌ | Live: a genuinely new conflict on a later reseed is still staged | T2 | `docs/automated-testing/import-and-staged-actions/<N+1>-…md` — green on **both** images, which is what makes it a regression guard |
| 25 | ❌ | Build and full suite clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1` — `0 Warning(s)  0 Error(s)`, 0 failed, run after step 7 |
| 26 | ❌ | T1: the app starts without error | Live | Developer runs it in Visual Studio — clean startup, then a Reset and two reseeds, no errors |
| 27 | ❌ | The stale reproduction steps and the widened scope are recorded on the issue itself | Live | A comment on #376 carrying the measurement, the site inventory, and the four decisions |
