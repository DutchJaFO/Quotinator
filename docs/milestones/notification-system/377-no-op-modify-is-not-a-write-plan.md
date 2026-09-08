# #377 — A Modify action whose resolution is a genuine no-op is still counted as Modified

**Status:** In progress (step 8)
**GitHub issue:** #377
**Tiers required:** T1, T2
**Depends on:** [#373](https://github.com/DutchJaFO/Quotinator/issues/373), [#374](https://github.com/DutchJaFO/Quotinator/issues/374)

**Next action: step 8.** Steps 1–7 are done; the full solution is green (4013 passed, 0 failed) at 0
warnings. What remains is filing the `ShouldBlock` defect and #377's own correction comment (step 8),
teaching the external-data sentinel (step 9), and the T2/T1 runs (step 10).

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

**This is a planning-time read of `data/sources/`, which `docs/testing-policy.md` permits and the
verification checklist does not repeat.** That policy's rule is *"reading such a file while writing a
test, to obtain a value, is fine; reading it while running one is not"* — so the numbers above are
obtained once, here, and pinned as literals; no test in this issue reads the bundled corpus at run
time. The one place the real corpus is still watched is the suite's own external-data sentinel, which
step 9 extends.

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

### Nothing has to be fixed before this issue can run

Checked rather than assumed, 2026-09-08. `#373` and `#374` are `Waiting for release` — their code is on
this branch already, so the dependency is satisfied, not pending. The `ShouldBlock` defect above is
explicitly not a prerequisite (decision B). `#376` is sequenced *after* this issue, not before it.

**The one that needed proving is [#381](https://github.com/DutchJaFO/Quotinator/issues/381)**, because
step 1's fixture work showed a quote re-stated without its character produces a Modify whose merged
payload keeps the character *name* while dropping `CharacterId`. If #377's detection swallowed that row
it would classify it terminal, stop applying it, and mask an open defect behind this fix. It does not:
stored and resolved differ on `CharacterId`, so the row is not payload-identical and is never a no-op.
`ImportActionPlannerTests.PlanAsync_IncomingOmitsTheCharacterLink_IsNotAResolvedToExistingNoOp` asserts
exactly that — green today and required to stay green — which turns the independence claim into a
checked one rather than a reasoned one. #381 stays independently visible and independently fixable.

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

13. **`docs/testing-policy.md` forbids what this plan's first draft asked for, and the rule landed one
    commit before this issue was planned** (`18418c29`, 2026-09-08): *"a unit test must not read
    `data/sources/` or `scripts/cache/` at run time"*, with a single named exception — the suite's
    external-data sentinel,
    `docs/automated-testing/import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md`.
    The first draft's own row 25 asserted a bundled-corpus reseed reports one modified row rather than
    25; that row is deleted, and every unit test here builds its own fixture. The bundled corpus is
    watched in exactly one place, by step 9, which is the point of having a sentinel at all.

14. **The sentinel's job is to reveal a missing rule, and that is what makes it the right home for the
    bundled-corpus check here** (developer, 2026-09-08). Its step 5 already states the principle —
    *"the only place in the project allowed to go red because the outside world changed rather than
    because this project broke"* — and its step 4C is the worked precedent for the shape step 9 needs:
    a population that is legitimate once *declared*, asserted to be empty of undeclared members, so an
    unexplained one fails rather than being listed for a human to eyeball. `process.md` refuses the
    listing shape outright, and 14's own text records nine wrong dates that sat in the database while a
    listing passed.

15. **`FieldMergeResult.FromIncoming` is a near-signal the code already computes and discards.** For
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

**Every row states its positive and its negative half, and neither is ticked alone.**
`docs/testing-policy.md` § *Every test proves the positive result as well as the negative* is the rule,
and it is not T2-scoped: *"a test that only asserts a failure passes against a build that fails
everything"*, with its own clause for unit tests — *"a success control beside the failure variants"*.
`docs/automated-testing/README.md` carries the T2 form, `docs/release-verification.md` the tier form.
It was restated on 2026-09-08 because this plan's first draft applied it to only seven of its rows.
This issue needs it more than most: nearly every requirement is *something is no longer counted as
`Modified`*, and a
planner that classified **every** resolved row as a no-op would satisfy all of them at once while
destroying the import. So the checklist pairs them in the same row rather than scattering controls into
rows of their own — a separate control row can be ticked independently, and then the pairing exists only
in prose.

**Every test builds its own fixture; none reads the bundled corpus** — `docs/testing-policy.md`, and
cross-check 13. The measured numbers in the Description are pinned literals obtained once at planning
time, not something a test re-derives.

**An exception is not a red.** #372 spent three steps with a row failing on `no such column` rather than
on its assertion, which looks identical for a test asserting the opposite. Read the failure message of
every row.

### 1. Write every test first, and run them red

**Status:** ✅ Done for every unit-test row, 2026-09-08 — each positive confirmed red on its own
assertion and each negative green. **The two T2 canaries (rows 23 and 25) are not run and are owned by
steps 9 and 10**, since both need a pre-fix Docker image; they stay ❌ until then rather than being
ticked on the strength of the unit tests. `ImportActionKind.ResolvedToExisting` and the two count properties
landed here as signatures only (the report builder stubbed to `0`), confirmed behaviour-neutral against
the existing suite — the same call #373's step 2 recorded for `Unchanged`.

Rows 2 and 19 needed no new test: `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportActionsSchema`
and `TranslationCompletenessTests` already own them and are green guards that must stay green.

**One fixture was red for the wrong reason and was corrected before step 3 relied on it.** The
mechanism-1 planner test passed `conflictRules: null`, which sends a Review Modify down the *Pending*
path — production always supplies at least an empty `ConflictRuleLookup`, so the action never reaches
the Decided path the classification acts on. It would have stayed red no matter how correct the fix
was. This is exactly the failure `docs/testing-policy.md` warns about under red-first: a red status is
not evidence until its *message* has been read.

**An extra test was added that the checklist does not list**, answering a question step 1 raised rather
than one it planned: `PlanAsync_IncomingOmitsTheCharacterLink_IsNotAResolvedToExistingNoOp`. See
*Nothing has to be fixed before this issue can run*.

**Owns rows 1–20.** Per `docs/testing-policy.md`'s signature-first rule, the signature lands before the
tests: `ImportActionKind.ResolvedToExisting` is added in this step, not step 2, because a test naming it
cannot compile without it and **a compile error is not a red test**. Adding the member alone is safe
because nothing writes it until step 3 — and if anything did, the un-widened CHECK would reject it,
which is exactly what row 1 asserts.

**Each row's negative half is expected green from the start and must stay green throughout.** That is
what makes the pairing worth anything: a positive going green while its negative goes red is the
over-broad fix announcing itself, and the two are read together at the end of every step, not only at
the end of the issue. Row 1's red is an exception and a correct one — `CHECK constraint failed:
ActionType IN ('Add', 'Modify', 'Unchanged')` names the constraint being widened rather than masking a
wrong test, the same call #373's step 1 made and recorded.

**Fixtures, not the bundled corpus, in every row.** The fixture mechanism 1 needs is small — a file
seeding a quote with a value, re-stated with that value absent — and it reproduces the 22-row case
without pinning anything to what vilaboim happens to contain.

**The absent field must be one the quote owns outright, and `character` is not one.** Found while
writing rows 14–16, 2026-09-08: a fixture built on `character` appears to work and silently measures
the wrong thing. Its staged sequence is `Add` → `Modify` → **`Unchanged`**, because the first reseed's
"no-op" is not a no-op at all — the merged payload keeps the character name while the apply writes
`CharacterId = null`, so the row genuinely changes and the second reseed then finds nothing to do. That
is [#381](https://github.com/DutchJaFO/Quotinator/issues/381)'s defect, not this one, and reading its
output as #377's would have produced the conclusion that no-op change entries do *not* accumulate — the
opposite of what the corpus measured. Rebuilt on `genres` (quote-owned, no FK), the sequence is
`Add` → `Modify` → `Modify` and the change-entry count grows per reseed, matching the measurement.
`date` is unsuitable for the same class of reason: it lives on the Source row, not the quote. Mechanism 2 needs
a two-file fixture plus a one-rule rule file; mechanism 3 needs a merge policy on the second file.

**Rows 14–16 need their own initializer wiring**, per cross-check 12: the real `ChangeWriter`, not
`DatabaseInitializerTests`' default `NoOpChangeWriter`. Written as a helper on that class rather than a
second copy of `CreateInitializer`. Row 14's negative half is what makes the wiring non-optional — with
`NoOpChangeWriter` in place, *both* halves of rows 14 and 15 pass whether or not the fix works, because
nothing writes a change entry at all.

The T2 canary runs against a pre-fix build per `docs/testing-policy.md` § Bug fixes: `git worktree add`
the commit before this work started, `docker build` under a distinct tag, execute
`11-clean-reseed-confirmation.md`'s new no-op assertion, confirm it fails, then remove the container,
image and worktree.

### 2. Add the migration and update the baseline

**Status:** ✅ Done, 2026-09-08 — migration 21
(`ImportActionResolvedToExistingMigrations.WidenActionTypeForResolvedToExisting`), baseline widened in
the same commit, rows 1–2 green: both the CHECK-constraint drift test and the structural one agree
across the baseline and incremental-replay paths.

**Owns rows 1–2.** The ADR 008 checklist in one commit: a full table rebuild of `Import_Action` widening
`CHECK (ActionType IN ('Add', 'Modify', 'Unchanged', 'ResolvedToExisting'))`, the baseline's own
`CREATE TABLE` widened to match, and both drift tests extended. The copy carries every column straight
across and rewrites no value — only the constraint admits one more member, so every row valid before is
valid after. Migrations 15, 17, 18 and 20 all took this shape for the same SQLite reason.

### 3. Detect the no-op after resolution, at every Modify site

**Status:** ✅ Done, 2026-09-08 — all ten sites converted through one shared helper
(`ImportActionPlanner.ResolvesToExisting`). Rows 3–9 and 14–16 green; build clean at 0 warnings.

**The comparison is of serialized payloads, not of merged fields**, and that is a decision rather than
convenience: the payload is what the apply path writes, and it carries the links a field map does not
(a Quote's `SourceId`/`CharacterId`/`PersonId`). A field-level test would call a row a no-op while its
`CharacterId` was being dropped — #381's shape, which must stay visible rather than be masked.

**Season needed one thing the other nine did not, and the plan did not anticipate it.** Its payload
carries `SeriesName`, which comes from the import entry rather than from either side's stored data, and
the existing-side payload is built without it — so a straight payload comparison called every Season a
real change. The no-op test uses an existing-side copy carrying the same non-written metadata, leaving
the action's own `ExistingValue` untouched, since that record is not this issue's to change. Worth
stating because it is the general hazard: the rule is *compare what would be written*, and a payload
that carries anything else needs that field neutralised on both sides.

**Three sibling tests asserted `Decided` for what is now `ResolvedToExisting`/`Applied`** — the Universe,
Series and Season `..._ReviewPolicy_MatchingRule_StagesDecidedNotPending` trio. Each keeps the claim it
was written for (a matching rule auto-resolves rather than leaving the row for a human), asserted
directly as "not `Pending`" with the new terminal outcome beside it, rather than having its old value
flipped to whatever now passes.

---

## A fourth counting surface, found during execution — decided and delivered

**Decision G (developer, 2026-09-08): option A.** `ImportSummary` gains its own `ResolvedToExisting`
count, matching what steps 4–5 do for the other surfaces. Delivered with step 4;
`ImportAsync_MergeOurs_TrueConflictKeepsExisting` now asserts `Updated = 0`, `ResolvedToExisting = 1`
and — the point of the decision — that the summary's own parts still sum to `Total`.

**`ImportResultResponse.Summary`, the response body of `POST /api/v1/import`, is a counting surface this
plan never enumerated.**

Found by `QuoteImportServiceTests.ImportAsync_MergeOurs_TrueConflictKeepsExisting`, which imports a
quote, re-imports a conflicting version under `MergeOurs`, and asserts both that the stored text is
unchanged *and* that `Summary.Updated == 1`. Those two assertions are the defect this issue exists to
fix, on a surface the plan did not scope: nothing was written, and the summary reported a write.

The classification now makes `Updated` come back `0`, which is why the test fails. That is arguably
correct — but it leaves the row unaccounted for. `ImportSummary` carries `Total`, `Imported`, `Updated`,
`Skipped` and a failure count; with `Updated` dropping to `0` and no bucket of its own, a no-op row is in
`Total` and in nothing else, so the summary stops adding up. That is precisely the "two different
nothings" problem #373 solved for the reseed report, reappearing on the import endpoint.

It needs a decision because `ImportSummary` is a **public API response shape**, documented in
`docs/api-endpoints.md`, and every option changes an existing contract:

| | What it means |
|---|---|
| **A** | Add `resolvedToExisting` to `ImportSummary`, matching what steps 4–5 do for the other surfaces. Additive to the response, keeps the total adding up, costs a documented contract change and its own tests. |
| **B** | Fold a no-op into the existing `Skipped` count, whose documented meaning is already "matched an existing quote and was left unchanged (`skip`/`review`)". No new field; blurs a distinction #374 spent an issue drawing, since a `Skip` row is a discarded difference and this one is a resolved one. |
| **C** | Leave `Updated` counting no-ops on this endpoint only. No contract change; the endpoint keeps reporting a write that did not happen, and the two counting surfaces disagree about the same row. |

**B and C were rejected on the reasoning that produced #373 and #374**: B blurs a distinction #374 spent
a whole issue drawing, and C leaves one endpoint reporting a write that did not happen while the other
surfaces disagree about the same row.

**The general lesson, recorded because it will recur:** the plan enumerated counting surfaces by reading
the reseed path, and missed one reachable only from the import path. A classification change has as many
reporting surfaces as there are callers, and the way to find them is to change the classification and
see what fails — which is what happened here.

**Owns rows 3–9 and 14–16.** The classification test is *the resolved payload equals the stored payload*, computed
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

**Constraint 5 is what carries the entire write side, and that is not obvious from reading it.**
Verified rather than assumed: `ImportActionResolutionCoordinator.TryApplyBatchAsync` selects
`actions.Where(a => a.Status.Parsed == ImportActionStatus.Decided)` and passes only those to the apply
callback. An action staged `Applied` is therefore never applied at all — no `UpdateOnNewestWins`, so no
`DateModified` restamp and no `ImportBatchId` re-attribution; no genre delete-and-reinsert; no
`LogChangeAsync`, so no `Audit_Change` row. Rows 14–16 fall out of that one choice and need no separate
code. **The corollary is the trap:** staging the new kind as `Decided` instead would look entirely
harmless — the report would still be correct, rows 3–9 would still pass — while reopening every write-
side defect this issue exists to close, silently. Rows 14–16 are what stop that, which is why they sit
in this step rather than in step 4.

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

**Status:** ✅ Done, 2026-09-08 — rows 10–13 green. `ImportActionReportBuilder` gained its arm,
`EntityTypeActionCounts` and `ReseedEntityCountDto` their field, and `FormatReport` its printed count.
`ImportBatch.RecordCount` needed no change: its `updated` tally counts `Modify` actions, and the new
kind falls out of it by construction.

Also delivered here, per decision G: `ImportSummary.ResolvedToExisting`, the fourth counting surface
found during execution — see the section above.

**Row 13 was red for a fixture reason, not a product one, and it is worth recording.**
`LatestBatchRecordCountAsync` ordered by `AppliedAt`, which is second-resolution — a cold start and the
reseed that follows it inside the same second tie, and the cold start's own `RecordCount` came back.
Re-ordered by `rowid`, which is insertion order and unambiguous.

**Owns rows 10–13.** `ImportActionReportBuilder` gains its arm (cross-check 10),
`EntityTypeActionCounts` and `ReseedEntityCountDto` each gain the field, and
`QuotinatorDatabaseInitializer.FormatReport`'s hand-written log line gains it alongside the others.
`ImportBatch.RecordCount`'s `updated` count (`:691`) excludes it in the same commit — it counts writes,
and this is not one.

### 5. Widen the confirmation dedupe key to the full breakdown

**Status:** ✅ Done, 2026-09-08 — rows 17–18 green. `IdentityComponents` carries every outcome bucket,
not `Added:Modified`; the `OrderBy(EntityType)` is untouched. An older stored payload still compares and
still reads its absent count as `0`.

**Owns rows 17–18.** Per decisions E and F, `IdentityComponents`' flattened tuple carries every outcome,
not `Added:Modified`. The `OrderBy(EntityType)` stays — its own doc comment explains why it is
load-bearing rather than tidiness. Row 21 is the row that matters here: a stored payload written before
this change must still compare, reading absent fields as `0` rather than throwing.

### 6. Say it in the notification's own words

**Status:** ✅ Done, 2026-09-08 — row 19 green. `bodyArgs` gained the count and both body keys state it,
in `UI.en-GB.json`, `UI.nl.json` and `UI.de.json` in one commit.

**Owns row 19.** `ConfirmFileAppliedCleanlyAsync`'s `bodyArgs` gains the count and the body key states
it, in `UI.en-GB.json`, `UI.nl.json` and `UI.de.json` in the same commit. A body assembled in English
renders half-translated for a Dutch or German reader, which is what #319 exists to prevent.

### 7. Update the documented shape

**Status:** ✅ Done, 2026-09-08 — row 20 green. `docs/api-endpoints.md` (both occurrences), the three
endpoint `[Description]` attributes and `docs/vocabulary.md`'s new entry. Kept with the code rather than
split into its own `docs` commit, because CLAUDE.md's API-doc rule requires `api-endpoints.md` and the
`[Description]` attributes to move together and `vocabulary.md` to land with the term it defines — the
more specific rule wins over the general docs-separate-from-code one.

**Owns row 20.** `docs/api-endpoints.md` (both occurrences), the endpoint `[Description]` attributes, and
`docs/vocabulary.md`'s new entry (cross-check 8) — all in one commit, and as a `docs` commit separate
from the code that motivated it, per `process.md`'s own rule on that split.

### 8. File the `ShouldBlock` ordering defect, and correct #377's impact paragraph

**Status:** ⬜ Not started

**Owns row 21.** Per decisions B and C. The new issue covers the pre-rule field set feeding
`CompletenessGuard.ShouldBlock` at `:445`, `:1115`, `:1271`, `:1791` and `:1977` — with Season named as
the one site already correct — and carries its own label and milestone in the same draft as its title
and body, per CLAUDE.md. The comment on #377 records what the measurement revised: that 24 of every 25
Modify actions on a reseed are no-ops rather than the single row the issue describes, that the
`Audit_Change` half accumulates rather than being one-time, and that the stated extra-confirmation
symptom is the cold-start→reseed transition rather than this defect.

### 9. Teach the external-data sentinel to reveal a missing rule

**Status:** ⬜ Not started

**Owns rows 22–23.** The half of this issue that only the bundled corpus can answer, and therefore the
half that belongs in `docs/automated-testing/import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md`
rather than in any unit test (cross-checks 13 and 14).

**What changes for the corpus once step 3 lands.** Today a reseed reports 25 modified rows and the
operator cannot tell the one real one from the 24 that wrote nothing. Afterwards it reports one, and the
24 become a named, countable population — which is the first time anyone can ask the question the
developer wants asked: *does each of these want a declared rule, or is it legitimately nothing?* The two
answers already in the measurement are different, which is why the check has to report rather than
assume:

- The 22 dateless vilaboim quotes need **no** rule. Upstream genuinely has no year; the stored value
  wins, and that is the correct and permanent outcome.
- The Star Wars Source is a rule that reports `AlreadyApplied` on every reseed after the first. It is
  **not** retirable either (#374: an `AlreadyApplied` rule is still doing work — it is what stops the
  incoming file re-imposing the wrong value), so it too is permanent.

Both are legitimate; neither is currently *declared* as legitimate anywhere. So the check follows step
4C's own shape rather than inventing one: **assert that every `ResolvedToExisting` row is one somebody
has accounted for, and fail on an undeclared one.** A new no-op appearing after a source refresh is then
a finding with a name, not a number that drifted — exactly the sentinel's stated purpose, and exactly
what 14's own history says a listing fails to deliver ("nine wrong dates sat in the database while a
listing passed").

**Its positive half is as load-bearing as its negative one.** Asserting "no undeclared no-ops" is
satisfied by a build that produces no no-ops at all — including one where step 3 broke the import
outright — so the step also asserts the known population is *present*: the count is non-zero and names
the entity types it covers, the same way 14's step 2 checks `zeroCounts` is empty rather than checking a
total.

**Written and run red first**, like any other test in this project — against a pre-fix image, where the
rows are still classified `Modify` and the new assertion therefore cannot pass.

### 10. Run the T2 documents green, then hand T1 to the developer

**Status:** ⬜ Not started

**Owns rows 24–27.** `11-clean-reseed-confirmation.md` and the extended `14-…` re-run live against a
freshly built image, each with its own *Canary* section recording the red run. T1 is the developer's own
action and is the one row this issue cannot close itself, per CLAUDE.md.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
**Every row states both halves and is ticked only when both hold.** The positive is what the fix must
make true; the negative is what it must leave alone. This issue's positives are almost all "no longer
counted as `Modified`", which a planner that classified *everything* as a no-op would satisfy perfectly
— so a positive with no paired negative proves nothing here.

| # | Status | Requirement | Method | Verification — **positive** / **negative** |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The `ActionType` CHECK admits the new member and nothing more | Unit test | `DatabaseInitializerOwnershipTests`' CHECK-constraint drift test, extended, on both the baseline and the replay path. **Positive:** `ResolvedToExisting` inserts. **Negative:** an unknown value is still rejected — a CHECK widened to admit anything would pass the positive alone |
| 2 | ✅ | The migration and the baseline produce an identical `Import_Action` schema | Unit test | the structural drift test, extended. **Positive:** baseline and incremental replay agree after the rebuild. **Negative:** the same test still fails on an introduced column difference — a table rebuild is where a column silently changes shape |
| 3 | ✅ | A field the incoming side omits and the stored row has resolves to a no-op, while the reverse still writes | Unit test | Mechanism 1 — 22 of every reseed's 24, on a two-file fixture (one seeding a dated quote, one re-stating the same id with `date: null`). **Positive:** `PlanAsync_IncomingOmitsAFieldTheStoredRowHas_StagesResolvedToExisting`. **Negative:** `PlanAsync_IncomingSuppliesAFieldTheStoredRowLacks_StillStagesModify` — the mirror direction genuinely writes (`FieldMergeResolver.cs:147`), and a fix keying on "a field was empty" rather than "the resolution changed nothing" would silently stop applying real enrichment |
| 4 | ✅ | A rule reporting `AlreadyApplied` on a Source is a no-op, while a rule that changes something is not | Unit test | Mechanism 2, on a fixture rule file — not the bundled one. **Positive:** `PlanSourcesAsync_RuleResolvesToExactlyExistingValues_StagesResolvedToExisting`. **Negative:** `PlanSourcesAsync_RuleResolvesToADifferentValue_StillStagesModify` — without it, a planner treating every rule-resolved row as a no-op passes the positive perfectly |
| 5 | ✅ | The same pair holds at the other four rule-consulting sites | Unit test | `PlanSourcesAsync_ByNaturalKey_…`, `PlanUniverseAsync_…`, `PlanSeriesAsync_…`, `PlanSeasonsAsync_…`, each with **both** halves — `Modify` is decided per branch independently, so a per-site positive without its per-site negative proves only that site was touched, not that it was touched correctly |
| 6 | ✅ | Season, the one site already computing post-rule, is not regressed | Unit test | **Positive:** `PlanSeasonsAsync_RuleResolvesToExactlyExistingValues_StagesResolvedToExisting`. **Negative:** `PlanSeasonsAsync_RuleResolvesToADifferentValue_StillStagesModify` — the guard against step 3 "making every branch consistent" by breaking the one branch that was already right |
| 7 | ✅ | A merge policy resolving back to the existing values is a no-op at a branch no rule reaches | Unit test | Mechanism 3, and the pair proving the fix is not rule-specific. **Positive:** `PlanCharactersAsync_MergeOursResolvesToExistingValues_StagesResolvedToExisting`. **Negative:** `PlanCharactersAsync_MergeOursResolvesToADifferentValue_StillStagesModify` |
| 8 | ✅ | Skip and no-op stay separate buckets in both directions | Unit test | Cross-check 11 — a Skip resolves to the existing values by construction, so a naive "merged equals existing" test swallows it. **Positive:** `SkipPolicyModify_IsStillSkipped_NotResolvedToExisting`. **Negative:** `ReviewPolicyNoOp_IsNotCountedAsSkipped` — the reverse confusion, which would erase #374's distinction from the other side |
| 9 | ✅ | Only a would-be-`Decided` Modify is reclassified | Unit test | **Positive:** `DecidedNoOpModify_IsReclassified`. **Negative:** `BlockedPendingOrStaleNoOpModify_IsNotReclassified` — a gate that never reclassified anything would pass the negative alone, and reclassifying one of these would hide a row a human is waiting on |
| 10 | ✅ | The report counts a no-op in its own bucket and a real change in `Modified` | Unit test | `ImportActionReportBuilderTests`, extended. **Positive:** a no-op lands in the new bucket. **Negative:** a genuine Modify still lands in `Modified` — a builder routing everything to the new bucket satisfies the positive |
| 11 | ✅ | `Incoming` still equals the sum of every outcome bucket | Unit test | `ImportActionReportBuilderTests.Incoming_EqualsTheSumOfEveryOutcome` (existing). **Positive:** the identity holds with the new bucket populated. **Negative:** the same test still fails when driven with an action matching no arm — the guard against `_ => counts` silently dropping the new kind |
| 12 | ✅ | The seed log line prints the new bucket alongside the others | Unit test | assertion over the formatted line, as #373's row 14. **Positive:** the new count appears. **Negative:** the six existing counts still appear and still carry their own values |
| 13 | ✅ | `ImportBatch.RecordCount` counts real writes only | Unit test | assertion over the batch row after a reseed — `QuotinatorDatabaseInitializer.cs:691`. **Positive:** a no-op does not increment it. **Negative:** a genuine update still does — a count stuck at zero would pass the positive |
| 14 | ✅ | The change log records real modifications and only those | Unit test | `DatabaseInitializerTests`, wired with the real `ChangeWriter` per cross-check 12. **Positive:** `Reseed_NoOpModify_WritesNoChangeEntry`. **Negative:** `Reseed_GenuineModify_StillWritesItsChangeEntry` — with `NoOpChangeWriter` in place, or an apply path that logged nothing at all, the positive passes without the fix |
| 15 | ✅ | `Audit_Change` stops growing on unchanged content but still grows on changed content | Unit test | The measured defect: +25 per reseed today, 24 of them false. **Positive:** `Reseed_Repeatedly_ChangeEntryCountNeverGrows`. **Negative:** `Reseed_WithGenuinelyChangedContent_DoesAddChangeEntries` |
| 16 | ✅ | A no-op leaves `DateModified` and `ImportBatchId` alone, a real write does not | Unit test | `UpdateOnNewestWins` rewrites both unconditionally today, and the batch re-attribution is a real data change rather than a metadata bump. **Positive:** `ResolvedToExistingAction_DoesNotRestampTheRow`. **Negative:** `GenuineModify_StillRestampsTheRow` |
| 17 | ✅ | The dedupe key distinguishes a different result and still suppresses an identical one | Unit test | `ReseedFileAppliedMetadataDtoTests`, decisions E and F. **Positive:** two payloads differing only in `Unchanged` are no longer the same notification. **Negative:** two payloads with an identical breakdown still are — an identity that never matches would announce a confirmation on every reseed, which is the defect #302 was filed for |
| 18 | ✅ | A confirmation written before this issue still compares and still renders | Unit test | `NotificationTableTests` plus the dedupe comparison. **Positive:** a stored payload with no such field reads it as `0`. **Negative:** it renders rather than throwing. #302's confirmations are already persisted on the developer's own database; a payload change that cannot read them is a regression in reading history |
| 19 | ✅ | The new message text exists in all three locales | Unit test | `TranslationCompletenessTests` (existing). **Positive:** the new key resolves in `en-GB`, `nl` and `de`. **Negative:** the test still fails on a key deliberately emptied in one file — it catches missing *and* empty, and only the second half proves it |
| 20 | ✅ | The documented breakdown matches what is returned | Unit test | assertion over `docs/api-endpoints.md` and the endpoint `[Description]` text — #373's row 20, extended. **Positive:** the new bucket is named in both. **Negative:** the assertion still fails against a description listing the old set, which is what #373 recorded its selector being wrong about twice |
| 21 | ❌ | The `ShouldBlock` ordering defect is filed with a label and a milestone | Issue | the new issue exists and is linked from this plan's Description — decision B |
| 22 | ❌ | The bundled corpus reveals a no-op nobody has accounted for | Automated (T2) | `14-fresh-seed-produces-zero-pending-actions.md`, extended per step 9 — the suite's external-data sentinel and, per `docs/testing-policy.md`, the only place allowed to read `data/sources/` at run time. **Positive:** the known no-op population is present, non-zero, and names the entity types it covers. **Negative:** undeclared no-ops = 0 — asserted, not listed, following that document's own step 4C. Without the positive half a build that broke the import outright and produced no actions at all would pass |
| 23 | ❌ | That sentinel assertion goes red before it goes green | Canary run | recorded in `14-…`'s own *Canary* section, run against a pre-fix image where the rows are still `Modify` |
| 24 | ❌ | The confirmation behaviour holds end to end | Automated (T2) | `11-clean-reseed-confirmation.md`, extended with a no-op assertion |
| 25 | ❌ | That assertion goes red before it goes green | Canary run | recorded in that document's own *Canary* section, run against a pre-fix build per step 1 |
| 26 | ✅ | Build is clean and no regression | Build + test run | `dotnet build --configuration Release` → 0 warnings, 0 errors; `dotnet test --configuration Release -m:1` → all green |
| 27 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | Developer: cold start → reseed → reseed. **Positive:** the reseed reports one modified Source rather than 25 modified rows, and writes no new `Audit_Change` row for the other 24. **Negative:** the quote counts and every entity type are unchanged from the cold start — a reseed that reports nothing modified because it imported nothing would satisfy the positive |

**The negative halves are not ceremony; three of them are the only thing standing between this fix and a
worse defect.** Row 3's would catch a fix that stops applying genuine enrichment. Row 14's would catch
the change log going silent altogether — and cannot even run without the `ChangeWriter` wiring
cross-check 12 describes. Row 22's positive would catch a build that passed the "no undeclared no-ops"
assertion by breaking the import so thoroughly that there were no actions to declare.
