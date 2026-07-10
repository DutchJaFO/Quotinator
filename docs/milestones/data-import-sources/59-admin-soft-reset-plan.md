# #59 — Admin: undo an applied import batch

**Status:** Waiting for release
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

**Status:** ✅ Done — `SqliteUnitOfWorkTests` (6 tests) passing, full owning-constructor path unaffected.

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

**Status:** ✅ Done — `IRestorableRepository<QuoteEntity/Source/Character/Person>` registered in
`Program.cs`; `Sql.Characters/People/Sources.CountActiveReferences` added and added to
`SqlQueryGuardTests`' documented aggregate-query inventory (CVE-2025-6965 guard, ADR 001) — caught
and fixed by that test on first run, not missed.

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

**Status:** ✅ Done — `ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow` passing
(full soft-delete-then-reimport cycle: Quote+Source+Character all soft-deleted, re-imported, all
resurrected as live rows). Full suite green after this step (952 tests, 0 failures).

**Design changed from the original plan during implementation — the original per-method approach was
wrong and caught by a genuinely red test, not assumed correct.** The original plan called
`IRestorableRepository<T>.HardDeleteAsync` directly inside `EnsureSourceExistsAsync`/
`EnsureCharacterExistsAsync`/`EnsurePersonExistsAsync` and the Quote-Add branch, immediately before
each insert — i.e. in apply/insert order (Source, then Character, then Quote). Writing the red/green
regression test first (soft-delete all three rows for one quote, then re-import the same content)
surfaced a `SQLite Error 19: FOREIGN KEY constraint failed`: hard-deleting a stale `Source` row fails
while a stale `Character` (or `Quote`) row still physically references it via
`Characters.SourceId`/`Quotes.SourceId` — SQLite enforces foreign keys against the physical row,
completely blind to `IsDeleted`. Apply-order (parent-first: Source, Character, Quote) is the *wrong*
order for clearing stale rows — it needs the reverse (child-first: Quote, Character, then
Source/Person), the same "bottom-up" principle already established for Add-reversal ordering (spec
item 4), just applied to hard-deletes instead of soft-deletes. Per-method placement also couldn't
solve this alone even if reordered, since `System_ImportActions` rows for one quote's Source/
Character/Quote actions can apply in any order the coordinator returns them (not guaranteed
insertion order) — a `Source` action processed before its `Character`/`Quote` action's own
stale-clearing has run would hit the identical failure regardless of the internal ordering inside
`ApplyResolvedActionAsync`.

**Actual design:** a new `ClearStaleAddTargetsAsync(batchId)` runs once per batch, in
`SqliteImportActionService.ApplyBatchAsync`, *before* `IImportActionCoordinator.TryApplyBatchAsync`
opens its own transaction — not per-action, and not inside the apply transaction. It reads all of the
batch's `Add` actions (`ISystemImportActionReader.GetAllForBatchAsync`), groups by `EntityType`, and
hard-deletes via the matching `IRestorableRepository<T>.HardDeleteAsync` (steps 1–2) in strict order:
every `Quote` id, then every `Character` id, then `Source`/`Person` ids — **except Quote itself,
which uses `RepositorySql.HardDelete("Quotes")` directly on a plain string id, not the repository
object; see step 6's write-up for why (`QuoteIdentity.StableId`'s lowercase output).** Running outside
the apply transaction is safe specifically because hard-deleting an already-soft-deleted row is
idempotent — a retry after a failed apply just finds nothing left to clear. This method call site
covers `/import`, the `POST /import?batchId=` alias, and seeding uniformly, since all three converge
on `IImportActionService.ApplyBatchAsync` — confirmed by grep, not assumed.

