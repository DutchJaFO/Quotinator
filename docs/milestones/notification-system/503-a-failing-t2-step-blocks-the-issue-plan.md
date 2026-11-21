# #503: A failing T2 step blocks the issue: remove the "Fully green after" exception and audit the issues it let through

**Status:** Planning
**GitHub issue:** #503
**Tiers required:** T2
**Depends on:** #347, #400, #404, #438, #445, #462, #468, #469, #472. Blocks #457, #490.

---

## Next action

**Execute this plan, from step 2.** Decisions A to G were all answered on 2026-11-21, and no question is
open. The comment on #438 that decision G calls for was posted the same day. The plan is built against
`5bfb43f6` (2026-11-21).

---

## What this changes

The issue body covers the motivation. In short: the T2 suite has three ways to report a pass while a step
is red. A `**Fully green after:** #NNN` header excuses named steps until another issue lands; an
*On failure* note tells the reader to record a known failure and carry on; and a step can print a value
without asserting it, or run nothing at all. #490 reached *Waiting for release* with `T2 ✅` through the
first two. The developer rule of 2026-11-19 removes all three: a T2 result is reported as it read, and a red
step in an issue's T2 scope blocks that issue until the issue owning the step is fixed.

---

## Cross-check against the authoritative sources

Done 2026-11-21, per `process.md` → Planning, step 3.

### ADRs

No ADR governs T2 reporting today; the exception is defined only in `docs/automated-testing/README.md`
(*A document may need more than one issue before it can be fully green*, and the template's
`Fully green after` line). The ADR README's rule *a rule is defined in exactly one place* means the new rule
gets an ADR, and the README, `testing-policy.md`, `release-verification.md`, `process.md` and `checklist.md`
point to it rather than restating it. The next free number is **029**. `EveryAdrIsIndexed`
(`RepositoryStructureTests.ResetDocumentation.cs`) holds the index to the folder.

### The documents: more than the issue names

The issue names four documents carrying the header. The suite holds seven, and two more documents carry a
known failure without one:

| Document | Smoke | Allowance | Owning issue | Owner's milestone | Allowance since |
|---|---|---|---|---|---|
| `database-lifecycle/03` step 8 | yes | header + *Record it and carry on* | #468 | v1.9.0 | 2026-10-31 |
| `notifications-and-changelog/01` step 9 | yes | header + *Record it and carry on* | #472 | v1.9.0 | 2026-11-01 |
| `import-and-staged-actions/14` step 5 | yes | header; step runs nothing | #400 | v1.9.0 | 2026-09-18 |
| `startup-and-degradation/06` step 6 | yes | header; passes or fails by timing | #445 | Startup and degraded experience | 2026-10-27 |
| `import-and-staged-actions/01` step 8 | yes | status printed, not asserted | #469 | v1.9.0 | 2026-11-01 |
| `import-and-staged-actions/16` step 3 | no | header; reading "inconclusive" | #347 | v1.9.0 | 2026-09-22 |
| `import-and-staged-actions/17` steps 2, 3 | no | header; readings "inconclusive" | #347 | v1.9.0 | 2026-09-22 |
| `startup-and-degradation/07` step 2 | no | header; reads as a failure when GitHub does not answer | #462 | Notification system | 2026-10-31 |
| `database-lifecycle/02` step 7 | no | *fails until #404 lands*, in *Observed effect* | #404 | v1.9.0 | 2026-11-08 |

