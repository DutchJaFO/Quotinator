# #181 — Minimal per-source conflict-resolution rule file + curated field-override preload

**Status:** Waiting for release
**GitHub issue:** #181
**Tiers required:** T1, T2
**Depends on:** #177 (dependency order established by #217, the parent tracking issue: #177 → #181 →
#153 — #177's `ImportBatches.Status` fix is needed first so the resolve→apply→reverse→retry cycle this
issue's own per-file conflict-resolution testing methodology relies on actually works); otherwise
unrelated to #179/#174/#180's Character/Series work; a hand-authored precursor to #153 (see Notes)

---

## Scope widened by #217 (2026-07-25) — 4 bundled files, not 2

**This issue's original scope (both the issue body below and this plan doc as first written) covers
only the two external bundled files (`vilaboim_movie-quotes.json`, `NikhilNamal17_popular-movie-
quotes.json`).** #217 (the parent tracking issue this issue now sits under, alongside #177 and #153)
widens that to **all 4 currently-bundled files**: `quotinator-curated.json` and `quotinator-series-
universe.json` (internally-authored) in addition to the two external ones. Rationale, per #217's own
Background: we cannot predict how a new bundled file will interact with data already seeded from
another one, and forcing every bundled file through `review` policy with its own conflict-resolution
file is what makes that interaction tractable — limiting this to only the two files that happen to
have externally-sourced conflicts today would leave the internally-authored files unverified against
the same standard. Items 4, 6, and 7 below are corrected accordingly; items 1, 2, 3, 5, 8, 9 are
unaffected by the widening (they describe mechanism, not a fixed file count).

**#217's per-file testing methodology also applies here**: each file (external-first exception aside)
goes through both a standalone Docker scenario (clean database, only that file) and, for every file
after the first, a layered scenario (clean database, every previously-processed file's conflicts
already resolved, then this file) — see #217's Background for the full methodology, including the
export-to-file-plus-markdown-presentation review process and the non-conflicting-field-correction
testing goal. File order: internal first (`quotinator-curated.json`, `quotinator-series-universe.json`),
then external (`NikhilNamal17_popular-movie-quotes.json`, `vilaboim_movie-quotes.json`) — exact order
within each pair confirmed at kickoff.

## Spec requirements (from the GitHub issue, corrected per the widened scope above)