**A third bug was found live, during T2 (Docker) verification — neither the unit suite nor T1 caught
it, because every prior test used a genre-less quote.** `QuoteGenres` (and `QuoteTranslations`) both
carry a hard FK to `Quotes(Id)`. A Quote-Add reversal only ever soft-deletes the Quote row itself
(step 6) — its genre rows, written by every Add via `QuoteSeedWriter.InsertGenresAsync`, stay
physically present. Re-importing that content then hit the exact `SQLite Error 19: FOREIGN KEY
constraint failed` this whole method exists to prevent, but one level deeper: hard-deleting the stale
*Quote* row was blocked by its own still-present, still-referencing `QuoteGenres` rows. Reproduced
live via `docker run` (full stack trace in the container log, `ClearStaleAddTargetsAsync` line 156)
before being fixed — `Sql.QuoteGenres.DeleteForQuote`/`Sql.QuoteTranslations.DeleteForQuote` (both
already-existing, plain-string `WHERE QuoteId = @id` statements — no new SQL) now run immediately
before the Quote hard-delete. A dedicated regression test using a genre-bearing quote
(`ReverseBatchAsync_ThenReImport_QuoteWithGenres_ResurrectsWithoutForeignKeyViolation`) was added and
confirmed green; the fix was then re-verified live in both `dotnet run` (T1) and a freshly rebuilt
Docker image (T2) before this issue was considered complete.

### 4. `IImportActionCoordinator.TryReverseBatchAsync` (`Quotinator.Data`)

**Status:** ✅ Done — `ImportActionResolutionCoordinatorTests` (4 new tests: whole-batch callback
invoked once, blocks on any non-`Applied` action, no-op on unknown batch, rolls back on callback
throw).

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

**Status:** ✅ Done — `SqliteImportActionServiceTests` (10 tests covering not-found, malformed id,
already-reversed, not-Applied, not-top-of-stack, top-of-stack-then-next-oldest succeeding in order).

**A genuine bug was found and fixed while testing the stack-order check, not assumed correct.**
`Sql.ImportBatches.SelectAll`'s `ORDER BY ImportedAt DESC` alone is not a reliable "most recent"
ordering: `ImportedAt` has only whole-second precision, so two batches created within the same
second (routine in fast-successive test/API calls) tie, and SQLite does not guarantee a stable order
for ties — a red test (`ReverseBatchAsync_NotTopOfStack_ThrowsImportBatchStateException`) caught the
older of two same-second batches sometimes being treated as the top of the stack. Fixed by adding
`, ROWID DESC` as a secondary sort key (`Sql.ImportBatches.SelectAll`/`SelectByType`) — SQLite's
implicit rowid increases monotonically with insertion order, giving a deterministic tiebreak.

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

**Status:** ✅ Done — `SqliteImportActionServiceTests` (Add soft-deletes Quote/Source/Character;
Source kept when still referenced by another live batch, mirroring
`ApplyBatchAsync_TwoBatchesReferencingSameNewSource_IdempotentNoDuplicateSourceRow`'s setup; Modify
restores fields, restores linkage even when source/character text changed, restores `ImportBatchId`
to `ExistingBatchId` including the `null`-provenance case, preserves `IsComplete`/`NoValueKnown`;
Skip-policy Modify is a no-op write; `SystemChangeLog` entries written; `ImportBatch` soft-deleted
while its actions stay `Applied`) — 16/16 passing. Full suite green after this step (972 tests, 0
failures).

**Two more genuine bugs were found and fixed while testing, neither assumed correct without a red
test first:**

1. **A Quote's own id is not safely comparable through `IRestorableRepository<QuoteEntity>`'s
   Guid-typed API.** `SoftDeleteAsync`/`HardDeleteAsync` take a `Guid`, and the application's
   globally-registered `GuidHandler` (`Quotinator.Data.Helpers`) always uppercases a `Guid`-typed
   Dapper parameter before comparing — the same convention `EntityIdentity.StableId` (Source/
   Character/Person's id generator) already follows, always emitting uppercase. `QuoteIdentity.StableId`
   (the id generator for a quote with no explicit `"id"` field in its source file, and — per its own
   doc comment — an algorithm that must never change) instead returns `Guid.ToString()`'s **default,
   lowercase** format; an explicit `"id"` field in a source file can be any case at all. `Sql.Quotes.Insert`
   binds `Id` as a plain string with no case normalization, so a Quote's stored `Id` can be
   lowercase — silently matching zero rows against the uppercase-forced comparison, discovered via
   `ReverseBatchAsync_WritesSystemChangeLogEntries` (a Source/Character reference-count check reading
   a Quote that appeared to still be live, because its own soft-delete had silently no-op'd).
   **Fixed by not routing Quote's own soft-delete/hard-delete through the repository object at all**
   — both `ClearStaleAddTargetsAsync` (step 3) and `ReverseQuoteActionAsync`'s Add branch call
   `RepositorySql.SoftDelete("Quotes")`/`HardDelete("Quotes")` directly via Dapper with the id as a
   plain string (`action.EntityId`, untouched) — matching exactly how `Sql.Quotes.SelectRawById`/
   `UpdateOnNewestWins` already compare Quote ids everywhere else in the codebase. Source/Character/
   Person's Add-action ids are always freshly `EntityIdentity`-derived (never a natural-key lookup
   result — a natural-key match means "already exists," which is a Modify, never an Add), so their
   repository-based calls remain safe and unchanged. Required adding `IDbConnectionFactory` to
   `SqliteImportActionService`'s constructor (for `ClearStaleAddTargetsAsync`'s standalone
   pre-transaction connection); `ReverseQuoteActionAsync` reuses the connection/transaction it's
   already given.
