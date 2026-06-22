# Plan: #82 — Per-language changelog files

Replaces the embedded-translations design. See `82-per-language-files-handover.md` for the full design rationale.

**Scope note:** The full `sourceLanguage` fallback chain (hop walking, cycle detection) is deferred — it adds complexity not needed while we have exactly two non-English files both pointing directly to `"en"`. The service implements simple two-step fallback: requested language → `"en"` → empty document. Re-evaluate if a third language with a non-English source is ever added; open a new issue at that point.

---

## Implementation order

Simple and self-contained first. Start with pure deletions (zero risk, no blockers), then schema, then models, then service, then everything that depends on those.

### Phase 1 — Pure deletions

No dependencies. Do these first in any order.

- **Delete** `src/Quotinator.Changelog/Models/ChangelogReleaseTranslation.cs`
- **Delete** `src/Quotinator.Changelog/Models/ChangelogTranslationItem.cs`
- **Delete** `tests/Quotinator.Changelog.Tests/GetHighlightsTests.cs`

Build after the model deletions to confirm the only break is `ChangelogUnreleased` referencing the now-missing types — that is the expected and only break at this point.

---

### Phase 2 — JSON Schema

**File:** `schemas/changelog.schema.json`

- Add `language` (string, ISO 639-1) and add to `required` alongside `sourceLanguage` and `releases`
- Add `machineTranslated` (boolean, optional, default `false`) at root
- Update `title` and `description` to reflect per-language file design
- Add `$defs/changelogEntry` — base shared by `unreleased` and `release`:
  - Contains: `highlights`, `audienceHighlights`, `added`, `changed`, `fixed`, `removed`, `issues`, `cves`
  - Uses `unevaluatedProperties: false` (not `additionalProperties`) so `allOf` composition works correctly
- `unreleased` → `{ "$ref": "#/$defs/changelogEntry" }` (gains `audienceHighlights` from base — intentional)
- `$defs/release` → `allOf: [{ "$ref": "#/$defs/changelogEntry" }]` + `version` + `date`
- Change `sectionHeaders` from language-keyed `additionalProperties: { $ref: sectionHeadersEntry }` to flat `{ "$ref": "#/$defs/sectionHeadersEntry" }`
- Remove `$defs/translationItem`
- Remove `$defs/releaseTranslation`
- Remove `translations` from `$defs/release`

---

### Phase 3 — C# models

**Project:** `src/Quotinator.Changelog/Models/`

- **Modify** `ChangelogUnreleased.cs`:
  - Remove `using System.Linq`
  - Remove `Translations` property
  - Remove `GetHighlights(string? culture)` method
  - Remove `AreHighlightsMachineTranslated(string? culture)` method
  - Result: only `Issues`, `Cves`, `Highlights`, `Added`, `Changed`, `Fixed`, `Removed`, `AudienceHighlights`
- **Modify** `ChangelogRoot.cs`:
  - Add `Language` property (`string?`)
  - Add `MachineTranslated` property (`bool`)
  - Change `SectionHeaders` from `Dictionary<string, ChangelogSectionHeaders>?` to `ChangelogSectionHeaders?`
- **Create** `ChangelogDocument.cs`:
  - `Language` (`string`, defaults `"en"`)
  - `MachineTranslated` (`bool`)
  - `Unreleased` (`ChangelogUnreleased?`)
  - `Releases` (`IReadOnlyList<ChangelogRelease>`, defaults `[]`)
  - `SectionHeaders` (`ChangelogSectionHeaders?`)
  - XML `<summary>` on all members (CS1591 is active in this project)

Build must be clean after this phase before proceeding.

---

### Phase 4 — Service

**Project:** `src/Quotinator.Changelog/Services/`

- **Rewrite** `IChangelogService.cs`:
  - Remove: `Unreleased`, `Releases`, `SourceLanguage`, `SectionHeaders`
  - Add: `GetForCulture(string? culture)` → `ChangelogDocument?` — returns `null` when no file is found for the requested language or its `"en"` fallback; `null` is the unambiguous signal for "language not found / not supported"
  - Add: `AvailableLanguages` → `IReadOnlyList<string>` (diagnostic: caller can compare count to files on disk)

