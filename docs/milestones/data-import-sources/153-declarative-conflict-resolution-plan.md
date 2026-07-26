# #153 — Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2)

**Status:** Planning
**GitHub issue:** #153
**Tiers required:** T1, T2
**Depends on:** #149, #154 (both shipped); #163 (shipped — its flat export row shape is real code now,
not prose); #181 (implemented and T1/T2-verified 2026-07-25, on this same feature branch — not yet
merged to `main` or tagged. #181 shipped *two* separate rule mechanisms, not the one this plan doc was
originally written around — see the Scope note below. #153 continues on this same branch, so #181's
code is available to build on regardless of `main`'s state)

---

## Scope note

This plan doc was originally written before #163 existed. **#163 has since shipped in full**:
`GET /import/actions/export`/`POST /import/actions/bulk-decide`, with the real, code-level flat row
shape `ImportActionFieldRow` (`src/Quotinator.Core/Models/ImportActionFieldRow.cs`) — `ActionId`,
`EntityId`, `EntityType`, `Field`, `ExistingValue`, `IncomingValue`, `Decision`, `CustomValue`,
`MarkCompletenessAs`. Step 1's identity question and the generation steps below, both previously
blocked on this shape existing, are designed against the real thing, not prose speculation.

**#181 shipped two separate rule mechanisms, not the one this plan doc was originally designed
around.** #181's own live verification found that a single rule mechanism keyed by entity id could not
prevent duplicate Source rows caused by a misspelled/inconsistent title — a `ConflictResolutionRule`
only ever corrects what a Quote's own field *displays* in its `MergedFields` audit trail, never which
Source row it actually links to, because `ResolveSourceAsync` resolves a Source from the *raw*
incoming title/type before any entity-id-keyed rule ever runs. #181 therefore shipped:

1. `ConflictResolutionRule`/`ruleFile` — keyed by **entity id + field name**, consulted only on the
   Modify path (an already-seen id).
2. `SourceAliasRule`/`sourceAliasFile` — keyed by **raw `(title, type)`**, not an entity id at all,
   consulted at the very top of `PlanAsync`'s Quote loop *before* `ResolveSourceAsync` ever runs, so it
   applies uniformly to both a first-seen Add and a re-seen Modify.

**Decided 2026-07-26: this issue's own generation and staleness-detection work covers both
mechanisms, not just `ConflictResolutionRule`.** An earlier draft of this plan doc recommended limiting
scope to `ConflictResolutionRule` only, reasoning that every alias #181 shipped came from manual
title-verification research with no decided-action shape to generate one from — that reasoning still
holds for *why* alias generation has to look different (see Step 13), but the developer's explicit
direction is to build both, not defer the alias half. Every step below reflects that decision; there is
no remaining open question about scope.

---

## Spec requirements (from the GitHub issue)

From `gh issue view 153`'s "What needs to be done" list (numbering preserved from the issue):

1. Decide and document what identifies "the same conflict" across separate import runs — quote Id +
   field name, or a content hash of the conflicting values — so a stored resolution rule reliably
   reapplies to the right conflict next time and not to an unrelated one that happens to share a
   quote Id.
2. The manifest gains a reference to its rule file. By default, the rule file lives alongside the
   imported file or the manifest itself — matching whichever folder the file it governs is already
   in, rather than introducing a new, separate location convention.
3. Reuse #149's `FieldMergeResolver`/`FieldResolutionChoice` machinery — extend it only where a
   genuine gap requires it (DRY/SOLID: do not build a parallel mechanism that duplicates what already
   exists).
4. Build a mechanism that flags a rule as invalid/stale when the underlying source's shape changes
   enough that silently reapplying it would produce a wrong result — a definite requirement, not
   optional.
5. Implement rule generation from a batch's already-decided actions (worst case one rule per action,
   best case one shared rule). Expose this via the rule-file endpoint itself — the same endpoint that
   provides the rule file to a user also supports *adding* generated rules to it (merging into an
   existing file, not only ever generating a fresh one). Hand-authoring the file from scratch remains
   fully supported. As a bonus, this generation mechanism doubles as a way to produce realistic
   smoke-test fixtures from real staged/seeded data. Rule storage per the location decided in item 2,
   and rule lookup/application during future staging so a matching rule auto-resolves instead of
   leaving the action `Pending`.