2. **A Modify's `ExistingBatchId` can legitimately be `null`** — `QuoteEntity.ImportBatchId`'s own
   doc comment: "Null for records predating provenance tracking." `Guid.Parse(action.ExistingBatchId!)`
   crashed on exactly this case (`ReverseBatchAsync_QuoteModify_PreservesCompletenessFlags`, whose
   `SeedExistingQuoteAsync` fixture never sets `ImportBatchId`). Fixed: `action.ExistingBatchId is
   null ? (Guid?)null : Guid.Parse(action.ExistingBatchId)` — restoring `null` preserves the original
   row's actual provenance state instead of crashing or inventing a value.

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

**Status:** ✅ Done — `ImportActionEndpointsTests` (6 tests: 401 without key, 200 on success,
lowercase `batchId` passed through unmangled, `?preview=true` threaded to the service, 404 for
unknown/already-reversed, 422 for empty/not-applied). `Import` tag, same group as `apply`/`discard`.
404 maps `ImportBatchNotFoundException`; 422 maps `ImportBatchStateException` (covers not-`Applied`,
stack-order conflict, empty batch, and unresolvable linkage — one exception type, several reasons,
matching `discard`'s existing precedent of not surfacing the exception's own message text in the
response).

### 8. `?preview=true`

**Status:** ✅ Done — `ReverseActions_Preview_PassesPreviewTrueAndReturns200`.

**Scope decision, not a silent gap:** the original plan described preview as a rich per-entity
summary (would-soft-delete / kept-because-still-referenced / blocked-by-unresolvable-linkage).
Implemented instead as a validation-only dry-run: `IImportActionService.ReverseBatchAsync(batchId,
preview: true, ...)` runs every blocking check (batch exists, not already reversed, `Applied`, top of
the stack, has actions) and returns success without calling the coordinator's write path — a caller
learns whether the real call would succeed, without the entity-by-entity classification the original
wording implied. That richer classification would require duplicating the reference-count/
natural-key-resolution logic in a second, read-only code path; the validation-only version delivers
the primary practical value (would this be blocked, and why) at a fraction of the implementation and
test cost. Flagged here rather than silently narrowed.

### 9. Auth and rate limiting

**Status:** ✅ Done — same `adminGroup` as `apply`/`discard` (`AdminApiKeyFilter` +
`RateLimitPolicies.Admin`), confirmed by `ReverseActions_NoApiKey_Returns401`.

### 10. i18n

**Status:** ✅ Done — reuses `ApiMessages.ImportBatchNotFound` (404). **A new key was needed after
all, found live during T1:** `ApiMessages.ImportActionBatchInvalidState`'s actual text is
"This batch cannot be **discarded**..." — reusing it for the 422 conditions on `reverse` produced a
factually wrong message (a user reversing a batch, told their batch "cannot be discarded"). Added a
dedicated `ImportActionBatchNotReversible` key, translated in all three `i18ntext/UI.*.json` locales,
covering all four of `reverse`'s actual 422 reasons (not currently applied, not top of the stack, no
actions, unresolvable linkage) — verified live against the running app, not just by reading the
JSON.

### 11. Documentation

