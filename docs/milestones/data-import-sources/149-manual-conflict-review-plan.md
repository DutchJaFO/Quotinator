# #149 — Manual conflict-review workflow

**Status:** Waiting for release

**Tiers required:** T1, T2

**GitHub issue:** #149

**Depends on:** #56

---

## Scope changes

The original issue text described a single-shot API: `POST .../resolve` accepts a per-field keep/replace
decision and immediately applies it, marking the conflict `resolved`. During planning, the user redirected
this to a **git-merge-style staged workflow** instead, for several concrete reasons:

1. **Side labeling** — the response must clearly show which side is `existing` (already in the
   database) vs `incoming` (from the imported file), including a human-readable label for each side's
   originating import batch, and a `sameFile` flag when both sides trace back to the same imported file
   (a single file can itself contain two entries sharing one quote ID — routes through the exact same
   `seenIds`-based detection as a genuine cross-file duplicate; the only gap was that neither side's
   batch was previously threaded through to the conflict log).
2. **Per-field custom override** — a caller can supply their own value for a field, not just
   keep-existing/take-incoming, validated the same as any normal quote field (e.g. a custom `genres`
   list can be a union, a subset, or unrelated to either side).
3. **The decision itself must be stored**, not just a status flip, so it can be undone before commit.
4. **Nothing executes until every conflict in an import batch has a decision** — mirrors git: resolving
   individual conflicts doesn't auto-commit; a separate `apply` call finalizes the whole batch atomically,
   refusing (with the list of still-pending conflict ids) if any are undecided.
5. **Maximize reuse in `Quotinator.Data`** — the entire staging/undo/readiness-checking workflow
   (`IConflictResolutionCoordinator`) is domain-agnostic and lives in `Quotinator.Data`, so any consumer
   of the library with their own schema can reuse the whole workflow, supplying only the one
   domain-specific piece: how a resolved field map gets written to their own tables.
6. **A related idea — a declarative "conflict resolution file"** for recurring conflicts from
   third-party sources not under our control — was explicitly deferred to its own future issue
   (#153, same milestone), not built here.
7. **Undo is scoped to before-commit only.** Reverting an already-applied batch is a separate, larger
   concern, deferred if ever needed.

A comment documenting this reconciliation was posted on #149 before implementation began.

---

## Design

### 1. Schema — `System_ImportConflicts`

**Status:** ✅ Done

New migration (`ImportConflictMigrations.AddExistingBatchId`, `DataOwnedMigrations` version 7) adds
`ExistingBatchId TEXT` via plain `ALTER TABLE ... ADD COLUMN` — the table was already in its final
`RecordBase` shape (migration 6), so no rebuild was needed. `BatchId`'s existing meaning (the batch
during which a conflict was *detected*, i.e. the incoming side) is unchanged; `ExistingBatchId` is the
batch that originally created the *existing* side. `DataBaselineSql` updated to match, with
`ExistingBatchId` placed at the end of the column list (matching where `ADD COLUMN` places it — caught by
the schema-drift test, which requires baseline and incremental-replay column *order* to match exactly).

New `ImportConflictStatus.Decided` constant — a third state between `Pending` and `Resolved`: a decision
is recorded but the owning batch hasn't been applied yet.

### 2. Threading existing-side provenance into conflict logging

**Status:** ✅ Done

Neither existing conflict-logging call site previously captured which batch created the *existing* side:
- `QuoteSeedWriter.TryGetExistingFieldsAsync` (live-import path) only returned field values. Extended
  `Sql.Quotes.SelectRawById()`'s projection and `RawQuoteRow` to also return `ImportBatchId`; the method
  now returns a new `ExistingQuoteFields(Fields, ImportBatchId)` record.
- The seed path's `seenIds` dictionary only tracked `(FilePath, Quote)`. Extended to
  `(FilePath, Quote, BatchId)` so a later duplicate within the same seeding run knows the first
  occurrence's owning batch.
