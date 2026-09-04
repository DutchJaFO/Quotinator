# #374 — A conflict rule cannot tell "already correct" from "cannot apply"

**Status:** In progress (step 12)
**GitHub issue:** #374
**Tiers required:** T1, T2
**Depends on:** [#375](https://github.com/DutchJaFO/Quotinator/issues/375) (done in code, released separately)

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

**Three principles govern this issue and #375, and separating them is what keeps the scope honest**
(developer, 2026-09-03):

1. **Seeding has no pending results when done.**
2. **Seeding does not guarantee the data is 100% complete and accurate.**
3. **The rules help us enhance and improve incoming results for sources we don't control.**

The first two are orthogonal, and this plan's earlier drafts repeatedly conflated them. `Pending` means
*a human decision is required before this batch can be applied* — nothing more. It does not mean the
data is right, and clearing it does not make the data right. Accuracy is a separate axis with no
completion state, worked by principle 3's rules and by the data enhancement milestone. Reading them as
one guarantee is what produced two wrong conclusions here: that every wrong date had to be corrected
before this issue could close, and that a quote we cannot fully attribute is a defect rather than an
ordinary, expected state.

---

## Scope changes

**2026-09-03 — the measured cause is not the stated one, and the issue keeps both.** The issue is
measured by 22 rows left pending after a reseed, on the stated reasoning that they are rules failing to
recognise their own outcome. Step 1 measured that none of the 22 is covered by a rule at all. Rather
than split the finding off, the issue absorbs it (developer): "we only file them as separate issues if
they don't fit in the 374 issue itself… everything else appears to fit the issue and is simply the
result of our analysis and the intent that the reseed should not have anything pending if the initial
reseeding had none."

**2026-09-03 (later the same day) — re-measured after #375 landed, count moved 22 → 18.** #375's step 7
attached four of the originally-conflicting quotes (`Mr. Robot`/`Arrow` entries resolved to a named
episode) to their own episode-level Source, which removes them from the show-level Source's date
disagreement. Re-running the issue's own three-call reproduction against current `HEAD` (temporary
diagnostic test, reverted after) gives `cold=0, reseed1=18, reseed2=36`, not `0/22/44`. The mechanism,
the two sub-cases in step 1's table, and every step from 3 onward are unaffected — this only revises the
specific row count step 1 and the verification checklist's row 1 cite. Two `tv` titles' remaining
un-resolved quotes are still part of the 18 (the four resolved ones came out; the four `tv` titles
themselves — `Arrow`, `Game of Thrones`, `Mr. Robot`, `The Good Place` — still have other quotes that
didn't resolve to an episode and are still counted, per step 8's own note that these are out of its
scope).

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
   case-insensitive for scalars, per CLAUDE.md's "GUID/enum/id/Name/Title comparisons are
   case-insensitive by default". **Corrected 2026-09-03:** list comparison is no longer element-wise —
   commit `928240ce` (same-day, landed after this cross-check was first written) changed it to a
   `HashSet`-based, order- and duplicate-count-insensitive comparison (`genres` unordered). The new
   outcome comparisons inherit this automatically by calling `ValuesEqual`, never `Equals` — no
   independent decision needed here, just an accurate description of what the existing helper now does.

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

**Status:** ✅ Done, 2026-09-03 — `ConflictRuleLookupTests.cs` only (the migration's drift/data-hazard
tests for steps 6–7 are written when those steps start, not here)

Per `docs/testing-policy.md`'s "signature first" rule, the signature was created before the tests: a new
`ConflictRuleOutcome` enum (`src/Quotinator.Data/Enums/ConflictRuleOutcome.cs`, step 4's naming —
`Apply`/`AlreadyApplied`/`Stale`/`Retirable`), and `TryResolve`'s `out bool isStale` replaced with
`out ConflictRuleOutcome outcome` — but with the method body left computing exactly what the old
`isStale` flag computed (`existingMatches`/`incomingMatches`, unchanged), translated into
`Stale`/`Apply` only. This is a mechanical signature change with no new behaviour, so it does not
itself need to go red — and it lets every one of the seven `ImportActionPlanner` call sites (step 5's
corrected count) compile against the new type today via a one-line local
(`outcome is Stale or Retirable`) without pre-empting step 5's real per-branch work. Confirmed
behaviour-neutral: all 1544 `Quotinator.Core.Tests` and 1327 of `Quotinator.Data.Tests`' 1337 pass
unchanged (the other 10 are the new red tests below).

Renames from the issue's own names:

| Issue's name | Written as |
|---|---|
| `RuleThatChangesAValue_ReportsApplied` | `TryResolve_StoredValueDiffersFromTheRulesOutcome_ReportsApply` |
| `RuleWhoseOutcomeIsAlreadyPresent_ReportsAlreadyApplied_NotStale` | `TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied` |
| `RuleWhoseSourceNoLongerMatches_ReportsCannotApply` | `TryResolve_IncomingValueDiffersFromRecordedSnapshot_ReportsStale` |

Three existing tests assert the behaviour step 4 reverses and are **rewritten, not deleted** —
`TryResolve_CurrentExistingValueDiffersFromRecordedSnapshot_IsStale` becomes
`TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale` (the reversal itself — row 10), and
`TryResolve_GovernedFieldMissingFromRecordedSnapshot_IsStale` splits into
`TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` (row 8, unchanged) and
`TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale` (row 9, the reversal). The remaining ten
stay untouched and are the regression guard that the incoming-side half of #153 survives.

**Found while writing these — two corrections to what this step originally planned:**

1. **A fourth existing test needed the same reversal, uncounted above.**
   `TryResolve_RecordedListValueDiffersFromCurrentSequence_IsStale` hinges on the same existing-side-only
   mismatch as the renamed scalar test, just for a `genres` list — it also reverses, and is renamed to
   `TryResolve_ListValueDriftedFromRecordedExisting_IsNotStale`. The plan's "three existing tests" was an
   undercount for the same reason step 5's "four places" was: nobody had grepped for every test built on
   the same shape, only the one the issue happened to name.
2. **Row 15's "list sibling" cannot be built as written.** `ConflictResolutionFieldRule.CustomValue` is
   `string?` in this project's schema (confirmed by reading `ConflictResolutionRule.cs`) — a `Custom`
   rule cannot hold a list value, so `AlreadyApplied` is unreachable for a list-valued field by
   construction (only `Apply`/`Stale`/`Retirable` are). Retested as
   `TryResolve_ListOutcomeAgreesOnlyByOrder_ReportsRetirable` instead: the "moved into agreement" check
   behind `Retirable` is the one other place list comparison genuinely matters, and it is reachable with
   a plain `Keep` rule. Extending `CustomValue` to hold list values is out of this issue's scope and not
   otherwise needed by anything in this plan.

Ten tests are red for the right reason — genuinely new or reversed behaviour the step-3 stub does not
yet implement — confirmed individually against the failure output, not just by the pass/fail count:
`TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale`,
`TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale`,
`TryResolve_ListValueDriftedFromRecordedExisting_IsNotStale`,
`TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied`,
`TryResolve_StoredValueIsMissing_ReportsApply`,
`TryResolve_KeepAndReplaceOutcomes_AreJudgedAgainstTheirOwnWantedValue`,
`TryResolve_IncomingMovedIntoAgreement_ReportsRetirable`, `TryResolve_AlreadyAppliedRule_IsNotRetirable`,
`TryResolve_OutcomeDiffersOnlyByCase_ReportsAlreadyApplied`, and
`TryResolve_ListOutcomeAgreesOnlyByOrder_ReportsRetirable`. The other fourteen — six unchanged plus eight
whose assertion is loose enough (`outcome is not (Stale or Retirable)`) to hold under both the stub and
the real step-4 logic — pass now and are expected to keep passing once step 4 lands.

### 4. Give `TryResolve` its full result

