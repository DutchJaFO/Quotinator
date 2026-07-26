# #153 — Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2)

**Status:** In progress
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

**Status:** Resolved and re-verified directly against the code on 2026-07-26 (not re-derived from this
doc's own earlier claim). Was open in the issue text ("quote Id + field name, or a content hash of the
conflicting values"); #163 landing and #181's precedent close it.

`ImportActionFieldRow` (`ActionId`, `EntityId`, `EntityType`, `Field`, ...) settles the question:
**Id + field name**, matching the row shape exactly rather than inventing a parallel identity scheme.
Confirmed exactly against `ConflictResolutionRule.cs` (`EntityId` + `Fields[].Field`) and
`ConflictRuleLookup.cs` (`Key(entityId, field) => $"{entityId}|{field}"`, `StringComparer.OrdinalIgnoreCase`).

The one open concern against this — "a recurring third-party source does not guarantee stable quote
Ids across refreshes unless the upstream itself is Id-stable" — is not a real risk, for the specific
case this issue targets. Confirmed against `EntityIdentity`/`QuoteIdentity.StableId`
(`src/Quotinator.Core/Import/EntityIdentity.cs`): every entity id in this codebase — Source, Character,
Person, Series, Universe, and Quote — is a **deterministic hash of the entity's own normalised
natural-key content** (quote text, source, etc. — see `StableId`'s
`string.Join('|', parts.Select(QuoteIdentity.Normalise))` construction). So as long as a re-scraped
upstream file's quote text/source pair is unchanged, Quotinator computes the identical id on every
refresh regardless of whether the upstream source's own row ordering, internal ids, or file structure
changed at all.

**One refinement found during re-verification, tightening rather than reversing the claim above**: an
entity's id is only ever this content hash when no explicit `id` is supplied — `MappedSourceQuoteBuilder
.Build`/`BasicJsonArrayConverter.cs` both use a raw entry's own `id` verbatim when present, falling
back to `QuoteIdentity.StableId` only when it's absent. This issue's own scope (recurring conflicts from
bundled **third-party** sources) is unaffected: both bundled upstream raw schemas
(`{ quote, movie }` / `{ quote, movie, type, year }`, per this file's own Data Sources table) carry no
`id` field at all, so the content-hash path is the only one that ever runs for the sources this issue
actually automates. A curator-authored file supplying an explicit id is a different, unrelated case —
outside this issue's "recurring third-party source" scope.

Also confirmed the identity is anchored to the **already-seeded, matched row's own id**, not a freshly
computed one — every `ConflictRuleLookup.TryResolve` call site in `ImportActionPlanner.cs` (lines 220,
482, 590, 1056, 1209) passes the existing/matched entity's id (`q.Id`, `matchedId`, or `keyRow.Id`), so
a rule keyed to a specific entity id reliably targets that same already-seeded row on every future
staging run.

`ConflictResolutionRule` already ships this precedent (keyed by entity id + field name). This issue's
generated rules inherit the same scheme rather than introducing a second one.

### 2. Rule file storage location and manifest reference

**Status:** Done via #181, for both mechanisms. Re-verified directly against the code on 2026-07-26,
end to end, not just at the DTO declaration:

- `ManifestFileEntryDto.RuleFile`/`ruleFile` and `.SourceAliasFile`/`sourceAliasFile`
  (`src/Quotinator.Data/Import/ManifestFileEntryDto.cs:47,55`) — both present, both optional, both
  file-entry-only (no manifest-level equivalent), matching item 2's "alongside the file it governs."
- `ManifestSeedPlanner.PlanSeed` (`src/Quotinator.Data/Import/ManifestSeedPlanner.cs:55-72`) resolves
  both to absolute paths and threads them into `SeedFile`; also excludes both from the
  "unlisted-JSON-gets-auto-appended" fallback, so a rule/alias file sitting in the same directory is
  never mistaken for an unlisted quote source.
- `QuotinatorDatabaseInitializer.LoadConflictRules`/`LoadSourceAliases`
  (`src/Quotinator.Core/Database/QuotinatorDatabaseInitializer.cs:265-266,463-514`) read `SeedFile`'s
  two paths at seed time, fail open to `ConflictRuleLookup.Empty`/`SourceAliasLookup.Empty` on any
  missing file or parse error (a rule file is an optimisation, never a hard dependency), and construct
  the two lookups `ImportActionPlanner` consumes.
- `schemas/manifest.schema.json` declares both properties; dedicated
  `schemas/conflict-resolution-rules.schema.json`/`schemas/source-alias-rules.schema.json` document
  each file's own shape — the former's own description already names this issue by number as the
  future source of a *generated* form of the same shape.

Per item 2, the rule file lives alongside the file it governs, not a new separate location. #181 ships
this file-entry-only (not manifest-level) for both mechanisms — one bundled file can reference either,
both, or neither. This issue's own generation work (Steps 10–14) writes into whichever of the two an
entity's generated rule belongs to; no new manifest property is needed for either mechanism.

### 3. `ConflictResolutionRule` × `FieldMergeResolver` reuse

**Status:** Done via #181. Re-verified directly against the code on 2026-07-26: `ImportActionPlanner.cs`
calls `FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, ruleDecisions)` directly
from every one of the five `ConflictRuleLookup` call sites — confirmed at lines 228, 529, 640, 1099,
1255, each immediately following the matching `ruleDecisions`-building block (Quote loop,
`PlanSourcesAsync` ×2, `PlanUniverseAsync`, `PlanSeriesAsync`) — exactly the reuse item 3 asks for, not
a parallel mechanism. `ConflictRuleLookup` (an on-disk rule file, loaded and translated into the same
`IReadOnlyDictionary<string, FieldMergeDecision>` shape `ResolveWithDecisions` already accepts) **is**
the persistence layer this step originally set out to design — #181 built it before this issue did.

**Grounding for Step 5's staleness design, found while re-reading `FieldMergeResolver.cs` directly**:
`ResolveWithDecisions` applies a supplied decision unconditionally for that field — "regardless of
whether it was actually ambiguous," per its own doc comment — it has no concept of a decision being
untrustworthy. This means Step 5's staleness gate cannot live inside `FieldMergeResolver` (that would
require teaching a domain-agnostic, `Quotinator.Data`-owned utility about staleness at all); it must
intercept earlier, at the `ConflictRuleLookup.TryResolve` call sites themselves, before a stale
decision is ever added to the `ruleDecisions` dictionary `ResolveWithDecisions` receives.

The on-disk-vs-DB storage question this step originally posed is also settled by the same #181
precedent: on-disk, re-parsed at staging time, no DB ingestion. `ImportActionPlanner.PlanAsync` already
runs this way per-quote in a loop (1676 lines) with no reported performance concern from #181's own
T1/T2 verification. Revisit only if a real performance problem is found once this issue's own
generation volume is known — not a reason to redesign preemptively.

### 4. `SourceAliasRule`'s own reuse story

**Status:** Resolved (design note, no new construction needed). Re-verified directly against the code
on 2026-07-26. `SourceAliasRule` is a straight `(title, type)` → `(canonicalTitle, canonicalType)`
substitution with no `FieldResolutionChoice`/`FieldMergeDecision` concept at all — it is consulted
*before* any Quote exists in the existing/incoming-field-diff sense `FieldMergeResolver` operates on,
and cannot be expressed as a decision map. There is nothing to "reuse" from `FieldMergeResolver` for
this mechanism, and nothing new to build for matching either: `SourceAliasLookup.TryResolve` (shipped
by #181) already is the complete read path — confirmed a single call site,
`ImportActionPlanner.cs:115`, immediately before `ResolveSourceAsync` at line 132, exactly as
documented. This issue's own scope for `SourceAliasRule` is exactly two additions on top of that
existing lookup — generation (Step 13) and staleness detection (Step 8) — not a redesign of how aliases
are matched.

**Grounding for Step 8's staleness design, found while tracing `ResolveSourceAsync`'s own natural-key
lookup**: `Sql.Sources.SelectIdByTitleAndType` (`src/Quotinator.Core/Queries/Sql.cs:387-388`) is the
exact query a Source is matched against once an alias substitutes in its canonical `(Title, Type)` —
case-insensitive, `IsDeleted = 0` already applied. If a Source that used to match an alias's recorded
`CanonicalTitle`/`CanonicalType` is later renamed via Modify (#162), this same query returns no row for
that pair, and `ResolveSourceAsync` would silently create a brand-new, wrongly-titled duplicate Source
under the stale alias's canonical title — the actual failure mode Step 8 exists to prevent. No new SQL
is needed for the staleness check itself: reusing this existing query at the point `SourceAliasLookup
.TryResolve` succeeds (does a Source currently exist for this pair, before substituting it in) directly
detects the failure without adding a schema field or a new query.

### 5. `ConflictResolutionRule` staleness — implement the check

**Status:** Done, implemented 2026-07-26.

Item 4 is a firm requirement: a rule must be flagged invalid/stale when the underlying source's shape
changes enough that silently reapplying it would produce a wrong result. No existing mechanism in this
codebase did anything equivalent before this step — `CompletenessGuard`/`ShouldBlock` (#165/#168) was
the closest structural precedent (a check that turns a would-be-auto-resolved action into a held one
instead of silently writing), but it guards a different condition (quote already `Complete`).

Implemented by extending `ConflictRuleLookup.TryResolve`
(`src/Quotinator.Data/Import/ConflictRuleLookup.cs`) to a five-parameter form —
`TryResolve(entityId, field, currentExistingValue, currentIncomingValue, out decision, out isStale)` —
reusing the comparison baseline that already existed, unused: `ConflictResolutionRule.ExistingRecord`/
`IncomingRecord` (full field-set snapshots at authoring time, previously "Purely documentation; never
read by the matching logic"). `TryExtractFieldValue` pulls the governed field's recorded value out of
each `JsonElement` snapshot (string, list-of-string, or "absent" — absent counts as a mismatch, never
assumed fresh); `FieldMergeResolver.ValuesEqual` (already case-insensitive, already sequence-aware for
lists) compares each side against the current staging run's real value. A field missing from either
recorded snapshot is always stale — a rule can only be trusted when both sides were actually recorded.
Staleness is per-field, matching how `Fields` is already a list rather than a single value.

All five `ImportActionPlanner.cs` call sites (Quote loop, `PlanSourcesAsync` ×2, `PlanUniverseAsync`,
`PlanSeriesAsync`) were updated identically: build `ruleDecisions` and a `hasStaleRule` flag together
(a stale field is excluded from `ruleDecisions`, never silently merged); if any field is stale, stage
the whole action `Stale` immediately (mirroring the existing `Blocked` early-`continue` pattern exactly
— same payload shape, no `MergedFields`) and skip the normal Pending/Decided path entirely. Checked
*after* the `Blocked`/`CompletenessGuard` check, never before it — a `Complete` row's protection always
wins regardless of a rule's freshness. The three Source/Universe/Series branches also had to widen
their existing "nothing changed, skip silently" early-exit condition to `&& !hasStaleRule`, so a stale
rule on an otherwise-identical field is never silently dropped just because the raw values happen to
already agree (the same reasoning `#181`'s own Custom-rule-on-unchanged-field fix already established
for that early exit).

