# #382 — `ShouldBlock` is evaluated against a pre-rule field set at four of five sites

**Status:** Waiting for release
**GitHub issue:** #382
**Tiers required:** T1, T2
**Depends on:** [#377](https://github.com/DutchJaFO/Quotinator/issues/377)

**Next action: ship it** — every step is complete and every verification row is green.

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
corpus (step 5). And the behaviour it changes is reachable only on an operator's hand-curated database
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

**Everything above was posted to the issue as its own comment** (verification row 21) — Planning-phase
output, landing with this plan, not a numbered step. An earlier draft made it step 1, which was wrong
twice over: it duplicated these sections as work, and it wrote "draft, then get approval" into a step,
turning a standing `process.md` mechanic that governs *every* commit and issue action into something
the plan appeared to be blocked on. A plan carrying its own approval gate is not executable, which
defeats the point of the header saying it is.

**Found while planning #377, and deliberately kept out of it** (that issue's decision B). #377 computes
its own post-resolution field set for classification and leaves `ShouldBlock`'s input untouched, so the
two issues touch adjacent lines and nothing else.

**It should have been filed when it was found, not when #377 reached the step that filed it.**
`process.md` requires a defect found mid-milestone to be filed immediately; scheduling it as #377's
eighth step left it living only as prose in another issue's plan across that issue's whole
implementation. Recorded because the rule was read and then not followed.

---

## Steps

### 1. Write the twelve tests and confirm which are red

**Status:** ✅ Done

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

The five green "still blocked" tests are regression guards, not filler: they are what stops step 2
being satisfied by deleting the guard call. Season's pair is the control the site that was already
right needs — it guards against a change that makes every branch consistent by breaking the one that
was already correct, the same control #377's row 6 needed. A run in which Season's pair goes red is a
failed fix, not a passing one.

The positive half asserts the action is **not** `Blocked`; the specific status it does reach is
`Applied` with kind `ResolvedToExisting`, via #377's no-op path, since a resolution equal to the stored
values writes nothing. Assert both, not just the absence of `Blocked` — "not Blocked" alone is
satisfied by a `Pending` regression.

**Measured against the pre-fix commit: 5 failed, 7 passed**, exactly as predicted above. The five
failures are the five "is not blocked" positives; Season's control pair and the five "still blocks"
regression guards passed unchanged.

**Two fixture findings, both of which changed a test rather than the code.**

*#181's retired test was overdetermined.* Its fixture — inherited unchanged by the first draft of the
Quote pair here — disagrees on two fields, not one: `BuildQuote` defaults `character` to `"Rick Blaine"`
while `SeedExistingQuoteAsync` stores none. That second difference is a backfill of a field the stored
row has no value for, which `FieldMergeResolver` settles towards the incoming side without any rule, and
which is a genuine write. A `Complete` row must still block it. So the retired test's `Blocked` would
have held under this issue's corrected ordering too — it never isolated what it named.

*Quote is the one site with an **early** rule application*, the Add-branch pass at `ImportActionPlanner.cs:259`
that rewrites `q` before the Modify branch runs. It applies a `Keep` outright, so a disagreement whose
only field carries a `Keep` rule is already identical by the time the Modify branch sees it, and exits as
`Unchanged` — before the guard, both before and after this fix. A single-field Keep fixture is therefore
green either way and proves nothing. The Quote pair uses a second field no rule mentions — `date`,
omitted on the incoming side and resolved back to the Source's stored value on its own (#377's
mechanism 1) — which survives that pass and does reach the guard.

### 2. Move rule resolution ahead of the `ShouldBlock` input at all five locations

**Status:** ✅ Done

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

### 3. Retire #181's superseded test and rewrite the comments it left behind

**Status:** ✅ Done

`PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided` asserts
`Blocked` for a `Keep` rule on a `Complete` row — the exact scenario step 2's first row now asserts is
not blocked. It cannot be left, and its name would be false if merely re-pointed. Delete it in the same
commit as step 2, and record the supersession in the XML doc of
`PlanAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked`, which is what remains of the
protection it existed for.

