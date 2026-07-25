# #153 — Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2)

**Status:** Planning
**GitHub issue:** #153
**Tiers required:** T1, T2
**Depends on:** #149, #154 (both shipped); #163 (shipped — its flat export row shape is real code now,
not prose); #181 (not yet implemented — this issue's rule generation/staleness/endpoint work builds on
#181's shipped rule-file format, so #181 must land first in the dependency order #217 (parent tracking
issue) establishes: #177 → #181 → #153)

---

## Scope note — re-verified 2026-07-25, no longer preliminary on #163

This plan doc was originally written before #163 existed at all — its own body once stated the whole
design was to be "finalised during planning once #163's actual decision-request shape is known." **#163
has since shipped in full**: `GET /import/actions/export`/`POST /import/actions/bulk-decide`, and the
real, code-level flat row shape is `ImportActionFieldRow` (`src/Quotinator.Core/Models/
ImportActionFieldRow.cs`) — `ActionId`, `EntityId`, `EntityType`, `Field`, `ExistingValue`,
`IncomingValue`, `Decision`, `CustomValue`, `MarkCompletenessAs`. Step 1's identity question and Step
5's generation step, both previously blocked on this shape existing, are resolved/designable below
against the real thing, not prose speculation.

**Superseded in part by #181 (filed 2026-07-15, not yet implemented as of this rewrite):** #181 ships
a minimal, hand-authored version of exactly the "parts that can be designed independently of #163"
named in the original scope note — a per-source rule-file format, its manifest reference, and
lookup/auto-apply wiring into `ImportActionPlanner.PlanAsync` (this plan doc's own Step 2 and Step 6,
below) — scoped, per #217 (the parent tracking issue both #181 and this issue now sit under), to all 4
currently-bundled files rather than the 2 #181 originally named. **When this issue is eventually
implemented, it builds Step 5's generation, Step 4's staleness detection, and Step 6's endpoint on top
of #181's shipped rule-file format — it does not re-design or replace that format.** Steps 2 and 6
below should be read as "confirm #181's shipped shape still fits" once #181 has actually landed, not
"design from scratch." `PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` and
`PlanAsync_NoMatchingRule_StagesPendingAsToday` (this issue's own Expected tests table) will be shipped
by #181 — this issue inherits them as regression guards once #181 lands, not fresh tests to write here.

---

## Spec requirements (from the GitHub issue)

From `gh issue view 153`'s "What needs to be done" list (numbering preserved from the issue):

1. Decide and document what identifies "the same conflict" across separate import runs — quote Id +
   field name, or a content hash of the conflicting values — so a stored resolution rule reliably
   reapplies to the right conflict next time and not to an unrelated one that happens to share a
   quote Id. **Explicitly still open** per the issue text; not decided by this plan doc either (see
   Step 1).
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

---

## Steps

### 1. "Same conflict" identity: resolved — Id + field name

**Status:** Resolved (this rewrite). Was "Not started"/genuinely open; #163 landing and #181's
precedent close it.

The issue text offered two candidates — quote Id + field name, or a content hash of the conflicting
values — without picking one, deferring to "once #163's actual decision-request shape is known."
That shape is now real code (`ImportActionFieldRow`: `ActionId`, `EntityId`, `EntityType`, `Field`,
...), and it settles the question: **Id + field name**, matching the row shape exactly rather than
inventing a parallel identity scheme.

The one open concern against this — "a recurring third-party source does not guarantee stable quote
Ids across refreshes unless the upstream itself is Id-stable" — turns out not to be a real risk.
Confirmed against `EntityIdentity`/`QuoteIdentity.StableId` (`src/Quotinator.Core/Import/
EntityIdentity.cs`): every entity id in this codebase, quotes included, is a **deterministic hash of
the entity's own normalised natural-key content** (quote text, source, etc. — see `StableId`'s
`string.Join('|', parts.Select(QuoteIdentity.Normalise))` construction), never anything the upstream
source itself supplies. So as long as a re-scraped upstream file's quote text/source pair is
unchanged, Quotinator computes the identical id on every refresh regardless of whether the upstream
source's own row ordering, internal ids, or file structure changed at all. A content hash of the
*conflicting values* would only be needed if Quotinator's ids were themselves upstream-derived and
unstable — they aren't.

**#181 already ships this precedent** for its own minimal, hand-authored rule files (its Step 1: "keyed
by quote id + field name"). This issue's generated rule format inherits the same scheme rather than
introducing a second one — one identity scheme across both the hand-authored and generated cases,
per this project's DRY convention.

### 2. Rule file storage location and manifest reference

**Status:** Superseded by #181 — confirm its shipped shape still fits rather than designing fresh.

