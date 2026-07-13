# #163 — Bulk-decide a staged import batch via file export/import, CSV and JSON (Phase 1 of #153)

**Status:** Planning
**GitHub issue:** #163
**Tiers required:** T1, T2
**Depends on:** #162, #149, #154 (per overview.md's dependency map)

---

## Spec requirements (from the GitHub issue)

1. New `GET /api/v1/import/actions/export?batchId=&format=csv|json` endpoint (default `json`)
   returns every decidable field across the batch's actions in the flat
   `ActionId,EntityId,EntityType,Field,ExistingValue,IncomingValue,Decision,CustomValue` row shape —
   both genuinely-`Pending` and already-`Decided` fields are included (an operator can see and revise
   an existing decision, not only decide a fresh one). No `X-Api-Key` required, matching
   `GET /import/actions`'s existing public-read precedent.
2. New `POST /api/v1/import/actions/bulk-decide?batchId=&format=csv|json` endpoint accepts the edited
   file back, groups rows by `ActionId`, and applies each action's decision via the existing
   `IImportActionService.DecideAsync` (both `Quote` and, per #162, `Source` rows) — reuses existing
   per-field validation, no new validation logic invented.
3. CSV parsing/writing uses a proper CSV library (quoting, embedded commas/quotes in
   `ExistingValue`/`IncomingValue` handled correctly) — not naive string splitting.
4. Data-domain validation: `Decision` must be a recognised `FieldResolutionChoice` member
   (`Keep`/`Replace`/`Custom`); an unrecognised value is a clear per-row error, not silently
   defaulted.
5. Consumer-domain (Engine) validation: `EntityType` must be one of `ImportActionEntityTypes.All`;
   `Field` must be a valid, currently-decidable field name for that `EntityType` (Quote's fixed field
   set, or Source's fields per #162). An entity type with no decidable fields at all
   (Character/Person/Conversation/StageDirection/SoundCue, until their own future issues land)
   reports the existing `ImportActionNotDecidableException` message per row.
6. A row-level error (unknown `ActionId`, action not part of the requested `batchId`, invalid
   `EntityType`, invalid `Decision` value, unknown `Field` name, action already applied) reports which
   row failed without aborting the rest of the file — matches `POST /import`'s existing "one bad row
   never aborts the rest" model.
7. `GET .../export`'s output (either format) round-trips through `POST .../bulk-decide` unmodified
   with zero errors — baseline correctness check before any real edits are made.
8. `README.md`/`addon/DOCS.md` endpoint tables updated in the same commit.

---

## Investigation findings (current codebase, as of this session)

No export/bulk-decide scaffolding exists anywhere in `src/` today — `grep -rn "export"` across the
solution returns only unrelated Bootstrap JS library hits. This issue's endpoints, DTOs, and CSV
reader/writer are entirely new code.

**Dependencies confirmed done, not yet released.** #162, #149, #154 are all `Waiting for release` on
this branch — nothing shipped requires the deferred-scope caution those plan docs used for their own
changes, but #163 itself is genuinely new work on top of already-landed (unreleased) code.

**`ConflictDecisionRequest` (`src/Quotinator.Engine/Models/ConflictDecisionRequest.cs`)** already
carries a flat, per-entity-prefixed property per decidable field — `QuoteText`/`OriginalLanguage`/
`Source`/`Date`/`Character`/`Author`/`Type`/`Genres` for Quote, `SourceTitle`/`SourceType`/
`SourceDate` for Source (added by #162), plus the entity-agnostic `MarkCompletenessAs` override
(#165). `SqliteImportActionService.ToDecisionMap`/`ToSourceDecisionMap`
(`src/Quotinator.Engine/Services/SqliteImportActionService.cs:905-944`) are the only place that maps
this request shape into `FieldMergeResolver`'s raw field-map keys (`quoteText`, `originalLanguage`,
`source`, `date`, `character`, `author`, `type`, `genres` for Quote; `title`, `type`, `date` for
Source). #163's bulk-decide endpoint needs the **reverse** mapping — a `(EntityType, Field)` string
pair back into the correct `ConflictDecisionRequest` property — which does not exist anywhere today
and is new code this issue must add.

**Only `Modify` actions are ever decidable.** `Add` actions are always staged already-`Decided` (an
Add is never ambiguous — see `ImportActionPlanner`'s remarks, confirmed by
`ImportActionNotDecidableException`'s own doc comment). So the export naturally only ever emits rows
for `Quote`/`Source` actions whose `ActionType` is `Modify` — this should be stated explicitly in the
implementation, not left implicit.

**`ImportActionStatus` has a fourth relevant value, `Blocked` (#165/#168), that the issue text never
mentions** — see "Open questions" below; the issue was filed before #165/#168 introduced it, and
`ImportActionResolutionCoordinator.DecideAsync` (`src/Quotinator.Data/Import/ImportActionResolutionCoordinator.cs:41-56`)
already accepts a decide call against a `Blocked` action (it only rejects `Applied`/`Discarded`), so
the underlying machinery already supports deciding a `Blocked` row today.

**The original per-field `Keep`/`Replace`/`Custom` choice is not persisted anywhere once an action is
`Decided`.** `SqliteImportActionService.DecideAsync` calls `FieldMergeResolver.ResolveWithDecisions`
immediately and stores only the **resolved** payload via `_coordinator.DecideAsync(actionId,
JsonSerializer.Serialize(resolvedPayload), ...)` — the caller's original `ConflictDecisionRequest`
(which field chose Keep vs Replace vs Custom) is never written to `MergedFields` or anywhere else. See
"Open questions" below — this is a real gap in requirement 1's "an operator can... revise a decision
already made" as literally stated.

**`ImportActionNotDecidableException`'s message text is already known-stale** (tracked separately as
issue #170, per `overview.md`'s dependency map: "no dependencies; independent bug, already wrong
today"): it reads "only 'Quote' actions support a decision," which is no longer accurate now that
Source Modify actions are decidable too (#162). #163 requirement 5 explicitly reuses this exception's
message for non-decidable entity types, so #163 inherits whichever wording #170 leaves behind —
sequencing (#170 before or after #163) should be confirmed with the developer, since #163's own new
test (`BulkDecide_NonDecidableEntityType_ReportsNotDecidable`) will otherwise assert against
currently-wrong text.

**No CSV writer exists anywhere in the codebase today**, and the one existing CSV reader
(`CsvLineParser` in `src/Quotinator.Converters.Csv/CsvLineParser.cs`) is `internal` to
`Quotinator.Converters.Csv` with no `InternalsVisibleTo` grant reaching `Quotinator.Engine` or
`Quotinator.Api` — it cannot be called from where #163's code needs to live. It is a genuine,
already-proven precedent, though: a minimal hand-rolled RFC 4180 parser (quoted fields, embedded
commas, embedded newlines, escaped `""` quotes), with an explicit doc comment reasoning "No external
dependency is justified for a flat, fixed-column format." No `CsvHelper` (or any CSV NuGet package)
reference exists anywhere in the solution. This directly informs requirement 3 ("uses a proper CSV
library... not naive string splitting") — see "Open questions" below for the unresolved placement
decision.

**JSON parsing policy applies.** Per `CLAUDE.md`'s JSON parsing policy, the JSON encoding of the flat
row shape must be a typed DTO deserialized via `JsonSerializer.Deserialize<List<T>>` — never manual
`JsonNode` walking. `FieldResolutionChoice` is already `[JsonConverter(typeof(JsonStringEnumConverter))]`-decorated,
so the DTO's `Decision` property can be typed directly as `FieldResolutionChoice?` for the JSON path
(case-insensitive member matching is automatic) while still needing a explicit string-to-enum
validation step for the CSV path, which has no equivalent built-in enum converter.

**`Sql.SystemImportActions`/`ISystemImportActionReader.GetAllForBatchAsync`** already exists (used
throughout `SqliteImportActionService`, e.g. `ClearStaleAddTargetsAsync`,
`ComputeRelatedActionIdsAsync`) and is the natural data source for the export endpoint — no new SQL
query is needed to enumerate a batch's actions; the new work is entirely in shaping the flat
per-field-row output and the reverse bulk-decide mapping.

---

## Open questions (found during investigation, not resolved here)

These are genuine gaps between the (older, pre-#165/#168) issue text and the current codebase. Per
the task's own instruction, they are flagged rather than silently decided:

1. **Does the export/bulk-decide file include `Blocked` actions, or only `Pending`/`Decided`?** The
   issue text only mentions `Pending`/`Decided`. `Blocked` (#165/#168) didn't exist when #163 was
   filed, but the coordinator's `DecideAsync` already accepts a decide call against a `Blocked`
   action today. Excluding `Blocked` rows from the export would silently leave a real,
   already-supported bulk-decide use case (resolving a `Complete`-row hold) out of scope; including
   them needs an explicit decision on whether `MarkCompletenessAs` also needs a file-format column,
   which the issue's CSV/JSON shape has no column for today.
2. **How does the export represent an already-`Decided` action's original per-field choice?** As
   found above, only the resolved value is persisted — the original `Keep`/`Replace`/`Custom`
   selection is not stored anywhere once decided. A `Decision` column value can be *inferred*
   heuristically (`Keep` if resolved == existing, `Replace` if resolved == incoming, `Custom`
   otherwise) but this is lossy and ambiguous exactly when existing and incoming already agree (an
   auto-resolved unambiguous field has no real "choice" to infer at all). This needs a decision before
   implementation: infer-and-document-the-caveat, treat every already-`Decided` row's `Decision`
   column as always blank/informational-only (breaking the "operator can revise" requirement), or
   change what `DecideAsync`/`MarkDecidedAsync` persist so the original decision is preserved (a
   larger change touching #149/#154's shipped-but-unreleased coordinator).
3. **Where does the CSV reader/writer live?** Three options, none picked here: (a) extract
   `CsvLineParser`'s logic into a new shared, non-`internal` location (e.g. `Quotinator.Data` or a
   small new `Quotinator.Csv` project) and add a matching writer, reused by both
   `Quotinator.Converters.Csv` and this issue; (b) duplicate an equivalent minimal parser/writer pair
   directly in `Quotinator.Engine`/`Quotinator.Api`, accepting the duplication; (c) add a `CsvHelper`
   NuGet dependency, which would be the first CSV *library* dependency in a project whose own
   `Quotinator.Converters.Csv` doc comment explicitly argued against needing one. Given CLAUDE.md's
   "do not add NuGet packages without a clear reason — keep the dependency footprint small," option
   (a) looks most consistent with existing precedent, but this is a real design decision for the
   assigned implementer/developer to make, not inferred here.
4. **Which project hosts the new DTOs and endpoint logic?** Following #149/#154's placement
   precedent (`ConflictDecisionRequest`/`FieldDecision` in `Quotinator.Engine.Models` because they
   need `FieldResolutionChoice`, a `Quotinator.Data` type Core cannot reference), the flat row DTO and
   any bulk-decide orchestration service almost certainly belong in `Quotinator.Engine`, with the
   endpoint itself in `Quotinator.Api/Endpoints/ImportEndpoints.cs` alongside the rest of `/import/actions/*`
   — flagged here for confirmation rather than assumed silently, since it does affect where CSV
   read/write code (open question 3) would need to be referenced from.
5. **Rate limiting / auth tier for the new endpoints.** The issue states export is public
   (`GET`, matching `GET /import/actions`'s precedent) and bulk-decide requires `X-Api-Key` (matching
   every other staged-action write). This plan doc adopts that as stated — no ambiguity here, listed
   only for completeness against the "Rate limiting is universal" project rule (`RequireRateLimiting`
   on both new endpoints, no exceptions).

---

## Steps

### 1. Write the red tests

**Status:** Not started.

Add the eleven test methods from the issue's "Expected tests" table to a new
`tests/Quotinator.Api.Tests/Endpoints/ImportActionExportEndpointsTests.cs` (or similarly named file,
grouped with the existing `ImportActionEndpointsTests.cs`). Confirm each fails for the expected reason
(endpoint doesn't exist yet) before any implementation code is written.

### 2. Resolve open questions 1–4 with the developer before implementation

**Status:** Not started.

Per this project's "gap resolution is the developer's decision" rule — do not silently pick an
answer. In particular, question 2 (how a `Decided` action's original choice is represented) has real
implementation-shape consequences (whether `MarkDecidedAsync`'s stored payload needs to change) that
should be settled before step 3 begins, not discovered mid-implementation.

### 3. Define the flat row DTO and the field-name ↔ `ConflictDecisionRequest` mapping

**Status:** Not started.

New DTO (name TBD, e.g. `ImportActionFieldRow`) in `Quotinator.Engine.Models`:
`ActionId` (Guid), `EntityId` (string), `EntityType` (string), `Field` (string), `ExistingValue`
(string?), `IncomingValue` (string?), `Decision` (`FieldResolutionChoice?`), `CustomValue` (string?).
List-valued fields (Quote's `genres`) serialize as a single delimited string in `ExistingValue`/
`IncomingValue`/`CustomValue` (issue specifies `;`-separated, e.g. `drama;comedy`) — needs a
dedicated encode/decode helper, since every other field is a plain scalar.

New reverse-mapping helper (per requirement 5's "valid, currently-decidable field name for that
`EntityType`" check) — the mirror image of `ToDecisionMap`/`ToSourceDecisionMap`
(`SqliteImportActionService.cs:905-944`): given `(EntityType, Field, Decision, CustomValue)`, either
produces a `FieldDecision`/`GenresFieldDecision` to slot into a `ConflictDecisionRequest`, or throws/
reports a row-level error for an unrecognised field name. Reuses `ImportActionEntityTypes.All` for the
`EntityType` validity check (requirement 5); the non-`Quote`/`Source` branch reports the same message
`ImportActionNotDecidableException` already produces (subject to #170's wording, see "Investigation
findings" above).

### 4. CSV read/write (per resolved open question 3)

**Status:** Not started.

Depending on the decision from step 2: extract/share `CsvLineParser`'s logic (most consistent with
existing precedent), duplicate a minimal equivalent, or add a CSV library dependency. A CSV **writer**
is new either way — nothing in the codebase writes CSV output today. Both directions need to handle
quoting for `ExistingValue`/`IncomingValue`/`CustomValue`, which may contain commas, quotes, or
newlines (a quote's own text can contain any of these).

### 5. `GET /api/v1/import/actions/export?batchId=&format=csv|json`

**Status:** Not started.

New route in `ImportEndpoints.cs`'s `publicGroup` (no `X-Api-Key`, matching `GET /actions`'s
precedent), `RequireRateLimiting(RateLimitPolicies.Admin)` per the project's universal rate-limiting
rule. Fetches the batch's actions via the existing `ISystemImportActionReader.GetAllForBatchAsync`,
filters to `Quote`/`Source` `Modify` actions (per "Only `Modify` actions are ever decidable" above),
and whichever status set is decided in step 2 (`Pending`/`Decided`, or also `Blocked`). For each such
action, emits one row per decidable field for that `EntityType` — reusing the existing field-map
helpers (`QuoteFieldMerge.ToFieldMap`, `SqliteImportActionService`'s private `ToFieldMap(SourceActionPayload)`,
or equivalents made accessible) rather than re-deriving field lists from scratch. Serializes to JSON
(`JsonSerializer.Serialize`, typed DTO list) or CSV (step 4's writer) per `?format=`.

### 6. `POST /api/v1/import/actions/bulk-decide?batchId=&format=csv|json`

**Status:** Not started.

New route in `ImportEndpoints.cs`'s `adminGroup` (`X-Api-Key` required, `AddEndpointFilter<AdminApiKeyFilter>()`,
matching every other staged-action write). Reads the uploaded file, parses per `?format=` (step 4's
reader for CSV, `JsonSerializer.Deserialize<List<T>>` for JSON), groups rows by `ActionId`. Per group:
validates every row (`ActionId` belongs to `batchId`, `EntityType` matches
`ImportActionEntityTypes.All`, `Field` is decidable for that `EntityType`, `Decision` is a recognised
`FieldResolutionChoice` member) before building a `ConflictDecisionRequest` and calling
`IImportActionService.DecideAsync` once per action id — a row-level failure is collected into a
response `errors[]` list (mirroring `POST /import`'s "one bad row never aborts the rest") rather than
aborting the whole file.

### 7. Response DTO for bulk-decide

**Status:** Not started.

New response shape (name TBD) carrying counts (rows processed, actions decided) plus the per-row
`errors[]` list (action id / row index, message) — modeled on `ImportResultResponse`'s existing
`Errors` field shape for consistency with the rest of the import surface, not invented from scratch.

### 8. i18n / `ApiMessages` / documentation

**Status:** Not started.

New `ApiMessages` keys for any new error conditions this issue introduces beyond what
`ImportActionNotFoundException`/`ImportActionStateException`/`ImportActionNotDecidableException`/
`UnresolvedFieldConflictException` already cover (e.g. "unknown format", "action not in this batch",
"unrecognised Decision value", "malformed CSV/JSON file") — each translated in all three
`i18ntext/UI.*.json` files in the same commit. `README.md`/`addon/DOCS.md` endpoint tables updated
(requirement 8) — both files' `/import/actions/*` rows need the two new routes added.

### 9. Round-trip test and full expected-test suite

**Status:** Not started.

Implement and pass all eleven tests from the issue's "Expected tests" table, including
`ExportImportActions_Json_ReturnsFlatFieldRows`/`..._Csv_...` and
`BulkDecide_JsonRoundTrip_NoErrors`/`..._CsvRoundTrip_NoErrors` (requirement 7 — export's own output
must re-import cleanly with zero errors, proving the two endpoints are true inverses of each other).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `GET /import/actions/export` returns every decidable field (JSON) for a batch's Quote/Source Modify actions | Unit test | `Quotinator.Api.Tests.ExportImportActions_Json_ReturnsFlatFieldRows` |
| 2 | ❌ | `GET /import/actions/export` returns the same rows correctly encoded as CSV, with proper quoting | Unit test | `Quotinator.Api.Tests.ExportImportActions_Csv_ReturnsFlatFieldRows` |
| 3 | ❌ | Export → bulk-decide JSON round-trip applies with zero errors | Unit test | `Quotinator.Api.Tests.BulkDecide_JsonRoundTrip_NoErrors` |
| 4 | ❌ | Export → bulk-decide CSV round-trip applies with zero errors | Unit test | `Quotinator.Api.Tests.BulkDecide_CsvRoundTrip_NoErrors` |
| 5 | ❌ | A valid bulk-decide file decides every action it names | Unit test | `Quotinator.Api.Tests.BulkDecide_ValidFile_DecidesEveryAction` |
| 6 | ❌ | A file mixing Quote and Source rows decides both correctly | Unit test | `Quotinator.Api.Tests.BulkDecide_MixedQuoteAndSourceRows_DecidesBoth` |
| 7 | ❌ | An unknown `ActionId` row reports a row-level error without aborting the rest of the file | Unit test | `Quotinator.Api.Tests.BulkDecide_UnknownActionId_ReportsRowErrorWithoutAbortingOthers` |
| 8 | ❌ | An invalid `EntityType` value returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_InvalidEntityTypeValue_Returns422WithClearMessage` |
| 9 | ❌ | A non-decidable `EntityType` (Character/Person/Conversation/StageDirection/SoundCue) reports "not decidable" per row | Unit test | `Quotinator.Api.Tests.BulkDecide_NonDecidableEntityType_ReportsNotDecidable` |
| 10 | ❌ | An invalid `Decision` value returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_InvalidDecisionValue_Returns422WithClearMessage` |
| 11 | ❌ | An unknown `Field` name for a given `EntityType` returns `422` with a clear message | Unit test | `Quotinator.Api.Tests.BulkDecide_UnknownFieldName_Returns422WithClearMessage` |
| 12 | ❌ | `README.md`/`addon/DOCS.md` document both new endpoints | Live (review) | Manual diff review — both tables list `GET /import/actions/export` and `POST /import/actions/bulk-decide` with accurate parameter/status-code descriptions |
| 13 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite passes, 0 warnings, 0 errors |
| 14 | ❌ | T1 — app starts in Visual Studio; export/bulk-decide reachable against a real dev database | Live (T1) | Developer's own pass: app starts cleanly; a genuine `review`-policy duplicate import produces a batch that can be exported (JSON and CSV) and re-applied via bulk-decide |
| 15 | ❌ | T2 — Docker smoke test: export a batch, edit the file, bulk-decide it, confirm the quote/source fields reflect the edited decisions | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` then curl-based export → edit → bulk-decide → `GET /import/actions/apply` cycle against a container-run instance, per CLAUDE.md's smoke-test conventions |

---

## Notes

T1 and T2 are both required per this project's blanket rule (no exemption for a non-Razor,
non-migration change — confirmed explicitly for #168, applies equally here).

**This plan doc surfaces five genuine open questions (see "Open questions" above) that must be
resolved with the developer before implementation starts** — this issue is older than several of its
own now-shipped-but-unreleased prerequisites (#162, #165, #168), and its text was never updated to
account for `Blocked` status, the loss of the original per-field decision at `Decided` time, or the
CSV-library placement decision `Quotinator.Converters.Csv`'s existing precedent raises. None of these
are resolved unilaterally in this document — they are flagged for the developer's decision, per this
project's "gap resolution is the developer's decision" rule.

**Requirement 1's "already-Decided… operator can revise" claim may not be fully achievable as
written** without either accepting a lossy inference of the original decision or changing what
`DecideAsync`/`MarkDecidedAsync` persist (open question 2) — this is the single biggest scope risk in
the issue as currently filed and should be the first thing confirmed with the developer.
