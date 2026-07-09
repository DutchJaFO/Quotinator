# #154 — Unify import, preview, and seeding on one staging engine

**Status:** ✅ Implementation complete (Phase A + Phase B), T1 ✅ T2 ✅ — awaiting explicit developer confirmation to close
**GitHub issue:** #154
**Tiers required:** T1, T2
**Depends on:** #149 (`FieldMergeResolver`, `UnresolvedFieldConflictException` — reused, not
`System_ImportConflicts` itself, which this issue retired in Phase B), #56 (audit/change log)

---

## Scope history (recorded before implementation)

This issue did not start as its own filed idea — it emerged while planning #59 ("Admin: targeted
soft-reset and restore by import batch"). Designing #59's "modified after import" preview detail
led to a better foundation: instead of an approximate timestamp heuristic, extend #149's
conflict-logging mechanism to log **every** action an import takes, not just genuine duplicate
conflicts — which is also exactly what a real undo (#59) needs.

That grew, through several rounds of user direction during planning, into unifying how imports,
preview, and seeding all work. Since it materially exceeds #59's filed scope, it was split out as
its own issue (this one) rather than silently expanding #59. **#59 is now downstream of this
issue** — see its own plan doc's updated "Depends on" line.

**Revision (this pass):** the original design below (kept for its still-accurate framing) assumed
`System_ImportConflicts` (#149) would keep running *alongside* the new `System_ImportActions`
table, "untouched, separate." Reviewing the plan against the actual code and against #154 itself
surfaced that this was never reconciled — an ambiguous Modify would produce a row in **both**
tables, decided through two different endpoints with the same request shape. Corrected:
**`System_ImportActions` becomes the sole mechanism.** `System_ImportConflicts`, its coordinator,
its service, and its `/import/conflicts/*` endpoints are retired once the new table reaches feature
parity — not kept in parallel. Since nothing in this milestone has shipped in a published release
yet, this is safe. `FieldMergeResolver`/`UnresolvedFieldConflictException` (domain-agnostic) are
kept and reused; the Conflict-specific entity/coordinator/service/endpoints are not.

A second change this revision: instead of "look up Source/Character/Person, defer the actual
`GetOrCreate`+random-GUID-insert to apply time" (the plan's original open inference below), give
Source/Character/Person their own stable, deterministic ids — mirroring
`Quotinator.Core.Import.QuoteIdentity.StableId`, which Quotes already have. A stable id lets the
planner determine identity via a pure read-only lookup, with no special deferred-linking mechanism
needed, and apply-time writes become simple idempotent inserts. This resolves what was previously
listed as an unconfirmed inference — see "Key decisions" item 6 below, now confirmed via this
replacement design instead.

Key decisions made during original planning (not re-litigated here, just recorded):

1. `preview` = stage (compute and durably record exactly what an import would do; write nothing to
   domain tables). `import` = execute a staged batch (apply it). A plain `POST /import` call still
   works as one convenient round-trip.
2. `POST /import` returns a distinct HTTP status per outcome: `200 OK` when everything applied,
   `202 Accepted` when the batch is left `Staged` awaiting a decision — callers branch on status
   code alone, no body parsing required. **`POST /import/preview` gets the same split** (revision —
   see Design Section 3): nothing ever applies from `/preview`, but `202` tells the caller up front
   that the file has unresolved conflicts it must adjust or resolve before the batch can be applied.
3. `POST /import/preview`'s contract changes from "nothing persisted, no `ImportBatch` created" to
   "stages a real, inspectable batch." Acceptable pre-release (same precedent #152 established for
   its own route move — nothing in this milestone has shipped yet).
4. Seeding uses the identical mechanism and is explicitly allowed to leave a source file's batch
   `Staged`/unapplied if it has unresolved ambiguity — no forced auto-resolution policy override.
   Startup is not blocked; it logs which file(s) ended up staged and continues normally. This is
   the intended fine-tuning loop (edit the file, change its manifest `duplicateResolution`
   override, or later supply a #153 decisions file, then re-stage/apply).
5. The mechanism must be a genuinely reusable, domain-agnostic primitive, maximized in
   `Quotinator.Data`.
6. ~~Source/Character/Person creation is deferred from staging time to apply time~~ — **superseded**
   by the stable-id design (this revision's Design Section 1): identity is now determined via a
   deterministic id, not via ordering of when a row happens to get created.

---

## Design

### 1. Stable ids for Source/Character/Person (new, this revision)

**Status:** ✅ Done — `EntityIdentity` built, `EntityIdentityTests` passing

New `src/Quotinator.Core/Import/EntityIdentity.cs`, sibling to `QuoteIdentity.cs`, same namespace.
**Does not modify `QuoteIdentity.cs`** — its own doc comment says its algorithm must never change.
Reimplements the same SHA-256 → forced-UUID-v4-bits → `Guid` mechanics, reusing
`QuoteIdentity.Normalise` per component, with a type-tag prefix so the three id spaces can never
collide with each other or with a `QuoteIdentity.StableId` value:

```csharp
public static class EntityIdentity
{
    public static string SourceId(string title, string type) => StableId("source", title, type);
    public static string CharacterId(string sourceId, string name) => StableId("character", sourceId, name);
    public static string PersonId(string name) => StableId("person", name);
}
```

**Critical nuance**: existence-checking stays a **natural-key DB lookup**
(`Sql.Sources.SelectIdByTitleAndType` etc., unchanged) — not "does a row exist whose Id equals the
computed stable id." Every pre-existing Source/Character/Person row has a random `Guid.NewGuid()`
Id, so a stable-id-based existence check would wrongly classify them all as new. The stable id is
only ever the id assigned **when inserting a genuinely new row**.

### 2. Generic staging primitive (`Quotinator.Data`)

**Status:** ✅ Done

New `SystemImportAction` entity (`Quotinator.Data.Entities`), RecordBase-shaped from creation:
`Id, BatchId` (loose string reference, no FK — mirrors `SystemImportConflict.BatchId`),
`ActionType` (free-text — `"Add"`/`"Modify"` today, room for a future `"Remove"` with zero
migration), `EntityType` (free-text, caller-owned meaning), `EntityId`, `ExistingBatchId` (null for
Add), `ExistingValue`/`IncomingValue`/`MergedFields` (opaque JSON, never deserialized in Data),
`AppliedPolicy`, `Status`, `DetectedAt`, `AppliedAt`, `DiscardedAt`.

Status lifecycle: `Pending` (needs an explicit decision) → `Decided` → `Applied`/`Discarded`.
Apply-readiness rule (generic, Data-owned, needs no domain knowledge): refuse if anything sharing
the batch is still `Pending`.

New `IImportActionCoordinator`/`ImportActionResolutionCoordinator` (`Quotinator.Data.Import`):
`StageAsync` (writes a batch of already-classified actions — classification is the caller's job,
not Data's), `DecideAsync`/`UndoDecisionAsync`, `TryApplyBatchAsync(batchId, applyResolvedAction,
ct)` (same refusal contract, caller-supplied per-action callback), and `DiscardBatchAsync(batchId,
ct)`.

A consuming project's plug-in surface stays deliberately small: (a) a *classifier* — domain schema
knowledge lives here only — returning Add / unambiguous-Modify / ambiguous-Modify-needs-a-decision
for one incoming row; (b) an *applier callback* — takes a `Decided` action, writes it to the
consumer's own tables. Everything else (table, status machine, decide/undo/apply/discard
orchestration) is reusable as-is.

**Post-build correction (same milestone, after ADR 008 was written):** `ActionType` and `Status`
were initially built as open string-constant classes, reasoning they were domain-agnostic like
`EntityType`/`BatchId`. That conflated two different kinds of column — these two are a closed set
`Quotinator.Data`'s own coordinator assigns and transitions between, not consumer-defined
vocabulary. Converted to real C# `enum`s, typed as `SafeValue<TEnum?>` with a registered
`SafeEnumHandler<TEnum>` and a matching SQL `CHECK` constraint per ADR 008.

### 3. Planner + applier (`Quotinator.Engine`)

**Status:** ✅ Done — `ImportActionPlanner`, `IImportActionService`/`SqliteImportActionService`, and
the `SqliteQuoteImportService` thin-orchestrator rewiring (including `Program.cs` DI) are all built
and tested. Note: this section's classifier/applier work is done, but the actual seeding call site
(Section 5) has not been rewired to use them yet.

**Planner**: new `internal static Quotinator.Engine.Database.ImportActionPlanner.PlanAsync`
— a side-effect-free classifier, callable identically from `/import/preview`, `/import`, and
seeding. Per quote row: resolve Source/Character/Person (0–3 Add actions using `EntityIdentity`,
always `Status=Decided` immediately — Add is never ambiguous; `GetOrCreateSourceAsync`/etc. never
*update* an existing row, so these three entity types only ever need an Add action) → look up
existing Quote by Id (`QuoteSeedWriter.TryGetExistingFieldsAsync`, unchanged, including same-batch
first-wins shadowing) → stage a Quote action: no existing row → `ActionType=Add`, `Status=Decided`;
existing row → same policy-to-status mapping `LogImportConflictAsync` already used
(`Status=Pending` iff `policy==Review`, else `Decided`), with the final resolved field values
**computed now** for every non-Review policy and stored as the action's payload — apply never
needs policy logic. New envelope types: `QuoteActionPayload` (fields + resolved
`SourceId`/`CharacterId`/`PersonId`), `SourceActionPayload`/`CharacterActionPayload`/
`PersonActionPayload`. Carrying the FK ids inside the Quote's own payload means the applier never
depends on Source/Character/Person actions having run first.

**Applier**: new `Quotinator.Engine.Services.IImportActionService`/`SqliteImportActionService`,
replacing `IConflictResolutionService`/`SqliteConflictResolutionService`. `ApplyResolvedActionAsync`
dispatches on `EntityType`: `Source`/`Character`/`Person` → idempotent insert using the precomputed
stable id (safe even under concurrently-staged batches referencing the same new entity —
`SystemChangeLog` Created logged only if the insert actually happened); `Quote` → deserialize the
payload (from `IncomingValue` for Add, resolved `MergedFields` for Modify) and write — apply is now
**uniform and policy-agnostic**, no `FieldMergeResolver` call at apply time.

`DecideAsync(actionId, ConflictDecisionRequest)`: rejects (422, new
`ImportActionNotDecidableException`) if `EntityType != "Quote"` or the action isn't `Pending`.
Otherwise builds the decision map and calls `FieldMergeResolver.ResolveWithDecisions` immediately
(fail at decide time, not apply time), persisting the **resolved** values into `MergedFields`.

`ImportBatch` (Engine entity) gains `Status` (`Staged`/`Applied`/`Discarded`) and `AppliedAt`
(nullable, distinct from `ImportedAt`) — **done**, migration007, with a `CHECK` constraint per
ADR 008. `SqliteQuoteImportService.ImportAsync` becomes a thin orchestrator: create `ImportBatch`
(`Status=Staged`) → `PlanAsync` → `StageAsync` (commit) → unless preview, attempt apply via
`SqliteImportActionService` (commit — **two sequential commits, not one shared transaction**; a
crash between them leaves the batch `Staged`, already a safe/recoverable state by this design's own
rules) → build `ImportResultResponse` from the resulting action rows — **done**. Response shaping
(`BuildConflictEntries`) still reuses the old `ImportConflictEntry` shape as a temporary bridge;
Section 4/Task 33 replaces it with the real `/import/actions` response shape.

### 4. Endpoints (`ImportEndpoints.cs`, `Import` tag)

**Status:** ✅ Done — including the `batchId`-mode alias on `POST /import` (see below); this was
initially built as file-mode-only, but the user rejected leaving the alias out on the grounds that
it's a key feature of the staging→execution design, not an optional convenience — it was built in
the same session as a follow-up, not deferred.

- `POST /api/v1/import/preview` — stage only, commit. **Same `200`/`202` split as `/import`**:
  `200 OK` when nothing in the staged batch is `Pending` (the file would apply cleanly as-is);
  `202 Accepted` with `batchId` + which actions need a decision when anything is `Pending`. Never
  applies either way. — **done**, verified live (curl smoke test against a real dev server: import
  under `review` policy returned `202`, `GET /import/actions?status=Pending` showed the staged
  rows, decide+apply round-tripped to `200`, status flipped to `Applied`).
- `POST /api/v1/import` — two modes on one route, distinguished by whether `batchId` is present:
  **file mode** (`file` required, `batchId` omitted) stages the file, then immediately attempts to
  apply it (two sequential commits — a crash between them leaves the batch `Staged`, a safe,
  recoverable state); **batch mode** (`batchId` given, `file`/`settings` ignored, `IFormFile? file`
  now nullable) applies a batch already staged by a prior `/import` or `/import/preview` call —
  a thin alias for `POST /import/actions/apply` (`IQuoteImportService.ApplyStagedBatchAsync`) that
  returns the same `ImportResultResponse` envelope shape as file mode, for one consistent response
  contract regardless of which mode was used. `404` (`ImportBatchNotFoundException`) if `batchId`
  doesn't parse or doesn't exist. Either mode: `200 OK` when everything applied; `202 Accepted`
  (body carries `batchId` + which actions need a decision) otherwise. — **done**, full test coverage
  in `ImportEndpointTests`/`QuoteImportServiceTests` (all-modes: 200/401/404/202), full suite green.
- `GET /api/v1/import/actions` — **the conflict-review endpoint** for staged batches; paginated,
  filter by `batchId`/`status`/`entityType`, all three matched case-insensitively (`?status=pending`
  matches a stored `Pending` value — a caller's casing is never guaranteed, and there's no reason to
  want case-sensitive matching on a GUID or a closed-set string like a status/entity-type name; see
  row 19 below). Polymorphic `ImportActionSummaryResponse` (loosely-typed
  `ExistingFields`/`IncomingFields`, not per-entity-type DTOs) — **done**, with:
  - `RelatedActionIds` — since a Quote action's payload references other staged actions in the same
    batch (its Source/Character/Person), the response must expose that relationship so a caller/UI
    can show "this quote also needs to create Source 'X'."
  - `AmbiguousFields` — computed per `Pending` Quote action the same way #149's
    `ConflictSummaryResponse.AmbiguousFields` was (`FieldMergeResolver.ResolveWithDecisions` with an
    empty decision map, catch `UnresolvedFieldConflictException`, its `FieldNames` is the list) —
    without this a caller can see raw JSON but not *which* fields actually need a decision.
  - **Deliberately not built here**: a bulk "apply one policy to every `Pending` action in a batch"
    endpoint. Deferred to #153 (declarative conflict-resolution file), which already owns that scope
    ("pre-fill a staged batch's ambiguous fields in bulk, using the same decide mechanism a human
    uses one row at a time"). #154 only exposes the one-at-a-time decide primitive for #153 to
    drive. Manual one-by-one review (this issue) and bulk-strategy application (#153) are two
    different issues by design.
- `POST /api/v1/import/actions/{id}/decide` / `.../undo` — reuses `ConflictDecisionRequest`/
  `FieldDecision`/`GenresFieldDecision` as-is — **done**.
- `POST /api/v1/import/actions/apply?batchId=` — 422 if anything sharing the batch is still
  `Pending`; otherwise commits everything — **done**.
- `POST /api/v1/import/actions/discard?batchId=` — marks everything `Discarded`; never touches
  domain tables. 422 if already applied/discarded — **done**.
- `/api/v1/import/conflicts/*` (#149) — stays live **only during Phase A** (below); removed in
  Phase B once `/import/actions/*` reaches parity — **unchanged, still live**; note CLAUDE.md's
  pre-push curl workflow was updated to exercise `/import/actions/*` as the primary path, since no
  live import/seed path writes to `System_ImportConflicts` any more (Section 5 above superseded it).

### 5. Seeding integration

**Status:** ✅ Done — `QuotinatorDatabaseInitializer` rewired to the shared planner/applier per file;
`Program.cs` DI updated; full test suite green (953 tests, 0 failures). Found and fixed a real
pre-existing bug along the way: `SqliteImportActionService`'s Skip short-circuit was skipping every
`Add` action too (not just genuine duplicate `Modify` conflicts) whenever a file's effective policy
was `Skip`, silently dropping brand-new quotes. Also fixed: `SqliteConflictResolutionServiceTests`
and 4 tests in `ConflictResolutionTests` that constructed a pending `System_ImportConflicts` row via
seeding — seeding no longer writes there, so `ConflictResolutionTests`' 4 tests were removed (with an
explanatory note) and `SqliteConflictResolutionServiceTests`' fixture now manufactures its pending
conflict row directly instead.

Per source file: stage via the shared planner, then attempt apply via the shared applier — same
per-file `ImportBatch` granularity as today. No policy override, no forced auto-resolution. A file
left with anything `Pending` simply stays `Staged` — its records don't appear in the live dataset
yet. Startup logs a clear, itemised message for every file left staged and continues normally
otherwise.

### 6. `ImportBatch.Type`/`.Status` enum fix (new, this revision)

**Status:** ✅ Done

Converted to `SafeValue<ImportBatchType?>`/`SafeValue<ImportBatchStatus?>`, registered
`RegisterEnumHandler<ImportBatchType>()`/`<ImportBatchStatus>()` in
`QuotinatorDapperConfiguration.RegisterDomainHandlers()`, updated all 5 call sites (2 constructions
+ 2 mutations in `QuotinatorDatabaseInitializer`/`SqliteQuoteImportService`, 1 test fixture
construction). **No new migration needed** — unlike `SystemImportConflict`/`SystemImportAction`,
`ImportBatches.Type` and `.Status` already had their CHECK constraints from earlier migrations
(migration004's widened `Type` CHECK, migration007's `Status` CHECK) — this was a pure C#-side fix.
Full suite green (953 tests) after the change, no regressions.

### 7. Retiring #149 — Phase A (build to parity) then Phase B (delete), not simultaneous

**Status:** ✅ Done

**Phase A** — Sections 1–6 above, plus new `/import/actions/*` endpoints additive alongside
`/conflicts/*`, plus the parity-proving test suite (Section 8) — done, T1/T2 confirmed.

**Phase B** — one focused commit: deleted `SystemImportConflict` entity,
`ConflictResolutionCoordinator`/`IConflictResolutionCoordinator`, `ConflictNotFoundException`/
`ConflictStateException` (kept `FieldMergeResolver`/`UnresolvedFieldConflictException` — reused by
`DecideAsync`), `ISystemImportConflictReader`/`Writer` + implementations,
`SystemImportConflictPageResult`, `SqliteConflictResolutionService`/`IConflictResolutionService`,
`ConflictSummaryResponse`/`ConflictPageResponse`/`ConflictBatchStatusResponse`, the `/conflicts/*`
route registrations, the `NoOpSystemImportConflictWriter` test double (already fully orphaned), and
their test files (`ImportConflictEndpointsTests`, `FakeConflictResolutionService`,
`ConflictResolutionCoordinatorTests`, `SystemImportConflictWriterReaderTests`,
`SqliteConflictResolutionServiceTests`) — 20 files deleted in total. Also removed now-dead
supporting code: `QuoteSeedWriter.LogImportConflictAsync` (its only remaining caller was the
deleted `SqliteConflictResolutionServiceTests`), the `ImportConflictStatus` enum (defined on the
deleted entity, referenced nowhere else), the `ApiRoutes.ImportConflicts*`/`ApiMessages.Conflict*`
constants, the `/conflicts/*` i18n keys (all three locales), and the `/conflicts/*` rows in
`README.md`/`addon/DOCS.md`/`RestApi.razor`. Kept `ConflictDecisionRequest`/`FieldDecision`/
`GenresFieldDecision` — absorbed, not deleted. Kept the `System_ImportConflicts` migration/baseline
entries and its schema-drift test untouched — squashing them is **#155's call**, not this issue's
(see "Not in scope" below); the table now simply has zero C# code reading or writing it.

### 8. Tests

**Status:** ✅ Done — every test class the plan called for exists and passes; full suite is 976
tests, 0 failures, 0 warnings.

- `Quotinator.Data.Tests`: `SystemImportActionWriterReaderTests`, `ImportActionResolutionCoordinatorTests`
  — against a fake classifier/applier callback, proving the coordinator needs no real Quote/Source
  schema. — **done**.
- `EntityIdentityTests` (Core.Tests) — determinism, normalization, no collision across the three id
  spaces or with `QuoteIdentity.StableId`. — **done**, 8 tests passing.
- `ImportActionPlannerTests` (Engine.Tests) — Add vs Modify vs Pending-Modify classification for
  Quotes; Add-only for Source/Character/Person; same-batch dedup; never writes to any domain table;
  stable-id reuse is idempotent across repeated runs. — **done**, 8 tests passing.
- `SqliteImportActionServiceTests` (Engine.Tests) — decide validates via `FieldMergeResolver`,
  rejects non-Quote/non-Pending decide targets, apply writes correctly and idempotently, discard
  leaves zero domain rows, `GetPagedAsync` correctly computes `RelatedActionIds`/`AmbiguousFields`
  and honours the `entityType` filter. — **done**, 12 tests passing (4 new this task).
- Regression proof: existing `QuoteImportServiceTests` — **done**, with a caveat: two tests
  (`ImportAsync_Preview_*`) were rewritten rather than left byte-for-byte unmodified, because
  `/preview`'s contract itself intentionally changed this revision (Key decision 3 — preview now
  stages a real, inspectable batch instead of persisting nothing). All 21 tests in the file pass;
  a code comment on the two rewritten tests explains why. The seeding test suite's own regression
  pass — **done** in Task 31 (`DatabaseInitializerTests`, `ImportBatchesTests`,
  `SourceCacheWiringTests`, `ConflictResolutionTests` all pass against the rewired pipeline).
- `ImportActionEndpointsTests` (Api.Tests, new) — mirrors `ImportConflictEndpointsTests`'s shape;
  16 tests covering auth, decide/undo/apply/discard success and every failure mode
  (not-found/not-decidable/ambiguous/already-resolved/not-decided/invalid-state). — **done**.
  Updated `ImportEndpointTests` for the `200`/`202`-split contract (`Import_ResultHasPendingConflict_Returns202`,
  parameterised over both `/import` and `/import/preview`) — **done**.
- Deleted in Phase B: `SystemImportConflictWriterReaderTests`, `ConflictResolutionCoordinatorTests`,
  `SqliteConflictResolutionServiceTests`, `ImportConflictEndpointsTests`, `FakeConflictResolutionService`.

### 9. DTOs / i18n / documentation

**Status:** ✅ Done

`Quotinator.Core.Models`: `ImportActionSummaryResponse`, `ImportActionPageResponse` (mirroring
`ConflictSummaryResponse`/`ConflictPageResponse`; `ImportActionBatchStatusResponse` already existed
from Section 2). Seven new `ApiMessages` keys (`ImportActionNotFound`/`AlreadyResolved`/`NotDecided`/
`NotDecidable`/`AmbiguousFieldsUnresolved`/`BatchNotFullyDecided`/`BatchInvalidState`), each with a
translated entry in all three `i18ntext/UI.*.json` files, plus five new `RestApi.razor` table-row
labels per locale. `README.md`/`addon/DOCS.md` — new `/import/actions/*` rows inserted, `/import`
and `/import/preview`'s existing rows corrected to describe the `200`/`202` split and the
stage-then-apply/stage-only contract accurately (the old text was already stale — it described
preview as "rolls back every write," which stopped being true once preview started staging a real
batch). `RestApi.razor` — five new endpoint rows, verified rendering live (see Verification).
`CLAUDE.md` — the pre-push checklist's manual conflict-review curl workflow was rewritten around
`/import/actions/*` as the primary, live path (verified against a running dev server — see
Verification row 16), with a note that `/import/conflicts/*` is no longer populated by any live
import/seed path and is exercised separately during Phase A.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Coordinator: stage/decide/undo/apply/discard state transitions correct against a fake classifier/applier (no real schema needed) | Unit test | `ImportActionResolutionCoordinatorTests` |
| 2 | ✅ | Reader/writer round-trip for `System_ImportActions`, including all Status transitions | Unit test | `SystemImportActionWriterReaderTests` |
| 3 | ✅ | `EntityIdentity` produces deterministic, non-colliding ids for Source/Character/Person | Unit test | `EntityIdentityTests` |
| 4 | ✅ | Planner correctly classifies Add / unambiguous-Modify / ambiguous-Modify for Quotes, Add-only for Source/Character/Person; never writes to any domain table | Unit test | `ImportActionPlannerTests` |
| 5 | ✅ | Applier writes Source/Character/Person (idempotently, using the stable id) and the Quote only at apply time; identical result whether reached via `/import`, `/import/actions/apply`, or seeding | Unit test + Live | `SqliteImportActionServiceTests`; seeding wired (Section 5); live curl smoke test confirmed the `/import` path end-to-end |
| 6 | ✅ | `DecideAsync` rejects non-Quote/non-Pending targets; validates via `FieldMergeResolver` at decide time | Unit test | `SqliteImportActionServiceTests` |
| 7 | 🟡 | Existing import behavior preserved by the planner/applier extraction, with preview's contract intentionally changed (Key decision 3) | Unit test | `QuoteImportServiceTests` — 21/21 pass; 2 tests rewritten for the new preview contract, not left unmodified |
| 8 | ✅ | Existing seeding behavior unchanged where nothing is ambiguous | Unit test | `DatabaseInitializerTests`, `ImportBatchesTests`, `SourceCacheWiringTests`, `ConflictResolutionTests` all pass against the rewired seeding pipeline (some seeding-integration tests were updated, not left byte-for-byte unmodified, since `System_ImportConflicts` is no longer seeding's mechanism — see Section 5) |
| 9 | ✅ | `POST /import` returns `200` when everything applies, `202` with a usable `batchId` when something needs review | Unit test + Live | `ImportEndpointTests.Import_ResultHasPendingConflict_Returns202`; live curl confirmed `202` for a genuine review-policy duplicate, `200` after decide+apply |
| 10 | ✅ | `POST /import/preview` stages only, never applies, and returns the **same `200`/`202` split** based on unresolved ambiguity | Unit test | Same test, parameterised over `/import` and `/import/preview` |
| 11 | ✅ | New `/import/actions/*` endpoints: correct auth, correct status codes, `AmbiguousFields`/`RelatedActionIds` present | Unit test + Live | `ImportActionEndpointsTests` (16 tests); live curl confirmed `GET /import/actions?batchId=` shape and decide/apply round-trip |
| 12 | ✅ | A seed file with unresolved ambiguity leaves its batch `Staged`, doesn't block startup, and is absent from `GET /quotes` until applied | Live | Fresh throwaway data dir + a genuinely conflicting user-imports file (real bundled quote id, different text) via `dotnet run`: startup logged `"ambiguous-test.json" left staged awaiting review`, completed normally, `GET /quotes/{id}` returned the original bundled text (unmodified) until decide+apply, confirmed by `GET /import/actions?status=Pending` |
| 13 | ✅ | A discarded (or never-applied) batch leaves zero domain-table rows, including no orphaned Source/Character/Person | Unit test | `DiscardBatchAsync_MarksActionsDiscarded_WritesNoDomainRows` |
| 14 | ✅ | Re-importing/re-seeding the same data twice creates no duplicate Source/Character/Person rows | Unit test + Live | `ApplyBatchAsync_TwoBatchesReferencingSameNewSource_IdempotentNoDuplicateSourceRow`; live check via `Quotinator.Tools.DbInspector` against the same throwaway DB after seeding + two subsequent `/import` calls touching the same Source/Character — `SELECT COUNT(*) FROM Sources WHERE Title='Airplane!'` and the equivalent for `Characters` both returned exactly `1` |
| 15 | ✅ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0/0; `dotnet test --configuration Release` → 976/976 pass |
| 16 | ✅ | T1 — full stage → decide → apply cycle; `/import`/`/import/preview` `200`/`202` splits; ambiguous seed file staged without blocking startup | Live | Every scenario in this row's scope was exercised end-to-end via `dotnet run` + curl (fresh-database startup with a genuinely ambiguous seed file, `/import`/`/import/preview` `200`/`202` split, decide→apply, `relatedActionIds` for a brand-new Source/Character with their stable ids visible in the staged payload), then confirmed by the developer's own Visual Studio pass. **That VS pass found a real bug**: `GET /import/actions?batchId=` returned zero results for a batch id copied straight from `POST /import/preview`'s own `batchId` response field. Root cause: `ImportResultResponse.BatchId` is typed `Guid`, which .NET serialises lowercase by default, while `System_ImportActions.BatchId` is always stored uppercase (`Guid.ToString("D").ToUpperInvariant()`) — the WHERE clause was a case-sensitive string comparison. This silently broke not just the list endpoint but `POST /import/actions/apply`/`.../discard` too — a mismatched-case `apply` call would find zero actions, return "nothing pending," and report `200 OK` having applied nothing. Fixed with `UPPER(BatchId) = UPPER(@batchId)` in `Sql.SystemImportActions`'s three affected queries (`SelectAllForBatch`, `MarkBatchDiscarded`, `BuildWhere`) — no migration needed, a pure query-level fix. Red/green verified: temporarily reverted the fix, confirmed the new regression test (`GetPagedAsync_And_ApplyBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue`) failed (`0` matches instead of `2`), then restored it and confirmed green. Full suite: 977/977 (976 + this new test) |
| 17 | ✅ | T2 — same cycle in Docker, including a fresh-seed startup with one intentionally ambiguous source file | Live | `docker build -f docker/Dockerfile` succeeded; container run with a mounted `/data/imports/` containing an ambiguous file and `Quotinator__DefaultConflictPolicy=review` reproduced the identical staged-batch/startup-not-blocked behaviour seen under `dotnet run`; `/health`, `/version`, `/quotes/random`, `/quotes/search`, and the full decide→apply cycle against `/import/actions/*` all verified against the running container |
| 18 | ✅ | **Phase B gate**: `/import/actions/*` demonstrated full parity with `/import/conflicts/*` in both unit tests and T1/T2 before any #149 code is deleted | Live + Unit test | Every capability `/import/conflicts/*` has is now covered by `/import/actions/*` (list/decide/undo/apply, plus discard which #149 never had) in both unit tests and live T1/T2 runs; developer confirmed their own T1 pass complete. Phase B proceeded (Task 35) |
| 19 | ✅ | `POST /import`'s `batchId`-mode alias (row 16's VS pass also flagged this as missing, not just the casing bug); `status`/`entityType` query filters on `/import/actions`, and `batchId`/`status` on the still-live `/import/conflicts`, all match case-insensitively | Unit test | `batchId`-mode: `ImportEndpointTests` (200/401/404/202) + `QuoteImportServiceTests.ApplyStagedBatchAsync_*`, full suite green (237/237 at the time). Case-insensitivity: `Sql.SystemImportActions`/`Sql.SystemImportConflicts`'s `BuildWhere`/`SelectAllForBatch` changed to `UPPER(col) = UPPER(@param)` for `Status`/`EntityType`/`BatchId`; new regression tests `GetPagedAsync_FilterByEntityTypeLowercase_StillMatchesUppercaseStoredValue`, `GetPagedAsync_FilterByStatusLowercase_StillMatchesStoredValue` (`SqliteImportActionServiceTests`), `GetPagedAsync_StatusFilterLowercase_StillMatchesStoredValue`, `ApplyBatchAsync_LowercaseBatchId_StillMatchesUppercaseStoredValue` (`SqliteConflictResolutionServiceTests`). Red/green verified by reverting `Sql.cs` and confirming the new tests failed. Full suite: 988/988 across all 9 test projects |
| 20 | ✅ | **Phase B**: `/import/conflicts/*` and all #149 code deleted (20 files); build clean; full suite green; live smoke test confirms `/conflicts` is gone and `/import/actions` + other endpoints are unaffected | Unit test + Live | `dotnet clean` + `dotnet build --configuration Release` → 0/0 (a stale incremental build initially masked 6 dangling `<see cref>` XML-doc warnings in surviving files — caught and fixed only after a genuinely clean rebuild). `dotnet test --configuration Release` → 938/938 across all 9 test projects (988 minus the ~50 tests belonging to the 5 deleted test files). Live: started `dotnet run` against the built Release binaries — `GET /api/v1/import/conflicts` → `404`, `GET /api/v1/import/actions` → `200` with live staged-action data, `GET /api/v1/health`/`GET /api/v1/quotes/random`/`GET /scalar/v1` → `200` |
| 21 | ✅ | T2 — full Docker cycle covering both Phase A and Phase B in one container | Live | `docker build -f docker/Dockerfile` succeeded. Container: clean fresh-seed startup (788 quotes), `/health`/`/version`/`/quotes/random`/search variants all matched documented behaviour, `GET /import/conflicts` → `404` (Phase B), `GET /import/actions` → `200` live data, OpenAPI spec confirmed no `/conflicts` paths remain. Full decide→apply cycle: `POST /import` (review policy) → `202`; `POST /import/actions/apply` while one action still pending → `422` with `pendingActionIds`; decide the second action → `204`; apply again → `200`. `batchId`-mode alias (`POST /import?batchId=`) verified end-to-end against a freshly staged preview batch → `200`. Discard flow verified → `204`, batch's actions confirmed `Discarded`. Case-insensitive `batchId`/`status` query filters confirmed live (lowercase values matched uppercase-stored data). No errors in container logs. |
| 22 | ✅ | `POST /import`'s dual-mode dispatch: a genuinely bodyless request (no `Content-Type`, no body at all, no `batchId`) returned a bare, uninformative framework `400` under real Kestrel (found during row 21's T2 pass) — bypassing `BadRequestExceptionHandler` entirely, since ASP.NET Core's Minimal API auto-binding for `IFormFile?`/`[FromForm] string?` fails at the routing/binding layer itself for a request with no form content-type, not via a thrown exception middleware can intercept. Fixed by taking `HttpRequest` manually instead of binding `file`/`settings` automatically, checking `HasFormContentType` before reading the form, and returning a clear `422` (`ErrorImportFileOrBatchIdRequired`) when neither `file` nor `batchId` is derivable | Unit test + Live | New `Import_NoBodyAndNoBatchId_Returns422` (asserts on the specific message, not just the status code, since `WebApplicationFactory`'s in-memory TestServer handled the old, broken code differently than real Kestrel and would have passed either way on status code alone) and `Import_WithBatchId_NoBodyAtAll_StillWorks` (proves `batchId` mode never touches the request body at all). Red/green verified by reverting `ImportEndpoints.cs` and confirming both tests failed. Also verified against a real `dotnet run` Kestrel instance (not just TestServer): the bodyless-no-batchId case now returns `422` with the correct message, and the bodyless-with-batchId case returns `404` for the unknown batch rather than a bare `400`. Full suite: 940/940. `README.md`/`addon/DOCS.md`'s `/import` row descriptions corrected to reflect `file`/`batchId` as alternatives (this had been missed when the `batchId`-mode alias itself was added in row 19) |

---

## Not in scope for this issue (deferred)

- #153 (declarative conflict-resolution file) — not built here, just enabled by this design; owns
  the future bulk-apply-a-policy-to-a-batch capability, not #154.
- #59's own reset/restore logic — downstream of this issue, not part of it (see #59's plan doc).
- #155 (migration review before milestone close) — separate, unrelated concern; tracked as its own
  issue. Also now the place where `System_ImportConflicts`' migration/baseline entries get squashed
  out once #149's code is removed in Phase B — not decided inline in this issue.