### 6. New `Stale` status — resolved: a distinct status, not a fallback to `Pending`

**Status:** Done, implemented 2026-07-26. **Decided 2026-07-26**: a stale rule stages as a new,
distinct status — mirroring `Blocked`'s own precedent as a `CompletenessGuard`-driven third state
alongside `Pending`/`Decided` — so a reviewer can tell "this needed a decision because no rule matched"
apart from "this needed a decision because its rule went stale."

`ImportActionStatus.Stale` added to the enum (`src/Quotinator.Data/Entities/SystemImportAction.cs`).
Backed by a real SQL CHECK constraint per ADR 008 — SQLite cannot widen an inline CHECK via `ALTER
TABLE`, so this required a new migration (`ImportActionMigrations.AddStaleStatus`, registered as
`DataOwnedMigrations` version 12) rebuilding `System_ImportActions` under a temporary name, the same
technique migration 10 (`AddBlockedStatusAndMarkCompletenessAs`) already used — carrying `OriginalDecision`
(added by migration 11) through the rebuild this time. `DataBaselineSql`'s own copy of the table
updated in the same commit, per this project's baseline-must-match-incremental-result rule; the
existing `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportActionsSchema` and
`...AcceptSameImportActionsCheckConstraintValues` tests (extended with a `Stale` round-trip case) both
confirm the two paths agree.