Plus the two issues filed on 2026-11-20 for the same class: `notifications-and-changelog/03` (#501) and the
canaries of `backup/03` and `backup/04` (#502). Both are in this milestone and are fixed by their own issues,
not here; this issue's audit counts them.

**Five of the nine are smoke documents.** The smoke set is in every issue's T2 scope, so a red smoke step
blocks every issue whose T2 ran it. `import-and-staged-actions/14` step 5 has run nothing since 2026-09-18:
every issue that recorded a T2 pass after that date ran a smoke set with an empty step in it. That is what
makes decision B decide the size of the audit.

**Seven of the nine owners sit outside this milestone.** Under the rule each becomes a dependency of every
issue it blocks here, so this milestone cannot release before them. That is decision C.

### The guards that already read the template

`AutomatedTestingSuite` parses the README's template block, and `OptionalMarker` (*only when it applies*)
makes an element optional. Removing the `Fully green after` line from the template makes the field
undefined, but no existing guard rejects an undefined **header field**: `SectionsTheTemplateDoesNotDefine`
reads `##` sections only. The issue's first test is therefore not redundant. `AutomatedTestingSuiteTests`
uses `Fully green after` as its sample of an optional field (lines 25, 43, 183, 780); those samples are
synthetic and keep working, but they are moved to `## Explicitly not covered here` alone, so the removed
field does not survive as the suite's own example.

### Elsewhere in the repository

- `docs/milestones/notification-system/423-only-a-new-database-seeds-plan.md` row 20 records *"the two
  pre-existing #438 defects met and recorded, not fixed here"*: the same allowance in a plan doc rather
  than a document. The audit covers it.
- Plan docs of #327, #339, #372, #373, #397, #411 and #420 mention the field. They are records of what was
  done and stay as written, except where the audit sets the issue back.

---

## Decisions

### A. What the second guard matches

The issue's `AutomatedTestDocuments_NeverSayToCarryOnPastAFailure` needs a mechanical definition of "tells the reader to
continue past a failing step". **Recommended:** it reads every `**On failure:**` paragraph and every
`**Expected:**` paragraph, and fails on any of *carry on*, *continue*, *record it*, *known issue*,
*known failure*, *not asserted*, *inconclusive*, *fails until*. The list is short, each phrase is one the
suite has actually used, and a hit means the step is excused. `Expected` is included because
`import-and-staged-actions/01` step 8 excuses itself there, not under *On failure*. The alternative, *On
failure* only as the issue's test name says, would leave that step and `16`/`17`'s *inconclusive* readings
standing.

**Answered 2026-11-21: `Expected` and `On failure`.** The test is renamed
`AutomatedTestDocuments_NeverExcuseAFailingStep` to say what it reads; the issue's *Expected tests* table
is corrected to the new name in step 3, through a drafted and approved `gh issue edit`.

### B. Which issues the audit sets back

**Answered 2026-11-21: red now.** Every issue at *Waiting for release* whose T2 scope includes a step that
is red, empty or unasserted today is set back, whatever its record said when it was written. Through the
smoke set (five of its documents are in the table above) that is every issue at *Waiting for release* with
`T2 ✅`. The audit still reads each issue's record, because the red documents in its scope are what become
its dependencies.

### C. The owners outside this milestone

**Answered 2026-11-21: verify what each one blocks, and move it only if it affects notifications
themselves.** Verified the same day against each issue's body:

| Owner | What it is about | Affects notifications | Result |
|---|---|---|---|
| #469 | Applying a batch that does not exist answers `200` **and dismisses a live notification** | yes | move to *Notification system* |
| #472 | A Reset run from a notification's action **leaves no reseed recommendation** | yes | move to *Notification system* |
| #347 | Staleness evaluation of conflict rules and aliases is invisible | no | stays in v1.9.0 |
| #400 | Import behaviours tested against bundled content | no | stays in v1.9.0 |
| #404 | Reversal, discard and undo throw their refusals | no | stays in v1.9.0 |
| #468 | `/version` counts stale after an API import | no | stays in v1.9.0 |
| #445 | Page reloads log `WebSocketException`s at Error | no | stays in *Startup and degraded experience* |

**Confirmed 2026-11-21, and done the same day:** #469 and #472 are in *Notification system* and listed in
`overview.md`. The five that stay still block issues here, so this milestone cannot release before they
land in theirs.

### D. This issue's own T2

**Answered 2026-11-21: T2 required.** This issue's T2 scope is the smoke set plus every document it
changes, and those stay red until their owners land. #503 therefore depends on all eight owners, and through
it so do #457 and #490, which depend on the same owners already.

### E. An intermittent step

**Answered 2026-11-21:** *when there are intermittent results then those tests are not trustworthy and
need to be improved.* A step whose result varies between runs of the same build is a failing step, and the
remedy is to make the test deterministic, never to accept either reading. ADR 029 and the README say so.
Two documents in the table are intermittent: `startup-and-degradation/06` step 6 (timing), and
`startup-and-degradation/07` step 2 (GitHub answering), whose improvement is #462's whole purpose.
`06` also declares `**Deterministic:** yes`, which its step 6 contradicts.

### F. Who improves `startup-and-degradation/06` step 6

**Answered 2026-11-21: #503 does it, in step 6.** #445 fixes the product bug the step reads, not the step's
timing. Before counting exceptions since Cancel, the step waits until the browser page's circuit has
finished closing, so #445's `TaskCanceledException` lines are counted on every run: red every time until
#445, green every time after.

### G. The two documents declared non-deterministic

`api-surface/06` and `startup-and-degradation/03` declare `**Deterministic:** no`.

**`api-surface/06`, answered 2026-11-21:** *we should test with and without a previous cookie so we trace
both variants. We should not let chance decide if we have 0 or more lines, as that means we may miss
situations where we had more or less than expected.* Step 1's first visit reads `0` lines in a fresh browser
and `2` where the browser already held a cookie from another key ring, and accepts either. Both variants
become deliberate and asserted: one with no prior cookie, expecting `0`, and one with a cookie from another
ring, expecting `2`. **#503 does it, in step 6** (answered 2026-11-21).

**`startup-and-degradation/03`, answered 2026-11-21.** Its pass or fail depends on its requests landing
before seeding finishes. Developer: *an extra delay would compromise the real app. Users and apps are
expected to wait for `/health` to return `200` before they access anything; any attempt to access another
endpoint is never guaranteed to get results.* The block that asks for already exists:
`StartupWaitMiddleware` refuses every path but `/health` with `503` while startup runs (#419), and
`StartupWaitMiddlewareTests` proves it in-process, with no timing involved. The document is rewritten to
check only what is guaranteed: from container start `/health` answers, never refuses the connection, and
reaches `200`, after which every endpoint answers. The window itself goes under *Explicitly not covered
here*, naming those tests. **#438 owns the rewrite**, it already owns this document; added to it by a
comment posted 2026-11-21. The document is in the smoke set, so #438 joins this issue's
dependencies.

**Raised in the same answer, filed as #504** (*Startup and degraded experience*): a `Retry-After` should be
a realistic wait, and the startup responses report stage progress and the time since startup began, ticking
in the browser. Not a dependency of this issue.

---

## Steps

### 1. Confirm decisions A to G

**Status:** ✅ Done: all seven answered on 2026-11-21; #469 and #472 moved, and #504 filed.

### 2. Add the touched test files to the IDE0008/IDE0090 scoped lists

**Status:** ⬜ Not started

`.editorconfig`: the new partial `tests/Quotinator.Api.Tests/Solution/RepositoryStructureTests.FailingSteps.cs`
and `AutomatedTestingSuiteTests.cs`.

### 3. Write the two guards and confirm them red

**Status:** ⬜ Not started

In `RepositoryStructureTests.FailingSteps.cs`, reading through `AutomatedTestingSuite`'s existing loaders:

- `AutomatedTestDocuments_CarryNoFullyGreenAfterHeader`: every `.md` under `docs/automated-testing/`,
  the README included, so the template line is covered. Red today on seven documents and the README.
- `AutomatedTestDocuments_NeverExcuseAFailingStep`: the phrases decision A settles, in every `Expected`
  and `On failure` paragraph. Red today on at
  least `database-lifecycle/03`, `notifications-and-changelog/01` and `import-and-staged-actions/01`.

Each failure message names the document, the step and the matched text. Record both red readings here.

### 4. Write ADR 029

**Status:** ⬜ Not started

`docs/architecture-decisions/029-a-failing-t2-step-blocks-the-issue.md`, indexed in the ADR README:

- A T2 result is reported as it read. A step is red, or it passed.
- A red step blocks every issue whose T2 scope includes it, until the issue owning the step is fixed, and
  that owner becomes a dependency of the blocked issue, recorded in its plan's `Depends on:` and in
  `overview.md`'s dependency map.
- A failing step is one that fails its *Expected*, runs no command, prints a value its *Expected* states
  without asserting it, or gives different results on runs of the same build (decision E). An intermittent
  test is not trustworthy, and the remedy is to make it deterministic.
- A document that cannot be green until another issue lands stays red. It is never weakened to the current
  build's behaviour, and never excused.

Context: #490's record, and the four documents the issue names. Rejected: the *Fully green after* header
as a transient state, which in practice lasted from 2026-09-02 to now and grew to seven documents.

### 5. Rewrite the README and point the workflow documents to the ADR

**Status:** ⬜ Not started

- `docs/automated-testing/README.md`: remove *A document may need more than one issue before it can be fully
  green* and the template's `Fully green after` line; add a short section naming the failing-step shapes and
  pointing to ADR 029. The `**On failure:**` guidance gains that it never tells the reader to carry on.
- `testing-policy.md`, `release-verification.md`, `process.md`, `checklist.md` and `CLAUDE.md`: grep each for
  T2 reporting; where one states or contradicts the rule, reduce it to a pointer to ADR 029.
- `AutomatedTestingSuiteTests`: the four samples named above drop `Fully green after`.

### 6. Remove every allowance from the documents

**Status:** ⬜ Not started

For each row of the table above: delete the header, delete the carry-on text, and leave the step asserting
what the correct behaviour is, so it reads red until its owner lands:

- `database-lifecycle/03` step 8, `notifications-and-changelog/01` step 9: *On failure* states what a failure
  means and stops, like every other step.
- `import-and-staged-actions/01` step 8: the unknown-batch apply asserts `--expect 404`, the response #469
  specifies.
- `import-and-staged-actions/14` step 5: no command, so red per ADR 029, with its *Canary* note rewritten.
- `import-and-staged-actions/16` and `17`: the evaluation-line readings are failures until #347, not
  inconclusive.
- `startup-and-degradation/06` step 6: made deterministic per decision F, reading red on every run until
  #445. Its `Deterministic:` field then tells the truth.
- `startup-and-degradation/07` step 2: red until #462 makes it deterministic; its header goes.
- `api-surface/06` (decision G): step 1's unasserted first visit becomes two asserted variants, a fresh
  browser expecting `0` lines and a cookie from another key ring expecting `2`. Its `Deterministic:` field
  becomes `yes` and its *Determinism* section loses *What varies*.
- `database-lifecycle/02`: *Observed effect* records step 7 as failing, without the *until #404 lands*
  allowance.

Each document's Knowledgebase links stay; they describe the bug, not an excuse. The two guards turn green
here.

### 7. Confirm the changed documents read red where they should

**Status:** ⬜ Not started

The red-first run of this issue's change (`process.md` → Implementation, step 1): each document from step 6
against a build of this branch, under `test-env.csx`. Recorded per document: the step it fails at, and that
no other step fails. A failure anywhere else is reported in its own right. `06` step 6 runs three times, to
show its reading no longer varies.

This is not this issue's T2. That is step 12, and it cannot pass before the owners land (decision D).

### 8. Audit the issues at *Waiting for release*

**Status:** ⬜ Not started

For every issue `overview.md` lists at *Waiting for release* with a T2 tier: read its plan's T2 step and
Verification row, and list the documents in its scope (the smoke set, plus its own). Under decision B, every
document in that scope that is red, empty, unasserted or intermittent today, the allowance table plus #501
and #502, sets it back and names its owner as a dependency. One table here: issue, documents in scope, red
steps found, owners, verdict. Decision G's two documents are classified in the same pass.

Every issue set back:

- its plan doc: header `In progress (step N)` naming its T2 step, `Depends on:` gains the owners, its T2
  row goes ❌, and its *Next action* names the wait;
- `overview.md`: status and Tiers column;
- its GitHub Definition of done: the boxes that rested on the record unticked. Each issue edit is drafted,
  pasted in full and approved before `gh issue edit` runs (`CLAUDE.md` → *Draft, review, then act*).

#490 has its plan and overview set back already (`5bfb43f6`); its GitHub Definition of done is still to be
corrected. #423's row 20 (*#438 defects met and recorded*) is assessed the same way.

### 9. Correct #490's step 8

**Status:** ⬜ Not started

Its *Knowledgebase* bullet records adding `the-database-could-not-be-checked.md` and
`KnowledgebaseLinks.TheDatabaseCouldNotBeChecked`. Both were removed in `86eb15fd`; the bullet says so, and
names the commit.

### 10. Update the dependency map

**Status:** ⬜ Not started

`overview.md`'s order of operations and dependency map gain every owner from step 8 as a blocker of the
issues it blocks, and the five owners that stay in other milestones (decision C) are named there as
blockers from outside. #469 and #472 were moved and listed during planning.

### 11. Full build and test run

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`, 0
warnings, 0 errors. Boyscout pass on the touched C# files only.

### 12. T2

**Status:** ⬜ Not started

The smoke set plus every document step 6 changed, against a build of the branch once every owner has
landed. Every step passes, or this issue stays `In progress` (decision D, ADR 029).

### 13. Changelog

**Status:** ⬜ Not started

Internal: `unreleased.changed` in `changelog.en.json`, `nl.json` and `de.json` in lockstep; no highlight.
Written at *Waiting for release*, after step 12.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | No document under `docs/automated-testing/`, the README's template included, carries `Fully green after` | Unit test | `RepositoryStructureTests.AutomatedTestDocuments_CarryNoFullyGreenAfterHeader`, red before step 6 |
| 2 | ❌ | No document excuses a failing step, in its `Expected` or its `On failure` text | Unit test | `RepositoryStructureTests.AutomatedTestDocuments_NeverExcuseAFailingStep`, red before step 6 |
| 3 | ❌ | ADR 029 records the rule, and is indexed | Unit test + Live | `RepositoryStructureTests.EveryAdrIsIndexed` passes; `Select-String -Path docs/architecture-decisions/029-*.md -Pattern 'blocks every issue'` returns a match |
| 4 | ❌ | The README drops the exception and points to ADR 029 | Live | `Select-String -Path docs/automated-testing/README.md -Pattern 'more than one issue before it can be fully green'` returns nothing; `-Pattern '029-a-failing-t2-step-blocks-the-issue'` returns a match |
| 5 | ❌ | The README names every failing-step shape: no command, unasserted, intermittent | Live | `Select-String -Path docs/automated-testing/README.md -Pattern 'runs no command','without asserting','intermittent'` returns all three |
| 6 | ❌ | Each changed document fails at its owned step and nowhere else, and `06` step 6 reads the same on three runs | Live | Step 7's record, one line per document |
| 7 | ❌ | Every issue at *Waiting for release* is audited, and each set back carries the change in its plan, `overview.md` and its GitHub Definition of done | Live | Step 8's table; for each issue set back, `gh issue view <N> --json body -q .body` shows the unticked boxes, and its plan header and overview row agree |
| 8 | ❌ | #490's step 8 records the removal in `86eb15fd` | Live | `Select-String -Path docs/milestones/notification-system/490-layered-database-check-plan.md -Pattern '86eb15fd'` matches inside step 8 |
| 9 | ❌ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal -m:1`, 0 warnings, 0 errors |
| 10 | ❌ | #469 and #472 are in *Notification system*; the other five owners are named as blockers | Live | `gh issue view 469 --json milestone -q .milestone.title` and the same for 472 print `Notification system`; `overview.md`'s dependency map names #347, #400, #404, #445, #468 |
| 11 | ❌ | `api-surface/06` asserts both cookie variants, `0` lines without a prior cookie and `2` with one from another key ring, and declares itself deterministic | Live | Step 7's record shows both variants asserted; `Select-String -Path docs/automated-testing/api-surface/06-*.md -Pattern '^\*\*Deterministic:\*\* yes'` matches |
| 12 | ❌ | T2 passes: the smoke set and every changed document, every step green | Live | Step 12's record |
