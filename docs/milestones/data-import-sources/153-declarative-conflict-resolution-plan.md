# #153 — Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2)

**Status:** Planning
**GitHub issue:** #153
**Tiers required:** T1, T2
**Depends on:** #149, #154, #163 (per `overview.md`'s dependency map)

---

## Scope note — this plan is necessarily preliminary

This issue's own body states one design decision is still genuinely open (identity of "the same
conflict" across runs) and that the whole design is to be "finalised during planning once #163's
actual decision-request shape is known." At the time of writing, **#163 has no plan doc yet**
(`docs/milestones/data-import-sources/163-bulk-decide-file-plan.md` does not exist in this
worktree) and its own issue is still `Planning`/`Not yet assessed`. This plan doc therefore records
the structure and constraints that are already fixed by #153's issue text, #149's and #154's shipped
machinery, and the current manifest schema — but the concrete rule-file schema, the exact reuse
points in `FieldMergeResolver`, and the generation algorithm cannot be finalised until #163 lands
and its flat `(ActionId, EntityId, EntityType, Field, ExistingValue, IncomingValue, Decision,
CustomValue)` row shape is real code, not just issue-body prose. Steps below are sequenced so the
genuinely blocked work (rule generation, which consumes #163's decided-action shape directly) comes
last, and the parts that can be designed independently of #163 (rule storage location, staleness
detection, lookup/apply wiring) come first.

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
| New: `Quotinator.Engine.Tests` | `PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` |
| New: `Quotinator.Engine.Tests` | `PlanAsync_NoMatchingRule_StagesPendingAsToday` |
| New: `Quotinator.Engine.Tests` | `RuleGeneration_StaleSourceShape_FlagsRuleRatherThanApplying` |
| New: `Quotinator.Engine.Tests` | `GenerateRuleFile_FromDecidedBatchActions_ProducesCandidateRules` |
| New: `Quotinator.Engine.Tests` | `GenerateRuleFile_MergesIntoExistingRuleFile_DoesNotOverwriteManualEdits` |

---

## Steps

### 1. Resolve the open "same conflict" identity question

**Status:** Not started.

The issue text offers two candidates — quote Id + field name, or a content hash of the conflicting
values — without picking one, and explicitly defers the choice to "once #163's actual
decision-request shape is known." Investigation for this plan doc did not surface a reason to prefer
one over the other independently of that shape:

- **Quote Id + field name** is simple and matches how `#149`/`#154` already key everything
  (`ConflictDecisionRequest`, `SystemImportAction.EntityId`), but a recurring third-party source
  (e.g. `vilaboim_movie-quotes.json` regenerated upstream) does not guarantee stable quote Ids across
  refreshes unless the upstream itself is Id-stable — worth confirming against `EntityIdentity`/
  `QuoteIdentity.StableId`'s actual determinism guarantees before committing to this as the sole key.
- **A content hash of the conflicting values** survives an Id change but requires deciding exactly
  what gets hashed (existing value only? existing+incoming pair? normalised how?) and how a rule
  keyed this way is looked up efficiently during planning without a full-table scan per field.

This step is genuinely blocked on #163 landing — #163's flat per-(action, field) row is the closest
existing precedent for "identify a specific field-level decision," and the rule file's own row shape
should very likely mirror it (same `EntityId`/`EntityType`/`Field` columns) rather than invent an
unrelated identity scheme. Decide this **after** reading #163's shipped code, not before.

### 2. Rule file storage location and manifest reference

**Status:** Not started.

Per item 2, the rule file lives alongside the file it governs, not a new separate location. This is
independently designable now (does not depend on #163):

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
`src/Quotinator.Engine/Database/ImportActionPlanner.cs:60-140`) may make a DB-backed index the more
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
lives in `Quotinator.Engine.Database` (`CompletenessGuard.ShouldBlock`, referenced from
`ImportActionPlanner.cs:121`). A staleness check for this issue would need its own condition — most
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

**Status:** Not started. Genuinely blocked on #163.

Item 5's generation step consumes "a batch's already-decided actions" — this is precisely #163's
export shape (`ActionId, EntityId, EntityType, Field, ExistingValue, IncomingValue, Decision,
CustomValue`, one row per (action, field), per #163's issue body). Once #163 ships, this step reads
decided rows for a batch and emits candidate rule-file rows, collapsing to "worst case one rule per
action, best case a single rule" — the collapsing heuristic (what makes two decided fields
generalizable into one shared rule vs. two separate rules) is unspecified in both issues and needs
its own design pass once real decided-action data is available to reason about, per the issue's own
"a single rule covers an entire recurring batch" framing (a hoped-for outcome, not a specified
algorithm).

