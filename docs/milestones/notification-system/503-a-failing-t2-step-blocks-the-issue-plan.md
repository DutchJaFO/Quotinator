# #503: A failing T2 step blocks the issue: remove the "Fully green after" exception and audit the issues it let through

**Status:** In progress (step 6)
**GitHub issue:** #503
**Tiers required:** T2
**Depends on:** #347, #400, #404, #438, #445, #462, #468, #469, #472. Blocks #457, #490.

---

## Next action

**Finish step 6. Nothing after step 6 is worked on until it is done,
step 7 included** (developer, 2026-11-22). Decision H's move is done (`81423d97`). What is left of step 6 is
running every document it changed that has not run since, listed at the end of the step, then the
developer's review of the step: decision F's screenshots are
gone, and its log counts stay as the workaround its amendment accepts. Once step 6 is done, step 7 runs every
document still to run: the six decision H changed or added, and `06` three times. Steps 1 to 5 are done. Steps 8, 9 and 10 are not started: they should not have been begun while
steps 6 and 7 were open, but changes were made under them on 2026-11-21 and 2026-11-22, and those stay in
place. Each step, when it starts, reviews them against its requirement and finishes them; step 10 then goes
to the developer's review. Then step 11, the full build and test run. Step 12, this issue's
T2, waits for every owner (decision D), and step 13 follows it. Decisions A to G were all answered on
2026-11-21, and no question is open. The comment on #438 that decision G calls for was posted the same day. The plan is built against
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

**Amended 2026-11-22.** #503 made the `capture-page.csx` change itself (`c683381c`), so `06` no longer waits for
#505. And the log count stays: *given that we have a fix planned we can accept this as a current work-around*
(developer). #506 replaces it with the exception totals when it lands; until then `06` reads the log, and #503
no longer waits for #506 either.

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

### H. A smoke document checks one thing; a check of another feature is its own document

**Decided 2026-11-22, by the developer, after step 1.** *Smoke tests should alert us to problems while being
efficient. The additional tests would be done in a full run.* And: *testing the effect of an import on other
endpoints should be a distinct test, as that allows us to pinpoint problems while keeping smoke tests
efficient. The full test would still indicate problems that the smoke test could have missed.*

Two smoke documents carry a check that belongs to another feature, and each makes its owner block every
issue's T2:

- **`import-and-staged-actions/14` step 5** compares each bundled file with what its converter produces. It is
  its own test, not part of the zero-pending check: no other step uses its result. Bundled files are used in
  tests only to verify that they work; they are a moving target, so no repeatable test rests on them. Doc 14
  exists to warn when upstream content has made the import fail or a rule stale. Step 5 moves to its own
  document, owned by #400. This supersedes #400's ruling of 2026-09-18 that doc 14 keeps its steps.
- **`database-lifecycle/03`'s `/version` reads** test what an import and a Reset do to another endpoint, the
  same mistake. *Reset functioning while version isn't accurately reporting the status is an acceptable
  result of a smoke test, because we know reset works*: Reset's own response and the list endpoints prove it.
  The `/version` reads, step 8 included, move to their own document, owned by #468. This replaces the reason
  step 6 records under *A failing step stops the run* for keeping step 8 in doc 03.
- **`import-and-staged-actions/14` step 2 and `19` step 1** read `/version` to prove the first-boot seed
  loaded content: the same problem, found while carrying out the move. *We use the endpoint results that did
  the action for primary results. Any additional endpoints are secondary sources that validate our primary
  results. Smoke tests only need to validate the primary result; a full test would have to test both*
  (developer, 2026-11-22). And: *these are two different tests: does the first-boot seeding work, and do the
  seed endpoints work as expected.* Doc 14 is the first; doc 19 is the second.
  - **Doc 14, first-boot seeding.** A first-boot seed runs at startup, not through an endpoint, so it has no
    response to read; what it reports as it runs, one `reseedFileApplied` notification per file with counts
    per entity type, is the only result it has. Step 2 reads that in place of `/version`.
  - **Doc 19, the seed endpoints.** Each step reads the response of the endpoint it calls. *We simply need
    the same files and same database state before we start either test* (developer, 2026-11-22), so the
    first-boot test and this one start alike: an empty database, and the same fixture. Doc 14 reads the
    bundled files, a moving target, so the first-boot test on owned data is a new document,
    `import-and-staged-actions/32`: a container starts on an empty database with the fixture in its imports
    folder, and the test reads the seed's own notification. Doc 19's container starts on an empty database
    with no files, the same fixture is copied in afterwards, and its steps call the seed endpoints. Its step 1
    loses the `/version` read, and the first-boot seed with it.
  - The `/version` reads go to the same document as doc 03's.
