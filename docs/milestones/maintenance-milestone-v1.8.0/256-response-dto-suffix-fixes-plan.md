# #256 — Fix Response/Dto/class-suffix violations

**Status:** Planning
**GitHub issue:** #256
**Tiers required:** N/A
**Depends on:** Nothing

---

## Background

Implements the `Request`/`Response`/`Dto` suffix half of
[ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md) for five specific
classes found while planning #227. Pure C# renames — no schema impact, no behavioural change,
independent of #253/#254 (the table/entity renames).

`PagedResult<T>`/`FilteredQuoteResult<T>` are explicitly out of scope — ADR 016 deferred the generic
`BaseResponse<T>`/`BaseRequest<T>` base-type design; that is a larger follow-up, not a simple rename,
and is not part of this issue's Definition of Done.

## The five renames

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

## Call-site scope

- **#2/#3 (`SourceQuote`/`SourceQuoteTranslation`)**: referenced in 21 files (confirmed via grep across
  `src/`), spanning `Quotinator.Core.Import`, `Quotinator.Core.Services`, `Quotinator.Core.Database`,
  and three converter plugin projects (`Quotinator.Converters.RegexArray`,
  `Quotinator.Converters.Csv`, `Quotinator.Converters.BasicJsonArray`) that construct/return
  `SourceQuote` instances as their `IQuoteSourceConverter.ConvertAsync` output. All are IDE-mechanical
  renames — the compiler (CS0246) finds every miss.
- **#1 (`SeedFilePreviewResponse`)**: 2 files — its own declaration and `AdminEndpoints.cs`, which
  builds `SeedFilePreviewResponse` instances for the seed-preview endpoint response.
- **#4 (`ImportRequestSettingsDto`)**: 6 files — its own declaration, `SourceImportSettingsDto` (base
  class doc-comment reference only), `ManifestFileEntryDto`, `ImportRequestSettingsParser`,
  `SqliteQuoteImportService`, `IQuoteImportService`.
- **#5 (`ChangelogRoot`)**: 3 files — its own declaration, `ChangelogService.cs` (2 references:
  a local variable type and the `JsonSerializer.Deserialize<ChangelogRoot>` call), and
  `scripts/changelog-import.csx` (constructs a `ChangelogRoot` instance directly). No test project
  references the type by name. **Not renamed**: historical prose mentions of `ChangelogRoot` inside
  already-released changelog entries (`src/Quotinator.Api/resources/changelog.{en,nl,de}.json`,
  describing #82's own past work) — those are frozen historical record, same as any other past release
  entry, not live code referencing the type.

---

## Steps

### 1. Rename SeedFilePreviewResponse → SeedFilePreview

**Status:** ⬜ Not started

Rename the class in `SeedPreviewResponse.cs` (stays in the same file — it is a small member type, not
worth splitting into its own file per this project's "single-file folders acceptable" precedent scaled
down to single-class-per-concept). Update `AdminEndpoints.cs`'s construction site and the XML doc
comment cross-reference (`<see cref="SeedPreviewResponse"/>` stays; the member type's own summary line
updates).

### 2. Rename SourceQuote → SourceQuoteDto, SourceQuoteTranslation → SourceQuoteTranslationDto

**Status:** ⬜ Not started

Rename both files and their declared types. Update every call site across the 21 files identified
above, including the three converter plugin projects. Compiler-verified — no logic change.

### 3. Rename ImportRequestSettingsDto → ImportSettingsDto

**Status:** ⬜ Not started

Rename the file and class. Update the 6 call sites, including the XML doc-comment cross-reference in
`SourceImportSettingsDto.cs`'s own summary (`<c>ImportRequestSettingsDto</c> in <c>Quotinator.Api</c>`
→ `<c>ImportSettingsDto</c>`).

### 4. Rename ChangelogRoot → ChangelogRootDto

**Status:** ⬜ Not started

Rename the file and class in `Quotinator.Changelog`. Update the 3 call sites (`ChangelogService.cs`'s
two references, `scripts/changelog-import.csx`'s construction site). Leave historical changelog JSON
prose mentions of `ChangelogRoot` untouched — they describe a past release, not live code.

### 5. Full solution build and test sweep

**Status:** ⬜ Not started

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green,
including `Quotinator.Changelog.Tests`. Grep for the five old names across `src/`, `scripts/`, and
`tests/` to confirm nothing was missed (test fixture files, JSON schema doc comments, etc. commonly
reference type names in prose).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `SeedFilePreviewResponse` renamed to `SeedFilePreview`, all references updated | Unit test | `Quotinator.Core.Tests` build succeeds; existing seed-preview endpoint tests in `Quotinator.Api.Tests` pass unchanged |
| 2 | ❌ | `SourceQuote`/`SourceQuoteTranslation` renamed to `SourceQuoteDto`/`SourceQuoteTranslationDto`, all 21 call sites updated | Unit test | Full `Quotinator.Core.Tests` and all three converter test projects (`Quotinator.Converters.RegexArray.Tests`, `.Csv.Tests`, `.BasicJsonArray.Tests`) pass unchanged |
| 3 | ❌ | `ImportRequestSettingsDto` renamed to `ImportSettingsDto`, all 6 call sites updated | Unit test | `Quotinator.Data.Tests` and `Quotinator.Core.Tests` import-settings-parsing tests pass unchanged |
| 4 | ❌ | `ChangelogRoot` renamed to `ChangelogRootDto`, all 3 call sites updated | Unit test | `Quotinator.Changelog.Tests` passes unchanged |
| 5 | ❌ | No remaining reference to any of the five old names anywhere in `src/`/`scripts/`/`tests/` (excluding frozen historical changelog JSON prose) | Live | `rg "SeedFilePreviewResponse|(?<!Translation)SourceQuote\b|SourceQuoteTranslation\b|ImportRequestSettingsDto|(?<!Dto)ChangelogRoot\b" src/ scripts/ tests/` (excluding the `*.json` changelog resource files and `*Dto` matches) returns nothing |
| 6 | ❌ | Build clean | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |

---

## Scope changes

None. `ChangelogRoot` → `ChangelogRootDto` was in scope from #256's original issue body — no deferral,
no scope change. A prior draft of this plan doc incorrectly claimed it was missing from the issue body
and raised an unnecessary scope question to the developer; that was a reading error on this plan doc's
part, not a real mismatch. The only genuine gap found was the issue's own Definition of Done checklist
saying "four renames" when five were always listed — fixed directly in the issue body (`gh issue edit
256`), not treated as a scope change.
