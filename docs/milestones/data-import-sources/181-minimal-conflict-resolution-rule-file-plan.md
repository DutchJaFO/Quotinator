# #181 — Minimal per-source conflict-resolution rule file + curated field-override preload

**Status:** In progress
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

**Scope per the widening above**: the originally-known conflicts (NikhilNamal17's 9) are Quote-level
only, so the top-level `PlanAsync` Quote logic is the minimum needed for the original 2-file scope.
Confirm via #217's own Docker scenarios (run against all 4 files) whether `quotinator-series-
universe.json` surfaces any real Series/Universe-level conflicts — if it does, `PlanSeriesAsync`/
`PlanUniverseAsync` need the same lookup wired in too; if it doesn't, only the Quote site is needed for
this issue's actual shipped scope, and the other entity types' sites are left unwired until a real
conflict from one of them is observed (matching this project's "don't build speculatively" convention).

### 4. Manifest policy change

**Status:** Not started.

`data/sources/manifest.json`: set `duplicateResolution: { default: "review" }` for **all 4
currently-bundled files** — `quotinator-curated.json`, `quotinator-series-universe.json`,
`NikhilNamal17_popular-movie-quotes.json`, `vilaboim_movie-quotes.json` (widened from the original
2-file scope) — overriding the top-level bundled default of `skip`.

### 5. Curated field-override preload file

**Status:** Not started.

`data/sources/quotinator-source-overrides.json` — same flat-quote schema shape as
`quotinator-curated.json` (per `schemas/source-flat.schema.json`), but populated only with the 9
NikhilNamal17 quote ids and their corrected `date` field, sourced directly from #147's findings
table (the correct year per pair — #147 itself doesn't state which of the two dates is authoritative
for every pair; where #147 doesn't already make this obvious, this step's own judgement call is
recorded here at implementation time, not silently assumed). Added to the manifest and seeded
*before* `NikhilNamal17_popular-movie-quotes.json` in seed order, so the correct value exists as the
"existing" row by the time NikhilNamal17's own (conflicting) row is processed.

### 6. Author rule files for all 4 bundled files

**Status:** Not started.

`data/sources/nikhilnamal17-conflict-rules.json` (or similar naming, confirm at implementation time
against this project's existing bundled-file naming convention): 9 entries, one per known conflict,
each `"resolution": "keep-existing"` pointing at the value step 5's override file establishes.
`data/sources/vilaboim-conflict-rules.json`, `data/sources/quotinator-curated-conflict-rules.json`,
`data/sources/quotinator-series-universe-conflict-rules.json`: each initially empty (`rules: []`),
added purely so the manifest reference and lookup path are exercised identically for all 4 bundled
files, and so a real conflict later found via #217's own Docker scenarios (should one arise for any of
these three) has a file already in place to receive it (widened from the original 2-file scope).

### 7. Live verification

**Status:** Not started.

Reseed (or fresh-seed) with all 4 files' `review` policy and rule files in place — confirm
`GET /import/actions?status=pending` returns zero entries for any of the 4 batches, and
`Quotinator.Tools.DbInspector` shows all 9 previously-conflicting NikhilNamal17 quotes now hold the
override-file's corrected `date` value (widened from the original 2-file scope). Cross-reference
against #217's own per-file Docker scenario methodology (Background section) — this step's live
verification and that methodology's scenario (a)/(b) runs are largely the same exercise, not two
separate verification passes.

### 8. Smoke-test fixtures and T2 checklist

**Status:** Not started.

Add to CLAUDE.md's living T2 smoke-test checklist (its own "only grows" convention): a scenario using
the shipped rule files directly — reseed and confirm zero `Pending` actions (matching rule exists);
temporarily add a new, deliberately non-matching field to a rule entry and reseed, confirming the
corresponding conflict *does* stage `Pending` (no matching rule); temporarily change a rule's
resolution value and reseed, confirming the auto-resolved outcome changes accordingly (proves the
lookup genuinely reads the rule file's content rather than a cached/hardcoded value).

### 9. Update #153's plan doc

**Status:** Done (this session, ahead of #181's own implementation — see
`153-declarative-conflict-resolution-plan.md`'s Steps 2 and 6, both marked "Superseded by #181").
Re-confirm at #181's actual implementation time that the shipped shape still matches what was
written there, updating further only if implementation reveals a genuine deviation.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A matching rule auto-resolves without staging `Pending` | Unit test | `Quotinator.Core.Tests.PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` — starts red |
| 2 | ❌ | No matching rule still stages `Pending` as today (regression guard) | Unit test | `Quotinator.Core.Tests.PlanAsync_NoMatchingRule_StagesPendingAsToday` — starts red |
| 3 | ❌ | All 9 known NikhilNamal17 conflicts auto-resolve via the rule file on seeding | Unit test | `Quotinator.Core.Tests.SeedNikhilNamal17_AllNineKnownConflicts_AutoResolveViaRuleFile` — starts red |
| 4 | ❌ | Vilaboim, quotinator-curated, and quotinator-series-universe seeding under `review` policy each produce zero staged actions (widened from vilaboim-only) | Unit test | `Quotinator.Core.Tests.SeedVilaboim_ReviewPolicy_NoStagedActions`, plus equivalents for the other two internally-authored files — starts red |
| 5 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 6 | ❌ | T1 — app starts in Visual Studio, all 4 bundled files seed cleanly with zero pending actions | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 7 | ❌ | T2 — Docker smoke test: fresh seed produces zero pending actions for all 4 files; a rule-file edit changes the resolved outcome on next reseed; a field with no matching rule still stages `Pending`; #217's own per-file scenario (a)/(b) Docker runs exercise this live for each file in turn | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `GET /import/actions?status=pending` + `Quotinator.Tools.DbInspector`; scenarios added to CLAUDE.md's T2 checklist per step 8 |

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