- `QuoteSeedWriter.LogImportConflictAsync` gained an `existingBatchId` parameter, threaded from both call
  sites into `SystemImportConflict.ExistingBatchId`.

### 3. `FieldMergeResolver.ResolveWithDecisions` (`Quotinator.Data.Import`)

**Status:** ✅ Done

New method alongside the existing policy-based `Resolve()`. Takes an explicit per-field decision map
(`IReadOnlyDictionary<string, FieldMergeDecision>`) — a decision always wins for that field, even if it
wasn't actually ambiguous (git-merge-style manual override). A field with no decision auto-resolves
exactly as `Resolve()` already does (empty-side wins, equal values keep existing). A field that is
genuinely ambiguous (both sides non-empty and differ) with no decision is collected and reported via a
new `UnresolvedFieldConflictException` listing every such field, not just the first one found.

### 4. `IConflictResolutionCoordinator` (`Quotinator.Data.Import`) — the reusable core

**Status:** ✅ Done

Generic orchestration requiring no domain schema knowledge:
- `DecideAsync(id, decisionsJson, ...)` — stages a decision (`Pending`→`Decided`), storing the caller's
  already-serialized decision JSON in `MergedFields`. Never touches any domain table.
- `UndoDecisionAsync(id, ...)` — reverts `Decided`→`Pending`, clears the stored decision.
- `TryApplyBatchAsync(batchId, applyResolvedConflict, ct)` — if any conflict sharing `batchId` is still
  `Pending`, returns their ids and applies nothing (git's "unmerged paths" refusal). Otherwise, in one
  transaction, invokes the caller-supplied `applyResolvedConflict` callback once per `Decided` conflict,
  then marks each `Resolved`. Commits once, for the whole batch.

Backed by new `ISystemImportConflictReader.GetByIdAsync`/`GetAllForBatchAsync` and
`ISystemImportConflictWriter.MarkDecidedAsync`/`ClearDecisionAsync`/`MarkResolvedAsync` — new
`Sql.SystemImportConflicts` factory/const SQL for each.

**Incidental fix:** `DuplicateResolutionPolicy`'s Dapper enum handler was only ever registered via
`QuotinatorDapperConfiguration.RegisterDomainHandlers()` (Engine-only), even though the enum itself lives
in `Quotinator.Data.Import` — meaning `Quotinator.Data.Tests` (which only calls the base `Configure()`)
could never write a `SystemImportConflict` row at all. Moved the registration to the base
`DatabaseConfiguration.Configure()`, matching `ChangeAction`/`InitiatorType`'s placement exactly.

### 5. `IConflictResolutionService` (`Quotinator.Engine.Services`) — the one domain-specific piece

**Status:** ✅ Done

Thin wrapper over the coordinator, mirroring `IQuoteImportService`'s placement rationale (needs both Core
DTOs and Data types). `GetPagedAsync` attaches human-readable batch labels (`ImportBatches.Name`) and
computes `AmbiguousFields`/`SameFile` for the response. `DecideAsync` validates immediately (calls
`ResolveWithDecisions` purely to catch `UnresolvedFieldConflictException` before anything is staged).
`ApplyBatchAsync` supplies the coordinator's one required callback: rebuild the resolved field values,
then run the exact same sequence already used for merge-policy duplicates during seeding/import
(`GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync`,
`Sql.Quotes.UpdateOnNewestWins`, regenerate `QuoteGenres`, `LogChangeAsync` with `ChangeAction.Modified`,
`InitiatorType.WriteEndpoint` — its first real consumer). Quote translations are deliberately left
untouched during apply — they were already excluded from the mergeable field set before #149 existed (a
distinct, manually-curated concern), and the original incoming file's translation data isn't available
any more by the time a batch is applied.

