# #339: Restructure the T2 suite into docs/automated-testing/, one document per test

**Status:** Waiting for release
**GitHub issue:** #339
**Tiers required:** T1, T2

---

## Description

`docs/smoke-tests.md` held 44 test sections in a single 2,205-line file. This issue split it into
`docs/automated-testing/`, one document per test inside a category subfolder, behind a common
template, and defined the three run scopes the suite had never had.

**The split is delivered and in use.** Seven categories, 75 documents, seven mechanical guards, and
every issue since 2026-08-27 has authored into the structure rather than into a monolith. What remains
is the second half of the issue's own requirement 3: the template is followed strictly, by every
document, mechanically rather than by review habit.

**This plan was rewritten on 2026-10-16** against the suite as it now stands. The per-step execution
record of the 27 steps that delivered the split is in git history; reproducing it here was 1,100 lines
describing finished work, which left a reader unable to answer what to do next.

**The rewrite dropped one outstanding commitment, found on 2026-10-20 while ticking the Definition of
done.** The original plan's step 5 recorded that
`EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument` was green vacuously when written, since
no index link existed yet to point at a missing file, and it named the step that would owe the proof:
*break one index link deliberately, confirm the test fails, restore it*. That step never ran, and the
rewrite carried the row forward as ✅ without the citation. Both link guards have now been proven red
against a deliberately repointed link, recorded in rows 3 and 4. Dropping a deferred red proof in a
rewrite is the same failure as step 13's, one document later: a row that reads as verified because
nobody re-read what it was waiting on.

---

## What the restructure delivered

Stated once, so the remaining steps below are readable as the whole of the outstanding work.

| Delivered | Where it lives |
|---|---|
| Seven category folders, per-category `NN-slug.md` numbering | `docs/automated-testing/` |
| 43 documents from the previous file's 44 sections, since grown to 75 | the category folders |
| The document template, the suite's rules, the three run scopes, the nine-document smoke set | `docs/automated-testing/README.md` |
| Three environment profiles and the snapshot/restore procedure | the index's *Environment profiles* |
| `scripts/testing/test-env.csx`, `http.csx`, `conflict-rule.csx`, `execute-sql.csx`, `sqlite-storage-probe.csx` | `scripts/testing/` |
| The whole suite in PowerShell, with the two Windows PowerShell 5.1 traps recorded as rules | the index, and every document |
| Seven guards over the suite | `RepositoryStructureTests` |
| The test-support script rule | [ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md) |
| The OpenAPI tag rule, its guard, and the missing `Notifications` description | [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md), `Program.cs`, `OpenApiSpecEndpointTests` |
| The live-only Definition-of-done variant | `docs/workflow/issues.md` |

---

## The finding this plan now addresses

Measured over all 75 documents on 2026-10-16.

**Every template element a guard covers held. Every element without one drifted.** That is the whole
diagnosis, and it is why the remedy is a guard rather than a review pass.

| Element | Guard | Conformance |
|---|---|---|
| `Smoke`, `Environment`, `Traces to`, `Preconditions`, `Determinism`, `Steps`, per-step `Expected`, PowerShell fences, port uniqueness, index and cross-reference links | yes | 75 of 75 |
| `## Observed effect` | none | **15 of 75 missing** |
| `## Cleanup` | none | **2 of 75 missing** |

