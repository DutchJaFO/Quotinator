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

Decisions confirmed with the user during planning:

1. **Route:** `POST /api/v1/import/actions/reverse?batchId=`, matching #154's `apply`/`discard`
   convention (`Import` tag, query-string `batchId`) rather than reviving the original issue's
   `/admin/import-batches/{id}/...` shape.
2. **A newer batch still being applied blocks reversal (422), it does not just warn.** The original
   design's `modifiedAfterImport` was informational only; this plan hard-refuses instead, so undo
   never silently clobbers a later batch's work. (The exact shape of this check evolved further
   during planning into a strict global stack — see decision 7.)
3. **Redo is out of scope.** A reversed batch stays reversed; re-importing the same data starts a
   fresh batch. No redo endpoint or inverse-batch mechanism.
4. **Soft-delete itself needs no new SQL.** `Quotinator.Data` already has a generic, tested
   soft-delete/hard-delete mechanism for any `RecordBase`+`[Table]` entity — `RepositorySql.SoftDelete`/
   `HardDelete`, exercised through `SqliteRestorableRepository<T>`, covered by
   `RepositorySqlGuardTests`/`SqliteRestorableRepositoryTests`. `Character`/`Person`/`QuoteEntity`/
   `Source`/`ImportBatch` all already qualify (`RecordBase` + `[Table(...)]`). This governs both
   Add-reversal (soft-delete) and the resurrection fix below (hard-delete) — see step 3.
5. **The resurrection hazard (below) is fixed by hard-deleting the stale row, not by inventing new
   uniqueness semantics.** #59 is the first feature to ever soft-delete a `Quotes`/`Sources`/
   `Characters`/`People` row. Every existence check `#154`'s planner relies on for duplicate detection
   (`Sql.Sources.SelectIdByTitleAndType`, `Sql.Characters.SelectIdBySourceAndName`,
   `Sql.People.SelectIdByName`, `QuoteSeedWriter.TryGetExistingFieldsAsync` → `Sql.Quotes.SelectRawById`)
   filters `WHERE IsDeleted = 0`, and all four tables' inserts are `INSERT OR IGNORE`. Once a row is
   soft-deleted, re-importing the same content computes the same deterministic id, the existence
   check finds nothing (soft-deleted rows are invisible to it), the planner stages a fresh Add, and
   `INSERT OR IGNORE` silently no-ops against the still-occupied primary key — the action reports
   `Applied` but nothing was written, and the row stays permanently invisible. The user's rule: a
   soft-deleted row never blocks a fresh insert at its id — when an insert targets an id currently
   occupied by a soft-deleted row, that old row is hard-deleted first, then the insert proceeds
   normally. No schema change (`Id` stays a plain `PRIMARY KEY`, confirmed against SQLite's own
   foreign-key documentation as the simplest, most correctly-supported parent key) — just a call to
   the already-existing `RepositorySql.HardDelete(tableName)` immediately before each insert. This
   touches the shared apply-time helpers in `SqliteImportActionService`
   (`EnsureSourceExistsAsync`/`EnsureCharacterExistsAsync`/`EnsurePersonExistsAsync`/the Quote-Add
   branch), so it also fixes resurrection for ordinary `/import` and seeding, not just undo — recorded
   here because it touches already-shipped #154 code, not just new #59 code.
6. **`ImportBatch` is soft-deleted on successful reversal, reusing its own already-registered
   repository — no new status value anywhere.** `ImportBatch` already inherits `RecordBase`
   (`IsDeleted`/`DateDeleted`), and `IImportBatchRepository` already extends `IRepository<ImportBatch>`,
   which already declares `SoftDeleteAsync` — no new registration needed. On a successful reversal,
   the last step soft-deletes the `ImportBatch` row via that existing repository method. This is the
   *sole* signal that a batch's effects are no longer live. No `Reversed` value is added to
   `ImportActionStatus` or `ImportBatchStatus`, and no `ReversedAt` column is added anywhere —
   `SystemImportAction` rows stay `Applied` permanently, an accurate historical record of what was
   done, which combined with `SystemChangeLog`'s per-field diffs and the batch's own `IsDeleted` flag
   fully reconstructs what happened and that it was later undone.