- **Every smoke document was then searched for the same pattern** (2026-11-22). Four read `/version`'s
  content counts: `database-lifecycle/03`, `import-and-staged-actions/14` and `19`, and
  `startup-and-degradation/06` step 6, whose `database.quotes=0` after the Reset is secondary to the Reset's
  own result notification, which the step already asserts. That read goes too.
- **Status is read from `/health`** (developer, 2026-11-22: *we should be using the health endpoint for
  status*), **and `/version` belongs in a test only where it tells the degraded state from the regular one, or
  where the version itself matters to the test** (developer, 2026-11-22). So `api-surface/01`'s version check
  stays, and so do the `503` while degraded and `200` once recovered in `startup-and-degradation/01`, `05`,
  `06` and `08` to `14`, and `startup-and-degradation/03`'s ready reading. `database-lifecycle/03`'s
  `status=ready` after Reset rode on its count reads and goes with them; that document reads `/health` there
  instead. An earlier reading of this bullet moved every one of those reads to `/health`; it was corrected
  the same day, before any document was changed but `06`, which was restored.

**Raised in the same answer, filed as #512** (*Blazor: Dashboard & Statistics*, 2026-11-22): `/version`
reports content statistics, which is not its primary function; the statistics get their own endpoint. Not a
dependency of this issue.

**What it does not change:** a full run, at the close of a milestone and at release, runs every document, so
the two new documents still hold this milestone's release until #400 and #468 land. They stop blocking each
issue's own T2.

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

**Status:** 🔄 In progress. The edits are done; the documents they changed are not all run. Still to do: run
each one not run since its change, listed at the end of this step, then the developer's review.

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
- **Since amended:** `startup-and-degradation/06`'s three log counts, to be replaced by #506's exception totals read
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
import before the Reset, which is what gives the later `0` after the Reset its meaning. Decision H replaces
this: `/version` is not evidence of the wipe, so its reads leave doc 03.

**Decision H's move, planned 2026-11-22.** Two new documents, each carrying the check it takes over unchanged
and red at that step until its owner lands, and two smoke documents that lose it:

- **New `import-and-staged-actions/31-bundled-files-match-their-converters.md`**, not smoke, *Fresh*, owned by
  #400. It takes doc 14's step 5 as it stands: its text, its *Expected* that no command runs and so the step
  fails, the paragraph saying `Quotinator:AutoUpdateSources` is off by default, and the rule that this is the
  one place allowed to go red because the outside world changed. Its *Determinism* section says what an
  upstream change does to it; its `Deterministic:` field is settled when #400 gives the step a command.
- **`import-and-staged-actions/14`** loses step 5. Steps 6 and 7 become 5 and 6, its *Canary* drops the step 5
  note, and its opening says it reads the bundled files to warn when upstream content makes the import fail or
  a rule stale. Step 2 reads the seed's own `reseedFileApplied` notifications in place of `/version`: every
  bundled file reports itself applied, with content.
- **New `import-and-staged-actions/32-a-first-boot-seed-reports-what-it-loaded.md`**, smoke, *Fresh*: the
  fixture in `{dataDir}/imports/` on an empty database; after health, the fixture's `reseedFileApplied`
  notification, each entity type with the count the fixture gives it. It is in the smoke set because a
  container that cannot seed on first boot does not fundamentally work.
- **`import-and-staged-actions/19`** starts its container on an empty database with no file in its imports
  folder, copies the fixture in after health, and runs its endpoint steps from there. Step 1 loses its
  `/version` read; its *Observed effect* row for `/version` goes with it, and its reseed reading is measured
  again from the empty start.
- **`startup-and-degradation/06`** step 6 loses its `/version` read and `database.quotes=0`; the Reset's result
  notification it already asserts is the primary result.
- **New `database-lifecycle/05-version-counts-follow-the-content.md`**, not smoke, *Fresh*, owned by #468: the
  secondary check of a seed, an import and a Reset. Doc 03's fixture, seeded in one container and imported
  through the API in another, `/version` held to the fixture's counts after each and to `0` after a Reset. It takes step 8's command,
  *Expected*, *On failure* and Knowledgebase link, and doc 03's *Observed effect* record of step 8. It stops at
  the import's `/version` read until #468.