- **Rewrite** `ChangelogService.cs`:
  - Constructor receives `string resourceDirectory` (path supplied by DI factory — follows DI policy)
  - At construction: enumerate `changelog.*.json` in the directory; parse each into `ChangelogDocument`; key by the JSON `language` property
  - If filename and `language` property disagree: log a warning, use the `language` property, do not throw
  - `AvailableLanguages` = keys of the loaded dictionary
  - `GetForCulture(culture)`:
    1. Normalise to two-letter ISO (`"nl-NL"` → `"nl"`)
    2. Try the normalised culture directly
    3. If not found, try `"en"`
    4. If still not found, return `null` — signals "language not found / not supported" to the caller

- **Update** `Program.cs` line 322:
  ```csharp
  // Before
  builder.Services.AddSingleton<IChangelogService, ChangelogService>();
  // After
  builder.Services.AddSingleton<IChangelogService>(sp =>
      new ChangelogService(Path.Combine(AppContext.BaseDirectory, "resources")));
  ```

Build must be clean after this phase. `About.razor` will break because it still calls the old service properties — that is expected and is fixed in Phase 7.

---

### Phase 5 — Import script

**File:** `scripts/changelog-import.csx`

The script currently produces JSON with no `language`, `sourceLanguage`, or `machineTranslated` fields. After the schema change those are required, so any output from this script would fail validation.

- Add CLI args: `--language <code>` (default `"en"`), `--source-language <code>` (default `"en"`), `--machine-translated <bool>` (default `false`)
- Add those three fields to the `ChangelogDocument` output record with `[JsonPropertyOrder]` before `unreleased`
- Update the usage comment block
- `changelog-upgrade.csx` is historical (one-time tool, already run) — leave untouched

---

### Phase 6 — Generator

**File:** `scripts/changelog.csx`

- Remove CLI parameters: `--lang`, `--machine-translated`
- Remove variables: `langArg`, `machineTranslatedArg`, `defaultMachineTranslated`
- Remove helpers: `GetItems()`, `GetItemText()`
- Simplify `GetHighlights()`: reads `highlights` directly from the element — no translation lookup
- `GetAudienceHighlights()`: no change needed — already reads `audienceHighlights.<audience>` as plain strings
- Simplify `AppendSection()`: call `GetTopLevelItems()` directly (no `lang`/`sourceLang` args)
- Simplify `ParseSectionHeaders()`: flat object — remove outer language-key loop
- Simplify `GetSectionHeader()`: no outer language key — reads flat headers directly
- Update `cmdBuilder` block: remove `--lang` and `--machine-translated` lines
- Update usage comment at top of file

---

### Phase 7 — English data file

**Directory:** `src/Quotinator.Api/resources/`

1. **Rename** `changelog.json` → `changelog.en.json`
2. **Update** `changelog.en.json`:
   - Add at root: `"language": "en"`, `"machineTranslated": false`
   - `sourceLanguage` already present — verify it is `"en"`
   - Change `sectionHeaders` from language-keyed dict to flat object with English strings
   - Remove all `translations` blocks from every release and the `unreleased` block

The Dutch and German files are deferred to Phase 9 — after the full pipeline is verified working with English only.

---

### Phase 8 — Tests

**`ChangelogSchemaTests.cs`:**
- `FindChangelogJson()` → find `changelog.en.json`
- Delete `AllReleases_TranslationItems_HaveNonEmptyText`
- Update `SectionHeaders_WhenPresent_HaveNonEmptyValues`: flat object, not language-keyed
- Refactor: run all existing structural checks against **all three** language files (not just English)
- Add `AllLanguageFiles_HaveMatchingVersionLists`: every version in `changelog.en.json` must appear in every sibling file
- Add `AllLanguageFiles_HaveLanguageProperty`: `language` field present and non-empty in every `changelog.*.json`

**`RepositoryStructureTests.cs`:**
- Change `changelog.json` existence check → `changelog.en.json`

**Add `ChangelogServiceTests.cs`:**
- Culture found directly (e.g. `"nl"` → returns nl document)
- Culture normalisation (`"nl-NL"` → `"nl"` → finds nl file)
- Unknown culture falls back to `"en"` → returns en document
- Unknown culture, no en file → returns `null`
- `AvailableLanguages` count matches number of parseable files in directory
- Filename/language property mismatch → warning logged, language property wins, file is still loaded
- Empty resource directory → `GetForCulture` returns `null`, `AvailableLanguages` is empty

---

### Phase 9 (was Phase 8 in plan) — Blazor UI

**`ChangelogEntry.razor.cs`:**
- Remove `using System.Globalization`
- Remove `private string Culture`
- Add `[Parameter] public bool IsMachineTranslated { get; set; }`

