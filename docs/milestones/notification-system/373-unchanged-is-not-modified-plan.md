# #373 — An import that re-states identical content reports it as modified

**Status:** In progress (step 9) — steps 1–8 done; the T2 runs wait on [#374](https://github.com/DutchJaFO/Quotinator/issues/374)
**GitHub issue:** #373
**Tiers required:** T1, T2
**Depends on:** [#372](https://github.com/DutchJaFO/Quotinator/issues/372) for reproduction — a reseed
that truncates first can never reach this path — and [#374](https://github.com/DutchJaFO/Quotinator/issues/374)
for the T2 runs, which reseed real content. **Blocks #372's step 6**, and #302 behind it

---

## Description

`ImportActionPlanner` records a `Modify` action for a row whose resolved value is identical to what is
stored. It already computes the evidence — `effectiveChanged`, the fields that differ under
`FieldMergeResolver.ValuesEqual` — and ignores it when empty. A reseed of unchanged files therefore
reports every quote as updated: `Quote +0 ~732`.

**Two different nothings have to be distinguishable** (developer, 2026-09-02): nothing happened because
there was no content, versus nothing happened because the content already matched. That is what makes a
reseed's purpose legible — re-import to heal deleted or altered content, and see which rows were
already correct.

**A report states what arrived and what became of it** (developer, 2026-09-02): "5 quotes incoming
resulting in 2 new quotes and 3 that were already in the database." Outcome counts alone do not say
that; an incoming count per entity type does.

**Every incoming item is accounted for, of every type** (developer, 2026-09-02): "if cold start says
'7 characters, 13 quotes, etc added' then the reseed should say '7 characters unchanged, 13 quotes
unchanged etc.' … hiding such details would have us chase non-existent bugs."

That widens the defect. Quotes are misreported; **every other entity type is not reported at all.** The
planner's own summary states the design — it emits actions for "the Quote itself and any
*not-yet-existing* Source/Character/Person it references", and every *not-yet-existing*
StageDirection/SoundCue/Conversation. An entity that already exists produces no action, so it appears
nowhere. Measured 2026-09-02 on `quotinator-curated.json`:

| | Reported |
|---|---|
| Cold start | `Character +7, Conversation +4, Person +3, Quote +13, SoundCue +1, Source +7, StageDirection +2` |
| Reseed | `Quote +0 ~13` — and nothing else at all |

The seven Characters, three People and seven Sources were all incoming and all already correct. A
reader has no way to tell that from a reader whose file never mentioned them.

## Cross-check against authoritative sources, 2026-09-02

Per `docs/workflow/process.md`'s Planning step 3. Findings 1 and 4 were wrong in this plan's first
draft and were corrected by the developer — both are recorded as corrections rather than quietly fixed.

1. **ADR 008 applies.** `Unchanged` is added to `ImportActionKind`, an existing persisted enum, so the
   full checklist follows: `Import_Action.ActionType` carries `CHECK (ActionType IN ('Add', 'Modify'))`
   in the baseline and in three migration files, SQLite cannot widen a CHECK in place, so this is a
   table-rebuild migration; the baseline is updated to match in the same commit and both drift tests
   are extended.

   **The first draft argued the opposite** — that an unchanged row is not an action and should be
   counted without being persisted, avoiding the migration. Overruled (developer): "it should be
   traceable as such". A count that exists only in a response cannot be inspected afterwards, and
   tracing what an import did to a row is the point.

2. **No JSON schema covers the report.** `schemas/` describes source files, not API responses. Checked
   rather than assumed.

3. **The counts are enumerated by hand in three places**, all of which CLAUDE.md requires in the same
   commit as the behaviour: the seed log line (`QuotinatorDatabaseInitializer.cs`),
   `docs/api-endpoints.md` twice, and the endpoint `[Description]` attributes.

4. **`NotificationTable` gains no column.** The first draft proposed a third one; overruled (developer):
   "we have metadata for that kind of content". The payload already carries structured detail, and the
   notification's own body is where the summary belongs — *5 incoming, 2 new, 3 already stored*. The
   rendered table stays as it is.

5. **Persisting the kind means no signature change.** `ImportActionReportBuilder` switches on `Status`
   first and consults `ActionType` for `Decided`/`Applied`, so a persisted `Unchanged` needs one new
   arm and nothing else. Had it not been persisted, `PlanAsync` would have had to return a tally
   alongside its actions — a second reason the developer's correction simplifies the change.

6. **The builder silently drops actions that match no arm.** Two `_ => counts` fall-throughs discard a
   row rather than counting it. An `Incoming` count equal to the sum of every outcome bucket makes that
   observable, which is why row 6 asserts the identity rather than just the number.

---

## Steps

**The step order enforces red-first; it is not left to memory.** Step 1 writes every test — unit and
automated — and runs them before any behaviour changes.

**Red and green are per step.** Every step names the rows it owns, re-runs them at the start to observe
them fail, and ends with them passing.

**An exception is not a red.** #372 spent three steps with a row failing on `no such column: Text`
rather than on its assertion, which looks identical for a test asserting the opposite. Read the failure
message of every row, not just its red status.

**This issue's reds are easy to fake.** Most rows assert something is reported as *unchanged*, and a
build reporting nothing at all satisfies that equally well. Every such row carries a control naming
non-zero counts and real entity types.

### 1. Write every test first, and run them red

**Status:** ✅ Done — 13 unit tests red on their own assertions, 4 controls green by design, T2 canary red

**Red:** rows 1 (CHECK constraint), 3 (`ReportsUnchangedNotModified`), 7 (`LeavesNothingPending`),
8 (`ExistingReferencedEntities`), 9 (`ExistingCompositeEntities`), 11–13 (report builder), 14
(`FormatReport`), 15–16 (reseed reporting), 20 (endpoint descriptions), plus the T2 document.

**Green by design:** rows 2, 4, 5, 6, 10 and 17 — controls and guards asserting what must *not* change.
Row 17 in particular: an older payload renders correctly today, because the new DTO fields default to
zero. It guards a regression rather than proving a fix.

**Row 1's red is an exception, and that is correct here** — `CHECK constraint failed: ActionType IN
('Add', 'Modify')`. The test asserts an INSERT succeeds; the exception names the constraint being
widened. Unlike #372's `no such column: Text`, it is not masking a wrong test.

**Row 9's first red *was* the wrong kind and was corrected.** `Single()` on an empty sequence throws
`InvalidOperationException`, which looks identical for a test asserting the opposite. Rewritten to
count first, then read.

**T2 canary, against the post-#372, pre-#373 build.** Both halves of the defect in one output:
`quotinator-curated.json` reports `Quote incoming=0 new=0 modified=13 unchanged=0` and nothing else —
one entity type where its cold start reported seven, and thirteen unchanged quotes counted as modified.
Recorded in the document's own *Canary* section.

Exit condition: every unit test in the verification table exists and **fails on its own assertion**,
and `21-reseed-preserves-existing-data.md`'s unchanged assertions have been run and failed.

**A defect in #308's own test surfaced here, and closing it is part of this step** (developer,
2026-09-02). `NotificationTableTests.PayloadDetail_ForEveryKind_IsSelfDescribing` — #308's verification
row 21 — cannot fail. `PayloadDetail` dispatches on `notification.MetadataKind.Parsed`, and the
`WithTitle` helper never sets it, so `TryDeserialize(null, json)` returns null for every kind and the
table is always empty. The assertion is `Assert.AreEqual(rows > 0, headers > 0)`, which with both zero
is `false == false`. Proven by passing a real, valid `ReseedFileApplied` payload through the helper and
getting zero rows.

**The remedy is full coverage per kind, not a one-line fix to the helper** (developer, 2026-09-02):
"given that we know what metadata kinds we have and which classes are associated we should have
positive and negative tests for these … we need full test coverage for all known variants, because that
will show us red the moment we introduce a new variant that has not been covered."

So each `NotificationMetadataKind` gets both directions, and the set is derived from the enum rather
than listed, so a member added later fails until it is covered. `NotificationMetadataKinds.PayloadTypes`
already holds one entry per member and is itself guarded — the same shape applied one layer up, to what
each kind *renders* rather than to what it deserializes into.

### 2. Add the enum member and its migration

**Status:** ✅ Done — rows 1–2 green

**Start of step:** row 1 red on `CHECK constraint failed: ActionType IN ('Add', 'Modify')`.
**End of step:** both green; the whole `Quotinator.Data.Tests` suite passes apart from rows 11–13,
which are step 5's.

Migration 20, `ImportActionUnchangedMigrations.WidenActionTypeForUnchanged` — a full table rebuild of
`Import_Action`, since SQLite cannot widen an inline CHECK, matching what migrations 15, 17 and 18 did
for the same reason. The copy carries every column straight across and rewrites no value: only the
constraint admits one more member, so every row valid before is valid after. The baseline's own
`CREATE TABLE` was widened in the same commit, which is what rows 1 and 2 hold it to.

**The enum member itself landed in step 1**, not here — the tests naming it could not compile without
it, and a compile error is not a red test. Adding the member alone was safe because nothing wrote
`Unchanged` until step 3; the CHECK would have rejected it if anything had.

`ImportActionKind.Unchanged`, with the ADR 008 checklist in one commit: a table-rebuild migration
widening `CHECK (ActionType IN ('Add', 'Modify', 'Unchanged'))`, the baseline updated to match, and
both schema-drift tests extended — the structural one and the CHECK-constraint one, since
`PRAGMA table_info` does not capture what a constraint accepts.

**Status for an unchanged action is `Applied`.** The row is terminal: nothing to decide, nothing to
write. `Applied` is what routes it to the builder's `ActionType` arm, and it means "this import dealt
with this row", which is true. It is not `Pending` — nobody is waiting on anything.

### 3. Classify an unchanged quote

**Status:** ✅ Done — rows 3–7 green

**Start of step:** rows 3, 7 red on their assertions; 4, 5, 6 green as controls.
**End of step:** all five green, with 121 existing planner tests unaffected.

**This step's own instruction was wrong, and the code says so where it matters.** It read "where
`effectiveChanged` is empty" — but under `Skip`, `resolved` is deliberately set to the *existing*
values, so `effectiveChanged` is empty for every Skip regardless of whether the content matches. Gating
on it would report a genuine disagreement the operator chose to skip as though the file agreed with the
database.

The classification compares **incoming against stored**, over the union of both field sets, through the
same `FieldMergeResolver.ValuesEqual` the Review branch already uses. Whether the file agrees with the
database is not a question any policy changes the answer to.

An unchanged action's status is `Applied`: terminal, nothing to decide, nothing to write, nothing to
reverse.

### 4. Account for every other entity type, which today emits nothing

**Status:** ✅ Done — rows 8–10 green, all 123 planner tests passing

**Start of step:** rows 8 and 9 red on their assertions; row 10 green as its control.
**End of step:** all three green.

**Nine sites, and the code had already named the defect at every one of them.** Each carried the
comment *"Unchanged — silent reuse, same as a natural-key match"* — the behaviour was described
accurately and simply never reported. Source (two branches), Person, Character, Universe, Series,
StageDirection, SoundCue and Conversation, plus the three reference resolvers
(`ResolveSourceAsync`/`ResolveCharacterAsync`/`ResolvePersonAsync`), which emit on the database lookup
rather than per referencing quote — the existing index is what makes that "once per distinct entity",
exactly as it already did for `Add`.

`UnchangedAction` is shared rather than copied per site: nine hand-written copies is how `Series` and
`Universe` would quietly end up shaped differently from `Source`.

**The blast radius was 18 existing tests, not the 8 this section first estimated.** Every
`*_NoActionStaged` / `*_NothingChanged` test in the planner suite asserted "no action of any kind" for
an entity that arrived and matched — true only because such an entity produced nothing at all. Each is
now scoped to exclude `Unchanged`, which preserves exactly what it was written to prove: an `Unchanged`
action stages no change. The assertion reads as its own explanation
(`ActionType.Parsed != ImportActionKind.Unchanged`).

**Estimating it at 8 was the error, and the estimate came from grepping rather than running.** The
eight were the ones failing at the time I looked, which was before the composite planners were
converted.

Source, Character, Person, Series, Universe, StageDirection, SoundCue and Conversation each produce an
action only when they do **not** already exist. An incoming row that matches what is stored is
currently invisible, which is the half of this defect that hides work rather than misdescribing it.
Each emits an `Unchanged` action instead.

**The extra rows are the feature, not its cost** (developer, 2026-09-02). A reseed will write roughly
one action per incoming entity rather than only per new one — hundreds where there were none — and that
is what makes an import auditable after the fact rather than only observable while it runs.

It is also what makes a reseed **safe when the sources change**. Bundled content is refreshed from
upstream; after a refresh the operator needs to see exactly which items the new version added or
altered, distinguished from the ones it left alone. Without a row per incoming entity there is nothing
to distinguish them by, and the operator is left guessing which of their data a reseed touched — which
is the reason to run one at all. #249's auto-purge clears a batch's actions once it applies fully, so
the rows do not accumulate indefinitely.

**Existing behaviour must not shift.** These types are planned as "create if absent"; adding an
`Unchanged` action must not make an absent entity stop being created, nor change the insertion order
that apply-time relies on — a Conversation's lines reference StageDirections and SoundCues planned
before it.

**Eight existing tests assert the behaviour being replaced and change with it** — one more than the two
this section originally named, the rest found by running the suite after the resolvers changed:

`PlanAsync_ExistingSourceCharacterPerson_ReusesRealIds_NoAddActionsForThem`,
`ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId`,
`ResolveCharacterAsync_SeriesScopedCrossSourceMatch_ReusesExistingCharacter`,
`ResolveCharacterAsync_ExistingGlobalCharacter_CaseInsensitiveNameMatch_ReusesRealId`,
`PlanAsync_SourceAliasMatches_ResolvesToExistingCanonicalSource_NoSpuriousSourceAdd`,
`PlanAsync_ModifyPathWithTypeMismatch_AliasAppliedBeforeSourceResolution_NoSpuriousSourceCreated`,
`ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_NoActionStaged`, and
`PlanStageDirectionsAsync_IdMatchFound_NothingChanged_NoActionStaged`.

**Each is scoped, not loosened, and the distinction matters.** Most are named for what they actually
guard — *no spurious Source **Add***, *reuses the real id* — while asserting the far broader "no action
of any kind". That was only ever true because an existing entity produced nothing at all. Scoping each
assertion to the kind its own name claims restores what it was written to prove; it does not weaken it.



`ImportActionPlannerTests.PlanStageDirectionsAsync_IdMatchFound_NothingChanged_NoActionStaged` asserts
`0` StageDirection actions, with the message *"Nothing differs — silent reuse, no action staged"*. That
silent reuse is precisely the defect: the row arrived, matched, and left no trace. It becomes one
`Unchanged` action.


`ImportActionPlannerTests.PlanAsync_ExistingSourceCharacterPerson_ReusesRealIds_NoAddActionsForThem`
asserts `HasCount(1, actions)` — "Only the Quote is new". Under this step it becomes four: the Quote
plus an `Unchanged` for each of Source, Character and Person. Its other half — that the resolved
payload carries the *real* existing ids rather than freshly generated ones — is unaffected and stays.
Named here so the change is a planned consequence rather than a surprise during execution.

### 5. Report what arrived and what became of it

**Status:** ✅ Done — rows 11–14 green

**Start of step:** rows 11–14 red on their own assertions.
**End of step:** all four green.

`Incoming` is counted **before** the outcome switch, not derived after it. That placement is the whole
value: an action reaching neither `_ => counts` arm is still counted as having arrived, so the identity
row 12 asserts stops holding and the dropped row becomes visible. Counting it inside the switch would
have reproduced the same blind spot one line lower.

`EntityTypeActionCounts` gains `Incoming` and `Unchanged`. `Incoming` is every action for that entity
type, so it equals the sum of the outcome buckets — asserted as an identity, which is what exposes the
builder's two `_ => counts` fall-throughs. The seed log line gains both alongside the six it prints by
hand.

### 6. Say it in the notification's own words, and lay its detail out for the new shape

**Status:** ✅ Done — rows 15–19 green

**The message text and the detail layout are both in scope** (developer, 2026-09-02): an unchanged
result is a third thing to display, and neither the sentence nor the table was written with it in mind.

**The message is new text, so it is three files in lockstep.** `UI.en-GB.json`, `UI.nl.json` and
`UI.de.json` all gain the key in the same commit — `TranslationCompletenessTests` fails otherwise, and
a body assembled in English would render half-translated for a Dutch or German reader, which is
exactly what #319 exists to prevent.

**Settled 2026-09-02: the metadata changes, the table does not** (developer). "It is the metadata we
use that would have to be changed, not the notification table."

**No count is ever derived from another.** A proposal to render `Entity / Incoming / Added / Updated`
and leave unchanged as a subtraction was rejected outright, and the reason generalises: "5 incoming, 3
added does not mean 2 are unchanged. It only means 3 were added. The 'missing' 2 could have been errors
in the import that could not be resolved." Blocked, stale, discarded, pending and failed rows all live
in that gap. Every outcome is counted explicitly or it is not reported at all.

So `ReseedEntityCountDto` carries every outcome as an independent figure — `Incoming`, `Added`,
`Modified`, `Unchanged`, `Blocked`, `Pending`, `Discarded` and `Stale` — and
`NotificationTable.PayloadDetail` keeps its `Entity / Added / Updated` columns untouched.

**The last four are recorded even though this branch should never produce them** (developer,
2026-09-02: "any information that tells us what happened during an import/reseed is valuable and helps
us find issues that happen during those actions faster"). A confirmation is written from the clean-apply
path, so `Blocked`/`Pending`/`Discarded`/`Stale` are normally zero — and a non-zero one is exactly the
thing worth finding quickly. Stating them costs four integers; assuming them absent costs a
reader working out why the numbers do not add up.

**The row filter is `Incoming > 0`, not a test over the outcome buckets.** Filtering on outcomes would
drop precisely the rows this issue exists to surface: an unchanged-only breakdown, or one whose entire
content was blocked.

**One consequence has to be handled rather than inherited.** Step 5's filter now admits an
unchanged-only breakdown, which against the unchanged table renders `Quote | 0 | 0` — a row of zeros
that states nothing and reads as a bug. Those rows are omitted from the *rendered table* while
remaining in the *payload*, which is exactly the split this decision draws: the metadata is the
complete record for the API, the log and any audit; the table is a view of what changed; the body
states the totals, unchanged among them.

`ReseedEntityCountDto` gains the fields, and the confirmation's body reads as *N incoming, X new, Y
already stored* rather than *X added and Y updated*. **No new column** in the rendered table — the
payload is the structured detail, the body is the summary.

**A no-op reseed must still confirm each file.** Its breakdown is unchanged-only, so the
`Added > 0 || Modified > 0` filter has to admit it; otherwise the confirmation vanishes exactly when
this issue's whole point is to report it.

**Old notification rows lack the fields and must still render**, deserialising to `0`.

### 7. Update the documented shape

**Status:** ✅ Done — row 20 green

**Row 20's own selector was wrong twice, and each attempt over-matched.** "per-entity-type" also
catches the reset endpoint, which mentions the report without listing its buckets, and `/import`, which
uses the phrase for policy overrides. "blocked" also catches the reset endpoint's "reset blocked only
by that quota". The enumeration's own slash-joined tail — `pending/stale` — appears nowhere else, and a
future description listing the old set still matches it and still fails.

`docs/api-endpoints.md` (both occurrences) and the endpoint `[Description]` attributes, same commit.

### 8. Unblock #372's step 6

**Status:** ⬜ Not started — turns row 21 green

The ten #302/#303 tests failing on #372's branch are all this behaviour. Each is rewritten to assert
what is now true, stating whether the old form was over-broad or the behaviour changed.

### 9. Run the T2 documents green

**Status:** 🚧 Blocked on [#374](https://github.com/DutchJaFO/Quotinator/issues/374) — turns rows 22–24 green

**A reseed of the bundled content leaves 22 pending reviews, and that is #374's.** Both documents
reseed real content, so neither can pass end to end until a rule can recognise its own outcome as
already applied. Measured with the fixture that mirrors the bundled manifest: cold start `0` pending,
one reseed `22`, a second `44`.

**Found by writing the positive test the developer asked for** (2026-09-02): "we always test positive
and negative aspects … we therefore also need a seeding test that does have 0 pending reviews so we
have proof of the positive aspect." The negative fixture has no rule file and can never apply, so it
only ever proved the stuck case stays stuck. The positive one — `Seed_WithAResolvableFile_...`, which
does pass — is what exposed that the ordinary path breaks on the *second* run.

`21-reseed-preserves-existing-data.md` and #302's `11-clean-reseed-confirmation.md`, whose
`**Fully green after:**` headers both name this issue — delete those lines when they pass. T1 is the
developer's own.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The migration and the baseline accept the same `ActionType` values | Unit test | `DatabaseInitializerOwnershipTests` CHECK-constraint drift test, extended with `Unchanged` and a rejected value, on both the baseline and the replay path |
| 2 | ❌ | The migration and the baseline produce an identical `Import_Action` schema | Unit test | the structural drift test, extended — a table rebuild is where a column silently changes shape |
| 3 | ❌ | Re-importing identical content classifies the action as unchanged | Unit test | `ImportActionPlannerTests.ReimportingIdenticalContent_ReportsUnchangedNotModified` |
| 4 | ❌ | Its counts are non-zero and name real entity types | Unit test | the control row 3 needs — reporting nothing at all satisfies row 3 without it |
| 5 | ❌ | Genuinely changed content is still `Modify` | Unit test | `ImportActionPlannerTests.ChangedContent_StillReportsModified` — without it, a planner classifying everything as unchanged passes rows 3 and 4 |
| 6 | ❌ | Content absent from the database is `Add`, never unchanged | Unit test | `ImportActionPlannerTests.AbsentContent_ReportsNewNotUnchanged` — the two nothings, told apart |
| 7 | ❌ | An unchanged action needs no decision | Unit test | `ImportActionPlannerTests.ReimportingIdenticalContent_LeavesNothingPending` — status is terminal, not `Pending` |
| 8 | ❌ | An already-existing Source, Character or Person is reported, not omitted | Unit test | `ImportActionPlannerTests.ExistingReferencedEntities_AreReportedUnchanged` — today they produce no action at all and vanish from the report |
| 9 | ❌ | The same holds for StageDirection, SoundCue and Conversation | Unit test | `ImportActionPlannerTests.ExistingCompositeEntities_AreReportedUnchanged` — planned by a different branch, so covered separately rather than assumed to follow |
| 10 | ❌ | An absent entity of those types is still created | Unit test | `ImportActionPlannerTests.AbsentReferencedEntities_AreStillAdded` — the control: reporting existing ones must not stop the planner creating missing ones, nor disturb the insertion order a Conversation's lines depend on |
| 11 | ❌ | The report carries an unchanged count | Unit test | `ImportActionReportBuilderTests`, extended |
| 12 | ❌ | `Incoming` equals the sum of every outcome bucket | Unit test | `ImportActionReportBuilderTests.Incoming_EqualsTheSumOfEveryOutcome` — the identity that exposes the two `_ => counts` fall-throughs, which drop a row today rather than counting it |
| 13 | ❌ | An action matching no outcome arm is caught, not dropped | Unit test | same test driven with an action the switch does not match; fails on row 12's identity |
| 14 | ❌ | The seed log prints both new counts | Unit test | assertion over the formatted line, so a hand-written format string cannot silently omit one |
| 15 | ❌ | A reseed of unchanged files confirms each file once, not twice | Unit test | `DatabaseInitializerTests.Reseed_AgainstCurrentContent_WritesOneConfirmationPerFile` — the growth this issue removes |
| 16 | ❌ | Every entity type the cold start reported is reported again by the reseed | Unit test | `DatabaseInitializerTests.Reseed_ReportsEveryEntityTypeTheColdStartDid` — cold start's seven types for `quotinator-curated.json` reappear as unchanged rather than collapsing to one. This is the row the developer's own reading names: a missing type reads as work that never happened |
| 17 | ❌ | A notification written before this issue still renders | Unit test | `NotificationTableTests` — a payload with no `unchanged`/`incoming` field reads as `0` rather than throwing |
| 18 | ❌ | The new message text exists in all three locales | Unit test | `TranslationCompletenessTests` (existing) — fails on a key present in `UI.en-GB.json` and missing or empty elsewhere |
| 19 | ❌ | The rendered detail accommodates an unchanged result | Unit test | `NotificationTableTests.PayloadDetail_ForEveryKind_IsSelfDescribing` (existing, #308) — headers exist exactly when rows do and every row's cell count matches, so whichever layout step 6 settles on is held to the same contract |
| 20 | ❌ | The documented breakdown matches what is returned | Unit test | assertion over the `[Description]` text, so an edit dropping a count fails rather than being caught by eye |
| 21 | ❌ | #372's ten blocked tests pass | Test run | the ten named in #372's step 6 |
| 22 | ❌ | A live reseed against an up-to-date database reports unchanged | Automated (T2) | `21-reseed-preserves-existing-data.md`, its `Fully green after` line removed |
| 23 | ❌ | #302's document passes end to end | Automated (T2) | `11-clean-reseed-confirmation.md`, same |
| 24 | ❌ | The T2 assertions go red before they go green | Canary run | run at step 1 against the pre-work build — `HEAD` at that moment, no worktree needed |
| 25 | ❌ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 26 | ❌ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 27 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | reseed twice against unchanged files; the second names every entity type that arrived and says it was already stored, and adds no second notification |

**Rows 4, 5 and 6 exist because "reports unchanged" is satisfied by reporting nothing.** Row 5 is the
sharpest: a planner classifying *everything* as unchanged would pass rows 3, 4 and 7 perfectly.

**Row 14 is not a forward-compatibility nicety.** #302's confirmations are already written and
persisted on the developer's own database; a payload change that cannot read them is a regression in
reading history.