- **`database-lifecycle/03`** loses every `/version` read: in steps 2 and 4 (seeded), step 8 entire, and in step
  10 (imported). Steps 9 to 12 become 8 to 11, with every reference to them. Its *Determinism* drops the bullet holding
  `/version` to the list endpoints; Reset's own response, asserted to `0`, and the list endpoints remain its
  evidence.
- **Guards**, each confirmed red by the document change before it is changed:
  `RepositoryStructureTests.TheResetDocument_CoversEveryContentCategory` stops requiring `/api/v1/version` in
  doc 03 (`ResetDocumentOtherReads`), and
  `SourceRefreshDocuments_SayTheRefreshIsOffByDefault` names doc 31 where it names doc 14. Both files join the
  IDE0008/IDE0090 scoped lists if they are not already in them.
- **Index and solution**: the README's `import-and-staged-actions/` and `database-lifecycle/` tables list the
  two documents; its bundled-content rule names doc 31 beside doc 14; both are added to `Quotinator.slnx`.
- **#400 and #468**: item 6 of #400 and #468's test table point at the new documents. Each edit is drafted,
  pasted in full and approved before `gh issue edit` runs.

The records that name doc 14 step 5 or doc 03 step 8 as a smoke step (`overview.md`, the set-back plans) are
corrected by steps 8 and 10, which have not started.

**One browser for the whole document, done 2026-11-22** (developer: *we fix that tool now*). `06` step 6's
log count varies because every `capture-page.csx` command starts and closes its own browser, and each close
logs #445's errors on either side of the count's mark. Decision F gave the fix to #505; it is made here instead,
so `06` no longer waits for #505:

- `capture-page.csx -- --session <file> --open` starts a headless browser that outlives the command and
  records its port, process and profile in the file. `--session <file> --close` asks it to close, removes its
  profile and the file.
- A capture given `--session <file>` opens a new tab in that browser and leaves it open: no page closes, and
  no page navigates away, until the document's cleanup. A tab that produces no frames is left open and another
  tab is opened, never a new browser.
- `06` opens the session in its setup, passes it to every capture, and closes it in its cleanup.
- Proved by step 7: `06` step 6 reads the same on three runs. #505's issue body records that #503 made the
  change, through an approved edit.
- **Done:** the three options, run against headless Edge: `--open` returned in 2 s; two captures each opened a
  tab and left it open; `--close` ended the browser and removed its profile and the session file; a capture
  without `--session` still starts and closes its own browser. `06` opens the session in step 1, gives it to all
  ten captures, and closes it in the cleanup after the last count. Every `Solution` test passes (140). #503 no
  longer depends on #505; #445 stays until step 7's three runs show its errors no longer reach the count.

**A capture without a screenshot, done 2026-11-22** (developer: *we fix that tool now; do verify that the
tests that use the tool keep working*). Decision F removes `06`'s screenshots, since its page scripts assert
what the pages show, but `capture-page.csx` always writes one:

- `--out` becomes optional. Without it the command runs its page script, prints and writes the result, and
  takes no screenshot; with neither `--out` nor a page script it refuses, having nothing to do.
- `06`'s ten captures lose `--out`.
- Every test document that uses the tool and can be run as written is run against a build of this branch, and
  each reads as it did before the change.
- **Done:** `capture-page.csx` takes `--out` as optional, and `06`'s ten captures lose it.
- **Not verified.** Two runs of these documents on 2026-11-22 are withdrawn: both went through a PowerShell
  runner of the assistant's own, which [ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md)
  rules out and the T2 README's *never worked around at run time with a wrapper or a runner of one's own*
  forbids. It also changed every command, by redirecting its stderr, and the first run's logs held none. The
  documents are run again, each command as written: `startup-and-degradation/06`, `01` and `05`,
  `notifications-and-changelog/14` and `01`, and `backup/06`, `07` and `08`. `notifications-and-changelog/15` and
  `16` use the tool but carry PowerShell blocks, so they are not run until #473 converts them.