7. **Batches undo as a strict global stack — LIFO across all batches, not a per-entity check.**
   Reversal is only permitted on the most recently applied batch that is still live
   (`Status = Applied`, `IsDeleted = 0`); an older batch cannot be reversed while a newer one is still
   applied, regardless of whether they touch overlapping entities. With imports `[A]`, `[B]`, `[C]`
   applied in that order, `[C]` must be undone before `[B]`, and `[B]` before `[A]`. A reversed batch
   drops out of the stack entirely via the same `IsDeleted = 0` filter every other query in this
   codebase already uses — there is no separate "is this later batch still a threat" question to
   answer, because a soft-deleted `ImportBatch` is already excluded the same way a soft-deleted row
   anywhere else is. This replaces an earlier draft of this decision that checked per-`EntityId`
   overlap between batches — strictly safer (a later batch that shares no entities with the one being
   reversed still blocks it) and needs no `SystemImportAction` involvement at all, only `ImportBatches`
   — reusing `Sql.ImportBatches.SelectAll` (`WHERE IsDeleted = 0 ORDER BY ImportedAt DESC`, already
   wired through `IImportBatchRepository.GetAllAsync()`): the target batch must be the first
   `Status = Applied` entry in that list. A narrower per-entity check is possible in principle for
   batches known to be fully self-contained (e.g. `[A] = books`, `[B] = movies`, `[C] = tv-series`
   with no cross-references) but is not built here — the safe default (strict stack) is what ships.
   `Quotinator.Data`'s generic coordinator never references `ImportBatch` (an Engine type, per ADR
   004), so this check lives in `Quotinator.Engine`, which is the only layer that knows both tables.
   See step 5.
8. **This redefinition needs zero schema or migration work.** No new enum values, no new columns —
   every previous draft of this plan that proposed a `Reversed` state and `ReversedAt` columns (with
   the associated `CHECK`-constraint and migration-freeze analysis) is superseded by decision 6.

---

## Spec requirements

1. An endpoint reverses every `Applied` `SystemImportAction` belonging to a batch, in one atomic
   operation: `POST /api/v1/import/actions/reverse?batchId=`.
2. Per action, dispatch on `ActionType`:
   - **`Add`** → soft-delete the created record (`IsDeleted = 1`, `DateDeleted = now`) via the
     already-existing, already-tested generic soft-delete mechanism (`RepositorySql.SoftDelete`,
     reached through a repository — see steps 1–2). No new SQL.
   - **`Modify`** → write the row back to the field values captured in `ExistingValue`, with
     `SourceId`/`CharacterId`/`PersonId` re-resolved via natural-key lookup (not trusted verbatim —
     `ExistingValue`'s stored ids are the *incoming* quote's resolved ids, not the existing row's
     actual linkage; see step 6) and `ImportBatchId` set back to `ExistingBatchId`.
   - A **`Modify` action whose `AppliedPolicy == Skip`** never wrote anything at apply time — its
     reversal is a no-op write.
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
5. **Safety gate — strict global stack, LIFO across all batches.** Only the most recently applied
   batch still live (`Status = Applied`, `IsDeleted = 0`) may be reversed — not a per-entity overlap
   check. If a newer batch is still applied, refuse (422) and name it, regardless of whether it
   touches any of the same entities as the batch being reversed.
6. **`?preview=true`** — computes and returns the same summary (would-soft-delete /
   kept-because-still-referenced / blocked-because-a-newer-batch-is-still-applied /
   blocked-by-unresolvable-linkage) without writing anything.
7. Every reversed row's change is logged via `SystemChangeLog` (#56) — `ChangeAction.SoftDelete` for
   a reversed Add, `ChangeAction.Modified` for a reversed Modify — reusing the existing
   `QuoteSeedWriter.LogChangeAsync`/`ChangeLogContext` helper. No new audit mechanism.
8. Requires `AdminApiKey` auth.
9. Rate limited under the `Admin` policy — confirmed as what the sibling `/import/actions/apply` and
   `/import/actions/discard` endpoints already use.
10. Redo (re-applying a reversed batch) is out of scope.
11. Unknown, or already-reversed (`IsDeleted = 1`), `batchId` → 404 (`ImportBatchNotFoundException`),
    matching how a soft-deleted row is treated as absent everywhere else in this codebase.