1. Design the minimal rule-file schema, keyed by quote id + field name, matching #153's own Step 1
   discussion (content-hash keying is unnecessary for this issue's fully-known, small conflict set).
2. Add a manifest reference for each bundled file's rule file — lives alongside the file it governs.
3. Wire rule lookup into `ImportActionPlanner.PlanAsync`'s conflict-staging logic — if a matching rule
   exists, the action resolves automatically instead of staging `Pending`. **Corrected from the
   original single-branch framing**: the file has grown substantially since this issue was filed
   (#171–#180's Character/Series/Universe/Person/StageDirection/SoundCue/Conversation work each added
   their own `isPending` check) — there is no longer one shared "conflict-staging branch," but a
   separate `isPending` site per entity-specific `Plan*Async` method. #181's originally-known conflicts
   (NikhilNamal17's 9) are all Quote-level, so only `PlanAsync`'s own top-level Quote logic strictly
   needs the rule lookup for the original 2-file scope — but the widened 4-file scope may surface
   Series/Universe-level conflicts from `quotinator-series-universe.json` too, which would need the
   lookup wired into `PlanSeriesAsync`/`PlanUniverseAsync` as well. Confirm which sites actually need
   it once #217's Docker scenarios have run against all 4 files, rather than assuming Quote-only.
4. Set `duplicateResolution: review` for **all 4 currently-bundled files** in `data/sources/
   manifest.json` — `quotinator-curated.json`, `quotinator-series-universe.json`,
   `NikhilNamal17_popular-movie-quotes.json`, `vilaboim_movie-quotes.json` (widened from the original
   2-file scope).
5. Create `data/sources/quotinator-source-overrides.json` with the correct values for NikhilNamal17's
   9 known conflicts, sourced from #147's own findings table. Unaffected by the widening — #147's own
   known-conflict data is specific to NikhilNamal17 regardless of how many other files also move to
   `review`.
6. **One rule file per bundled file, all 4** — a rule file for `NikhilNamal17` (9 known entries), and
   initially-empty rule files for `vilaboim`, `quotinator-curated`, and `quotinator-series-universe`
   (populated as #217's own Docker scenarios surface real conflicts for each, widened from the
   original 2-file scope).
7. Confirm live: reseeding with `review` set for all 4 files produces zero staged `Pending` actions
   (widened from the original 2-file scope).
8. These per-source rule files double as smoke-test fixtures — add scenarios to CLAUDE.md's T2
   checklist in the same commit.
9. Update `153-declarative-conflict-resolution-plan.md` to note #153 builds on this issue's shipped
   format rather than inventing a new one — **already done** in this session's #153 rewrite
   (2026-07-25), ahead of this issue's own implementation; re-confirm the shipped shape still matches
   once #181 is actually implemented.

---

## Background — why this issue exists

Filed while preparing for the Data Enrichment milestone's known-conflict work (#147, kept unchanged
— not touched by this issue). Verified (2026-07-15) both bundled files should move from the silent
`skip` default to `review`. Counted directly against the bundled files: `vilaboim` has 0 internal
duplicate-id collisions, `NikhilNamal17` has exactly 9 — matching #147's own table precisely.

Rather than a one-off manual decide (which wouldn't persist across a future re-seed or
`Quotinator__AutoUpdateSources` refresh reintroducing the identical conflict), the goal is to resolve
conflicts as close to the source as possible: a small, per-source declarative rule file that staging
consults before ever creating a `Pending` action.

This is a deliberately **minimal, hand-authored slice** of #153's eventual design — #153's own Step 6
(rule lookup and auto-apply) is exactly what this issue builds, hand-authored rather than generated.
#153's remaining scope (generation from decided actions, staleness detection, a rule-file endpoint)
stays with #153, still gated on #163. This issue's rule-file format is what #153 builds on top of
later — `153-declarative-conflict-resolution-plan.md` has been updated to reflect this (its Steps 2
and 6 marked "Superseded by #181").

The curated field-override preload file (`data/sources/quotinator-source-overrides.json`) is
distinct in purpose from `quotinator-curated.json` (which adds wholly new, fully-curated quotes, not
corrections to rows seeded from elsewhere). It's what a source's own rule file resolves *against*.

---

## Steps

### 1. Design the minimal rule-file schema

**Status:** Done. `Quotinator.Data.Import.ConflictResolutionRule`/`ConflictResolutionRuleFile` (POCOs,
`JsonSerializer.Deserialize<T>`-compatible per this project's JSON parsing policy) reuse
`FieldResolutionChoice` directly for `resolution` — confirmed `Keep` already matches this issue's
"keep-existing" need exactly, no new enum member added. `ConflictRuleLookup` wraps a loaded rule file
for O(1) `quoteId + field` lookup, case-insensitive on quote id per this project's id-comparison
convention. Live at `src/Quotinator.Data/Import/ConflictResolutionRule.cs`,
`ConflictResolutionRuleFile.cs`, `ConflictRuleLookup.cs`.

**Shape revised twice from the original per-field-flat design above, both times from developer
feedback during implementation review** — the schema actually shipped is:
```json
{
  "rules": [
    {
      "quoteId": "<guid>",
      "existingRecord": { "quoteText": "...", "source": "...", "date": "...", "...": "..." },
      "incomingRecord": { "quoteText": "...", "source": "...", "date": "...", "...": "..." },
      "fields": [ { "field": "date", "resolution": "Keep" } ]
    }
  ]
}
```
Round 1: a bare `quoteId` conveys nothing to a human maintaining the file, so a per-field
`existingValue`/`incomingValue` pair was added. Round 2: even that wasn't enough — the developer pointed
out a human still has to guess which *other* fields might also differ, since only the one ruled field's
values were shown. Replaced with `existingRecord`/`incomingRecord` holding each side's **complete**
field set (an opaque `JsonElement`, not a typed `SourceQuote` — `Quotinator.Data` stays free of any
dependency on `Quotinator.Core`'s Quote-specific shape, per ADR 004), so a reviewer sees the whole
row on both sides and can judge for themselves whether an unruled field also genuinely differs. `fields`
is grouped by quote (one entry, `fields: [...]`) rather than flattened to one entry per field, since a
single quote can have more than one field in conflict at once (`ConflictRuleLookup`'s constructor
flattens this into its own internal per-field index). `schemas/conflict-resolution-rules.schema.json`
added to match, and wired into `SourceDataIntegrityTests` (new `RuleFiles_ConformToSchema` test,
`SourceFiles`/`SourceFiles_AllListedInManifest` updated to recognise a manifest entry's `ruleFile`
as a different shape from an entry's own `file`, not another quote source).

**Round 3 (same review pass)**: `Custom` resolution added — `Keep`/`Replace` can only pick a side's
existing value, but a field can be wrong/missing on *both* sides (e.g. the LOTR Fellowship quote's
`character` is `null` on both occurrences in the raw file; it's actually Galadriel). Reuses
`FieldMergeResolver.ResolveWithDecisions`'s existing "a decision always wins, ambiguous or not" contract
— no new merge logic needed, just a `customValue` field (required iff `resolution: "Custom"`, schema
enforces via `if`/`then`/`else`) threaded through `ConflictRuleLookup` as a `FieldMergeDecision`. Also
renamed `quoteId` → `entityId` (schema, DTOs, rule files) so one rule file can mix ids from more than
one entity type, and wired the same rule lookup into `PlanSeriesAsync`/`PlanUniverseAsync` — both had an
`if (changedFields.Count == 0) continue` early exit Quote's loop doesn't have, which would have skipped
a `Custom` rule entirely for a field that isn't ambiguous; fixed by computing `ruleDecisions` before that
exit and gating it on `changedFields.Count == 0 && ruleDecisions.Count == 0` instead.

**Scope-verification finding (re-verified live against the actual bundled file, not just #147's own
summary table)**: of the 9 NikhilNamal17 pairs #147 lists, only 6 have a genuine field conflict once
checked directly — 3 pairs (`"It's not about what I want..."`, `"Some men just want..."`, `"If you want
to get crazy..."`) are byte-for-byte identical on every field between their two occurrences and need no
rule at all. Two findings #147 never surfaced, because it only compared `date`: the Simpsons Movie pair
also differs on `source` casing (`"The Simpsons movie"` vs `"The Simpsons Movie"`), and the Zootopia
pair — which #147 calls "identical row" since both dates already agree — actually differs on `type`
(`movie` vs `anime`, the second occurrence is wrong). Both were folded into this issue's own rule-file
work rather than split out, since the mechanism is identical (developer decision, 2026-07-25).

**Cross-cutting fix folded in (developer decision, 2026-07-25)**: while investigating the Simpsons Movie
casing difference, found `FieldMergeResolver.ValuesEqual` (`src/Quotinator.Data/Import/
FieldMergeResolver.cs`) did a plain, case-sensitive `Equals(a, b)` for every scalar string field —
inconsistent with `QuoteIdentity.StableId`'s own case-insensitive normalisation for the *same* imported
value, and with this project's established case-insensitive-by-default convention (CLAUDE.md). Fixed to
compare case-insensitively via a private `ScalarComparer`, applied uniformly to every field (including
free-text ones like `quoteText`) rather than only identity-like fields — a deliberate developer decision
weighing the (small, accepted) risk of a pure-casing-only quote-text correction going unregistered
against the alternative of a growing per-field exemption list. Documented in CLAUDE.md alongside the
existing GUID/enum/id case-insensitivity section. This also means the Simpsons Movie pair no longer
needs a separate `source` rule — case-insensitive comparison already treats the two castings as equal,
leaving only its `date` conflict to resolve via a rule.

Keyed by quote id + field name — the simpler of the two options #153's own plan doc weighs (id+field
vs. content hash), justified here specifically because this issue's conflict set is small and fully
enumerated in advance (not an open-ended recurring stream from continuous upstream churn, which is
what motivated considering a content hash in #153's own Step 1). Shape, per file:

```json
{
  "rules": [
    { "quoteId": "<guid>", "field": "date", "resolution": "keep-existing" }
  ]
}
```

`"resolution": "keep-existing"` matches this issue's actual need (the curated-overrides file already
holds the correct value as the existing row; the rule just says "never let this specific bundled
source's re-import silently overwrite it"). A `"custom"` resolution kind with an inline value is not
needed here — reuse `FieldResolutionChoice`'s existing vocabulary (`FieldMergeResolver.cs`) rather
than inventing a third. Confirm at implementation time whether `FieldResolutionChoice.Keep`/`Custom`
already covers this exactly, per this project's DRY/SOLID convention (do not build a parallel
mechanism #153's own Step 3 already flags as the requirement to reuse).

### 2. Manifest reference

**Status:** Done — and fully wired end to end, not just schema. `ManifestFileEntryDto.RuleFile`
(`ruleFile` in JSON) added, `schemas/manifest.schema.json` updated in the same commit (also corrected
its stale `review` policy description while touching this section — it claimed review "behaves like
skip today", no longer true since #154). `SeedFile.RuleFilePath` carries the resolved absolute path
(same `Path.Combine(dir, ...)` convention as `FilePath` itself) from `ManifestSeedPlanner.PlanSeed`
through to `QuotinatorDatabaseInitializer.SeedIfEmptyInternalAsync`, which loads it via a new
`LoadConflictRules` helper (fail-open: missing/invalid file → `ConflictRuleLookup.Empty`, matching
`LoadSourceFileAsync`'s own convention for the source file itself) and passes the resulting
`ConflictRuleLookup` into `PlanAsync`. Proven end to end (not just unit-level) by
`DatabaseInitializerTests.InitialiseAsync_SecondFileReviewPolicyMatchingRule_AutoResolvesNoPendingActionLeft`
— a two-file seed batch where the second file's rule file resolves its only conflicting field and the
action applies immediately at startup with zero rows left `Pending`, plus a regression-guard sibling
test proving the no-rule-file case is unchanged.

### 3. Rule lookup and auto-apply wiring

**Status:** Done for the Quote-level site (`PlanAsync` itself). `ImportActionPlanner.cs` is confirmed at
1511 lines, at `src/Quotinator.Core/Database/ImportActionPlanner.cs` (not `Import/` as an earlier draft
of this doc assumed). **Correction to this step's own premise**: `isPending = policy ==
DuplicateResolutionPolicy.Review` (line 184, pre-change) is unconditional — it does **not** check
whether any field actually differs first. Re-importing an existing quote under `review` with every
field identical still stages a `Pending` Modify action (with an empty `ambiguousFields` set once
decided). The rule lookup added here doesn't change that unconditional-staging behaviour; it only
changes whether a genuinely *ambiguous* field can be pre-resolved.

Implementation: before the `isPending`/`status` computation, build a per-field decisions map from
`ConflictRuleLookup.TryResolve(quoteId, field)` for every key in `existingFields`, then call the
already-existing `FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, decisions)` —
reusing #149's own decide-machinery rather than writing new merge logic. If every genuinely-ambiguous
field has a matching rule, this succeeds and the action stages `Decided` with the rule-resolved
`MergedFields`, exactly like a human-decided action. If any ambiguous field has no rule,
`ResolveWithDecisions` throws `UnresolvedFieldConflictException` (caught, discarded) and staging falls
through to normal `Pending`, unaffected — a safe default matching this step's original design. The rule
lookup runs *after* `CompletenessGuard.ShouldBlock`, never before — a rule never bypasses the guard; a
`Complete` row still blocks a silent overwrite regardless of whether a rule could have resolved the
change (`PlanAsync_ReviewPolicy_MatchingRuleButCompletenessGuardBlocks_StillStagesBlockedNotDecided`).
No staleness detection (that's #153's own later addition).

Four new tests in `ImportActionPlannerTests.cs` cover: full rule coverage → `Decided` with correct
merged value; partial coverage (one of two ambiguous fields has a rule) → still `Pending`, regression
guard; a rule for a different quote id → no effect, regression guard matching pre-#181 behaviour; a
matching rule against a `Complete` row → still `Blocked`. Full solution suite green (30/30 projects,
0 warnings/errors) after this step.

**Scope widened beyond Quote (developer decision during review, ahead of any observed Series/Universe
conflict — a deliberate exception to "don't build speculatively", since the wiring cost was small and
known upfront)**: `PlanSeriesAsync`/`PlanUniverseAsync` also wired, with the early-exit fix from Round 3
above. Source/Person/Character/StageDirection/SoundCue/Conversation sites remain unwired — still gated
on an observed real conflict.

**`PlanSourcesAsync` wired too (2026-07-25), once that gating condition was met.** Live Docker
verification (Step 7/10) surfaced a genuine cross-file Source enrichment conflict:
`quotinator-curated.json` establishes "Star Wars: Episode V - The Empire Strikes Back" implicitly (via a
quote, with a real `date`), and `quotinator-series-universe.json`'s own `sources[]` entry for the same
title later tries to set its `seriesId` — a legitimate Modify with no way to auto-resolve under `review`,
since `PlanSourcesAsync` had never been wired to `conflictRules`. Wired in both of its branches (explicit-id
match and natural-key/enrichment match), same shape as `PlanSeriesAsync`/`PlanUniverseAsync` (ruleDecisions
built before the "nothing changed" early exit, `ResolveWithDecisions` tried after `CompletenessGuard`).
Two new tests (`PlanSourcesAsync_NoExplicitId_ReviewPolicy_NoMatchingRule_StagesPending`,
`...MatchingRule_StagesDecided`) cover it; a corresponding rule now lives in
`quotinator-series-universe-conflict-rules.json`.

### 4. Manifest policy change

**Status:** Done. All 4 files set to `duplicateResolution: { default: "review" }` in
`data/sources/manifest.json` (`quotinator-series-universe.json` already had it). File order also
reordered to match the confirmed kickoff order — `quotinator-curated.json`,
`quotinator-series-universe.json`, `NikhilNamal17_popular-movie-quotes.json`,
`vilaboim_movie-quotes.json` — since manifest file order is the actual seed order, needed for #217's
layered Docker scenarios to test in that sequence. Each entry also references its own `ruleFile`.

### 5. Curated field-override preload file

**Status:** Not needed — found during implementation, not built. #147's own text describes these as
**internal duplicates within `NikhilNamal17_popular-movie-quotes.json` itself** ("a duplicate whose
`firstSeenInFile` and `conflictFile` were both `NikhilNamal17_popular-movie-quotes.json`"), not a
cross-file collision against some other pre-existing row. Tracing `PlanAsync`'s Quote loop directly: the
*first* occurrence of a given quote id within one file's own import always stages as a fresh `Add`
(`existing is null`, unconditional, before any `isPending` logic runs at all) and immediately becomes
the in-memory `seenQuotes` baseline for any *later* occurrence of the same id **within that same
file/batch**. So the first-seen row already serves as the "existing" side for the second-seen row — no
separate pre-seeded override file is needed to establish a baseline; the rule file alone (`Keep` for
whichever side happens to be first-seen and correct, `Replace` for the one exception where the
second-seen value is the correct one) fully resolves all 6 genuine NikhilNamal17 conflicts. Re-confirmed
directly against the real bundled file line-by-line for all 6 (see step 6's rule file) before writing a
single rule — 5 of 6 need `Keep` (first-seen row correct); Captain Marvel's `date` needs `Replace` (the
*second*-seen row, 2019, is the film's real release year — the one pair where first-seen order and
correctness diverge). Verify this reasoning holds live in step 7 before treating it as settled — the
mechanism has never been exercised end to end against the real 732-entry file, only against small
synthetic fixtures in unit tests so far.

### 6. Author rule files for all 4 bundled files

**Status:** Done, but shape changed during review (see step 1's "Shape revised twice" note — grouped by
quote, full `existingRecord`/`incomingRecord`, not the flat `field`+`resolution`-only shape drafted
here originally). `data/sources/nikhilnamal17-conflict-rules.json`: 6 entries (not 9 — see step 1's
"Scope-verification finding": 3 of #147's 9 listed pairs are byte-for-byte identical on every field and
need no rule at all). `data/sources/vilaboim-conflict-rules.json`,
`data/sources/quotinator-curated-conflict-rules.json`,
`data/sources/quotinator-series-universe-conflict-rules.json`: each empty (`{"rules":[]}`), in place so
the manifest reference and lookup path are exercised identically for all 4 bundled files and so a real
conflict later found via #217's own Docker scenarios has a file already there to receive it.

### 7. Live verification

**Status:** Done (2026-07-25). A fresh Docker seed of all 4 files (`docker build` + `docker run`,
`quotinator:local`) produces zero `Pending` actions across all 4 batches — 799/799 unique quotes
seeded, no file left "staged awaiting review". `Quotinator.Tools.DbInspector` confirms no duplicate
`Sources` rows remain for any of the title clusters this issue's own review found and fixed (Star Wars
episodes, The Avengers, The Dark Knight, The Godfather Part II, Creed II, Zootopia all resolve to
exactly one row each). This subsumed #217's own per-file scenario (a)/(b) methodology — the same Docker
run exercises all 4 files in their real manifest order rather than as isolated fixtures.

**This step's own exercise is what surfaced two mechanism gaps beyond the originally-scoped 9 known
NikhilNamal17 conflicts** — see Step 10 for both: (1) a much larger set of title/type inconsistencies
within `NikhilNamal17_popular-movie-quotes.json` needing a new mechanism (`SourceAliasLookup`) rather
than a `ConflictResolutionRule`, and (2) cross-file duplicates between `vilaboim_movie-quotes.json` and
the other three files once NikhilNamal17's own conflicts stopped silently blocking its entire batch
from ever applying (a fresh seed previously landed only 112 quotes, not 799 — NikhilNamal17's whole
batch stayed `Staged`/unapplied as long as even one of its known conflicts sat `Pending`, since a batch
only auto-applies once every action in it is `Decided`).

### 8. Smoke-test fixtures and T2 checklist

**Status:** Done (2026-07-25). Added to CLAUDE.md's living T2 smoke-test checklist: fresh-seed
zero-pending-actions check, a `Quotinator.Tools.DbInspector` duplicate-Sources check, a negative test
(temporarily delete a rule, confirm the conflict stages `Pending` again), and a mutation test
(temporarily flip a rule's resolution, confirm the outcome changes) — all four live-verified against a
real Docker container, not just written and assumed correct.

**Live-verification finding, folded into the smoke-test scenario itself**: the mutation test's first
attempt used `GET /quotes/{id}` to check the outcome and found the date *unchanged* despite the rule
flipping from `Keep` to `Replace` — not a bug, but confirmation of the same Source-derived-vs-Quote-owned
distinction Step 10 documents. `date` is read via JOIN from `Sources.Date`, never stored on the Quote
itself; a per-quote rule only ever affects that Quote's own `MergedFields` audit trail. The checklist
now checks `System_ImportActions.MergedFields` directly via DbInspector instead, and documents this
limitation explicitly so a future reader doesn't waste time on the same wrong assumption.

### 9. Update #153's plan doc

**Status:** Done (this session, ahead of #181's own implementation — see
`153-declarative-conflict-resolution-plan.md`'s Steps 2 and 6, both marked "Superseded by #181").
Re-confirm at #181's actual implementation time that the shipped shape still matches what was
written there, updating further only if implementation reveals a genuine deviation.

### 10. Source-title canonicalization (found live during #217's full title-review pass)

**Status:** Done (2026-07-25).

**Why this exists, and why it's a different mechanism from steps 1–9.** #217's Docker verification
for this issue expanded (developer-directed) into a full manual review of every series/universe in
`quotinator-series-universe.json`, which surfaced ~30 cases in
`NikhilNamal17_popular-movie-quotes.json` where a quote's raw `source`/`type` text is simply a
misspelling/variant of an already-canonical Source (e.g. `"Avengers : Infinity War"` vs the correct
`"Avengers: Infinity War"`). Tracing `ImportActionPlanner.PlanAsync`'s Quote loop directly: `sourceId`
is resolved (`ResolveSourceAsync`, line ~107) from the **raw incoming** `q.Source`/`q.Type`, before
the `ConflictResolutionRule` lookup (steps 1–3 above) ever runs, and that same `sourceId` is baked
into `ExistingValue`/`IncomingValue`/`MergedFields` unchanged. A `ConflictResolutionRule` can correct
what a Quote's own `source`/`type` field *displays* in its audit trail, but never which Source row the
Quote actually links to — and `ConflictResolutionRule` only fires on the Modify path (an already-seen
entity id) in the first place, whereas nearly every one of these ~30 cases is a fresh `Add` (a
never-before-seen quote id), so the rule mechanism never even runs for them.

**Confirmed as a live, already-present bug, not just a gap in the new findings**: the one shipped rule
that *does* touch `type` — Zootopia (`nikhilnamal17-conflict-rules.json`, `type: Keep`, incoming
`"anime"` vs correct `"movie"`) — is a Modify-path case (intra-file duplicate, #147), yet still hits the
same root cause. `ResolveSourceAsync` runs with the raw `"anime"`, finds no matching Source, and queues
a genuine Source **Add** for a brand-new `EntityIdentity.SourceId("Zootopia", "anime")` row — a second,
spurious "Zootopia" Source distinct from the pre-existing correct movie one. The `type: Keep` rule then
corrects the Quote's own displayed `type` back to `"movie"`, but the Quote's `SourceId` FK still points
at the spurious anime-derived row. No existing test caught this — the regression tests for steps 1–3
only ever use identical `source` text on both sides, exercising a `quoteText`/`date`/`character` rule,
never a genuine `source`/`type` divergence.

**Decision (developer, 2026-07-25): fix properly now, on this feature branch, rather than defer** —
"we are free to rewrite mechanics to get the desired behaviour until we merge to main... this is all so
future milestones for UX and enrichment do not have to solve these problems on their own." Unlike the
Godfather/Shawshank cross-file quote-id-duplication finding (still deferred — see the scratch tracking
notes folded into this doc's history), this is squarely inside #181's own subject (bundled-file conflict
resolution) and the mechanism doesn't exist anywhere else yet.

**Design**: a new, separate mechanism — `SourceAliasRule`/`SourceAliasRuleFile`/`SourceAliasLookup`
(`Quotinator.Data/Import/`), keyed by raw `(title, type)` rather than by entity id, holding a straight
substitution (no Keep/Replace/Custom semantics needed — there's only ever one canonical answer):
```json
{
  "aliases": [
    { "title": "Avengers : Infinity War", "type": "movie", "canonicalTitle": "Avengers: Infinity War", "canonicalType": "movie" }
  ]
}
```
Consulted at the very top of `PlanAsync`'s Quote loop — immediately after the existing `#210`
id-canonicalization step that produces the working `q`, and before `ResolveSourceAsync`,
`ResolveCharacterAsync`, the existing-quote lookup, or the `ConflictResolutionRule` stage. Matching is
case-insensitive on both `title` and `type`, per this project's id/value-comparison convention. A match
produces a corrected `q` (title/type substituted); everything downstream — Source resolution, Character
resolution, the audit trail, the `ConflictResolutionRule` stage — then sees the canonical value with no
special-casing needed anywhere else. This is why it also fixes the Zootopia bug: once `q.Type` is
normalized to `"movie"` before `ResolveSourceAsync` ever runs, `existingFields["type"]` and
`incomingFields["type"]` already agree, so there's no conflict left for a rule to resolve — the
Zootopia `type: Keep` rule becomes dead code and is removed in favour of an alias entry.

**Scope**: Quote's own `ResolveSourceAsync` call only, for now. Character's `characters[]` schema also
carries a `sourceTitle`/`sourceType` pair (#175) resolved via a separate method
(`ResolveOrStageSourceIdAsync`) — not wired to aliases yet, since no current finding needs it; revisit
if a future conflict surfaces there (same "don't build speculatively" convention as step 3's own
Source/Person/Character/StageDirection/SoundCue/Conversation sites).

**File**: one alias file per bundled file that needs one, same manifest-reference pattern as
`ruleFile` (`sourceAliasFile` property) — `nikhilnamal17-source-aliases.json` holds 25 entries
(19 from the original title-review batch + Zootopia + Godfather Part II + 4 Star Wars episode
consolidations); `vilaboim-source-aliases.json` holds one (`"Star Wars"` → the same canonical Episode
IV title, found live once vilaboim's own raw "Star Wars" quote conflicted against NikhilNamal17's
already-aliased Source — the same quote/id, same real film, referenced by raw text independently in
two files, so each file needing the alias needs its own entry); `quotinator-curated`/
`quotinator-series-universe` get empty scaffolding (`{"aliases":[]}`), matching step 6's own rationale.

**Also folded into this step (2026-07-25, live findings during final Docker verification)**:
- `quotinator-series-universe.json` itself had "The Godfather II" and "The Godfather Part II" listed
  as two separate `sources[]` entries under the same series — an internal duplicate in the hand-crafted
  file, found by re-scanning the full `sources[]` list after fixing the alias mechanism, not by the
  original title-review pass. Removed "The Godfather II"; NikhilNamal17's own "The Godfather II" quote
  now aliases to "The Godfather Part II".
- The Star Wars trilogy split (done earlier, before this mechanism existed) only fixed *series
  assignment* — it never consolidated the 10 raw title spellings down to one canonical Source per
  film. Verified against actual quote content (not just title similarity) that 4 of the 10 were
  genuine duplicates of the same film under a different raw spelling (`"Star Wars"` bare vs
  `"...Episode IV..."`, `"...Empire Strikes Back"` with a comma vs a colon, two malformed Episode VI
  spellings, `"...Episode VII..."` vs the bare subtitle) — consolidated to 6 canonical Sources
  (Episodes I, III, IV, V, VI, VII), 4 new aliases added.
- 36 cross-file duplicate Quote conflicts surfaced between `vilaboim_movie-quotes.json` and
  NikhilNamal17/curated, once NikhilNamal17's own conflicts stopped silently blocking its whole batch
  from applying (see Step 7's note) — every one had `date` as the only genuinely differing field
  (vilaboim's raw format never carries a year), so all 36 resolved via `date: Keep` rules in
  `vilaboim-conflict-rules.json`, reviewed and approved row-by-row via a markdown table in chat, not
  decided unilaterally.
- James Bond was entirely unmodelled ("Goldfinger" existed as a bare, series-less Source) — added as a
  universe with an era-based sub-series (`"Sean Connery Era"`, mirroring the Star Wars trilogy-era
  pattern), matching the one Bond film currently in the bundled data.
- `PlanSourcesAsync` wiring (see Step 3's own note) was itself found via this step's live Docker run,
  not designed in ahead of time.

**Final live-verification result**: fresh Docker seed of all 4 files → 799/799 unique quotes, **zero**
`Pending` actions, no duplicate `Sources` row for any previously-conflicting title (confirmed via
`Quotinator.Tools.DbInspector`).

**Retroactive sourcing verification (2026-07-25, same session)**: the developer asked what sources
backed this session's title/date corrections. Honest answer: almost all of them came from recalled
model knowledge, not a live citable lookup — a gap against this project's own correctness priority.
Every factual claim made this session (every renamed/consolidated title, every added/changed release
date, which real film each Star Wars raw spelling belongs to) was retroactively re-verified via actual
web search. All confirmed correct — no corrections needed. The developer then asked a follow-up: that
retroactive pass itself used inconsistent, unscoped searches with no defined source priority (which is
exactly what let the Godfather Part II colon/no-colon conflict surface without a clean resolution rule)
— so a proper procedure doc was written: `docs/workflow/source-verification.md` (source tiers,
escalation order, conflict-resolution rule, linked from CLAUDE.md's Data Sources section). Future
`ConflictResolutionRule`/`SourceAliasRule` entries must follow that procedure, not recalled knowledge or
an arbitrary search order.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A matching rule auto-resolves without staging `Pending` | Unit test | `ImportActionPlannerTests.PlanAsync_ReviewPolicy_MatchingRuleCoversTheOnlyChangedField_StagesDecidedNotPending` |
| 2 | ✅ | No matching rule still stages `Pending` as today (regression guard) | Unit test | `ImportActionPlannerTests.PlanAsync_ReviewPolicy_NonMatchingRuleLookup_StagesPendingAsToday` |
| 3 | ✅ | The 6 genuine NikhilNamal17 conflicts (3 of #147's original 9 were byte-for-byte identical, needing no rule) auto-resolve via the rule file on live seeding | Live (T2) | Docker seed of `NikhilNamal17_popular-movie-quotes.json` — zero `Pending` actions, confirmed via `GET /import/actions?status=pending` |
| 4 | ✅ | All 4 bundled files seeding under `review` policy each produce zero staged actions | Live (T2) | Fresh Docker seed, all 4 files in real manifest order — 799/799 unique quotes, zero `Pending` |
| 5 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green (2337+ tests across all projects), 0 warnings, 0 errors |
| 6 | ✅ | T1 — app starts in Visual Studio, all 4 bundled files seed cleanly with zero pending actions | Live (T1) | Developer confirmed via a genuine database reset (`POST /admin/database/reset`) in Visual Studio (2026-07-25) — 799 quotes / 464 sources / 45 duplicates, matching the T2 Docker result exactly. Also caught a real gap Docker verification missed: "Dr. No" (James Bond, 1962) had never been added to the Sean Connery Era series alongside Goldfinger — fixed and re-verified. |
| 7 | ✅ | T2 — Docker smoke test: fresh seed produces zero pending actions for all 4 files; no duplicate `Sources` row for any previously-conflicting title | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `GET /import/actions?status=pending` + `Quotinator.Tools.DbInspector`; scenarios to be added to CLAUDE.md's T2 checklist (Step 8) |

---

## Notes

T1 and T2 are both required — this issue changes startup seeding behaviour and adds new bundled data
files (per this project's blanket T1/T2 rule).

This issue has no technical dependency on #179/#174/#180's Character/Series work — it is purely about
conflict resolution for the 4 currently-bundled files, and could be implemented independently of that
work's own internals. It now sits under **#217** (parent tracking issue, created 2026-07-25) alongside
#177 and #153, in the dependency order #177 → #181 → #153 — #177 first because #181's own testing
methodology (resolve → apply → reverse → retry, per #217's Background) needs a working
`POST /import/actions/reverse`, which #177 fixes. #181 before #153 because #153's own plan doc
(rewritten 2026-07-25, same session as this correction) explicitly builds on this issue's shipped
rule-file format rather than designing one from scratch.

**Scope widened 2026-07-25 (this correction), by #217's own explicit decision**: from 2 bundled files
to all 4. See the "Scope widened by #217" section near the top of this doc for the full reasoning —
in short, the two internally-authored files (`quotinator-curated.json`, `quotinator-series-
universe.json`) get the same `review`-policy-plus-rule-file treatment as the two external ones, so
every bundled file is verified against the same conflict-resolution standard rather than only the
ones that happened to have known external conflicts already.

#147 (the 9 known NikhilNamal17 conflicts, "Data Enrichment" milestone) is deliberately left open and
untouched by this issue — this issue is pipeline/mechanism work that happens to resolve #147's known
conflicts as a side effect, not a reclassification of #147's own scope or milestone.