- **Run again, 2026-11-22**, against `quotinator:local` built from the working tree on `c683381c`, each
  command as written, one at a time, reading each result before the next:
  - `startup-and-degradation/06` passes every step and its cleanup. Its log names one browser, opened in
    step 1, every capture a new tab in it, and its close in the cleanup; no PNG is written.
  - `startup-and-degradation/01` and `05`, `notifications-and-changelog/14` and `backup/06`, `07` and `08`
    pass every step and their cleanups.
  - `notifications-and-changelog/01` fails at step 9's second command, `reseedrecommended` missing, #472's
    failure; step 10 is not run, as the step says, and the cleanup is.
  - **Deviations, so those three runs are withdrawn.** `06`'s step 3 seven reads were sent in one batch, so
    may have run concurrently rather than in order. Two read-only commands were changed by a pipe the
    assistant added (`05`'s step 6 `json --contains`, `backup/08`'s step 5 notifications read). Running the
    changed command again is not enough (developer: *any changes could result in tests that have different
    behaviour than expected, which contaminates results*), so each document is run again in full.
  - **Run again in full, as written, 2026-11-22:** `startup-and-degradation/06` and `05` pass every step and
    their cleanups, with no deviation. `backup/08`, after its renumbering below, passed steps 1 to 10 and was
    stopped after step 11's first command, its cleanup run; that run is incomplete and is not a reading. A
    further start was abandoned after step 2, when two of its commands were sent in one message, so might not
    have run in order. Run again from step 1, 2026-11-22, each command alone: `backup/08` passes every step and
    its cleanup, with no deviation.
  - **Seen, not failures.** `capture-page.csx` started a second browser twice in `14`'s step 2, after the
    first produced no frames, its built-in retry. `notifications-and-changelog/01`'s cleanup passes `--bind`
    with no path, so `destroy` runs `docker volume rm` against a volume that container never had.
    `backup/08` numbers a step `6b`, against the README's template of whole-number steps.
