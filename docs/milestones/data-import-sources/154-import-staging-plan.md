# #154 — Unify import, preview, and seeding on one staging engine

**Status:** Planning
**GitHub issue:** #154
**Tiers required:** T1, T2
**Depends on:** #149 (`IConflictResolutionCoordinator`, `System_ImportConflicts` table), #56 (audit/change log)

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

Key decisions made during planning (not re-litigated here, just recorded):

1. `preview` = stage (compute and durably record exactly what an import would do; write nothing to
   domain tables). `import` = execute a staged batch (apply it). A plain `POST /import` call still
   works as one convenient round-trip.
2. `POST /import` returns a distinct HTTP status per outcome: `200 OK` when everything applied,
   `202 Accepted` when the batch is left `Staged` awaiting a decision — callers branch on status
   code alone, no body parsing required.
3. `POST /import/preview`'s contract changes from "nothing persisted, no `ImportBatch` created" to
   "stages a real, inspectable batch." Acceptable pre-release (same precedent #152 established for
   its own route move — nothing in this milestone has shipped yet).
4. Seeding uses the identical mechanism and is explicitly allowed to leave a source file's batch
   `Staged`/unapplied if it has unresolved ambiguity — no forced auto-resolution policy override.
   Startup is not blocked; it logs which file(s) ended up staged and continues normally. This is
   the intended fine-tuning loop (edit the file, change its manifest `duplicateResolution`
   override, or later supply a #153 decisions file, then re-stage/apply).
5. The mechanism must be a genuinely reusable, domain-agnostic primitive, maximized in
   `Quotinator.Data` — mirroring exactly how #149 already splits `IConflictResolutionCoordinator`
   (generic, Data) from `SqliteConflictResolutionService` (the one domain-specific piece, Engine).
6. Source/Character/Person creation is deferred from staging time to apply time (flagged to the
   user as an inference in the filed issue, not yet separately re-confirmed at implementation
   start — confirm before building step 3 below if any doubt remains).

---

## Design

### 1. Generic staging primitive (`Quotinator.Data`)

**Status:** ⬜ Not started

New `SystemImportAction` entity (`Quotinator.Data.Entities`), RecordBase-shaped from creation:
`Id, BatchId` (loose string reference, no FK — mirrors `SystemImportConflict.BatchId`),
`ActionType` (free-text — `"Add"`/`"Modify"` today, room for a future `"Remove"` with zero
migration), `EntityType` (free-text, caller-owned meaning), `EntityId`, `ExistingBatchId` (null for
Add), `ExistingValue`/`IncomingValue`/`MergedFields` (opaque JSON, never deserialized in Data),
`AppliedPolicy`, `Status`, `DetectedAt`, `AppliedAt`, `DiscardedAt`.

Status lifecycle: `Pending` (needs an explicit decision) → `Decided` → `Applied`/`Discarded`.
Apply-readiness rule (generic, Data-owned, needs no domain knowledge): refuse if anything sharing
the batch is still `Pending`.

New `IImportActionCoordinator`/`ImportActionResolutionCoordinator` (`Quotinator.Data.Import`) — a
**new sibling** to `IConflictResolutionCoordinator`, not a generalization of it (keeps #149's
shipped, T1/T2-verified code untouched): `StageAsync` (writes a batch of already-classified
actions — classification is the caller's job, not Data's), `DecideAsync`/`UndoDecisionAsync`
(identical shape to `ConflictResolutionCoordinator`), `TryApplyBatchAsync(batchId,
applyResolvedAction, ct)` (same refusal contract, caller-supplied per-action callback), and a new
`DiscardBatchAsync(batchId, ct)` with no #149 analogue.

A consuming project's plug-in surface stays deliberately small: (a) a *classifier* — domain schema
knowledge lives here only — returning Add / unambiguous-Modify / ambiguous-Modify-needs-a-decision
for one incoming row; (b) an *applier callback* — takes a `Decided` action, writes it to the
consumer's own tables. Everything else (table, status machine, decide/undo/apply/discard
orchestration) is reusable as-is.

### 2. Quotinator-specific plug-in (`Quotinator.Engine`)