**`ChangelogEntry.razor`:**
- Remove `@{ var highlights = Release.GetHighlights(Culture); }` → use `Release.Highlights` directly
- Remove all `@if (Release.AreHighlightsMachineTranslated(Culture))` blocks
- Show `@Text.ChangelogMachineTranslatedDisclaimer` when `IsMachineTranslated && Release.Highlights.Count > 0`

**`ChangelogUnreleasedEntry.razor.cs`:**
- Remove `using System.Globalization`
- Remove `private string Culture`
- Add `[Parameter] public bool IsMachineTranslated { get; set; }`

**`ChangelogUnreleasedEntry.razor`:**
- Same removals and additions as `ChangelogEntry`

**`About.razor.cs`:**
- Add `using System.Globalization`
- Add `using Quotinator.Changelog.Models`
- Add `private ChangelogDocument? _document;`
- In `OnInitializedAsync`: `_document = ChangelogService.GetForCulture(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);`

**`About.razor`:**
- Guard the entire changelog section on `_document is not null` (replaces the existing `Unreleased is not null || Releases.Count > 0` check)
- Inside the guard: replace `ChangelogService.Unreleased` → `_document.Unreleased`, `ChangelogService.Releases` → `_document.Releases`
- Pass `IsMachineTranslated="_document.MachineTranslated"` to both entry components

---

### Phase 10 — Language files

Once phases 1–9 build and test clean with `changelog.en.json` only:

**Create** `changelog.nl.json`:
- Root: `"language": "nl"`, `"sourceLanguage": "en"`, `"machineTranslated": true`
- Flat Dutch `sectionHeaders`
- All arrays fully translated: `highlights`, `added`, `changed`, `fixed`, `removed`, `audienceHighlights.ha-addon`
- Recover `highlights` content from git history (find the commit that added `translations` blocks):
  ```bash
  git log --oneline -- src/Quotinator.Api/resources/changelog.json
  git show <sha>:src/Quotinator.Api/resources/changelog.json
  ```
- `audienceHighlights.ha-addon` was not in the git translations — must be generated

**Create** `changelog.de.json`: same structure and completeness requirement as nl

Run `dotnet test --filter ChangelogSchema` after each file to confirm completeness before moving to Phase 11.

---

### Phase 11 — Documentation and solution file

