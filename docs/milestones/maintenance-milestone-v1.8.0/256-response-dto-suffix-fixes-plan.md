# #256 — Fix Response/Dto/class-suffix violations

**Status:** Waiting for release
**GitHub issue:** #256
**Tiers required:** N/A
**Depends on:** Nothing

---

## Background

Implements the `Request`/`Response`/`Dto` suffix half of
[ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md) for seventeen
classes total: the five specific classes named while planning #227, plus twelve more found live
during #256's own pre-implementation review (2026-08-02) — see "Additional classes found live"
below. Pure C# renames — no schema impact, no behavioural change, independent of #253/#254 (the
table/entity renames).

`PagedResult<T>`/`FilteredQuoteResult<T>` are explicitly out of scope — ADR 016 deferred the generic
`BaseResponse<T>`/`BaseRequest<T>` base-type design; that is a larger follow-up, not a simple rename,
and is not part of this issue's Definition of Done.

**Also explicitly out of scope, deferred to follow-up issues rather than folded in here** (found
during the same 2026-08-02 review, but not simple renames):
- A family of classes with a genuinely ambiguous `Dto` boundary — JSON blobs stored in a database
  text column rather than an on-disk file (`QuoteActionPayload` and eight sibling `*ActionPayload`
  classes in `ImportActionPlanner.cs`), a class serving two boundaries at once
  (`ImportActionFieldRow` — both an HTTP response's bare-array element and an uploaded file's row
  shape), and three converter-options classes deserialized from either a manifest.json file or an
  HTTP import request's `converterOptions` field (`BasicJsonArrayConverterOptions`,
  `CsvConverterOptions`, `RegexArrayConverterOptions`). ADR 016's `Dto` row currently reads "today,
  exclusively on-disk JSON file shapes," which doesn't cleanly cover any of these — needs an ADR
  clarification before a rename decision, not a plan-doc judgment call.
- `GET /api/v1/admin/audit` returns `Quotinator.Data.Entities.AuditEntryEntity` directly (via
  `PagedItems<AuditEntryEntity>`), with no `Response`-suffixed DTO layer at all — arguably the exact
  boundary conflation ADR 016 exists to prevent, but building that mapping is new code, not a pure
  rename, so it doesn't fit this issue's "pure renames only" Definition of Done.

## The five original renames

| # | Class today | File today | Class after | File after | Why |
|---|---|---|---|---|---|
| 1 | `SeedFilePreviewResponse` | `src/Quotinator.Core/Models/SeedPreviewResponse.cs` (same file as its parent) | `SeedFilePreview` | *(unchanged — stays in the same file as `SeedPreviewResponse`)* | It is a member type — `SeedPreviewResponse.Files` is `IReadOnlyList<SeedFilePreviewResponse>` — never bound directly to an endpoint. ADR 016 reserves `Response` for the literal top-level type an endpoint returns; `SeedPreviewResponse` itself already carries that suffix correctly. |
| 2 | `SourceQuote` | `src/Quotinator.Core/Import/SourceQuote.cs` | `SourceQuoteDto` | `src/Quotinator.Core/Import/SourceQuoteDto.cs` | Deserialized via `JsonSerializer.Deserialize<T>` from `data/sources/*.json` — a JSON-file-shape class, the exact case CLAUDE.md's JSON parsing policy names as needing the `Dto` suffix. |
| 3 | `SourceQuoteTranslation` | `src/Quotinator.Core/Import/SourceQuoteTranslation.cs` | `SourceQuoteTranslationDto` | `src/Quotinator.Core/Import/SourceQuoteTranslationDto.cs` | Same reasoning as #2 — nested translation shape inside the same JSON file format. |
| 4 | `ImportRequestSettingsDto` | `src/Quotinator.Data/Import/ImportRequestSettingsDto.cs` | `ImportSettingsDto` | `src/Quotinator.Data/Import/ImportSettingsDto.cs` | Drops the erroneous `Request` — it is a JSON settings-blob shape (the `settings` multipart field of `POST /api/v1/import`), not itself an HTTP request body type, and its base class `SourceImportSettingsDto` already omits `Request` for the same reason. |
| 5 | `ChangelogRoot` | `src/Quotinator.Changelog/Models/ChangelogRoot.cs` | `ChangelogRootDto` | `src/Quotinator.Changelog/Models/ChangelogRootDto.cs` | Deserialized via `JsonSerializer.Deserialize<ChangelogRoot>` in `ChangelogService.cs` from `changelog.*.json` — same JSON-file-shape reasoning as #2/#3. `Quotinator.Changelog` is a separate project from `Quotinator.Core`/`Quotinator.Data`, but ADR 016's suffix rule is project-wide — confirmed in scope by the developer (2026-08-01) rather than deferred to a separate issue. |