**Status:** ✅ Done, 2026-09-03 — all 24 `ConflictRuleLookupTests` pass, including the 10 that were red
after step 3; `Quotinator.Data.Tests` (1337) and `Quotinator.Core.Tests` (1544) both green throughout,
confirming the real algorithm is a strict refinement of step 3's translation, not a regression against
anything the mechanical version got right.

`RuleEntry` also dropped its now-unused `RecordedExisting` field (`ExistingRecord` is genuinely never
read by `TryResolve` any more, matching what `ConflictResolutionRule.ExistingRecord`'s own XML doc
already claimed before this step existed) — a `ConflictResolutionRule` still carries `ExistingRecord`
for a human reviewing the file, per that type's own documented purpose; only the internal per-field
index inside `ConflictRuleLookup` stopped needing to carry it forward.

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

**Status:** ✅ Done, 2026-09-03 — no further code change needed beyond what step 3 already did; see the
finding below.

**Corrected 2026-09-03 — this was undercounted.** The plan originally named four call sites; #375
added a fifth branch (`PlanSeasonsAsync`) after this text was first drafted, and a re-grep of the current
`ImportActionPlanner.cs` finds **seven**, not four. `ImportActionPlanner` consults `conflictRules.TryResolve`
in:

1. `PlanAsync` — the Add-branch Custom correction (a brand-new quote, both sides identical, only `Custom`
   decisions are meaningful).
2. `PlanAsync` — the Quote Modify branch (an existing quote against its incoming re-import).
3. `PlanSourcesAsync` — two separate branches within the same method: one resolving by `(Title, Type)`,
   one resolving by an explicit natural key. Both follow the identical `isStale`-branching shape and both
   need the same four-way switch.
4. `PlanUniverseAsync` — one branch.
5. `PlanSeriesAsync` — one branch.
6. `PlanSeasonsAsync` — one branch (new since #375; not in this plan's original draft).

Every one of the seven follows the same shape: `if (!conflictRules.TryResolve(...)) continue; if
(isStale) hasStaleRule = true; else ruleDecisions[field] = decision;` — so the fix is the same edit
repeated seven times, not seven different designs. `Apply` keeps today's path. `AlreadyApplied` must
resolve its field to the stored value rather than falling through to `Pending`, so the action reaches a
terminal state with nothing left to decide. `Stale` and `Retirable` both keep staging
`ImportActionStatus.Stale` — they differ in the remedy reported, not in whether the action waits.
`Retirable` additionally needs its own reporting path per step 10, wherever the caller currently only
sets `hasStaleRule = true` with no distinction.

**Finding: the seven call sites needed no per-branch redesign.** Step 3's mechanical translation
(`bool isStale = outcome is ConflictRuleOutcome.Stale or ConflictRuleOutcome.Retirable;`) already
produces the exact behaviour this step describes, because the planner's own branching is binary
(hold-for-review vs. apply-the-decision) and the four outcomes collapse onto that binary exactly the
way the table above specifies: `Stale`/`Retirable` both set `hasStaleRule = true` (both hold, per the
table); `Apply`/`AlreadyApplied` both fall to `ruleDecisions[field] = decision`, and
`FieldMergeResolver.ResolveWithDecisions` applying an `AlreadyApplied` decision resolves the field to
the value it already holds — a no-op in effect, but a terminal one, so nothing falls through to
`Pending`. Verified directly, not just reasoned about: `ImportActionPlannerTests.AlreadyAppliedRule_LeavesNothingPending`,
`.StaleRule_StillStagesStale`, and `.ConflictWithNoRule_StillStagesPending` (verification rows 16–17) all
pass against the unmodified step-3/4 code. What step 5 actually still owes is **reporting** — a curator
currently has no way to tell a `Stale` field from a `Retirable` one on the resulting action, since both
collapse to the same `hasStaleRule` bit and the same `ImportActionStatus.Stale` — but that is exactly
step 10's job, not a gap in this step.

### 6. Date joins a Source's natural key

**Status:** ✅ Done, 2026-09-03

`UNIQUE (Title, Type)` became `UNIQUE (Title, Type, Date)` on `Quotinator_Source` via Migration008 (a
create-new/copy/drop/rename rebuild — SQLite cannot widen a table constraint via `ALTER`), baseline and
both schema-drift tests updated in the same commit. Table-wide, per `Type`-not-just-`movie`, as planned.

**Went beyond a schema change alone — the resolution logic that decides which Source row a quote's date
belongs to needed a full rewrite, which the plan's original text did not anticipate in this much
detail:**

- `EntityIdentity.SourceId` gained a `(title, type, date)` overload that defers to the two-argument one
  when `date` is `null`, producing an *identical* hash — every existing Source keeps the id it has.
  `ResolveSourceAsync` (`ImportActionPlanner.cs`) uses the 2-arg form for a title's first-ever variant
  and the 3-arg form only for a second-or-later variant, so a brand-new, single-date title (the
  overwhelming majority) is completely unaffected.
- `ResolveSourceAsync` was rewritten around a new `SourceVariant` record and `PickSourceVariant` picking
  in priority order: an exact date match (including two date-less variants matching each other); if the
  incoming side states a date and none matches, a date-less variant to backfill (the pre-#374 behaviour,
  preserved); otherwise, if the incoming side has no date, the nearest existing variant rather than a
  needless duplicate. Two batch-local structures track this per `PlanAsync` call:
  `sourceVariantsByKey` (every dated variant seen so far, from the database or this batch's own new
  Adds) and `stagedSourceVariantIds` (which variant ids already had an action staged this run, so a
  repeat quote for an already-settled variant stages nothing further — the direct extension of what the
  old single-value `sourceIndex` cache did for the single-variant case).
- **Found while implementing, not anticipated by this step's original text:** `sourceIndex` (the
  pre-existing single-value cache, shared with `PlanSourcesAsync`'s own `sources[]` resolution) had to
  stay in play *alongside* the new per-variant tracking — a `sources[]`-declared Source is staged into
  `sourceIndex` before quotes are ever resolved, and a quote referencing that same title needs to adopt
  it as a known variant rather than querying a database that doesn't have it yet (it's still only
  staged). `ResolveSourceAsync` checks `sourceIndex` as a fallback exactly for this cross-feature case.
