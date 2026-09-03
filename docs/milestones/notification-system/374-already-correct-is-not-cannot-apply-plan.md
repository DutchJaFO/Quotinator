# #374 — A conflict rule cannot tell "already correct" from "cannot apply"

**Status:** Planning
**GitHub issue:** #374
**Tiers required:** T1, T2
**Depends on:** [#375](https://github.com/DutchJaFO/Quotinator/issues/375)

---

## Description

`ConflictRuleLookup.TryResolve` reports two outcomes where there are four. It matches a rule on
`(entityId, field)`, compares the rule's recorded `existingRecord`/`incomingRecord` snapshot against the
current values, and sets one `isStale` flag when either differs. That flag conflates *the result we
wanted is already in place* with *this rule can no longer be applied*. After the first import the stored
value *is* the value the rule produced, so `recordedExisting` can never equal `currentExisting` again: a
rule that resolved something is permanently in the "differs" state on every subsequent run, and the
resolution encoded in the rules file can never be recognised as already applied.

**The end result is what matters** (developer, 2026-09-03): "if the expected value is 'x' then all we
need to know is whether we still have to change the target value to 'x' or add it if it was missing. If
the incoming differs from what was recorded then the rule no longer applies and it is 'stale', so we
signal that we need to update the rule."

**The issue's title names its symptom; its scope is the intent behind it** — a reseed leaves nothing
pending when the initial seeding left nothing, however often it runs. Step 1 found that the symptom and
the intent have different causes, and the Scope changes section below records the decision to keep both.

---

## Scope changes

**2026-09-03 — the measured cause is not the stated one, and the issue keeps both.** The issue is
measured by 22 rows left pending after a reseed, on the stated reasoning that they are rules failing to
recognise their own outcome. Step 1 measured that none of the 22 is covered by a rule at all. Rather
than split the finding off, the issue absorbs it (developer): "we only file them as separate issues if
they don't fit in the 374 issue itself… everything else appears to fit the issue and is simply the
result of our analysis and the intent that the reseed should not have anything pending if the initial
reseeding had none."

Which piece delivers which guarantee, since they are not the same work:

| Steps | Delivers |
|---|---|
| 3–5 | A rule can recognise its own result — the issue's title |
| 6–7 | **Zero pending on every reseed** — each quote agrees with its own Source, so nothing is left to decide |
| 8 | Data quality: without it step 6 leaves a spurious Source row per wrong date. Not required for zero-pending |
| 9–10 | The two things the analysis found that nothing reports today |

**Filed separately, and first: [#375](https://github.com/DutchJaFO/Quotinator/issues/375)**, a season
analog for multi-season TV alongside the existing Universe → Series → Source hierarchy.

**This plan twice argued it was not a prerequisite. Overruled (developer, 2026-09-03):** "we already
have identified several quotes that should be linked to a specific season of a tv-series, therefore we
need to add that issue first." The argument was that step 6's constraint is table-wide, so the four `tv`
titles separate into two Source rows each and nothing is left pending — technically true, and it reaches
the wrong end state. Those rows are not two shows; they are one show whose quotes belong to different
seasons. Step 8 would then have "corrected" the second year away as a wrong date, deleting the very
information those quotes carry. Zero-pending would have been reached by discarding the signal, which is
not what the intent asks for.

**Not adopted: `existingRecord` as a staleness input.** It stops being read (step 4), which returns
`schemas/conflict-resolution-rules.schema.json`'s existing claim about it to being true. `incomingRecord`
keeps its role and gains a second one; step 11 corrects the text for both.

---

## Cross-check against authoritative sources, 2026-09-03

Per `docs/workflow/process.md`'s Planning step 3.

1. **ADR 016 applies — the outcome is an enum in its own file.** A second bool, or a nullable one, is
   not the shape this project uses. It goes under `src/Quotinator.Data/Enums/`, one type per file,
   alongside the eleven outcome enums already there (`BackupOutcome`, `SourceRefreshOutcome`,
   `ChangelogImportOutcome`, …).

2. **ADR 008 does *not* apply, and that is worth stating rather than assuming.** The new enum is a
   return value, not a persisted column: an already-applied rule resolves its action, which lands as
   `Decided` or — since #373 — `Unchanged`, and a stale one keeps staging the existing
   `ImportActionStatus.Stale`. No new member, so no CHECK constraint and no drift-test change on that
   account. Steps 6 and 7 do change schema, but neither touches a CHECK.

3. **ADR 002 settles how identity is expressed, and corrects this plan's first draft.** That draft
   argued steps 6–7 force a wipe-and-reseed, reasoning that a Source's identity *is* its derived id.
   Overruled (developer): "you assume the Id is the only foreign key possible and therefore try to add
   all unique identifiers into it. We can have multi-column keys." ADR 002 already prescribes the shape
   — a surrogate `Guid Id` with "the natural uniqueness constraint … enforced with a `UNIQUE` constraint
   alongside the surrogate key" — and `Quotinator_Source` already carries `UNIQUE (Title, Type)`. Since
   `ResolveSourceAsync` matches on `Sql.Sources.SelectExistingByTitleAndType`, existing rows are found
   by natural key and keep their ids. There is no id rewrite and no forced reseed.

4. **ADR 011 bounds where a season belongs.** Universe → Series → Source, one-to-many at both levels,
   with Simplicity ranked above Extensibility. A season concept is a fourth level or a Source attribute;
   either is a design decision that ADR does not make, which is why it is filed separately rather than
   improvised here — see #375.

5. **The schema and the model claim the recorded snapshots are never read; that is half true and this
   issue decides which half.** `schemas/conflict-resolution-rules.schema.json` and
   `ConflictResolutionRule`'s own XML docs say it of `existingRecord` *and* `incomingRecord`. After step
   4 it is accurate for the first and wrong for the second. Corrected at step 11 — a documentation
   defect inherited from #153, not a scope change.

6. **`source-alias-rules.schema.json` has no date field**, so an alias can correct a wrong title but not
   a wrong date, and cannot target one of two same-titled Sources. Step 8 adds it.

7. **The rule file's own shape needs nothing new.** Every outcome is derivable from what a rule already
   records — no schema field, no generator change, no re-authoring of the four bundled rule files.
   `ConflictRuleGenerator` records only the conflicted fields, which is safe here: the field a rule
   governs is present in its own snapshot by construction.

8. **Comparison stays case-insensitive, via the one helper.** `ConflictRuleLookup` keys
   `OrdinalIgnoreCase`, and every value comparison goes through `FieldMergeResolver.ValuesEqual` —
   case-insensitive for scalars, element-wise for lists, per CLAUDE.md's "GUID/enum/id/Name/Title
   comparisons are case-insensitive by default". The new comparisons use `ValuesEqual` too, never
   `Equals`.

9. **`data/sources/*.json` cannot carry curated intent.** They are regenerated from upstream by their
   converter plugins on `sources/refresh`, so anything hand-authored there is wiped. Every correction in
   step 8 goes in the hand-authored per-source overlay.

10. **`docs/vocabulary.md` carries no entry for these outcomes.** They are new project vocabulary and go
    in that file in the same commit, per CLAUDE.md.

11. **The issue's test names are renamed to the file's own convention.** All thirteen existing
    `ConflictRuleLookupTests` are `TryResolve_<condition>_<expectation>`; the issue's names are not. The
    mapping is recorded at step 3 rather than left to drift silently.

---

## Steps

### 1. Establish why the 22 are `Pending` and not `Stale`

**Status:** ✅ Done, 2026-09-03

The issue asks for this before anything changes: the 22 stage `Pending`, but a stale rule stages
`Stale`, so `TryResolve` must be returning `false` for them. Measured by running the issue's own
three-call reproduction against `NikhilNamal17WithRuleFileBatch` and dumping every non-terminal row, the
fields that actually differ, and whether a rule covers each `(entityId, field)` pair. The diagnostic was
a temporary test method, removed once its output was recorded.

**Not one of the 22 is covered by a rule.** The outcome defect is real on its own reasoning, but it is
not what strands these rows, and fixing it alone would not move the count.

**What strands them: the file disagrees with itself about a Source's date, and a quote does not own that
field.** `Sql.Quotes.SelectRawById` builds the existing side's field map from `s.Title AS Source,
s.Date` — the **shared Source row**. So when two entries in one file claim different dates for the same
Source, the first to arrive creates the Source row and every later entry disagreeing with it conflicts
on every re-import, forever.

| Fields that differ | Rows |
|---|---|
| `date` only | 19 |
| `source` + `date` | 3 |

```
"Back to the future" / 1958   ← entry 70f14cdd creates the Source row
"Back to the Future" / 1985   ← entry 9add7984 disagrees, and is Pending on every reseed
```

```
"Wolf of the Wall Street" / 2014   ← aliased onto "The Wolf of Wall Street" (2013)
```

The alias corrects the title and leaves the date, which is how aliasing adds a disagreement of its own.

Scale, across all three bundled files: `NikhilNamal17_popular-movie-quotes.json` has 732 entries over
418 distinct sources, 21 of which are claimed with more than one date; `vilaboim_movie-quotes.json`
(99/86) and `quotinator-curated.json` (13/7) have none. The 21 are not one kind of disagreement:

| Sub-case | Example | What it needs |
|---|---|---|
| A wrong date | `"Back to the future" / 1958` against `1985` | correcting — a mistake |
| A distinct work sharing a title | `"The Lion King"` — 5 quotes dated 1994, 2 dated 2019 | a second Source row, which the current natural key cannot distinguish |
| A per-quote date on a multi-year work | `"Mr. Robot"` 2015/2017, all `tv` | a season, which has no home (`QuoteEntity` has no `Date` column) |

**A second finding, reported rather than folded in silently:** the cold start says none of this. A quote
reaching an already-existing Source is an `Add`, so nothing compares its date claim against the Source
already stored. The contradiction is swallowed on first import and only becomes visible on the next
reseed. Step 9 addresses it.

### 2. Measure what the new constraints would break, before writing either migration

**Status:** ✅ Done, 2026-09-03

| Check | Result |
|---|---|
| `UNIQUE (QuoteText, SourceId)` violations across all three bundled files | **1** — `"Hello. My name is Inigo Montoya…"` twice under one Source |
| `UNIQUE (Title, Type)` violations | 0, necessarily — the constraint already exists |
| Same-title-different-date sources, by type | **16 `movie`, 4 `tv`** (`Arrow`, `Game of Thrones`, `Mr. Robot`, `The Good Place`), plus one more created by aliasing |

Both numbers change the plan rather than confirming it. The single duplicate is why step 7 deduplicates
at all — a `CREATE UNIQUE INDEX` written blind against this corpus would have failed on it. The type
split is why step 8 treats `tv` separately.

### 3. Write every test first, and run them red

**Status:** ⬜ Not started

Per `docs/testing-policy.md`'s red-before-green rule, covering the schema work as much as the lookup: a
migration's drift tests and its data-hazard test are written before the migration is. Renames from the
issue's own names:

| Issue's name | Written as |
|---|---|
| `RuleThatChangesAValue_ReportsApplied` | `TryResolve_StoredValueDiffersFromTheRulesOutcome_ReportsApply` |
| `RuleWhoseOutcomeIsAlreadyPresent_ReportsAlreadyApplied_NotStale` | `TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied` |
| `RuleWhoseSourceNoLongerMatches_ReportsCannotApply` | `TryResolve_IncomingValueDiffersFromRecordedSnapshot_ReportsStale` |

Three existing tests assert the behaviour step 4 reverses and are **rewritten, not deleted** —
`TryResolve_CurrentExistingValueDiffersFromRecordedSnapshot_IsStale` becomes the already-applied and
apply cases, and `TryResolve_GovernedFieldMissingFromRecordedSnapshot_IsStale` splits: missing from
`incomingRecord` is still stale, missing from `existingRecord` no longer is. The remaining ten stay
untouched and are the regression guard that the incoming-side half of #153 survives.

### 4. Give `TryResolve` its full result

**Status:** ⬜ Not started

A new enum in `src/Quotinator.Data/Enums/`, replacing `out bool isStale`. The target side is judged
against **the value the rule wants**, never against `recordedExisting`, which is what reduces a
six-cell matrix to four outcomes:

| Outcome | When | What the caller does |
|---|---|---|
| `Stale` | the field is absent from `incomingRecord`, or `recordedIncoming` ≠ the current incoming value | hold for review, and signal that **the rule** needs re-authoring |
| `Retirable` | as `Stale`, but the incoming side has moved *to* agreement: the field would now resolve identically with the rule removed | hold the same way, propose the opposite remedy — delete the rule |
| `AlreadyApplied` | the incoming side still matches, and the stored value already equals the wanted value | nothing to do; resolve to what is stored |
| `Apply` | the incoming side still matches, and the stored value differs — including when it is missing | apply the rule: change it, or add it |

The wanted value comes from the rule's own resolution against the **current** sides, exactly as
`FieldMergeResolver.ResolveWithDecisions` computes it: `Custom` → `customValue`, `Keep` → the current
existing value, `Replace` → the current incoming value. Evaluation order matters — staleness first, on
the incoming side alone; then retirement; only then the wanted-value comparison. A moved incoming side
is stale even when the stored value happens to be right, because the rule was written against a file
that no longer exists.

**Why `incomingRecord` is recorded** (developer, 2026-09-03): "recording the expected incoming value is
what allows us to see if our rules have an effect and could be retired once incoming starts to match the
target value." That is the whole justification for keeping a snapshot now that `existingRecord` is no
longer read, and it is why `Retirable` is separate from `Stale`: the two carry opposite remedies.

Two traps in the retirement test, both covered by verification rows:

- **`AlreadyApplied` is not retirable.** It is still doing work on every import — the incoming file
  still carries the wrong value, and the rule is what stops it overwriting the corrected one.
- **"Incoming matches the target" is the test only for `Custom`.** The general form is *would this field
  resolve to the same value with the rule removed*: `Keep` and `Replace` are retirable when the two
  sides agree; `Custom` additionally needs the agreed value to equal `customValue`, since correcting a
  value both sides get wrong is exactly what a Custom rule is for.

Retirement is reported, never automatic — deleting a rule is a curator's decision about data.

**Naming:** `Apply` / `AlreadyApplied` / `Stale` / `Retirable`. `Apply` rather than the issue's
`Applied` because `TryResolve` has applied nothing at the point it answers. `Stale` rather than
`CannotApply` because the project already uses that word for this state (`ImportActionStatus.Stale`,
#153). `Retirable` describes the rule rather than the field, deliberately — it is the one member that is
advice about the rule file. One enum, not three plus a flag: `Retirable` and `Stale` are mutually
exclusive readings of one condition, and a caller ignoring the distinction still branches correctly.

### 5. Teach the planner what each outcome means

**Status:** ⬜ Not started

`ImportActionPlanner` consults `TryResolve` in four places (the Add-branch Custom correction, the Quote
Modify branch, and the two later entity branches), each branching on `isStale` alone. `Apply` keeps
today's path. `AlreadyApplied` must resolve its field to the stored value rather than falling through to
`Pending`, so the action reaches a terminal state with nothing left to decide. `Stale` and `Retirable`
both keep staging `ImportActionStatus.Stale` — they differ in the remedy reported, not in whether the
action waits.

### 6. Date joins a Source's natural key

**Status:** ⬜ Not started

`UNIQUE (Title, Type)` becomes `UNIQUE (Title, Type, Date)` on `Quotinator_Source`. **SQLite cannot drop
a table constraint**, so this is a table-rebuild migration — create-new, copy, drop, rename — and per
CLAUDE.md's schema-migration policy the baseline is updated to match in the same commit, with both
schema-drift tests extended.

**The constraint is table-wide, so it applies to every `Type`, not only `movie`.** The decision was
stated for movies, and a narrower form would have to be a conditional index rather than the table's own
key. That is why #375 lands first rather than after: with seasons modelled, a `tv` quote
carrying a season year resolves to its season, and the table-wide key is correct for what remains. Run
the other way round, the same constraint splits one show into a Source row per year — a shape step 8
would then be asked to undo by discarding the season year, which is the information the season work
exists to keep.

Two code changes travel with it:

- `EntityIdentity.SourceId` takes the date, so two same-titled Sources cannot collide on the primary
  key. This affects **new rows only** — existing rows are found by natural key and keep the ids they
  have (cross-check 3).
- The alias staleness check is the one place that re-derives a live row's id and looks it up via
  `Sql.Sources.SelectExistingById`. It cannot re-derive a pre-change row's id, and that needs handling
  here rather than discovering later.

### 7. A quote is unique per Source

**Status:** ⬜ Not started

`UNIQUE (QuoteText, SourceId)` on `Quotinator_Quote`, which has no unique constraint of any kind today —
the only principal table without one. A quote's uniqueness currently rests entirely on
`QuoteIdentity.StableId` hashing the quote text against the source *title text*, which is why two
same-titled Sources cannot each hold the same line. The "must never change" warning on that algorithm
governs the surrogate id, not the uniqueness rule.

**No table rebuild is needed**: SQLite cannot add a table constraint by `ALTER`, but
`CREATE UNIQUE INDEX IF NOT EXISTS` enforces the same thing and is idempotent, which the migration
policy requires. The baseline gains the matching index.

Step 2 measured exactly one violation in the bundled corpus, so the migration deduplicates before
creating the index — and the test for that runs against a fixture carrying a duplicate, not against the
corpus, so it keeps failing if the corpus later changes.

A quote may exist under more than one Source and is unique within each; nothing here assumes a line
present in one is present in another (developer, 2026-09-03: "we should not assume that quotes were in
them all, unless we have proof").

### 8. Let an alias correct a wrong date, and correct the ones we have

**Status:** ⬜ Not started

`SourceAliasRule` and `source-alias-rules.schema.json` gain a date on both sides, so an alias can target
one of two same-titled Sources and say which is canonical. Without this, step 6 leaves a spurious Source
row for every wrong date.

The corrections themselves are two jobs, and neither may be answered from recognition —
`docs/workflow/source-verification.md` governs both:

- **16 `movie` titles**, where a second date may be a typo (`"Back to the future" / 1958`) *or* a
  genuinely distinct work (`"The Lion King"`, animated 1994 and live-action 2019). Each is decided
  individually; deciding wrongly either merges two films or splits one. Both Lion King entries dated
  2019 carry lines plausible in either film, which is exactly the case that rule exists for.
- **4 `tv` titles** — `Arrow` 2015/2017, `Game of Thrones` 2011/2012, `Mr. Robot` 2015/2017,
  `The Good Place` 2018/2019 — which are **not** date corrections. A second year on a multi-season show
  is a season, and those quotes are linked to one by #375. Correcting
  the year away here would delete that link; nothing in this step touches them.

### 9. Report a file that contradicts itself, at cold start

**Status:** ⬜ Not started

A quote reaching an already-existing Source is an `Add`, and nothing compares its date claim against the
Source already stored — so the contradiction is invisible on first import and surfaces only on the next
reseed. After step 6 the same disagreement instead creates a second Source silently, which is no more
legible. Report it where it happens.

### 10. Surface retirable rules

**Status:** ⬜ Not started

Endorsed as a feature (developer, 2026-09-03): "helps improve the rules as incoming data is updated." It
is advice about a file, not about a row, so an action's own status is the wrong carrier: the candidates
are the seed log, the import report, or a notification of its own — the last being this milestone's own
subject and the only one a curator sees without going looking.

### 11. Correct the two documents that say the snapshots are never read

**Status:** ⬜ Not started

`schemas/conflict-resolution-rules.schema.json` and `ConflictResolutionRule`'s own XML docs, both of
which say it of `existingRecord` and `incomingRecord` alike. After step 4 it is true of the first and
false of the second; say so, and say what `incomingRecord` is actually for — the sole input deciding
whether the rule still applies, and the only thing that can tell a curator a rule has become redundant.
Add the outcome vocabulary to `docs/vocabulary.md` in the same commit.

### 12. Re-measure the reproduction, and unblock #373

**Status:** ⬜ Not started

Re-run the three calls: cold start, reseed, reseed — `0 / 0 / 0`, which steps 6 and 7 are what make
reachable. Then #373's two T2 documents (`21-reseed-preserves-existing-data.md`,
`11-clean-reseed-confirmation.md`) and its step 9.

### 13. Boyscout: explicit types, and the `.editorconfig` list

**Status:** ⬜ Not started

Per CLAUDE.md's "Variable declarations". Files are added to the scoped `IDE0008` list the moment each is
first touched, not at the end — at minimum `src/Quotinator.Data/Import/ConflictRuleLookup.cs`,
`src/Quotinator.Core/Database/ImportActionPlanner.cs`,
`src/Quotinator.Data/Import/ConflictResolutionRule.cs`, `src/Quotinator.Core/Import/EntityIdentity.cs`,
`src/Quotinator.Core/Database/QuotinatorMigrations.cs`, and the matching test files.
`ImportActionPlanner.cs` is 2,041 lines of near-uniformly `var`-declared locals; converting it is the
largest single piece of work here and is worth knowing before it is discovered mid-step.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Why the 22 rows are `Pending` and not `Stale` is established, with the field names, before anything changes | Live | step 1: 0 of 22 covered by a rule; 19 differ on `date`, 3 on `source`+`date`; 21 sources claimed with more than one date |
| 2 | ✅ | What the two new constraints would break is measured before either migration is written | Live | step 2: one `(QuoteText, SourceId)` duplicate in the bundled corpus; 16 `movie` / 4 `tv` same-title-different-date sources |
| 3 | ❌ | A stored value that already equals the rule's wanted value reports already-applied | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied` |
| 4 | ❌ | A stored value that differs from the wanted value reports apply | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueDiffersFromTheRulesOutcome_ReportsApply` — the control; without it, a lookup answering already-applied unconditionally passes row 3 |
| 5 | ❌ | A missing stored value reports apply, not already-applied | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueIsMissing_ReportsApply` — "or add it if it was missing" |
| 6 | ❌ | A moved incoming side reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingValueDiffersFromRecordedSnapshot_ReportsStale` — #153's incoming-side half, unchanged |
| 7 | ❌ | A moved incoming side reports stale even when the stored value is already right | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingMovedButOutcomeAlreadyStored_ReportsStale` — an already-correct target must not mask a rule that needs re-authoring |
| 8 | ❌ | A field absent from `incomingRecord` reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` |
| 9 | ❌ | A field absent from `existingRecord` no longer reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale` — half of the split, and the visible record of the reversal |
| 10 | ❌ | A stored value drifted from `recordedExisting` is no longer stale | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale` — the rewritten #153 test; this is the reversal itself |
| 11 | ❌ | `Keep` and `Replace` are judged against their own wanted values, not only `Custom` | Unit test | `ConflictRuleLookupTests.TryResolve_KeepAndReplaceOutcomes_AreJudgedAgainstTheirOwnWantedValue` — including `Keep` always already-applied once a row exists |
| 12 | ❌ | A rule whose incoming side has moved to agreement reports retirable, not stale | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingMovedIntoAgreement_ReportsRetirable` |
| 13 | ❌ | A `Custom` rule is retirable only when the agreed value is the custom value | Unit test | `ConflictRuleLookupTests.TryResolve_CustomRuleWhereBothSidesAgreeButNotWithCustomValue_IsNotRetirable` — both sides agreeing on a *wrong* value is what a Custom rule exists to correct |
| 14 | ❌ | An already-applied rule is never reported retirable | Unit test | `ConflictRuleLookupTests.TryResolve_AlreadyAppliedRule_IsNotRetirable` — it is still doing work on every import |
| 15 | ❌ | The already-applied comparison is case-insensitive and compares lists element-wise | Unit test | `ConflictRuleLookupTests.TryResolve_OutcomeDiffersOnlyByCase_ReportsAlreadyApplied` and its list sibling — proves `ValuesEqual`, not `Equals` |
| 16 | ❌ | An already-applied rule leaves its action needing no decision | Unit test | `ImportActionPlannerTests.AlreadyAppliedRule_LeavesNothingPending` |
| 17 | ❌ | A stale rule still stages `Stale`, and a conflict with no rule still stages `Pending` | Unit test | `ImportActionPlannerTests.StaleRule_StillStagesStale` and `..._ConflictWithNoRule_StillStagesPending` — the controls for row 16 |
| 18 | ❌ | Two works sharing a title and differing in date become two Source rows | Unit test | `ImportActionPlannerTests.SameTitleDifferentDate_ResolvesToTwoSources` — the Lion King shape, which today collapses to one |
| 19 | ❌ | A `tv` quote carrying a season year resolves to its season, not to a second Source row for the show | Unit test | `ImportActionPlannerTests.TvQuoteWithASeasonYear_ResolvesToItsSeason` — the four measured titles are one show each; a second Source row for the same show is the failure this row catches |
| 20 | ❌ | Two Sources differing only in date get distinct ids | Unit test | `EntityIdentityTests.SourceId_SameTitleAndTypeDifferentDate_DiffersById` — without it the natural key admits the row and the primary key rejects it |
| 21 | ❌ | An existing Source is still matched by natural key and keeps its id | Unit test | `ImportActionPlannerTests.ExistingSource_IsMatchedByNaturalKey_AndKeepsItsId` — the control that this needs no id rewrite; a failure here is the wipe-and-reseed the design exists to avoid |
| 22 | ❌ | The alias staleness check still works against a Source created before the change | Unit test | `ImportActionPlannerTests.AliasAgainstPreChangeSource_IsNotFalselyStale` — the one path that re-derives a live row's id |
| 23 | ❌ | The migration and the baseline produce an identical `Quotinator_Source` schema | Unit test | the structural drift test, extended — a table rebuild is where a column silently changes shape |
| 24 | ❌ | The migration and the baseline produce an identical `Quotinator_Quote` schema, index included | Unit test | the same drift test; an index created by migration and missing from the baseline is exactly the drift it exists to catch |
| 25 | ❌ | The same quote text under two different Sources is two rows | Unit test | `SqliteQuoteServiceTests.SameQuoteTextUnderTwoSources_IsTwoRows` — unique within each Source, with neither assumed to hold the other's line |
| 26 | ❌ | The same quote text twice under one Source is rejected | Unit test | `SqliteQuoteServiceTests.SameQuoteTextUnderOneSource_IsRejected` — the control; without it row 25 passes with no constraint at all |
| 27 | ❌ | The migration deduplicates before creating the index | Unit test | a fixture database carrying a duplicate, migrated — not the bundled corpus, so the test keeps failing if the corpus changes |
| 28 | ❌ | An alias can target one of two same-titled Sources by date | Unit test | `SourceAliasLookupTests.AliasWithDate_TargetsTheMatchingSourceOnly` |
| 29 | ❌ | An alias file without dates still loads and applies | Unit test | `SourceAliasLookupTests.AliasWithoutDate_StillApplies` — the three shipped alias files predate the field |
| 30 | ❌ | Every date correction added in step 8 cites a verified source | Recorded rationale | one citation per corrected entry, per `docs/workflow/source-verification.md` |
| 31 | ❌ | A file that contradicts itself about a Source's date is reported at cold start | Unit test | `DatabaseInitializerTests.ColdStart_WithAFileThatContradictsItself_ReportsIt` — today it is silent |
| 32 | ❌ | A retirable rule is surfaced where a curator will see it | Unit test | assertion over whichever carrier step 10 settles on |
| 33 | ❌ | Reseeding repeatedly leaves nothing pending | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithAResolvableFile_LeavesNothingPending` — `0 / 0 / 0`, the issue's stated intent |
| 34 | ❌ | The cold-start half still passes unchanged | Unit test | `DatabaseInitializerTests.Seed_WithAResolvableFile_LeavesNothingPendingAndNoAlerts` (existing) |
| 35 | ❌ | The two existing real-rule-file tests still pass | Unit test | `InitialiseAsync_NikhilNamal17WithRealRuleFile_ProducesNoUnresolvedActions` and `..._GaladrielQuoteGetsCharacterOnAdd` |
| 36 | ❌ | The bundled counts still match after the schema change | Unit test | `InitialiseAsync_AllSourceFiles_SeedsExpectedCounts` — its Source and Quote totals move by a known amount or not at all, and either way the number is asserted rather than adjusted to whatever appears |
| 37 | ❌ | Every new test is red against the pre-fix build | Test run | run at step 3 against `HEAD` before any change |
| 38 | ❌ | The schema and the model say which snapshot is read and which is not | Unit test | assertion over the schema's own `existingRecord`/`incomingRecord` description text |
| 39 | ❌ | #373's two T2 documents pass | Automated (T2) | `21-reseed-preserves-existing-data.md` and `11-clean-reseed-confirmation.md`, both green end to end |
| 40 | ❌ | An existing database survives the upgrade with its data intact | Automated (T2) | a pre-change database migrated in place: row counts unchanged, no Source duplicated, no quote orphaned. The rebuild in step 6 is the riskiest thing here and no unit test covers a real file |
| 41 | ❌ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 42 | ❌ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 43 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | reseed twice against the bundled content; `/import-review` stays empty across both runs |

**Rows 4, 5 and 17 exist because "nothing is pending" is satisfied by resolving everything.** A lookup
answering already-applied unconditionally would pass rows 3, 16 and 33 perfectly, and would silently
honour rules against a source that had moved — the exact harm #153 added the staleness check to prevent.

**Rows 7 and 10 are the two halves of the reversal, asserted in both directions.** Row 10 proves the
existing-side check is gone; row 7 proves the incoming-side check did not go with it.

**Rows 13 and 14 are the controls on retirement advice.** Retirement tells a curator to delete a rule; a
suite that only proves retirable rules are spotted, without proving working ones are not, is advice
nobody should follow.

**Rows 21 and 40 are the ones that would hurt to discover late.** Row 21 is the whole argument that this
schema change needs no id rewrite; row 40 is the only check that the argument survives contact with a
real database rather than a freshly-created one.

**Row 26 is not redundant with row 25.** With no constraint at all, the same quote text under two
Sources is already two rows — row 25 passes today. Only row 26 fails today.
