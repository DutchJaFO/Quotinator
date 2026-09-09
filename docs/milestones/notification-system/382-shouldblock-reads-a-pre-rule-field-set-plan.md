# #382 — `ShouldBlock` is evaluated against a pre-rule field set at four of five sites

**Status:** Planning
**GitHub issue:** #382
**Tiers required:** T1, T2
**Depends on:** [#377](https://github.com/DutchJaFO/Quotinator/issues/377)

**Next action: execute this plan.**

---

## Description

`CompletenessGuard.ShouldBlock` decides whether a row a human has marked `Complete` blocks an import
rather than being silently changed. #168's comment at the Quote branch states the invariant it is meant
to have — *"ShouldBlock is evaluated against what would actually be WRITTEN (resolved), not the raw
incoming value"* — and that holds for the merge policies it was written for. It does not hold under
Review-with-rules.

`effectiveChanged` is computed **before** `ruleResolved` overwrites `resolved`, so a `Complete` row
whose only difference is a field a rule resolves back to the stored value is staged `Blocked`. The
block is real and an operator must clear it by hand, for a change that would never have been made.

**Five code locations across four entity types, not four.** The issue's prose says "four of the five
rule-consulting sites" while its own table lists five rows — the two readings differ over whether
`PlanSourcesAsync`'s explicit-id and natural-key branches count as one site or two. They are two
separate pieces of code and each needs its own edit and its own tests, so this plan counts five. The
issue's line numbers predate the #377 merge; the current ones are below.

| Entity | Method / branch | `effectiveChanged` | `ShouldBlock` | rule resolution |
|---|---|---|---|---|
| Quote | `PlanAsync` | `:445` | `:479` | `:496`–`:567` |
| Source | `PlanSourcesAsync`, explicit-id | `:1160` | `:1163` | `:1197`–`:1206` |
| Source | `PlanSourcesAsync`, natural-key | `:1322` | `:1325` | `:1358`–`:1367` |
| Universe | `PlanUniverseAsync` | `:1860` | `:1863` | `:1896`–`:1905` |
| Series | `PlanSeriesAsync` | `:2052` | `:2055` | `:2088`–`:2101` |
| Season | `PlanSeasonsAsync` | `:2261` | `:2267` | `:2244`–`:2257` |

### What the cross-check found

The current behaviour is not an oversight. All five defective locations carry an explicit `#181`
comment stating it as a decision — *"A rule never bypasses CompletenessGuard above — a Complete row
still blocks regardless of whether a rule could have resolved the change"* — and
[#181's plan doc](../data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md) states the
same thing in its Step 3: *"The rule lookup runs after `CompletenessGuard.ShouldBlock`, never before."*
It shipped a regression test asserting exactly this issue's reproduction case as correct:
`PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided` uses a
`Keep` rule — so the resolution equals the stored value and nothing would be written — and asserts
`Blocked`.

**Season is the deviation, not the model.** Season's rule branch was added late (its own comment
records that Season shipped without one in #375), inserted where `resolved` is built, which happened to
place it ahead of `effectiveChangedFields`. Neither #375's nor #377's plan doc records an ordering
decision, and Season alone carries no `#181` comment. The issue has this backwards.

**#168's comment does not actually conflict with #181's.** Its own text scopes itself to policy
resolution — *"Skip's resolved value always equals existingFields (nothing written) … a merge policy
only blocks on fields the merge itself would actually change"*. The issue reads it as a universal
claim about every resolution mechanism, which it was never written as.

### Measurement

Taken during planning, per #377's lesson that a plan resting on a prediction is a false plan. The
result is decisive enough that no diagnostic test was needed:

**Zero rows in the bundled corpus are affected, in either direction.** `ShouldBlock`'s first clause is
`status == CompletenessStatus.Complete`, so every row this issue can reach is a `Complete` row.
`Complete` is human-set only: every table's `CompletenessStatus` column defaults to `'Incomplete'`,
`CompletenessGuard.ComputeNextStatus` can only ever reach `NeedsReview`, and no import-file schema or
entry DTO carries a completeness field at all — `MarkCompletenessAs` exists solely on
`ConflictDecisionRequest` and the bulk-decide row shape, both reachable only through
`POST /import/actions/…/decide`. A bundled reseed therefore produces no `Complete` row and no `Blocked`
action of this kind.

Two consequences carried into the plan below. The change is invisible on a reseed, so its live
verification has to construct the state through the decide endpoint rather than look for it in the
corpus (step 6). And the behaviour it changes is reachable only on an operator's hand-curated database
— which is precisely the case #181 was protecting.

### Decision

**Option A: move `ShouldBlock` onto the post-resolution field set at all five locations** (developer
decision, 2026-09-09, taken against the finding above rather than around it). The invariant #168 states
becomes true everywhere, Season stops being an outlier without being touched, and a `Complete` row is
blocked only when something would actually be written to it.

This deliberately supersedes #181's decision. What survives of it is the narrower guarantee that a rule
never writes to a `Complete` row unannounced: a rule resolving to a *different* value still blocks, at
every site, and each site gets a test asserting so. What is given up is blocking on disagreement alone.

The alternative considered and rejected was keeping the pre-rule set, correcting #168's comment to say
so, and moving Season into line with the other five instead.

**Found while planning #377, and deliberately kept out of it** (that issue's decision B). #377 computes
its own post-resolution field set for classification and leaves `ShouldBlock`'s input untouched, so the
two issues touch adjacent lines and nothing else.

**It should have been filed when it was found, not when #377 reached the step that filed it.**
`process.md` requires a defect found mid-milestone to be filed immediately; scheduling it as #377's
eighth step left it living only as prose in another issue's plan across that issue's whole
implementation. Recorded because the rule was read and then not followed.

---

## Steps

### 1. Record the cross-check finding and the decision on the GitHub issue

**Status:** ⬜ Not started

The issue's Notes call Season "the model, not a fifth defect" and say nothing about #181. A reader
arriving at the closed issue would have no way to learn that a prior, tested decision was reversed
here, or that the reversal was taken deliberately rather than in ignorance of it.

Post one comment carrying this plan's *What the cross-check found*, *Measurement* and *Decision*
sections. Draft-then-approve per `process.md` — the full text pasted into the chat before
`gh issue comment` runs.

No scope changes to the spec: the Definition of done's *"Fix implemented at all four sites, with Season
left as-is"* stays literally true under A, and its two named tests are written as named.

### 2. Write the twelve tests and confirm which are red

**Status:** ⬜ Not started

All in `tests/Quotinator.Core.Tests/Database/ImportActionPlannerTests.cs`. Two per site: a `Complete`
row whose rule resolves to the stored value, and a `Complete` row whose rule resolves to a different
value and must still block. The existing `BuildQuoteTextKeepRule` shape is the model for the first half
— a `Keep` resolution is what makes the resolved payload equal the stored one.

| Site | Test | Before the fix |
|---|---|---|
| Quote | `PlanAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ❌ red |
| Quote | `PlanAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green |
| Source, explicit-id | `PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ❌ red |
| Source, explicit-id | `PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green |
| Source, natural-key | `PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ❌ red |
| Source, natural-key | `PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green |
| Universe | `PlanUniverseAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ❌ red |
| Universe | `PlanUniverseAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green |
| Series | `PlanSeriesAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ❌ red |
| Series | `PlanSeriesAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green |
| Season | `PlanSeasonsAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` | ✅ green — control |
| Season | `PlanSeasonsAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` | ✅ green — control |

The five green "still blocked" tests are regression guards, not filler: they are what stops step 3
being satisfied by deleting the guard call. Season's pair is the control the site that was already
right needs — it guards against a change that makes every branch consistent by breaking the one that
was already correct, the same control #377's row 6 needed. A run in which Season's pair goes red is a
failed fix, not a passing one.

The positive half asserts the action is **not** `Blocked`; the specific status it does reach is
`Applied` with kind `ResolvedToExisting`, via #377's no-op path, since a resolution equal to the stored
values writes nothing. Assert both, not just the absence of `Blocked` — "not Blocked" alone is
satisfied by a `Pending` regression.

### 3. Move rule resolution ahead of the `ShouldBlock` input at all five locations

**Status:** ⬜ Not started

Only the rule *computation* and the `resolved` overwrite move. The `Blocked` exit and the `Stale` exit
stay exactly where they are, in that order — today `Blocked` takes precedence over `Stale` at every
site, and hoisting the stale staging along with the resolution would silently invert that.

**Source (both branches), Universe, Series** — a clean local move. `ruleDecisions` and `hasStaleRule`
are already built earlier (before the "nothing changed" early exit, per #181's own Round 3 fix), so
only the `FieldMergeResult? ruleResolved = …` block and the `if (ruleResolved is not null) resolved = …`
line move, from below the `Stale` exit to directly above the `resolvedFields`/`effectiveChangedFields`
computation.

**Quote** — the decisions loop, the `hasStaleRule` flag, and the `ResolveWithDecisions` call currently
share one `if (policy == Review && conflictRules is not null)` block with the `Stale` staging. Split
it: hoist `hasStaleRule` to the enclosing scope, keep the staging block behind the `Blocked` exit, and
leave the resolve guarded by `!hasStaleRule` so a stale row still never resolves. Resulting order:

1. `resolved` from the policy switch
2. the `contentIsIdentical` → `Unchanged` exit (#373) — moved above the field-set computation, which it
   never used
3. the rule computation and the `resolved` overwrite
4. `resolvedFields` / `effectiveChanged`
5. `ShouldBlock` → `Blocked`
6. `hasStaleRule` → `Stale`

Placing the `Unchanged` exit first is what keeps the decisions loop from running on content-identical
rows, so the loop's one side effect — `retirableRuleFindings.Add` — cannot start reporting findings it
does not report today. The loop already `continue`s on every field where both sides are equal, so this
is belt-and-braces rather than a fix; verification row 15 asserts it either way.

`contentIsIdentical` keeps comparing incoming against stored, not via `effectiveChanged`. #377 already
recorded why those differ under Skip, and nothing here changes it.

### 4. Retire #181's superseded test and rewrite the comments it left behind

**Status:** ⬜ Not started

`PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided` asserts
`Blocked` for a `Keep` rule on a `Complete` row — the exact scenario step 2's first row now asserts is
not blocked. It cannot be left, and its name would be false if merely re-pointed. Delete it in the same
commit as step 3, and record the supersession in the XML doc of
`PlanAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked`, which is what remains of the
protection it existed for.

At all five locations, replace the `#181` comment's *"a rule never bypasses CompletenessGuard"*
sentence with what is now true: the guard runs on the resolved values, so a rule that resolves a
disagreement away no longer blocks, while a rule that would write a different value still does. Cite
both issues — a reader who greps `#181` must land on the reversal, not on the reversed text. Add the
same comment to `PlanSeasonsAsync`, which never carried one, so the site that was accidentally right
becomes deliberately right.

#168's comment stays as written at every site: under A its claim is finally true everywhere.

### 5. Note the supersession in #181's plan doc

**Status:** ⬜ Not started

[#181's plan doc](../data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md) states the
rule-after-guard ordering and names its regression test. Both are now false, and it is a released
milestone's document that nothing else would correct. One sentence in its Step 3, pointing at #382,
in its own `docs [#382]:` commit per `process.md`'s separate-commit rule.

### 6. Add the T2 document and confirm it red against the pre-fix image

**Status:** ⬜ Not started

New document under `docs/automated-testing/import-and-staged-actions/`, added to the index and to
`Quotinator.slnx`. `Smoke: no` — this is a narrow protection-mechanism behaviour, not something whose
failure invalidates other results.

The measurement above is what shapes it: no bundled row is `Complete`, so the state has to be built.
`import-and-staged-actions/12` already establishes the pattern — reach a `Pending` Modify, decide it
with `markCompletenessAs: "Complete"`, then import again — and `18-rule-file-override-endpoints`
establishes how a rule file is installed live. Both halves in one document: with a `Keep` rule the
second import must **not** stage `Blocked`, and with a rule resolving to a different value it must.

Confirm it red first, against a `docker build` of the commit before step 3, per `process.md`'s
Implementation step 1 — then tear down container, image and worktree. A document run only against the
finished build shows that something happens, not that it would have caught the absence.

### 7. Full build and test run

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both `0 Warning(s)  0 Error(s)`. Every file this issue touches goes into `.editorconfig`'s scoped
`IDE0008`/`IDE0090` sections at the moment it is first touched, per the boyscout rules.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A `Complete` quote whose rule resolves to the stored value is not blocked | Unit test | `ImportActionPlannerTests.PlanAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` — status `Applied`, kind `ResolvedToExisting` |
| 2 | ❌ | A `Complete` quote whose rule resolves to a different value still blocks | Unit test | `ImportActionPlannerTests.PlanAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` — status `Blocked` |
| 3 | ❌ | Same, Source explicit-id branch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 4 | ❌ | Same, Source explicit-id branch, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 5 | ❌ | Same, Source natural-key branch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 6 | ❌ | Same, Source natural-key branch, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 7 | ❌ | Same, Universe | Unit test | `ImportActionPlannerTests.PlanUniverseAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 8 | ❌ | Same, Universe, still-blocks half | Unit test | `ImportActionPlannerTests.PlanUniverseAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 9 | ❌ | Same, Series | Unit test | `ImportActionPlannerTests.PlanSeriesAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 10 | ❌ | Same, Series, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSeriesAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 11 | ❌ | Season, already correct, is not broken by making the others consistent | Unit test | `ImportActionPlannerTests.PlanSeasonsAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` and `...RuleResolvesToADifferentValue_IsStillBlocked` — green before step 3 and after |
| 12 | ❌ | The five "is not blocked" tests were genuinely red before the fix | Live | `dotnet test --filter "RuleResolvesToExistingValue_IsNotBlocked"` at the commit before step 3 — 5 failed, 1 passed (Season) |
| 13 | ❌ | `Blocked` still takes precedence over `Stale` at every site | Unit test | Existing `#153` staleness tests stay green; `dotnet test --filter ImportActionPlannerTests` reports no new failure |
| 14 | ❌ | #181's protection survives in its narrowed form and its superseded test is gone | Live | `grep -c "MatchingRuleButCompletenessGuardBlocks" tests/` → `0`; rows 2/4/6/8/10 green |
| 15 | ❌ | A content-identical row still stages `Unchanged` and reports no retirable-rule finding | Unit test | Existing `#373` Unchanged tests and the `#153` retirable-finding tests stay green |
| 16 | ❌ | Every site's comment names the reversal rather than the reversed decision | Live | `grep -n "never bypasses CompletenessGuard" src/Quotinator.Core/Database/ImportActionPlanner.cs` → no matches; six sites cite `#181`/`#382` |
| 17 | ❌ | #181's plan doc no longer asserts the reversed ordering | Live | `grep -n "382" docs/milestones/data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md` → the supersession sentence |
| 18 | ❌ | Live: a `Complete` row with a `Keep` rule is not blocked on reimport; with a differing rule it is | T2 | The new `docs/automated-testing/import-and-staged-actions/` document, red against a pre-step-3 image and green after |
| 19 | ❌ | Build and full suite clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1` — both `0 Warning(s)  0 Error(s)` |
| 20 | ❌ | T1: the app starts without error | Live | Developer starts the app in Visual Studio and confirms startup |
