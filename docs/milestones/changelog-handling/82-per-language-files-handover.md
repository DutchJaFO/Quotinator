# Handover: #82 — Per-language changelog files redesign

## Context

Issue #82 was originally designed with embedded translations inside a single `changelog.json`. During implementation it became clear that this approach:
- Duplicates the schema (every translatable field needs a `translationItem` wrapper variant)
- Complicates the generator (`--lang` lookup, fallback logic inside `GetItems`)
- Complicates the C# model (`Translations` dictionary, `GetHighlights(culture)`, per-item `machineTranslated`)
- Diverges from how the UI already handles this (`UI.en-GB.json`, `UI.nl.json`, `UI.de.json`)

**Decision:** adopt the same per-language file pattern that `Toolbelt.Blazor.I18nText` uses. One file per language; each file is flat content in that language; no embedded translation infrastructure.

---

## Design decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | File naming: `changelog.en.json`, `changelog.nl.json`, `changelog.de.json` | Matches `UI.en-GB.json` / `UI.nl.json` / `UI.de.json` pattern exactly |
| 2 | Top-level `language` property (ISO 639-1) declares what language the file is in | Self-documenting; allows schema validation independent of filename |
| 3 | Top-level `sourceLanguage` property defines the fallback language | Enables arbitrary fallback chains: `nl → de → en`. A chain is valid as long as it is acyclic and terminates at a file where `language == sourceLanguage`. |
| 4 | Top-level `machineTranslated` (boolean, defaults `false`) | File-level flag replaces per-item `machineTranslated`. The disclaimer is shown or hidden based on this one property. |
| 5 | `sectionHeaders` is a flat object in each file, not keyed by language | Each file contains its own language's section headers; no outer language key needed |
| 6 | All content in a language file is complete for that language | No partial-release fallback at generator or model level. The service falls back at file level (if `changelog.nl.json` does not exist, load the `sourceLanguage` file). |
| 7 | Generator `--lang` and `--machine-translated` parameters are removed | The input file IS the language. To generate Dutch output: `--input changelog.nl.json`. |
| 8 | `IChangelogService` becomes culture-aware; loads all language files at startup | Singleton loads all available `changelog.*.json` files into a dictionary. Provides `GetForCulture(string? culture)` returning a `ChangelogDocument`. |
| 9 | New `ChangelogDocument` wrapper carries file-level metadata (`Language`, `MachineTranslated`) | Gives the Blazor component a single object rather than multiple service properties |
| 10 | Blazor components render `Release.Highlights` directly — no `GetHighlights(culture)` | Culture resolution is done once by the service; the component receives pre-resolved content |

---

## New JSON file structure

### `changelog.en.json` (English source — rename from `changelog.json`)

```json
{
  "language": "en",
  "sourceLanguage": "en",
  "machineTranslated": false,
  "sectionHeaders": {
    "highlights": "Highlights",
    "added": "Added",
    "changed": "Changed",
    "fixed": "Fixed",
    "removed": "Removed"
  },
  "unreleased": { ... },
  "releases": [
    {
      "version": "1.5.1",
      "date": "2026-06-20",
      "highlights": ["English text."],
      "added": ["..."],
      "fixed": ["..."]
    }
  ]
}
```

### `changelog.nl.json` (Dutch — machine-translated from English)

```json
{
  "language": "nl",
  "sourceLanguage": "en",
  "machineTranslated": true,
  "sectionHeaders": {
    "highlights": "Hoogtepunten",
    "added": "Toegevoegd",
    "changed": "Gewijzigd",
    "fixed": "Opgelost",
    "removed": "Verwijderd"
  },
  "unreleased": { ... },
  "releases": [
    {
      "version": "1.5.1",
      "date": "2026-06-20",
      "highlights": ["Nederlandse tekst."],
      "added": ["..."],
      "fixed": ["..."]
    }
  ]
}
```

### `changelog.de.json` (German — machine-translated from English)

Same structure, `"language": "de"`, `"sourceLanguage": "en"`, `"machineTranslated": true`.

---

## Fallback chain (service behaviour)

```
GetForCulture("nl")
  → look for changelog.nl.json         found? return it
  → read nl file's sourceLanguage       "en"
  → look for changelog.en.json         found? return it
  → (en's sourceLanguage == "en")       terminates
  → last resort: return empty document
```

