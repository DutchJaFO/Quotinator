# #271 — Rename ActionPayload/ConverterOptions classes, add ImportActionFieldRow subclasses (ADR 016 revision)

**Status:** Waiting for release
**GitHub issue:** #271
**Tiers required:** N/A
**Depends on:** #264 (ADR 016 revision this issue implements)

---

## Background

Implements the class-rename half of ADR 016's "Revision — issue #264" section: `Dto` now also covers
JSON stored in a database column (not just on-disk files), and a class genuinely used at two different
serialization boundaries gets a shared unsuffixed base class plus two thin, boundary-specific
subclasses rather than a full duplicate split. Pure C# renames plus one small class split — no schema
impact, no behavioural change. Split out of #264 as its own issue, matching #256's own precedent for
this class of work (#256 itself explicitly deferred these exact classes for this reason — see its
"Also explicitly out of scope" section).

Every call site was investigated directly (not assumed) to resolve the one real design question: which
direction each `ImportActionFieldRow` call site is on.

- **Export/Response direction** (`GET /import/actions/export`): `SqliteImportActionService`/
  `IImportActionService.ExportBatchAsync`, `ImportActionFieldRowMapper.ToCsvRow`,
  `ConflictRuleGenerator.Generate` (fed exclusively by `ExportBatchAsync`'s result — its only caller is
  `ImportRuleEndpoints.cs`'s `/conflict/generate`), and `ImportEndpoints.cs`'s export `.Produces<>()`.
- **Bulk-decide/Dto direction** (`POST /import/actions/bulk-decide`, upload content):
  `SqliteImportActionService`/`IImportActionService.BulkDecideAsync`,
  `ImportActionFieldRowMapper.FromCsvRow`/`BuildRequest`, and `ImportEndpoints.cs`'s
  `ParseJsonRows`/`ParseCsvRows`.

Every production call site is unambiguously one direction or the other — nothing needs to accept the
bare base type. That makes `ImportActionFieldRow` a natural `abstract` class: it is never legitimately
constructed directly, only through one of its two subclasses, and `abstract` makes that a compile-time
guarantee.

## Call-site scope

- **Ten payload classes** (`QuoteActionPayload`, `SourceActionPayload`, `CharacterActionPayload`,
  `PersonActionPayload`, `SeriesActionPayload`, `UniverseActionPayload`, `StageDirectionActionPayload`,
  `SoundCueActionPayload`, `ConversationActionPayload`, `ConversationLinePayload`): declared entirely
  in `ImportActionPlanner.cs`; referenced there plus `SqliteQuoteImportService.cs` and
  `SqliteImportActionService.cs`. Test coverage: `ImportActionPlannerTests.cs`,
  `SqliteImportActionServiceTests.cs`.
- **Three converter-options classes** (`BasicJsonArrayConverterOptions`, `CsvConverterOptions`,
  `RegexArrayConverterOptions`): one declaration + one converter-class reference per plugin project.
  Test coverage: each plugin's own test project, plus `RepositoryStructureTests.cs`
  (`Quotinator.Api.Tests`), which constructs two of the three directly, not just a naming check.
- **`ImportActionFieldRow` split**: declaration in `ImportActionFieldRow.cs`; call sites in
  `ImportActionFieldRowMapper.cs`, `ConflictRuleGenerator.cs`, `SqliteImportActionService.cs`,
  `IImportActionService.cs`, `ImportEndpoints.cs`. Test coverage:
  `ImportActionFieldRowMapperTests.cs`, `ConflictRuleGeneratorTests.cs`,
  `SqliteImportActionServiceTests.cs`, `ImportActionEndpointsTests.cs`, `ImportRuleEndpointsTests.cs`,
  `FakeImportActionService.cs`.
- **Two stale doc comments**: `QuoteConflictFieldsDto`'s and `ImportActionFieldRow`'s own doc comments
  still name the pre-#253 `System_ImportConflicts`/`System_ImportActions` tables instead of the current
  `Import_Action` table.

---

## Steps

### 1. Rename the ten `*ActionPayload`/`ConversationLinePayload` classes

**Status:** ✅ Done

Each gains `Dto`: `QuoteActionPayloadDto`, `SourceActionPayloadDto`, `CharacterActionPayloadDto`,
`PersonActionPayloadDto`, `SeriesActionPayloadDto`, `UniverseActionPayloadDto`,
`StageDirectionActionPayloadDto`, `SoundCueActionPayloadDto`, `ConversationActionPayloadDto`,
`ConversationLinePayloadDto`. Same mechanical pattern for all ten, applied file-by-file; compiler
(CS0246) finds every miss. Update `ImportActionPlanner.cs`, `SqliteQuoteImportService.cs`,
`SqliteImportActionService.cs`, and both matching test files.

### 2. Rename the three converter-options classes

**Status:** ✅ Done

`BasicJsonArrayConverterOptions` → `BasicJsonArrayConverterOptionsDto`, `CsvConverterOptions` →
`CsvConverterOptionsDto`, `RegexArrayConverterOptions` → `RegexArrayConverterOptionsDto`. Update each
declaration, its converter class, its plugin's test project, and `RepositoryStructureTests.cs`.

### 3. Split ImportActionFieldRow into an abstract base plus Response/Dto subclasses

**Status:** ✅ Done — one additional finding: `SqliteImportActionServiceTests.ExportThenBulkDecide_UnmodifiedExportedRows_RoundTripsWithZeroErrors`
reused an exported `Response` row directly as `BulkDecideAsync`'s `Dto`-typed input in-memory — a
pattern no real caller can do once the split enforces the type boundary (every real caller goes
through the wire, JSON or CSV). Fixed by mapping the exported rows into `ImportActionFieldRowDto`
explicitly in the test, matching what a real caller's own serialize/deserialize round-trip does.

`ImportActionFieldRow` becomes `abstract`, keeping its six existing properties unchanged. Add two new
sealed subclasses in the same file, each with its own `<summary>` (CS1591 is enforced in
`Quotinator.Core`): `ImportActionFieldRowResponse : ImportActionFieldRow` (export) and
`ImportActionFieldRowDto : ImportActionFieldRow` (bulk-decide upload). Update every production call
site to construct/accept whichever subclass matches its own direction (see Background above), then the
matching test files — each test file's own helper methods/inline construction follow whichever
direction that specific test exercises. The JSON-wire-format round-trip test
(`SqliteImportActionServiceTests.ExportThenBulkDecide_ViaJsonWireFormat_RoundTripsWithZeroErrors`)
deserializes exported/serialized JSON as `List<ImportActionFieldRowDto>` — proving the wire shape is
compatible across the split.

### 4. Fix two stale doc comments

**Status:** ✅ Done

`QuoteConflictFieldsDto`'s doc comment: `System_ImportConflicts` → `Import_Action`.
`ImportActionFieldRow`'s doc comment: `System_ImportActions` → `Import_Action`.

### 5. Full solution build and test sweep

**Status:** ✅ Done — 0 warnings, 0 errors; 625/625 tests passed; grep sweep for all thirteen old
names returned only legitimate base-class declarations/inherited-member doc-comment references.

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green.
Word-boundary grep for all thirteen old class names across `src/`/`tests/` to confirm nothing was
missed, excluding frozen historical text (git history, already-shipped changelog entries).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Ten `*ActionPayload`/`ConversationLinePayload` classes renamed to `Dto`-suffixed forms, all call sites updated | Unit test | `Quotinator.Core.Tests` (`ImportActionPlannerTests.cs`, `SqliteImportActionServiceTests.cs`) passes unchanged |
| 2 | ✅ | Three converter-options classes renamed to `Dto`-suffixed forms, all call sites updated | Unit test | Each converter's own test project plus `Quotinator.Api.Tests`' `RepositoryStructureTests.cs` passes unchanged |
| 3 | ✅ | `ImportActionFieldRow` is `abstract`; `ImportActionFieldRowResponse`/`ImportActionFieldRowDto` added; every call site uses the subclass matching its own direction | Unit test | `Quotinator.Core.Tests` and `Quotinator.Api.Tests`' import-action/rule endpoint tests pass unchanged |
| 4 | ✅ | Two stale doc comments corrected | Live | Read both doc comments, confirm they name `Import_Action` |
| 5 | ✅ | No remaining reference to any of the thirteen old names anywhere in `src/`/`tests/` (excluding frozen historical text) | Live | Word-boundary grep for each of the thirteen old names, excluding immediate `Dto`/`Response` matches, across `src/`, `tests/` — returns nothing |
| 6 | ✅ | Build clean | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors; full test suite passes unchanged (625/625) |
