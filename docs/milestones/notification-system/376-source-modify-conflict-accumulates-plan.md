# #376 — A Source-level Modify conflict stages a new Pending action on every reseed

**Status:** Planning
**GitHub issue:** #376
**Tiers required:** T1, T2
**Depends on:** (none)

**Next action: execute it** — every scope question is settled (see *Decisions*), the reproduction is
confirmed, and the site inventory is written down.

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

### 1. Write the accumulation tests and confirm each is red

**Status:** ⬜ Not started

All in `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs`, modelled on
`Reseed_Repeatedly_WithACaseOnlyPendingModify_PendingCountNeverGrows` — cold start, two reseeds,
assert the count staged at cold start never grows. Temp-directory fixtures only, never
`data/sources/`.

The list below is the estimate; the exact set settles against the code in step 2, and any named test
not written gets its reason recorded here rather than dropped silently.

| Site | Test | Expected before the fix |
|---|---|---|
| Source, natural-key | `Reseed_Repeatedly_WithASourceModifyConflict_PendingCountNeverGrows` | ❌ red — measured 1 → 2 → 3 |
| Source, explicit-id | `Reseed_Repeatedly_WithAnExplicitIdSourceModifyConflict_PendingCountNeverGrows` | ❌ red |
| Source, date backfill | `Reseed_Repeatedly_WithABlockedSourceDateBackfill_BlockedCountNeverGrows` | ❌ red |
| Source, stale rule | `Reseed_Repeatedly_WithAStaleSourceRule_StaleCountNeverGrows` | ❌ red |
| Series | `Reseed_Repeatedly_WithASeriesModifyConflict_PendingCountNeverGrows` | ❌ red |
| Universe | `Reseed_Repeatedly_WithAUniverseModifyConflict_PendingCountNeverGrows` | ❌ red |
| Season | `Reseed_Repeatedly_WithASeasonModifyConflict_PendingCountNeverGrows` | ❌ red |
| Person | `Reseed_Repeatedly_WithAPersonModifyConflict_PendingCountNeverGrows` | ❌ red |
| Character | `Reseed_Repeatedly_WithACharacterModifyConflict_PendingCountNeverGrows` | ❌ red |
| Conversation + StageDirection + SoundCue | `Reseed_Repeatedly_WithAConversationModifyConflict_PendingCountNeverGrows` | ❌ red — one curated-conversation fixture reaches all three |

Two controls, without which a fix that simply stops staging anything passes every row above:

| Control | Test | Expected before **and** after |
|---|---|---|
| A genuinely new conflict is still staged on a later reseed | `Reseed_WithANewSourceConflict_StillStagesIt` | ✅ green both sides |
| A conflict that becomes resolvable between reseeds is staged and applied, not skipped | `Reseed_AfterARuleResolvesAKnownConflict_AppliesItInsteadOfSkipping` | ❌ red at the Quote site today, ✅ at the Source sites |

The second control is what decision 2 rests on. A green result at the Quote site falsifies it, and
this plan's decision 2 must be rewritten before step 5 proceeds rather than the test being adjusted
to agree with it.

### 2. Confirm every site in the inventory is actually reachable

**Status:** ⬜ Not started

The table in *The site inventory* was read from the source, not driven. A site whose unresolved status
no entry shape can actually produce needs no guard and no test — and a site reachable by a shape the
estimate above missed needs both. Resolve each row to reachable-or-not before writing the fix, and
record the outcome in that table.

`PlanPeopleAsync`/`PlanCharactersAsync` take no `conflictRules` argument at all, so their `Stale`
column is empty by construction; confirm their `Pending`/`Blocked` are genuinely reachable rather than
assuming symmetry with the rule-consulting methods.

### 3. Add `ImportActionKind.AlreadyReported` and its widening migration

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

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

### 6. Carry the count through the report, the confirmation and the detail table

**Status:** ⬜ Not started

Following what #377 did for `ResolvedToExisting`, end to end:

1. `EntityTypeActionCounts.AlreadyReported` (`src/Quotinator.Data/Import/FileImportReport.cs`) and the
   matching bucket arm in `ImportActionReportBuilder.Build`.
2. `ReseedEntityCountDto.AlreadyReported` (`src/Quotinator.Data/Notifications/`), `[JsonPropertyName("alreadyReported")]`
   — a plain `int`, so every notification written before this deserialises to `0`, exactly as
   `Incoming` documents for its own #373 introduction.
3. `QuotinatorDatabaseInitializer`'s confirmation writer, which builds the DTO from the report.
4. `NotificationTable.razor.cs` — a seventh column and a seventh cell in #383's totals row.
5. `UI.en-GB.json`, `UI.de.json`, `UI.nl.json` — `NotificationsDetailAlreadyReportedColumn`, all three
   in the same commit per the localisation checklist.
6. Every existing assertion of the form
   `Incoming == Added + Modified + Unchanged + ResolvedToExisting + Skipped` gains the new term. These
   are real edits to passing tests, so each one is checked by reading, not by re-running until green.

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
| 11 | ❌ | Every test in rows 1–10 was genuinely red before step 5 | Live | `dotnet test --filter "Reseed_Repeatedly_With"` at the commit before step 5 — each listed test fails with a growing count |
| 12 | ❌ | A genuinely new conflict is still staged on a later reseed | Unit test | `DatabaseInitializerTests.Reseed_WithANewSourceConflict_StillStagesIt` — green before and after |
| 13 | ❌ | A conflict that becomes resolvable between reseeds is applied, not skipped — at every site including Quote | Unit test | `DatabaseInitializerTests.Reseed_AfterARuleResolvesAKnownConflict_AppliesItInsteadOfSkipping` |
| 14 | ❌ | Every site in the inventory is resolved reachable-or-not by driving it, not by reading | Live | Step 2's outcome recorded in the inventory table; no row left as an unverified reading |
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