Request-side DTOs (`ConflictDecisionRequest`, `FieldDecision`, `GenresFieldDecision`) live in
`Quotinator.Engine.Models`, not `Quotinator.Core.Models` — they need `FieldResolutionChoice` (a
`Quotinator.Data` enum), and `Quotinator.Core` has zero project references, so it can never reference a
Data type (verified directly against `Quotinator.Core.csproj`). Response DTOs
(`ConflictSummaryResponse`, `QuoteConflictFieldsDto`, `ConflictPageResponse`,
`ConflictBatchStatusResponse`) have no Data-typed properties, so they fit `Quotinator.Core.Models`
normally, matching `ImportResultResponse`'s placement.

### 6. Endpoints — `src/Quotinator.Api/Endpoints/ImportEndpoints.cs`

**Status:** ✅ Done

New top-level route group `/api/v1/import` (confirmed: deliberately not nested under `/api/v1/quotes`,
where the sibling import/preview endpoints actually live, and not under `/api/v1/admin`):
- `GET /conflicts?status=&batchId=&page=&pageSize=` — public (read-only, no key — matches
  `GET /admin/audit`'s precedent).
- `POST /conflicts/{id}/decide` — requires `X-Api-Key`.
- `POST /conflicts/{id}/undo` — requires `X-Api-Key`.
- `POST /conflicts/apply?batchId=` — requires `X-Api-Key`; `batchId` as a query param (bulk action over
  the `/conflicts` resource, not a distinct `/batches` resource).

New `ApiRoutes` constants, new `ApiTags.Import` tag, new `ApiMessages` keys (`ConflictNotFound`,
`ConflictAlreadyResolved`, `ConflictNotDecided`, `ConflictAmbiguousFieldsUnresolved`,
`ConflictBatchNotFullyDecided`) translated in all three `i18ntext/UI.*.json` files.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ResolveWithDecisions`: explicit decision always wins (Keep/Replace/Custom), including for unambiguous fields | Unit test | `FieldMergeResolverTests.ResolveWithDecisions_KeepDecision_AlwaysKeepsExistingEvenWhenUnambiguous`, `...ReplaceDecision_AlwaysTakesIncomingEvenForAmbiguousField`, `...CustomDecision_UsesCallerSuppliedValueOverridingBothSides` |
| 2 | ✅ | Undecided field auto-resolves when unambiguous; throws with every ambiguous field name when not | Unit test | `FieldMergeResolverTests.ResolveWithDecisions_UnambiguousFieldNoDecision_AutoResolvesEmptySideWins`, `...EqualValuesKeepExisting`, `...AmbiguousFieldNoDecision_ThrowsWithFieldName`, `...AmbiguousFieldsNoDecision_ThrowsWithEveryAmbiguousFieldName` |
| 3 | ✅ | Writer/reader status transitions round-trip correctly (Decided/Pending/Resolved, MergedFields storage) | Unit test | `SystemImportConflictWriterReaderTests.MarkDecidedAsync_TransitionsToDecidedAndStoresDecisionJson`, `...CalledAgain_OverwritesPriorDecision`, `ClearDecisionAsync_RevertsToPendingAndClearsMergedFields`, `MarkResolvedAsync_SetsResolvedStatusAndResolvedAt`, `ExistingBatchId_RoundTripsCorrectly` |
| 4 | ✅ | Coordinator: decide stages without touching any domain table; not-found/already-resolved/not-decided all throw the correct exception | Unit test | `ConflictResolutionCoordinatorTests.DecideAsync_UnknownId_ThrowsConflictNotFoundException`, `...PendingConflict_StagesDecisionAndNeverInvokesApplyCallback`, `...AlreadyResolvedConflict_ThrowsConflictStateException`, `UndoDecisionAsync_DecidedConflict_RevertsToPending`, `...StillPendingConflict_ThrowsConflictStateException` |
| 5 | ✅ | Coordinator: batch apply refuses (and invokes nothing) while any conflict is pending; applies atomically once all decided; rolls back and leaves state unchanged on callback failure | Unit test | `ConflictResolutionCoordinatorTests.TryApplyBatchAsync_SomeConflictsStillPending_ReturnsPendingIdsAndNeverInvokesCallback`, `...EveryConflictDecided_InvokesCallbackOncePerConflictAndMarksAllResolved`, `...CallbackThrows_RollsBackAndLeavesConflictsDecided`, `...NoConflictsForBatch_ReturnsNullWithoutInvokingCallback` — all exercised against a fake in-memory callback, proving the coordinator needs no real Quote/Source/Character schema |
| 6 | ✅ | Data-owned baseline and incremental migration replay produce identical `System_ImportConflicts` schema, including the new `ExistingBatchId` column | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema` |
| 7 | ✅ | Service layer: only genuinely ambiguous fields are reported; same-file conflicts detected correctly | Unit test | `SqliteConflictResolutionServiceTests.GetPagedAsync_PendingConflict_ReportsOnlyGenuinelyAmbiguousFields` |
| 8 | ✅ | Service layer: decide with an ambiguous field left undecided throws before staging | Unit test | `SqliteConflictResolutionServiceTests.DecideAsync_AmbiguousFieldLeftUndecided_ThrowsUnresolvedFieldConflictException` |
| 9 | ✅ | Service layer: decide + apply correctly resolves FK fields (Source/Character/Person), writes exactly one `Modified` `System_ChangeLog` row with `InitiatedByType=WriteEndpoint`, marks the conflict `Resolved` | Unit test | `SqliteConflictResolutionServiceTests.DecideAsync_ThenApplyBatch_WritesResolvedFieldsAndOneChangeLogRow` |
| 10 | ✅ | Undo before commit reverts the decision and the batch stays fully unapplied | Unit test | `SqliteConflictResolutionServiceTests.UndoDecisionAsync_BeforeApply_RevertsDecisionAndBatchStaysUnapplied` |
| 11 | ✅ | All four endpoints: correct auth requirements (`GET` public, all writes require `X-Api-Key`), correct status codes (200/204/401/404/422), 422 body carries field names / pending conflict ids | Unit test | `ImportConflictEndpointsTests` — all 13 test methods |
| 12 | ✅ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 warnings/errors; `dotnet test --configuration Release` → 898/898 passed |
| 13 | ✅ | T1 — app starts in VS without error; migration applies cleanly | Live | Started in Visual Studio against the real dev database: log shows `applying 1 pending Data migration(s) (version 6 → 7)...` → `schema updated (data v7, app v6)`, source refresh succeeded, `home page ready`, and a live `GET /api/v1/import/conflicts` request returned `200` — clean startup, no errors |
| 14 | ✅ | T2 — Docker smoke test, full decide→undo→decide→apply flow against a real conflict | Live | `docker build` succeeded; migration `Data v5 → v7` applied against a copy of the real dev database; a genuine pending conflict produced via `POST /quotes/import` with a per-request `duplicateResolution: review` override; `GET /import/conflicts` correctly showed `batchLabel`/`existingBatchLabel`/`sameFile`/`ambiguousFields: ["quoteText"]`; `decide` → `GET ?status=decided` (ambiguousFields recomputed to `[]`) → `undo` → `GET ?status=pending` (ambiguousFields back to `["quoteText"]`) → `decide` again with a `Custom` genres decision (`["drama","romance"]`, matching neither original side) → `apply` returned `200`; `GET /quotes/{id}` confirmed the quote's text and genres were correctly rewritten; conflict status was `resolved` with `resolvedAt` set; container logs showed zero errors throughout |

---

## Not in scope for this issue (deferred)

- Undo *after* a batch has been applied (reverting already-written domain data) — confirmed deferred to
  its own future issue if ever needed.
- A declarative "conflict resolution file" for recurring conflicts from third-party sources — filed as
  its own separate issue, [#153](https://github.com/DutchJaFO/Quotinator/issues/153).
- Blazor conflict-review UI — the eventual consumer (milestone #11), not this issue (API-only, matching
  #45's own precedent).
- `EntityType` values other than `"Quote"` — no other entity type generates conflicts today.