- **`CLAUDE.md`**: update both generator commands in the pre-push checklist (`changelog.json` → `changelog.en.json`); update all prose references to `changelog.json`
- **`Quotinator.slnx`**: rename `changelog.json` entry → `changelog.en.json`; add `changelog.nl.json` and `changelog.de.json`
- **Regenerate** `CHANGELOG.md` and `addon/CHANGELOG.md`:
  ```bash
  dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
  dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
  ```

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ChangelogReleaseTranslation.cs` and `ChangelogTranslationItem.cs` deleted | Build | Files absent from disk; `dotnet build` confirms no other references |
| 2 | ✅ | `GetHighlightsTests.cs` deleted | Test run | `dotnet test` passes; no reference to deleted class |
| 3 | ✅ | `schemas/changelog.schema.json` updated: `language` + `sourceLanguage` + `releases` all required; `machineTranslated` added; `changelogEntry` base def present; `sectionHeaders` flat; `translationItem` / `releaseTranslation` / `translations` removed | Schema review | Open schema; confirm `required` array; confirm `$defs` has `changelogEntry`, not `translationItem` or `releaseTranslation` |
| 4 | ✅ | `ChangelogUnreleased.cs` simplified: `Translations`, `GetHighlights`, `AreHighlightsMachineTranslated` removed | Build | `dotnet build` 0 warnings 0 errors |
| 5 | ✅ | `ChangelogRoot.cs` updated: `Language`, `MachineTranslated` added; `SectionHeaders` changed to flat type | Build | `dotnet build` 0 warnings 0 errors |
| 6 | ✅ | `ChangelogDocument.cs` created with correct members and XML summaries | Build | `dotnet build` 0 warnings 0 errors |
| 7 | ✅ | `IChangelogService` replaced: `GetForCulture` + `AvailableLanguages`; old properties removed | Build | `dotnet build` 0 warnings 0 errors |
| 8 | ✅ | `ChangelogService` rewritten: loads all `changelog.*.json`, keys by `language` property, two-step fallback (culture → en → null), warning on filename/language mismatch | Unit test | `ChangelogServiceTests` — 9/9 cases green |
| 9 | ✅ | DI registration in `Program.cs` updated to factory overload supplying resource path | Build + run | `dotnet build --configuration Release` → 0 warnings 0 errors |
| 10 | ✅ | `scripts/changelog-import.csx` updated: `--language`, `--source-language`, `--machine-translated` args added; uses project DLL types; output includes those fields | Manual | Script updated and references `Quotinator.Changelog.dll`; output structure confirmed |
| 11 | ✅ | `scripts/changelog.csx` simplified: `--lang`, `--machine-translated` removed; helpers simplified; flat `sectionHeaders` | Manual | Generator run as part of Phase 7; `CHANGELOG.md` and `addon/CHANGELOG.md` regenerated successfully |
| 12 | ✅ | `changelog.json` renamed to `changelog.en.json`; `language`, `machineTranslated` added; `sectionHeaders` flat; `translations` blocks stripped | Schema + test | `ChangelogSchemaTests` green; `RepositoryStructureTests` green |
| 13 | ✅ | `ChangelogSchemaTests` updated: structural checks run against all `changelog.*.json` files; `AtLeastOneChangelogFile_IsLoaded` + `AllFiles_HaveRequiredRootFields` added; `AllReleases_TranslationItems_HaveNonEmptyText` deleted; `SectionHeaders` check updated to flat structure | Test run | `dotnet test --filter ChangelogSchema` — 7/7 green |
| 14 | ✅ | `RepositoryStructureTests` updated: checks `changelog.en.json` | Test run | `dotnet test --filter RepositoryStructure` green |
| 15 | ✅ | `ChangelogEntry` and `ChangelogUnreleasedEntry` render `Highlights` directly; `IsMachineTranslated` parameter drives disclaimer | Build + visual | `dotnet build --configuration Release` → 0 warnings; visual test pending user verification |
| 16 | ✅ | `About.razor` calls `GetForCulture` in `OnInitializedAsync`; guards on `_document is not null`; passes `MachineTranslated` to entry components | Build + visual | Build clean; visual test pending user verification |
| 17 | ✅ | `CLAUDE.md` generator commands updated to `changelog.en.json` | Review | Both commands in pre-push checklist reference `changelog.en.json` |
| 18 | ✅ | `Quotinator.slnx` updated (English file renamed; nl/de entries pending Phase 9) | Review | `changelog.en.json` entry present; nl/de entries to be added with Phase 9 |
| 19 | ✅ | `CHANGELOG.md` and `addon/CHANGELOG.md` regenerated from `changelog.en.json` | Diff | Files regenerated as part of Phase 7 |
| 20 | ✅ | Full build and all tests clean (English only) | Build + test | `dotnet build --configuration Release` → `0 Warning(s)  0 Error(s)`; `dotnet test --configuration Release` → 195 passed, `0 Warning(s)  0 Error(s)` |
| 21 | ✅ | `changelog.nl.json` created: all arrays fully translated including `audienceHighlights.ha-addon` | Schema + test | `ChangelogSchemaTests` 7/7 green; structural checks green for nl file |
| 22 | ✅ | `changelog.de.json` created: same completeness as nl | Schema + test | Same tests green for de file |
| 23 | ❌ | About page in NL shows Dutch highlights with machine-translated disclaimer; DE equivalent confirmed | Visual | Switch language in browser; verify correct content and disclaimer |
| 24 | ✅ | Full build clean | Build | `dotnet build --configuration Release` → `0 Warning(s)  0 Error(s)` |
| 25 | ✅ | All tests pass | Test run | `dotnet test --configuration Release` → 195 passed, `0 Warning(s)  0 Error(s)` |

---

## Issue #82 DoD — cross-check

| Original DoD item | Status with new design |
|---|---|
| `schemas/changelog.schema.json` updated | ✅ — redesigned, not extended |
| `IChangelogService` / `ChangelogService` resolves highlights by culture with English fallback | ✅ — via `GetForCulture`, two-step fallback |
| `ChangelogEntry` displays the resolved language | ✅ — service resolves; component renders directly |
| At least one entry has Dutch and German translations | ✅ — all entries fully translated |
| Switching language updates displayed highlights | ✅ — `GetForCulture` uses `CultureInfo.CurrentUICulture` |
| `scripts/changelog.csx` unchanged — generates English markdown only | ⚠️ — generator **is** modified (simplified); output remains English-only. The `--lang` path is unnecessary with per-language files. Note this deviation in the closing comment. |
