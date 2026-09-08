# #378 — A Keep/Replace resolution on a Quote's date field never actually takes effect

**Status:** Waiting for release. Fix implemented and verified; every checklist row is ✅.
**GitHub issue:** #378
**Tiers required:** T1, T2
**Depends on:** none — found live while verifying [#374](https://github.com/DutchJaFO/Quotinator/issues/374)'s own conflict-rule mechanism against the real bundled corpus

---

## Description

`ConflictRuleLookup.TryResolve` correctly computes a `Keep`/`Replace` decision's wanted value and
`FieldMergeResolver.ResolveWithDecisions` correctly folds it into `MergedFields`. Neither of those was
the defect. The defect was one step further downstream: `ImportActionPlanner.ResolveSourceAsync`
resolves — and, where none exists yet, creates — a Quote's linked Source variant against the quote's own
**raw incoming date**, before the field-merge decision for that same quote's `date` field is known. For
two in-file duplicate quotes sharing one id but disagreeing on date, this independently produces two
Source variants (one per raw date), and `SqliteImportActionService.ApplyResolvedActionAsync`'s Quote
branch writes whichever variant the **last-processed occurrence's own raw date** resolved to — with no
awareness of what the field-merge decided. A `Keep` rule that correctly computes `MergedFields.date` as
the existing (first) value was therefore silently overridden the moment the second occurrence's own
Source-resolution FK write landed.

Found live while verifying #374's own conflict-rule mechanism against the real bundled corpus
(`nikhilnamal17-conflict-rules.json`'s pre-existing Auntie Mame `Keep` rule) — the rule reported no error
of any kind; the quote simply, silently displayed the wrong date on every subsequent read.

## Root cause and fix

`ImportActionPlanner.PlanAsync` already applied a matching `Custom` decision to a quote's own fields
*before* `ResolveSourceAsync` ran (#153's own early-normalization block) — but only for `Custom`,
because `Keep`/`Replace` need to know the *true* existing value to resolve against, and `existing` was
not computed until *after* Source/Character/Person resolution had already run.

The fix: `existing` is now resolved before Source/Character/Person resolution, and the early
normalization block was extended to also apply a matching `Keep`/`Replace` decision (not only `Custom`)
whenever `existing` is available — using the same `ConflictRuleLookup.TryResolve` call the later Modify
branch already made, just run earlier. A quote with nothing to compare against yet (`existing is null`,
a brand-new Add) still only normalizes via `Custom` — **not because Keep/Replace are meaningless there**
(developer correction, 2026-09-08: "when there is no data the 'existing value' is treated as NULL. Keep
and Replace can still be executed, provided the row is created" — both are perfectly well-defined against
a null existing, `Keep`→null, `Replace`→incoming), but because a rule that matches while nothing exists
yet is exactly as likely to be a mistake (a role-reversed or vestigial recorded snapshot) as a genuine
choice, and applying it blind risks corrupting a field silently. See "Keep/Replace against nothing" below
for what this project does instead: hold the Add for review rather than apply or ignore it.

`ApplyMergedFields` indexes every field of its `merged` dictionary unconditionally, so the early block
cannot pass a partial (decided-fields-only) map — it blends the true existing value in for decided
fields and the same value on both sides for every other field (trivially non-ambiguous, auto-resolves
straight through), so an unrelated genuinely-ambiguous field with no matching rule (e.g. `character`)
never throws here — staging that as `Pending` stays the later Modify branch's own job.

A `Custom` rule was never affected by this defect (see #374's own investigation, which is what led here)
— it already normalized before Source resolution ran, so at most one Source variant was ever created
for it.

## Keep/Replace against nothing — found live during this fix's own verification

Extending the early-normalization block naively to also *apply* Keep/Replace when nothing exists yet
(treating a null existing as a real value, per the developer's correction above) immediately broke 4
real tests: the actual bundled corpus has four `nikhilnamal17-conflict-rules.json` `quoteText` "Keep"
rules whose `existingRecord`/`incomingRecord` are already byte-identical — vestigial rules from an
earlier session that no longer describe any real disagreement. Applying them naively would have resolved
`quoteText` (a `NOT NULL` column) to `null`, corrupting the row.

**Resolution (developer directive): mark it for review, not silently apply or silently ignore.** A
Keep/Replace decision that matches while nothing exists yet now holds the Add as `Pending`
(`keepOrReplaceAgainstNothing`, mirroring the existing `dateNeedsReview` mechanism) instead of either
outcome — a curator confirms whether it is a genuine fix or a mistake before anything is written. This
also exposed a same-batch accumulation gap: two in-file occurrences of the same id that both trigger this
(as the four real rules' byte-identical duplicates do) would otherwise each independently stage their own
Pending Add, since the database-based dedup check can't see a same-batch sibling that hasn't been staged
yet — fixed with a dedicated same-batch `stagedPendingReviewAddIds` set, mirroring `stagedSourceVariantIds`'s
own reasoning.

The four real vestigial rules were deleted outright (not "fixed") once reviewed — they never described a
real conflict, so there was nothing to keep pending review for.

## Reproduction steps

1. Seed a file containing two quote entries sharing the same id (same `quote`+`source`, so both hash to
   the same `QuoteIdentity.StableId`) but different `date` values, e.g. `"1958"` and `"2005"`.
2. Provide a `ConflictResolutionRule` for that entity id resolving `date` via `"resolution":"keep"`,
   with `existingRecord.date` matching the first occurrence and `incomingRecord.date` matching the
   second.
3. Run a cold seed (`InitialiseAsync`) against this fixture, under `DuplicateResolutionPolicy.Review`.
4. Query the quote's linked Source's `Date` column:
   `SELECT s.Date FROM Quotinator_Quote q JOIN Quotinator_Source s ON s.Id = q.SourceId WHERE q.Id = @id`

Confirmed genuinely red without the fix, green with it, via `git stash` of `ImportActionPlanner.cs`
against the same test (not merely written once against already-fixed code).

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|---------------|
| 1 | ✅ | A `Keep` resolution controls which Source variant the quote links to, not just its reported field | Unit test | `DatabaseInitializerTests.Seed_WithADuplicateQuoteKeepRule_QuoteLinksToTheKeptDateNotTheLastOccurrences` — confirmed red via `git stash` of `ImportActionPlanner.cs`, green with it restored |
| 2 | ✅ | No regression in the field-merge/planner suites this change touches | Test run | `dotnet test --configuration Release -m:1` — full solution green, 0 warnings, 0 errors |
| 3 | ✅ | The real bundled corpus's own pre-existing `Keep` rule (Auntie Mame) resolves cleanly with the fix, with no leftover spurious Source row | Live (T2 Docker) | `masterdata/sources` shows exactly one "Auntie Mame" row (`date: 1958`); `/quotes/search?q=banquet` returns `date: 1958` — confirmed across 3 independent fresh containers |
| 4 | ✅ | The real bundled corpus, reseeded repeatedly, stays at zero Stale with the fix in place | Live (T2 Docker) | 5 consecutive reseeds on one persistent DB, all identical: `stale=0` every time (see #374's own row 33/54, which this fix is what makes achievable via `Keep` rather than only via `Custom`) |
| 5 | ✅ | The developer's own machine confirms the same behaviour | Live (T1) | Confirmed 2026-09-08, Visual Studio, against live-fetched source data: two consecutive reseeds both report `stale=0` across every entity type |
| 6 | ✅ | A Keep/Replace rule matching a brand-new quote (nothing exists yet) stages Pending for review — not applied, not ignored | Unit test | `DatabaseInitializerTests.Seed_WithAKeepRuleMatchingABrandNewQuote_StagesPendingForReviewRatherThanApplyingOrIgnoring` — confirmed red via `git stash`, green with it restored |
| 7 | ✅ | Two in-file occurrences of the same id that both trigger review stage exactly one Pending action, not two | Unit test | Same test as row 6 — asserts `Assert.HasCount(1, actions)` against a two-occurrence fixture |
| 8 | ✅ | The four real vestigial `quoteText` "Keep" rules (byte-identical existing/incoming, doing nothing) are removed from the real corpus | Live (T2 Docker) + unit test | Confirmed via direct inspection of both raw occurrences for all four entity ids (byte-identical); removed from `nikhilnamal17-conflict-rules.json`; `NikhilNamal17RealCorpusWithCurrentRuleFile_ResolvesCompletelyAndStaysStable` (row 33/#374) passes again at genuinely zero pending |

## Scope changes

The original issue only covered the Source-link defect (rows 1-5). "Keep/Replace against nothing stages
Pending for review" (rows 6-8) was added during this fix's own verification, per direct developer
direction in the working session — not filed as a separate issue, since it's the same root defect class
(a Keep/Replace decision reaching the apply/staging path without the safeguards a genuine existing value
would provide) discovered while fixing the first half. See the GitHub issue's own comment recording this.

## Definition of done

- [x] Failing test listed above is red before the fix is written
- [x] Fix implemented — the Quote's apply path no longer lets a later occurrence's independently-resolved
      Source variant silently override what the field-merge decided for `date`
- [x] Listed test passes (green)
- [x] No regression in related tests (`ImportActionPlannerTests`, `SqliteImportActionServiceTests`, the
      real-corpus `DatabaseInitializerTests` suite)
- [ ] Findings summarised in a closing comment