- The alias staleness check (re-deriving a live row's id via `EntityIdentity.SourceId` and looking it up)
  still uses the 2-arg form unchanged — aliases are keyed on `(title, type)` alone, so this path never
  needed to become date-aware, and no handling was needed here after all (the plan's own anticipation of
  a problem here didn't materialise).

**A second, more serious gap found live, after step 6 was believed done: fragmentation of a real tv show
across two Source rows, exactly what the plan's own step 6 text argued sequencing #375 first would
prevent.** Verified directly against the bundled corpus: "Arrow" and "Mr. Robot" each split into two
Source rows (2015/2017), because their un-curated remaining quotes each carry a per-quote year that
disagrees with the show's other quotes — #375 already established these years are simply wrong, not
season markers, for exactly these titles, but nothing in step 6 read that finding; it treated any
differing date as a legitimate new variant, movie or tv alike.

**Design principle (developer decision, 2026-09-04), stated in the developer's own words:** "every time
we don't know what to do with the data that means we have a conflict to report." Four consequences
follow, and the fix implements all four:

1. **Scope is narrow, not universal.** "This is only an issue for sources that can have series and
   where we don't have series data" — `Movie` is unaffected; a second date there is legitimately either
   a distinct work or an updated release date, and the existing per-variant logic already handles both
   without ambiguity (`MovieQuoteWithASecondYear_StillResolvesToTwoSources` is the control proving this).
2. **The mechanism is a reported conflict, not a silent choice either way.** A series-capable type
   (currently `Tv` only — `EntityIdentity`/`ResolveSourceAsync`'s new `IsSeriesCapable` helper; no bundled
   evidence yet that `Anime` needs the same treatment) with **no Series data on any existing variant**
   attaches the quote to the nearest existing Source (the #375 nearest-Source principle) *and* stages the
   quote's own action as `Pending` instead of `Decided` — a new `SourceResolution(SourceId,
   DateNeedsReview)` record threads this out of `ResolveSourceAsync`, since a plain `string` return could
   no longer say both things at once.
3. **Once Series data exists, the ambiguity resolves itself.** The check is
   `variants.TrueForAll(v => v.SeriesId is null)` — a variant already linked to a Series exits this path
   entirely and falls through to the ordinary per-date-variant logic. `Quotinator_Series` has no
   start/end date columns today, so the "place a season within the show's known range" half of the
   developer's design is not yet buildable — recorded as follow-up work, not attempted here. (The
   developer separately supplied Wikipedia/IMDb references for Arrow's real run (2012–2020, 8 seasons)
   as the kind of source that would seed such a Series entry once that schema work lands.)
4. **The resolution options are a curator's, not the importer's, to take** — "once a conflict is
   reported we can decide our action (add it as an unknown new season or accept that it will be added to
   the series itself)." Nothing here builds that decision UI; it stages the conflict correctly and stops.

**Two further defects surfaced only by testing this against the real corpus, both fixed in the same
pass — this is why "found while implementing" recurs so often in this plan:**

- **Accumulation, the exact bug this whole issue exists to fix, recurring for a new mechanism.** A
  `Pending` action is never applied, so a quote reported this way has no row in `Quotinator_Quote` and
  looked "never-before-seen" on every subsequent reseed — measured growing `3 → 6 → 9` across two
  reseeds before the fix. `Sql.Quotes.SelectHasPendingActionById` (checked before staging) recognises an
  already-reported conflict and stages nothing further for it; `Reseed_Repeatedly_WithAResolvableFile_PendingCountNeverGrows`
  (renamed from `_LeavesNothingPending` — the correct count is 3, permanently, not 0) is the regression
  guard.
- **`ImportActionResolutionCoordinator.TryApplyBatchAsync` held the entire batch on this new `Pending`
  case too, not just `Blocked`.** Every pre-existing `Pending` in the codebase pairs with `Modify`
  (protecting an existing row) — this is the first `Pending` `Add`, and without extending the same
  isolated-hold exception already built for `Blocked` `Add` (step 7 below), three tv quotes held an
  entire 1,200+-quote reseed to nothing applied at all (measured: `QuoteCount` dropped from 798 to 112).
  Fixed by widening that exception to `(Blocked or Pending) and ActionType is not Add`.

### 7. A quote is unique per Source

**Status:** ✅ Done, 2026-09-03 — with a materially larger consequence than the plan anticipated; see
below and steps 6/7's shared verification rows.

`CREATE UNIQUE INDEX IF NOT EXISTS UX_Quotinator_Quote_QuoteText_SourceId` via Migration009, no table
rebuild (as planned). The migration's own dedup step deliberately avoids `MIN(...)`+grouping — that
shape trips `SqlAggregateGuard`'s CVE-2025-6965 heuristic (a whole-file text scan, so even the words
describing the avoided pattern in a comment tripped it) even though a single aggregate term against a
two-column grouping is safe; an `ORDER BY`/`LIMIT 1` correlated subquery expresses the identical "keep
the earliest row in this set" logic without the flagged shape or its vocabulary.

**Found while implementing — three things this step's plan text did not anticipate:**

1. **Every `Quotinator_Quote` insert is `INSERT OR IGNORE`**, for the pre-existing purpose of making a
   same-id re-import idempotent. Adding the unique index meant `OR IGNORE` now *also* silently swallowed
   a genuinely different quote id colliding on content alone — the row was never created, and the next
   step to look it up by id crashed with "sequence contains no elements". Confirmed this is not only a
   test-fixture artifact: the real bundled corpus's own known duplicate crashed
   `InitialiseAsync_AllSourceFiles_SeedsExpectedCounts` (all three files) the same way.
2. **Developer decision, 2026-09-03: surface a genuine (QuoteText, SourceId) collision under a different
   id as a review conflict**, not a silent merge. `ImportActionPlanner`'s Add-branch now checks both this
   batch's own staged Adds (`stagedQuoteTextsBySource`, for two brand-new ids colliding before either
   reaches the database) and a new `Sql.Quotes.SelectExistingIdByTextAndSource` query (case-insensitive,
   deliberately a shade broader than the index's own case-sensitive matching — a false-positive review is
   harmless, a missed real one is not) before staging a plain Add; a collision stages `Blocked` with the
   conflicting id recorded in `ExistingValue` instead.
3. **`ImportActionResolutionCoordinator.TryApplyBatchAsync` held the *entire batch* on any single
   Blocked/Pending/Stale action — pre-existing code, not new.** A single colliding quote anywhere in a
   1,200+-quote file meant the whole cold-start import wrote nothing at all. **Developer decision,
   2026-09-03: narrow this, not accept it.** Every other `Blocked` action in the codebase pairs with
   `ActionType.Modify` (protecting an *existing* row from a silent change — a property of that row,
   reasonably holding the batch); a `Blocked` `Add` protects nothing that exists yet, so
   `TryApplyBatchAsync` now excludes `Blocked`+`Add` from the batch-gating set — a domain-agnostic rule
   (ADR 004: `Quotinator.Data` has no Quote-specific knowledge here), not a Quote-specific carve-out.
   `Pending` and `Stale`, and `Blocked`+`Modify`, still gate the whole batch exactly as before.
4. **A genuine, previously-invisible duplicate was found and fixed at the data layer**:
   `quotinator-curated.json`'s "Inigo Montoya" entry carried a fresh hand-invented id instead of the id
   `NikhilNamal17_popular-movie-quotes.json`'s own identical-text entry already computes — a violation of
   CLAUDE.md's import-file-minimalism policy (a correction entry must reuse the id it corrects). Fixed by
   pointing the curated entry's `id` (and its conversation-line reference) at the real id, turning it into
   a proper correction. This is a real, permanent fix, not a test-only accommodation — see the updated
   `InitialiseAsync_AllSourceFiles_SeedsExpectedCounts`/`TracksCrossFileDuplicates` counts.
5. **A second such duplicate was found and left as a documented exception, not fixed** — spawned as its
   own follow-up task rather than fixed here. NikhilNamal17's own raw data carries "Hope is a good
   thing..." twice, once under "Shawshank Redemption" and once under "The Shawshank Redemption"; the
   project's own real alias file already merges those titles, so the two collide once resolved. Unlike
   Inigo Montoya, neither side of this duplicate is the hand-authored curated file — both live in
   `NikhilNamal17_popular-movie-quotes.json`, which is converter-generated and must never be hand-edited
   (CLAUDE.md). No existing mechanism resolves a cross-id content collision in a generated file (the
   existing `ConflictResolutionRule` mechanism corrects one field on one already-matched id, not this).
   `InitialiseAsync_NikhilNamal17WithRealRuleFile_ProducesNoUnresolvedActions` carries a one-id documented
   exception for this until the follow-up task lands.

A quote may exist under more than one Source and is unique within each; nothing here assumes a line
present in one is present in another (developer, 2026-09-03: "we should not assume that quotes were in
them all, unless we have proof").

**A sixth defect, found only by live T2 verification against real (not fixture) data, after every unit
test above was already green:** `GET /import/actions` crashed with a 500
(`ArgumentNullException` on a null `ExistingValue`) the moment a real NikhilNamal17 reseed produced a
genuine tv-conflict Pending row. `SqliteImportActionService.ComputeAmbiguousFields` assumed every
`Pending` action is a Modify with two rows to diff (`ExistingValue`/`IncomingValue` both populated);
step 6's new Pending-`Add` case (a series-capable Source's own date conflict) has no existing row at
all, so `ExistingValue` is `null` by construction and the deserialize call threw before the endpoint
could return anything. No unit test caught this because every existing `ComputeAmbiguousFields` test
exercised a Modify-shaped Pending action — none exercised the new Add-shaped one this issue introduces.
Fixed by returning `[]` early when `ExistingValue is null` (an Add has no per-field ambiguity to
compute; the reviewer decides where the whole new row belongs, not which field differs), with
`SqliteImportActionServiceTests.GetPagedAsync_PendingAdd_AmbiguousFieldsIsEmpty` as the regression
guard. Re-verified live afterward: the exact three tv-conflict ids and the one documented Shawshank
`Blocked` id all render correctly through the real endpoint with no crash.

### 8. Let an alias correct a wrong date, and correct the ones we have

**Status:** ✅ Done, 2026-09-04 — the mechanism; the 16 individual title determinations are their own
ongoing curatorial work, per this step's own scoping below, not a precondition of it

`SourceAliasRule`/`source-alias-rules.schema.json` gain optional `date`/`canonicalDate` fields.
`SourceAliasLookup` keeps two dictionaries — dated and date-less — trying an exact `(title, type, date)`
match first and falling back to the date-less form, so the three alias files shipped before this field
existed keep matching every date of their raw title exactly as before. `ImportActionPlanner`'s alias
substitution now overrides the quote's own `Date` with `CanonicalDate` when the alias supplies one
(previously it only ever corrected `Source`/`Type`), and the staleness check uses the 3-arg
`EntityIdentity.SourceId` form when a `CanonicalDate` is present — a dated alias's canonical target is
by definition a second-or-later variant, matching `ResolveSourceAsync`'s own creation convention for
that case.

Without this, step 6 leaves a spurious Source row for every wrong date — that residue is what the
mechanism now lets a curator close, one alias at a time.

**Correcting the twenty is not a precondition for this issue, and this step does not enumerate them.**
Zero pending comes from steps 6 and 7 alone: with date in the key, each quote resolves to the Source
matching its own claim and agrees with it, whether or not that Source's date is right. An uncorrected
wrong date leaves a second Source row that is simply the nearest Source we can identify for the quotes
citing it — the same standing rule #375 adopted for quotes whose episode is unknown (developer,
2026-09-03: "we do not expect all quotes to be perfectly attributed… examples that need more research in
the data enhancement milestone"). The residue is enhancement material, not a blocker.

- **16 `movie` titles** are candidates, each needing an individual determination — a second date may be
  a typo (`"Back to the future" / 1958`) or a genuinely distinct work (`"The Lion King"`, animated 1994
  and live-action 2019, both real). Deciding wrongly merges two films or splits one, so each is decided
  under `source-verification.md` when it is decided, and left alone until then. Note both Lion King
  entries dated 2019 carry lines plausible in either film. Not attempted here — this is ongoing
  curatorial work under a separate, already-documented procedure, not a mechanism this issue builds.
- **4 `tv` titles** — `Arrow`, `Game of Thrones`, `Mr. Robot`, `The Good Place` — are **not** date
  corrections and are explicitly out of this step. #375 established that their years are simply wrong
  rather than season markers, and that the quotes' seasons come from episode lookups it has already
  done. Correcting a year here would achieve nothing and could contradict that work.

### 9. Report a file that contradicts itself, at cold start

**Status:** ✅ Done, 2026-09-04

Reporting-only — it changes nothing about which rows get created, only whether anyone is told.
`ResolveSourceAsync` already handled a self-contradicting file correctly (a second, differently-dated
quote gets its own Source variant rather than corrupting the first); this makes that outcome visible at
cold start instead of leaving it silent until a later reseed's own accumulation happened to surface it
(step 1's original finding).

`QuotinatorDatabaseInitializer.ReportSelfContradictingSources` derives the finding from the same
`actions` list already returned by `PlanAsync` for that file — no new field threaded through the
planner. It groups every staged Source `Add` by `(Title, Type)` case-insensitively and logs a Warning
(`LogSourceDateContradiction`, `[Database - Seed]`) for any group with more than one member. A log line
was chosen over a new notification type: this is advice a curator can act on by reading the log, not a
standing condition that needs its own dismissible UI element, and building a whole new
`NotificationMetadataKind` (schema, i18n in three languages, dismiss trigger) for a reporting-only
enhancement would be disproportionate to what it delivers.

Verified with a real self-contradicting fixture file through the actual cold-start path (not a
unit-level simulation of the planner alone): `DatabaseInitializerTests.ColdStart_WithAFileThatContradictsItself_ReportsIt`,
with `..._WithNoSelfContradiction_ReportsNothing` as the control. Confirmed red without the
`ReportSelfContradictingSources` call, green with it.

### 10. Surface retirable rules

**Status:** ✅ Done, 2026-09-04

Endorsed as a feature (developer, 2026-09-03): "helps improve the rules as incoming data is updated." It
is advice about a file, not about a row, so an action's own status is the wrong carrier — the row itself
still stages `Stale` for review either way (`Stale`/`Retirable` differ only in the remedy reported, per
step 4's own table). The underlying detection (`ConflictRuleOutcome.Retirable`) was already fully built
and tested at step 4; what this step adds is a channel to get it out of the planner at all.

`PlanAsync` gains an optional trailing `retirableRuleFindings` parameter (a `List<RetirableRuleFinding>`
sink) — optional and trailing specifically so none of this method's many existing call sites (100+,
overwhelmingly in tests) needed to change. Threaded through the four private per-entity-type helpers
(`PlanSourcesAsync`, `PlanUniverseAsync`, `PlanSeriesAsync`, `PlanSeasonsAsync`) the same way
`conflictRules` already was. At each of the six call sites that consult `ConflictRuleOutcome` (step 5's
own count), a `Retirable` outcome is appended to the sink alongside the existing `isStale` collapse — a
pure addition, no change to which status an action stages. `QuotinatorDatabaseInitializer` passes a
fresh list per file and logs each finding (`LogRetirableRule`, `[Database - Seed]`, Information level) —
the same log-based reporting choice as step 9, for the same proportionality reason.

**Found while proving this correctly with a real fixture, not assumed:** the Quote-Modify call site
(step 5's site 1) has its own "skip when already equal" optimisation before it ever calls
`ConflictRuleLookup.TryResolve` — and every `Retirable` case is, by definition, exactly the equal-values
case that optimisation skips. `Retirable` is therefore unreachable through that one call site in
practice; the other five (no such pre-check) are where it fires. The test built around this
(`ImportActionPlannerTests.PlanAsync_RetirableRule_IsCollectedIntoTheSink`) uses the Universe branch for
exactly this reason, with `..._StaleRule_IsNotCollectedAsRetirable` as the control. A second, initially
wrong assumption was caught by the test itself: a `Retirable` action still stages `ImportActionStatus.Stale`
(matching step 4's table precisely — it is not resolved like `Apply`/`AlreadyApplied`), not `Decided`,
which the test's first draft asserted incorrectly before being corrected against the actual result.
Confirmed red without the capture line, green with it.

### 11. Correct the two documents that say the snapshots are never read

**Status:** ✅ Done, 2026-09-03

`ConflictResolutionRule.IncomingRecord`'s XML doc and `conflict-resolution-rules.schema.json`'s matching
description both corrected to say what's actually true after step 4: `ExistingRecord` is genuinely never
read (unchanged, already accurate), `IncomingRecord` is read for staleness/retirement detection. Outcome
vocabulary (`ConflictRuleOutcome`) added to `docs/vocabulary.md` in the same commit.

### 12. Re-measure the reproduction, and unblock #373

**Status:** In progress. The reproduction re-measurement is done. Both of #373's own T2 documents have
been run live (2026-09-04), twice — once before and once after the sixth defect below — against a
freshly rebuilt image each time: doc 21 passes except one already-known, separately-scoped issue and one
stale-document assertion (both noted below); doc 11 initially found a real regression (reseed
confirmations duplicating instead of deduping), traced to a case-only content difference silently
resolving forever, fixed (`quoteText`/`character` case-sensitivity, corrected mid-flight to exclude
`source` after live evidence showed it was the wrong field — see the sixth defect below), and
re-verified live: the duplication the developer's own report described is gone, with one further,
distinct, pre-existing gap found and recorded (not fixed) rather than left silent. Five defects found by
this step's own earlier T2 pass, plus the developer-reported sixth (case-sensitivity) and everything it
led to, are all documented below with their regression guards. Full solution green throughout
(`dotnet test -m:1`, 0 failures, 0 warnings). **Not yet done:** #373's own step 9 needs the same live
confirmation the two documents above already got. #373's step 8 (its own remaining "unblock #372's step
6" work) is unaffected by this issue and stays #373's to do.

`DatabaseInitializerTests.Reseed_Repeatedly_WithAResolvableFile_PendingCountNeverGrows` (new; renamed
from `_LeavesNothingPending` per row 33's correction below — this paragraph was not updated when that
correction was made, and stated the old name and the old `0/0/0` assertion until fixed here) runs the
issue's own three calls — cold start, reseed, reseed — against `NikhilNamal17WithRuleFileBatch` and
asserts `3 / 3 / 3`: three genuine, permanent tv-season conflicts are correct by design (row 19), and
stability — not zero — is what this test actually proves. `StagedActionCountAsync` only counts
`Pending`, so the one already-documented `Blocked` exception (step 7's finding 5) doesn't affect this
count.

**Four more defects found only by this step's own live T2 pass, none caught by any unit test above
because none of the fixtures those tests use combine a full multi-file corpus with a Review-policy
reseed the way a real one does:**

1. **`ComputeAmbiguousFields` crashed on a Pending Add's null `ExistingValue`** — the same shape as step
   7's sixth defect, on a different code path. Fixed with an early `[]` return, mirroring that fix.
   `SqliteImportActionServiceTests.GetPagedAsync_PendingAdd_AmbiguousFieldsIsEmpty` is the regression
   guard (shared with step 7's own citation for the sixth defect).
2. **`BuildFields`/`ToSummaryAsync` crashed (`JsonException`) reading a `Blocked` collision's
   `ExistingValue`** — it holds a bare `{conflictingQuoteId}` marker for that status, not a full
   `QuoteActionPayloadDto`. Fixed by only calling `BuildFields` for non-Add action types.
   `SqliteImportActionServiceTests.GetPagedAsync_BlockedCollisionAgainstAnExistingQuote_DoesNotCrash` is
   the regression guard.
3. **`PlanSourcesAsync`'s natural-key branch crashed (`InvalidOperationException`) against a title with
   two or more dated variants** — `QuerySingleOrDefaultAsync` assumed at most one row; switched to the
   list-returning query plus the existing `PickSourceVariant` helper (step 6's own mechanism).
   `ImportActionPlannerTests.PlanSourcesAsync_NaturalKeyEntryAgainstTwoDatedVariants_DoesNotCrash` is the
   regression guard.
4. **A `Blocked` collision doubled on every reseed**, the exact accumulation class this issue exists to
   fix, recurring for a status step 6/7's own dedup check never covered:
   `Sql.Quotes.SelectHasPendingActionById` only guarded the `dateNeedsReview` path, leaving `Blocked`
   unguarded. Renamed to `SelectHasUnresolvedActionById`, widened to `Status IN ('Pending', 'Blocked')`,
   and the `dateNeedsReview &&` gate removed so every already-unresolved quote is recognised regardless
   of which mechanism reported it. `DatabaseInitializerTests.Reseed_Repeatedly_WithABlockedCollision_BlockedCountNeverGrows`
   is the regression guard.

**A fifth, reported by the developer directly from a real T1 run rather than found by this step's own
T2 pass, with a distinct root cause from any of the above:** a Review-policy file's confirmation read
"107 items came in, 106 added, 0 updated and 0 already stored" — the numbers do not add up.
`ConfirmFileAppliedCleanlyAsync`'s `Modified` count excluded every `Review`-policy row, on the mistaken
assumption that `AppliedPolicy` reflects what happened to that specific row; it actually always echoes
the *file's* own configured policy (every Modify branch in `ImportActionPlanner` stamps the same
batch-wide `policy` parameter). A `Review`-tagged Modify reaching this code path already resolved
cleanly — the exclusion should only ever have applied to `Skip`, which is the one policy guaranteed to
change nothing by construction. Fixed by narrowing the exclusion to `Skip` alone.

**Raised immediately afterward by the developer: narrowing the exclusion to `Skip` alone still hides
something — a `Skip`-policy row that genuinely differed from what is stored now counts toward none of
`Added`/`Modified`/`Unchanged`, only toward `Incoming`, which is the same class of defect in a smaller
blast radius.** "Knowing items were skipped is valuable information" — folding a real, deliberately
discarded difference into whichever neighbouring bucket happened to be convenient is exactly what this
step had just finished ruling out for `Review`. Fixed properly this time: `ReseedEntityCountDto` gains
its own `Skipped` bucket (`src/Quotinator.Data/Notifications/ReseedEntityCountDto.cs`), populated
alongside `Modified` in `ConfirmFileAppliedCleanlyAsync` so `Incoming` always equals
`Added + Modified + Unchanged + Skipped`. The fix reaches every layer that would otherwise still hide
it: the confirmation body template (`NotificationReseedFileApplied{Bundled,User}Body`, all three
locales) states the skipped count as its own number rather than folding it into "already stored", and
`NotificationTable.razor.cs`'s detail table gains a `Skipped` column and includes a skipped-only row in
its filter — before this, a row with nothing added or modified but something skipped would have been
silently dropped from the table even after the underlying payload started carrying the number
correctly. `DatabaseInitializerTests.Reseed_SkipPolicyFileWithADifferingRow_ReportsItAsSkippedRatherThanVanishing`
and `NotificationTableTests.SkippedOnlyRow_StillRenders` are the two regression guards, both confirmed
red before the fix and green after.

**A near-identical fix was drafted and reverted for `SqliteQuoteImportService.cs`'s `ImportSummary`,
correctly.** Its `Updated`/`Skipped` counts look like the same bug, but are not: that type's own XML
docs already scope `Updated` to `newest-wins`/`merge-ours`/`merge-theirs` only and `Skipped` to
`skip`/`review` together — a narrower, pre-existing, intentional contract, unlike
`ReseedEntityCountDto.Modified`'s generic "how many rows of this type the file modified". Applying the
same widening there broke `QuoteImportServiceTests.ImportAsync_Review_BehavesLikeSkip`, which is what
caught the false analogy before it shipped; the file was reverted in full rather than partially
patched.

**Two more cross-file duplicates found while investigating a live "we are missing rules" report,
the same underlying cause as step 7's finding 5 (Shawshank), fixed the same way:** vilaboim's own
"I'll have what she's having." and "Keep your friends close, but your enemies closer." entries are
byte-identical to two NikhilNamal17 entries once NikhilNamal17's own alias file corrects their titles
("When Harry Met Sally" → "When Harry Met Sally...", "The Godfather II" → "The Godfather Part II") to
match vilaboim's raw spelling exactly. `data/sources/vilaboim-quote-exclusions.json` (new, same
mechanism as step 7 finding 5's `nikhilnamal17-quote-exclusions.json`) excludes both ids. Verified live:
`Blocked` collisions dropped from 2 to 0 on cold start and stayed 0 across two further reseeds.

**Confirmed still open, and explicitly out of this issue's scope — a different bug class from every one
above:** `Pending` grows unbounded across reseeds (`3 → 4 → 5`, live-verified) for a genuine field-level
Source Modify conflict ("Silence of the Lambs", a title/date disagreement between the two bundled files
with no covering rule). The Modify-path accumulation fix below (dedup point 3) covers a Quote's own
Modify branch only, not `PlanSourcesAsync`'s; a Modify conflict's identity there is `(entity, field)`,
which nothing currently dedups against. Not fixed here — recorded so it is not lost, and left for its
own issue.

**A sixth defect, reported by the developer directly (not found by this step's own live T2 pass):
"if the quote, title or character are identical except for case then that needs to be reviewed as it
could be a correction or an unwanted update" — a case-only difference must not be silently resolved,
because only a human can tell a correction from an unwanted downgrade. Also: "an unstable result on the
second run is proof that the rules were incomplete or incorrect" — endorsing the diagnostic method that
led here (a value that changes shape between one reseed and the next, on genuinely unchanged input, is
itself evidence, not noise to explain away).** `FieldMergeResolver` gains a `caseSensitiveFields`
parameter (`Resolve`, `ResolveWithDecisions`, and a new `ValuesEqual(field, a, b, caseSensitiveFields)`
overload) — domain-agnostic per ADR 004, so the actual field names are supplied by the caller, never
hardcoded in `Quotinator.Data`. `Quotinator.Core`'s `QuoteFieldMerge.CaseSensitiveContentFields` is that
caller-supplied set, threaded through every comparison in the Quote-Modify branch of
`ImportActionPlanner.PlanAsync` (`contentIsIdentical`, `effectiveChanged`, the rule-consulting loop's
skip-check, and the `Resolve`/`ResolveWithDecisions` calls themselves).

**First attempt included `source` — the field named directly in the developer's own wording — and a live
T2 run against the real bundled corpus found this was wrong, not merely incomplete.** A quote's `source`
field is never independently persisted (`Sql.Quotes.SelectRawById` builds it from `s.Title AS Source`, a
join to the Source row the quote has already resolved to, matched case-insensitively as identity always
is in this project). Real upstream data routinely spells one film's title with different, harmless
casing across different quote lines — measured **14 such cases in the bundled NikhilNamal17 corpus
alone** (e.g. "The Dark Knight" / "the dark knight" / "The Dark knight", all the same film). Making
`source` case-sensitive turned every one into a permanent false "needs review" conflict with nothing
genuine to decide, and — far more seriously — a single such case-only in-file duplicate held an entire
cold-start seed to **zero rows written** (see dedup point below). `source` was removed from
`CaseSensitiveContentFields`; only `quoteText` and `character` remain, since both are genuinely stored
per quote and never derived from a join. See `QuoteFieldMerge.CaseSensitiveContentFields`'s own XML doc
for the full account, and `ImportActionPlannerTests.PlanAsync_QuoteCharacterDiffersOnlyByCase_StagesPendingForReview`
(with `..._ExactMatch_StaysUnchanged` and `..._MatchingConflictRuleStillResolves` as controls) for the
regression guards.

**Two further defects surfaced only by testing the first (too-broad) attempt against the real corpus and
a live Docker reseed, both fixed in the same pass:**

1. **A same-batch, in-file collision on a now-ambiguous field could hold an entire cold-start seed to
   zero rows written.** Two lines in one import file that hash to the same id and disagree only on a
   case-sensitive field go through the Quote-Modify branch against each other (`seenQuotes`, an
   in-memory same-batch stand-in for "existing"), correctly staging `Pending` — but
   `ImportActionResolutionCoordinator.TryApplyBatchAsync`'s existing gating rule (a Blocked/Pending
   *Modify* holds the whole batch, since every other one protects a genuinely stored row) doesn't know
   the difference between that and a real, previously-committed row. A same-batch collision protects
   nothing that already exists, exactly like the pre-existing Blocked/Pending-*Add* exception already
   established — widened to also exempt a Blocked/Pending Modify whose `ExistingBatchId` equals its own
   `BatchId` (the marker `QuoteSeedWriter`'s same-batch branch stamps, always different from a real prior
   row's `ExistingBatchId`). `ImportActionResolutionCoordinatorTests.TryApplyBatchAsync_PendingModifyFromSameBatch_DoesNotHoldTheRestOfTheBatch`
   is the regression guard, confirmed red before the fix.
2. **The accumulation-prevention check the Add branch already had was never extended to Modify** —
   before a case-only difference became its own permanent ambiguity, a Modify's own ambiguous fields
   either auto-resolved or (rarely) blocked a Complete row, never stayed genuinely `Pending` release over
   release the way an Add-branch conflict could. Once one could, nothing stopped a later reseed from
   comparing the same still-unresolved quote against its (unchanged) stored row and re-staging a fresh
   duplicate `Pending` action on top of the one already awaiting review — measured live: a single known
   conflict, left unresolved, grew the real corpus's staged count `4 → 19` in one reseed. Fixed with the
   same query the Add branch already uses (`Sql.Quotes.SelectHasUnresolvedActionById`), added to the top
   of the Modify branch, skipped only when `ExistingBatchId == BatchId` (a same-batch collision, which by
   definition can have nothing in `Import_Action` yet).
   `DatabaseInitializerTests.Reseed_Repeatedly_WithACaseOnlyPendingModify_PendingCountNeverGrows` is the
   regression guard.

**Re-verified live end to end after both corrections**, against a freshly rebuilt image: doc 11's own
step 4 (reseed twice, confirm no duplicate confirmation) and step 7 (four seeding variants) both showed
the exact duplication the developer's original report described — `4 → 5` confirmations on one file,
`8`/`2`/`10` instead of `4`/`1`/`5` across the variants — with the *first* corrected build (source still
excluded, dedup fixes in place). One further, distinct, pre-existing gap remains and was traced but not
fixed here: a Review-policy Modify whose full resolution is a complete no-op (`existingFields`,
`incomingFields` and `mergedFields` all identical — found on a `Quotinator_Source` row with zero actual
field differences) is still classified `Modify` and counted in `Modified`, the same "hides what happened"
shape as the `Skip`-only fix above but for `Review`'s own auto-resolve path — this is what still produces
one settling, non-repeating confirmation duplicate per fresh install (stable after the first reseed, not
growing further). Recorded here rather than fixed, since it is architecturally deeper — it would need a
"no-op Modify" classification threaded through every entity's own Modify branch, not just Quote's — and
is a plausible candidate for its own issue.

### 13. Boyscout: explicit types, and the `.editorconfig` list

**Status:** ✅ Done, 2026-09-03 — for every file this issue actually touched

Per CLAUDE.md's "Zero-warnings policy and boyscout rules". `ConflictRuleLookup.cs`,
`ImportActionResolutionCoordinator.cs`, `ConflictRuleLookupTests.cs` (new files/first-touched this
issue) converted fully and added to `.editorconfig`'s scoped `IDE0008` list — `dotnet format style` for
the mechanical majority, hand-fixed for the `out var` shapes it doesn't reach.
`ConflictResolutionRule.cs`/`ConflictRuleOutcome.cs`/`Sql.cs` added to the same list for completeness
(no pre-existing `var` to convert). `ImportActionPlanner.cs`, `QuotinatorMigrations.cs`,
`EntityIdentity.cs`, and the touched test files were already in the scoped list from #375's own work —
confirmed this issue introduced no new `var` in them (0 warnings throughout). The file-wide conversion
of `ImportActionPlanner.cs`'s ~2,000 remaining pre-existing `var` locals — never in this issue's
scope — is unaffected and remains for whichever future work actually rewrites that file wholesale.
Full solution: `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); every test project
green (`Quotinator.Core.Tests` 1560, `Quotinator.Data.Tests` 1337, full solution `dotnet test -m:1`
green end to end).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Why the rows are `Pending` and not `Stale` is established, with the field names, before anything changes | Live | step 1: 0 of 22 covered by a rule; 19 differ on `date`, 3 on `source`+`date`; 21 sources claimed with more than one date. Re-measured 2026-09-03 after #375 landed: 22 → 18 (4 moved to their own episode Source) — the mechanism is unchanged, only the count |
| 2 | ✅ | What the two new constraints would break is measured before either migration is written | Live | step 2: one `(QuoteText, SourceId)` duplicate in the bundled corpus; 16 `movie` / 4 `tv` same-title-different-date sources |
| 3 | ✅ | A stored value that already equals the rule's wanted value reports already-applied | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueAlreadyEqualsTheRulesOutcome_ReportsAlreadyApplied` |
| 4 | ✅ | A stored value that differs from the wanted value reports apply | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueDiffersFromTheRulesOutcome_ReportsApply` — the control; without it, a lookup answering already-applied unconditionally passes row 3 |
| 5 | ✅ | A missing stored value reports apply, not already-applied | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueIsMissing_ReportsApply` — "or add it if it was missing" |
| 6 | ✅ | A moved incoming side reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingValueDiffersFromRecordedSnapshot_ReportsStale` — #153's incoming-side half, unchanged |
| 7 | ✅ | A moved incoming side reports stale even when the stored value is already right | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingMovedButOutcomeAlreadyStored_ReportsStale` — an already-correct target must not mask a rule that needs re-authoring |
| 8 | ✅ | A field absent from `incomingRecord` reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` |
| 9 | ✅ | A field absent from `existingRecord` no longer reports stale | Unit test | `ConflictRuleLookupTests.TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale` — half of the split, and the visible record of the reversal |
| 10 | ✅ | A stored value drifted from `recordedExisting` is no longer stale | Unit test | `ConflictRuleLookupTests.TryResolve_StoredValueDriftedFromRecordedExisting_IsNotStale` — the rewritten #153 test; this is the reversal itself. `TryResolve_ListValueDriftedFromRecordedExisting_IsNotStale` is the list-valued sibling, found while writing this row (step 3's "found while writing" note) |
| 11 | ✅ | `Keep` and `Replace` are judged against their own wanted values, not only `Custom` | Unit test | `ConflictRuleLookupTests.TryResolve_KeepAndReplaceOutcomes_AreJudgedAgainstTheirOwnWantedValue` — including `Keep` always already-applied once a row exists |
| 12 | ✅ | A rule whose incoming side has moved to agreement reports retirable, not stale | Unit test | `ConflictRuleLookupTests.TryResolve_IncomingMovedIntoAgreement_ReportsRetirable` |
| 13 | ✅ | A `Custom` rule is retirable only when the agreed value is the custom value | Unit test | `ConflictRuleLookupTests.TryResolve_CustomRuleWhereBothSidesAgreeButNotWithCustomValue_IsNotRetirable` — both sides agreeing on a *wrong* value is what a Custom rule exists to correct |
| 14 | ✅ | An already-applied rule is never reported retirable | Unit test | `ConflictRuleLookupTests.TryResolve_AlreadyAppliedRule_IsNotRetirable` — it is still doing work on every import |
| 15 | ✅ | The already-applied comparison is case-insensitive; the list-valued "moved to agreement" comparison behind `Retirable` ignores order | Unit test | `ConflictRuleLookupTests.TryResolve_OutcomeDiffersOnlyByCase_ReportsAlreadyApplied` and `TryResolve_ListOutcomeAgreesOnlyByOrder_ReportsRetirable` — proves `ValuesEqual`, not `Equals`. **Corrected 2026-09-03:** the plan's original "list sibling" for `AlreadyApplied` cannot be built — `ConflictResolutionFieldRule.CustomValue` is `string?`, so a `Custom` rule cannot hold a list value; `Retirable` is the reachable list-comparison case instead |
| 16 | ✅ | An already-applied rule leaves its action needing no decision | Unit test | `ImportActionPlannerTests.AlreadyAppliedRule_LeavesNothingPending` — passes unmodified against step 3/4's code; see step 5's finding |
| 17 | ✅ | A stale rule still stages `Stale`, and a conflict with no rule still stages `Pending` | Unit test | `ImportActionPlannerTests.StaleRule_StillStagesStale` and `..._ConflictWithNoRule_StillStagesPending` — the controls for row 16 |
| 18 | ✅ | Two works sharing a title and differing in date become two Source rows | Unit test | `ImportActionPlannerTests.SameTitleDifferentDate_ResolvesToTwoSources` — the Lion King shape |
| 19 | ✅ | A `tv` quote carrying a disagreeing year attaches to the nearest Source and is reported as a conflict, not split into a second Source row for the show | Unit test | **Corrected 2026-09-04:** the plan's original guess (resolves to "its season") assumed season-resolution infrastructure this issue doesn't build; the real fix is `ImportActionPlannerTests.TvQuoteWithASecondYear_NoSeriesData_AttachesToNearestSourceAndStagesPending`, with `MovieQuoteWithASecondYear_StillResolvesToTwoSources` as the type-based control. Found live against the real corpus: "Arrow" and "Mr. Robot" each split into two Source rows before this fix |
| 20 | ✅ | Two Sources differing only in date get distinct ids | Unit test | `EntityIdentityTests.SourceId_SameTitleAndTypeDifferentDate_DiffersById` — without it the natural key admits the row and the primary key rejects it |
| 21 | ✅ | An existing Source is still matched by natural key and keeps its id | Unit test | `ImportActionPlannerTests.ExistingSource_IsMatchedByNaturalKey_AndKeepsItsId` — the control that this needs no id rewrite; a failure here is the wipe-and-reseed the design exists to avoid. Row was left ❌ after the test was actually written — a checklist/reality drift caught by the developer, corrected 2026-09-04 |
| 22 | ✅ | The alias staleness check still works against a Source created before the change | Unit test | **Corrected 2026-09-04:** the plan's guessed test name (`AliasAgainstPreChangeSource_IsNotFalselyStale`) was never written and doesn't exist. The scenario is already covered by the pre-existing `ImportActionPlannerTests.PlanAsync_SourceAliasFresh_CanonicalSourceExists_RegressionStillDecided` — seeds a Source directly (bypassing `ResolveSourceAsync` entirely, exactly "created before the change"), then resolves an alias against it and asserts `Decided`, not `Stale`. Confirmed still passing |
| 23 | ✅ | The migration and the baseline produce an identical `Quotinator_Source` schema | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` — generic, table-agnostic structural diff; already covers `Quotinator_Source` without needing a Source-specific case added, confirmed passing against the updated baseline |
| 24 | ✅ | The migration and the baseline produce an identical `Quotinator_Quote` schema, index included | Unit test | the same `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` — its structural diff includes indexes, so `UX_Quotinator_Quote_QuoteText_SourceId` is covered without a dedicated case |
| 25 | ✅ | The same quote text under two different Sources is two rows | Unit test | `SqliteQuoteServiceTests.SameQuoteTextUnderTwoSources_IsTwoRows` — unique within each Source, with neither assumed to hold the other's line. Written and confirmed passing 2026-09-04 |
| 26 | ✅ | The same quote text twice under one Source is rejected | Unit test | `SqliteQuoteServiceTests.SameQuoteTextUnderOneSource_IsRejected` — the control; without it row 25 passes with no constraint at all. Written and confirmed passing 2026-09-04 |
| 27 | ✅ | The migration deduplicates before creating the index | Unit test | `DatabaseInitializerTests.Migration009_DeduplicatesBeforeCreatingTheUniqueIndex` — a minimal fixture built at exactly v8 (not the bundled corpus, so it keeps failing if the corpus changes), with a genuine inserted duplicate, migrated to v9. Written and confirmed passing 2026-09-04 |
| 28 | ✅ | An alias can target one of two same-titled Sources by date | Unit test | `SourceAliasLookupTests.TryResolve_AliasWithDate_TargetsTheMatchingSourceOnly`, plus the planner-level `ImportActionPlannerTests.PlanAsync_DatedAliasCorrectsWrongDate_ResolvesToCanonicalDatedSource` |
| 29 | ✅ | An alias file without dates still loads and applies | Unit test | `SourceAliasLookupTests.TryResolve_AliasWithoutDate_StillApplies`, plus `TryResolve_DatedAliasForOneDate_DatelessAliasStillCoversOtherDates` (a dated alias for one date must not shadow the date-less fallback for a different one) |
| 30 | ✅ | An uncorrected wrong date still leaves nothing pending | Unit test | `ImportActionPlannerTests.AWrongDatedSource_StillLeavesNothingPending` — the control for row 28 |
| 31 | ✅ | A file that contradicts itself about a Source's date is reported at cold start | Unit test | `DatabaseInitializerTests.ColdStart_WithAFileThatContradictsItself_ReportsIt`, with `..._WithNoSelfContradiction_ReportsNothing` as the control. Confirmed red without step 9's `ReportSelfContradictingSources` call, green with it |
| 32 | ✅ | A retirable rule is surfaced where a curator will see it | Unit test | `ImportActionPlannerTests.PlanAsync_RetirableRule_IsCollectedIntoTheSink` (the sink threads a real finding out of the planner, not just out of `ConflictRuleLookup` in isolation) with `..._StaleRule_IsNotCollectedAsRetirable` as the control. Confirmed red without step 10's capture line, green with it. The log-based surfacing itself (`LogRetirableRule`) is mechanically identical to row 31's already-proven pattern |
| 33 | ✅ | Reseeding repeatedly leaves the pending count stable, not growing | Unit test | **Corrected 2026-09-04:** `0 / 0 / 0` is no longer the right target — three genuine, permanent tv-season conflicts are correct and by design (row 19). `DatabaseInitializerTests.Reseed_Repeatedly_WithAResolvableFile_PendingCountNeverGrows` (renamed from `_LeavesNothingPending`) asserts `3 / 3 / 3` — stability is what actually matters, and what growing `3→6→9` before the fix would have violated |
| 34 | ✅ | The cold-start half still passes, with the known conflicts accounted for | Unit test | **Corrected 2026-09-04:** `DatabaseInitializerTests.Seed_WithAResolvableFile_LeavesOnlyKnownConflictsPendingAndOneAlert` (renamed from `_LeavesNothingPendingAndNoAlerts` — three genuine conflicts and their alert are now the expected, asserted outcome, not zero) |
| 35 | ✅ | The two existing real-rule-file tests still pass | Unit test | `InitialiseAsync_NikhilNamal17WithRealRuleFile_ProducesNoUnresolvedActions` (now with four documented exceptions, up from one — see step 7's own findings) and `..._GaladrielQuoteGetsCharacterOnAdd` |
| 36 | ✅ | The bundled counts still match after the schema change | Unit test | `InitialiseAsync_AllSourceFiles_SeedsExpectedCounts` — Source/Quote totals asserted at their actual measured values (798→792 Quote, 501→497 Source, the second move being the tv-conflict fix's own effect), never adjusted to whatever number a passing run happened to produce |
| 37 | ✅ | Every new test is red against the pre-fix build | Test run | Done at step 3, 2026-09-03 — ten tests confirmed red against the step-3 stub (see step 3's own list); row left ❌ after the fact, corrected 2026-09-04 to reflect that this already happened |
| 38 | ✅ | The schema and the model say which snapshot is read and which is not | Live | **Corrected 2026-09-04 — downgraded from "Unit test" to "Live":** no precedent exists anywhere in this codebase for asserting on XML-doc/schema-description prose text (checked `SourceDataIntegrityTests` and every other schema test), and building one solely for this would be exactly the kind of validation-for-its-own-sake CLAUDE.md warns against. Verified by direct reading instead: `ConflictResolutionRule.IncomingRecord`'s XML doc and `conflict-resolution-rules.schema.json`'s `incomingRecord` description both correctly state it is read for staleness/retirement detection (step 11) |
| 39 | ❌ | #373's two T2 documents pass | Automated (T2) | Not yet exercised in this session's T2 pass — #373's own remaining scope. Must be confirmed before this issue's own closing comment cites T2 as complete |
| 40 | ✅ | An existing database survives the upgrade with its data intact | Automated (T2) | Live Docker run, 2026-09-04: seeded a database with the pre-#374 image (`quotinator:old`, built from `HEAD` via `git stash`) — schema v7, 800 quotes, 467 sources. Ran the new image (`quotinator:local`) against that same volume: automatic pre-migration backup taken, "applying 2 pending App migration(s) (version 7 → 9)" with zero errors/warnings, final state 796 quotes (4 genuine content duplicates collapsed by Migration009, consistent with the known Inigo Montoya and Shawshank duplicates), 467 sources (no Source duplicated by Migration008's rebuild). Confirmed via `GET /api/v1/version` and container logs |
| 41 | ✅ | Build is clean | Build | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s), confirmed 2026-09-04 |
| 42 | ✅ | No regression | Test run | `dotnet test --configuration Release -m:1` → all green, 0 failures, confirmed 2026-09-04 (`Quotinator.Core.Tests` 1570, `Quotinator.Data.Tests` 1337, full solution) |
| 43 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | reseed twice against the bundled content; `/import-review` stays empty across both runs. **T1 is the developer's own action, not the assistant's — see CLAUDE.md** |
| 44 | ✅ | A `Blocked` collision does not double on every reseed | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithABlockedCollision_BlockedCountNeverGrows` — step 12's fourth live-found defect |
| 45 | ✅ | A Review-policy row that auto-resolved cleanly counts as modified, not silently excluded | Unit test | `DatabaseInitializerTests.Reseed_ReviewPolicyFileWithOneGenuineModify_ConfirmationCountsAddUpToIncoming` — the "107 items, 106 added, 0 updated" defect |
| 46 | ✅ | A Skip-policy row that genuinely differed is reported as skipped, not folded silently into another bucket or dropped from the UI | Unit test | `DatabaseInitializerTests.Reseed_SkipPolicyFileWithADifferingRow_ReportsItAsSkippedRatherThanVanishing` (payload) and `NotificationTableTests.SkippedOnlyRow_StillRenders` (UI table) — both confirmed red before the fix, green after |
| 47 | ✅ | Two further vilaboim/NikhilNamal17 cross-file duplicates (surfaced by NikhilNamal17's own alias file matching vilaboim's raw spelling) are excluded | Live | Docker T2: `Blocked` count 2 → 0 on cold start, stable at 0 across two reseeds, after adding `data/sources/vilaboim-quote-exclusions.json` |
| 48 | ✅ | A quote's `quoteText`/`character` differing only by case from what is stored stages Pending, not a silent resolve | Unit test | `ImportActionPlannerTests.PlanAsync_QuoteCharacterDiffersOnlyByCase_StagesPendingForReview`, with `..._ExactMatch_StaysUnchanged` and `..._MatchingConflictRuleStillResolves` as controls |
| 49 | ✅ | A quote's `source` field is excluded from the case-sensitivity rule | Live | Docker T2 against the real bundled corpus: 14 case-only `source` differences in NikhilNamal17 alone, all routine upstream-data noise with a correctly-resolved Source regardless of casing — `source` removed from `QuoteFieldMerge.CaseSensitiveContentFields` after this finding |
| 50 | ✅ | A same-batch collision on a newly-ambiguous field does not hold the rest of the batch to zero writes | Unit test | `ImportActionResolutionCoordinatorTests.TryApplyBatchAsync_PendingModifyFromSameBatch_DoesNotHoldTheRestOfTheBatch`, confirmed red before the `ExistingBatchId == BatchId` gating exemption |
| 51 | ✅ | A case-only Pending Modify does not re-stage a duplicate on a later reseed | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_WithACaseOnlyPendingModify_PendingCountNeverGrows` — extends the Add branch's existing `SelectHasUnresolvedActionById` dedup to Modify |
| 52 | ✅ | Both of #373's own T2 documents run clean against the fixed build, with the developer-reported duplication actually gone | Automated (T2) | Live Docker, 2026-09-04, rebuilt image: doc 21 steps 1–4/6 pass (step 5 can't execute — no `DELETE /quotes/{id}` endpoint exists, a document defect not a product one); doc 11 steps 1–3/5/6/8 pass, step 4/7 duplication confirmed gone (`4 → 5` fixed to stable) — one distinct, pre-existing no-op-Modify gap found and recorded, not fixed (see step 12's own text) |

**A live T2 run against real data found a sixth defect no unit test caught** (see step 7's own new
finding above): `GET /import/actions` 500'd on a genuine Pending-Add row. Fixed and reverified live —
recorded here rather than as a new numbered row, since it doesn't correspond to anything the plan
originally asked to verify; `SqliteImportActionServiceTests.GetPagedAsync_PendingAdd_AmbiguousFieldsIsEmpty`
is its permanent regression guard.

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