6. `README.md`/`addon/DOCS.md` updated to document the new mechanism, if it introduces any new
   endpoint or user-facing file format.

**Not in scope** (per the issue): #149's interactive decide/undo/apply workflow — already shipped;
this issue automates *recurring* cases only, not a replacement for manual review of one-off
conflicts.

**Expected tests** (from the issue's own table, all starting red):

| Test class | Test method |
|---|---|
| New: `Quotinator.Core.Tests` | `PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` |
| New: `Quotinator.Core.Tests` | `PlanAsync_NoMatchingRule_StagesPendingAsToday` |
| New: `Quotinator.Core.Tests` | `RuleGeneration_StaleSourceShape_FlagsRuleRatherThanApplying` |
| New: `Quotinator.Core.Tests` | `GenerateRuleFile_FromDecidedBatchActions_ProducesCandidateRules` |
| New: `Quotinator.Core.Tests` | `GenerateRuleFile_MergesIntoExistingRuleFile_DoesNotOverwriteManualEdits` |

**Correction:** the first two rows' exact method names don't exist in the codebase — #181 already
shipped this behaviour under different names (see Step 16). Kept verbatim above since this is a direct
quote of the issue's own text; treat the first two rows as "behaviour already covered," not literal
test names to create.

---

## Steps

### 1. "Same conflict" identity — resolved: Id + field name

**Status:** Resolved. Was open in the issue text ("quote Id + field name, or a content hash of the
conflicting values"); #163 landing and #181's precedent close it.

`ImportActionFieldRow` (`ActionId`, `EntityId`, `EntityType`, `Field`, ...) settles the question:
**Id + field name**, matching the row shape exactly rather than inventing a parallel identity scheme.

The one open concern against this — "a recurring third-party source does not guarantee stable quote
Ids across refreshes unless the upstream itself is Id-stable" — is not a real risk. Confirmed against
`EntityIdentity`/`QuoteIdentity.StableId` (`src/Quotinator.Core/Import/EntityIdentity.cs`): every
entity id in this codebase, quotes included, is a **deterministic hash of the entity's own normalised
natural-key content** (quote text, source, etc. — see `StableId`'s
`string.Join('|', parts.Select(QuoteIdentity.Normalise))` construction), never anything the upstream
source itself supplies. So as long as a re-scraped upstream file's quote text/source pair is
unchanged, Quotinator computes the identical id on every refresh regardless of whether the upstream
source's own row ordering, internal ids, or file structure changed at all.

`ConflictResolutionRule` already ships this precedent (keyed by entity id + field name). This issue's
generated rules inherit the same scheme rather than introducing a second one.

### 2. Rule file storage location and manifest reference

**Status:** Done via #181, for both mechanisms.

Per item 2, the rule file lives alongside the file it governs, not a new separate location. #181 ships
this file-entry-only (not manifest-level) for both mechanisms: `ManifestFileEntryDto.RuleFile`/
`ruleFile` for `ConflictResolutionRule`, and a second, independent
`ManifestFileEntryDto.SourceAliasFile`/`sourceAliasFile` for `SourceAliasRule` — one bundled file can
reference either, both, or neither. This issue's own generation work (Steps 10–14) writes into
whichever of the two an entity's generated rule belongs to; no new manifest property is needed for
either mechanism.

### 3. `ConflictResolutionRule` × `FieldMergeResolver` reuse

**Status:** Done via #181. `ImportActionPlanner.cs` calls
`FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions)` directly from
every one of the five `ConflictRuleLookup` call sites (Quote loop, `PlanSourcesAsync` ×2,
`PlanUniverseAsync`, `PlanSeriesAsync`) — exactly the reuse item 3 asks for, not a parallel mechanism.
`ConflictRuleLookup` (an on-disk rule file, loaded and translated into the same
`IReadOnlyDictionary<string, FieldMergeDecision>` shape `ResolveWithDecisions` already accepts) **is**
the persistence layer this step originally set out to design — #181 built it before this issue did.
Nothing further needed here.

The on-disk-vs-DB storage question this step originally posed is also settled by the same #181
precedent: on-disk, re-parsed at staging time, no DB ingestion. `ImportActionPlanner.PlanAsync` already
runs this way per-quote in a loop (1676 lines) with no reported performance concern from #181's own
T1/T2 verification. Revisit only if a real performance problem is found once this issue's own
generation volume is known — not a reason to redesign preemptively.

### 4. `SourceAliasRule`'s own reuse story

**Status:** Resolved (design note, no new construction needed). `SourceAliasRule` is a straight
`(title, type)` → `(canonicalTitle, canonicalType)` substitution with no `FieldResolutionChoice`/
`FieldMergeDecision` concept at all — it is consulted *before* any Quote exists in the
existing/incoming-field-diff sense `FieldMergeResolver` operates on, and cannot be expressed as a
decision map. There is nothing to "reuse" from `FieldMergeResolver` for this mechanism, and nothing
new to build for matching either: `SourceAliasLookup.TryResolve` (shipped by #181) already is the
complete read path. This issue's own scope for `SourceAliasRule` is exactly two additions on top of
that existing lookup — generation (Step 13) and staleness detection (Step 8) — not a redesign of how
aliases are matched.

### 5. `ConflictResolutionRule` staleness — implement the check

**Status:** Not started.

Item 4 is a firm requirement: a rule must be flagged invalid/stale when the underlying source's shape
changes enough that silently reapplying it would produce a wrong result. No existing mechanism in this
codebase does anything equivalent today — `CompletenessGuard`/`ShouldBlock` (#165/#168) is the closest
structural precedent (a check that turns a would-be-auto-resolved action into a held one instead of
silently writing), but it guards a different condition (quote already `Complete`).

The comparison baseline already exists, unused: `ConflictResolutionRule.ExistingRecord`/
`IncomingRecord` (`src/Quotinator.Data/Import/ConflictResolutionRule.cs:31-42`) already record the full
field set on both sides *at the time the rule was authored*, explicitly documented today as "Purely
documentation; never read by the matching logic." No new schema field is needed — only new logic to
actually read what's already there. In `ConflictRuleLookup` (or a new sibling method alongside
`TryResolve`), when a candidate rule is found for an entity, compare the *current* staging run's
`existingFields`/`incomingFields` (already computed in `ImportActionPlanner` at every
`ConflictRuleLookup.TryResolve` call site) against the rule's recorded `ExistingRecord`/`IncomingRecord`
for the field(s) the rule governs. A mismatch on either side means the source's shape moved since the
rule was written — mark that specific field's resolution as stale rather than applying it silently. A
rule can be partially stale (one governed field's snapshot still matches, a different governed field's
doesn't) — treat staleness per-field, matching how `Fields` is already a list rather than a single
value.

### 6. New `Stale` status — resolved: a distinct status, not a fallback to `Pending`

**Status:** Not started. **Decided 2026-07-26**: a stale rule stages as a new, distinct status —
mirroring `Blocked`'s own precedent as a `CompletenessGuard`-driven third state alongside
`Pending`/`Decided` — so a reviewer can tell "this needed a decision because no rule matched" apart
from "this needed a decision because its rule went stale." `ImportActionStatus` gains a `Stale` value;
`GET /import/actions` (and its `status=` filter) exposes it the same way it already exposes `Pending`/
`Decided`/`Blocked`. This is new API surface — Step 17 (documentation) must cover it in
`README.md`/`addon/DOCS.md`'s status-value lists, and the `[Description]` attribute on the `status=`
query parameter needs the new value added.

### 7. Tests — `ConflictResolutionRule` staleness

**Status:** Not started. At minimum: a rule whose recorded `ExistingRecord` no longer matches the
current staging run's existing value stages `Stale`, not the rule's own resolution; a rule whose
recorded snapshot still matches applies exactly as #181's own non-stale tests already prove
(regression, not new behaviour); a rule governing two fields where only one field's snapshot is stale
treats the fields independently (per-field staleness, not whole-rule); `GET /import/actions?status=stale`
(lowercase, per this project's case-insensitive-by-default convention — see `TextClauses`/#211) returns
exactly the staged-`Stale` actions.

### 8. `SourceAliasRule` staleness

**Status:** Not started. A different failure mode from `ConflictResolutionRule`'s, since an alias has
no "existing vs incoming" shape to diff: the plausible failure is a canonical title itself later
changing (e.g. a further correction renames "The Fate of the Furious"), which would silently leave an
alias still pointing at the old canonical string. Detect this by comparing the alias's own recorded
`CanonicalTitle`/`CanonicalType` against the *actual current* Source row it resolves to (looked up via
the stable id `CanonicalTitle`/`CanonicalType` would themselves produce) at the point
`SourceAliasLookup.TryResolve` is consulted. If the live Source's own `Title`/`Type` has since diverged
from what the alias records as canonical, the alias is stale — surfaced via the same `Stale` status
Step 6 introduces, applied to the Quote(s) this alias would otherwise have silently resolved for.

### 9. Tests — `SourceAliasRule` staleness

**Status:** Not started. At minimum: an alias whose recorded `CanonicalTitle` no longer matches the
live Source's actual `Title` (simulating a later correction) is detected as stale and does not silently
resolve; an alias whose recorded canonical value still matches applies exactly as #181's own existing
alias tests already prove (regression, not new behaviour).

### 10. `ConflictResolutionRule` generation — the generalization heuristic is simpler than it first looked

**Status:** Done (finding, not implementation). Read all four real, bundled rule files
(`data/sources/{nikhilnamal17,vilaboim,quotinator-curated,quotinator-series-universe}-conflict-rules.json`)
to check the issue's "worst case one rule per action, best case a single shared rule" framing against
real data, rather than designing an algorithm speculatively. Every real rule is already **one
`ConflictResolutionRule` entry per entity id**, with a `Fields` list covering every field that entity
needed resolved (e.g. one real entry has a single `date` field; another has `date` *and* `character`
together in one entry). There is no wildcard/pattern concept in the schema at all — every rule is tied
to one specific `EntityId`. The collapsing the issue describes is **already fully expressed by
grouping decided `ImportActionFieldRow` export rows by `EntityId`**: worst case, an entity has one
decided field → one `ConflictResolutionFieldRule`; best case, an entity has several decided fields →
they all collapse into that same entity's single `ConflictResolutionRule` entry instead of one rule
apiece. No cross-entity pattern-matching mechanism needs to be designed or built.

### 11. `ConflictResolutionRule` generation — implement the export-row → rule-entry mapping

**Status:** Not started. Read `GET /import/actions/export`'s `ImportActionFieldRow` rows for a batch,
group by `EntityId`, and for each group emit one `ConflictResolutionRule` with `ExistingRecord`/
`IncomingRecord` populated from the group's own rows' `ExistingValue`/`IncomingValue` (reassembled into
the full-record JSON shape `ConflictResolutionRule` expects — see Step 5, these are recorded as
complete field sets, not just the resolved field) and one `ConflictResolutionFieldRule` per row
(`Field`/`Decision`/`CustomValue` map directly to `Field`/`Resolution`/`CustomValue`). Only rows with a
real `Decision` (not still-`Pending`, and not `Stale` per Step 6) are eligible.

### 12. `ConflictResolutionRule` generation — merge-not-overwrite into an existing rule file

**Status:** Not started. Load the target rule file (if it exists), and for each newly-generated
`ConflictResolutionRule`: if an entry with the same `EntityId` already exists, merge the new `Fields`
into it (a newly-decided field not previously covered gets added; a field the file already covers is
left as the human originally wrote it — a generation run must never silently overwrite a manual edit)
rather than replacing the whole entry. If no entry exists for that `EntityId`, append it. Write the
merged file back preserving whatever JSON formatting/ordering convention the hand-authored files
already use.

### 13. `SourceAliasRule` generation — candidate detection, not auto-generation

**Status:** Not started. Aliases cannot be generated the same mechanical way `ConflictResolutionRule`
entries can: every alias #181 shipped came from manual title-verification research (a web search per
title, per `docs/workflow/source-verification.md`) confirming what a film's real canonical title is —
there is no decided-action shape (Keep/Replace/Custom) to read a canonical title back from, because
nobody "decides" a canonical title through the normal conflict-review flow. Building an endpoint that
auto-writes alias entries without human verification would violate this project's own source-
verification policy (a title/date claim must be checked against real sources before being recorded,
per `docs/workflow/source-verification.md`'s procedure) — so generation for this mechanism means
**detect and suggest, never auto-write**: scan existing `Sources` rows for near-duplicate `(Title,
Type)` pairs not already covered by an alias (e.g. same normalized-punctuation/case title, or a close
string-distance match) and surface them as candidates for a human to research and confirm by hand,
using the existing hand-edit path — not a new auto-write mechanism. This still delivers the automation
value item 5 asks for (finding likely duplicates without a human having to notice them first) without
skipping the verification step this project requires for a factual title claim.

### 14. Rule-file endpoints

**Status:** Not started. Two distinct endpoints, since the two mechanisms have fundamentally different
generation semantics (Step 11's mechanical read-back vs. Step 13's suggest-only scan):
- `ConflictResolutionRule`: GET serves the current rule file for a source; POST triggers Step 11+12
  (generate from a given `batchId`, merge, persist) — a genuine write endpoint.
- `SourceAliasRule`: GET returns Step 13's candidate-duplicate suggestions for a source — read-only,
  never writes an alias entry itself.

Route placement TBD (likely under `/api/v1/import` alongside `/actions/export`, matching the existing
route-group convention) — confirmed at implementation time. Both need the standard admin-auth
treatment every write endpoint in this project already has (`X-Api-Key`); the alias-candidate GET may
not need admin auth at all since it's read-only and produces no side effect, matching this project's
existing pattern of admin-gating writes rather than reads — confirm against `/import/actions/export`'s
own auth requirement as precedent before deciding either way.

### 15. Tests — rule generation (both mechanisms)

**Status:** Not started. `ConflictResolutionRule`: a batch with one decided field for one entity
produces a single one-field rule; a batch with multiple decided fields for the same entity collapses
into one rule with multiple `Fields` entries (proving Step 10's grouping-by-`EntityId` claim, not just
asserting it); merging into an existing file preserves a field the file already manually covers
unchanged; merging adds a new field/entity the file didn't previously cover; an undecided (`Pending`)
or `Stale` action is excluded from generation. `SourceAliasRule`: a near-duplicate Source pair is
surfaced as a candidate; an existing, already-aliased pair is not re-suggested; the candidate endpoint
never writes to the alias file itself.

### 16. Rule lookup and auto-apply during staging

**Status:** Lookup and auto-apply themselves are Done via #181, for both mechanisms. Only
staleness-awareness (Steps 5–9) remains.

Wires into `ImportActionPlanner.PlanAsync`'s Quote Modify branch — `ConflictRuleLookup.TryResolve` is
consulted from four methods (five literal call sites — `PlanSourcesAsync` calls it twice, once per
resolution branch: explicit-id match and natural-key match): the Quote loop itself, and
`PlanSeriesAsync`/`PlanUniverseAsync`/`PlanSourcesAsync`. Today, a `Review`-policy duplicate is staged
`Pending` unconditionally when a field differs and no rule resolves it. This issue adds
staleness-awareness on top of #181's own rule lookup — if a matching but *stale* rule exists, the
action stages `Stale` (per Step 6) rather than #181's simpler "matching rule always applies" behaviour.
`PlanAsync_ReviewPolicy_MatchingRuleCoversTheOnlyChangedField_StagesDecidedNotPending` and
`PlanAsync_ReviewPolicy_NonMatchingRuleLookup_StagesPendingAsToday` (the issue's own expected-tests
table names these differently — see the correction note above) are shipped by #181 already — this
issue inherits them as regression guards for the non-stale case, adding new tests only for the
stale-rule case (Steps 7, 9).

`SourceAliasLookup.TryResolve` is consulted once, at the very top of `PlanAsync`'s Quote loop, before
`ResolveSourceAsync` ever runs — structurally a different integration point from anything
`ConflictRuleLookup` touches. Per Step 8, this is where the alias-staleness check runs, before the
Source it would otherwise silently resolve to is ever used.

`ConflictResolutionRule`'s own rule-file scope (Step 1) covers all 4 bundled files under #217; live
#217 verification confirmed a genuine cross-file Source enrichment conflict (a Source created by one
file, later assigned a Series by another) with no way to auto-resolve, which is why
`PlanSourcesAsync` is wired alongside `PlanSeriesAsync`/`PlanUniverseAsync` — all four sites are
already wired, not just the Quote branch.

### 17. Documentation

**Status:** Not started.

Per item 6 and CLAUDE.md's "Keeping API documentation in sync" section: update `README.md`'s and
`addon/DOCS.md`'s endpoint tables and the `[Description]` attributes for both new endpoints (Step 14),
and document the new `Stale` status value (Step 6) everywhere `ImportActionStatus`'s existing values
are already documented (endpoint descriptions, the `status=` query parameter's own description).
`schemas/manifest.schema.json` needs no change (Step 2 — no new manifest property).
`scripts/SOURCES.md`'s source-adding workflow doc should mention the new alias-candidate-suggestion
endpoint (Step 13) as part of adding a new source, since that's exactly when a duplicate-title risk is
highest.

### 18. Tests — overall

**Status:** Not started.

The five tests listed in the issue's "Expected tests" table (reproduced above) are the floor, not the
full set — Steps 1–16 above each imply additional coverage (manifest-schema validation, `Stale` status
round-trip for both mechanisms, endpoint auth/status-code tests for both new rule-file routes) beyond
what's individually listed under each step's own "Tests" step. Per this project's red-green policy,
every test must be confirmed red before its corresponding implementation lands.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | "Same conflict" identity scheme decided and documented | Live (review) | Resolved in Step 1: Id + field name, matching #181's precedent and #163's `ImportActionFieldRow` shape |
| 2 | ✅ | Manifest gains a rule-file reference; schema updated; file lives alongside the file/manifest it governs | Live (review) | Shipped by #181 for both mechanisms: `ruleFile` and `sourceAliasFile`, both file-entry-only on `ManifestFileEntryDto`, both in `schemas/manifest.schema.json` |
| 3 | ✅ | `ConflictResolutionRule` application reuses `FieldMergeResolver.ResolveWithDecisions` rather than a parallel mechanism | Live (review) | Already true via #181: `ImportActionPlanner.cs` calls `ResolveWithDecisions` directly from every `ConflictRuleLookup` call site |
| 4 | ✅ | `SourceAliasRule`'s own mechanism identified — no new construction needed for matching | Live (review) | Resolved in Step 4: `SourceAliasLookup.TryResolve` (shipped by #181) is the complete read path; this issue only adds generation and staleness on top |
| 5 | ❌ | A `ConflictResolutionRule` is flagged `Stale` (not silently applied, not silently discarded) when the underlying source's shape has changed enough to invalidate it | Unit test | Step 7's tests |
| 6 | ❌ | A `SourceAliasRule` is flagged `Stale` when its recorded canonical value no longer matches the live Source | Unit test | Step 9's tests |
| 7 | ❌ | Rule generation from a batch's decided actions produces candidate `ConflictResolutionRule` entries, worst case one per action, best case one shared entity rule | Unit test | Step 15's tests |
| 8 | ❌ | Generation merges into an existing rule file without overwriting manual edits | Unit test | Step 15's tests |
| 9 | ❌ | `SourceAliasRule` candidate-duplicate detection surfaces likely duplicates without auto-writing an alias entry | Unit test | Step 15's tests |
| 10 | ✅ | A matching, non-stale `ConflictResolutionRule` auto-resolves a staged action instead of leaving it `Pending`, even under `Review` policy | Unit test | Already shipped by #181 under a different name: `ImportActionPlannerTests.PlanAsync_ReviewPolicy_MatchingRuleCoversTheOnlyChangedField_StagesDecidedNotPending` |
| 11 | ✅ | No matching rule stages `Pending` exactly as today (regression guard) | Unit test | Already shipped by #181 under a different name: `ImportActionPlannerTests.PlanAsync_ReviewPolicy_NonMatchingRuleLookup_StagesPendingAsToday` |
| 12 | ❌ | `README.md`/`addon/DOCS.md` updated for both new endpoints and the new `Stale` status | Live | Manual diff review against the endpoints and status value actually added |
| 13 | ❌ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 warnings/errors; `dotnet test --configuration Release` → all passing |
| 14 | ❌ | T1 — app starts in Visual Studio without error against a manifest referencing both rule file types; a recurring conflict from a re-imported third-party source auto-resolves without requiring manual decide; a stale rule stages `Stale` instead of silently applying | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 15 | ❌ | T2 — Docker smoke test: stage a batch with a known recurring conflict, generate a rule file from its decided actions, re-stage the same conflict on a subsequent import, confirm it auto-resolves; alter a canonical value to trigger staleness and confirm `Stale` staging | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + curl workflow, to be defined once both rule-file endpoints' actual routes exist |

---

## Notes

T1 and T2 are both required per this project's blanket rule (CLAUDE.md — no exemption for a change
like this one).

**History of this plan doc's revisions**, kept brief since the doc itself now reflects current state
throughout rather than accumulating narrative:

- Originally written before #163 existed; blocked on its shape.
- Re-verified after #163 shipped — Step 1 and the generation steps redesigned against the real
  `ImportActionFieldRow` shape.
- Re-verified after #181 shipped — discovered #181 built *two* rule mechanisms, not the one this doc
  was designed around; every step re-read against both.
- Reviewed again 2026-07-26 — verified every "Not started"/"Done" claim directly against the codebase
  rather than trusting the labels as written. Two steps turned out to already be done via #181 and were
  corrected. The two tests the issue's own table names don't exist under those literal names — #181
  shipped equivalent, better-named coverage; corrected with the real names. "Shipped" language
  throughout corrected to "implemented and T1/T2-verified on this feature branch, not yet merged to
  `main`."
- **Both remaining open questions resolved by explicit developer decision (2026-07-26)**: scope extends
  to `SourceAliasRule` generation/staleness (not limited to `ConflictResolutionRule`); a stale rule
  surfaces as a new, distinct `Stale` status (not a fallback to ordinary `Pending`). Steps 4, 6, 8, 9,
  13, and 15 above are new as a direct result of the scope decision. No open questions remain in this
  plan doc.

**Considered and rejected (2026-07-14), while scoping #179's Series/Universe schema:** generalizing
this issue's declarative rule-file mechanism to also cover curator-only enrichment injection (a rule
kind that always sets a field value regardless of what the incoming import provides, for fields no
upstream source has at all — e.g. a `Series`/`Universe` tag on a Source). Rejected as unnecessary
complexity: a hand-authored curated overlay file (same pattern as `quotinator-curated.json`), imported
alongside the bundled sources on every startup, already solves this via #162's existing Source
Modify/decidability path with zero new mechanism, since the overlay file is persistent rather than
regenerated from upstream. `FieldResolutionChoice.Custom` (`FieldMergeResolver.cs:108`) already lets a
decision-maker set an arbitrary value during conflict resolution — the gap this idea would have closed
(a field with no incoming value to compare against at all, so no conflict is ever staged) isn't a real
gap once the overlay file itself provides an explicit value, since that then produces a genuine
incoming-vs-existing Modify to decide. This issue's scope remains recurring conflict resolution only,
unchanged from its original text. See #179's plan doc Background and #169's closing comment for the
full reasoning.