The rule-file endpoint (GET-and-serve today's rule file; the same route also accepting a
generate-and-merge POST, per item 5) is new API surface not decided by #154's or #149's existing
route set — likely lives under `/api/v1/import` alongside `/actions/export` (#163) rather than a new
top-level tag, but this is a naming/routing decision to make once #163's actual export route exists
to place it next to.

### 6. Rule lookup and auto-apply during staging

**Status:** Not started.

Wires into `ImportActionPlanner.PlanAsync`'s existing Quote Modify branch
(`src/Quotinator.Engine/Database/ImportActionPlanner.cs:96-140`): today, a `Review`-policy duplicate
is staged `Pending` unconditionally (line 140's `isPending` check only looks at `policy`). This step
adds a rule lookup before that decision — if a matching, non-stale rule exists for the changed
field(s), the action stages `Decided` (with the rule's resolution already applied to
`MergedFields`/resolved payload, mirroring how `DecideAsync` already persists a resolved value) even
under `Review` policy, instead of `Pending`. `PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending`
and `PlanAsync_NoMatchingRule_StagesPendingAsToday` (both listed in the issue's expected-tests table)
map directly onto this branch.

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
| 1 | ❌ | "Same conflict" identity scheme decided and documented | Unit test | TBD once Step 1 is resolved against #163's shipped shape |
| 2 | ❌ | Manifest gains a rule-file reference; schema updated; file lives alongside the file/manifest it governs | Unit test | TBD — likely a `ManifestSeedPlanner`/manifest-schema validation test mirroring existing `duplicateResolution` coverage |
| 3 | ❌ | Rule application reuses `FieldMergeResolver.ResolveWithDecisions` rather than a parallel mechanism | Unit test | Code review + a test asserting the rule-lookup path calls into the existing method, not a new duplicate one |
| 4 | ❌ | A rule is flagged (not silently applied, not silently discarded) when the underlying source's shape has changed enough to invalidate it | Unit test | `Quotinator.Engine.Tests.RuleGeneration_StaleSourceShape_FlagsRuleRatherThanApplying` |
| 5 | ❌ | Rule generation from a batch's decided actions produces candidate rules, worst case one per action | Unit test | `Quotinator.Engine.Tests.GenerateRuleFile_FromDecidedBatchActions_ProducesCandidateRules` |
| 6 | ❌ | Generation merges into an existing rule file without overwriting manual edits | Unit test | `Quotinator.Engine.Tests.GenerateRuleFile_MergesIntoExistingRuleFile_DoesNotOverwriteManualEdits` |
| 7 | ❌ | A matching, non-stale rule auto-resolves a staged action instead of leaving it `Pending`, even under `Review` policy | Unit test | `Quotinator.Engine.Tests.PlanAsync_MatchingRuleExists_AutoResolvesWithoutPending` |
| 8 | ❌ | No matching rule stages `Pending` exactly as today (regression guard) | Unit test | `Quotinator.Engine.Tests.PlanAsync_NoMatchingRule_StagesPendingAsToday` |
| 9 | ❌ | `README.md`/`addon/DOCS.md` updated if a new endpoint or file format is introduced | Live | Manual diff review against the endpoint(s) actually added |
| 10 | ❌ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 warnings/errors; `dotnet test --configuration Release` → all passing |
| 11 | ❌ | T1 — app starts in Visual Studio without error against a manifest referencing a rule file; a recurring conflict from a re-imported third-party source auto-resolves without requiring manual decide | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 12 | ❌ | T2 — Docker smoke test: stage a batch with a known recurring conflict, generate a rule file from its decided actions, re-stage the same conflict on a subsequent import, confirm it auto-resolves | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + curl workflow, to be defined once the rule-file endpoint's actual route exists |

---

## Notes

T1 and T2 are both required per this project's blanket rule (CLAUDE.md, reinforced 2026-07-12 per the
#168 plan doc's Notes section — no exemption for a change like this one).

This issue depends on #163 (Phase 1) landing first — its own body says it "generalizes the per-action
decisions #163's file format produces into persistent per-source rules," so #163's actual file format
needs to exist before this issue's design can be fully concrete. As of this plan doc, #163 has not
been implemented and has no plan doc in this worktree either — Steps 1 and 5 above are explicitly
blocked on it, not merely sequenced after it for convenience.

Other open questions surfaced during investigation, not resolved here (flagged per this project's
"gap resolution is the developer's decision" rule — do not decide these unprompted):

- Whether the rule file's source of truth is a hand-edited on-disk artifact re-parsed at staging
  time, or ingested into a DB table with the file only ever a human-facing export/import format (see
  Step 3). This materially changes the implementation shape (file-watcher/parse-on-demand vs. a new
  migration and table) and should be decided explicitly before implementation starts.
- Whether the manifest's rule-file reference is a per-file-entry property, a manifest-level property
  covering every listed file, or both (see Step 2) — the issue's "matching whichever folder ... is
  already in" phrasing is ambiguous between these readings.
- The rule-generalization heuristic in Step 5 ("worst case one rule per action, best case a single
  rule covers an entire recurring batch") has no algorithm specified in either issue — needs its own
  design pass against real #163-shaped decided-action data.
- Whether "flags a rule as invalid/stale" (item 4) means the rule is held for human review via a new
  status surfaced on an existing or new endpoint, or something else — the issue does not specify the
  user-facing mechanics of a stale flag, only that reapplying a stale rule silently must not happen.