Per item 2, the rule file lives alongside the file it governs, not a new separate location. This is
independently designable now (does not depend on #163). **#181 already ships this**: one rule file
per source (`vilaboim`'s and `NikhilNamal17`'s own), a manifest reference alongside each, hand-
authored rather than generated. What's below is the original preliminary design reasoning, kept for
context on the open placement question (`SourceImportSettingsDto` vs. file-entry-only) that #181's
implementation actually resolved — check its shipped code for the answer before re-deciding:

- Extend `ManifestFileEntryDto` (`src/Quotinator.Data/Import/ManifestFileEntryDto.cs`) with a new
  optional property, analogous in spirit to its existing `duplicateResolution` override — but this is
  a **reference to a file path**, not a policy selection, so it is a materially different kind of
  property from anything `ManifestPolicy`/`SourceImportSettingsDto` carries today. Confirm during
  implementation whether it belongs on `SourceImportSettingsDto` (shared with the top-level manifest,
  matching how `duplicateResolution` cascades manifest → file) or is file-entry-only, since a
  manifest-level rule file covering every listed file in that directory is also a plausible reading
  of "matching whichever folder ... is already in."
- Add the corresponding property to `schemas/manifest.schema.json` (both the top-level manifest
  object and the per-file `files[]` item, mirroring `duplicateResolution`'s dual placement — see the
  schema excerpt read during investigation, `schemas/manifest.schema.json:14-17` and `:66-68`).
- Confirm this is additive to the existing schema (`additionalProperties: false` on both the manifest
  root and each file entry means the new property name must be added explicitly in both places or
  manifest validation will reject it).

### 3. Rule file schema and `FieldMergeResolver` reuse

**Status:** Not started.

`FieldMergeResolver.ResolveWithDecisions` (`src/Quotinator.Data/Import/FieldMergeResolver.cs:84-146`)
already takes an `IReadOnlyDictionary<string, FieldMergeDecision>` — a decision always wins for that
field, unresolved ambiguous fields collected and thrown via `UnresolvedFieldConflictException`. This
is the exact mechanism item 3 says to reuse. The gap this issue adds on top: `ResolveWithDecisions`'s
decision map is built fresh, in memory, per call by a caller (today: `SqliteImportActionService.
DecideAsync`, from a single `ConflictDecisionRequest`) — there is no persistence layer above it. This
issue's rule file is that persistence layer: a durable, on-disk (or DB-stored — TBD, see below) set
of decisions keyed by whatever Step 1 decides, loaded and translated into the same
`IReadOnlyDictionary<string, FieldMergeDecision>` shape `ResolveWithDecisions` already accepts, so no
new merge algorithm is written — only a new *source* of decisions feeding the existing one.

Open question not resolved by this plan doc: is the rule file itself stored on disk (alongside the
source file, as item 2's "lives alongside" phrasing suggests literally) and parsed at staging time,
or ingested once into a DB table (mirroring how `data/sources/*.json` itself is seeded into `Quotes`
rather than re-read from disk on every request)? The issue's wording ("the manifest gains a reference
to its rule file") reads as an on-disk file, but the lookup-performance concern from Step 1 (matching
a rule to a field during `ImportActionPlanner.PlanAsync`, which runs per-quote in a loop — see
`src/Quotinator.Core/Database/ImportActionPlanner.cs`, now 1500+ lines after #171–#180's entity work
landed; re-confirm the current shape rather than trusting a stale line number) may make a DB-backed index the more
practical implementation even if the source-of-truth artifact is a file a user hand-edits. Flagging
rather than deciding — this is exactly the kind of design-decision-is-the-developer's-call point
CLAUDE.md's authoritative-sources rule says should not be silently picked.

### 4. Staleness detection

**Status:** Not started.

Item 4 is a firm requirement: a rule must be flagged invalid/stale when the underlying source's
shape changes enough that silently reapplying it would produce a wrong result. No existing mechanism
in this codebase does anything equivalent today — `CompletenessGuard`/`ShouldBlock` (#165/#168) is
the closest structural precedent (a check that turns a would-be-auto-resolved action into a held one
instead of silently writing), but it guards a different condition (quote already `Complete`) and
lives in `Quotinator.Core.Database` (`CompletenessGuard.ShouldBlock`, referenced from multiple sites
in `ImportActionPlanner.cs` today, one per entity type). A staleness check for this issue would need its own condition — most
likely comparing the rule's recorded `ExistingValue`/`IncomingValue` (or whatever Step 1's identity
scheme captures) against the field values actually seen during a later staging run, and treating a
mismatch as "the source's shape moved out from under this rule" rather than blindly applying it.
Concretely: if a rule says "for quote X, field `character`, always take incoming," but a later import
shows quote X's *existing* value no longer matches what the rule was originally generated against,
the rule should not silently fire — this needs to surface as a distinct condition (new
`ImportActionStatus`-adjacent state, or a new field on the rule row itself, e.g. `IsStale`) that a
future `GET /import/actions`-style endpoint can report, per the "flags a rule" wording (flagging, not
silently discarding or silently applying).

### 5. Rule generation from decided batch actions, with merge-not-overwrite semantics

**Status:** Not started. No longer blocked on #163 (shipped) — blocked on #181 landing first
(dependency order), since this step's rule format inherits #181's shipped shape (Step 1 above).

Item 5's generation step consumes "a batch's already-decided actions" — this is precisely #163's real,
shipped export shape: `GET /import/actions/export` already returns one `ImportActionFieldRow` per
(action, field), including `Decision`/`CustomValue` reflecting the actual original choice (not an
inference — #163's `OriginalDecision` column). This step reads those decided rows for a batch and
emits candidate rule-file rows in #181's Id+field format (Step 1 above), collapsing to "worst case one
rule per action, best case a single rule" — the collapsing heuristic (what makes two decided fields
generalizable into one shared rule vs. two separate rules) is unspecified in both issues and needs its
own design pass once this step is actually implemented, against real decided-action data from #217's
own per-bundled-file conflict-resolution work, per the issue's own "a single rule covers an entire
recurring batch" framing (a hoped-for outcome, not a specified algorithm).

The rule-file endpoint (GET-and-serve today's rule file; the same route also accepting a
generate-and-merge POST, per item 5) is new API surface — likely lives under `/api/v1/import`
alongside `/actions/export` (#163) rather than a new top-level tag, matching the existing route-group
convention; final placement confirmed at implementation time.

### 6. Rule lookup and auto-apply during staging

**Status:** Not started — blocked on #181 landing first (not yet implemented as of this rewrite,
despite the prior version of this plan doc assuming it had already shipped). Once #181 lands, this
step's remaining scope is adding staleness-awareness on top of what #181 built, not writing the
lookup from scratch.

Wires into `ImportActionPlanner.PlanAsync`'s Quote Modify branch (`src/Quotinator.Core/Database/
ImportActionPlanner.cs` — line numbers not cited here; the file has grown substantially since this
plan doc was first written, from #171–#180's Series/Universe/Character/StageDirection/SoundCue/
Conversation work landing, and #181 will add its own rule-lookup wiring before this step touches it —
confirm the current shape at implementation time rather than trusting any line number written here
today): today, a `Review`-policy duplicate is staged `Pending` unconditionally when a field differs.
This step adds staleness-awareness on top of #181's own rule lookup — if a matching but *stale* rule
exists, the action stages `Pending` (not silently applied) rather than #181's simpler "matching rule
always applies" behaviour. `PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` and
`PlanAsync_NoMatchingRule_StagesPendingAsToday` (both listed in the issue's expected-tests table) are
shipped by #181 already, once #181 lands — this issue inherits them as regression guards for the
non-stale case, adding new tests only for the stale-rule case Step 4 introduces.

**Note — Quote-only vs. multi-entity scope.** #181's own rule-file scope (Step 1 above) covers all 4
bundled files under #217, not just Quote-level conflicts — if `quotinator-series-universe.json`
produces genuine Series/Universe conflicts once #217's per-file Docker scenarios actually run it, #181
(and therefore this step) needs rule-lookup wiring in `PlanSeriesAsync`/`PlanUniverseAsync` too, not
only the Quote-level branch this step's original text assumed was the only site. Confirm against
#181's actual shipped scope before assuming "the Quote branch" is the only integration point.

### 7. Documentation

**Status:** Not started.

Per item 6 and CLAUDE.md's "Keeping API documentation in sync" section: if this issue adds a new
endpoint (the rule-file GET/generate-merge route from Step 5), update `README.md`'s and
`addon/DOCS.md`'s endpoint tables and the `[Description]` attributes on the new endpoint in the same
commit. If a new manifest property is added (Step 2), `schemas/manifest.schema.json` is already
covered by Step 2 itself, but `scripts/SOURCES.md`'s source-adding workflow doc should be checked for
whether it needs a mention of the new rule-file reference.

### 8. Tests

**Status:** Not started.

The five tests listed in the issue's "Expected tests" table (reproduced above) are the floor, not the
full set — Steps 1–6 above each imply additional coverage (manifest-schema validation for the new
property, staleness-flag round-trip, endpoint auth/status-code tests for the new rule-file route) that
cannot be enumerated precisely until Steps 1 and 5's open questions are resolved against #163's actual
shape. Per this project's red-green policy, every test must be confirmed red before its corresponding
implementation lands.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | "Same conflict" identity scheme decided and documented | Live (review) | Resolved in this rewrite's Step 1: Id + field name, matching #181's precedent and #163's `ImportActionFieldRow` shape |
| 2 | ❌ | Manifest gains a rule-file reference; schema updated; file lives alongside the file/manifest it governs | Unit test | TBD — likely a `ManifestSeedPlanner`/manifest-schema validation test mirroring existing `duplicateResolution` coverage; confirm #181's shipped shape first |
| 3 | ❌ | Rule application reuses `FieldMergeResolver.ResolveWithDecisions` rather than a parallel mechanism | Unit test | Code review + a test asserting the rule-lookup path calls into the existing method, not a new duplicate one |
| 4 | ❌ | A rule is flagged (not silently applied, not silently discarded) when the underlying source's shape has changed enough to invalidate it | Unit test | `Quotinator.Core.Tests.RuleGeneration_StaleSourceShape_FlagsRuleRatherThanApplying` |
| 5 | ❌ | Rule generation from a batch's decided actions produces candidate rules, worst case one per action | Unit test | `Quotinator.Core.Tests.GenerateRuleFile_FromDecidedBatchActions_ProducesCandidateRules` |
| 6 | ❌ | Generation merges into an existing rule file without overwriting manual edits | Unit test | `Quotinator.Core.Tests.GenerateRuleFile_MergesIntoExistingRuleFile_DoesNotOverwriteManualEdits` |
| 7 | ❌ | A matching, non-stale rule auto-resolves a staged action instead of leaving it `Pending`, even under `Review` policy | Unit test | `Quotinator.Core.Tests.PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` (shipped by #181; inherited as a regression guard once #181 lands) |
| 8 | ❌ | No matching rule stages `Pending` exactly as today (regression guard) | Unit test | `Quotinator.Core.Tests.PlanAsync_NoMatchingRule_StagesPendingAsToday` (shipped by #181; inherited as a regression guard once #181 lands) |
| 9 | ❌ | `README.md`/`addon/DOCS.md` updated if a new endpoint or file format is introduced | Live | Manual diff review against the endpoint(s) actually added |
| 10 | ❌ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 warnings/errors; `dotnet test --configuration Release` → all passing |
| 11 | ❌ | T1 — app starts in Visual Studio without error against a manifest referencing a rule file; a recurring conflict from a re-imported third-party source auto-resolves without requiring manual decide | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 12 | ❌ | T2 — Docker smoke test: stage a batch with a known recurring conflict, generate a rule file from its decided actions, re-stage the same conflict on a subsequent import, confirm it auto-resolves | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + curl workflow, to be defined once the rule-file endpoint's actual route exists |

---

## Notes

T1 and T2 are both required per this project's blanket rule (CLAUDE.md, reinforced 2026-07-12 per the
#168 plan doc's Notes section — no exemption for a change like this one).

**Re-verified 2026-07-25, as part of setting up #217 (parent tracking issue for #177/#181/#153).** #163
has since shipped in full — this plan doc's original "genuinely blocked on #163" framing throughout
Steps 1 and 5 is resolved; both are now designed against #163's real, shipped shape rather than
prose. What remains blocking is #181, not #163: #181 (Not yet implemented, per its own plan doc) ships
the rule-file format and lookup wiring this issue builds on — #217 establishes the dependency order
#177 → #181 → #153 explicitly, so this issue cannot start implementation before #181 lands. All
`Quotinator.Engine`/`Quotinator.Engine.Tests` references throughout this plan doc (stale since #206's
project merge) have been corrected to `Quotinator.Core`/`Quotinator.Core.Tests` in this same pass.

Other open questions surfaced during investigation, not resolved here (flagged per this project's
"gap resolution is the developer's decision" rule — do not decide these unprompted):

- Whether the rule file's source of truth is a hand-edited on-disk artifact re-parsed at staging
  time, or ingested into a DB table with the file only ever a human-facing export/import format (see
  Step 3). This materially changes the implementation shape (file-watcher/parse-on-demand vs. a new
  migration and table) and should be decided explicitly before implementation starts — #181's own
  shipped choice (once it lands) may settle this by precedent.
- Whether the manifest's rule-file reference is a per-file-entry property, a manifest-level property
  covering every listed file, or both (see Step 2) — the issue's "matching whichever folder ... is
  already in" phrasing is ambiguous between these readings; #181's shipped shape (once it lands)
  should settle this too.
- The rule-generalization heuristic in Step 5 ("worst case one rule per action, best case a single
  rule covers an entire recurring batch") has no algorithm specified in either issue — needs its own
  design pass against real decided-action data, most plausibly generated during #217's own per-file
  conflict-resolution work.
- Whether "flags a rule as invalid/stale" (item 4) means the rule is held for human review via a new
  status surfaced on an existing or new endpoint, or something else — the issue does not specify the
  user-facing mechanics of a stale flag, only that reapplying a stale rule silently must not happen.

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