**And a section the template never defined was invented twelve times.**
[`testing-policy.md`](../../testing-policy.md)'s *A feature is covered when both outcomes, both
extremes, and a canary exist* has required a canary of every T2 document since 2026-10-06 (#420). The
template has no slot for one, so authors improvised:

- Eleven documents carry a `## Canary…` heading in ten distinct spellings.
- A twelfth states the same thing as `## Confirming this test would have caught the original bug`.
- Placement varies: three sit before `Observed effect`, one after, and seven are in documents that
  have no `Observed effect` at all.
- Five of the headings carry the detail that belongs in the body, naming an issue number in the
  heading itself.

**Three further sections sit outside the template**, and all three say the same kind of thing: what
this document deliberately does not cover, and where that coverage lives instead.
`api-surface/02-pagination-contract` points at `OpenApiSpecEndpointTests`,
`database-lifecycle/03-reset-is-a-full-wipe` at #156's test-only fixtures, and
`identity-and-casing/02-quote-id-case-insensitive-lookup` at the unit-tier `SqlIdCaseGuard`. Three
independent authors reaching for the same shape is a missing slot, not three mistakes.

---

## Scope

**In scope: the template, and every document's conformance to it.** The template gains the two
sections the suite demonstrably needs, every document carries every section the template defines in
the order it defines them, and three guards hold it there.

**A Canary section states the canary the document needs, not a verdict on whether one was run.** The
index already requires this of every other field: *a document with a missing observation states the
observation it needs, and the run fails*, and *a document never records its own verdict*. A canary is
the same kind of claim. So the 63 documents gaining one are stating a procedure, and this issue does
not thereby owe 63 canary runs.

**Out of scope: the quality of any individual test.** Whether a document's assertions can distinguish
the feature working from the feature broken, whether its canary has actually been run, and whether it
still runs as written are all test quality, and they are already parcelled out:
[#411](https://github.com/DutchJaFO/Quotinator/issues/411) and
[#438](https://github.com/DutchJaFO/Quotinator/issues/438) in this milestone, and
[#441](https://github.com/DutchJaFO/Quotinator/issues/441),
[#402](https://github.com/DutchJaFO/Quotinator/issues/402),
[#401](https://github.com/DutchJaFO/Quotinator/issues/401),
[#414](https://github.com/DutchJaFO/Quotinator/issues/414),
[#400](https://github.com/DutchJaFO/Quotinator/issues/400) and
[#347](https://github.com/DutchJaFO/Quotinator/issues/347) in v1.9.0.

**Out of scope: running the suite.** Every test runs at milestone close, which is the run scope's own
answer. The per-issue scope requires executing every document an issue changed, and this issue adds
prose sections rather than changing any document's steps, expectations or environment. That exception
is stated here rather than assumed, and the conformance guards are what cover the change instead.

**One command's text did change, and it is named rather than glossed over.** Step 11's dash pass
reached four lines inside code fences, three of them PowerShell comments and one a display loop in
`notifications-and-changelog/11` that prints each confirmation's title and body for a reader. Its
separator went from a dash to a colon. No assertion parses that line: the step requires one
confirmation per cleanly-applied file, each naming its own file, which the printed titles satisfy
either way. So the exception above still holds in substance, and this is what it cost in fact.

**In scope, as its own step and its own commit: 801 em and en dashes across 61 of the 76 files here**
(developer decision, 2026-10-16). This issue touches all 75 documents, so the boyscout rule reaches
them. It stays a separate commit rather than riding along with the template work for two reasons: 801
mechanical replacements would bury that work in the diff, and a replacement this size needs its own
review pass, since a dash standing in for a colon, a parenthesis or a sentence break does not reduce
to one substitution.

---

## Scope change: the #347 dependency is retired (2026-10-16)

**Developer decision.** This plan previously carried `Depends on: #347`, and verification row 28 stayed
red because `import-and-staged-actions/16` and `17` cannot distinguish the staleness mechanism running
from it never running.

That is the quality of two specific tests, not the structure this issue delivers, and #347 is in
v1.9.0, so the dependency meant a Notification system issue could not close until a later milestone
shipped, holding [#327](https://github.com/DutchJaFO/Quotinator/issues/327) and
[#328](https://github.com/DutchJaFO/Quotinator/issues/328) behind it.

Both documents already carry `**Fully green after:** #347`, which is the convention the suite adopted
for exactly this, so retiring the dependency removes a duplicate record rather than losing one. The
same applies to `import-and-staged-actions/14` and `startup-and-degradation/03`, blocked on #400 and
#438; both are in the smoke set, and both say so themselves.

---

## Steps

### 1. Retire the #347 dependency and update the overview

**Status:** ✅ Done, 2026-10-16

The `Depends on:` header is gone from this plan. `overview.md`'s dependency map and Order of operations
row for #339 drop the cross-milestone block, and #327's and #328's rows keep their dependency on this
issue's structure, which is what they were actually waiting for.

### 2. Extend the document template with Canary and an exclusions section

**Status:** ✅ Done, 2026-10-16

Two additions to the index's *Document template* block, each because the suite demonstrated the need
rather than to anticipate one.

Both landed in the block itself, with the optional one carrying the same `← only when it applies`
marker the `Fully green after:` field already used, so optionality is readable from the template
rather than known separately by the guards. The prose beneath records why each exists, both canary
forms, the rule that a canary section states a procedure and never a run's verdict, and the
measurement that guarded elements held at 75 documents while unguarded ones drifted. The 45 existing
`RepositoryStructureTests` stayed green across the change.

**`## Canary`, required, between `## Observed effect` and `## Cleanup`.** It states how this document
is proven able to fail: a build from the commit before the issue in `Traces to`, run against the
document's own steps, with the reading each step gives on that build; or, where no pre-fix build
applies, a positive control the run produces itself. The section carries the procedure, never a verdict,
and the issue number goes in the body rather than the heading.

**`## Explicitly not covered here`, optional, between `## Determinism` and `## Steps`.** It states what
the document does not cover and where that coverage lives, which is what all three off-template sections
already say. Optional, like the `Fully green after:` field: a document with nothing to exclude omits it,
and one that has something uses this exact heading in this exact position.

The template block also gains the statement that it is the authoritative list, and that the guards in
step 2 read their expectations from it.

### 3. Write the three conformance guards and confirm each red

**Status:** ✅ Done, 2026-10-17

Three statements, so three tests, in `RepositoryStructureTests` beside the seven that already hold the
suite.

| Test | Statement |
|---|---|
| `EveryAutomatedTestingDocument_CarriesEverySectionTheTemplateDefines` | Every required header field and section the template defines is present in every document |
| `EveryAutomatedTestingDocument_OrdersItsSectionsAsTheTemplateDoes` | The sections appear in the template's own order |
| `NoAutomatedTestingDocument_CarriesASectionTheTemplateDoesNotDefine` | No document carries a section outside the defined set |

**Each reads the required set from the index's own template block**, parsing the fenced sample for its
`## ` headings and `**Field:**` lines, rather than hardcoding a list. This follows
`EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile`, which reads the profiles from the index
for the same reason: a hardcoded list is a second source of truth, and the template drifting from the
guard is the same defect one layer down.

**Expected red state, measured 2026-10-16**, with step 2's template in place and before steps 4 to 7:

- **Presence: 92 missing sections.** No document uses the bare `## Canary` heading, so all 75 are
  missing it, plus 15 missing `## Observed effect` and 2 missing `## Cleanup`.
- **Off-template: 13 sections.** Eleven `## Canary…` variants, six colon-separated and five
  dash-separated, one
  `## Confirming this test would have caught the original bug`, and one
  `## Why the systemic id-case guard has no test of its own`. The two
  `## Explicitly not covered here` sections stop being off-template the moment step 2 defines the
  heading.

**Measured red state, 2026-10-17.** All three failed, 45 existing guards stayed green, and the build
reported 0 warnings and 0 errors.

| Guard | Failures | Matches the prediction |
|---|---|---|
| Presence | 92: 75 `## Canary`, 15 `## Observed effect`, 2 `## Cleanup` | yes |
| Off-template | 13 | yes, and the two `## Explicitly not covered here` sections correctly dropped out once step 2 defined the heading |
| Order | 2 | no, and this is the better result |

**The ordering guard was predicted unable to fail, and that prediction was wrong.** The reasoning was
that no document uses the bare `## Canary` heading yet, so nothing recognisable could be out of place,
and a mutation would be needed to show the guard works. Running it found a real case instead:
`api-surface/02` and `database-lifecycle/03` both place their exclusions section after
`## Observed effect`, where the template puts it between `## Determinism` and `## Steps`. Two authors
independently chose the later position, which is worth knowing, and the template's position stands;
step 7 moves both. No mutation was needed for this guard.

**The guards were confirmed to read the template rather than a list of their own**, which is the
property that keeps them from drifting apart. `## Canary` was removed from the template block and the
presence guard's 75 Canary failures went to 0 while its 15 `## Observed effect` failures stayed; the
block was then restored byte-identical. A hardcoded list would have been indifferent to the edit.

**Three MSTEST0037 warnings were introduced and fixed in the same step**, before the red run was
recorded: `Assert.IsTrue` on an index comparison wants `Assert.IsGreaterThan` and
`Assert.IsGreaterThanOrEqualTo`. The 0-warnings gate applies to a test build exactly as to a plain
one.

### 4. Normalise the twelve improvised canary sections

**Status:** ✅ Done, 2026-10-17

Eleven `## Canary…` headings and `## Confirming this test would have caught the original bug` become
`## Canary`. The detail the heading carried, which issue's pre-fix build and what it showed, moves into
the section body, where it is readable rather than compressed into a title.

**No body needed the heading's content moved into it.** All twelve already named their issue and their
pre-fix commit in the opening sentence, so the headings were duplicating what the section said one line
below. That is worth recording as the reason the eleven spellings cost nothing to retire.

**Three sections also moved**, since they sat above `## Observed effect` where the template puts them
below it: `backup/06`, `backup/07` and `notifications-and-changelog/14`.

**Measured after the step**: the presence guard's Canary failures fell from 75 to 63, the off-template
guard from 13 to 1, and the ordering guard stayed at 2. The remaining three failures are all step 7's
work, so the counts moved by exactly the amount this step touched and nothing else.

**One correction made in passing, which gives step 8 a rule.** `backup/06`'s canary table quotes the
log line an older build wrote, `seeding refused — no backup could be taken`, and the row exists
*because* that wording has since changed. Rewriting its dash was an error, caught and reverted in the
same step: quoted program output is evidence, not prose.

### 5. Give the other 63 documents the Canary section they need

**Status:** ✅ Done, 2026-10-17

Each states the canary its own subject requires, in one of two forms, and says which applies:

- **A pre-fix build**, where the document traces to a specific fix: the commit before that issue's
  first commit, built under a distinct tag per `testing-policy.md`'s *Red first applies to automated
  tests*, with the reading the document's steps give against it.
- **A positive control inside the run**, where no pre-fix build applies: a baseline or contract
  document whose subject predates the suite, or one whose pre-fix build cannot be run by the current
  `test-env.csx`. The control is what makes a step that could report nothing report something,
  produced by the test itself.

Where neither form can be stated for a document, that is a finding about the document and is recorded
against the quality issues in scope above, not resolved by writing a canary that cannot be run.

**All 75 documents now carry one**, measured: the presence guard's Canary failures are `0`, and the
only remaining failures are steps 6 and 7's own. 1,091 lines added, none carrying an em or en dash.

**The template's wording was corrected while this step ran, and the correction matters.** It said the
section carries "a procedure, never a verdict", which read as forbidding exactly what the twelve
existing sections do: record what a pre-fix build actually read. That is not the verdict the index
forbids. *This test currently fails* is a claim about the current build and goes stale; the reading a
canary produced is a claim about a named commit, which does not change. The template and its prose now
say so, and name the commit and tag as part of the record.

**Writing them surfaced two things worth keeping.**

`17-source-alias-staleness` has no step that makes an alias genuinely stale: steps 2 and 3 both read
lists that must be empty, so between them they cover two paths to the same absence and none to the
presence. Its sibling `16` gained that case; this one has not. Per the index, its canary states the
rename the document needs rather than a control it does not have, and the run reports it.

Four documents are control pairs rather than independent tests, and now say so and link each other:
`23` with `24` on whether a `Complete` row blocks, and `25` with `26` on whether a conflict is staged
again. In each pair a build that answers one way for everything passes one document and fails the
other, so either alone cannot tell a correct guard from a disabled one.

### 6. Fill the fifteen missing Observed effect and two missing Cleanup sections

**Status:** ✅ Done, 2026-10-17

`## Observed effect` is the field the issue's requirement 14 exists for, the Knowledgebase material
#333 sweeps, so its absence in 15 documents is a delivered requirement that drifted back out. Each
states what the circumstance the document creates actually produces: the log lines, health response,
UI state and messages an operator would see, distinct from the per-step expectations.

The two documents with no `## Cleanup`, `import-and-staged-actions/25` and `26`, already run
`test-env.csx destroy` as their final step, so the teardown exists and only the section is missing.
Both move that command out of `## Steps` and into `## Cleanup`, where a reader looking for how to
return the machine to a clean state will find it.

**The presence guard is green: no document is missing any section the template defines.** The two
remaining failures are step 7's, and 227 lines were added with no em or en dash.

**None of the fifteen claims a measurement, and that is deliberate.** Every `Observed effect` section
already in the suite opens with a measured date against a named build, because it was written from a
run. These fifteen were not, and inventing a date would have turned the Knowledgebase's own source
material into fabricated evidence.

So they state what the circumstance produces, drawn from what the document's own steps assert and the
behaviour the traced issue replaced, with no date and no quoted output the document does not already
quote. An undated statement of what an operator sees stays true; a dated one is the stronger claim and
is added when a run actually measures it. Neither form goes stale, which is why no "not yet measured"
note was written either.

### 7. Bring the three off-template sections onto the defined heading

**Status:** ✅ Done, 2026-10-17

`api-surface/02` and `database-lifecycle/03` keep their content under step 2's
`## Explicitly not covered here` heading and move it to the defined position.
`identity-and-casing/02`'s `## Why the systemic id-case guard has no test of its own` says the same
thing and takes the same heading; its content already names the unit tests that cover the guard and
the endpoint gap they do not, which is [#341](https://github.com/DutchJaFO/Quotinator/issues/341).

All three guards went green on this step: 48 of 48 passing, 0 warnings. Presence, order and the
defined-section set hold across all 75 documents.

### 8. Extend the template so a measurement and a determinism claim are both stated

**Status:** ✅ Done, 2026-10-17

Three additions, all from developer direction on 2026-10-17 after step 6 wrote fifteen undated
`Observed effect` sections.

**An `Observed effect` either records a measurement or is listed as awaiting one.** A measured section
names the date and the build it was read against, which sixty already do. The fifteen that state the
effect without having measured it are listed in the index instead, so the difference is visible and
countable rather than a matter of reading sixty sections and noticing which ones lack a date.

**Where an effect can be measured precisely, the document says what to measure.** A prose description
of what an operator sees is not the same as a number, and the fifteen awaiting entries each name the
quantity and where it is read, so the next run records a figure instead of re-describing the
behaviour.

**Every document declares whether its results are deterministic**, as a `**Deterministic:**` header
field beside `Smoke:` and `Environment:`. Determinism is already the suite's rule and already has a
section; what is missing is the test *saying* which it is, so a reader can tell a document that pins
everything from one whose reading moves. A document answering `no` says in `## Determinism` what
varies and why it cannot be pinned.

**The marker is one spelling, `**Measured <date>**`, and settling that was the finding.** Seven words
introduced a date across the suite, `Measured`, `Re-measured`, `Captured`, `Live-verified`,
`Executed`, and two uses of lowercase `measured` and `live-verified` mid-sentence. A guard reading
seven synonyms is a guard with six ways to miss, which is the same lesson the eleven canary headings
taught. One of those dates was a developer decision rather than a reading, so the date alone was never
a reliable signal either.

### 9. Write the four guards for those claims, and confirm each red

**Status:** ✅ Done, 2026-10-17

| Test | Statement |
|---|---|
| `EveryObservedEffect_IsMeasuredOrListedAsAwaitingMeasurement` | Each `## Observed effect` records a measurement, or its document is in the index's awaiting list |
| `TheAwaitingMeasurementList_MatchesTheUnmeasuredDocuments` | That list names exactly the unmeasured documents, and nothing else |
| `EveryAutomatedTestingDocument_DeclaresWhetherItIsDeterministic` | Every document carries `**Deterministic:** yes` or `no` |
| `EveryNonDeterministicDocument_SaysWhatVaries` | A document answering `no` states what varies in its `## Determinism` section |

The second is the forcing function, and it is the same shape as
`SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke`: two independent places that cannot drift. Adding
a measurement to a document without removing its awaiting entry fails, so the bookkeeping cannot be
skipped, and the list shrinks as runs land rather than being a note somebody has to remember.

**Three were red against the tree as it stood**: 61 documents carried no measurement marker, the index
had no awaiting list, and no document carried the new field. The presence guard from step 3 went red
with them, which is the template-driven design working: a section added to the template becomes
required of every document without anyone editing a test.

**The fourth could not be red when written, and was proven by mutation instead.** With no document
yet answering `no`, `EveryNonDeterministicDocument_SaysWhatVaries` had no subject. Once step 10 gave
it two, `**What varies:**` was removed from `startup-and-degradation/03`, the guard failed naming that
document, and the line was restored. The same technique as the ordering guard in step 3, and recorded
rather than claimed.

**Two SYSLIB1045 warnings were introduced and fixed in the same step**: two inline `Regex.Match` calls
became `GeneratedRegex` partials, matching the file's existing idiom.

### 10. Fill the determinism field and the awaiting-measurement list

**Status:** ✅ Done, 2026-10-17

`**Deterministic:**` on all 75 documents, read from each one's own `## Determinism` section rather
than assumed. Most are `yes`: a fixed image and a pinned setting produce the same readings every run.
The ones that are not are the point of the exercise, and the suite already knows two of them, the
source-download path that has produced both failures and sub-second successes, and the startup window
whose state is transient rather than pollable.

The index's awaiting-measurement list carries one row per unmeasured document, naming the quantity the
next run must read.

**Measured, and the figure is the finding: 61 of 75 documents carried no measurement.** Twelve of
those were genuinely measured under another word and were normalised, leaving **49 documents, two
thirds of the suite, whose `Observed effect` describes what an operator would see without any run
behind it.** The `backup/` category is the exception, fully measured at 8 of 8. The `backup` work was
done while this convention was being argued for, which is probably why.

**The determinism answer is 73 `yes` and 2 `no`**, and finding the two was not a sweep. Candidates
came from grepping every `## Determinism` section for admissions of variability, then reading each
hit: `backup/01` and `backup/03` both turned out to *pin* their timing explicitly and say so, so they
are `yes`. The two that genuinely vary are `api-surface/06`, whose step 1 accepts either `0` or `2`
depending on what the browser pane already held, and `startup-and-degradation/03`, which observes a
window that a fast machine can close before the first request lands.

**Both `no` answers are bounded in the safe direction**, and their `**What varies:**` lines say so:
missing the startup window fails the assertions rather than passing them, so the exposure is a false
negative and never a false positive.

**The first grep for variability matched every document in the suite**, because the pattern `race`
is a substring of `Traces to:`. Re-run with word boundaries it matched eight. Worth recording because
a 75-of-75 hit rate reads as a finding rather than as a broken pattern, and the only reason it did not
become one is that the number was implausible.

**The 75-document field insertion was a one-shot `.csx` in the scratchpad, not a committed script.**
ADR 010 puts a bulk find-and-replace across many files in C# rather than a shell one-liner, and also
says a verification script is kept only if something verifies with it. A script for an insertion that
happens once meets the first rule and fails the second, so it ran from the scratchpad and was not
committed.

### 11. Remove every em and en dash from the suite

**Status:** ✅ Done, 2026-10-17

801 occurrences across 61 of the 76 files, measured 2026-10-16. Its own commit, placed after the
content work so the two are separable in review, and after step 10 so the sections it rewrites are
already in their final shape.

**Not a single substitution.** A dash here stands in for a colon, a parenthetical pair, or a sentence
break, and each reads differently once the character is gone. The five improvised canary headings step
4 normalises are the easy case; a mid-sentence pair carrying a clause is not. Every replacement gets
read, which is why this is a step rather than a sweep.

**Quoted program output is left exactly as it is**, whatever it contains. A log line, a response body
or a rendered message reproduced in a document is evidence of what a build emitted, and editing its
punctuation makes the document claim something that build did not do. Found during step 4, where
`backup/06`'s canary quotes a since-reworded log line whose dash is the very thing the row is about.

**780 replaced, 7 kept.** Measured before: 776 in prose, 4 inside code fences, 14 inside inline code
spans. The fenced and inline figures came from a per-line backtick count, and that classification was
wrong in both directions, which is the reason for reading each one rather than trusting it:

- **The 4 "inside a fence" were authored prose**, three PowerShell comments and one output format
  string. Every one was replaced. A classification that treats a fence as evidence would have left
  them.
- **Of the 14 "inside an inline span", 7 were prose** whose line happened to carry an odd number of
  backticks, because a code span opened on an earlier line. The other 7 are the genuine quoted output,
  five of them `execute-sql.csx`'s own `OK — N row(s) affected.`, one a quoted log-line format, and
  one the since-reworded refusal line `backup/06`'s canary turns on.

**The shapes, measured:** 737 of the 776 were a mid-sentence ` — `, 32 wrapped at end of line, 5
continued at line start, and 2 were step ranges where "to" is the replacement. Each became a comma, a
colon, a semicolon, a full stop or a parenthetical pair according to what its sentence was doing, and
the end-of-line ones were read together with their continuation, since the right choice is not
visible from the broken line alone.

**Mechanics: every replacement was written out by hand and applied by line number.** A
`.csx` in the scratchpad applied a tab-separated list of file, line and replacement text, refusing any
row whose target line held no dash or whose replacement still held one, so a mistyped line number
failed loudly instead of rewriting the wrong line. Twelve batches, grouped by category.

**`backup/` needed nothing.** Its eight documents were already clean, having been written after #348
removed the dashes from the files it touched, which is the boyscout rule working.

A punctuation sweep afterwards found no damage: every `::`, ` :` and `,`-then-capital hit was
pre-existing code or quoted output. All 52 guards stayed green.

### 12. Confirm the guards green and the full test suite clean

**Status:** ✅ Done, 2026-10-17

All eleven suite guards green over 75 documents: the seven that existed before this replan, the three
conformance guards from step 3, and the four measurement and determinism guards from step 9. `52`
`RepositoryStructureTests` pass.

`dotnet build --configuration Release` reports 0 warnings and 0 errors, and
`dotnet test --configuration Release -m:1` reports 11 projects successful, 4,565 tests, 0 failures, 0
warnings. Run after step 11's dash pass, so it covers the committed state.

**No new `.editorconfig` entry was needed.** The only file this work touched under `src/` or `tests/`
is `RepositoryStructureTests.cs`, which is already in both the `IDE0008` and `IDE0090` scoped lists,
so explicit types and target-typed `new` applied to everything written here and the build proves it.

**No further T2 pass is required by this work.** Nothing under `src/` changed after the replan: the
OpenAPI tag fix that brought T2 into this issue is recorded as verified, and everything since is
documentation and guard tests. The one command text that changed is named in the Scope section above.

---

---

### 13. Write every guard against example data, including the empty set

**Status:** ✅ Done

**The guards were only ever exercised against one snapshot of the live tree, so what they enforce is
unestablished** (developer, 2026-10-17). Each went red against whatever the documents happened to
contain that day and green once those cases were fixed. That demonstrates the guard noticed those
cases; it says nothing about the rule holding over any document the tree did not happen to contain.
This is the issue's own deliverable, so it is not done until the guards are.

**Testing against the real tree is the application-side validation, not the test.** A unit test states
a scenario and asserts the outcome, so a failure in the real tree points back at a scenario someone
chose. A guard that only ever reads the real tree inverts that: it reports on today's content, and an
unexpected result means the scenarios were never enumerated at all.

**One guard cannot be validated against the tree even in principle.**
`EveryNonDeterministicDocument_SaysWhatVaries` has no `IsNotEmpty` assertion and must not have one,
since zero non-deterministic documents is a legitimate state. Working correctly over nothing and
broken so that it inspects nothing produce the identical green. Only example data separates them.

**And a concrete defect path is already visible in the guard code itself.** `SectionHeadings` tracks
fenced blocks by flipping a boolean on any line starting with three backticks, while
`ExtractDocumentTemplateBlock` counts depth, because a fence nested inside another defeats the toggle.
The same file both knows the toggle is insufficient and relies on it, and no test covers a document
carrying a nested fence. Found by reading the code after the developer's challenge, not by a run.

What this step does:

- Give the scanning logic its input instead of letting it find its own: the document set and the
  template as parameters, so a test can hand it a scenario.
- Cover each rule with example documents: conforming, each violation it exists to catch, the shapes
  that have already fooled this logic once (a nested fence, a heading inside a code block), and the
  empty set, whose correct outcome differs per guard and is therefore asserted rather than inferred.
- Keep one thin test per guard over the real tree, which is where the live content belongs.

**What this step produced.** `AutomatedTestingSuite` holds the eight rules as functions from a
document set and a template to the failures in them; `AutomatedTestingSuiteTests` states 59 scenarios
against documents the test writes; the eight guards in `RepositoryStructureTests` are now three lines
each, loading the live tree and calling the rule. The template parser, the heading reader and the
fence walk moved with them, so the suite has one scanner rather than three.

**One scanner, because the two it replaced were each right about a different input.** The toggle read
the index's own template sample wrong, since a fence nested in another flips it back. Depth counting
read a document's quoted program output wrong, since the suite opens those blocks with a bare fence
and depth counting takes a bare fence for a close. Both shapes are live here. The CommonMark rule is
neither approximation: any fence line opens a block, and only a bare fence at least as long as the
opening one closes it, because a closing fence cannot carry an info string
([spec 0.31.2, example 147](https://spec.commonmark.org/0.31.2/#fenced-code-blocks), read 2026-10-20).

**That rule also found a rendering defect in the index.** Its template sample opened with three
backticks and contained a three-backtick PowerShell fence, so by the same rule the sample ended at
that inner fence's closing line and everything below it, `## Observed effect` included, rendered as
live markdown rather than as sample. The sample is now a four-backtick block, and the index says why.

**Row 32 is implemented against seven guards failing on the empty set, not six.** The row was written
expecting `EveryNonDeterministicDocument_SaysWhatVaries` to pass over an empty set, on the ground that
zero non-deterministic documents is legitimate. Those are two different states: a set of documents in
which none declares itself non-deterministic is legitimate and passes, which the rule is tested for;
a set with no documents in it is the state that let this rule ship having inspected nothing. Letting
it pass there would preserve the exact false green the step exists to remove, so every rule reports
the empty set as a failure, and each rule answers "zero documents of my kind" separately.

**Each of the 59 tests was proven able to fail, by mutation of the rule it covers** (2026-10-20), in
six passes: the fence scanner reverted to a toggle (10 red, the nested-fence and template-reader
tests among them); a bare fence made not to open a block (2 red, the quoted-output tests); the
empty-set contracts removed (11 red, exactly the empty-set and empty-template tests); the section
terminators reverted to requiring a following heading, the unclosed-fence branch removed and the
renamed-row branch removed (4 red, one each); every rule made to report nothing (18 red, every test
that names a violation); and every rule made to report a bogus failure (30 red, every test that
asserts a document passes). Two fixtures were strengthened during this pass: an index with no
template section and an index with no awaiting list each now carry a decoy a looser reader would
pick up, so the two tests asserting "reads nothing" can fail.

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `docs/automated-testing/` exists with one kebab-case subfolder per category | Live | Seven category folders, all kebab-case |
| 2 | ✅ | One numbered document per existing section; no test dropped, merged or rewritten | Live | 43 documents from 44 sections; the index's mapping table resolves all 44 old numbers |
| 3 | ✅ | An index carries the rules block, the living-checklist rule, and every test document | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, red 2026-08-22 on an empty folder; `...EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument`, red 2026-10-20 against an index row pointing at `api-surface/01-baseline-renamed.md`, restored after |
| 4 | ✅ | Cross-references between tests are links, not prose, and every link resolves | Unit test | `RepositoryStructureTests.EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument`, red 2026-10-20 against `backup/04`'s link to `startup-and-degradation/04` repointed at a file that does not exist, restored after |
| 5 | ✅ | Every test is marked in or out of the smoke set, and the index's set cannot drift from the documents | Unit test | `RepositoryStructureTests.SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` |
| 6 | ✅ | The index states the three run scopes as the authoritative definition | Live | Index names per-issue T2, milestone close and release; `release-verification.md`'s T2 tier points at it rather than restating it |
| 7 | ✅ | The numbering scheme is recorded; each filename carries a number and a stable slug | Live | Index states per-category numbering; every filename matches `NN-slug.md` |
| 8 | ✅ | Every document names an environment profile the index defines and establishes it | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile` |
| 9 | ✅ | Every step carries its own expected result | Unit test | `RepositoryStructureTests.EveryAutomatedTestingStep_CarriesItsOwnExpectedResult` |
| 10 | ✅ | Every test creates and destroys its own container and volume, so any two can run concurrently | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` |
| 11 | ✅ | No bash construct returns to the suite | Unit test | `RepositoryStructureTests.EveryAutomatedTestingCodeBlock_IsPowerShell` |
| 12 | ✅ | The reliability rule is stated in the index and served by a Determinism field on every document | Live | Index carries the rule; row 13 covers the per-document half |
| 13 | ✅ | Every document carries every section the template defines | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_CarriesEverySectionTheTemplateDefines`, red against the 92 missing sections measured 2026-10-16, green over all 75 |
| 14 | ✅ | The sections appear in the order the template defines | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_OrdersItsSectionsAsTheTemplateDoes`, red 2026-10-17 against `api-surface/02` and `database-lifecycle/03`, which placed their exclusions section after `## Observed effect` |
| 15 | ✅ | No document carries a section the template does not define | Unit test | `RepositoryStructureTests.NoAutomatedTestingDocument_CarriesASectionTheTemplateDoesNotDefine`, red against the 13 off-template sections measured 2026-10-16 |
| 16 | ✅ | The guards read their expectations from the index's template block, so template and guard cannot drift | Live | Removing `## Canary` from the template block took the presence guard's 75 Canary failures to 0 while its 15 `## Observed effect` failures stayed, 2026-10-17; the block was restored byte-identical |
| 17 | ✅ | The template defines a Canary section, required of every document, carrying a procedure and any reading a canary run produced | Live | The index's template block states it, its position and both forms; all 75 documents carry one, and none states its own current pass or fail |
| 18 | ✅ | The template defines the exclusions section, and the three off-template sections use it | Live | `api-surface/02`, `database-lifecycle/03` and `identity-and-casing/02` carry `## Explicitly not covered here` in the defined position; the ordering and off-template guards both confirm it |
| 19 | ✅ | Test outcomes are recorded as Knowledgebase material by an `Observed effect` section on every document | Unit test | Row 13's guard reports no document missing the section; #333 requirement 6 states the sweep. The fifteen written here carry no measured date, since none was run for them |
| 20 | ✅ | Test resources sit beside the document; test scripts sit in `scripts/testing/` | Live | `scripts/testing/` holds the five suite scripts; no `.csx` anywhere under `docs/` |
| 21 | ✅ | ADR 010 revised in place; `CLAUDE.md`'s Developer Context bullet matches | Live | ADR 010's Decision section states the subfolder rule and the unverified-verifier rule; `CLAUDE.md` states it identically |
| 22 | ✅ | `docs/smoke-tests.md` is removed and no reference to it resolves to nothing | Live | The file is gone; every remaining mention is an archival record in a closed issue's plan doc, each naming where the suite went |
| 23 | ✅ | A live-only issue has a Definition of done it can honestly tick | Live | `docs/workflow/issues.md` carries the live-only variant; no placeholder Expected-tests row is required |
| 24 | ✅ | Every OpenAPI tag an endpoint uses is declared with a non-empty description, and the rule is recorded | Unit test | `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription`; ADR 020, its README row, its `.slnx` entry, and `CLAUDE.md`'s *Endpoint groups* section |
| 25 | ✅ | This issue carries no dependency on another milestone | Live | No `Depends on:` header here; `overview.md`'s dependency map and Order of operations row for #339 name no blocker |
| 26 | ✅ | No file in the suite carries an em or en dash, except where it is quoted program output | Live | A `Select-String` over every `docs/automated-testing/**/*.md`, read as UTF-8, for a character class of U+2013 and U+2014, returns 7 hits, each inside an inline code span reproducing what a build emitted. 780 replaced from the 787 present 2026-10-17 |
| 27 | ✅ | Every `Observed effect` either records a measurement against a named build, or is listed in the index as awaiting one | Unit test | `RepositoryStructureTests.EveryObservedEffect_IsMeasuredOrListedAsAwaitingMeasurement`, red against 61 unmeasured sections measured 2026-10-17, green over all 75 |
| 28 | ✅ | The awaiting-measurement list names exactly the unmeasured documents, and says what each one's next run must measure | Unit test | `RepositoryStructureTests.TheAwaitingMeasurementList_MatchesTheUnmeasuredDocuments`, red while the index had no such list; its 49 rows now match, and adding a measurement without removing its row fails |
| 29 | ✅ | Every document declares whether its results are deterministic | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_DeclaresWhetherItIsDeterministic`, red against all 75 before step 10; now 73 `yes` and 2 `no` |
| 30 | ✅ | A document that declares itself non-deterministic says what varies | Unit test | `AutomatedTestingSuiteTests.NonDeterministicDocumentsNotNamingWhatVaries_*`: four tests covering a document that names its variable, one that declares `no` and says nothing, a set in which every document is deterministic, and a Determinism section that ends the document. `RepositoryStructureTests.EveryNonDeterministicDocument_SaysWhatVaries` holds the live tree to the same rule |
| 31 | ✅ | Each guard's rule is proven against example data, not against whatever the tree holds | Unit test | `AutomatedTestingSuiteTests`, 59 tests over documents the test writes, covering all eight rules: conforming, each violation, a nested fence, a heading inside a code block, and a block opened by a bare fence. Each proven red by mutation, 2026-10-20, in the six passes step 13 records |
| 32 | ✅ | Every guard states what it does with an empty document set, and asserts it | Unit test | `AutomatedTestingSuiteTests.*_FailsAnEmptyDocumentSet`, one per rule: all eight report the empty set as a failure. Removing the eight contracts turns exactly those tests, and the three empty-template ones, red. See step 13 for why all eight fail rather than seven, and `..._PassesASetInWhichEveryDocumentIsDeterministic` for the legitimate case that row originally meant |
| 33 | ✅ | One scanner reads fenced code, by the CommonMark rule, so no rule is correct only for its own caller's input | Unit test | `AutomatedTestingSuite.FenceSpans` serves the template reader, the heading reader and the PowerShell guard. `SectionHeadings_IgnoresAHeadingInsideAFenceNestedInAnother` is red under a toggle; `SectionHeadings_IgnoresAHeadingInsideABlockOpenedByABareFence` is red under depth counting |
| 34 | ✅ | The index's template sample renders as a sample, all the way to its last section | Live | The sample's own PowerShell fence ended a three-backtick block at its closing line, by [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/#fenced-code-blocks) example 147, leaving `## Observed effect`, `## Canary` and `## Cleanup` rendering as live index headings. It is now a four-backtick block, the index states why, and `DocumentTemplateBlock_ReadsNothingFromASampleThatIsNeverClosed` covers the open-block case |
