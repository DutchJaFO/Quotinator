# #377 — A Modify action whose resolution is a genuine no-op is still counted as Modified

**Status:** Planning
**GitHub issue:** #377
**Tiers required:** T1, T2
**Depends on:** [#373](https://github.com/DutchJaFO/Quotinator/issues/373), [#374](https://github.com/DutchJaFO/Quotinator/issues/374)

**Next action: execute step 1.** Every design decision is taken (see *Decisions taken*) and the scope is
measured rather than predicted (see *Measurement*), so no step below rests on an assumption about what
the corpus contains.

---

## Description

A `Modify` action whose resolution lands on exactly the values already stored — the resolved payload
equal to the stored one, so nothing is written differently — is still classified
`ImportActionKind.Modify`, applied as a real write, and counted in `ReseedEntityCountDto.Modified`.

**The defect still applies at `HEAD` (`18418c29`), measured 2026-09-08.** Neither #373 nor #374 covers
it, and both left it deliberately: #373's own step 9 records it as "one further, distinct, pre-existing
gap … found and recorded, not fixed", which is what filed this issue.

### Measurement

Measured against the real bundled corpus — all five content files with their own rule, alias and
exclusion files, mirroring `data/sources/manifest.json`'s own wiring — over a cold start and two
reseeds, with `Import_Action` auto-purge off so every batch's rows survive. A temporary diagnostic test
(`Issue377DiagnosticTests`), removed once this table was recorded, following #374's step 1.

| | Actions | Modify | **No-op Modify** | `Audit_Change` rows with `Action='Modified'` |
|---|---|---|---|---|
| Cold start | 1507 | 58 | **1** | 58 |
| Reseed 1 | 3015 (+1508) | 83 (+25) | **25 (+24)** | 83 (+25) |
| Reseed 2 | 4523 (+1508) | 108 (+25) | **49 (+24)** | 108 (+25) |

A no-op is counted here as `ExistingValue` and `MergedFields` being JSON-equivalent — the action's own
record of what was stored and what the resolution decided to write.

**Two facts this table establishes that nothing in the issue, or in this plan's first draft, predicted:**

1. **24 of every reseed's 25 Modify actions are no-ops — 96%, not an edge case.** This plan's first
   draft predicted one row, reasoning from the corpus's single conflict rule. That reasoning was wrong
   about which mechanism dominates (below), and the prediction was wrong by 24×.
2. **`Audit_Change` rows with `Action='Modified'` exactly equal the Modify count at every pass**
   (58/58, 83/83, 108/108) and grow by 25 per reseed, 24 of them false. The change history gains 24
   fabricated "this row was modified" entries on every reseed, permanently — ADR 014 forbids purging
   them, and #249's auto-purge clears `Import_Action`, not `Audit_Change`.

### The mechanism, which is not primarily the one the issue names

**1. A field the incoming side does not carry and the stored side does — 22 of 24 per reseed.**
`FieldMergeResolver.ResolveWithDecisions` (`FieldMergeResolver.cs:143`) resolves
`!existingEmpty && incomingEmpty` to the existing value, with no rule, no decision and no ambiguity.
That is correct behaviour and must not change. What is wrong is what happens next: the resolved payload
now equals the stored one, and the planner stages a `Modify` anyway.

Live example, from the measurement: `vilaboim_movie-quotes.json`'s raw upstream format is
`{ quote, movie }` — it carries no year at all — while `NikhilNamal17_popular-movie-quotes.json` carries
`year` for the same quotes. Every quote the two files share therefore arrives from vilaboim with
`date: null` against a stored date, resolves back to the stored date, and is reported as modified on
every reseed:

```
existing  … "source":"The Godfather","date":"1972" …
incoming  … "source":"The Godfather","date":null   …
merged    … "source":"The Godfather","date":"1972" …   ← identical to existing
```

Differing fields across all no-ops, per reseed: `date` ×22, `character` ×1, `genres` ×1.

**No rule is involved in any of these.** Confirmed directly: none of the affected quote ids appears in
any of the four bundled `*-conflict-rules.json` files.

**2. A rule reporting `AlreadyApplied` — 1 of 24 per reseed.** The mechanism the issue names, and the
one this plan's first draft assumed was the whole story.
`data/sources/quotinator-series-universe-conflict-rules.json` carries exactly one rule — Source
`3e72279b-187d-cc49-94e7-83e63335f56d` (*Star Wars: Episode V — The Empire Strikes Back*),
`seriesId: Replace`. Cold start makes the Source an `Add`; on every reseed after that `seriesId` already
holds the rule's own outcome, the rule reports #374's `ConflictRuleOutcome.AlreadyApplied`, a decision
is recorded, and the branch's `changedFields.Count == 0 && ruleDecisions.Count == 0 && !hasStaleRule`
early exit (`ImportActionPlanner.cs:1094`) is bypassed by that decision alone. It is the one Source
no-op in the table, and it is 4% of the problem.

**3. A merge policy resolving back to the existing values.** `MergeOurs` keeps the existing side on every
conflict. Not present in the measurement — every bundled file is `review` (`data/sources/manifest.json`)
— but reachable by any user import that sets a merge policy, and it reaches branches the other two do
not.

**What the three share, and what the fix therefore keys on:** the resolved payload equals the stored
one. That test covers all three without needing to know which produced it — which is what makes a single
detection point at each Modify site correct, rather than a per-mechanism special case.

### What it actually costs

The issue says the cost is a one-time reporting inaccuracy that "does not accumulate further". The
measurement says otherwise, and the write side is worse than the reporting side.

A no-op `Modify` is `Decided`, so `ImportActionResolutionCoordinator.TryApplyBatchAsync` applies it like
any other, and `SqliteImportActionService`'s apply switch — one shape for Quote, Source, Character,
Person, Series, Universe, StageDirection, SoundCue and Conversation alike — runs the real write path:

- `Sql.Quotes.UpdateOnNewestWins` (`src/Quotinator.Core/Queries/Sql.cs:85`) has no old-versus-new guard.
  It sets `DateModified=@mod` **and `ImportBatchId=@batchId`** unconditionally, so a row nothing changed
  is re-stamped as modified now and **re-attributed to the reseed's batch** — the latter is a genuine
  data change, not just a metadata bump.
- The Quote arm additionally runs `Sql.QuoteGenres.DeleteForQuote` followed by a full genre re-insert, so
  a quote's genre rows are deleted and recreated on every reseed.
- `QuoteSeedWriter.LogChangeAsync(… ChangeAction.Modified …)` (`QuoteSeedWriter.cs:41`) has no
  old-versus-new gate, which is what produces the 24 false `Audit_Change` rows per reseed measured above.

For Quotes it also inflates `ImportBatch.RecordCount`, which counts every non-Skip Quote `Modify` as
"updated" (`QuotinatorDatabaseInitializer.cs:691`).

**The issue's stated user-visible symptom does not survive checking, separately from all of the above.**
The claim is "one extra, differently-worded confirmation per fresh install".
`ReseedFileAppliedMetadataDto.IdentityComponents` dedupes on `fileName + origin + per-type
Added:Modified`. Cold start reports `Added=N, Modified=0`; the first reseed reports `Added=0,
Modified=25`. Those differ — but they differ *with the fix too* (`Added=0, Modified=1`), because `Added`
alone already changed, and the second reseed matches the first either way. The extra confirmation is the
cold-start→reseed transition, not this defect. Decision C records how that correction reaches the issue.

### An adjacent defect, deliberately not fixed here

**`CompletenessGuard.ShouldBlock` is fed a pre-rule field set at four of the five rule-consulting
sites.** #168's own comment claims *"ShouldBlock is evaluated against what would actually be WRITTEN
(resolved)"* — true for merge policies, false under Review-with-rules. `effectiveChanged` is computed at
`:445` (Quote), `:1115` and `:1271` (Source), `:1791` (Universe), `:1977` (Series) — all **before**
`ruleResolved` overwrites `resolved`. Only Season computes it after (`:2169`, then `:2180`). A `Complete`
row can therefore be Blocked over a field a rule was about to resolve away.

Decision B keeps this out of #377: step 3 computes a *second*, post-resolution field set for its own
no-op test and leaves `ShouldBlock`'s existing input untouched, so this issue changes no blocking
behaviour at all. Step 8 files the defect as its own issue.

---

## Decisions taken

All were the developer's, 2026-09-08, per `process.md`'s *Gap resolution* rule. Two overruled the
assistant's own recommendation and are recorded as such rather than quietly adopted.

**A. A classification fix, not a reporting one — a new persisted `ImportActionKind` member.** The
alternatives were a report-time bucket derived from `ExistingValue == MergedFields` (mirroring #374's
`Skipped`, no migration, but leaving the write and its false `Audit_Change` row in place) and reusing
`ImportActionKind.Unchanged` (no migration, terminal, but conflating *the file agreed with the database*
with *the file disagreed and the resolution kept the existing values* — the distinction #373 built its
incoming-versus-stored comparison for at `:451`–`:455`). Chosen: the new member, with the full ADR 008
checklist, exactly as #373 paid it for `Unchanged`.

**B. The `ShouldBlock` ordering defect is filed separately, not fixed here.** Turning a currently-Blocked
row into a Decided one is a behaviour change on a protection mechanism; it earns its own red tests and
its own T1 rather than riding along inside a classification fix, where it would also make this issue's
own reds harder to read.

**C. The issue's impact paragraph is corrected by a comment on #377, not by editing the body**, so the
original reading stays legible next to the measurement that revised it.

**D. The member is named `ResolvedToExisting`.** Alternatives considered: `Unwritten`, `NoOp`. The chosen
name says what happened rather than what did not, and reads correctly in a stored `Import_Action` row
with no surrounding context — the same standard `AlreadyApplied` and `Retirable` were named to in #374.

**E. The new bucket joins the confirmation dedupe key — overruling the assistant's recommendation.** The
plan argued for keeping it out, matching how `Unchanged` and `Skipped` are already excluded from
`IdentityComponents`. Overruled: a change in what a reseed did is a different result and should announce
itself.

**F. And so do the other outcome buckets.** The assistant then argued the cost back, on the grounds that
a bundled file gaining one quote would re-announce a confirmation where today it dedupes silently.
Overruled again, in the developer's own words: **"knowing that items have not changed and why they have
not changed is valuable information (period)."** So the identity is the full breakdown. The
re-announcement is the feature, not its cost — the same reading #373 applied when it decided an
unchanged entity type must appear in the report rather than being omitted.

---

## Cross-check against authoritative sources, 2026-09-08

Per `docs/workflow/process.md`'s Planning step 3.

1. **ADR 008 applies in full, following decision A.** `ImportActionKind.ResolvedToExisting` is a member
   of an enum backing a persisted column (`Import_Action.ActionType`), so: the `CHECK` is widened via a
   table-rebuild migration (SQLite cannot widen an inline CHECK), `QuotinatorMigrations.Baseline` is
   updated to match in the same commit, and both schema-drift tests are extended — the structural one and
   the CHECK-constraint one, since `PRAGMA table_info` does not capture what a constraint accepts. #373's
   migration 20 (`ImportActionUnchangedMigrations.WidenActionTypeForUnchanged`) is the worked example.

2. **ADR 016 introduces nothing new here.** No new enum *type* — a member is added to an existing one.

3. **ADR 014 is what makes the `Audit_Change` measurement load-bearing.** Audit-trail tables never purge
   and have no flagging mechanism, so each of the 24 false `Modified` entries a reseed writes is
   permanent. That is the argument that settled decision A, and it comes from an ADR the issue never
   mentions.

4. **No JSON schema covers this.** `schemas/` describes source files and rule files, not API responses or
   notification payloads — checked rather than assumed. `conflict-resolution-rules.schema.json` needs no
   new field: every input this fix reads is already recorded.

5. **The outcome buckets are enumerated by hand in four places**, all of which CLAUDE.md requires in the
   same commit as the behaviour: the seed log line (`QuotinatorDatabaseInitializer.FormatReport`),
   `docs/api-endpoints.md` (both occurrences), and the endpoint `[Description]` attributes. #373's
   verification row 20 is the guard, and its selector matches the enumeration's slash-joined tail.

6. **A new count in the confirmation body is three locale files in lockstep.**
   `ConfirmFileAppliedCleanlyAsync` builds `bodyArgs` as a single array applied to every language, and
   `TranslationCompletenessTests` fails on a key missing from `UI.nl.json`/`UI.de.json`. #374 already
   widened this array once, for `Skipped` — the same edit, one argument further.

7. **`ReseedFileAppliedMetadataDto.IdentityComponents` changes shape, per decisions E and F**, from
   `{EntityType}:{Added}:{Modified}` to the full breakdown. Its own doc comment explains why the ordering
   is load-bearing (the producer groups by planner emission order); that stays true and the `OrderBy` is
   not touched. Old stored payloads still compare — a stored payload missing the new field reads it as
   `0`, which is what row 21 holds.

8. **`docs/vocabulary.md` carries no entry for this outcome.** `ResolvedToExisting` is new project
   vocabulary and goes in that file in the same commit, per CLAUDE.md.

9. **#376 touches the same file and stays sequenced after this.** It adds an already-reported check to
   `PlanSourcesAsync`'s Pending path; #377 changes the same method's Modify classification. They do not
   conflict logically but do conflict textually, and `overview.md` already orders #377 first (row 23)
   with #376 immediately after (row 24). Keep that order.

10. **`ImportActionReportBuilder`'s two `_ => counts` fall-throughs are the trap.** #373 added `Incoming`
    counted *before* the switch precisely so a new kind matching no arm becomes visible as a broken
    identity rather than a silently dropped row. `ResolvedToExisting` needs its own arm in that switch,
    and `Incoming_EqualsTheSumOfEveryOutcome` is what catches forgetting it.

11. **`Skipped` must not absorb this and this must not absorb `Skipped`.** A `Skip`-policy Modify already
    resolves to the existing values *by construction* (`ImportActionPlanner.cs:436`), so a naive
    "merged equals existing" test would reclassify every Skip as `ResolvedToExisting` and erase the
    distinction #374 built: under Skip a real difference arrived and was deliberately discarded; here no
    difference survived resolution at all. The detection is gated on the action not being Skip-policy, and
    row 8 is the control that holds it.

12. **`DatabaseInitializerTests`' own harness cannot observe the write half, and that is why no existing
    test caught it.** `CreateInitializer` passes `NoOpChangeWriter.Instance` to
    `SqliteImportActionService`, so no `Audit_Change` row is ever written in that class. Rows 16–18 must
    construct their initializer with the real `ChangeWriter` — the measurement above had to do exactly
    this to see anything at all. A test written against the existing helper would pass whether or not the
    fix works.

13. **`FieldMergeResult.FromIncoming` is a near-signal the code already computes and discards.** For
    mechanism 1 it is empty, because `FieldMergeResolver` deliberately does not record a field it kept
    from the existing side (`:143`–`:146`). It is not a sufficient test on its own — a `Keep` decision is
    also absent from it, and a `Replace` of an identical value is present in it — so the detection still
    compares resolved against stored. Noted because it is the same shape as #373's own finding: the
    evidence was already being computed and thrown away.

---

## Steps

**The step order enforces red-first; it is not left to memory.** Step 1 writes every test and runs it
before any behaviour changes. **Red and green are per step** — each names the rows it owns, re-runs them
at the start to observe them fail, and ends with them passing.

**This issue's reds are easy to fake.** Most rows assert that something is *not* counted as `Modified`,
and a planner that classified every resolved row as a no-op would satisfy them all. Every such row
carries a control asserting a genuine change is still `Modify` — rows 4, 6, 8, 9, 11, 15 and 18.

**An exception is not a red.** #372 spent three steps with a row failing on `no such column` rather than
on its assertion, which looks identical for a test asserting the opposite. Read the failure message of
every row.

### 1. Write every test first, and run them red

**Status:** ⬜ Not started

**Owns rows 3–23.** Per `docs/testing-policy.md`'s signature-first rule, the signature lands before the
tests: `ImportActionKind.ResolvedToExisting` is added in this step, not step 2, because a test naming it
cannot compile without it and **a compile error is not a red test**. Adding the member alone is safe
because nothing writes it until step 3 — and if anything did, the un-widened CHECK would reject it,
which is exactly what row 1 asserts.

Rows 4, 6, 8, 9, 11, 15 and 18 are green by design from the start: they are the controls asserting what
must *not* change. Row 1's red is an exception and a correct one — `CHECK constraint failed: ActionType
IN ('Add', 'Modify', 'Unchanged')` names the constraint being widened rather than masking a wrong test,
the same call #373's step 1 made and recorded.

**Rows 16–18 need their own initializer wiring**, per cross-check 12: the real `ChangeWriter`, not
`DatabaseInitializerTests`' default `NoOpChangeWriter`. Written as a helper on that class rather than a
second copy of `CreateInitializer`.

The T2 canary runs against a pre-fix build per `docs/testing-policy.md` § Bug fixes: `git worktree add`
the commit before this work started, `docker build` under a distinct tag, execute
`11-clean-reseed-confirmation.md`'s new no-op assertion, confirm it fails, then remove the container,
image and worktree.

### 2. Add the migration and update the baseline

**Status:** ⬜ Not started

**Owns rows 1–2.** The ADR 008 checklist in one commit: a full table rebuild of `Import_Action` widening
`CHECK (ActionType IN ('Add', 'Modify', 'Unchanged', 'ResolvedToExisting'))`, the baseline's own
`CREATE TABLE` widened to match, and both drift tests extended. The copy carries every column straight
across and rewrites no value — only the constraint admits one more member, so every row valid before is
valid after. Migrations 15, 17, 18 and 20 all took this shape for the same SQLite reason.

### 3. Detect the no-op after resolution, at every Modify site

**Status:** ⬜ Not started

**Owns rows 3–11.** The classification test is *the resolved payload equals the stored payload*, computed
**after** any rule resolution has been applied — which is why a second field set is needed rather than
reusing `effectiveChanged`.

Five constraints, each of which a plausible implementation gets wrong:

1. **Do not move `ShouldBlock`'s input.** Per decision B, `effectiveChanged` keeps being computed where
   it is and keeps feeding `CompletenessGuard.ShouldBlock` unchanged; this step adds a *separate*
   post-resolution set for its own test. Season alone already computes post-rule (`:2169` → `:2180`) and
   needs no second computation — do not "make it consistent" with the others and regress the one site
   that is right.
2. **The dominant mechanism has no rule and no merge policy involved**, so the detection cannot live
   inside the rule branch. It belongs after `resolved` reaches its final value, on the path every Modify
   takes — which is also what makes one edit per site correct rather than one per mechanism.
3. **Only a would-be-`Decided` Modify is eligible.** `Blocked`, `Pending` and `Stale` never reach the
   terminal write, so reclassifying one would hide a row a human is waiting on. Row 9 is the guard.
4. **Skip is excluded** — cross-check 11. Row 8 holds that a Skip Modify stays `Skipped`.
5. **The action stays terminal and keeps its payload.** `Status` is `Applied` (nothing to decide, nothing
   to write, nothing to reverse — #373's own ruling for `Unchanged`), and `MergedFields` is still
   serialised, so `GET /import/actions` can still show what the resolution decided even though nothing
   was written. Traceability is why decision A chose a persisted member over a derived count.

Ten sites: Quote (`PlanAsync`'s Modify branch), Source ×2, Person, Character, Series, Universe, Season,
StageDirection, SoundCue, Conversation. Expect one shared helper rather than ten hand-written copies —
#373's step 4 recorded `UnchangedAction` being shared for exactly this reason ("nine hand-written copies
is how Series and Universe would quietly end up shaped differently from Source").

**Expect a blast radius in existing tests, and scope rather than loosen each one.** #373's step 4
estimated eight and found eighteen, because every `*_NoActionStaged`-shaped test asserted "no action of
any kind" where the real claim was narrower. Any test here asserting `Modify` for a row whose resolution
is a no-op is asserting the defect; each is rewritten to state what it actually guards, with the reason
recorded, never flipped to pass.

### 4. Report the new outcome

**Status:** ⬜ Not started

**Owns rows 12–15 and 19.** `ImportActionReportBuilder` gains its arm (cross-check 10),
`EntityTypeActionCounts` and `ReseedEntityCountDto` each gain the field, and
`QuotinatorDatabaseInitializer.FormatReport`'s hand-written log line gains it alongside the others.
`ImportBatch.RecordCount`'s `updated` count (`:691`) excludes it in the same commit — it counts writes,
and this is not one.

### 5. Widen the confirmation dedupe key to the full breakdown

**Status:** ⬜ Not started

**Owns rows 20–21.** Per decisions E and F, `IdentityComponents`' flattened tuple carries every outcome,
not `Added:Modified`. The `OrderBy(EntityType)` stays — its own doc comment explains why it is
load-bearing rather than tidiness. Row 21 is the row that matters here: a stored payload written before
this change must still compare, reading absent fields as `0` rather than throwing.

### 6. Say it in the notification's own words

**Status:** ⬜ Not started

**Owns row 22.** `ConfirmFileAppliedCleanlyAsync`'s `bodyArgs` gains the count and the body key states
it, in `UI.en-GB.json`, `UI.nl.json` and `UI.de.json` in the same commit. A body assembled in English
renders half-translated for a Dutch or German reader, which is what #319 exists to prevent.

### 7. Update the documented shape

**Status:** ⬜ Not started

**Owns row 23.** `docs/api-endpoints.md` (both occurrences), the endpoint `[Description]` attributes, and
`docs/vocabulary.md`'s new entry (cross-check 8) — all in one commit, and as a `docs` commit separate
from the code that motivated it, per `process.md`'s own rule on that split.

### 8. File the `ShouldBlock` ordering defect, and correct #377's impact paragraph

**Status:** ⬜ Not started

**Owns row 24.** Per decisions B and C. The new issue covers the pre-rule field set feeding
`CompletenessGuard.ShouldBlock` at `:445`, `:1115`, `:1271`, `:1791` and `:1977` — with Season named as
the one site already correct — and carries its own label and milestone in the same draft as its title
and body, per CLAUDE.md. The comment on #377 records what the measurement revised: that 24 of every 25
Modify actions on a reseed are no-ops rather than the single row the issue describes, that the
`Audit_Change` half accumulates rather than being one-time, and that the stated extra-confirmation
symptom is the cold-start→reseed transition rather than this defect.

### 9. Run the T2 document green, then hand T1 to the developer

**Status:** ⬜ Not started

**Owns rows 25–28.** `11-clean-reseed-confirmation.md` re-run live against a freshly built image, with
its own *Canary* section recording step 1's red run. T1 is the developer's own action and is the one row
this issue cannot close itself, per CLAUDE.md.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The migration and the baseline accept the same `ActionType` values | Unit test | `DatabaseInitializerOwnershipTests` CHECK-constraint drift test, extended with `ResolvedToExisting` and a rejected value, on both the baseline and the replay path |
| 2 | ❌ | The migration and the baseline produce an identical `Import_Action` schema | Unit test | the structural drift test, extended — a table rebuild is where a column silently changes shape |
| 3 | ❌ | A field absent from the incoming side, present in the stored one, is not a `Modify` | Unit test | `ImportActionPlannerTests.PlanAsync_IncomingOmitsAFieldTheStoredRowHas_StagesResolvedToExisting` — mechanism 1, 22 of every reseed's 24 no-ops |
| 4 | ❌ | A field absent from the *stored* side, present in the incoming one, is still a `Modify` | Unit test | `ImportActionPlannerTests.PlanAsync_IncomingSuppliesAFieldTheStoredRowLacks_StillStagesModify` — the control row 3 needs, and the direction that genuinely writes (`FieldMergeResolver.cs:147`) |
| 5 | ❌ | A rule reporting `AlreadyApplied` on a Source no longer stages a `Modify` | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_RuleResolvesToExactlyExistingValues_StagesResolvedToExisting` — mechanism 2, the Star Wars row |
| 6 | ❌ | A rule that genuinely changes a Source field still stages a `Modify` | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_RuleResolvesToADifferentValue_StillStagesModify` — the control row 5 needs: a planner classifying every rule-resolved row as a no-op passes row 5 perfectly |
| 7 | ❌ | The same holds at the other four rule-consulting sites | Unit test | `PlanSourcesAsync_ByNaturalKey_…`, `PlanUniverseAsync_…`, `PlanSeriesAsync_…`, `PlanSeasonsAsync_…` — one per site, because `Modify` is decided per branch independently |
| 8 | ❌ | A `Skip`-policy Modify is still `Skipped`, never reclassified | Unit test | `ImportActionPlannerTests.SkipPolicyModify_IsNotReclassifiedAsResolvedToExisting` — cross-check 11; a Skip resolves to the existing values by construction, and folding it in erases #374's distinction |
| 9 | ❌ | A Blocked, Pending or Stale Modify is never reclassified | Unit test | `ImportActionPlannerTests.NonTerminalModify_IsNotReclassifiedAsResolvedToExisting` — reclassifying one would hide a row a human is waiting on |
| 10 | ❌ | A merge policy resolving back to existing values is a no-op at a branch no rule reaches | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_MergeOursResolvesToExistingValues_StagesResolvedToExisting` — mechanism 3, and the row proving the fix is not rule-specific |
| 11 | ❌ | Season, the one site already computing post-rule, is unaffected | Unit test | `PlanSeasonsAsync_RuleResolvesToADifferentValue_StillStagesModify` — the control against step 3 "making every branch consistent" by regressing the correct one |
| 12 | ❌ | The report does not count a no-op as `Modified` | Unit test | `ImportActionReportBuilderTests`, extended with the new bucket |
| 13 | ❌ | `Incoming` still equals the sum of every outcome bucket | Unit test | `ImportActionReportBuilderTests.Incoming_EqualsTheSumOfEveryOutcome` (existing) — catches a new kind matching no arm and being silently dropped |
| 14 | ❌ | The seed log line prints the new bucket | Unit test | assertion over the formatted line, as #373's row 14 |
| 15 | ❌ | `ImportBatch.RecordCount` does not count a no-op as a write, but still counts a real one | Unit test | assertion over the batch row after a reseed — `QuotinatorDatabaseInitializer.cs:691`; the second clause is the control |
| 16 | ❌ | A no-op writes no change-log entry | Unit test | `DatabaseInitializerTests.Reseed_NoOpModify_WritesNoChangeEntry`, wired with the real `ChangeWriter` per cross-check 12 — with `NoOpChangeWriter` this row passes without the fix |
| 17 | ❌ | Repeated reseeds do not grow `Audit_Change` | Unit test | `DatabaseInitializerTests.Reseed_Repeatedly_ChangeEntryCountNeverGrows` — the measured defect: +25 per reseed today, 24 of them false |
| 18 | ❌ | A genuine modification still writes its change-log entry | Unit test | the control rows 16–17 need: an apply path that logged nothing at all would pass both |
| 19 | ❌ | A no-op leaves `DateModified` and `ImportBatchId` alone | Unit test | `SqliteImportActionServiceTests.ResolvedToExistingAction_DoesNotRestampTheRow` — `UpdateOnNewestWins` rewrites both unconditionally today, and the batch re-attribution is a real data change, not a metadata bump |
| 20 | ❌ | The confirmation dedupe key carries every outcome bucket | Unit test | `ReseedFileAppliedMetadataDtoTests` — two payloads differing only in `Unchanged` are no longer the same notification (decisions E and F) |
| 21 | ❌ | A confirmation written before this issue still compares and still renders | Unit test | `NotificationTableTests` plus the dedupe comparison — a stored payload with no such field reads as `0` rather than throwing. #302's confirmations are already persisted on the developer's own database; a payload change that cannot read them is a regression in reading history |
| 22 | ❌ | The new message text exists in all three locales | Unit test | `TranslationCompletenessTests` (existing) |
| 23 | ❌ | The documented breakdown matches what is returned | Unit test | assertion over `docs/api-endpoints.md` and the endpoint `[Description]` text — #373's row 20, extended |
| 24 | ❌ | The `ShouldBlock` ordering defect is filed with a label and a milestone | Issue | the new issue exists and is linked from this plan's Description — decision B |
| 25 | ❌ | A reseed of the real bundled corpus reports 1 modified, not 25 | Unit test | `DatabaseInitializerTests`, against the manifest-mirroring batch the measurement used — the measured number, asserted, so a regression is a failing test rather than a re-measurement |
| 26 | ❌ | The behaviour holds against the real corpus end to end | Automated (T2) | `11-clean-reseed-confirmation.md`, extended with a no-op assertion |
| 27 | ❌ | The T2 assertion goes red before it goes green | Canary run | recorded in that document's own *Canary* section, run against a pre-fix build per step 1 |
| 28 | ❌ | Build is clean and no regression | Build + test run | `dotnet build --configuration Release` → 0 warnings, 0 errors; `dotnet test --configuration Release -m:1` → all green |
| 29 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | Developer: cold start → reseed → reseed, with the reseed reporting one modified Source rather than 25 modified rows, and no new `Audit_Change` row for the 24 |

**Rows 4, 6, 8, 9, 11, 15 and 18 exist because "is not counted as Modified" is satisfied by counting
nothing at all.** A planner that classified *every* resolved row as a no-op would pass rows 3, 5, 7, 10,
12, 16, 17 and 25 without a single control failing. Row 4 is the sharpest: mechanism 1 and its mirror
image differ only in which side was empty, and a fix that keys on "a field was empty" rather than on
"the resolution changed nothing" would silently stop applying real enrichment.