**Maximum depth: (n − 1) hops**, where n is the number of `changelog.*.json` files found at startup.

With 3 languages (en, nl, de): maximum 2 hops. The worst-case valid chain is `nl → de → en`. A 4th hop would necessarily revisit a language already in the chain, so the limit prevents all cycles without needing to track visited nodes — the counter alone is sufficient.

The service counts the available language files once at startup and passes that count as the hop limit into the fallback walk.

---

## Schema changes (`schemas/changelog.schema.json`)

### Remove
- `$defs/releaseTranslation`
- `$defs/translationItem`
- `translations` property from `$defs/release`
- `sectionHeaders` outer language-key dictionary (was `additionalProperties: sectionHeadersEntry`) → replaced by flat `sectionHeadersEntry` directly

### Add
- Top-level `language` (string, ISO 639-1, required)
- Top-level `machineTranslated` (boolean, optional, default false)
- `$defs/changelogEntry` — base definition shared by `unreleased` and `release`
  - Uses `unevaluatedProperties: false` (NOT `additionalProperties: false`) to allow `allOf` composition
  - Contains: `highlights`, `audienceHighlights`, `added`, `changed`, `fixed`, `removed`, `issues`, `cves`
- `$defs/release` uses `allOf: [{ "$ref": "#/$defs/changelogEntry" }]` + `version` + `date`
- `unreleased` uses `{ "$ref": "#/$defs/changelogEntry" }`

### Change
- `sourceLanguage`: description updated to "ISO 639-1 code of the fallback language file. When `language == sourceLanguage` there is no further fallback."
- `sectionHeaders`: type changes from `object` with `additionalProperties: { "$ref": "#/$defs/sectionHeadersEntry" }` to directly `{ "$ref": "#/$defs/sectionHeadersEntry" }`

### Why `unevaluatedProperties` and not `additionalProperties`

