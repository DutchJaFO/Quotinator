# #503: A failing T2 step blocks the issue: remove the "Fully green after" exception and audit the issues it let through

**Status:** In progress (step 6)
**GitHub issue:** #503
**Tiers required:** T2
**Depends on:** #347, #400, #404, #438, #445, #462, #468, #469, #472, #505, #506. Blocks #457, #490.

---

## Next action

**Continue with the unblocked steps: 7 (run the changed documents), then 8 to 10.** Steps 1 to 5 are done and
committed; step 6 is done except `startup-and-degradation/06`'s log counts, which wait for #505 and #506. Decisions A to G were all answered on 2026-11-21, and no question is
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

**Answered 2026-11-21: #503 fixes the document, and does it without the log.** The first answer was to wait
before counting log lines. It was replaced the same day, once the developer ruled that screenshots, log
reads and audit-trail reads are secondary evidence, removed from the tests, and that a test relying on one
points to a missing feature (filed for the rest of the suite as #505, in v1.9.0). For this document:

- Step 5's and step 6's screenshots go; the page scripts already assert what the pages show.
- Step 6's three log counts go: exceptions since Cancel, the `JsonReaderException` control, and the
  cleanup's count since the control. *If by removing log counts we can no longer validate the test, we
  need to replace that validation with something we can prove* (developer). The Reset's own result
  (`Completed`, a backup taken), the `200`s and the cleared banner already prove the Reset did not fail.
  What only the count caught, an exception thrown and handled without changing any result, is proven
  through a new feature: the application counts the exceptions it logs (every one passes through
  `LogMessages`' *thrown*, *handled* or *not handled* method, ADR 022) and reports the totals through a
  REST endpoint, which the step reads before and after the repair. That feature is its own issue, and a
  dependency of this one.
- **The browser stays open for the whole document** (developer, 2026-11-21: *we should only be closing a
  page or browser if the test calls for it. Any automation that does this when the test doesn't require it
  at best risks adding noise, at worst creates a workflow that does not exist in the real world.*). Today
  every `capture-page.csx` command starts and closes its own browser, and each close makes the application
  log #445's `WebSocketException`s, which is what made step 6's count depend on timing. With one browser
  opened in setup and closed in cleanup, no close falls between step 6's two readings, so the totals
  measure only the repair, and #445 shows only in its own tests. The `capture-page.csx` change belongs to
  #505, so this document waits for #505 as well as #506.

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

**Status:** ✅ Done

`.editorconfig`: the new partial `tests/Quotinator.Api.Tests/Solution/RepositoryStructureTests.FailingSteps.cs`
added to both lists. `AutomatedTestingSuiteTests.cs` was already in both.

### 3. Write the two guards and confirm them red

**Status:** ✅ Done

**Both halves, answered 2026-11-21.** The suite's convention (`RepositoryStructureTests.LoadSuiteDocuments`'
remarks, #339 step 13) gives every guard a rule half beside its live half, and the first draft of this plan
named only the live half. The rule half: each check is a method on `AutomatedTestingSuite`, proven in
`AutomatedTestingSuiteTests` against supplied documents, one that breaks the rule and one that does not, so a
check that matched nothing could not pass. That proof outlives step 6, when the real documents stop being red.

The live half, in `RepositoryStructureTests.FailingSteps.cs`, reading through `AutomatedTestingSuite`'s
existing loaders:

- `AutomatedTestDocuments_CarryNoFullyGreenAfterHeader`: every `.md` under `docs/automated-testing/`,
  the README included, so the template line is covered. Red today on seven documents and the README.
- `AutomatedTestDocuments_NeverExcuseAFailingStep`: the phrases decision A settles, in every `Expected`
  and `On failure` paragraph. Red today on at
  least `database-lifecycle/03`, `notifications-and-changelog/01` and `import-and-staged-actions/01`.

Each failure message names the document, the step and the matched text.

**Done 2026-11-21.**

- **Rule half:** `AutomatedTestingSuite.MentionsOfFullyGreenAfter` and `.StepsThatExcuseAFailure`, with
  twelve tests in `AutomatedTestingSuiteTests`. A paragraph runs from its `Expected`/`On failure` label to
  the next blank line, heading, fence or label; phrases match as whole words. **Red first:** with both rule
  bodies disabled, the five tests expecting a finding failed and the seven expecting none passed; with the
  bodies restored, all twelve pass.
- **Live half, red on today's documents.** `AutomatedTestDocuments_CarryNoFullyGreenAfterHeader`: 13 lines,
  the README's section and template line and every mention in the seven documents with the header.
  `AutomatedTestDocuments_NeverExcuseAFailingStep`: 14 lines, the expected excuses in
  `database-lifecycle/03` step 8, `notifications-and-changelog/01` step 9, `import-and-staged-actions/01`
  step 8 and `17` step 2, plus six the developer ruled real traps rather than false hits (step 6's further
  findings).
- **#503's own *Expected tests* table** was updated through an approved `gh issue edit`: the second test
  is renamed `AutomatedTestDocuments_NeverExcuseAFailingStep`, and a row names the rule-half tests.

### 4. Write ADR 029

**Status:** ✅ Done

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

**Done 2026-11-21.** Written as planned, indexed in the ADR README and added to `Quotinator.slnx`. Its
Rejected section also names recording a known failure and carrying on, and its Consequences name the two
guards and the rule tests. `EveryAdrIsIndexed` passes.

### 5. Rewrite the README and point the workflow documents to the ADR

**Status:** ✅ Done

- `docs/automated-testing/README.md`: remove *A document may need more than one issue before it can be fully
  green* and the template's `Fully green after` line; add a short section naming the failing-step shapes and
  pointing to ADR 029. The `**On failure:**` guidance gains that it never tells the reader to carry on.
- `testing-policy.md`, `release-verification.md`, `process.md`, `checklist.md` and `CLAUDE.md`: grep each for
  T2 reporting; where one states or contradicts the rule, reduce it to a pointer to ADR 029.
- `AutomatedTestingSuiteTests`: the four samples named above drop `Fully green after`.

**Done 2026-11-21.**

- **README:** the exception section is replaced by *A failing step is reported as failing*, which points to
  ADR 029, names the four failing-step shapes, and says a document that cannot be green yet stays red and an
  intermittent step is made deterministic. The template's `Fully green after` line is gone, and the
  `On failure:` guidance says it never names a failure as known and tells the reader to carry on.
- **Workflow documents:** none restated or contradicted the rule; none said where it applies either. One
  pointer each: `release-verification.md` (T2 is recorded ✅ only when every step in scope passed),
  `process.md` Implementation step 3, `checklist.md` *Implementing an issue*, and `CLAUDE.md` Pre-Push
  item 6. `testing-policy.md` names no T2 result rule and is unchanged.
- **Fixtures:** `AutomatedTestingSuiteTests`' fixture index uses a made-up `**Needs network:**` field as its
  optional header, so the template reader's coverage of an optional field survives; the three template tests
  and `TemplateElement`'s doc comment follow it.
- **Result:** every `Solution` test passes except the two live guards, which stay red until step 6.

### 6. Remove every allowance from the documents

**Status:** 🔄 In progress: every part is done except `startup-and-degradation/06`'s log counts, which wait for
#505 and #506.

**Done 2026-11-21, except that part.**

- **Headers and carry-on notes removed**, each *On failure* now stating what the failure means and stopping:
  `database-lifecycle/03` step 8 (#468), `notifications-and-changelog/01` step 9 (#472),
  `import-and-staged-actions/14` (#400), `16` and `17` (#347), `startup-and-degradation/06` (#445) and
  `07` (#462).
- **`import-and-staged-actions/01` step 8** asserts `--expect 404` for the batch that does not exist.
- **`import-and-staged-actions/14` step 5** says it runs no command and so fails; its owner is #400.
- **`import-and-staged-actions/16` and `17`**: a missing evaluation line fails the step. `17` gains step 4,
  which renames the Source the `Star Wars` alias resolves to through a `sources[]` correction and requires
  the reseed to report the alias stale; its *Canary* names step 4 as the control.
- **`startup-and-degradation/07`**: *wait and run it again* is gone; step 2's dependence on GitHub makes it
  failing until #462.
- **Three excuses the word list could not see**, found while editing: `database-lifecycle/02` step 7 (*The
  exception count fails today, and is tracked*, with later steps run past it), `startup-and-degradation/03`
  step 1 (*Destroy the container and re-run*), and `startup-and-degradation/06` step 6 (*`/about` reading `1`
  is not a failure*, against a command asserting `0`). Each now states the failure and stops.
- **The guard's further findings**: `startup-and-degradation/03` step 3's scope sentence moved to a new
  *Explicitly not covered here*; `startup-and-degradation/06` step 5's screenshot sentence gone, step 6's
  unasserted *Continue* claim removed and its button readings pointed at the command that asserts them.
- **`startup-and-degradation/06`** now declares `Deterministic: no` and names step 6's log count as what
  varies, until #503 converts it.
- **`api-surface/06` rewritten** to the answered design: five steps over three containers, every request made
  by `http.csx` with a cookie the test saved itself, each asserted on the response's antiforgery cookie, the
  log read only for the pair (#505's exception). `scripts/testing/http.csx` gains `--cookies-out` and
  `--cookie-file`, turning the handler's own cookie store off when either is used, since that store ignores a
  hand-set `Cookie` header.
- **Result:** both guards pass, and every `Solution` test passes (141).
- **Still to do:** `startup-and-degradation/06`'s three log counts, replaced by #506's exception totals read
  with one browser kept open (#505), once both land.

**`api-surface/06`, answered 2026-11-21:** decision G's variants were counted from the log, which #505
classes as secondary evidence. Developer: *if it has to validate a cookie then it needs a cookie to
validate.* The document is rewritten so the test brings its own cookies, with no browser:

- It requests a page from a container on the shared key ring and one with its own key ring, saving each
  response's cookie with `http.csx --headers-out`.
- Three cases, each one request with a cookie the test chose, asserted on the response's `Set-Cookie`: no
  cookie issues a new one; the shared-ring cookie is not replaced; the own-ring cookie is replaced.
- The log count stays only for *logged only then*, which is this document's subject and so #505's
  exception: `2` lines in the third case, `0` in the other two.
- `scripts/testing/http.csx` gains `--cookie-file`, sending a cookie saved by an earlier request.
- With no browser, nothing is left behind for another test, so step 4's *Do not continue with the suite*
  goes with it.

For each row of the table above: delete the header, delete the carry-on text, and leave the step asserting
what the correct behaviour is, so it reads red until its owner lands:

- `database-lifecycle/03` step 8, `notifications-and-changelog/01` step 9: *On failure* states what a failure
  means and stops, like every other step.
- `import-and-staged-actions/01` step 8: the unknown-batch apply asserts `--expect 404`, the response #469
  specifies.
- `import-and-staged-actions/14` step 5: no command, so red per ADR 029, with its *Canary* note rewritten.
- `import-and-staged-actions/16` and `17`: the evaluation-line readings are failures until #347, not
  inconclusive.
- `startup-and-degradation/06` steps 5 and 6 and its cleanup: screenshots and log counts replaced per
  decision F, step 6 reading the exception totals endpoint before and after the repair. Red on every run
  until #445. Its `Deterministic:` field then tells the truth.
- `startup-and-degradation/07` step 2: red until #462 makes it deterministic; its header goes.
- **The guard's further findings (developer, 2026-11-21: *the guard is doing its job and highlighting
  problems and potential traps; be more careful and explicit in tests, and prefer scripts when you need to
  test for conditions*).** The eight phrases stay. Every sentence the guard reports is rewritten so the
  condition it describes is asserted by a command, or the sentence leaves the `Expected`/`On failure` text:
  - `api-surface/06` step 4, *Do not continue with the suite until it reads `0`*: a leftover cookie that
    affects later tests is a dependency between tests. The step stops on failure like any other, and the
    document's own cleanup removes the cookie.
  - `startup-and-degradation/03` step 3, *What the database holds is not asserted*: a scope statement,
    moved under *Explicitly not covered here*.
  - `startup-and-degradation/06` step 5, *Both screenshots record it*: the screenshot is not the
    assertion; the sentence goes. Developer, 2026-11-21: *screenshots are the same potential trap that
    reading logs or audit trails is. They are secondary evidence of actions that we likely can trace in
    code during unit tests and in REST endpoints.* Answered 2026-11-21: #505, in v1.9.0, records the
    principle in an ADR and applies it across the suite.
  - `startup-and-degradation/06` step 6, three mentions of the *Continue* button: where the prose restates
    a value a command asserts, it names the command instead; where it states something no command checks
    (*`/rest-api` is where the modal's Continue lands*), a page-script assertion is added or the claim goes.
- `api-surface/06` (decision G): step 1's unasserted first visit becomes two asserted variants, a fresh
  browser expecting `0` lines and a cookie from another key ring expecting `2`. Its `Deterministic:` field
  becomes `yes` and its *Determinism* section loses *What varies*.
- `database-lifecycle/02`: *Observed effect* records step 7 as failing, without the *until #404 lands*
  allowance.

Each document's Knowledgebase links stay; they describe the bug, not an excuse. The two guards turn green
here.

**`import-and-staged-actions/17`'s missing positive step: #503 adds it, answered 2026-11-21.** Its *Canary*
says no step makes an alias genuinely stale, so a correct build and a staleness check that never runs read
the same, and the removed header was what excused that. No open issue owned it (#347 makes the evaluation
observable but adds no such step). A new step 4, modelled on `16`'s step 4, renames the Source an alias
resolves to, reseeds, and requires the alias to be reported stale; the document's *Canary* then names it as
the control. Under the stop rule it runs only once steps 2 and 3 pass, after #347.

**A failing step stops the run, answered 2026-11-21.** With the excuses gone, `database-lifecycle/03`,
`notifications-and-changelog/01`, `import-and-staged-actions/01` and `14` stop at their failing step on every
run until the owner lands. Developer: *steps in a test tend to rely on the outcomes of a previous step, so we
cannot continue such a test past the failing step. If we could continue, those steps can't possibly be relevant
to the test.* The README's stop rule stands, and each rewritten *On failure* says stop. The old *nothing below
reads this step* was wrong where it was checked: `database-lifecycle/03` step 8 proves `/version` counted the
import before the Reset, which is what gives the later `0` after the Reset its meaning.

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
| 1 | ✅ | No document under `docs/automated-testing/`, the README's template included, carries `Fully green after` | Unit test | `RepositoryStructureTests.AutomatedTestDocuments_CarryNoFullyGreenAfterHeader`, red before step 6 |
| 2 | ✅ | No document excuses a failing step, in its `Expected` or its `On failure` text | Unit test | `RepositoryStructureTests.AutomatedTestDocuments_NeverExcuseAFailingStep`, red before step 6 |
| 3 | ✅ | ADR 029 records the rule, and is indexed | Unit test + Live | `RepositoryStructureTests.EveryAdrIsIndexed` passes; `Select-String -Path docs/architecture-decisions/029-*.md -Pattern 'blocks every issue'` returns a match |
| 4 | ✅ | The README drops the exception and points to ADR 029 | Live | `Select-String -Path docs/automated-testing/README.md -Pattern 'more than one issue before it can be fully green'` returns nothing; `-Pattern '029-a-failing-t2-step-blocks-the-issue'` returns a match |
| 5 | ✅ | The README names every failing-step shape: no command, unasserted, intermittent | Live | `Select-String -Path docs/automated-testing/README.md -Pattern 'runs no command','without asserting','intermittent'` returns all three |
| 6 | ❌ | Each changed document fails at its owned step and nowhere else, and `06` step 6 reads the same on three runs | Live | Step 7's record, one line per document |
| 7 | ❌ | Every issue at *Waiting for release* is audited, and each set back carries the change in its plan, `overview.md` and its GitHub Definition of done | Live | Step 8's table; for each issue set back, `gh issue view <N> --json body -q .body` shows the unticked boxes, and its plan header and overview row agree |
| 8 | ❌ | #490's step 8 records the removal in `86eb15fd` | Live | `Select-String -Path docs/milestones/notification-system/490-layered-database-check-plan.md -Pattern '86eb15fd'` matches inside step 8 |
| 9 | ❌ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal -m:1`, 0 warnings, 0 errors |
| 10 | ❌ | #469 and #472 are in *Notification system*; the other five owners are named as blockers | Live | `gh issue view 469 --json milestone -q .milestone.title` and the same for 472 print `Notification system`; `overview.md`'s dependency map names #347, #400, #404, #445, #468 |
| 11 | ❌ | `api-surface/06` asserts both cookie variants, `0` lines without a prior cookie and `2` with one from another key ring, and declares itself deterministic | Live | Step 7's record shows both variants asserted; `Select-String -Path docs/automated-testing/api-surface/06-*.md -Pattern '^\*\*Deterministic:\*\* yes'` matches |
| 12 | ❌ | T2 passes: the smoke set and every changed document, every step green | Live | Step 12's record |
| 13 | ✅ | Both rules report a document that breaks them and pass one that does not, independently of today's documents | Unit test | `AutomatedTestingSuiteTests.MentionsOfFullyGreenAfter_*` (4) and `.StepsThatExcuseAFailure_*` (8): 12 pass; red with the rule bodies disabled (step 3) |