**Status:** ✅ Done — `README.md` and `addon/DOCS.md` new endpoint row (kept identical between the
two, per their existing convention); `ImportEndpoints.cs`'s `.WithDescription(...)` on the new route;
`RestApi.razor` new table row wired to a new `ApiRoutes.ImportActionsReverse` constant and
`Text.ImportActionsReverseLabel` (added to all three `i18ntext/UI.*.json` locales — reusing the
existing pattern for `apply`/`discard`'s own rows, `TranslationCompletenessTests` green).

### 12. Smoke-test checklist

**Status:** ✅ Done — added a "Reverse (undo)" curl sequence to `CLAUDE.md`'s Pre-Push Checklist step
6, immediately after the existing Discard sequence: clean apply → `?preview=true` → real reverse →
actions still show `Applied` → reversing again returns `404` → re-importing the same content
resurrects it live → reversing an older batch out of order returns `422` (stack rule). Not yet
executed — that's Verification rows 23–24 (T1/T2), tracked separately.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `SqliteUnitOfWork` wrapping an external connection/transaction never opens, commits, or disposes it — only the owning caller does | Unit test | `SqliteUnitOfWorkTests` (6 tests: exposes connection/transaction, `BeginTransactionAsync`/`CommitAsync`/`RollbackAsync`/`DisposeAsync` all no-op for a wrapped instance, owning-constructor path unaffected) |
| 2 | ✅ | `IRestorableRepository<QuoteEntity/Source/Character/Person>` resolve from DI and work correctly | Unit test | `SqliteRestorableRepositoryTests` (pre-existing, generic); confirmed via steps 5–6's own tests that Source/Character/Person Add-reversal (always `EntityIdentity`-derived, always uppercase ids) works correctly through them — Quote deliberately does **not** use them, see row 15's finding |
| 3 | ✅ | Reference-count check finds zero references for an orphaned Source/Character/Person, nonzero for one still in use | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteAdd_SoftDeletesOrphanedSourceAndCharacter` / `_SourceStillReferencedByAnotherBatch_IsKeptNotSoftDeleted` |
| 4 | ✅ | Quote Add action reversal soft-deletes the Quote row | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteAdd_SoftDeletesQuote` |
| 5 | ✅ | Quote Modify action reversal restores the pre-change field values | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteModify_RestoresExistingFields` |
| 6 | ✅ | Quote Modify reversal restores the correct Source/Character/Person linkage even when the Modify changed the source/character/author text | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteModify_SourceTextChanged_RestoresOriginalLinkage` |
| 7 | ✅ | Quote Modify reversal restores `ImportBatchId` to `ExistingBatchId`, not the reversing batch's own id — including when `ExistingBatchId` is `null` (predates provenance tracking) | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteModify_RestoresExistingBatchId`; the `null` case found and fixed via `_QuoteModify_PreservesCompletenessFlags` (see step 6) |
| 8 | ✅ | Quote Modify reversal preserves `IsComplete`/`NoValueKnown`, never resets them | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteModify_PreservesCompletenessFlags` |
| 9 | ✅ | Skip-policy Modify reversal is a no-op write | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_SkipPolicyModify_NoWriteButReversesCleanly` |
| 10 | ✅ | Source/Character/Person Add reversal soft-deletes only when no active row still references the entity | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteAdd_SoftDeletesOrphanedSourceAndCharacter` / `_SourceStillReferencedByAnotherBatch_IsKeptNotSoftDeleted` (same tests as row 3 — one check, two provable outcomes) |
| 11 | ✅ | Reversal ordering is bottom-up (Quote before Character before Source/Person) | Unit test | Implicitly proven by row 3/10's "kept when still referenced" test — reversing the batch's own Quote first is what makes the reference-count check see the right state; a wrong-order implementation would fail that test |
| 12 | ✅ | Refuses (422) when any action in the batch isn't `Applied` | Unit test | `ImportActionResolutionCoordinatorTests.TryReverseBatchAsync_ActionNotApplied_ReturnsBlockingIdsAndNeverInvokesCallback` (Data-layer coordinator gate) + `SqliteImportActionServiceTests.ReverseBatchAsync_NotApplied_ThrowsImportBatchStateException` (Engine-layer pre-check); the endpoint's own 422 mapping is verified at step 7/row 20 |
| 13 | ✅ | Refuses (422) when the target batch is not the most recently applied still-live batch (strict LIFO stack, not scoped to shared entities); succeeds when it is; an already-reversed later batch never blocks | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_NotTopOfStack_ThrowsImportBatchStateException`, `_TopOfStack_ThenNextOldest_BothSucceedInOrder`, `_AlreadyReversed_ThrowsImportBatchNotFoundException` — found and fixed a real same-second timestamp tiebreak bug, see step 5 |
| 14 | ✅ | Refuses (422) when the natural-key re-resolution finds no match for the original Source/Character/Person | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_QuoteModify_OriginalSourceNoLongerExists_ThrowsImportBatchStateException` — under #59's own invariants (strict LIFO stack + bottom-up ordering) this specific scenario isn't reachable through the ordinary API surface, so the test corrupts the database state directly after staging rather than orchestrating it through the normal flow; proves the defensive check itself, not the (currently unreachable) end-to-end path |
| 15 | ✅ | Re-importing previously-undone content resurrects it (hard-deletes the stale soft-deleted row, inserts fresh) instead of silently no-op'ing, including a quote with genres | Unit test | `SqliteImportActionServiceTests.ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`, `ReverseBatchAsync_ThenReImport_QuoteWithGenres_ResurrectsWithoutForeignKeyViolation` — found and fixed **three** real bugs across implementation and live verification: an FK-ordering bug (step 3), a Quote-id case-sensitivity bug (step 6), and a `QuoteGenres` FK bug found only live in T2 (step 3) |
| 16 | ✅ | Every reversed row's change is logged to `SystemChangeLog` (`SoftDelete` for a reversed Add, `Modified` for a reversed Modify) | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_WritesSystemChangeLogEntries` |
| 17 | ✅ | On success, the `ImportBatch` row is soft-deleted; `SystemImportAction` rows for the batch remain `Applied`, untouched | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_ImportBatchIsSoftDeleted_ActionsRemainApplied` |
| 18 | ✅ | `?preview=true` validates without writing anything (scope-narrowed to blocking-check validation only — see step 8) | Unit test | `ImportActionEndpointsTests.ReverseActions_Preview_PassesPreviewTrueAndReturns200`; `SqliteImportActionServiceTests` (service-level: preview returns before the coordinator is ever called) |
| 19 | ✅ | `POST /import/actions/reverse?batchId=` returns 404 for an unknown or already-reversed batch | Unit test | `ImportActionEndpointsTests.ReverseActions_UnknownOrAlreadyReversedBatchId_Returns404` |
| 20 | ✅ | Returns 422 for an empty batch or one not currently `Applied` | Unit test | `ImportActionEndpointsTests.ReverseActions_EmptyOrNotApplied_Returns422` |
| 21 | ✅ | `batchId` matches case-insensitively | Unit test | `ImportActionEndpointsTests.ReverseActions_LowercaseBatchId_StillMatchesUppercaseStoredValue` (endpoint passes it through unmangled) + `SqliteImportActionServiceTests` (service resolves it via `Guid.TryParse`/`GetByIdAsync`, inherently case-insensitive — no `UPPER()` SQL workaround needed here, unlike `SystemImportAction.BatchId`'s plain-string comparison) |
| 22 | ✅ | `AdminApiKey` auth required; `Admin` rate-limit policy applied | Unit test | `ImportActionEndpointsTests.ReverseActions_NoApiKey_Returns401` |
| 23 | ✅ | T1 — full stage → apply → reverse cycle live; re-import after undo resurrects data, including genres | Live | `dotnet run` against a real local DB: `newest-wins` import → `200`; `?preview=true` reverse → `200` (no write); real reverse → `200`; `GET /quotes/search` confirms the quote gone; reversing again → `404`; re-import same content → `200`, quote reachable again via search; reversing an older batch out of order → `422` with the corrected `ErrorImportActionBatchNotReversible` message (not the discard-specific one); repeated with a genre-bearing quote after the T2-found `QuoteGenres` FK fix, confirmed no errors in server logs |
| 24 | ✅ | T2 — same cycle verified in Docker | Live | `docker build -f docker/Dockerfile` succeeded; fresh container, seeded 788 quotes. Full cycle against a genre-bearing quote: import → `200`, search confirms live → `?preview=true` → `200` → real reverse → `200`, search confirms gone → reversing again → `404` → re-import → **found the `QuoteGenres` FK bug here** (`500`, `SQLite Error 19`, full stack trace in container logs) → fixed (step 3) → rebuilt image → re-ran the entire cycle clean: `200` at every step, quote resurrected with genres intact, search confirms. Stack-order LIFO also verified against two seed batches sharing the exact same `ImportedAt` second (`422` while the newer one was still live, `200` once it became top of stack) — the same same-second tiebreak bug found in unit tests (step 5), independently reconfirmed live. Zero errors in the final container log |
