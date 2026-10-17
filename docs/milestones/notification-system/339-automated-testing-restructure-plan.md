# #339: Restructure the T2 suite into docs/automated-testing/, one document per test

**Status:** In progress (step 6)
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
answer. The per-issue scope requires executing every document an issue changed, and this issue changes
no document's steps, commands, expectations or environment: it adds prose sections. That exception is
stated here rather than assumed, and the conformance guards are what cover the change instead.

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

**Status:** ⬜ Not started

`## Observed effect` is the field the issue's requirement 14 exists for, the Knowledgebase material
#333 sweeps, so its absence in 15 documents is a delivered requirement that drifted back out. Each
states what the circumstance the document creates actually produces: the log lines, health response,
UI state and messages an operator would see, distinct from the per-step expectations.

The two documents with no `## Cleanup`, `import-and-staged-actions/25` and `26`, already run
`test-env.csx destroy` as their final step, so the teardown exists and only the section is missing.
Both move that command out of `## Steps` and into `## Cleanup`, where a reader looking for how to
return the machine to a clean state will find it.

### 7. Bring the three off-template sections onto the defined heading

**Status:** ⬜ Not started

`api-surface/02` and `database-lifecycle/03` keep their content under step 2's
`## Explicitly not covered here` heading and move it to the defined position.
`identity-and-casing/02`'s `## Why the systemic id-case guard has no test of its own` says the same
thing and takes the same heading; its content already names the unit tests that cover the guard and
the endpoint gap they do not, which is [#341](https://github.com/DutchJaFO/Quotinator/issues/341).

### 8. Remove every em and en dash from the suite

**Status:** ⬜ Not started

801 occurrences across 61 of the 76 files, measured 2026-10-16. Its own commit, placed after the
content work so the two are separable in review, and after step 7 so the sections it rewrites are
already in their final shape.

**Not a single substitution.** A dash here stands in for a colon, a parenthetical pair, or a sentence
break, and each reads differently once the character is gone. The five improvised canary headings step
4 normalises are the easy case; a mid-sentence pair carrying a clause is not. Every replacement gets
read, which is why this is a step rather than a sweep.

**Quoted program output is left exactly as it is**, whatever it contains. A log line, a response body
or a rendered message reproduced in a document is evidence of what a build emitted, and editing its
punctuation makes the document claim something that build did not do. Found during step 4, where
`backup/06`'s canary quotes a since-reworded log line whose dash is the very thing the row is about.

### 9. Confirm the guards green and the full test suite clean

**Status:** ⬜ Not started

All three new guards green over 75 documents, the seven existing suite guards still green, and
`dotnet build` plus `dotnet test` at 0 warnings and 0 errors per the Pre-Push Checklist. Any file this
issue touches under `src/` or `tests/` goes into `.editorconfig`'s `IDE0008` and `IDE0090` scoped lists
per the boyscout rule.

---

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `docs/automated-testing/` exists with one kebab-case subfolder per category | Live | Seven category folders, all kebab-case |
| 2 | ✅ | One numbered document per existing section; no test dropped, merged or rewritten | Live | 43 documents from 44 sections; the index's mapping table resolves all 44 old numbers |
| 3 | ✅ | An index carries the rules block, the living-checklist rule, and every test document | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, `...EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument` |
| 4 | ✅ | Cross-references between tests are links, not prose, and every link resolves | Unit test | `RepositoryStructureTests.EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument` |
| 5 | ✅ | Every test is marked in or out of the smoke set, and the index's set cannot drift from the documents | Unit test | `RepositoryStructureTests.SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` |
| 6 | ✅ | The index states the three run scopes as the authoritative definition | Live | Index names per-issue T2, milestone close and release; `release-verification.md`'s T2 tier points at it rather than restating it |
| 7 | ✅ | The numbering scheme is recorded; each filename carries a number and a stable slug | Live | Index states per-category numbering; every filename matches `NN-slug.md` |
| 8 | ✅ | Every document names an environment profile the index defines and establishes it | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile` |
| 9 | ✅ | Every step carries its own expected result | Unit test | `RepositoryStructureTests.EveryAutomatedTestingStep_CarriesItsOwnExpectedResult` |
| 10 | ✅ | Every test creates and destroys its own container and volume, so any two can run concurrently | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` |
| 11 | ✅ | No bash construct returns to the suite | Unit test | `RepositoryStructureTests.EveryAutomatedTestingCodeBlock_IsPowerShell` |
| 12 | ✅ | The reliability rule is stated in the index and served by a Determinism field on every document | Live | Index carries the rule; row 13 covers the per-document half |
| 13 | ❌ | Every document carries every section the template defines | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_CarriesEverySectionTheTemplateDefines`, red against the 92 missing sections measured 2026-10-16 |
| 14 | ❌ | The sections appear in the order the template defines | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_OrdersItsSectionsAsTheTemplateDoes`, red 2026-10-17 against `api-surface/02` and `database-lifecycle/03`, which place their exclusions section after `## Observed effect` |
| 15 | ❌ | No document carries a section the template does not define | Unit test | `RepositoryStructureTests.NoAutomatedTestingDocument_CarriesASectionTheTemplateDoesNotDefine`, red against the 13 off-template sections measured 2026-10-16 |
| 16 | ✅ | The guards read their expectations from the index's template block, so template and guard cannot drift | Live | Removing `## Canary` from the template block took the presence guard's 75 Canary failures to 0 while its 15 `## Observed effect` failures stayed, 2026-10-17; the block was restored byte-identical |
| 17 | ✅ | The template defines a Canary section, required of every document, carrying a procedure and any reading a canary run produced | Live | The index's template block states it, its position and both forms; all 75 documents carry one, and none states its own current pass or fail |
| 18 | ❌ | The template defines the exclusions section, and the three off-template sections use it | Live | `api-surface/02`, `database-lifecycle/03` and `identity-and-casing/02` carry `## Explicitly not covered here` in the defined position |
| 19 | ❌ | Test outcomes are recorded as Knowledgebase material by an `Observed effect` section on every document | Unit test | Row 13's guard covers the presence half; #333 requirement 6 states the sweep |
| 20 | ✅ | Test resources sit beside the document; test scripts sit in `scripts/testing/` | Live | `scripts/testing/` holds the five suite scripts; no `.csx` anywhere under `docs/` |
| 21 | ✅ | ADR 010 revised in place; `CLAUDE.md`'s Developer Context bullet matches | Live | ADR 010's Decision section states the subfolder rule and the unverified-verifier rule; `CLAUDE.md` states it identically |
| 22 | ✅ | `docs/smoke-tests.md` is removed and no reference to it resolves to nothing | Live | The file is gone; every remaining mention is an archival record in a closed issue's plan doc, each naming where the suite went |
| 23 | ✅ | A live-only issue has a Definition of done it can honestly tick | Live | `docs/workflow/issues.md` carries the live-only variant; no placeholder Expected-tests row is required |
| 24 | ✅ | Every OpenAPI tag an endpoint uses is declared with a non-empty description, and the rule is recorded | Unit test | `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription`; ADR 020, its README row, its `.slnx` entry, and `CLAUDE.md`'s *Endpoint groups* section |
| 25 | ✅ | This issue carries no dependency on another milestone | Live | No `Depends on:` header here; `overview.md`'s dependency map and Order of operations row for #339 name no blocker |
| 26 | ❌ | No file in the suite carries an em or en dash | Live | A `Select-String` over every `docs/automated-testing/**/*.md`, read as UTF-8, for a character class of U+2013 and U+2014, returns nothing. 801 occurrences across 61 of 76 files measured 2026-10-16 |