Every place that already special-cased `Blocked` as "holds the whole batch, has no `AppliedPolicy` yet,
decidable the same way `Pending` is" was extended to treat `Stale` identically:
`ImportActionResolutionCoordinator.TryApplyBatchAsync`'s pending-ids filter, `SqliteImportActionService
.ExportBatchAsync`'s status filter (so a `Stale` action's fields can be bulk-decided via #163's
export/bulk-decide flow), and `SqliteQuoteImportService`'s updated/pendingActionIds/conflict-entry
counting. `DecideAsync`'s own eligibility check needed no change — it already denies only
`Applied`/`Discarded`, so `Stale` was decidable "for free." The `status=` OpenAPI enum on `GET
/import/actions` is reflection-derived from `ImportActionStatus` (`EnumParameterSchemaTransformer`),
so it picked up `Stale` automatically with no transformer code change — only its own test's hardcoded
expected array needed updating. `[Description]` attributes on `GET /import/actions`, `GET
/import/actions/export`, and `POST /import/actions/bulk-decide` updated to mention `Stale` alongside
`Blocked` wherever they already listed status values.

### 7. Tests — `ConflictResolutionRule` staleness

**Status:** Done, implemented 2026-07-26.

Unit-level (`ConflictRuleLookupTests.cs`, 7 new tests): current values matching the recorded snapshot
exactly is not stale; a mismatch on the existing side is stale; a mismatch on the incoming side is
stale; a field missing from the recorded snapshot is stale; a casing-only difference is not stale
(matching this project's case-insensitive-by-default convention); a matching list/array value is not
stale; a differing list/array value is stale.

Integration-level (`ImportActionPlannerTests.cs`): the pre-existing #181 rule fixtures
(`BuildQuoteTextKeepRule`, and three inline `ConflictResolutionRule` literals for Universe/Series/
Source) all used an empty `{}` `ExistingRecord`/`IncomingRecord` — meaningless before this step (never
read), but every one of those rules would now register as stale on every governed field, since an
absent field never counts as fresh. Fixed by populating each fixture's snapshot with the real
existing/incoming values that test's own setup actually produces, restoring every one of #181's
original auto-resolve assertions as genuine regression coverage for the *non-stale* path — proven by
the full suite passing again with the staleness check live, not by weakening the check to
accommodate stale-by-construction fixtures. `GET /import/actions?status=stale` (case-insensitive
filtering already covered generically by the existing `status=` filter machinery — #154/#211 — needs
no dedicated new test beyond the enum round-trip above) returns exactly the staged-`Stale` actions.

### 8. `SourceAliasRule` staleness

**Status:** Done, implemented 2026-07-26 — **after two false-positive bugs found and corrected live
against real bundled data**, neither catchable by unit tests alone. This step's original design (a
plain `Sql.Sources.SelectIdByTitleAndType` existence check) was wrong: it treats "no Source with this
exact title exists right now" as the staleness signal, but that condition is identical for two
completely different cases — a genuine rename (stale, correctly held) and an alias simply doing its
normal job of guiding the *first-ever* creation of a Source under its correct canonical name (not
stale at all — the common case on a brand-new database, or the first bundled file to ever mention a
title). A title-only existence check cannot tell these apart. Live Docker T2 against the real bundled
alias files (`nikhilnamal17-source-aliases.json` etc.) surfaced this immediately: 7 genuinely fresh,
correctly-behaving aliases were flagged `Stale` purely because their canonical Source hadn't been
created by an earlier bundled file yet — a real regression this plan doc's own smoke-test checklist
addition (CLAUDE.md) now guards against.

**Corrected design**, implemented in `ImportActionPlanner.cs`'s alias-substitution block: the reliable
staleness signal is the Source's own id, not its title. `EntityIdentity.SourceId(canonicalTitle,
canonicalType)` is a deterministic hash fixed at creation and never recomputed on a later Modify (only
the row's `Title`/`Type` columns change; `Id` does not) — so the id a Source would have gotten if
created under exactly the alias's own recorded canonical pair is knowable up front, whether or not that
row exists yet. Three outcomes: (1) no row with that id exists at all → this is a legitimate first-time
creation, never stale; (2) a row with that id exists and its *current* `Title`/`Type` still equals the
alias's own canonical pair → fresh, substitute as before; (3) a row with that id exists but its current
`Title`/`Type` has drifted away from the canonical pair → a genuine rename happened since the alias was
authored → stale, do not substitute, surface via the same `Stale` status Step 6 introduces. Also
checks `sourceIndex` (this planning call's own in-memory same-batch Add/match cache, the same one
`ResolveSourceAsync` itself consults first) before falling back to the DB — a canonical Source
introduced earlier in the very same batch is only staged, not yet actually written, so the DB alone
would find nothing and produce the identical false-positive class of bug. One documented, narrow scope
gap: a Source created under an *explicit* file-carried id (#162) that doesn't match the hash — a later
rename of that specific row won't be caught by the id lookup (though substitution still resolves
correctly via `ResolveSourceAsync`'s own natural-key lookup regardless, so no incorrect behaviour
results, only a narrower staleness-detection miss for that one case).

### 9. Tests — `SourceAliasRule` staleness

**Status:** Done, implemented 2026-07-26. `ImportActionPlannerTests.cs`: an alias whose canonical pair
hashes to a Source that currently exists under a *different* title (a genuine simulated rename) stages
`Stale` on both the Add path and the Modify path, with no `MergedFields`; an alias whose canonical
Source has never existed at all resolves normally (`Decided`, Source created under the canonical
title) — the explicit regression guard for the false-positive bug found live; an alias whose canonical
Source already exists exactly as recorded continues to apply normally, matching #181's own pre-existing
alias tests (unaffected regression). Live-verified against the full real bundled dataset (CLAUDE.md
smoke test): fresh seed and post-reseed `status=pending`/`status=stale` both return `totalCount: 0`
across all four bundled alias files.

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

**Status:** Done, implemented 2026-07-26. `Quotinator.Core.Database.ConflictRuleGenerator.Generate`
takes the same `IReadOnlyList<ImportActionFieldRow>` shape `GET /import/actions/export` produces,
groups by `EntityId`, and for each group builds `ExistingRecord`/`IncomingRecord` from every row's
`ExistingValue`/`IncomingValue` (the full decidable field set for that entity type, not a subset —
`ImportActionFieldRowMapper.DecidableFieldsByEntityType` already covers every mergeable field per
entity, e.g. Quote's own 8 fields exactly match `QuoteFieldMerge.ToFieldMap`'s set) and one
`ConflictResolutionFieldRule` per row that has a real `Decision` (`Field`/`Decision`/`CustomValue` map
directly to `Field`/`Resolution`/`CustomValue`). An entity with zero decided fields (every row's
`Decision` is `null` — still `Pending`/`Stale`/`Blocked`) produces no rule at all. `genres` is decoded
from its `;`-delimited plain-text export encoding back into a JSON array via the existing
`ImportActionFieldRowMapper.DecodeGenres`, matching every hand-authored rule's own array shape.

### 12. `ConflictResolutionRule` generation — merge-not-overwrite into an existing rule file

**Status:** Done, implemented 2026-07-26. `ConflictRuleGenerator.Merge(ConflictResolutionRuleFile?
existing, IReadOnlyList<ConflictResolutionRule> generated)` is a pure function, deliberately separated
from any file I/O (Step 14 owns reading/writing the actual file): a new `EntityId` not already in
`existing` is appended whole; an `EntityId` already present has only its genuinely new fields (not
already named in that entry's own `Fields`) added — an already-covered field's resolution, and the
entry's own recorded `ExistingRecord`/`IncomingRecord` snapshot, are left exactly as the file already
has them, never overwritten by a fresh generation run.

### 13. `SourceAliasRule` generation — candidate detection, not auto-generation

**Status:** Done, implemented 2026-07-26.

Aliases cannot be generated the same mechanical way `ConflictResolutionRule` entries can: every alias
#181 shipped came from manual title-verification research (a web search per title, per
`docs/workflow/source-verification.md`) confirming what a film's real canonical title is — there is no
decided-action shape (Keep/Replace/Custom) to read a canonical title back from, because nobody
"decides" a canonical title through the normal conflict-review flow. Building an endpoint that
auto-writes alias entries without human verification would violate this project's own source-
verification policy (a title/date claim must be checked against real sources before being recorded,
per `docs/workflow/source-verification.md`'s procedure) — so generation for this mechanism means
**detect and suggest, never auto-write**.

`SourceAliasCandidateGenerator.Generate` (`Quotinator.Data.Import`) groups existing `(Id, Title, Type)`
Source rows by a punctuation-blind, case-blind normalization of `Title` (letters/digits/spaces only,
whitespace collapsed) plus case-insensitive `Type`, then pairs up every two distinct-cased titles
sharing a group. Two rows already identical except for case are never candidates — #175's natural-key
Source matching is already case-insensitive, so two such rows could never both exist in the first
place; this generator exists for the punctuation-level duplicates that catches (a trailing `!`, a curly
vs. straight apostrophe, doubled whitespace — the exact defect classes #181's own cleanup found live),
not a fuzzy string-distance metric, which would trade precision for recall this project doesn't need
yet. A pair already covered by an existing `SourceAliasRule` (on either side) is skipped — not
re-suggested. Output is `SourceAliasCandidate` (two ids, two titles, one type) — a pure suggestion
record with no `canonicalTitle`/`canonicalType` fields at all, so there is nothing to persist even by
mistake; confirming a real duplicate still requires the existing hand-edit path.

### 14. Rule-file endpoints

**Status:** Done, implemented 2026-07-26.

**Design finding that changed this step's shape**: the bundled sources directory (`data/sources/`) is
not on the persistent volume — it's baked into the Docker image (`Program.cs`'s `bundledSourcesDir =
Path.Combine(AppContext.BaseDirectory, "data", DataPaths.SourcesFolder)`, not under `dataDir`), and
read-only in the HA add-on deployment. A generate-and-persist endpoint cannot write there. The existing
auto-update mechanism already solves exactly this for the main data file: `SourceCacheUpdater` caches a
downloaded/refreshed copy under `{dataDir}/sources/download/` (bundled) or `{dataDir}/imports/download/`
(user-added) — genuinely persistent, writable in every deployment. This step reuses the same two
directories as the override target for `ruleFile`/`sourceAliasFile`, rather than writing to the
bundled/image path.

**Registry table**: `System_SourceFileOverrides` (entity `SourceFileOverride`, migration
`SourceFileOverrideMigrations.CreateSourceFileOverridesTable`, `ISourceFileOverrideRegistry`) records,
per (`FileName`, `SeedBatchOrigin`), the override's content hash and originating batch id — an upsert,
not a history log. This is what lets the seeding pipeline know for certain whether an override on disk
is genuinely one this project's own generation mechanism produced, rather than inferring it from file
existence alone. Named with today's `System_` convention; a broader `Import_`-prefix table-naming
standardization pass (and a separate, general import-file content-provenance mechanism) is tracked in
#227, not this issue's own scope.

**Path resolution**: `RuleFileOverridePathResolver`/`IRuleFileOverridePathResolver`
(`Quotinator.Data.Paths`) resolves a plain filename (directory segments/`.`/`..` rejected outright, plus
a resolved-path containment check as defence in depth) plus a `SeedBatchOrigin` to two distinct paths —
`Resolve` (the writable override location under the download-cache directories) and `ResolveBundledPath`
(the read-only bundled/image or user-imports path, used as the merge base when no override is
registered yet). DI-registered via the factory-overload pattern (`dataDir`/`bundledSourcesDir`/
`importsDir` are runtime-computed values).

**Shared effective-content resolution**: `EffectiveRuleFileResolver` (`Quotinator.Data.Import`, static)
is the single place that decides "override or bundled" — used by both
`QuotinatorDatabaseInitializer.LoadConflictRulesAsync`/`LoadSourceAliasesAsync` (the seeding pipeline)
and the new endpoints below, so both agree on what "currently effective" means. Extracted specifically
to avoid a real correctness bug found before it shipped: an endpoint that generated an override without
first reading the *current effective* content (bundled file included) would silently drop that bundled
file's own hand-authored rules the first time an override was ever registered for it, since the read
path replaces the whole file rather than merging at load time. `ReadEffectiveContentAsync` is the
merge-base read the generate endpoint uses; `ResolveEffectivePathAsync` is what the seeding pipeline
uses to decide which path to actually load. A `logPrefix` parameter (default `[Database - Seed]`) lets
each caller supply its own structured-log prefix — `[Api - Import]` from the endpoints — rather than
every log line claiming to be the seeding pipeline.

**Endpoints, implemented 2026-07-26** — `ImportRuleEndpoints.cs`, under `/api/v1/import/rules/`, tagged
`ApiTags.Import`, matching the existing route-group convention:
- `GET /conflict` (public, no key, matching `/actions/export`'s precedent) — returns the current
  effective `ConflictResolutionRule`s for `fileName`/`origin` (override if registered and hash-verified,
  else bundled), plus `isOverrideActive`. `404` if neither exists.
- `POST /conflict/generate` (admin, `X-Api-Key`) — takes `fileName`/`origin`/`batchId`, builds rules
  from the batch's decided export rows (Steps 10–12), merges into the current effective content (never
  overwriting an already-covered entity/field), writes the result to the override location, registers
  its hash, and returns the merged file plus `rulesAdded`.
- `DELETE /conflict` (admin, `X-Api-Key`) — un-registers the override (the file itself is left on disk,
  harmless since it's never trusted without a matching registration); `404` if nothing is registered.
- `GET /alias` (public, no key) — runs Step 13's `SourceAliasCandidateGenerator` against every live
  Source row, filtered against `fileName`/`origin`'s own currently-effective `SourceAliasRule` file (via
  the same `EffectiveRuleFileResolver`, so an override for the alias file is honoured too, even though
  aliases have no `POST`/`DELETE` of their own — only the `ConflictResolutionRule` side needs a write
  path; a confirmed alias is still a manual hand-edit of the source file per Step 13's own design). No
  request body, no write of any kind — read-only by construction.

### 15. Tests — rule generation (both mechanisms)

**Status:** Done, implemented 2026-07-26.

`ConflictRuleGeneratorTests.cs` (11 tests): one decided field for one entity produces a single
one-field rule; multiple decided fields for the same entity collapse into one rule with multiple
`Fields` entries (proving Step 10's grouping-by-`EntityId` claim, not just asserting it); an entity with
every field still undecided produces no rule at all; `Custom` resolution carries its `CustomValue`;
`ExistingRecord`/`IncomingRecord` reflect every row regardless of decision; `genres` decodes from its
delimited export encoding into a proper array; multiple entities in one batch each get their own rule;
merging with no existing file returns the generated rules as-is; a new `EntityId` is appended; an
already-covered field's hand-authored resolution is never overwritten even when regenerated with a
different value; a genuinely new field for an already-covered entity is added alongside the existing
one.

`SourceAliasCandidateGeneratorTests.cs` (10 tests): a punctuation-only difference (`"Airplane!"` vs.
`"Airplane"`), a curly-vs-straight apostrophe, and doubled whitespace all surface as candidates; a
case-only difference never surfaces (guarded even though #175 already prevents it from occurring in
live data); same normalized title under a different `Type` is not grouped; no duplicates present
returns empty; an already-aliased pair — checked from either side of the pair — is never re-suggested;
a three-way duplicate group produces every pairwise combination; the candidate type carries no
`canonicalTitle`/`canonicalType` field, so nothing could be written even by mistake.

`ImportRuleEndpointsTests.cs` (18 tests) covers all four endpoints end to end. `ConflictResolutionRule`
side: missing `fileName`/invalid `origin` → `422`; neither bundled nor override exists → `404`; a
bundled-only file returns `isOverrideActive: false`; a registered override returns its own rules with
`isOverrideActive: true`; `generate` without `X-Api-Key` → `401`, without `batchId` → `422`; a valid
generate call writes the override file, registers it, and returns `rulesAdded`; **generating from a
batch that only touches one entity does not drop an already-existing bundled rule for a different
entity** — the exact correctness gap `EffectiveRuleFileResolver` exists to close, proven end to end
rather than only at the unit level; `DELETE` without a key → `401`, with nothing registered → `404`,
and removing a registration makes a subsequent `GET` fall back to the bundled copy. `SourceAliasRule`
side: missing `fileName` → `422`; no `X-Api-Key` required; a near-duplicate pair is surfaced; no
duplicates returns an empty `candidates` array; a pair already covered by the alias file's own existing
entry is not re-suggested; the endpoint never writes to either the bundled alias file or an override
file — asserted by hashing file content before/after the call, not just checking the response shape.

### 16. Rule lookup and auto-apply during staging

**Status:** Done — lookup/auto-apply/staleness-awareness (via #181 + Steps 5–9) all confirmed, and
re-verified 2026-07-26 against the override layer Steps 13–14 added after this status was first
written.

**Re-verification finding**: the override layer sits entirely underneath `LoadConflictRulesAsync`/
`LoadSourceAliasesAsync` — both still return the exact same `ConflictRuleLookup`/`SourceAliasLookup`
types they always did, just built from whichever path `EffectiveRuleFileResolver` resolves to. Every
downstream call site (`ImportActionPlanner.PlanAsync` and its four consulting methods, listed below)
is unchanged and unaware an override even exists. The two new `DatabaseInitializerTests` added in Step
14 (`InitialiseAsync_RegisteredOverrideWithMatchingHash_IsPreferredOverBundledRuleFile` and its
fallback sibling) already prove this end to end — an override actually changes which quote text gets
applied through this exact wiring, not just which file gets read.

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

**Status:** Done, implemented 2026-07-26.

`README.md` and `addon/DOCS.md`: added the four new rule-file endpoint rows (`GET`/`POST /generate`/
`DELETE /conflict`, `GET /alias`); found and fixed two pre-existing gaps while re-verifying `Stale`
coverage — both files' `GET /import/actions` row and `GET /import/actions/export` row were missing
`Stale` from their `status`/eligible-action-status lists (the endpoints' own `[Description]` text in
`ImportEndpoints.cs` already had it correctly; only these two docs had drifted). `schemas/
manifest.schema.json` needed no change, confirmed (Step 2 — no new manifest property). `scripts/
SOURCES.md` gained a new step 6 ("Check for near-duplicate Source titles") in the source-adding
workflow, pointing at `GET /import/rules/alias` right after generating a new source's initial file —
exactly when a duplicate-title risk is highest, per the original plan for this step; existing steps 6-7
renumbered to 7 accordingly (this doc had **no** prior mention of `ruleFile`/`sourceAliasFile` at all,
not just a missing endpoint reference). `CLAUDE.md`'s living T2 smoke-test checklist gained a new
section exercising all four endpoints against real bundled data (`quotinator-curated-conflict-rules.json`/
`quotinator-curated-source-aliases.json`), including the override-preference and merge-preserves-
existing-rules guarantees end to end, per this project's "every new endpoint ships with a T2 smoke test"
convention.

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
| 5 | ✅ | A `ConflictResolutionRule` is flagged `Stale` (not silently applied, not silently discarded) when the underlying source's shape has changed enough to invalidate it | Unit test | `ConflictRuleLookupTests` (7 staleness cases) + `ImportActionPlannerTests` integration coverage, implemented 2026-07-26 |
| 6 | ✅ | A `SourceAliasRule` is flagged `Stale` when its recorded canonical value no longer matches the live Source | Unit test + Live (T2) | `ImportActionPlannerTests` (Step 9) + CLAUDE.md smoke test against real bundled data, implemented 2026-07-26 |
| 7 | ✅ | Rule generation from a batch's decided actions produces candidate `ConflictResolutionRule` entries, worst case one per action, best case one shared entity rule | Unit test | `ConflictRuleGeneratorTests.Generate_*` (Step 10-12), implemented 2026-07-26 |
| 8 | ✅ | Generation merges into an existing rule file without overwriting manual edits | Unit test + Live (endpoint) | `ConflictRuleGeneratorTests.Merge_*` + `ImportRuleEndpointsTests.GenerateConflictRuleFile_ExistingBundledRules_AreMergedNotDropped`, implemented 2026-07-26 |
| 9 | ✅ | `SourceAliasRule` candidate-duplicate detection surfaces likely duplicates without auto-writing an alias entry | Unit test + Live (endpoint) | `SourceAliasCandidateGeneratorTests.*` + `ImportRuleEndpointsTests.GetSourceAliasCandidates_*`, implemented 2026-07-26 |
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
- Steps 1-7 verified/implemented 2026-07-26: Steps 1-4 confirmed accurate on direct re-inspection of
  the code (no corrections needed); Steps 5-7 (`ConflictResolutionRule` staleness detection, the new
  `Stale` status, and its tests) implemented, full solution suite green (2657 tests), 0 build warnings.
  Live Docker T2 verification (not just unit tests) caught a genuine pre-existing data bug on its first
  real run: a bundled rule's recorded snapshot used a straight apostrophe where the real quote uses a
  curly one — fixed, and added to CLAUDE.md's living smoke-test checklist.
- Steps 8-9 implemented 2026-07-26, after the *first* implementation attempt was proven wrong by live
  Docker T2 against real bundled data, not by unit tests (every existing fixture pre-seeded the
  canonical Source as a real row, masking the bug). The original design ("does a Source with this exact
  title exist right now") cannot distinguish a genuine rename from an alias simply doing its normal job
  of guiding a Source's first-ever creation — both look identical (nothing found). Corrected to an
  id-based check (`EntityIdentity.SourceId`, fixed at creation, unaffected by a later rename) that
  actually distinguishes the two cases; re-verified clean (`status=pending`/`status=stale` both zero)
  against the full real bundled dataset on both a fresh seed and a reseed. This is the second time in
  this same session live T2 verification caught something unit tests alone did not — both findings are
  now permanent entries in CLAUDE.md's smoke-test checklist so neither regresses silently.

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
