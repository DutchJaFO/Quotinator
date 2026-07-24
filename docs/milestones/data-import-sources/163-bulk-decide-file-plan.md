# #163 — Bulk-decide a staged import batch via file export/import, CSV and JSON (Phase 1 of #153)

**Status:** In progress (step 7)
**GitHub issue:** #163
**Tiers required:** T1, T2
**Depends on:** #162, #149, #154

---

## Spec requirements (from the GitHub issue, as amended by developer decisions 2026-07-24)

1. New `GET /api/v1/import/actions/export?batchId=&format=csv|json` endpoint (default `json`)
   returns every decidable field across the batch's actions in the flat
   `ActionId,EntityId,EntityType,Field,ExistingValue,IncomingValue,Decision,CustomValue,
   MarkCompletenessAs` row shape — `Pending`, `Decided`, **and `Blocked`** actions are all included
   (decision: yes — see Resolved decisions below). `MarkCompletenessAs` is one value per `ActionId`
   group (repeated on every row of that group), needed to resolve a `Blocked` hold. No `X-Api-Key`
   required, matching `GET /import/actions`'s existing public-read precedent.
2. New `POST /api/v1/import/actions/bulk-decide?batchId=&format=csv|json` endpoint accepts the edited
   file back, groups rows by `ActionId`, and applies each action's decision via the existing
   `IImportActionService.DecideAsync` — reuses existing per-field validation, no new validation logic
   invented.
3. CSV parsing/writing uses a proper CSV library (quoting, embedded commas/quotes in
   `ExistingValue`/`IncomingValue` handled correctly) — not naive string splitting. **Resolved: extract
   `CsvLineParser`'s existing logic into a shared, non-`internal` location and add a matching writer —
   no duplication, no new NuGet dependency (developer decision: DRY).**
4. Data-domain validation: `Decision` must be a recognised `FieldResolutionChoice` member
   (`Keep`/`Replace`/`Custom`); an unrecognised value is a clear per-row error, not silently
   defaulted.
5. Consumer-domain validation: `EntityType` must be one of `ImportActionEntityTypes.All`; `Field` must
   be a valid, currently-decidable field name for that `EntityType`. **Every entity type is decidable
   after this issue** — Series/Universe (the last two remaining non-decidable types) get their own
   Modify/decidability support as part of this issue (decision: yes, brought into scope — see Resolved
   decisions below). `ImportActionNotDecidableException`'s message is retained only as a forward-
   compatibility guard for any future entity type added without a Modify path yet.
6. A row-level error (unknown `ActionId`, action not part of the requested `batchId`, invalid
   `EntityType`, invalid `Decision` value, unknown `Field` name, action already applied) reports which
   row failed without aborting the rest of the file — matches `POST /import`'s existing "one bad row
   never aborts the rest" model.
7. `GET .../export`'s output (either format) round-trips through `POST .../bulk-decide` unmodified
   with zero errors — baseline correctness check before any real edits are made.
8. `README.md`/`addon/DOCS.md` endpoint tables updated in the same commit.
9. **New (developer decision): an already-`Decided` action's export row shows its actual original
   per-field choice, not an inferred one.** `System_ImportActions` gains a new additive
   `OriginalDecision` column; `DecideAsync` persists the caller's actual `FieldDecision` per field
   there (alongside the existing resolved-value `MergedFields` column, which is untouched) so a
   re-export reflects exactly what was chosen, and a bulk-decide revision is a genuine round-trip, not
   a guess.

---

## Investigation findings (current codebase, re-verified 2026-07-24)

No export/bulk-decide scaffolding exists anywhere in `src/` today — `grep -rn "export"` across the
solution returns only unrelated Bootstrap JS library hits. This issue's endpoints, DTOs, and CSV
reader/writer are entirely new code.

**This plan doc's own original investigation (written when only Quote and Source were decidable) was
substantially stale, re-verified against the current codebase before starting implementation.** Since
this plan doc was first written, #171/#172 (StageDirection/SoundCue), #173 (Person), #175 (Character),
and #176 (Conversation) have all shipped their own Modify/decidability paths, and #206 merged
`Quotinator.Engine` into `Quotinator.Core`.

