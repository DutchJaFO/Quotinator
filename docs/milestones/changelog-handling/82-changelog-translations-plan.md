# Plan: #82 — Changelog: translated highlights for frontend display

## Problem

Once #80 introduces `src/Quotinator.Api/resources/changelog.json` as the source of truth, translated highlights for changelog entries are achievable. The Blazor UI already supports multiple UI languages; changelog highlights on the About page should follow the user's selected language rather than always displaying English.

## Dependency

**#80 must be fully closed before this issue begins.** This plan assumes `changelog.json` exists with the complete schema (including `translations`), `IChangelogService` / `ChangelogService` are reading it, and the `ChangelogEntry` control is in place. The `translations` field is defined and validated in #80 — this issue adds only the resolution logic and frontend wiring.

## Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `translations` is an optional per-entry object in `changelog.json`, keyed by ISO 639-1 code | Mirrors the quote translation model. Entries without translations fall back to English transparently. |
| 2 | Each translation object carries only `highlights` | The generation script produces English-only markdown; technical `added/changed/fixed/removed` arrays remain English-only for developer docs. |
| 3 | Resolution lives in `IChangelogService`: add `GetReleasesForCulture(CultureInfo culture)` | Keeps culture resolution in the service layer, not scattered across Blazor components. Parallel to how `IApiLocalizer` resolves via `CultureInfo.CurrentUICulture`. |
| 4 | `ChangelogRelease.Highlights` remains a `List<string>` | The returned `ChangelogRelease` objects from `GetReleasesForCulture` already carry the resolved highlights — no model change needed. Existing `ChangelogEntry` control renders them without modification. |
| 5 | `About.razor` switches from `ChangelogService.Releases` to `ChangelogService.GetReleasesForCulture(CultureInfo.CurrentUICulture)` | One call-site change; no component interface changes. |
| 6 | Fallback is always English (`highlights` at the top level) | If a translation is absent or `highlights` is empty for the requested culture, fall through to the English array. Same fallback pattern as `IApiLocalizer` and quote content translations. |
| 7 | `scripts/changelog.csx` unchanged — generates English markdown only | Translations are frontend-only; not surfaced in the markdown changelogs. |
| 8 | Schema validation test extended to cover `translations` structure | Ensures translation entries are well-formed arrays, not accidentally null. Does not assert non-empty content — that is a human gate. |
| 9 | At least one `changelog.json` entry has Dutch and German translations | Proof of concept; demonstrates the full path works end-to-end before shipping. |

## `IChangelogService` extension

```csharp
/// <summary>Returns releases with highlights resolved for the given culture, falling back to the top-level English highlights.</summary>
IReadOnlyList<ChangelogRelease> GetReleasesForCulture(CultureInfo culture);
```

`ChangelogService` implements this by:
1. Iterating `_releases` (the already-deserialized list from #80)
2. For each release, checking if `_translationsMap[release.Version][culture.TwoLetterISOLanguageName]` exists and has non-empty `highlights`
3. If yes, returning a copy of the `ChangelogRelease` with `Highlights` replaced by the translated list
4. If no, returning the original release unchanged

The private DTO deserialized from JSON includes the `translations` object. `ChangelogRelease` (the public record) is unchanged.

## Implementation steps

### Step 1 — Confirm schema is complete

`schemas/changelog.schema.json` already includes the `translations` property (defined in #80). Confirm the at-least-one proof-of-concept entry in `changelog.json` has Dutch and German translations. No schema changes needed.

Verify: `ChangelogSchemaTests` still passes; at least one entry has `translations.nl` and `translations.de`.

### Step 2 — Update private DTO in `ChangelogService`

Add `Dictionary<string, ChangelogHighlightsTranslation> Translations` to the private deserialization DTO (where `ChangelogHighlightsTranslation` is an internal record with a `Highlights` list). Wire deserialization via `System.Text.Json`.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 3 — Add `GetReleasesForCulture` to `IChangelogService` and `ChangelogService`

Implement the culture-resolution method as described above. The existing `Releases` property (used by the existing tests) is unchanged.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 4 — Update `About.razor.cs` to use `GetReleasesForCulture`

Replace the call to `ChangelogService.Releases` with `ChangelogService.GetReleasesForCulture(CultureInfo.CurrentUICulture)`. The culture is already available via `CultureInfo.CurrentUICulture` which `RequestLocalizationMiddleware` sets from the cookie/header.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 5 — Add at least one translated entry to `changelog.json`

Add `translations.nl` and `translations.de` highlights to the most recent release entry as a proof of concept. Keep the translations accurate and plain-English (no auto-translation).

Verify: `ChangelogSchemaTests` still passes; translations appear in the JSON.

### Step 6 — Extend schema validation test

Extend `ChangelogSchemaTests` to assert:
- When `translations` is present, each value is an object with a `highlights` array
- No null entries inside the `highlights` arrays of any translation

Verify: `dotnet test --configuration Release --filter ChangelogSchema` passes.

### Step 7 — Browser confirmation: language switching updates highlights

Run the app locally, open the About page in English (default), switch to Dutch using the `LanguageSelector`, confirm the highlights for the proof-of-concept entry change to Dutch. Switch back to English; confirm they revert.

Verify: browser shows correct translated highlight for Dutch; English fallback works for entries with no translation.

---

## Verification table

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `schemas/changelog.schema.json` includes `translations` (defined in #80) and at least one entry has Dutch and German translations | Manual | `ChangelogSchemaTests` passes; at least one `changelog.json` entry has `translations.nl` and `translations.de` |
| 2 | ❌ | Private DTO deserializes `translations`; `ChangelogService` builds correctly | Build | `dotnet build --configuration Release`: 0 warnings, 0 errors |
| 3 | ❌ | `IChangelogService.GetReleasesForCulture` exists; resolves with English fallback | Build + unit test | `ChangelogServiceTests.GetReleasesForCulture_TranslationPresent_ReturnsTranslated` and `_NoTranslation_ReturnsFallback` pass |
| 4 | ❌ | `About.razor.cs` uses `GetReleasesForCulture(CultureInfo.CurrentUICulture)` | Build | `dotnet build --configuration Release`: 0 warnings, 0 errors |
| 5 | ❌ | At least one `changelog.json` entry has Dutch and German translations | Manual | Entry exists in JSON; `ChangelogSchemaTests` passes |
| 6 | ❌ | Schema validation test extended to cover `translations` structure | Unit test | `ChangelogSchemaTests` passes under `dotnet test --configuration Release --filter ChangelogSchema` |
| 7 | ❌ | Switching language to Dutch shows translated highlights for the proof-of-concept entry | Browser | User switches to Dutch in the `LanguageSelector` and confirms the Dutch highlights appear; switching back to English restores English highlights |

---

## What the issue proposes that this plan adds

- Issue proposes `IChangelogService` / `ChangelogService` resolution — this plan specifies the method signature and fallback logic explicitly
- Issue does not specify whether `ChangelogRelease` changes — this plan keeps it unchanged (resolution at service level, not model level)
- Issue does not specify how `About.razor` calls the new method — this plan names the exact call-site change
- Issue mentions `ChangelogEntry` control — this plan confirms no changes are needed to the control itself (it receives already-resolved `ChangelogRelease` objects)