- **`backup/08`'s step 6b, fixed 2026-11-22** (developer: *Yes, under #503*). It came in with `d8ba09da`
  (#348), inserted after step 6 without renumbering. Steps `6b` and `7` to `10` become `7` to `11`, with every
  reference inside the document; the Observed effect table notes the rows were measured under the old numbers.
  The #348 plan's records citing `backup/08` step 9 are left as written. New rule
  `AutomatedTestingSuite.StepsNotNumberedConsecutively`, with seven rule tests, and the live guard
  `EveryAutomatedTestingDocument_NumbersItsStepsConsecutively`, which failed on `backup/08` alone before the
  renumbering and passes after it. Every `Solution` test passes (148).
- **#505's requirement 3** now records that #503 made `--out` optional and that what remains there is
  removing the other documents' screenshots, an approved edit applied 2026-11-22 and read back.
- **`notifications-and-changelog/15` and `16` converted to single commands, 2026-11-22** (developer: *we
  convert now, but we do not tamper with 473*). They use `capture-page.csx`, which this step changed, so this
  step cannot be done until they run; and they could not run while written in PowerShell. Listing #473,
  which converts them, as a dependency would have closed a cycle: #473 depends on #327, #327 on #482 and
  #457, whose T2 #503 blocks. #503 converts these two itself, and #473 is left as it is, since editing it
  would claim part of an issue done before it starts.
  - **Tooling, red first:** TestProbe gains `log --sum-out`, the sum of a number the log reports, and
    `json --distinct-length-equals-json`, a count of distinct values; ten tests failed to compile before
    either existed, and pass after. Two further tests pin `--where <array>:<field>=` matching an empty value,
    behaviour that already existed, so they were green from the start; they are the positive control for
    `16`'s empty-body reading. `notification-rows-by-kind.js` adds `innerHeight` and a per-kind `emptyTitle`,
    fields no earlier reading uses.
  - **Run as written, 2026-11-22:** `15` passes every step and its cleanup. `16`'s first run was abandoned at
    its cleanup, two independent commands having been sent in one message; run again from step 1, each
    command alone, it passes every step and its cleanup. Every `Solution` test (148), every TestProbe test
    (230) and the full `dotnet test --configuration Release -m:1` pass, 0 warnings.
- **Still to do:** run, each command as written, every other document this step changed and has not run
  since: `database-lifecycle/02`, `03` and `05`; `import-and-staged-actions/01`, `14`, `16`, `17`, `19`, `31`
  and `32`; `startup-and-degradation/03` and `07`; and `api-surface/06`.

**Decision H's move, done 2026-11-22** (`81423d97`):

- **New documents.** `import-and-staged-actions/31` (not smoke; doc 14's step 5 as it stood, red at its only
  step, #400). `import-and-staged-actions/32` (smoke; the fixture seeded at first boot, step 2 reading the
  `reseedFileApplied` notification's ten counts). `database-lifecycle/05` (not smoke; doc 03's fixture seeded in
  one container and imported in another, `/version` held to the fixture's counts after each and to `0` after a
  Reset; step 5, the imported counts, fails until #468). Doc 05 compares `/version` with the fixture's counts
  rather than reading the list endpoints again: the fixture decides them, and `03` already reads the rows.
- **Doc 14:** step 5 gone, steps renumbered; step 2 holds the seed's `reseedFileApplied` notifications to the
  number of files the seed preview lists, a count of files that moves with the configuration and never with a
  source's size; its opening, *Determinism*, *Canary* and *Observed effect* follow.
- **Doc 19:** the container starts on an empty database with no file; step 1 copies the fixture in after health.
  Its `/version` read and its row in *Observed effect* are gone.
- **Doc 03:** every `/version` read gone, step 8 included; steps 9 to 12 are 8 to 11; after each Reset `/health`
  is read for status; *Determinism*, *Observed effect* and *Canary* follow.
- **Doc 06:** step 6's `/version` count read gone; its degraded and recovered `/version` reads stay, per the
  corrected bullet in decision H.
- **The probe reads a path into text holding JSON**, so doc 32 asserts each count rather than matching text:
  `JsonPathReader` continues into a string holding a JSON object or array. Red first:
  `TryRead_PathIntoAStringHoldingJson_ReturnsTheInnerField` failed before the change, and all 218 probe tests pass
  after it. `--where` cannot narrow an array inside such text, so doc 32 reads the entries by position; the seed
  writes them ordered by type name.
- **Guards**, each red on the document change first: `TheResetDocument_CoversEveryContentCategory` no longer
  requires `/api/v1/version` in doc 03, and `SourceRefreshDocuments_SayTheRefreshIsOffByDefault` names doc 31
  in place of doc 14. Both files were already in the IDE0008/IDE0090 scoped lists, as were the probe's two.
- **Index and solution:** the smoke set is twenty-one, with doc 32; the category tables list docs 05, 31 and 32;
  the bundled-content rule names doc 31; the awaiting-measurement list gains 31 and 32 (37 of 88); all three are
  in `Quotinator.slnx`.
- **Result:** every `Solution` test passes (140).
- **#400 and #468**, edited after approval and read back: #400's ruling that doc 14 keeps its steps is marked superseded for step 5, its item 6 and test row name doc 31; #468's test row names doc 05's step 5.

### 7. Confirm the changed documents read red where they should

**Status:** 🔄 In progress, and waiting: nothing more is run until step 6 is done (developer, 2026-11-22). Then
the six documents decision H changed or added are run, and `startup-and-degradation/06` three times.

The red-first run of this issue's change (`process.md` → Implementation, step 1): each document from step 6
against a build of this branch, under `test-env.csx`. Recorded per document: the step it fails at, and that
no other step fails. A failure anywhere else is reported in its own right. `06` step 6 runs three times, to
show its reading no longer varies.

This is not this issue's T2. That is step 12, and it cannot pass before the owners land (decision D).

**Still to run, for decision H's move:** `import-and-staged-actions/14`, `19` and `32`, and `database-lifecycle/03` after the
move, expected to pass every step; `import-and-staged-actions/31`, expected to fail at its only step, which
runs no command (#400); and `database-lifecycle/05`, expected to fail at the imported container's `/version`
read (#468) and nowhere before it.

**Run 2026-11-21** against `quotinator:local` built from `4e051906`, every document as written, stopping at its
first failing step:

| Document | Reading | Owner |
|---|---|---|
| `api-surface/06` | every step passes; cleanup leaves no container | none |
| `database-lifecycle/02` | steps 1 to 6 pass; step 7 fails, `2` exceptions where `0` is asserted | #404 |
| `database-lifecycle/03` | steps 1 to 7 pass; step 8 fails, every `/version` count `0` | #468 |
| `import-and-staged-actions/01` | steps 1 to 7 pass; step 8 fails, the unknown batch answers `200` where `404` is asserted | #469 |
| `import-and-staged-actions/14` | steps 1 to 4 pass; step 5 fails, it runs no command | #400 |
| `import-and-staged-actions/16` | steps 1 and 2 pass; step 3 fails, no evaluation line (`reportFiles=5 staged=0`, `stale=0`) | #347 |
| `import-and-staged-actions/17` | step 1 passes; step 2 fails, no evaluation line (`5` report lines, `5` seed batches applied, `795` records, `pending=0`, `stale=0`) | #347 |
| `notifications-and-changelog/01` | steps 1 to 8 pass; step 9's Reset result passes and its reseed recommendation fails | #472 |
| `startup-and-degradation/03` | every step passes on this run: the requests landed inside the window, by chance | #438 |
| `startup-and-degradation/06` | every step passes on this run, including the timing-dependent count | conversion by #503 after #505, #506 |
| `startup-and-degradation/07` | every step passes on this run: GitHub answered | #462 |

`startup-and-degradation/03`, `06` and `07` each passed by chance on this run, and each stays a failing step
under ADR 029 until its owner makes it deterministic. No document failed anywhere but its owned step.

**`import-and-staged-actions/17`'s new step 4, run on its own** (steps 1 then 4, outside the stop rule, to
prove the step itself works): the first draft failed twice, both now fixed in the document. An import with no
quotes is refused (`File contained no quotes`), although the schema allows an empty list, so the fixture
carries one new quote on `Airplane!`. A Source's title is decided as `sourceTitle`, not as the `title` its
`ambiguousFields` names. As written, it then read `canonicalSources=1`, `sourceActions=1 ambiguous=title`, the
renamed title, `reportedStale=6` and `stale=6`, six quote `Modify` actions `Stale`.

**Runner limit, recorded so it is not misread:** the loop that ran the single-command documents runs fenced
commands and checks exit codes. It found nothing to run in `14`'s step 5 and continued; under the stop rule
that step fails and the reading ends there, as the table states. `startup-and-degradation/03`'s first command
is expected to exit `1`, so it was rerun with that exit accepted.

### 8. Audit the issues at *Waiting for release*

**Status:** ⬜ Not started. It cannot start before steps 6 and 7 are done (developer, 2026-11-22). The audit
was carried out before then, on 2026-11-21, and its changes stay in place: the 40 plan docs, `overview.md` and
33 GitHub Definitions of done, recorded below. This step starts by reviewing them against the documents as
they read once step 6 is finished, which can change which are red, and corrects what no longer holds.

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

**Changes already in place, made 2026-11-21 before this step could start.** `overview.md` listed 40 issues at *Waiting for release* with `T2 ✅`, plus #490 already
set back; #313 (no tiers), #83 (`T2 ⬜`) and #383 (T1 only) carry no T2 record and are not in the audit. Every
one of the 41 is set back: its T2 scope includes the smoke set, and six smoke documents fail today
(`database-lifecycle/03` #468, `import-and-staged-actions/01` #469 and `14` #400,
`notifications-and-changelog/01` #472, `startup-and-degradation/03` #438 and `06` #503 after #505 and #506).
Each plan was read for the documents its own T2 ran beyond the smoke set, by path and by title:

| Issues | Red documents in scope beyond the smoke set | Owners added beyond the smoke set's | Verdict |
|---|---|---|---|
| #312, #81, #302, #303, #307, #308, #309, #319, #323, #326, #349, #350, #367, #369, #370, #409, #410, #413, #419, #422, #423, #397, #371, #372, #373, #374, #375, #376, #377, #378, #382, #389 | none | none | set back |
| #304 | `notifications-and-changelog/03`, named in its *Expected tests* | #501 | set back |
| #305 | `database-lifecycle/02`, `notifications-and-changelog/03`, both run | #404, #501 | set back |
| #339 | `import-and-staged-actions/16`, `17`: its step 1 retired the #347 dependency in favour of the header ADR 029 removes | #347 | set back |
| #348 | `import-and-staged-actions/16`, `17` (step 33), `backup/03`, `04` | #347, #502 | set back |
| #411 | every document (steps 14 and 15): `import-and-staged-actions/16`, `17`, `database-lifecycle/02`, `notifications-and-changelog/03`, `backup/03`, `04` | #347, #404, #501, #502 | set back |
| #420 | `import-and-staged-actions/16`; row 17 cites its step 4, which the stop rule never reaches | #347 | set back |
| #424 | `startup-and-degradation/07`, which it wrote | #462 | set back |
| #482 | `backup/03`, `04` | #502 | set back |
| #490 | as recorded in `5bfb43f6`, plus `startup-and-degradation/03` | #438 added today | set back earlier |

What each set-back changed:

- **Plan doc**, all 40: header `In progress (step N)` naming its T2 step, or `In progress (T2)` where the plan
  has none; `Depends on:` names the owners; the T2 step's status reads ❌, keeping the earlier record after it;
  a *Next action* block states the wait, with the earlier text kept under *Written at Waiting for release*.
  A row that claimed the T2 scope passed goes ❌ (#81 row 8, #411 rows 12 and 14, #420 rows 17 and 20, #422
  row 9, #423 row 20); every other plan gains a ❌ row *T2 passes*.
- **`overview.md`**: each row reads `In progress` and `T2 ⬜`; its dependency-map line and its order-of-
  operations row name the owners. #490's and #457's gain #438.
- **GitHub Definition of done**, run 2026-11-21 after approval, each edit checked to differ in exactly the
  expected lines and read back: *No regression in related tests* unticked on 33 issues (#312, #302, #304,
  #307, #308, #309, #305, #319, #323, #326, #339, #348, #349, #350, #369, #370, #409, #410, #411, #413, #419,
  #420, #422, #423, #397, #371, #374, #376, #378, #382, #389, #482, #490), and on #304 also *All expected tests
  pass (green)*, since its *Expected tests* names `notifications-and-changelog/03`. #81, #303, #367, #424,
  #372, #373, #375 and #377 had no ticked box resting on T2. The approved draft headed itself *32 issues*
  while listing 33; the 33 listed were edited.

**Decision G's two documents, classified:** `api-surface/06` is rewritten deterministic and passes (step 7),
so it blocks nothing. `startup-and-degradation/03` stays failing until #438 rewrites it, and #438 is an owner of
every issue above. #423's row 20 is that same allowance, covered by the same owner.

### 9. Correct #490's step 8

**Status:** ⬜ Not started. It cannot start before steps 6 and 7 are done (developer, 2026-11-22). #490's plan
was corrected before then, on 2026-11-21, and the correction stays in place, recorded below; this step
starts by reviewing it.

Its *Knowledgebase* bullet records adding `the-database-could-not-be-checked.md` and
`KnowledgebaseLinks.TheDatabaseCouldNotBeChecked`. Both were removed in `86eb15fd`; the bullet says so, and
names the commit.

**Change already in place, made 2026-11-21 before this step could start**, while step 7's image built. The bullet was wrong in a second way: it said the entry was
linked from `CheckInconclusive`, which `86eb15fd`'s parent shows carried no link; it was linked from a
`NotEnoughSpace` reason. The bullet now records that this step added the entry, the constant and that reason,
that `86eb15fd` removed all three when the check stopped needing a working copy, and that `CheckInconclusive`
carries no link.

### 10. Update the dependency map

**Status:** ⬜ Not started. It cannot start before steps 6 and 7 are done (developer, 2026-11-22). Changes were
made to `overview.md` before then, and they stay in place, listed below; this step starts by reviewing them
against the requirement, finishes them, and runs verification row 14. It is done only when that row passes
and the developer has reviewed it. Those early changes were marked done three times without being complete:
in `1ad395cd`, when only the blocked issues' lines named their owners; in `959070dd`, with #457's and #503's
order-of-operations rows naming no blockers; and in `ab29679b`, with no verification row and this design
still describing five outside owners where seven are listed.

`overview.md`'s dependency map and order of operations record every owner from step 8 as a blocker of each
issue it blocks, in both directions: the owner's line lists the issue, and the issue's map line and order
row name the owner. Every row in the order of operations comes after each issue it depends on or is
T2-blocked by; where it cannot, this step records why, since `overview.md` carries status only. The owners outside this milestone, the five decision C
leaves where they are plus #505 and #506 from decision F, are named under the map as blockers from outside,
each with the step it owns and what it blocks. #469 and #472 were moved and listed during planning.

**Changes already in place, made 2026-11-21 and 2026-11-22 before this step could start:**

- **Dependency map, both directions.** Each of the 40 set-back issues' lines names its owners (step 8). Each
  owner in this milestone has a line naming the failing step it owns and every issue it blocks: #438, #469 and
  #472 (smoke steps) and #503 (`startup-and-degradation/06`) list the same 43 issues, the 40 plus #490, #457
  and #503 (#503's own line omits itself); #501, #502 and #462 list theirs. #469 and #472 had no line before.
- **Owners outside this milestone** are named in a table under the map: #400 and #468 (smoke steps), #347,
  #404, #505, #506 (v1.9.0) and #445 (*Startup and degraded experience*).
- **Order of operations.** #503 and the six owners in this milestone (#438, #469, #472, #501, #502, #462) come
  first; #469 and #472 were missing from it. Every blocked row names its blockers, #490's and #457's
  included. #503 stays first although #438, #469, #472 and #462 block its T2, because the rule has to be in
  place before anything else is judged; its row says that it runs first and closes after them.
- **Rows ahead of what they depend on.** Checking the order found three rows that came before an issue they
  depend on, out of order before this issue's first edit: #81 before #309 and #307, #302 before #372, and
  #373 before #372. The developer chose on 2026-11-22 to correct them under this step, so the requirement above
  covers every row, not only the owners. #81 now follows #307, and #377 and #372 come before #302 (#372 needs
  #377). #372 and #373 depend on each other for different steps; #372 comes first, as #373's reproduction
  needs it, and its step 6 waits for #373.
- **Synced 2026-11-22 after decision H, and made status-only** (developer: *keep plan and overview in sync*):
  #400 and #468 no longer own a smoke step, so they leave the T2 blockers of the 42 plans and their map lines and
  remain blockers of #503 alone. The map lines lost their `(#503's audit)` notes and their explanations; the
  outside-owner table keeps only owner, milestone and whose T2 it blocks; every order row's reason is the issue's
  status. The 42 plans' `Depends on:` headers lost their `(set back by #503, 2026-11-21)` parentheticals.

**A check of those changes, 2026-11-22 on `ab29679b`'s `overview.md`, before this step could start:** 14
owners, the seven in this milestone and the seven outside it. (a) and (b): no issue an owner lists lacks
that owner on its map line or in its order row, and no owner a line or row names fails to list that issue.
(c): the order runs against a dependency only at #503 (ahead of #438, #462, #469 and #472) and at #372 (ahead
of #373), and both rows say why. It is not this step's verification: row 14 is run when the step does.

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
| 11 | ✅ | `api-surface/06` brings its own cookies and asserts each case on the response's antiforgery cookie: none sent issues one, the same ring's is not replaced, another ring's is replaced and logs the pair once; and it declares itself deterministic | Live | Step 7's record: every step of `api-surface/06` passes; `Select-String -Path docs/automated-testing/api-surface/06-*.md -Pattern '^\*\*Deterministic:\*\* yes'` matches |
| 12 | ❌ | T2 passes: the smoke set and every changed document, every step green | Live | Step 12's record |
| 13 | ✅ | Both rules report a document that breaks them and pass one that does not, independently of today's documents | Unit test | `AutomatedTestingSuiteTests.MentionsOfFullyGreenAfter_*` (4) and `.StepsThatExcuseAFailure_*` (8): 12 pass; red with the rule bodies disabled (step 3) |
| 14 | ❌ | Every owner from step 8 is a blocker in `overview.md` in both directions, and every row of the order of operations comes after what it depends on or is T2-blocked by, or says why | Live | Reading `overview.md`: (a) for each owner's map line and each row of the outside-owner table, every issue listed names that owner on its map line and in its order row (#505 and #506 block the others through #503, which those rows name); (b) every owner a map line or order row names lists that issue; (c) no order row comes before an issue its map line depends on or is T2-blocked by, nor after one it blocks; the only exceptions are #503 and the #372/#373 pair, and step 10 records why |
| 15 | ❌ | No smoke document checks another feature or reads a secondary source: doc 14 compares no bundled file with its converter, and docs 03, 14, 19 and 06 read no `/version` content count | Unit test + Live | `TheResetDocument_CoversEveryContentCategory` and `SourceRefreshDocuments_SayTheRefreshIsOffByDefault` pass, each red before its change; `Select-String -Path docs/automated-testing/database-lifecycle/03-*.md, docs/automated-testing/import-and-staged-actions/14-*.md, docs/automated-testing/import-and-staged-actions/19-*.md, docs/automated-testing/startup-and-degradation/06-*.md -Pattern '^dotnet.*--(equals|greater-than) database\.'` returns nothing, and `Select-String -Path docs/automated-testing/import-and-staged-actions/14-*.md -Pattern 'converter produces'` return nothing |
| 16 | ❌ | `import-and-staged-actions/31` and `database-lifecycle/05` exist, are not smoke, are indexed and in the solution, and each fails only at the step its owner holds; `import-and-staged-actions/32` exists, is smoke, is indexed and in the solution, and passes | Unit test + Live | `RepositoryStructureTests` passes (index, smoke set, solution); step 7's record shows doc 31 failing at its only step (#400) and doc 05 at the imported `/version` read (#468), and docs 14, 19, 32 and 03 passing every step |