12. A batch not currently `Applied` (still `Staged`, or already `Discarded`), or with zero actions →
    422/state exception, not a silent 200 — mirrors `DiscardBatchAsync`'s existing guards.
13. On success, the `ImportBatch` row itself is soft-deleted via its own already-registered
    `IImportBatchRepository.SoftDeleteAsync` — the sole signal that the batch is no longer live.
    `RecordCount` is left untouched as a historical fact, not decremented. `SystemImportAction` rows
    for the batch are never modified — they remain `Applied` permanently as the historical record.
14. `?batchId=` matches case-insensitively, exactly like the existing `/import/actions/apply`,
    `/import/actions/discard`, and `GET /import/actions` filters (`UPPER(BatchId) = UPPER(@batchId)`
    in `Sql.SystemImportActions`) — this project has already hit and fixed this exact bug once for
    the sibling endpoints (#154 verification row 19); the new endpoint must not reintroduce it.

---

## Steps

### 1. Let `SqliteUnitOfWork` wrap an externally-owned connection/transaction

**Status:** ⬜ Not started

The repository parameter model (`IUnitOfWork? unitOfWork = null` on every `IRepository<T>` method)
already exists precisely so a caller-supplied transaction can be used instead of the repository
opening its own — that is the whole reason the Unit of Work concept exists (ADR 003). The specific
gap this step closes: `SqliteUnitOfWork.BeginTransactionAsync()` today only knows how to *create* a
connection via its `IDbConnectionFactory` — there is no way to construct one around a connection and
transaction someone else already opened. That is exactly the shape
`IImportActionCoordinator.TryReverseBatchAsync`'s whole-batch callback needs: one connection and
transaction, owned and committed by the coordinator, that repository calls inside the callback must
participate in without taking over its lifecycle.

Add a second, `internal` constructor to `SqliteUnitOfWork(IDbConnection connection, IDbTransaction
transaction)` that sets `Connection`/`Transaction` directly and marks the instance non-owning:
`BeginTransactionAsync`/`CommitAsync`/`DisposeAsync` become no-ops for a wrapped instance (the
coordinator that supplied the connection/transaction remains solely responsible for opening,
committing, and disposing it — repository calls against the wrapped unit of work never commit or
roll back anything themselves today, matching existing repository behavior). Fully backward
compatible: the existing public `SqliteUnitOfWork(IDbConnectionFactory)` constructor and its callers
are unaffected.

### 2. Register the generic restorable repositories

**Status:** ⬜ Not started

`Character`/`Person`/`QuoteEntity`/`Source` already carry `[Table(...)]` and inherit `RecordBase`, so
`SqliteRestorableRepository<T>` — fully generic, already tested against a synthetic `Widget` fixture
in `SqliteRestorableRepositoryTests` — already works for all four with zero new code. Register
`IRestorableRepository<QuoteEntity>`, `<Source>`, `<Character>`, `<Person>` in `Program.cs`. No new
registration is needed for `ImportBatch` — `IImportBatchRepository`/`SqliteImportBatchRepository` are
already registered and already expose `SoftDeleteAsync` (inherited from `IRepository<ImportBatch>`).

The one genuinely new query this issue needs is the live-reference-count check for
Source/Character/Person Add-reversal (spec item 3) — Quote-specific FK semantics
(`SELECT COUNT(*) FROM Quotes WHERE SourceId = @id AND IsDeleted = 0`, and the equivalent for
`Characters.SourceId`), not something the generic repository layer provides for any `RecordBase`
table.

### 3. Make the shared insert paths resurrection-safe

**Status:** ⬜ Not started

Per Scope changes decision 5: before each of the four existing insert statements (`Sql.Quotes.Insert`,
`Sql.Sources/Characters/People.InsertIfNotExists`), call `IRestorableRepository<T>.HardDeleteAsync(id,
wrappedUnitOfWork)` (steps 1–2) in `SqliteImportActionService`'s `EnsureSourceExistsAsync`/
`EnsureCharacterExistsAsync`/`EnsurePersonExistsAsync` and the Quote-Add branch of
`ApplyResolvedActionAsync`, immediately before the insert. If the row was active, the delete affects
zero rows and the subsequent `INSERT OR IGNORE` behaves exactly as it does today (the existing
concurrent-double-Add protection is unaffected). If the row was soft-deleted, it is removed first, so
the insert always succeeds fresh. No new SQL. This is shared apply-time code — it also fixes
resurrection for ordinary `/import` and seeding, not just undo.

### 4. `IImportActionCoordinator.TryReverseBatchAsync` (`Quotinator.Data`)

**Status:** ⬜ Not started

New method on `IImportActionCoordinator`, sibling to `TryApplyBatchAsync`/`DiscardBatchAsync`.
Refuses (returns blocking ids, does not throw) only if any `SystemImportAction` for the batch isn't
`Applied` — the symmetric mirror of `TryApplyBatchAsync`'s "refuse if anything is still Pending"
check, expressible generically over `BatchId`/`Status` with no knowledge of what a `Quote` or an
`ImportBatch` is. Spec item 5's stack-order check does **not** live here (Scope changes decision 7)
— it needs `ImportBatch`, which this Data-layer coordinator must never reference (ADR 004). Otherwise
invokes a whole-batch callback once, in the coordinator's own transaction:
`Func<IReadOnlyList<SystemImportAction>, IDbConnection, IDbTransaction, Task>`. Unlike
`TryApplyBatchAsync`'s per-action callback, this is whole-batch because spec item 4's ordering
requirement (Quote before Character before Source/Person) needs the whole batch visible at once — a
per-action callback can't sequence that without the generic coordinator knowing entity-type
semantics, which ADR 004 says it shouldn't.

### 5. Pre-check in `Quotinator.Engine` before invoking the coordinator

**Status:** ⬜ Not started

Before calling `TryReverseBatchAsync`, `SqliteImportActionService`'s reversal entry point looks up
the `ImportBatch` via `IImportBatchRepository` and refuses (distinct 404/422s, spec items 11–12) if:
not found; already `IsDeleted = 1` (already reversed); or `Status != Applied` (still `Staged`, or
`Discarded`). It then performs spec item 5's stack-order check itself: call
`IImportBatchRepository.GetAllAsync()` (already `WHERE IsDeleted = 0 ORDER BY ImportedAt DESC` —
`Sql.ImportBatches.SelectAll`, no new query), take the first entry whose `Status == Applied`, and
compare its `Id` to the batch being reversed. If they don't match, refuse (422) and name the batch
that's actually on top of the stack — a strict global LIFO check across *all* batches, not scoped to
this batch's own entities, so it needs no `SystemImportAction` involvement at all. This still lives
in Engine rather than step 4's generic coordinator because it needs `ImportBatch`.

### 6. `SqliteImportActionService.ReverseAppliedActionsAsync` (`Quotinator.Engine`)

**Status:** ⬜ Not started

The domain-specific whole-batch callback passed to `TryReverseBatchAsync`, sibling to
`ApplyResolvedActionAsync`. Sorts the batch's `Applied` actions Quote → Character → Source/Person,
then per action:

- `Quote`/Add → soft-delete via `IRestorableRepository<QuoteEntity>.SoftDeleteAsync` (steps 1–2).
- `Quote`/Modify (not Skip) → re-resolve `SourceId`/`CharacterId`/`PersonId` from
  `ExistingValue.Fields.Source`/`.Type`/`.Character`/`.Author` via the same natural-key lookups the
  planner uses (`Sql.Sources.SelectIdByTitleAndType`, `Sql.Characters.SelectIdBySourceAndName`,
  `Sql.People.SelectIdByName`) — **do not trust `ExistingValue.SourceId/CharacterId/PersonId`
  directly**. Confirmed in `ImportActionPlanner.PlanAsync`: those fields are populated from the same
  `sourceId`/`characterId`/`personId` locals used for `IncomingValue` (resolved from the *incoming*
  quote's text), not from the existing row's actual linkage — invisible when source/character/author
  text is unchanged between existing and incoming, but wrong the moment a Modify actually changes
  that text (e.g. re-attributing a misattributed quote). If a lookup finds nothing, refuse the whole
  batch reversal (422) rather than restore a dangling link. Restore fields via
  `Sql.QuoteGenres.DeleteForQuote` + re-insert and an extended `Sql.Quotes.UpdateOnNewestWins` that
  accepts an explicit `ImportBatchId` parameter set to `ExistingBatchId` — kept as a raw, surgical SQL
  statement rather than switching to the generic `IRepository<QuoteEntity>.UpdateAsync`, because
  Dapper.Contrib's generated `UPDATE` writes *every* column: `ExistingValue`'s snapshot never captured
  `IsComplete`/`NoValueKnown` (confirmed against `QuoteSeedWriter.TryGetExistingFieldsAsync`'s field
  list), so a full-entity `UpdateAsync` would silently reset both to their defaults on every
  Modify-undo unless first read back and preserved — the existing targeted statement, which only ever
  touches the columns it names, avoids that risk without extra work.
- `Source`/`Character`/`Person`/Add → soft-delete via the matching `IRestorableRepository<T>`, only
  if step 2's live-reference-count check finds zero remaining references.

As the last step of the callback (still inside the coordinator's one shared transaction, so it commits
or rolls back atomically with everything above): soft-delete the `ImportBatch` row itself via
`IImportBatchRepository.SoftDeleteAsync(batchId, wrappedUnitOfWork)` (Scope changes decision 6).

Writes `SystemChangeLog` entries via the existing `QuoteSeedWriter.LogChangeAsync`/`ChangeLogContext`
helper (`SoftDelete` for a reversed Add, `Modified` for a reversed Modify) for every entity-level
change — unaffected by using `IRestorableRepository<T>` for the writes themselves, since that's a
separate, additive log from the repository's own automatic `SystemAuditEntry` write. Routing
Add/Add-reversal through the repository object is in fact the first time `SystemAuditEntry` gets
exercised by real domain code, not just `SqliteRestorableRepositoryTests`' synthetic fixture — a
reasonable side benefit, not a scope concern.

### 7. `POST /api/v1/import/actions/reverse?batchId=` endpoint

**Status:** ⬜ Not started

`Import` tag. 404 for an unknown or already-reversed batch. 422 with blocking ids/reasons for a
not-`Applied` batch, a stack-order conflict (a newer batch is still applied), an unresolvable
linkage, or an empty batch. 200 with a summary on success. `batchId` matched case-insensitively,
consistent with the sibling endpoints.

### 8. `?preview=true`

**Status:** ⬜ Not started

Dry-run the same classification (would-soft-delete / kept-because-still-referenced /
blocked-because-a-newer-batch-is-still-applied / blocked-by-unresolvable-linkage) without calling
the coordinator's write path.

### 9. Auth and rate limiting

**Status:** ⬜ Not started

`AdminApiKey` auth + `Admin` rate-limit policy, confirmed as what the sibling `/import/actions/*`
endpoints already use.

### 10. i18n

**Status:** ⬜ Not started

New `ApiMessages` keys for the new 404/422 conditions, each translated in all three
`i18ntext/UI.*.json` locales per CLAUDE.md's localisation checklist — no hardcoded strings.

### 11. Documentation

**Status:** ⬜ Not started

Update `README.md`, `addon/DOCS.md`, OpenAPI `[Description]` attributes, and `RestApi.razor` (the
`/import/actions/*` table — #154 added rows there for `apply`/`discard`; easy to miss since it isn't
in the original issue's own checklist).

### 12. Smoke-test checklist

**Status:** ⬜ Not started

Add this endpoint's curl sequence to `CLAUDE.md`'s Pre-Push Checklist step 6 — the project's single,
living source of truth for T2 verification.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `SqliteUnitOfWork` wrapping an external connection/transaction never opens, commits, or disposes it — only the owning caller does | Unit test | `SqliteUnitOfWorkTests.WrappedConstructor_NeverOwnsOrDisposesExternalConnection` |
| 2 | ❌ | `IRestorableRepository<QuoteEntity/Source/Character/Person>` resolve from DI and work against a wrapped unit of work | Unit test | `SqliteRestorableRepositoryTests` (parameterised over the four registered types) |
| 3 | ❌ | Reference-count check finds zero references for an orphaned Source/Character/Person, nonzero for one still in use | Unit test | `SqliteImportActionServiceTests.HasActiveReferences_...` |
| 4 | ❌ | Quote Add action reversal soft-deletes the Quote row | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteAdd_SoftDeletesQuote` |
| 5 | ❌ | Quote Modify action reversal restores the pre-change field values | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_RestoresExistingFields` |
| 6 | ❌ | Quote Modify reversal restores the correct Source/Character/Person linkage even when the Modify changed the source/character/author text | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_SourceTextChanged_RestoresOriginalLinkage` |
| 7 | ❌ | Quote Modify reversal restores `ImportBatchId` to `ExistingBatchId`, not the reversing batch's own id | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_RestoresExistingBatchId` |
| 8 | ❌ | Quote Modify reversal preserves `IsComplete`/`NoValueKnown`, never resets them | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_QuoteModify_PreservesCompletenessFlags` |
| 9 | ❌ | Skip-policy Modify reversal is a no-op write | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_SkipPolicyModify_NoWrite` |
| 10 | ❌ | Source/Character/Person Add reversal soft-deletes only when no active row still references the entity | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_SourceAdd_SoftDeletesWhenOrphaned` / `_KeepsWhenStillReferenced` |
| 11 | ❌ | Reversal ordering is bottom-up (Quote before Character before Source/Person) | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_OrdersQuoteBeforeSourceCharacterPerson` — case that gives the wrong answer if reversed in the other order |
| 12 | ❌ | Refuses (422) when any action in the batch isn't `Applied` | Unit test | `ImportActionResolutionCoordinatorTests.TryReverseBatchAsync_NotAllApplied_ReturnsBlockingIds` |
| 13 | ❌ | Refuses (422) when the target batch is not the most recently applied still-live batch (strict LIFO stack, not scoped to shared entities); succeeds when it is; an already-reversed later batch never blocks | Unit test | `SqliteImportActionServiceTests.CheckStackOrder_NewerBatchStillApplied_Refuses` / `_ReversedLaterBatchDoesNotBlock` / `_TargetIsTopOfStack_Succeeds` |
| 14 | ❌ | Refuses (422) when the natural-key re-resolution finds no match for the original Source/Character/Person | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_OriginalLinkageUnresolvable_Refuses` |
| 15 | ❌ | Re-importing previously-undone content resurrects it (hard-deletes the stale soft-deleted row, inserts fresh) instead of silently no-op'ing | Unit test | `SqliteImportActionServiceTests.ApplyResolvedActionAsync_ReAddAfterUndo_ResurrectsSoftDeletedRow` |
| 16 | ❌ | Every reversed row's change is logged to `SystemChangeLog` (`SoftDelete` for a reversed Add, `Modified` for a reversed Modify) | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_WritesChangeLogEntries` |
| 17 | ❌ | On success, the `ImportBatch` row is soft-deleted; `SystemImportAction` rows for the batch remain `Applied`, untouched | Unit test | `SqliteImportActionServiceTests.ReverseAppliedActionsAsync_SoftDeletesImportBatch_LeavesActionsApplied` |
| 18 | ❌ | `?preview=true` returns the same summary shape without writing anything | Unit test | `ImportActionEndpointsTests.ReverseActions_Preview_DoesNotWrite` |
| 19 | ❌ | `POST /import/actions/reverse?batchId=` returns 404 for an unknown or already-reversed batch | Unit test | `ImportActionEndpointsTests.ReverseActions_UnknownOrAlreadyReversedBatchId_Returns404` |
| 20 | ❌ | Returns 422 for an empty batch or one not currently `Applied` | Unit test | `ImportActionEndpointsTests.ReverseActions_EmptyOrNotApplied_Returns422` |
| 21 | ❌ | `batchId` matches case-insensitively | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue` |
| 22 | ❌ | `AdminApiKey` auth required; `Admin` rate-limit policy applied | Unit test | `ImportActionEndpointsTests.ReverseActions_NoApiKey_Returns401` |
| 23 | ❌ | T1 — full stage → apply → reverse cycle live; re-import after undo resurrects data | Live | `dotnet run` + curl: import → apply → reverse → `GET /quotes/{id}` 404 → re-import same content → `GET /quotes/{id}` 200 |
| 24 | ❌ | T2 — same cycle verified in Docker | Live | `docker build -f docker/Dockerfile` + container run + the row 23 curl sequence |
