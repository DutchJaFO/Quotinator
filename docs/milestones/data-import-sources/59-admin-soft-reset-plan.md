# #59 — Admin: undo an applied import batch

**Status:** Planning
**GitHub issue:** #59
**Tiers required:** T1, T2
**Depends on:** #58, #56, #154

---

## Scope changes

This issue's original design (soft-delete/restore directly via `ImportBatchId` foreign keys, with an
audit-log timestamp heuristic for "has this changed since import") predates #154, which has since
shipped `System_ImportActions` — a durable per-row Add/Modify log with full before/after field
snapshots. That makes an exact, action-level undo possible instead of an approximate, whole-record
one, so this plan is rewritten entirely against #154's shipped design. `System_ImportConflicts`
(#149), which the original plan's audit-log section pointed at, was itself retired in #154 Phase B.
The original spec's `batch_reset`/`batch_restore` audit entries are dropped in favor of reusing the
already-shipped `SystemChangeLog` (#56) mechanism every apply-time action already writes to.

The user's framing for this redefinition: undo replays a batch's own recorded actions in reverse —
`Add ⇒ remove`, `Modify (edit) ⇒ revert to the recorded pre-change snapshot`.

Three decisions were confirmed with the user during planning:

1. **Route:** `POST /api/v1/import/actions/reverse?batchId=`, matching #154's `apply`/`discard`
   convention (`Import` tag, query-string `batchId`) rather than reviving the original issue's
   `/admin/import-batches/{id}/...` shape.
2. **A newer batch touching the same entity blocks reversal (422), it does not just warn.** The
   original design's `modifiedAfterImport` was informational only; this plan hard-refuses instead, so
   undo never silently clobbers a later batch's work.
3. **Redo is out of scope.** `Reversed` is a genuine terminal state — a reversed batch stays reversed;
   re-importing the same data starts a fresh batch. No redo endpoint or inverse-batch mechanism.

**A fourth item changes already-shipped #154 code, not just new #59 code, so it is recorded here as
a scope decision rather than folded silently into a step.** Tracing the actual reversal path against
`ImportActionPlanner.cs`/`QuoteSeedWriter.cs`/`Sql.cs` surfaced that #59 is the first feature to ever
soft-delete a `Quotes`/`Sources`/`Characters`/`People` row. `Quotinator.Data` already has a generic,
tested soft-delete mechanism for any `RecordBase`+`[Table]` entity (`RepositorySql.SoftDelete`/
`HardDelete`, see step 2) — none of these four tables need new SQL for the soft-delete itself. But
every existence check `#154`'s planner relies on for duplicate detection
(`Sql.Sources.SelectIdByTitleAndType`, `Sql.Characters.SelectIdBySourceAndName`,
`Sql.People.SelectIdByName`, `QuoteSeedWriter.TryGetExistingFieldsAsync` → `Sql.Quotes.SelectRawById`)
filters `WHERE IsDeleted = 0`, and all four tables' inserts are `INSERT OR IGNORE`. Once a row is
soft-deleted, re-importing the same content computes the same deterministic id, the existence check
finds nothing (it's soft-deleted, so invisible), the planner stages a fresh Add, and the
`INSERT OR IGNORE` silently no-ops against the still-occupied primary key — the action reports
`Applied`, but nothing was written and the row stays permanently invisible. This is a latent hazard
in the soft-delete-plus-idempotent-insert combination generally, reachable for the first time through
undo.

**Resolved:** the user's rule is that a soft-deleted row never blocks a fresh insert at its id — when
an insert targets an id currently occupied by a soft-deleted row, that old row is hard-deleted first,
then the insert proceeds normally. This needs no schema change (`Id` stays a plain `PRIMARY KEY`, no
partial/filtered unique indexes, no foreign-key implications to work through — confirmed against
SQLite's own foreign-key documentation that a plain `PRIMARY KEY` remains the simplest and most
correctly-supported parent key). It only requires calling the already-existing
`RepositorySql.HardDelete(tableName)` immediately before each of the four existing insert statements,
inside the shared apply-time helpers in `SqliteImportActionService` (`EnsureSourceExistsAsync`/
`EnsureCharacterExistsAsync`/`EnsurePersonExistsAsync`/the Quote-Add branch) — the same code path
already shared by `/import`, `/import/preview`, and seeding, not new #59-only code, and no new SQL
either. See steps 2 and 3.

---

## Spec requirements

1. An endpoint reverses every `Applied` `SystemImportAction` belonging to a batch, in one atomic
   operation: `POST /api/v1/import/actions/reverse?batchId=`.
2. Per action, dispatch on `ActionType`:
   - **`Add`** → soft-delete the created record (`IsDeleted = 1`, `DateDeleted = now`) via the
     already-existing, already-tested generic `RepositorySql.SoftDelete(tableName)` — no new SQL
     needed (see step 2).
   - **`Modify`** → write the row back to the field values captured in `ExistingValue`, with
     `SourceId`/`CharacterId`/`PersonId` re-resolved via natural-key lookup (not trusted verbatim —
     `ExistingValue`'s stored ids are the *incoming* quote's resolved ids, not the existing row's
     actual linkage; see step 5) and `ImportBatchId` set back to `ExistingBatchId`.
   - A **`Modify` action whose `AppliedPolicy == Skip`** never wrote anything at apply time — its
     reversal is a no-op write, but the action still transitions to `Reversed`.
3. **`Source`/`Character`/`Person` Add-reversal is conditional.** These three are inserted
   idempotently and the same stable id can legitimately be staged as an Add by more than one batch,
   or referenced by a manually-created quote. Soft-delete only if a live-reference-count check
   (`SELECT COUNT(*) FROM Quotes WHERE SourceId = @id AND IsDeleted = 0`, and the equivalent for
   `Characters.SourceId`) finds zero remaining references — correct regardless of which batch or
   manual edit holds the reference, replacing the original spec's FK-join "shared by another batch"
   check.
4. **Ordering: bottom-up.** Every `Quote` action in the batch reverses before any `Character`, and
   every `Character`/`Person`/`Source` Add is evaluated only after all the batch's own Quote
   reversals complete — otherwise item 3's reference check would see the batch's own
   about-to-be-removed quotes and wrongly conclude the entity is still in use.
5. **Safety gate — no newer batch may have touched the same entity.** For every `EntityId` the batch
   touches, check whether any *other* batch's `Applied` action against the same `EntityId` has a
   later `DetectedAt`/`AppliedAt`. If one exists, refuse (422) and report which action(s)/batch(es)
   are blocking. A `Reversed` action never counts as blocking — only `Applied` ones do.
6. **`?preview=true`** — computes and returns the same summary (would-soft-delete /
   kept-because-still-referenced / blocked-by-a-newer-batch / blocked-by-unresolvable-linkage)
   without writing anything.
7. Every reversed row's change is logged via `SystemChangeLog` (#56) — `ChangeAction.SoftDelete` for
   a reversed Add, `ChangeAction.Modified` for a reversed Modify — reusing the existing
   `QuoteSeedWriter.LogChangeAsync`/`ChangeLogContext` helper. No new audit mechanism.
8. Requires `AdminApiKey` auth.
9. Rate limited under the `Admin` policy — confirmed as what the sibling `/import/actions/apply` and
   `/import/actions/discard` endpoints already use.
10. Redo (re-applying a reversed batch) is out of scope — `Reversed` is a terminal state.
11. Unknown `batchId` → 404 (`ImportBatchNotFoundException`), matching `/import/actions/apply`/
    `/import/actions/discard`.
12. A batch with zero actions, or whose actions are all already `Discarded`/`Reversed` → 422/state
    exception, not a silent 200 — mirrors `DiscardBatchAsync`'s existing guards.
13. The `ImportBatch` row itself is never soft-deleted by reversal — only `Status`/`ReversedAt`
    change, the same way `Discarded` only changes `Status`. `RecordCount` is left untouched as a
    historical fact, not decremented.
14. `?batchId=` matches case-insensitively, exactly like the existing `/import/actions/apply`,
    `/import/actions/discard`, and `GET /import/actions` filters (`UPPER(BatchId) = UPPER(@batchId)`
    in `Sql.SystemImportActions`) — this project has already hit and fixed this exact bug once for
    the sibling endpoints (#154 verification row 19); the new endpoint must not reintroduce it.

---

## Steps

### 1. Add the `Reversed` terminal state

**Status:** ⬜ Not started

Add `Reversed` to `ImportActionStatus` and `ImportBatchStatus`. Widen both `CHECK` constraints to
include it. Add a nullable `ReversedAt` column to `SystemImportAction` (mirroring `DiscardedAt`/
`AppliedAt`) and to `ImportBatch` (mirroring `AppliedAt`).

Confirmed via `git show v1.7.2:src/Quotinator.Engine/Database/QuotinatorMigrations.cs` that neither
`System_ImportActions` nor `ImportBatches.Status` exist in the last published release (`v1.7.2`) —
both were introduced entirely within the still-unreleased #154. Per ADR 009/CLAUDE.md's
migration-freeze rule (which only protects migrations already applied to a real, released database),
the `CHECK` constraints and column definitions can be edited in place rather than requiring ADR 008's
create-rebuild-rename dance. **Re-verify this immediately before implementing** — if a release is
tagged between now and then, this step reverts to a normal ADR-008 widening migration.

### 2. Reference-count SQL (soft-delete itself already exists, generically)

**Status:** ⬜ Not started

`Quotinator.Data` already provides a generic, tested soft-delete/hard-delete mechanism —
`RepositorySql.SoftDelete(tableName)` / `RepositorySql.HardDelete(tableName)` — exercised through
`SqliteRestorableRepository<T>` and covered by `RepositorySqlGuardTests`/
`SqliteRestorableRepositoryTests`. `Character`/`Person`/`QuoteEntity`/`Source` already carry the
`[Table(...)]` attribute and inherit `RecordBase`, so they already qualify. `Quotinator.Data` already
grants `InternalsVisibleTo` to `Quotinator.Engine`, so these `internal` factory methods are directly
callable from Engine code today — **no new `Sql.Quotes.SoftDelete`-style consts, and no visibility
change, are needed.**

The repository *object* (`SqliteRestorableRepository<T>`) is not a drop-in fit for this specific code
path, though: its methods either open their own throwaway connection or take an `IUnitOfWork`, and
`SqliteUnitOfWork.BeginTransactionAsync()` always opens a brand-new connection — there is no
constructor for wrapping an already-open external connection/transaction, which is exactly the shape
`IImportActionCoordinator`'s shared-batch callback provides (one connection/transaction covering the
whole reversal, so it all commits or rolls back together). Step 5 therefore calls
`RepositorySql.SoftDelete`/`HardDelete`'s SQL text directly via Dapper against that shared connection
— the same raw-SQL-on-a-shared-connection style `ApplyResolvedActionAsync` already uses for its own
writes (and, consistent with that existing code, logs only to `SystemChangeLog`, not the generic
`SystemAuditEntry` the repository object would have written automatically).

The one genuinely new query this step needs is the live-reference-count check for
Source/Character/Person Add-reversal (spec item 3) — Quote-specific FK semantics, not something the
generic repository layer provides for any `RecordBase` table.

### 3. Make the shared insert paths resurrection-safe

**Status:** ⬜ Not started

Per the Scope changes decision: before each of the four existing insert statements
(`Sql.Quotes.Insert`, `Sql.Sources/Characters/People.InsertIfNotExists`), call the already-existing,
already-tested `RepositorySql.HardDelete(tableName)` (`DELETE FROM {tableName} WHERE Id = @id AND
IsDeleted = 1` — see step 2) in `SqliteImportActionService`'s `EnsureSourceExistsAsync`/
`EnsureCharacterExistsAsync`/`EnsurePersonExistsAsync` and the Quote-Add branch of
`ApplyResolvedActionAsync`, immediately before the insert. If the row was active, the delete affects
zero rows and the subsequent `INSERT OR IGNORE` behaves exactly as it does today (the existing
concurrent-double-Add protection is unaffected). If the row was soft-deleted, it is removed first, so
the insert always succeeds fresh. No new SQL — reuses the same generic factory method as step 5. This
is shared apply-time code — it also fixes resurrection for ordinary `/import` and seeding, not just
undo.

### 4. `IImportActionCoordinator.TryReverseBatchAsync` (`Quotinator.Data`)

**Status:** ⬜ Not started

New method on `IImportActionCoordinator`, sibling to `TryApplyBatchAsync`/`DiscardBatchAsync`.
Refuses (returns blocking ids, does not throw) if any action in the batch isn't `Applied`, or if a
newer batch's `Applied` action shares an `EntityId` with this batch (spec item 5) — both are
expressible generically over `BatchId`/`EntityId`/`Status`/`DetectedAt` without any knowledge of
what a `Quote` is, keeping the ADR 004 Data/Engine boundary intact.

**Decided:** unlike `TryApplyBatchAsync`'s per-action callback, this method takes a whole-batch
callback — `Func<IReadOnlyList<SystemImportAction>, IDbConnection, IDbTransaction, Task>`. Spec
item 4's ordering requirement (Quote before Character before Source/Person) needs the whole batch
visible at once; a per-action callback can't sequence that without the generic coordinator knowing
entity-type semantics, which ADR 004 says it shouldn't. This is an internal API shape decision, not
a scope or behavior question, so it's settled here rather than left open.

### 5. `SqliteImportActionService.ReverseAppliedActionsAsync` (`Quotinator.Engine`)

**Status:** ⬜ Not started

The domain-specific whole-batch callback, sibling to `ApplyResolvedActionAsync`. Sorts the batch's
`Applied` actions Quote → Character → Source/Person, then per action:

- `Quote`/Add → soft-delete via `RepositorySql.SoftDelete("Quotes")` (see step 2).
- `Quote`/Modify (not Skip) → re-resolve `SourceId`/`CharacterId`/`PersonId` from
  `ExistingValue.Fields.Source`/`.Type`/`.Character`/`.Author` via the same natural-key lookups the
  planner uses (`Sql.Sources.SelectIdByTitleAndType`, `Sql.Characters.SelectIdBySourceAndName`,
  `Sql.People.SelectIdByName`) — **do not trust `ExistingValue.SourceId/CharacterId/PersonId`
  directly**. Confirmed in `ImportActionPlanner.PlanAsync`: those fields are populated from the same
  `sourceId`/`characterId`/`personId` locals used for `IncomingValue` (resolved from the *incoming*
  quote's text), not from the existing row's actual linkage — invisible when source/character/author
  text is unchanged between existing and incoming, but wrong the moment a Modify actually changes
  that text (e.g. re-attributing a misattributed quote). If a lookup finds nothing (the original
  Source/Character/Person no longer exists), refuse the whole batch reversal (422) rather than
  restore a dangling link. Restore fields via `Sql.QuoteGenres.DeleteForQuote` + re-insert and an
  extended `Sql.Quotes.UpdateOnNewestWins` that accepts an explicit `ImportBatchId` parameter set to
  `ExistingBatchId` (not the reversing batch's own id — `UpdateOnNewestWins` today always sets
  `ImportBatchId=@batchId` to whichever batch is writing, which is correct on apply but wrong on
  undo).
- `Source`/`Character`/`Person`/Add → soft-delete via `RepositorySql.SoftDelete(tableName)`, only if
  step 2's live-reference-count check finds zero remaining references.

Writes `SystemChangeLog` entries via the existing `QuoteSeedWriter.LogChangeAsync`/`ChangeLogContext`
helper (`SoftDelete` for a reversed Add, `Modified` for a reversed Modify).

### 6. `POST /api/v1/import/actions/reverse?batchId=` endpoint

**Status:** ⬜ Not started

`Import` tag. 404 for an unknown batch (`ImportBatchNotFoundException`). 422 with blocking
ids/reasons for a not-fully-`Applied` batch, a newer-batch conflict, an unresolvable Risk-1 linkage,
or an already-`Discarded`/`Reversed`/empty batch. 200 with a summary on success. `batchId` matched
case-insensitively, consistent with the sibling endpoints.

### 7. `?preview=true`

**Status:** ⬜ Not started

Dry-run the same classification (would-soft-delete / kept-because-still-referenced /
blocked-by-a-newer-batch / blocked-by-unresolvable-linkage) without calling the coordinator's write
path.

### 8. Auth and rate limiting

**Status:** ⬜ Not started

`AdminApiKey` auth + `Admin` rate-limit policy, confirmed as what the sibling `/import/actions/*`
endpoints already use.

### 9. i18n

**Status:** ⬜ Not started

New `ApiMessages` keys for the new 404/422 conditions, each translated in all three
`i18ntext/UI.*.json` locales per CLAUDE.md's localisation checklist — no hardcoded strings.

### 10. Documentation

**Status:** ⬜ Not started

Update `README.md`, `addon/DOCS.md`, OpenAPI `[Description]` attributes, and `RestApi.razor` (the
`/import/actions/*` table — #154 added rows there for `apply`/`discard`; easy to miss since it isn't
in the original issue's own checklist).

### 11. Smoke-test checklist

**Status:** ⬜ Not started

Add this endpoint's curl sequence to `CLAUDE.md`'s Pre-Push Checklist step 6 — the project's single,
living source of truth for T2 verification.

### 12. Schema-drift tests

**Status:** ⬜ Not started

Update `Baseline_And_IncrementalReplay_...` tests for the two new `CHECK` values and the two new
columns, per the existing ADR 008/009 convention.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Reversing a batch transitions every `Applied` action to `Reversed` and the batch to `Reversed`/`ReversedAt`, atomically | Unit test | `ImportActionResolutionCoordinatorTests.TryReverseBatchAsync_AllApplied_MarksEverythingReversed` |
| 2 | ❌ | Quote Add action reversal soft-deletes the Quote row | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteAdd_SoftDeletesQuote` |
| 3 | ❌ | Quote Modify action reversal restores the pre-change field values | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_RestoresExistingFields` |
| 4 | ❌ | Quote Modify reversal restores the correct Source/Character/Person linkage even when the Modify changed the source/character/author text | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_SourceTextChanged_RestoresOriginalLinkage` |
| 5 | ❌ | Quote Modify reversal restores `ImportBatchId` to `ExistingBatchId`, not the reversing batch's own id | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_RestoresExistingBatchId` |
| 6 | ❌ | Skip-policy Modify reversal is a no-op write but still transitions the action to `Reversed` | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_SkipPolicyModify_NoWriteButTransitionsState` |
| 7 | ❌ | Source/Character/Person Add reversal soft-deletes only when no active row still references the entity | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_SourceAdd_SoftDeletesWhenOrphaned` / `_KeepsWhenStillReferenced` |
| 8 | ❌ | Reversal ordering is bottom-up (Quote before Character before Source/Person) | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_OrdersQuoteBeforeSourceCharacterPerson` — case that gives the wrong answer if reversed in the other order |
| 9 | ❌ | Refuses (422) when any action in the batch isn't `Applied` | Unit test | `ImportActionResolutionCoordinatorTests.TryReverseBatchAsync_NotAllApplied_ReturnsBlockingIds` |
| 10 | ❌ | Refuses (422) when a newer batch's `Applied` action shares an `EntityId`; a `Reversed` action never counts as blocking | Unit test | `ImportActionResolutionCoordinatorTests.TryReverseBatchAsync_NewerBatchTouchedEntity_Refuses` / `_ReversedActionDoesNotBlock` |
| 11 | ❌ | Refuses (422) when the natural-key re-resolution finds no match for the original Source/Character/Person | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_OriginalLinkageUnresolvable_Refuses` |
| 12 | ❌ | Re-importing previously-undone content resurrects it (hard-deletes the stale soft-deleted row, inserts fresh) instead of silently no-op'ing | Unit test | `SqliteImportActionServiceTests.ApplyResolvedActionAsync_ReAddAfterUndo_ResurrectsSoftDeletedRow` |
| 13 | ❌ | Every reversed row's change is logged to `SystemChangeLog` (`SoftDelete` for a reversed Add, `Modified` for a reversed Modify) | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_WritesChangeLogEntries` |
| 14 | ❌ | `?preview=true` returns the same summary shape without writing anything | Unit test | `ImportActionEndpointsTests.ReverseActions_Preview_DoesNotWrite` |
| 15 | ❌ | `POST /import/actions/reverse?batchId=` returns 404 for an unknown batch | Unit test | `ImportActionEndpointsTests.ReverseActions_UnknownBatchId_Returns404` |
| 16 | ❌ | Returns 422 for an empty batch or one already `Discarded`/`Reversed` | Unit test | `ImportActionEndpointsTests.ReverseActions_EmptyOrAlreadyTerminalBatch_Returns422` |
| 17 | ❌ | `batchId` matches case-insensitively | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue` |
| 18 | ❌ | `AdminApiKey` auth required; `Admin` rate-limit policy applied | Unit test | `ImportActionEndpointsTests.ReverseActions_NoApiKey_Returns401` |
| 19 | ❌ | Schema-drift: baseline and incremental-replay paths produce identical schema for the two new `CHECK` values and two new columns | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` (extended) |
| 20 | ❌ | T1 — full stage → apply → reverse cycle live; re-import after undo resurrects data | Live | `dotnet run` + curl: import → apply → reverse → `GET /quotes/{id}` 404 → re-import same content → `GET /quotes/{id}` 200 |
| 21 | ❌ | T2 — same cycle verified in Docker | Live | `docker build -f docker/Dockerfile` + container run + the row 20 curl sequence |