`ChangelogRoot` was already listed in #256's own GitHub issue body from the start — an earlier draft of
this plan doc misread the issue body and claimed it was missing, which was a misreading, not a real
scope gap; corrected 2026-08-01. See Scope changes below.

## Additional classes found live (2026-08-02)

A pre-implementation review swept every `JsonSerializer.Deserialize<T>`/`JsonElement.Deserialize<T>`
call site across `src/` (the same technique ADR 016's own research used) to check for `Dto`-boundary
misses the original five-class list didn't catch. Found twelve — all in the same
"deserialized from an on-disk JSON file" boundary as `SourceQuote`/`ChangelogRoot`, just not swept the
first time because most were added by later issues (#162/#163/#173/#175/#180) after ADR 016's own
2026-08-01 research pass:

| # | Class today | File today | Class after | Why |
|---|---|---|---|---|
| 6 | `ConflictResolutionRuleFile` | `src/Quotinator.Data/Import/ConflictResolutionRuleFile.cs` | `ConflictResolutionRuleFileDto` | Own doc comment: "The on-disk shape of a per-source conflict-resolution rule file (#181)." Deserialized via `JsonSerializer.Deserialize<ConflictResolutionRuleFile>`. |
| 7 | `SourceAliasRuleFile` | `src/Quotinator.Data/Import/SourceAliasRuleFile.cs` | `SourceAliasRuleFileDto` | Own doc comment: "The on-disk shape of a per-source title-alias file (#181)." Same reasoning as #6. |
| 8 | `ParsedSourceFile` | `src/Quotinator.Core/Import/ParsedSourceFile.cs` | `ParsedSourceFileDto` | The top-level container `SourceQuoteFileReader.TryParseExtended` produces — "the full set of sections a Quotinator source file (extended format) can contain." The literal top-level shape of the extended source-file format; missed even though its own member `Quotes` is `IReadOnlyList<SourceQuote>`, the very type #2 already renames. |
| 9 | `SourceEntry` | `src/Quotinator.Core/Import/SourceEntry.cs` | `SourceEntryDto` | "A Source declaration deserialized from a Quotinator source file's `sources` section." |
| 10 | `PersonEntry` | `src/Quotinator.Core/Import/PersonEntry.cs` | `PersonEntryDto` | "An explicit Person declaration deserialized from a Quotinator source file's `people` section (#173)." |
| 11 | `CharacterEntry` | `src/Quotinator.Core/Import/CharacterEntry.cs` | `CharacterEntryDto` | "A Character declaration deserialized from a Quotinator source file's `characters` section." |
| 12 | `SourceStageDirection` | `src/Quotinator.Core/Import/SourceStageDirection.cs` | `SourceStageDirectionDto` | "...deserialized from a Quotinator source file's `stageDirections` section." |
| 13 | `SourceSoundCue` | `src/Quotinator.Core/Import/SourceSoundCue.cs` | `SourceSoundCueDto` | "...deserialized from a Quotinator source file's `soundCues` section." |
| 14 | `SourceConversation` | `src/Quotinator.Core/Import/SourceConversation.cs` | `SourceConversationDto` | "...deserialized from a Quotinator source file's `conversations` section." |
| 15 | `SourceConversationLine` | `src/Quotinator.Core/Import/SourceConversationLine.cs` | `SourceConversationLineDto` | "One position in a `SourceConversation`'s ordered line list" — a nested member of #14, one level deeper. Unlike the `Request`/`Response` rule, `Dto` applies uniformly through the whole parse tree (matching the existing precedent of `ManifestFileEntryDto`/`ManifestGithubDto`/`ManifestPolicyDto`, all members of `ManifestDto`, all already `Dto`-suffixed) — a member is not exempted the way it would be for `Request`/`Response`. |
| 16 | `SeriesEntry` | `src/Quotinator.Core/Import/SeriesEntry.cs` | `SeriesEntryDto` | "An explicit Series declaration deserialized from a Quotinator source file's `series` section (#180)." |
| 17 | `UniverseEntry` | `src/Quotinator.Core/Import/UniverseEntry.cs` | `UniverseEntryDto` | "An explicit Universe declaration deserialized from a Quotinator source file's `universe` section (#180)." |

Numbering continues from the original five (1–5) so every rename in this issue has one stable number
across both tables; renumbered starting at 6 rather than restarting at 1 to avoid two different
classes both being called "#1" in cross-references below.

## Call-site scope

- **#2/#3 (`SourceQuote`/`SourceQuoteTranslation`)**: the original plan doc said 21 files, confirmed
  via a `src/`-only grep on 2026-08-01. Re-confirmed 2026-08-02 including `tests/`: 28 files total
  (`\bSourceQuote\b` word-boundary grep across `src/`+`tests/`) — the higher count is `tests/`
  coverage the original pass excluded, not scope drift. Spans `Quotinator.Core.Import`,
  `Quotinator.Core.Services`, `Quotinator.Core.Database`, three converter plugin projects
  (`Quotinator.Converters.RegexArray`, `Quotinator.Converters.Csv`, `Quotinator.Converters.BasicJsonArray`)
  that construct/return `SourceQuote` instances as their `IQuoteSourceConverter.ConvertAsync` output,
  and their three matching test projects. All are IDE-mechanical renames — the compiler (CS0246) finds
  every miss.
- **#1 (`SeedFilePreviewResponse`)**: 2 files — its own declaration and `AdminEndpoints.cs`, which
  builds `SeedFilePreviewResponse` instances for the seed-preview endpoint response.
- **#4 (`ImportRequestSettingsDto`)**: the original plan doc listed `ManifestFileEntryDto` as a call
  site; re-confirmed 2026-08-02 that `ManifestFileEntryDto.cs` no longer references the type at all
  (drifted since 2026-08-01), while two test files do and weren't listed
  (`FakeQuoteImportService.cs`, `QuoteImportServiceTests.cs`) — 7 files total now: its own
  declaration, `SourceImportSettingsDto` (base class doc-comment reference only),
  `ImportRequestSettingsParser`, `SqliteQuoteImportService`, `IQuoteImportService`, and the two test
  files.
- **#5 (`ChangelogRoot`)**: 3 files — its own declaration, `ChangelogService.cs` (2 references:
  a local variable type and the `JsonSerializer.Deserialize<ChangelogRoot>` call), and
  `scripts/changelog-import.csx` (constructs a `ChangelogRoot` instance directly). No test project
  references the type by name. **Not renamed**: historical prose mentions of `ChangelogRoot` inside
  already-released changelog entries (`src/Quotinator.Api/resources/changelog.{en,nl,de}.json`,
  describing #82's own past work) — those are frozen historical record, same as any other past release
  entry, not live code referencing the type.
- **#6/#7 (`ConflictResolutionRuleFile`/`SourceAliasRuleFile`)**: referenced in
  `QuotinatorDatabaseInitializer.cs`, `ImportRuleEndpoints.cs`, their own declarations, plus test
  coverage — confirmed via grep, exact count driven off the compiler at implementation time rather
  than pinned here (per the drift found in #2/#3 and #4 above, a hardcoded count in this doc is the
  thing that goes stale, not the rename itself).
- **#8–17 (`ParsedSourceFile` and the nine section-entry types)**: concentrated in
  `SourceQuoteFileReader.cs` (constructs all ten as part of `TryParseExtended`), each type's own
  declaration file, `ImportActionPlanner.cs`/`SqliteImportActionService.cs` (consume the parsed
  sections during staging), and their matching test files
  (`SourceQuoteFileReaderTests.cs`, `ImportActionPlannerTests.cs`, and per-entity test files). Same
  approach — compiler-driven at implementation time, not a fixed count in this doc.

---

## Steps

### 1. Rename SeedFilePreviewResponse → SeedFilePreview

**Status:** ✅ Done

Rename the class in `SeedPreviewResponse.cs` (stays in the same file — it is a small member type, not
worth splitting into its own file per this project's "single-file folders acceptable" precedent scaled
down to single-class-per-concept). Update `AdminEndpoints.cs`'s construction site and the XML doc
comment cross-reference (`<see cref="SeedPreviewResponse"/>` stays; the member type's own summary line
updates).

### 2. Rename SourceQuote → SourceQuoteDto, SourceQuoteTranslation → SourceQuoteTranslationDto

**Status:** ✅ Done

Rename both files and their declared types. Update every call site across `src/`+`tests/` (28 files
per the 2026-08-02 recount above), including the three converter plugin projects and their test
projects. Compiler-verified — no logic change.

### 3. Rename ImportRequestSettingsDto → ImportSettingsDto

**Status:** ✅ Done

Rename the file and class. Update every call site, including the XML doc-comment cross-reference in
`SourceImportSettingsDto.cs`'s own summary (`<c>ImportRequestSettingsDto</c> in <c>Quotinator.Api</c>`
→ `<c>ImportSettingsDto</c>`), and the two test files found in the 2026-08-02 recount
(`FakeQuoteImportService.cs`, `QuoteImportServiceTests.cs`).

### 4. Rename ChangelogRoot → ChangelogRootDto

**Status:** ✅ Done

Rename the file and class in `Quotinator.Changelog`. Update the 3 call sites (`ChangelogService.cs`'s
two references, `scripts/changelog-import.csx`'s construction site). Leave historical changelog JSON
prose mentions of `ChangelogRoot` untouched — they describe a past release, not live code.

### 5. Rename ConflictResolutionRuleFile → ConflictResolutionRuleFileDto, SourceAliasRuleFile → SourceAliasRuleFileDto

**Status:** ✅ Done

Rename both files and their declared types. Update call sites in `QuotinatorDatabaseInitializer.cs`,
`ImportRuleEndpoints.cs`, and their test coverage. Compiler-verified.

### 6. Rename ParsedSourceFile and the nine extended-source-file section types

**Status:** ✅ Done

`ParsedSourceFile` → `ParsedSourceFileDto`; `SourceEntry` → `SourceEntryDto`; `PersonEntry` →
`PersonEntryDto`; `CharacterEntry` → `CharacterEntryDto`; `SourceStageDirection` →
`SourceStageDirectionDto`; `SourceSoundCue` → `SourceSoundCueDto`; `SourceConversation` →
`SourceConversationDto`; `SourceConversationLine` → `SourceConversationLineDto`; `SeriesEntry` →
`SeriesEntryDto`; `UniverseEntry` → `UniverseEntryDto`. Rename all ten files and their declared types
together in one pass, since they're tightly coupled through `SourceQuoteFileReader.cs` and
`ParsedSourceFileDto`'s own member properties — splitting them across separate steps would leave the
solution in a non-compiling state between steps for no benefit. Update every call site across
`Quotinator.Core.Import`, `Quotinator.Core.Database` (`ImportActionPlanner.cs`,
`SqliteImportActionService.cs`), and their test coverage. Compiler-verified — no logic change.

### 7. Full solution build and test sweep

**Status:** ✅ Done

`dotnet build --configuration Release -nodeReuse:false` (after a clean `bin`/`obj`, per the lesson
from #255's own Step 4 — incremental builds can mask stale warnings) — 0 warnings, 0 errors. Full
test suite green, including `Quotinator.Changelog.Tests`. Grep for all seventeen old names across
`src/`, `scripts/`, and `tests/` to confirm nothing was missed (test fixture files, JSON schema doc
comments, etc. commonly reference type names in prose).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `SeedFilePreviewResponse` renamed to `SeedFilePreview`, all references updated | Unit test | `Quotinator.Core.Tests` build succeeds; existing seed-preview endpoint tests in `Quotinator.Api.Tests` pass unchanged |
| 2 | ✅ | `SourceQuote`/`SourceQuoteTranslation` renamed to `SourceQuoteDto`/`SourceQuoteTranslationDto`, all call sites updated | Unit test | Full `Quotinator.Core.Tests` and all three converter test projects (`Quotinator.Converters.RegexArray.Tests`, `.Csv.Tests`, `.BasicJsonArray.Tests`) pass unchanged |
| 3 | ✅ | `ImportRequestSettingsDto` renamed to `ImportSettingsDto`, all call sites updated | Unit test | `Quotinator.Data.Tests` and `Quotinator.Core.Tests` import-settings-parsing tests pass unchanged |
| 4 | ✅ | `ChangelogRoot` renamed to `ChangelogRootDto`, all 3 call sites updated | Unit test | `Quotinator.Changelog.Tests` passes unchanged |
| 5 | ✅ | `ConflictResolutionRuleFile`/`SourceAliasRuleFile` renamed to `ConflictResolutionRuleFileDto`/`SourceAliasRuleFileDto`, all call sites updated | Unit test | `Quotinator.Data.Tests` and `Quotinator.Core.Tests` rule-file tests pass unchanged |
| 6 | ✅ | `ParsedSourceFile` and the nine section-entry types renamed to their `Dto`-suffixed forms, all call sites updated | Unit test | `Quotinator.Core.Tests` (`SourceQuoteFileReaderTests.cs`, `ImportActionPlannerTests.cs`, per-entity tests) passes unchanged |
| 7 | ✅ | No remaining reference to any of the seventeen old names anywhere in `src/`/`scripts/`/`tests/` (excluding frozen historical changelog JSON prose) | Live | Word-boundary grep for each of the seventeen old names, excluding immediate `Dto`-suffixed matches, across `src/`, `scripts/`, `tests/` — returned nothing |
| 8 | ✅ | Build clean | Live | `dotnet build --configuration Release -nodeReuse:false` (after a clean `bin`/`obj`) reported 0 warnings, 0 errors; full test suite passed unchanged (2,870 tests, 0 failed) |

---

## Scope changes

**Twelve additional classes found live during pre-implementation review (2026-08-02) — see
"Additional classes found live" above.** Folded into this issue's own scope rather than deferred,
since they're the same "pure rename, same `Dto` boundary as the original five" shape as everything
else here. Two related-but-different findings from the same review were explicitly *not* folded in
and are tracked as separate follow-up issues instead, since neither is a simple rename:
- A family of classes with a genuinely ambiguous `Dto` boundary (JSON blobs in a database column
  rather than a file, and classes serving two boundaries at once) — needs an ADR 016 clarification
  before any rename decision.
- `GET /api/v1/admin/audit` returning `AuditEntryEntity` directly with no `Response` DTO layer — a
  real architectural gap, but building the missing mapping is new code, not a rename.

`ChangelogRoot` → `ChangelogRootDto` was in scope from #256's original issue body — no deferral,
no scope change. A prior draft of this plan doc incorrectly claimed it was missing from the issue body
and raised an unnecessary scope question to the developer; that was a reading error on this plan doc's
part, not a real mismatch. The only genuine gap found in the original five was the issue's own
Definition of Done checklist saying "four renames" when five were always listed — fixed directly in
the issue body (`gh issue edit 256`), not treated as a scope change.

Two more small drifts found during the 2026-08-02 recount (both already folded into the "Call-site
scope" section above, not treated as separate scope changes since they don't change what gets
renamed, only which files need updating): `SourceQuote`'s call-site count grew from 21 to 28 once
`tests/` was included (the original count was `src/`-only by its own wording, not a real
undercount); `ImportRequestSettingsDto`'s listed `ManifestFileEntryDto` call site no longer exists
(the reference was removed by unrelated work since 2026-08-01), while two test files that do
reference it were previously unlisted.

**Unexpected name collision found during Step 1's implementation, not caught by planning:**
`SeedFilePreviewResponse` → `SeedFilePreview` (a rename ADR 016 itself already decided, not new to
this plan) collides with a pre-existing, unrelated `Quotinator.Data.Import.SeedFilePreview` — a
plain domain value object (`SeedPreviewResult`'s own per-file dry-run scan result, #221), correctly
unsuffixed under ADR 016's own "domain value object" exemption, same category as `SeedBatch`/
`SeedFile`. Both types are used in the same two files (`QuotinatorDatabaseInitializer.cs`, which
constructs the Data-layer record; `AdminEndpoints.cs`, which maps it into the Core-layer response
member), producing genuine `CS0104` ambiguity the moment the `Response` suffix was dropped. Resolved
by fully-qualifying each use site with its correct namespace (`Quotinator.Data.Import.SeedFilePreview`
in the initializer, `Quotinator.Core.Models.SeedFilePreview` in the endpoint) — the minimal,
ADR-consistent fix; neither type's name changes, since the Data-layer type is out of this issue's
scope and the Core-layer name is the one ADR 016 already locked in.