**Project structure — `Quotinator.Engine` no longer exists (#206).** Current paths:
- `ConflictDecisionRequest.cs` → `src/Quotinator.Core/Models/ConflictDecisionRequest.cs`
- `SqliteImportActionService.cs` → `src/Quotinator.Core/Services/SqliteImportActionService.cs`
- `ImportActionPlanner.cs` → `src/Quotinator.Core/Database/ImportActionPlanner.cs`
- `Sql.cs` (with `SystemImportActions`, `Series`, `Universe`) → `src/Quotinator.Core/Queries/Sql.cs`
- `ISystemImportActionReader` → `src/Quotinator.Data/Repositories/ISystemImportActionReader.cs`
  (unaffected by the Engine→Core merge — it was already in `Quotinator.Data`)
- `System_ImportActions`'s own migrations (needed for requirement 9's new column) →
  `src/Quotinator.Data/Database/ImportActionMigrations.cs` — this table is **Quotinator.Data-owned**
  (tracked via `System_SchemaVersion`, a fixed internal list, applied before any consumer migration —
  see `CLAUDE.md`'s "Migration ownership split"), not one of `Quotinator.Core`'s own
  `QuotinatorMigrations`.

**Seven of nine entity types are decidable today — only Series/Universe remain Add-only.**
`SqliteImportActionService.DecideAsync` has a Modify-decision branch for Quote, Source, Character,
Person, StageDirection, SoundCue, and Conversation. `SeriesEntry`/`UniverseEntry`
(`src/Quotinator.Core/Import/SeriesEntry.cs`/`UniverseEntry.cs`) carry no explicit `id` at all — #180
matched/created them purely by `Name` (unique natural key), with no Correction shape and no Modify
path, unlike every other entity's own two-shape (`id`-present Correction / `id`-absent Creation)
pattern. `SeriesEntity`/`UniverseEntity` (`src/Quotinator.Core/Entities/Series.cs`/`Universe.cs`) have
a small field set: Series has `Name` + nullable `UniverseId`; Universe has `Name` only.

**`ConflictDecisionRequest`'s current property list is far larger than this plan doc originally
described.** 25 properties across 7 entity-scoped groups, plus the entity-agnostic
`MarkCompletenessAs`:
- Quote: `QuoteText`, `OriginalLanguage`, `Source`, `Date`, `Character`, `Author`, `Type`, `Genres`
- Source (#162/#180): `SourceTitle`, `SourceType`, `SourceDate`, `SourceSeriesId`
- StageDirection (#171): `StageDirectionText`, `StageDirectionImageUrl`
- SoundCue (#172): `SoundCueText`, `SoundCueSoundFileUrl`, `SoundCueImageUrl`
- Conversation (#176): `ConversationDescription`
- Person (#173): `PersonName`, `PersonDateOfBirth`, `PersonDateOfDeath`
- Character (#175): `CharacterName`

This issue adds two more groups: `SeriesName`/`SeriesUniverseId` (Series) and `UniverseName`
(Universe) — see Steps below.

**Six `To*DecisionMap` helpers exist in `SqliteImportActionService.cs` today** (up from the original
two): `ToDecisionMap` (Quote, line 1318), `ToSourceDecisionMap` (1342), `ToPersonDecisionMap` (1360),
`ToCharacterDecisionMap` (1377), `ToStageDirectionDecisionMap` (1392), `ToSoundCueDecisionMap` (1408).
Conversation has no dedicated helper — its single `description` field is built inline (lines 150-154).
This issue adds `ToSeriesDecisionMap`/`ToUniverseDecisionMap` alongside these, plus the **reverse**
mapping bulk-decide itself needs — a `(EntityType, Field)` string pair back into the correct
`ConflictDecisionRequest` property, across all nine entity types — which does not exist anywhere
today and is new code this issue must add.

**Only `Modify` actions are ever decidable.** `Add` actions are always staged already-`Decided` (an
Add is never ambiguous). So the export naturally only ever emits rows for actions whose `ActionType`
is `Modify` — this should be stated explicitly in the implementation, not left implicit.

**`ImportActionStatus` has four members: `Pending`, `Decided`, `Applied`, `Discarded`, `Blocked`.**
`ImportActionResolutionCoordinator.DecideAsync` still only rejects `Applied`/`Discarded` — deciding a
`Blocked` action is accepted today, which is exactly the mechanism requirement 1's inclusion of
`Blocked` rows relies on.

**The original per-field `Keep`/`Replace`/`Custom` choice was not persisted anywhere once an action
is `Decided` — confirmed, and resolved by requirement 9 above.** Every branch in `DecideAsync`
resolves via `FieldMergeResolver.ResolveWithDecisions` and calls `_coordinator.DecideAsync(actionId,
JsonSerializer.Serialize(resolved...Payload), request.MarkCompletenessAs)` — only the **resolved**
payload is stored today; the caller's original `ConflictDecisionRequest`/`FieldDecision.Choice` is
discarded. This issue changes that — see Steps below.

**`ImportActionNotDecidableException`'s message text is no longer stale — #170 has shipped.** Current
wording (`src/Quotinator.Core/Services/ImportActionNotDecidableException.cs`): `"Import action
'{actionId}' is a '{entityType}' action and cannot be manually decided — this action's entity type
does not currently support a Modify decision."` After this issue, nothing in
`ImportActionEntityTypes.All` reaches this path — it becomes a forward-compatibility guard only.

**No CSV writer exists anywhere in the codebase today**, and the one existing CSV reader
(`CsvLineParser` in `src/Quotinator.Converters.Csv/CsvLineParser.cs`) is still `internal` to
`Quotinator.Converters.Csv` with its only `InternalsVisibleTo` grant reaching
`Quotinator.Converters.Csv.Tests` — not `Quotinator.Core` or `Quotinator.Api`. No `CsvHelper` (or any
CSV NuGet package) reference exists anywhere in the solution.

**JSON parsing policy applies.** Per `CLAUDE.md`'s JSON parsing policy, the JSON encoding of the flat
row shape must be a typed DTO deserialized via `JsonSerializer.Deserialize<List<T>>` — never manual
`JsonNode` walking. `FieldResolutionChoice` is already `[JsonConverter(typeof(JsonStringEnumConverter))]`-decorated,
so the DTO's `Decision` property can be typed directly as `FieldResolutionChoice?` for the JSON path
(case-insensitive member matching is automatic) while still needing an explicit string-to-enum
validation step for the CSV path, which has no equivalent built-in enum converter.

**`ISystemImportActionReader.GetAllForBatchAsync`** already exists (used throughout
`SqliteImportActionService`, e.g. `ClearStaleAddTargetsAsync`, `ComputeRelatedActionIdsAsync`) and is
the natural data source for the export endpoint — no new SQL query is needed to enumerate a batch's
actions; the new work is entirely in shaping the flat per-field-row output and the reverse
bulk-decide mapping.

**Field-map helpers per entity already exist and should be reused, not re-derived.**
`QuoteFieldMerge.ToFieldMap` (Quote) plus six private `ToFieldMap` overloads in
`SqliteImportActionService.cs` (`SourceActionPayload`, `CharacterActionPayload`,
`PersonActionPayload`, `StageDirectionActionPayload`, `SoundCueActionPayload`,
`ConversationActionPayload`, lines 1205-1221) already produce the field-name → value maps the export
endpoint needs per entity type. This issue adds `ToFieldMap(SeriesActionPayload)`/
`ToFieldMap(UniverseActionPayload)` alongside them.

---

## Resolved decisions (developer, 2026-07-24)

Four genuine gaps existed between the (older, pre-#171–#176) issue text and the current codebase.
Per this project's "gap resolution is the developer's decision" rule, they were surfaced rather than
silently decided, and resolved as follows:

1. **Blocked actions are included in export/bulk-decide.** Rationale given: "the idea is that we
   decide what should happen in bulk" — a `Blocked` action (held because it would silently overwrite a
   `Complete` row) is exactly the case an operator needs to resolve, often in bulk across many rows at
   once. Consequence: the flat row shape gains a `MarkCompletenessAs` column (requirement 1) — one
   value per `ActionId` group, since `MarkCompletenessAs` is an action-level, not field-level, concept.
2. **The original per-field decision is genuinely persisted, not inferred.** Rationale given: "we want
   to return to the exact previous state" — a heuristic inference (resolved == existing → assume Keep,
   etc.) cannot distinguish "the operator chose Keep" from "there was never a real choice because both
   sides already agreed," so it cannot honestly reconstruct what was actually decided. `DecideAsync`
   now also writes a new `OriginalDecision` column on `System_ImportActions` (Quotinator.Data-owned,
   additive migration) storing the caller's actual per-field `FieldDecision` map — untouched
   `MergedFields` keeps storing the resolved value as it always has. **Why this was never a problem
   before this issue:** every prior decide flow is one-shot and forward-only — `POST .../decide` takes
   a fresh `ConflictDecisionRequest` from the caller right there in the call, and the only way to
   "change your mind" was `POST .../undo` (discards the old decision entirely, then decide again from
   scratch). Nothing before this issue ever needed the system to remember and redisplay a past choice.
3. **CSV reader/writer: DRY — extract and share, never duplicate.** `CsvLineParser`'s logic moves out
   of `Quotinator.Converters.Csv` into a shared, non-`internal` location (exact project TBD in Steps —
   `Quotinator.Data` is the natural home, since it already hosts other domain-agnostic infrastructure
   and both `Quotinator.Converters.Csv` and this issue's code can reference it), with a matching writer
   added alongside it. `Quotinator.Converters.Csv` is refactored to call the shared parser instead of
   its own copy — no duplicated CSV logic anywhere in the codebase, and no new NuGet dependency.
4. **Series/Universe are brought fully into scope of this issue.** They are the only two entity types
   left without a Modify/decide path (every other exclusion from the original issue text has since
   shipped its own dedicated issue — #171/#172/#173/#175/#176). Rather than leaving Series/Universe as
   a permanent exception or spinning off yet another dedicated issue, this issue gives them the same
   `id`-present-Correction / `id`-absent-Creation two-shape treatment #162 gave Source and #173 gave
   Person, plus the full Modify/decide/reverse/cleanup machinery every other entity now has — see
   Steps below. This is a substantial addition on top of the export/bulk-decide work itself,
   comparable in size to #162 or #173 individually.
5. **Rate limiting / auth tier for the new endpoints** — unchanged from the original issue text: export
   is public (`GET`, matching `GET /import/actions`'s precedent), bulk-decide requires `X-Api-Key`
   (matching every other staged-action write). `RequireRateLimiting` on both, no exceptions, per the
   project's universal rate-limiting rule.

---

## Steps

### 1. Write the red tests

**Status:** Not started.

Add the eleven test methods from the issue's original "Expected tests" table, plus new tests covering
Series/Universe decidability (mirroring #173's own `PlanPeopleAsync`/`DecideAsync`/reverse test
shapes), `Blocked`-action bulk-decide, and `OriginalDecision` round-trip fidelity (a revised export
shows the actual prior choice, not an inferred one) — to a new
`tests/Quotinator.Api.Tests/Endpoints/ImportActionExportEndpointsTests.cs` (API-level) plus
`Quotinator.Core.Tests` additions for the Series/Universe planner/service logic, grouped with the
existing `ImportActionPlannerTests.cs`/`SqliteImportActionServiceTests.cs`. Confirm each fails for the
expected reason before any implementation code is written.

### 2. `System_ImportActions.OriginalDecision` — new additive migration

**Status:** Done.

New Quotinator.Data-owned migration in `src/Quotinator.Data/Database/ImportActionMigrations.cs`
(`ALTER TABLE System_ImportActions ADD COLUMN OriginalDecision TEXT;` — nullable, one schema change,
idempotent per this project's migration policy — `ALTER TABLE ... ADD COLUMN` has no `IF NOT EXISTS`
form in SQLite, so this is a genuinely new, never-to-be-edited migration entry, not a retrofit of an
existing one). Update the Data-owned baseline schema to match, and the schema-drift test that compares
baseline vs. incremental replay for `System_ImportActions`.

### 3. `DecideAsync` persists the original per-field decision

**Status:** Done.

Every existing branch in `SqliteImportActionService.DecideAsync` (Source, StageDirection, SoundCue,
Conversation, Person, Character, Quote) additionally serializes the resolved
`Dictionary<string, FieldMergeDecision>` (or the raw `ConflictDecisionRequest` fields actually
supplied) into the new `OriginalDecision` column alongside the existing `MergedFields` write —
additive to `_coordinator.DecideAsync`'s own signature/call, not a replacement of what it already
does with `MergedFields`. Confirm this doesn't affect `ApplyResolvedActionAsync` (which only reads
`MergedFields`, unchanged) or `ComputeAmbiguousFields` (which computes from `ExistingValue`/
`IncomingValue`, unchanged) — `OriginalDecision` is read only by the new export endpoint (step 9).

### 4. Series/Universe: widen `series[]`/`universe[]` schema to the two-shape pattern

**Status:** Done.

`schemas/source-extended.schema.json`'s `series`/`universe` `$def`s gain an optional `id` (UUID v4,
same pattern as `source`/`character`/`person`). `SeriesEntry.cs`/`UniverseEntry.cs` gain a nullable
`Id` property. `id` present → Correction shape, matched by that id; `id` absent → today's existing
Creation-via-Name behaviour, unchanged.

### 5. Series/Universe: `Sql.Series`/`Sql.Universe` new queries and planner Modify branches

**Status:** Done.

Add `SelectExistingById`, `UpdateFieldsById`, `SelectCompletenessById`, `UpdateCompletenessById` to
both `Sql.Series` and `Sql.Universe` nested classes (`src/Quotinator.Core/Queries/Sql.cs`), mirroring
the shape #173 added for `Sql.People`. `PlanSeriesAsync`/`PlanUniverseAsync`
(`ImportActionPlanner.cs`) gain an id-match lookup in front of the existing natural-key path: id
present and found → field-map diff (`name`, plus `universeId` for Series) → unchanged-check →
policy-based resolution → `CompletenessGuard.ShouldBlock` evaluated against the policy-resolved value
→ stage `Blocked` or `Modify` — the exact shape `PlanPeopleAsync` already established. An id present
but not found falls back to the existing natural-key path, matching every other entity's own
"id declared but doesn't match anything" precedent (`PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged`'s
shape) — an explicit but unmatched id is honoured as the new row's own id, mirroring
`PlanSourcesAsync`'s `canonicalId ?? EntityIdentity.SeriesId(...)`/`EntityIdentity.UniverseId(...)`
precedent (the same bug #175 found and fixed for Character — do not repeat it here).

### 6. Series/Universe: apply/decide/ambiguous-fields/reverse/cleanup branches

**Status:** Done.

Mirroring #173's own Person work exactly, for both Series and Universe:
- `ApplyResolvedActionAsync`'s Series/Universe cases split on `ActionType`: `Add` unchanged; `Modify`
  calls the new `UpdateFieldsById`, then `ApplyCompletenessAsync`.
- `DecideAsync` gains `EntityType == Series && ActionType == Modify` / `EntityType == Universe && ...`
  branches, using new `ToSeriesDecisionMap`/`ToUniverseDecisionMap` helpers and new
  `ConflictDecisionRequest.SeriesName`/`SeriesUniverseId`/`UniverseName` properties (nullable
  `FieldDecision?`).
- `ComputeAmbiguousFields` gains `Series`/`Universe` cases.
- `ReverseAppliedActionsAsync`'s Series/Universe cases split on `ActionType`: `Modify` restores fields
  via `UpdateFieldsById` from `ExistingValue`, no active-reference check; `Add` re-verifies live
  (write the test against the unmodified Guid-typed repository path first, per #175's own
  re-verification precedent — this session's #200–205/#176–178 work may already make a raw-SQL switch
  unnecessary here too, but confirm rather than assume).
- `ClearStaleAddTargetsAsync`'s Series/Universe branches — same re-verification approach as above.

### 7. Define the flat row DTO and the field-name ↔ `ConflictDecisionRequest` mapping

**Status:** Done.

New DTO (name TBD, e.g. `ImportActionFieldRow`) in `Quotinator.Core.Models`:
`ActionId` (Guid), `EntityId` (string), `EntityType` (string), `Field` (string), `ExistingValue`
(string?), `IncomingValue` (string?), `Decision` (`FieldResolutionChoice?`), `CustomValue` (string?),
`MarkCompletenessAs` (`CompletenessStatus?`, per resolved decision 1). List-valued fields (Quote's
`genres`) serialize as a single delimited string in `ExistingValue`/`IncomingValue`/`CustomValue`
(issue specifies `;`-separated, e.g. `drama;comedy`) — needs a dedicated encode/decode helper, since
every other field is a plain scalar.

New reverse-mapping helper (per requirement 5's "valid, currently-decidable field name for that
`EntityType`" check) covering all nine decidable entity types — given `(EntityType, Field, Decision,
CustomValue)`, either produces a `FieldDecision`/`GenresFieldDecision` to slot into a
`ConflictDecisionRequest`, or throws/reports a row-level error for an unrecognised field name.
Mirrors: the six existing `To*DecisionMap` helpers (Quote, Source, Person, Character, StageDirection,
SoundCue); Conversation's single `description` field, which has **no dedicated `To*DecisionMap`
helper today** — it is built inline in `DecideAsync` (lines 150-154) rather than through the six-helper
pattern, so the reverse mapping needs its own explicit one-field case for Conversation, not a lookup
against a helper that doesn't exist; and the two new `ToSeriesDecisionMap`/`ToUniverseDecisionMap`
helpers from step 6. Reuses `ImportActionEntityTypes.All` for the `EntityType` validity check — every
member is now decidable, so the `ImportActionNotDecidableException` path is unreachable in practice
today, kept only as the forward-compatibility guard requirement 5 describes.

### 8. CSV read/write — extract shared parser, add writer

**Status:** Done.

Per resolved decision 3: extract `CsvLineParser`'s logic (`src/Quotinator.Converters.Csv/CsvLineParser.cs`)
into a shared, non-`internal` location — `Quotinator.Data` (confirm final placement against existing
project-boundary conventions; it needs to be reachable from `Quotinator.Converters.Csv`,
`Quotinator.Core`, and `Quotinator.Api` alike). Add a matching CSV **writer** alongside it — nothing in
the codebase writes CSV output today. `Quotinator.Converters.Csv` is refactored to call the shared
parser instead of its own copy, confirming no behaviour change via its existing test suite. Both
directions need to handle quoting for `ExistingValue`/`IncomingValue`/`CustomValue`, which may contain
commas, quotes, or newlines (a quote's own text can contain any of these).

### 9. `GET /api/v1/import/actions/export?batchId=&format=csv|json`

**Status:** Not started.

New route in `ImportEndpoints.cs`'s `publicGroup` (no `X-Api-Key`, matching `GET /actions`'s
precedent), `RequireRateLimiting(RateLimitPolicies.Admin)` per the project's universal rate-limiting
rule. Fetches the batch's actions via the existing `ISystemImportActionReader.GetAllForBatchAsync`,
filters to `Modify` actions across every decidable entity type, `Pending`, `Decided`, **and
`Blocked`** statuses (resolved decision 1). For each such action, emits one row per decidable field
for that `EntityType` — reusing the existing field-map helpers (step 7's full set) rather than
re-deriving field lists from scratch. For an already-`Decided` action, the `Decision`/`CustomValue`
columns are populated from the new `OriginalDecision` column (step 2/3) — the actual prior choice, not
an inference. `MarkCompletenessAs` is populated from the action's own stored value when set.
Serializes to JSON (`JsonSerializer.Serialize`, typed DTO list) or CSV (step 8's writer) per
`?format=`.

### 10. `POST /api/v1/import/actions/bulk-decide?batchId=&format=csv|json`

**Status:** Not started.

New route in `ImportEndpoints.cs`'s `adminGroup` (`X-Api-Key` required, `AddEndpointFilter<AdminApiKeyFilter>()`,
matching every other staged-action write). Reads the uploaded file, parses per `?format=` (step 8's
reader for CSV, `JsonSerializer.Deserialize<List<T>>` for JSON), groups rows by `ActionId`. Per group:
validates every row (`ActionId` belongs to `batchId`, `EntityType` matches
`ImportActionEntityTypes.All`, `Field` is decidable for that `EntityType`, `Decision` is a recognised
`FieldResolutionChoice` member) before building a `ConflictDecisionRequest` (including
`MarkCompletenessAs` from the group, if any row supplies it) and calling
`IImportActionService.DecideAsync` once per action id — a row-level failure is collected into a
response `errors[]` list (mirroring `POST /import`'s "one bad row never aborts the rest") rather than
aborting the whole file. Deciding a `Blocked` action here works exactly like deciding a `Pending` one
today (the coordinator already accepts it) — no new machinery needed beyond wiring `MarkCompletenessAs`
through.

### 11. Response DTO for bulk-decide

**Status:** Not started.

New response shape (name TBD) carrying counts (rows processed, actions decided) plus the per-row
`errors[]` list (action id / row index, message) — modeled on `ImportResultResponse`'s existing
`Errors` field shape for consistency with the rest of the import surface, not invented from scratch.

### 12. i18n / `ApiMessages` / documentation

**Status:** Not started.

New `ApiMessages` keys for any new error conditions this issue introduces beyond what
`ImportActionNotFoundException`/`ImportActionStateException`/`ImportActionNotDecidableException`/
`UnresolvedFieldConflictException` already cover (e.g. "unknown format", "action not in this batch",
"unrecognised Decision value", "malformed CSV/JSON file") — each translated in all three
`i18ntext/UI.*.json` files in the same commit. `README.md`/`addon/DOCS.md` endpoint tables updated
(requirement 8) — both files' `/import/actions/*` rows need the two new routes added.

### 13. Round-trip test and full expected-test suite

**Status:** Not started.

Implement and pass the full expected-test set: the issue's original eleven tests plus the new ones
from step 1 (Series/Universe decidability, `Blocked`-action bulk-decide, `OriginalDecision` round-trip
fidelity). `ExportImportActions_Json_ReturnsFlatFieldRows`/`..._Csv_...` and
`BulkDecide_JsonRoundTrip_NoErrors`/`..._CsvRoundTrip_NoErrors` (requirement 7 — export's own output
must re-import cleanly with zero errors) prove the two endpoints are true inverses of each other,
across the full nine-entity-type surface, not just Quote/Source.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `GET /import/actions/export` returns every decidable field (JSON) for a batch's Modify actions, including `Blocked` ones | Unit test | `Quotinator.Api.Tests.ExportImportActions_Json_ReturnsFlatFieldRows` |
| 2 | ❌ | `GET /import/actions/export` returns the same rows correctly encoded as CSV, with proper quoting | Unit test | `Quotinator.Api.Tests.ExportImportActions_Csv_ReturnsFlatFieldRows` |
| 3 | ❌ | An already-`Decided` action's exported `Decision`/`CustomValue` reflects the actual original choice, not an inference | Unit test | `SqliteImportActionServiceTests.DecideAsync_PersistsOriginalDecision_ExportReflectsExactPriorChoice` |
| 4 | ❌ | Export → bulk-decide JSON round-trip applies with zero errors | Unit test | `Quotinator.Api.Tests.BulkDecide_JsonRoundTrip_NoErrors` |
| 5 | ❌ | Export → bulk-decide CSV round-trip applies with zero errors | Unit test | `Quotinator.Api.Tests.BulkDecide_CsvRoundTrip_NoErrors` |
| 6 | ❌ | A valid bulk-decide file decides every action it names | Unit test | `Quotinator.Api.Tests.BulkDecide_ValidFile_DecidesEveryAction` |
| 7 | ❌ | A file with one row per entity type — **all nine**: Quote, Source, Character, Person, StageDirection, SoundCue, Conversation, Series, Universe — decides every one of them correctly through the new reverse-mapping path, not just the two the issue text originally named | Unit test | `Quotinator.Api.Tests.BulkDecide_AllNineEntityTypes_DecidesEachCorrectly` (`[DataRow]`/`[DynamicData]` per entity type, so a tenth entity type added later fails this test until it's added to the data set) |
| 7a | ❌ | Conversation specifically — the one entity type with no dedicated `To*DecisionMap` helper (its `description` field is built inline in `DecideAsync`) — decides correctly through bulk-decide's reverse mapping, not silently skipped or misrouted | Unit test | `Quotinator.Api.Tests.BulkDecide_ConversationRow_DecidesDescriptionViaInlineMapping` |
| 8 | ❌ | A file resolving a `Blocked` action (with `MarkCompletenessAs`) applies it correctly | Unit test | `Quotinator.Api.Tests.BulkDecide_BlockedAction_ResolvesWithMarkCompletenessAs` |
| 9 | ❌ | An unknown `ActionId` row reports a row-level error without aborting the rest of the file | Unit test | `Quotinator.Api.Tests.BulkDecide_UnknownActionId_ReportsRowErrorWithoutAbortingOthers` |
| 10 | ❌ | An invalid `EntityType` value returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_InvalidEntityTypeValue_Returns422WithClearMessage` |
| 11 | ❌ | An invalid `Decision` value returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_InvalidDecisionValue_Returns422WithClearMessage` |
| 12 | ❌ | An unknown `Field` name for a given `EntityType` returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_UnknownFieldName_Returns422WithClearMessage` |
| 13 | ❌ | Series: an id-match with a differing `name`/`universeId` stages a `Modify` action, decidable end-to-end | Unit test | `ImportActionPlannerTests.PlanSeriesAsync_IdMatchFound_FieldsDiffer_StagesModifyAction`, `SqliteImportActionServiceTests.DecideAsync_SeriesModify_ResolvesFieldDecisions` |
| 14 | ❌ | Universe: an id-match with a differing `name` stages a `Modify` action, decidable end-to-end | Unit test | `ImportActionPlannerTests.PlanUniverseAsync_IdMatchFound_NameDiffers_StagesModifyAction`, `SqliteImportActionServiceTests.DecideAsync_UniverseModify_ResolvesFieldDecisions` |
| 15 | ❌ | `Quotinator.Converters.Csv` still parses correctly after moving to the shared parser — no behaviour change | Unit test | `Quotinator.Converters.Csv.Tests` full suite unchanged and green |
| 16 | ❌ | `README.md`/`addon/DOCS.md` document both new endpoints | Live (review) | Manual diff review — both tables list `GET /import/actions/export` and `POST /import/actions/bulk-decide` with accurate parameter/status-code descriptions |
| 17 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite passes, 0 warnings, 0 errors |
| 18 | ❌ | T1 — app starts in Visual Studio; export/bulk-decide reachable against a real dev database; schema migration (`OriginalDecision` column) applies cleanly | Live (T1) | Developer's own pass: app starts cleanly; a genuine `review`-policy duplicate import produces a batch that can be exported (JSON and CSV) and re-applied via bulk-decide |
| 19 | ❌ | T2 — Docker smoke test: export a batch (including a `Blocked` action), edit the file, bulk-decide it, confirm the fields (and completeness) reflect the edited decisions | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` then curl-based export → edit → bulk-decide → `GET /import/actions/apply` cycle against a container-run instance, per CLAUDE.md's smoke-test conventions |

---

## Notes

T1 and T2 are both required per this project's blanket rule (no exemption for a non-Razor,
non-migration change).

**Re-verified against the current codebase (2026-07-24), before starting implementation — nearly
every "Investigation findings" claim from when this plan doc was first written turned out stale.** It
was written when only Quote and Source were decidable and `Quotinator.Engine` still existed;
#171–#176 and #206 have since shipped.

**This issue grew substantially in scope during its 2026-07-24 review, by explicit developer
decision, not by inference.** What started as export/bulk-decide plumbing over an already-decidable
surface now also: (a) makes Series/Universe decidable for the first time (comparable in size to #162
or #173 individually), and (b) changes what `DecideAsync` persists so a decision genuinely round-trips
(a real schema change, not just new read/write endpoints). Both were flagged as open questions rather
than assumed, and both were explicitly confirmed rather than picked unilaterally — see "Resolved
decisions" above for the full reasoning behind each.