At all five locations, replace the `#181` comment's *"a rule never bypasses CompletenessGuard"*
sentence with what is now true: the guard runs on the resolved values, so a rule that resolves a
disagreement away no longer blocks, while a rule that would write a different value still does. Cite
both issues — a reader who greps `#181` must land on the reversal, not on the reversed text. Add the
same comment to `PlanSeasonsAsync`, which never carried one, so the site that was accidentally right
becomes deliberately right.

#168's comment stays as written at every site: under A its claim is finally true everywhere.

### 4. Note the supersession in #181's plan doc

**Status:** ✅ Done

[#181's plan doc](../data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md) states the
rule-after-guard ordering and names its regression test. Both are now false, and it is a released
milestone's document that nothing else would correct. One sentence in its Step 3, pointing at #382,
in its own `docs [#382]:` commit per `process.md`'s separate-commit rule.

### 5. Add the T2 document and confirm it red against the pre-fix image

**Status:** ✅ Done

A document was written, driven live against both a pre-fix and a post-fix image, and then **deleted**
rather than shipped. It cannot assert what it names, for a reason that is worth more than the document
would have been.

**What was built and run.** Container `qt-import-23` on port `18623`, against `quotinator:qt382-prefix`
(a `git worktree` + `docker build` of the commit before step 2) and then `quotinator:qt382-post`. The
fixture: add a quote against the bundled dated Source `Airplane!` (1980), Modify it under `review`,
decide with `markCompletenessAs: "Complete"` and apply, then re-import the row with `date` omitted — the
one field the resolver settles back to its stored value on its own.

**Pre-fix it staged `Blocked`, exactly the defect.** Post-fix it staged `Blocked` too.

**Why: `POST /import` supplies no `ConflictRuleLookup` at all.**
`SqliteQuoteImportService.PlanAsync` (`src/Quotinator.Core/Services/SqliteQuoteImportService.cs:90`)
omits the `conflictRules` argument, leaving it `null`, so `PlanAsync`'s
`if (policy == Review && conflictRules is not null)` gate never opens and the resolution — rule-driven
or not — never runs on that path. Only `QuotinatorDatabaseInitializer` (seed/reseed) passes a lookup.
Confirmed by probe rather than by reading: the same import against an *Incomplete* row staged `Pending`
with an empty `ambiguousFields`, which is what a skipped resolution looks like and not what an
attempted one does. This is pre-existing behaviour, unrelated to #382 and unchanged by it.

**The seed/reseed path is the only live route, and it is blocked in turn.** Reaching the state there
needs a `Complete` row plus a rule whose resolution equals the stored value, installed as a rule-file
override — and an override can only be produced by `POST /import/rules/conflict/generate` from a batch
with a decided field. Re-importing `data/sources/quotinator-curated.json` under `review` now stages
**zero** pending actions (measured on the post-fix container; `200`, not `202`), so there is no decided
field to generate from. #372/#373's own "a reseed leaves zero pending items" result is what closed that
door. Note also that `18-rule-file-override-endpoints` step 3 assumes the opposite and may itself be
stale — not this issue's to fix.

**Two observability findings, both reportable rather than fixable here.**

1. **No Review-policy import through `POST /import` can auto-resolve anything.** Whether that is
   intended or a gap is a scope question, not a call to make inside a bug fix about guard ordering.
2. **`GET /quotes/{id}` does not expose `completenessStatus`** — the field is on every masterdata
   response (`SourceResponse`, `CharacterResponse`, …) but not on `QuoteResponse`, so a quote's
   completeness cannot be read back through the API at all. The document had to infer it from staging
   behaviour instead of asserting it.

**None of that was a discovery — it was a planning failure.** This step originally asserted a live
route existed, citing `import-and-staged-actions/12` and `18` as though their mechanics carried over,
without checking either claim. Both were checkable statically in minutes: `SqliteQuoteImportService`
does not pass `conflictRules`, and `18`'s "stage a batch to generate from" premise had already been
invalidated by #372/#373. Planning step 3 exists to catch exactly this; it was run against the code
premise and not against the verification premise. The cost was a full pre-fix/post-fix Docker cycle
spent proving something a `grep` would have shown.

### The route that does work — via `{dataDir}/imports/`

`ManifestSeedPlanner.Plan(dir)` is directory-agnostic: it reads `manifest.json` from whatever directory
it is given and resolves each entry's `ruleFile` relative to that same directory
(`ManifestSeedPlanner.cs:57`). `{dataDir}/imports/` is scanned exactly like `data/sources/`, and its
files seed through `QuotinatorDatabaseInitializer` — **the path that does pass a `ConflictRuleLookup`**.
So a user-imports folder carrying its own `manifest.json`, a quotes file, and a conflict-rules file
reaches the rule-resolution branch with no override endpoint involved at all.

Wire names verified against the DTOs rather than assumed: `files[].file` and `files[].ruleFile`
(`ManifestFileEntryDto`).

**Two documents, not one** (developer, 2026-09-09: *"it is better to have separate test documents
instead of trying to combine them as one test document. that allows for better diagnosis if a test
fails"*). A single document holding both halves is stateful across its own steps, and that coupling
bites concretely rather than theoretically: #374's accumulation guard makes `PlanAsync` skip a quote
that already carries an unresolved action, so the positive half failing leaves a `Blocked` row that
makes the negative half stage nothing and report `blocked=0` — which reads exactly like the guard
wrongly declining to block. A red run would fail twice with only the first failure real. Splitting
them removes the coupling rather than documenting it; each builds its own container, its own fixture
and its own entity id.

- **`23-complete-row-blocks-only-on-a-real-write.md`** — the positive. A `Complete` row whose omitted
  `date` the resolver settles back to the Source's stored value writes nothing, so it must not block.
  Red pre-fix (`Blocked/Modify`), green post-fix (`blocked=0`, `Applied/ResolvedToExisting`).
- **`24-complete-row-still-blocks-a-real-write.md`** — the negative, and what stops the narrowing
  going too far: a `Replace` rule resolving to a different value must still block. Green on **both**
  images (`blocked=1`, quote unwritten), which is what makes it a regression guard rather than a
  post-fix artefact.

Both were confirmed red-or-green against a `docker build` of the commit before step 2, per
`process.md`'s Implementation step 1, then container, image and worktree torn down. Both are added to
`docs/automated-testing/README.md`'s index and to `Quotinator.slnx`; `Smoke: no`.

Confirm red first against a `docker build` of the commit before step 2, per `process.md`'s
Implementation step 1, then tear down container, image and worktree. Add the document to
`docs/automated-testing/README.md`'s index and to `Quotinator.slnx`; `Smoke: no`.

**Two observability gaps stay open regardless, and are reportable rather than fixable here** — both are
scope questions, not parts of a guard-ordering fix. (a) No Review-policy import through `POST /import`
can auto-resolve anything, rule or otherwise. (b) `GET /quotes/{id}` does not expose
`completenessStatus`, though every masterdata response carries it, so a quote's completeness cannot be
read back through the API at all.

### 6. Full build and test run

**Status:** ✅ Done

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both `0 Warning(s)  0 Error(s)`.

**A run was made out of order and does not count.** It was executed before step 5 and reported
4,034 tests green across ten projects — but step 5 produces a new `docs/automated-testing/` document
plus its index row and `Quotinator.slnx` entry, and `RepositoryStructureTests` asserts that every
document is reachable from the index and that the smoke set matches. A suite run that predates those
artifacts cannot speak for them. Re-run this step once step 5 resolves; the earlier figure is recorded
only so the difference is visible, not as evidence.

**No `.editorconfig` change was needed, and one was deliberately not made.** Both touched files —
`ImportActionPlanner.cs` and `ImportActionPlannerTests.cs` — are already in the scoped `IDE0008` list,
and the build stays at zero warnings. They are *not* in the `IDE0090` list, and adding them was measured
rather than assumed: escalating it for those two files surfaces **214** warnings, every one a
pre-existing `new Type(...)` this issue never touched. That is the bulk rewrite the ratchet exists to
avoid, and `.editorconfig`'s own rule scopes `IDE0090` to files a session actually converted `var` in —
which this issue did not. Recorded here so the figure is available to whichever milestone-close pass
next tests the ratchet's end state.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A `Complete` quote whose rule resolves to the stored value is not blocked | Unit test | `ImportActionPlannerTests.PlanAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` — status `Applied`, kind `ResolvedToExisting` |
| 2 | ✅ | A `Complete` quote whose rule resolves to a different value still blocks | Unit test | `ImportActionPlannerTests.PlanAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` — status `Blocked` |
| 3 | ✅ | Same, Source explicit-id branch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 4 | ✅ | Same, Source explicit-id branch, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 5 | ✅ | Same, Source natural-key branch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 6 | ✅ | Same, Source natural-key branch, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_NoExplicitId_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 7 | ✅ | Same, Universe | Unit test | `ImportActionPlannerTests.PlanUniverseAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 8 | ✅ | Same, Universe, still-blocks half | Unit test | `ImportActionPlannerTests.PlanUniverseAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 9 | ✅ | Same, Series | Unit test | `ImportActionPlannerTests.PlanSeriesAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` |
| 10 | ✅ | Same, Series, still-blocks half | Unit test | `ImportActionPlannerTests.PlanSeriesAsync_CompleteRow_RuleResolvesToADifferentValue_IsStillBlocked` |
| 11 | ✅ | Season, already correct, is not broken by making the others consistent | Unit test | `ImportActionPlannerTests.PlanSeasonsAsync_CompleteRow_RuleResolvesToExistingValue_IsNotBlocked` and `...RuleResolvesToADifferentValue_IsStillBlocked` — green before step 2 and after |
| 12 | ✅ | The five "is not blocked" tests were genuinely red before the fix | Live | `dotnet test --filter "RuleResolvesToExistingValue_IsNotBlocked"` at the commit before step 2 — 5 failed, 1 passed (Season) |
| 13 | ✅ | `Blocked` still takes precedence over `Stale` at every site | Unit test | Existing `#153` staleness tests stay green; `dotnet test --filter ImportActionPlannerTests` reports no new failure |
| 14 | ✅ | #181's protection survives in its narrowed form and its superseded test is gone | Live | `grep -c "MatchingRuleButCompletenessGuardBlocks" tests/` → `0`; rows 2/4/6/8/10 green |
| 15 | ✅ | A content-identical row still stages `Unchanged` and reports no retirable-rule finding | Unit test | Existing `#373` Unchanged tests and the `#153` retirable-finding tests stay green |
| 16 | ✅ | Every site's comment names the reversal rather than the reversed decision | Live | `grep -n "never bypasses CompletenessGuard" src/Quotinator.Core/Database/ImportActionPlanner.cs` → no matches; six sites cite `#181`/`#382` |
| 17 | ✅ | #181's plan doc no longer asserts the reversed ordering | Live | `grep -n "382" docs/milestones/data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md` → the supersession sentence |
| 18 | ✅ | Live: a `Complete` row is not blocked when the resolution writes nothing | T2 | `docs/automated-testing/import-and-staged-actions/23-complete-row-blocks-only-on-a-real-write.md` — red on `quotinator:qt382-prefix` (`Blocked/Modify`), green on `qt382-post` (`blocked=0`, `Applied/ResolvedToExisting`) |
| 19 | ✅ | Live: a `Complete` row still blocks when a rule resolves to a different value | T2 | `docs/automated-testing/import-and-staged-actions/24-complete-row-still-blocks-a-real-write.md` — `blocked=1` with the quote unwritten on **both** images, proving it a regression guard rather than a post-fix artefact |
| 20 | ✅ | Build and full suite clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1` — `0 Warning(s)  0 Error(s)`, 4,034 tests across ten projects, 0 failed, run after step 5 so it covers both documents' index and solution entries |
| 21 | ✅ | T1: the app starts without error | Live | Developer ran it in Visual Studio 2026-09-09 — clean startup at 1.9.0-alpha, then a Reset and two reseeds, no errors |
| 22 | ✅ | The cross-check finding and the decision are recorded on the issue itself | Live | [#382 comment 5601823156](https://github.com/DutchJaFO/Quotinator/issues/382#issuecomment-5601823156) — the #181 supersession, Season being the deviation, the five-locations count, and the measurement |