`additionalProperties: false` in JSON Schema only sees properties declared in the **same** schema object. When `release` extends `changelogEntry` via `allOf`, the `additionalProperties: false` in `changelogEntry` would reject `version` and `date` (defined in the outer `release` schema, not in `changelogEntry`). `unevaluatedProperties: false` (introduced in Draft 2020-12, already declared in this schema's `$schema` field) evaluates the **full composed schema** before deciding what is additional. Use it on the base definition and any leaf that needs to be closed.

---

## C# model changes (`src/Quotinator.Changelog/Models/`)

### Delete
- `ChangelogReleaseTranslation.cs`
- `ChangelogTranslationItem.cs`

### Modify — `ChangelogRoot.cs`
```csharp
public sealed class ChangelogRoot
{
    public string? Language { get; init; }                          // NEW
    public string? SourceLanguage { get; init; }
    public bool MachineTranslated { get; init; }                   // NEW
    public ChangelogSectionHeaders? SectionHeaders { get; init; }  // CHANGED: was Dictionary<string, ChangelogSectionHeaders>
    public ChangelogUnreleased? Unreleased { get; init; }
    public List<ChangelogRelease>? Releases { get; init; }
}
```

### Modify — `ChangelogUnreleased.cs`
Remove entirely:
- `using System.Linq;`
- `Translations` property
- `GetHighlights(string? culture)` method
- `AreHighlightsMachineTranslated(string? culture)` method

After removal the class contains only: `Issues`, `Cves`, `Highlights`, `Added`, `Changed`, `Fixed`, `Removed`, `AudienceHighlights`.

### New — `ChangelogDocument.cs`
```csharp
namespace Quotinator.Changelog.Models;

/// <summary>
/// A fully-resolved changelog for one language, produced by <see cref="IChangelogService"/>.
/// Carries file-level metadata alongside the release content.
/// </summary>
public sealed class ChangelogDocument
{
    /// <summary>ISO 639-1 language code of this document's content.</summary>
    public string Language { get; init; } = "en";

    /// <summary><see langword="true"/> when the content was machine-translated.</summary>
    public bool MachineTranslated { get; init; }

    /// <summary>Pending unreleased changes. <see langword="null"/> when no unreleased block is present.</summary>
    public ChangelogUnreleased? Unreleased { get; init; }

    /// <summary>All releases, newest first.</summary>
    public IReadOnlyList<ChangelogRelease> Releases { get; init; } = [];

    /// <summary>Section display names for this language. <see langword="null"/> when not declared.</summary>
    public ChangelogSectionHeaders? SectionHeaders { get; init; }
}
```

### No change
- `ChangelogRelease.cs`
- `ChangelogSectionHeaders.cs`

---

## Service interface changes (`src/Quotinator.Changelog/Services/`)

### `IChangelogService.cs` — replace entirely

```csharp
/// <summary>Provides changelog content resolved to the requested language.</summary>
public interface IChangelogService
{
    /// <summary>
    /// Returns the changelog document for <paramref name="culture"/>, following the
    /// <c>sourceLanguage</c> fallback chain until a file is found.
    /// Returns an empty document when no language file exists at all.
    /// </summary>
    ChangelogDocument GetForCulture(string? culture);

    /// <summary>ISO 639-1 codes of all language files found at startup.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }
}
```

### `ChangelogService.cs` — rewrite

Behaviour:
1. Constructor receives the resource directory path (injected via DI factory overload so path is computed at registration time — follows the existing DI policy).
2. At construction, enumerate `changelog.*.json` files in the directory. Parse and cache each as a `ChangelogDocument` in `Dictionary<string, ChangelogDocument>` keyed by `language`.
3. `GetForCulture(culture)`:
   - Normalise `culture` to two-letter ISO (e.g. `"nl-NL"` → `"nl"`).
   - Walk the fallback chain: load `culture` → read its `sourceLanguage` → load that → repeat.
   - Cycle detection: track visited language codes; break if revisited.
   - Return the first document found; return an empty `ChangelogDocument` if none found.

DI registration in `Program.cs` (already present — update the path and constructor call):
```csharp
builder.Services.AddSingleton<IChangelogService>(sp =>
    new ChangelogService(Path.Combine(AppContext.BaseDirectory, "resources")));
```

---

## Blazor component changes

### `ChangelogEntry.razor.cs`
- Remove `using System.Globalization;`
- Remove `private string Culture => ...`

### `ChangelogEntry.razor`
- Remove `@{ var highlights = Release.GetHighlights(Culture); }` — replace with direct use of `Release.Highlights`
- Remove `@if (Release.AreHighlightsMachineTranslated(Culture))` blocks
- The machine-translated disclaimer is now driven by a page-level `bool IsMachineTranslated` parameter or cascading value from the About page (not per-release)

### Same changes apply to `ChangelogUnreleasedEntry.razor` and `.razor.cs`

### About page / Changelog page
- Call `IChangelogService.GetForCulture(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)`
- Pass `document.MachineTranslated` down to the entry controls (parameter or `CascadingValue`)
- Show the `ChangelogMachineTranslatedDisclaimer` once at page level when `MachineTranslated == true`, or keep it per-entry (decision for implementer — per-entry is already wired in the Razor; page-level is cleaner UX)

---

## Generator changes (`scripts/changelog.csx`)

### Remove parameters
- `--lang` / `langArg`
- `--machine-translated` / `defaultMachineTranslated` / `machineTranslatedArg`

### Remove helpers
- `GetItems()` — translation-aware lookup no longer needed
- `GetItemText()` — was for unwrapping `{text, machineTranslated}` objects; content is now plain strings
- Language-keyed `ParseSectionHeaders()` — replace with flat object parse

### Simplify
- `GetHighlights()`: reads `highlights` directly from the element (no translation lookup)
- `GetAudienceHighlights()`: reads `audienceHighlights.<audience>` directly (no translation lookup)
- `AppendSection()`: calls `GetTopLevelItems()` directly
- `ParseSectionHeaders()`: reads a flat `sectionHeaders` object (not keyed by language)

### Update header comment
Remove `--lang` and `--machine-translated` from the usage block.

---

## CLAUDE.md changes

In the pre-push checklist, update both generator commands:
```bash
# Before
--input src/Quotinator.Api/resources/changelog.json

# After
--input src/Quotinator.Api/resources/changelog.en.json
```

Update all prose references to `changelog.json` → `changelog.en.json`.

---

## `Quotinator.slnx` changes

In the `/data/sources/` (or wherever `changelog.json` is listed as a solution item):
- Rename the entry to `changelog.en.json`
- Add `changelog.nl.json` and `changelog.de.json` once those files are created

---

## Files to create / rename / delete

| Action | Path |
|---|---|
| Rename | `src/Quotinator.Api/resources/changelog.json` → `changelog.en.json` |
| Create | `src/Quotinator.Api/resources/changelog.nl.json` |
| Create | `src/Quotinator.Api/resources/changelog.de.json` |
| Create | `src/Quotinator.Changelog/Models/ChangelogDocument.cs` |
| Delete | `src/Quotinator.Changelog/Models/ChangelogReleaseTranslation.cs` |
| Delete | `src/Quotinator.Changelog/Models/ChangelogTranslationItem.cs` |
| Delete | `tests/Quotinator.Changelog.Tests/GetHighlightsTests.cs` |

---

## Tests affected

### Delete entirely
- `tests/Quotinator.Changelog.Tests/GetHighlightsTests.cs` — the method under test no longer exists

### Update — `ChangelogSchemaTests.cs`
- `FindChangelogJson()` helper: look for `changelog.en.json` instead of `changelog.json`
- `AllReleases_HaveNonNullHighlightsArray`: no change (structure same)
- `AllReleases_TranslationItems_HaveNonEmptyText`: **DELETE** — `translationItem` objects no longer exist
- Add new test: `AllLanguageFiles_HaveMatchingVersionLists` — verifies that every release version in `changelog.en.json` also appears in each `changelog.*.json` sibling file (completeness guard)
- Add new test: `AllLanguageFiles_HaveLanguageProperty` — verifies `language` field is present and non-empty in every `changelog.*.json`

### Update — `RepositoryStructureTests.cs`
- Change the `changelog.json` existence check to look for `changelog.en.json`

### No change
- `GeneratedFileHeaderTests.cs`
- All other test files

---

## Content for `changelog.nl.json` and `changelog.de.json`

The Dutch and German translations that were machine-generated in this session are preserved in the git history of `src/Quotinator.Api/resources/changelog.json` on `feature/changelog-handling`. They were added by commit `<run git log to find it>` and removed by the subsequent `git restore`. To recover them for use as source content for the new language files:

```bash
git show HEAD~N:src/Quotinator.Api/resources/changelog.json
```

Find the commit that added the `translations` blocks and extract the `nl` and `de` content from each release entry. That content goes verbatim (as plain strings, not `{text, machineTranslated}` objects) into the `highlights`, `audienceHighlights.ha-addon`, `added`, `changed`, `fixed`, `removed` arrays of the corresponding releases in `changelog.nl.json` and `changelog.de.json`.

**Note:** the `audienceHighlights.ha-addon` translations were NOT generated in that session — only `highlights` were. The implementer will need to generate `audienceHighlights.ha-addon` translations for `nl` and `de` for all 25 releases that have `ha-addon` content.

---

## Implementation order

1. Delete `ChangelogReleaseTranslation.cs` and `ChangelogTranslationItem.cs`
2. Simplify `ChangelogUnreleased.cs` (remove translation members)
3. Update `ChangelogRoot.cs` and add `ChangelogDocument.cs`
4. Rewrite `IChangelogService.cs` and `ChangelogService.cs`
5. Update `ChangelogEntry.razor` / `.razor.cs` and `ChangelogUnreleasedEntry.razor` / `.razor.cs`
6. Update About / Changelog page for `GetForCulture` and `MachineTranslated` flag
7. Refactor `schemas/changelog.schema.json` (composition + new top-level fields)
8. Update `changelog.csx` (remove translation lookup, simplify helpers)
9. Rename `changelog.json` → `changelog.en.json`; add `language`, `sourceLanguage`, `machineTranslated` to it; strip all `translations` blocks
10. Create `changelog.nl.json` and `changelog.de.json` (recover content from git history)
11. Update tests: delete `GetHighlightsTests.cs`; update `ChangelogSchemaTests` and `RepositoryStructureTests`
12. Update `CLAUDE.md` generator commands
13. Update `Quotinator.slnx`
14. `dotnet build --configuration Release` → 0 warnings, 0 errors
15. `dotnet test --configuration Release` → all tests pass
16. Regenerate `CHANGELOG.md` and `addon/CHANGELOG.md` using the new `changelog.en.json` source
