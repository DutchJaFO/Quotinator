# #181 — Minimal per-source conflict-resolution rule file + curated field-override preload

**Status:** Planning
**GitHub issue:** #181
**Tiers required:** T1, T2
**Depends on:** none technically; unrelated to #179/#174/#180's Character/Series work; a hand-authored
precursor to #153 (see Notes)

---

## Spec requirements (from the GitHub issue)

1. Design the minimal rule-file schema, keyed by quote id + field name, matching #153's own Step 1
   discussion (content-hash keying is unnecessary for this issue's fully-known, small conflict set).
2. Add a manifest reference for each bundled file's rule file — lives alongside the file it governs.
3. Wire rule lookup into `ImportActionPlanner.PlanAsync`'s existing conflict-staging branch — if a
   matching rule exists, the action resolves automatically instead of staging `Pending`.
4. Set `duplicateResolution: review` for both `vilaboim_movie-quotes.json` and
   `NikhilNamal17_popular-movie-quotes.json` in `data/sources/manifest.json`.
5. Create `data/sources/quotinator-source-overrides.json` with the correct values for NikhilNamal17's
   9 known conflicts, sourced from #147's own findings table.
6. One rule file per source — a rule file for `NikhilNamal17` (9 entries) and a separate one for
   `vilaboim` (currently empty).
7. Confirm live: reseeding with `review` set for both files produces zero staged `Pending` actions.
8. These per-source rule files double as smoke-test fixtures — add scenarios to CLAUDE.md's T2
   checklist in the same commit.
9. Update `153-declarative-conflict-resolution-plan.md` to note #153 builds on this issue's shipped
   format rather than inventing a new one.

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

**Status:** Not started.

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

**Status:** Not started.

Add a `ruleFile` (or similar) property to the relevant `ManifestFileEntryDto` entries for
`vilaboim_movie-quotes.json` and `NikhilNamal17_popular-movie-quotes.json` in
`data/sources/manifest.json`, pointing at each source's own rule file. Add the corresponding property
to `schemas/manifest.schema.json` (both files' entries need it; `additionalProperties: false` means
the schema must be updated in the same commit or manifest validation rejects the new property).

### 3. Rule lookup and auto-apply wiring

**Status:** Not started.

`ImportActionPlanner.PlanAsync`'s existing conflict-staging branch (`ImportActionPlanner.cs:96-140`)
stages `Pending` unconditionally today when policy is `Review` and a field differs
(`ImportActionPlanner.cs:140`'s `isPending` check). This step adds a rule lookup before that check:
if a loaded rule matches the field, the action stages `Decided` with the rule's resolution already
applied, instead of `Pending`. No staleness detection (that's #153's own later addition) — a rule
that no longer matches current data (e.g. the conflict shape has changed) simply doesn't match and
falls through to normal `Pending` staging, which is a safe default in the absence of staleness logic,
not a gap this issue needs to close.

### 4. Manifest policy change

**Status:** Not started.

`data/sources/manifest.json`: set `duplicateResolution: { default: "review" }` for both
`vilaboim_movie-quotes.json` and `NikhilNamal17_popular-movie-quotes.json` entries, overriding the
top-level bundled default of `skip`.

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

### 6. Author the two per-source rule files

**Status:** Not started.

`data/sources/nikhilnamal17-conflict-rules.json` (or similar naming, confirm at implementation time
against this project's existing bundled-file naming convention): 9 entries, one per known conflict,
each `"resolution": "keep-existing"` pointing at the value step 5's override file establishes.
`data/sources/vilaboim-conflict-rules.json`: empty `rules: []`, added purely so the manifest
reference and lookup path are exercised identically for both bundled files, and so a future vilaboim
conflict (should one ever arise from an upstream refresh) has a file already in place to receive it.

### 7. Live verification

**Status:** Not started.

Reseed (or fresh-seed) with both files' `review` policy and rule files in place — confirm
`GET /import/actions?status=pending` returns zero entries for the NikhilNamal17/vilaboim batches,
and `Quotinator.Tools.DbInspector` shows all 9 previously-conflicting quotes now hold the
override-file's corrected `date` value.

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
| 1 | ❌ | A matching rule auto-resolves without staging `Pending` | Unit test | `Quotinator.Engine.Tests.PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` — starts red |
| 2 | ❌ | No matching rule still stages `Pending` as today (regression guard) | Unit test | `Quotinator.Engine.Tests.PlanAsync_NoMatchingRule_StagesPendingAsToday` — starts red |
| 3 | ❌ | All 9 known NikhilNamal17 conflicts auto-resolve via the rule file on seeding | Unit test | `Quotinator.Engine.Tests.SeedNikhilNamal17_AllNineKnownConflicts_AutoResolveViaRuleFile` — starts red |
| 4 | ❌ | Vilaboim seeding under `review` policy produces zero staged actions | Unit test | `Quotinator.Engine.Tests.SeedVilaboim_ReviewPolicy_NoStagedActions` — starts red |
| 5 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 6 | ❌ | T1 — app starts in Visual Studio, both bundled files seed cleanly with zero pending actions | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 7 | ❌ | T2 — Docker smoke test: fresh seed produces zero pending actions for both files; a rule-file edit changes the resolved outcome on next reseed; a field with no matching rule still stages `Pending` | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `GET /import/actions?status=pending` + `Quotinator.Tools.DbInspector`; scenarios added to CLAUDE.md's T2 checklist per step 8 |

---

## Notes

T1 and T2 are both required — this issue changes startup seeding behaviour and adds new bundled data
files (per this project's blanket T1/T2 rule).

This issue has no dependency on #179/#174/#180's Character/Series work — it is purely about
Source/Quote-level conflict resolution for the two currently-bundled external sources, and could be
implemented independently, in any order relative to that work. It is sequenced in `overview.md`
directly before #153 because #153's own plan doc now explicitly builds on this issue's shipped
format — the ordering reflects that relationship, not a hard technical blocking dependency in either
direction (#181 doesn't need #153 or #163 to exist first, and #153 doesn't strictly need #181 either,
though skipping it would mean #153 designs its rule-file format from zero instead of confirming an
already-working one).

#147 (the 9 known NikhilNamal17 conflicts, "Data Enrichment" milestone) is deliberately left open and
untouched by this issue — this issue is pipeline/mechanism work that happens to resolve #147's known
conflicts as a side effect, not a reclassification of #147's own scope or milestone.