**Status:** ⬜ Not started

New side-effect-free planner: the classifier above, specific to Quotinator's Quote/Source/
Character/Person schema. Looks up (never creates) a matching Quote and, for Sources/Characters/
People, whether a match already exists; computes the merge via `FieldMergeResolver`. Used
identically by `/import/preview`, `/import`'s staging phase, and the seed flow's staging step.

Evolved `QuoteSeedWriter` as the shared applier: given a `Decided` action, resolves
`GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync` (moved here from
staging time) and writes the Quote — today's existing logic, invoked later in the pipeline. Used
identically by `/import/actions/apply`, `/import` (file mode, once nothing's ambiguous), and the
seed flow's apply attempt.

`ImportBatch` (Engine entity) gains `Status` (`Staged`/`Applied`/`Discarded`, plain `ADD COLUMN`, no
CHECK — matching `ConflictPolicy`'s precedent) and `AppliedAt` (nullable, distinct from
`ImportedAt`). `SqliteQuoteImportService.ImportAsync` becomes a thin orchestrator: stage via the
planner, then (unless staging-only) attempt apply via the applier.

### 3. Endpoints (`ImportEndpoints.cs`, `Import` tag)

**Status:** ⬜ Not started

- `POST /api/v1/import/preview` — stages only, never applies. Returns `batchId` + a summary of
  every planned action.
- `POST /api/v1/import` — `file`+`settings` (stage + attempt apply) or `batchId` (apply an
  already-staged batch — convenience alias for `/import/actions/apply`). `200 OK` when everything
  applied; `202 Accepted` (body carries `batchId` + which actions need a decision) otherwise.
- `GET /api/v1/import/actions` — paginated, filter by `batchId`/`status`.
- `POST /api/v1/import/actions/{id}/decide` / `.../undo` — reuses `ConflictDecisionRequest`/
  `FieldDecision`/`GenresFieldDecision` as-is.
- `POST /api/v1/import/actions/apply?batchId=` — 422 if anything sharing the batch is still
  `Pending`; otherwise commits everything in one transaction.
- `POST /api/v1/import/actions/discard?batchId=` — marks everything `Discarded`; never touches
  domain tables (per the deferred-creation design, no Source/Character/Person rows exist to clean
  up). 422 if already applied/discarded.
- `/api/v1/import/conflicts/*` (#149) — untouched, separate table/coordinator.

### 4. Seeding integration

**Status:** ⬜ Not started

Per source file: stage via the shared planner, then attempt apply via the shared applier — same
per-file `ImportBatch` granularity as today. No policy override, no forced auto-resolution. A file
left with anything `Pending` simply stays `Staged` — its records don't appear in the live dataset
yet. Startup logs a clear, itemised message for every file left staged and continues normally
otherwise.

### 5. Migrations

**Status:** ⬜ Not started

- **Quotinator.Data** (`DataOwnedMigrations`, version 8, after `ImportConflictMigrations.
  AddExistingBatchId` at 7): new `System_ImportActions` table + indexes on `BatchId`/`Status`. Add
  to `DataBaselineSql` in the same commit (schema-drift test requires it).
- **Quotinator.Engine** (`QuotinatorMigrations.All`, version 7, confirmed next after
  `Migration006_RecordCompleteness`): `ALTER TABLE ImportBatches ADD COLUMN Status TEXT NOT NULL
  DEFAULT 'Applied'` + `ADD COLUMN AppliedAt TEXT` — existing rows backfill correctly (everything
  before this feature always committed immediately). Update `BaselineSchema` to match.

### 6. Tests

**Status:** ⬜ Not started

- Regression proof: re-run existing `SqliteQuoteImportServiceTests` and the seeding test suite
  **unmodified** after the planner/applier extraction.
- `Quotinator.Data.Tests`: `SystemImportActionWriterReaderTests`, `ImportActionResolutionCoordinatorTests`
  (decide/undo, apply-refuses-with-pending, apply-commits-once, discard-marks-everything-and-
  creates-nothing) — against a fake classifier/applier callback, proving the coordinator needs no
  real Quote/Source schema (mirrors `ConflictResolutionCoordinatorTests`).
- `Quotinator.Engine.Tests`: planner classification correctness; Source/Character/Person resolution
  deferred to apply time (never at stage time); `/import`'s `200`-vs-`202` split; `/import/preview`'s
  stage-only contract; seeding leaving a batch `Staged` when ambiguous with correct startup log and
  unaffected boot; a discarded (or never-applied) batch leaves zero rows anywhere, including no
  orphaned Sources/Characters/People.
- `Quotinator.Api.Tests`: new `ImportActionEndpointsTests`; updated `ImportEndpointTests` for
  `/import`'s dual-mode + status-code split and `/import/preview`'s new stage-only contract.

### 7. DTOs / i18n / documentation

**Status:** ⬜ Not started

`Quotinator.Core.Models` response DTOs (mirrors #149's Core placement — pure wire-shape POCOs).
New `ApiMessages` + all-three-locale `i18ntext/UI.*.json` keys for not-found/already-applied/
already-discarded/not-decided/ambiguous-fields cases. `README.md`/`addon/DOCS.md`/`RestApi.razor`
updates (new endpoint rows, `/import`'s two status codes documented). `CLAUDE.md` — document the
generic-primitive placement rationale and the seeding-can-leave-a-batch-staged behavior explicitly
(real behavior change from today's always-fully-applies model).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Coordinator: stage/decide/undo/apply/discard state transitions correct against a fake classifier/applier (no real schema needed) | Unit test | `ImportActionResolutionCoordinatorTests` |
| 2 | ⬜ | Reader/writer round-trip for `System_ImportActions`, including all Status transitions | Unit test | `SystemImportActionWriterReaderTests` |
| 3 | ⬜ | Planner correctly classifies Add / unambiguous-Modify / ambiguous-Modify; never creates Source/Character/Person during staging | Unit test | Engine.Tests planner test class |
| 4 | ⬜ | Applier resolves Source/Character/Person and writes the Quote only at apply time; identical result whether reached via `/import`, `/import/actions/apply`, or seeding | Unit test | Engine.Tests applier test class |
| 5 | ⬜ | Existing import behavior unchanged by the planner/applier extraction | Unit test | `SqliteQuoteImportServiceTests` passes **unmodified** |
| 6 | ⬜ | Existing seeding behavior unchanged where nothing is ambiguous | Unit test | Existing seeding test suite passes **unmodified** for the non-ambiguous case |
| 7 | ⬜ | `POST /import` returns `200` when everything applies, `202` with a usable `batchId` when something needs review | Unit test | `ImportEndpointTests` (updated) |
| 8 | ⬜ | `POST /import/preview` stages only, creates a real inspectable batch, never applies | Unit test | `ImportEndpointTests` (updated) |
| 9 | ⬜ | New `/import/actions/*` endpoints: correct auth (writes require `X-Api-Key`), correct status codes | Unit test | `ImportActionEndpointsTests` |
| 10 | ⬜ | A seed file with unresolved ambiguity leaves its batch `Staged`, doesn't block startup, and is absent from `GET /quotes` until applied | Unit test + Live | Engine.Tests + T1 manual restart |
| 11 | ⬜ | A discarded (or never-applied) batch leaves zero domain-table rows, including no orphaned Source/Character/Person | Unit test | Engine.Tests |
| 12 | ⬜ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0/0; `dotnet test --configuration Release` → all pass |
| 13 | ⬜ | T1 — full stage → decide → apply cycle in Visual Studio; `/import` direct-call `200`/`202` split; ambiguous seed file staged without blocking startup | Live | Manual VS run per this doc's scope |
| 14 | ⬜ | T2 — same cycle in Docker, including a fresh-seed startup with one intentionally ambiguous source file | Live | `docker build` + smoke test |

---

## Not in scope for this issue (deferred)

- #153 (declarative conflict-resolution file) — not built here, just enabled by this design.
- #59's own reset/restore logic — downstream of this issue, not part of it (see #59's plan doc).
- #155 (migration review before milestone close) — separate, unrelated concern raised during this
  issue's planning; tracked as its own issue.
