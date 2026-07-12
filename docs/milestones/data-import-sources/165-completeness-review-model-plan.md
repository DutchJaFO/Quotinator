# #165 — Generalize record completeness to a 3-state model and hard-block modifying completed rows

**Status:** In progress
**GitHub issue:** #165
**Tiers required:** T1, T2
**Depends on:** #55 (unshipped — edited in place), #154

---

## Scope changes

This issue did not start as its own filed idea. It emerged while planning #162 (Source field
decidability): giving Source's `Title`/`Type`/`Date` a Modify path means the staging engine can, for
the first time, attempt to overwrite a field on a record a human has already reviewed. Verified
against the actual code: `IsComplete`/`NoValueKnown` (#55, `Migration006_RecordCompleteness`) exist
on `Quotes`/`Sources`/`Characters`/`People` today but are write-once-at-insert only — nothing reads
them to make a decision anywhere in the codebase — and `Conversations`/`StageDirections`/`SoundCues`
have neither column. This is a genuine cross-entity mechanism every future "entity X decidability"
issue will need, so it's built once, generically, here — #162 depends on this issue and only consumes
what it builds.

**Verified:** `Migration006_RecordCompleteness` (commit `b8fea98`) has not shipped in any release tag
(latest: `v1.7.2`) and isn't merged to `main` yet — only present on the feature branch this work is
happening on. It can be edited in place; nothing needs migrating forward from a released shape.

Key decisions made during planning (see #162's issue history for the full back-and-forth):

1. Modifying a `Complete` row must always block the **entire batch**, not just the one affected
   action, until a human explicitly decides it — not merely exclude that one action while unrelated
   ones proceed.
2. A record that becomes fully populated through ordinary imports/edits, without a human ever
   explicitly confirming it, should be distinguishable from one a human actually reviewed — hence a
   3-state model (`Incomplete`/`NeedsReview`/`Complete`), not a 2-state bool.
3. Marking a record `Complete` is always available as part of deciding any import action (any entity
   type), not gated behind a separate future admin endpoint — the moment a human reviews a decision is
   exactly the moment they can also confirm the record is done.

---

## Design

### 1. `CompletenessStatus` replaces `IsComplete`

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

Edit `Migration006_RecordCompleteness` in place (`src/Quotinator.Engine/Database/
QuotinatorMigrations.cs`) to add a 3-state enum column instead of a bool:

```sql
CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
  CHECK (CompletenessStatus IN ('Incomplete','NeedsReview','Complete'))
```

on `Quotes`/`Sources`/`Characters`/`People`, plus `NoValueKnown` (unchanged JSON-array-of-field-names
shape, already present on these four). Same migration is widened to **also add both
`CompletenessStatus` and `NoValueKnown`** to `Conversations`/`StageDirections`/`SoundCues`, which have
neither column today, so all seven content tables gain the same model together. Update the
fresh-database baseline schema in the same commit; add a schema-drift test per the existing
baseline/incremental-replay convention (`Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`'s
precedent).

- `Incomplete` (default) — nothing known yet, exactly today's `IsComplete=0` meaning.
- `NeedsReview` — **system-set only**: transitions from `Incomplete` when an apply (Add or Modify)
  results in `NoValueKnown` becoming empty while status was still `Incomplete`. Never auto-transitions
  away from `Complete`.
- `Complete` — **human-set only**, via the decide endpoint (§5).

New `CompletenessStatus` enum lives in `Quotinator.Data.Entities` (mirrors `ImportActionStatus`/
`ImportBatchStatus` — Data-owned, `[SafeValue<TEnum?>]`-backed per ADR 008, registered by the base
`DatabaseConfiguration`) since it's meant to be reusable by any consuming project's entities, not
Quotinator-domain-specific. The migrations that add the columns stay in each table's owning project
(`Quotinator.Engine`'s `QuotinatorMigrations`) per CLAUDE.md's Data/Engine migration-ownership split —
only the enum type and CHECK-constraint pattern are Data-owned.

### 2. Domain-agnostic completeness guard

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

New static helper, `src/Quotinator.Data/Import/CompletenessGuard.cs`, alongside `FieldMergeResolver`
(same domain-agnostic contract — operates only on `CompletenessStatus` + field-name strings):

```csharp
public static class CompletenessGuard
{
    public static bool ShouldBlock(CompletenessStatus status, IReadOnlySet<string> changedFields);
    public static CompletenessStatus ComputeNextStatus(CompletenessStatus current, IReadOnlyList<string> noValueKnownAfterApply);
}
```

`ComputeNextStatus` transitions `Incomplete → NeedsReview` whenever `noValueKnownAfterApply` is empty
— regardless of whether that's because a later apply just filled in the last gap, or because the row
was fully specified from the moment it was created. Both are equally "nothing is currently flagged
unknown, worth a human confirming it's actually correct." An earlier revision of this design required
seeing a genuine non-empty→before/empty→after transition, reasoning that a brand-new row's vacuously
empty `NoValueKnown` shouldn't count — that reasoning doesn't hold: an empty list means the same thing
regardless of how the row got there, and nothing in the codebase yet populates `NoValueKnown` with
real per-field markers at creation anyway (that population logic doesn't exist yet — everything the
current insert paths write for it is a hardcoded `'[]'`), so in practice every newly created row
reaches `NeedsReview` immediately today. That's a real, visible consequence of restoring the simpler
design, not a bug — confirmed and reverted back to this simpler form after review.

`ShouldBlock` returns true only when `status == Complete` — a `NeedsReview` row hasn't been
human-confirmed yet and stays freely correctable. `ComputeNextStatus` implements the `Incomplete →
NeedsReview` auto-transition; called once per apply, per entity, wherever `NoValueKnown` is
recomputed.

### 3. `ImportActionStatus.Blocked` + whole-batch hold

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

1. `ImportActionStatus` (`Quotinator.Data.Entities`) gains `Blocked`. New migration widening
   `System_ImportActions.Status`'s CHECK constraint (Data-owned migration, rebuild-under-temp-name
   pattern per `database-conventions.md`'s `Migration004_ImportBatchTypeUserSeed` precedent) + update
   the fresh-database baseline schema in the same commit. Same migration also adds `MarkCompletenessAs`
   (§5) to `System_ImportActions` — same table, same commit.
2. `ImportActionResolutionCoordinator.TryApplyBatchAsync` (`Quotinator.Data.Import`) — the guard that
   currently blocks the whole batch only on `Status == Pending` widens to `Status is Pending or
   Blocked`. A batch containing even one unresolved `Blocked` action holds entirely — nothing in it
   applies, including unrelated, otherwise-ready actions, until every `Blocked` action is resolved.
3. `DecideAsync`/`UndoDecisionAsync` behaviour for `Blocked` needs no further change: deciding a
   `Blocked` action is already permitted (rejects only `Applied`/`Discarded`); undo already reverts
   unconditionally to `Pending`.
4. `TryReverseBatchAsync` already blocks on `Status != Applied` — no change needed; a batch containing
   an unresolved `Blocked` action still can't be reversed until resolved.

### 4. Where entities call the guard

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

Each entity's own planner code (e.g. #162's `ImportActionPlanner.PlanSourcesAsync`, and any future
sibling for Character/Person/etc.) calls `CompletenessGuard.ShouldBlock(existingRow.CompletenessStatus,
changedFieldNames)` before deciding whether to stage a `Modify` or a `Blocked` action — this stays
domain-specific (each entity knows its own field names), consuming the generic guard rather than
reimplementing it. `SqliteImportActionService.ComputeAmbiguousFields`/decide-rejection messaging
widens from Quote-only to any entity type with a `Pending`/`Blocked` action.

### 5. Explicit completeness override at decide time

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

Deciding *any* import action (any entity type, not just a `Blocked` one) can always also set the
target record's `CompletenessStatus` directly — most usefully `Complete`. If the decide call doesn't
provide this, the automatic `Incomplete → NeedsReview` computation applies instead, which by
construction never touches an already-`Complete` row.

Traced end to end against the real decide/apply plumbing: decide never writes the target table
directly — `SqliteImportActionService.DecideAsync` resolves field decisions into a payload and calls
`ImportActionResolutionCoordinator.DecideAsync`, which calls `SystemImportActionWriter.MarkDecidedAsync`
to persist `Status → Decided` + `MergedFields` on the `System_ImportActions` row. The target-table
write happens later, at apply time, when `ApplyResolvedActionAsync` reads `MergedFields` back out. So
the completeness override is recorded at decide time, applied at apply time — same two-phase shape as
every other decided field.

- `SystemImportAction` gains `MarkCompletenessAs: SafeValue<CompletenessStatus?>` (Data-owned column,
  Data-owned enum, entity-agnostic — same column regardless of `EntityType`).
- `ConflictDecisionRequest` gains a shared, entity-agnostic `CompletenessStatus? MarkCompletenessAs`
  property (not per-field like `SourceTitle`/`QuoteText`) — available on every decide call regardless
  of entity type; no new endpoint/DTO needed.
- `SqliteImportActionService.DecideAsync` → `ImportActionResolutionCoordinator.DecideAsync` (new
  optional parameter) → `SystemImportActionWriter.MarkDecidedAsync` (new optional parameter) — threads
  the value through to persist it alongside `MergedFields`/`Status`.
- `ApplyResolvedActionAsync`'s per-entity branches read `action.MarkCompletenessAs` back: if set,
  write it directly (human override always wins, regardless of current status); if null, fall back to
  `CompletenessGuard.ComputeNextStatus`. Applies to **Quote's own existing apply path too** — Quote
  already has both columns from #55, so it gets the same behaviour as part of this issue.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `CompletenessGuard.ShouldBlock` returns true only when status is `Complete` | Unit test | `CompletenessGuardTests.ShouldBlock_StatusComplete_ChangedFieldsPresent_ReturnsTrue` + 3 negative cases |
| 2 | ✅ | `CompletenessGuard.ComputeNextStatus` transitions `Incomplete → NeedsReview` whenever `NoValueKnown` is empty afterward — including a fresh row that was empty from the start | Unit test | `CompletenessGuardTests.ComputeNextStatus_IncompleteWithEmptyNoValueKnown_TransitionsToNeedsReview` + `..._FreshRowFullySpecifiedAtCreation_AlsoTransitionsToNeedsReview` |
| 3 | ✅ | `ComputeNextStatus` never demotes an already-`Complete` status | Unit test | `CompletenessGuardTests.ComputeNextStatus_Complete_NeverDemoted` |
| 4 | ✅ | `Blocked` status accepted by the widened CHECK constraint | Unit test | Covered by `ImportActionResolutionCoordinatorTests`'s `Blocked`-status test fixtures inserting/querying the real schema; no separate migration test needed since the table was never shipped (edited in place) |
| 5 | ✅ | Baseline schema and incremental replay produce identical `CompletenessStatus`/`NoValueKnown`/`Blocked` schema across all affected tables | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`, `Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` (both passing against the revised schema) |
| 6 | ✅ | A `Blocked` action anywhere in a batch prevents the whole batch from applying, including unrelated ready actions | Unit test | `ImportActionResolutionCoordinatorTests.TryApplyBatchAsync_BlockedActionInBatch_HoldsEntireBatch` |
| 7 | ✅ | Once the `Blocked` action is decided, the rest of the batch (previously held) applies normally | Unit test | `ImportActionResolutionCoordinatorTests.TryApplyBatchAsync_BlockedActionResolved_UnrelatedActionsThenApply` |
| 8 | ✅ | `MarkCompletenessAs` provided at decide time overrides auto-compute at apply time | Unit test | `SqliteImportActionServiceTests.ApplyBatchAsync_MarkCompletenessAsProvided_OverridesAutoCompute` |
| 9 | ✅ | `MarkCompletenessAs` omitted falls back to `ComputeNextStatus` | Unit test | `QuoteImportServiceTests.ImportAsync_FreshDatabase_NoValueKnownEmptyAndCompletenessAlreadyNeedsReview`, `ConflictResolutionTests.Seed_FreshQuote_NoValueKnownEmptyAndCompletenessAlreadyNeedsReview` — confirm a real Add (no `MarkCompletenessAs` override) genuinely reaches `NeedsReview` via auto-compute, not just that the override path works |
| 10 | ✅ | Quote's own apply path respects an already-`Complete` status when `MarkCompletenessAs` is omitted | Unit test | `SqliteImportActionServiceTests.ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` |
| 11 | ✅ | Decide endpoint accepts and persists `MarkCompletenessAs`, visible after apply | Unit test | `ImportActionResolutionCoordinatorTests.DecideAsync_MarkCompletenessAsProvided_PersistsOnTheAction` (Data layer) + `SqliteImportActionServiceTests.ApplyBatchAsync_MarkCompletenessAsProvided_OverridesAutoCompute` (Engine layer, end to end through `ConflictDecisionRequest`) |
| 12 | ✅ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); `dotnet test --configuration Release` → all 9 projects passing, 1150 tests total (Api.Tests 251, Engine.Tests 273, Data.Tests 364) |
| 13 | ❌ | T1 — full stage → decide (with/without `MarkCompletenessAs`) → apply cycle; a `Complete` row's field genuinely cannot be silently overwritten | Live | Developer's own Visual Studio pass |
| 14 | ❌ | T2 — same cycle in Docker | Live | Per `CLAUDE.md`'s smoke-test checklist, extended with a `Complete`-status batch-hold scenario |

---

## Not in scope for this issue (deferred)

- Automatic re-keying of a pre-existing, natural-key-matched row onto an explicit file-carried id —
  #162's own concern, not this issue's.
- A UI/admin endpoint dedicated to browsing or bulk-setting completeness status outside the decide
  flow — the decide-time override (§5) covers the only mechanism this issue commits to.
- Extending decidability to Character/Person/Conversation/StageDirection/SoundCue — each is its own
  future "entity X decidability" issue; this issue only builds the mechanism they'll all share.
