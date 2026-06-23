# Plan: #82 — Changelog: translated highlights for frontend display

> **STATUS: Complete. Closed 2026-06-23.**
> The embedded-translation approach (single `changelog.json` with `translations` objects per release) was replaced with a per-language file model matching the `UI.*.json` i18nText pattern.
> Implementation followed **[82-per-language-files-handover.md](82-per-language-files-handover.md)**. Shipped in v1.6.0. Formally closed 2026-06-23 against v1.6.2.
> The original decisions and superseded verification table below are kept for historical context only.

## Problem

Once #80 introduces `src/Quotinator.Api/resources/changelog.json` as the source of truth, translated highlights for changelog entries are achievable. The Blazor UI already supports multiple UI languages via `Toolbelt.Blazor.I18nText` and the `LanguageSelector` control. Changelog highlights on the About page should follow the user's selected language rather than always displaying English.

## Dependency

**#80 must be fully closed before this issue begins.** This plan assumes `changelog.json` exists with the complete schema (including `translations`), `IChangelogService` / `ChangelogService` are reading it, and `ChangelogEntry` / `ChangelogUnreleasedEntry` controls are in place.

## Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `translations` is an optional per-entry object in `changelog.json`, keyed by ISO 639-1 code | Mirrors the quote translation model. Entries without translations fall back to English transparently. |
| 2 | Each translation object carries only `highlights` | The generation script produces English-only markdown; technical `added/changed/fixed/removed` arrays remain English-only for developer docs. |
| 3 | Resolution lives on the model: `ChangelogUnreleased.GetHighlights(string? culture)` | `ChangelogUnreleased` is the base class for both released and unreleased entries. One method covers both. Components call `entry.GetHighlights(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)` directly — no service method or object copying needed. |
| 4 | `ChangelogEntry` and `ChangelogUnreleasedEntry` both call `GetHighlights(culture)` | Both components render highlights; both must respect the user's language. Culture is read from `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName` in each code-behind. |
| 5 | Fallback is always the top-level `Highlights` | If a translation is absent, its `Highlights` list is empty, or `culture` is null, fall through to the English array unchanged. Same fallback pattern as `IApiLocalizer` and quote content translations. |
| 6 | `scripts/changelog.csx` unchanged — generates English markdown only | Translations are frontend-only; not surfaced in the markdown changelogs. |
| 7 | Schema validation test extended to cover `translations` structure | Ensures translation entries are well-formed arrays, not accidentally null. Does not assert non-empty content — that is a human gate. |
| 8 | At least one `changelog.json` entry has Dutch and German translations | Proof of concept; demonstrates the full path works end-to-end before shipping. |
| 9 | `IChangelogService` unchanged | Resolution is on the model — no new service method, no object copying. |
| 10 | `Translations` is already on `ChangelogUnreleased` (public, deserialized directly) | The #80 model redesign put `Translations` as a public `init` property on `ChangelogUnreleased`. No DTO work needed. |

## `GetHighlights` method

Added to `ChangelogUnreleased`:

```csharp
public IReadOnlyList<string> GetHighlights(string? culture)
{
    if (culture is not null
        && Translations.TryGetValue(culture, out var translation)
        && translation.Highlights.Count > 0)
        return translation.Highlights;
    return Highlights;
}
```

## Implementation steps

### Step 1 — Confirm schema is complete

`schemas/changelog.schema.json` already includes the `translations` property. Confirm the at-least-one proof-of-concept entry in `changelog.json` has Dutch and German translations. No schema changes needed.

Verify: `ChangelogSchemaTests` passes; at least one entry has `translations.nl` and `translations.de`.

### Step 2 — Add `GetHighlights` to `ChangelogUnreleased`

Implement as above. Both ideal (translation present) and not-ideal (fallback) unit tests added to `Quotinator.Changelog.Tests`.

Verify: `dotnet test --configuration Release`: 0 failures.

### Step 3 — Update `ChangelogEntry` and `ChangelogUnreleasedEntry`

In each code-behind, add `private string Culture => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;`. In each Razor, replace `Release.Highlights` / `Unreleased.Highlights` with `Release.GetHighlights(Culture)` / `Unreleased.GetHighlights(Culture)`.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 4 — Add proof-of-concept translations to `changelog.json`

Add `translations.nl` and `translations.de` highlights to at least one release entry.

Verify: `ChangelogSchemaTests` passes; translations appear in JSON.

### Step 5 — Extend schema validation test

Extend `ChangelogSchemaTests` to assert that when `translations` is present, each value has a `highlights` array with no null entries.

Verify: `dotnet test --configuration Release --filter ChangelogSchema` passes.

### Step 6 — Browser confirmation

Run the app locally, open About page, switch to Dutch, confirm translated highlights appear for the proof-of-concept entry. Switch back to English; confirm fallback works.

---

## Verification table

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `schemas/changelog.schema.json` includes `translations`; at least one entry has `translations.nl` and `translations.de` | Manual | `ChangelogSchemaTests` passes |
| 2 | ❌ | `ChangelogUnreleased.GetHighlights(culture)` returns translated highlights when present; falls back to English when absent | Unit test | `GetHighlights_TranslationPresent_ReturnsTranslated` and `GetHighlights_NoTranslation_ReturnsFallback` pass |
| 3 | ❌ | `ChangelogEntry` and `ChangelogUnreleasedEntry` use `GetHighlights(Culture)` | Build | `dotnet build --configuration Release`: 0 warnings, 0 errors |
| 4 | ❌ | At least one `changelog.json` entry has Dutch and German translations | Manual | Entry exists in JSON; `ChangelogSchemaTests` passes |
| 5 | ❌ | Schema validation test covers `translations` structure | Unit test | `ChangelogSchemaTests` passes |
| 6 | ❌ | Switching language to Dutch shows translated highlights for the proof-of-concept entry; English restores on switch back | Browser | User confirms in `LanguageSelector` |
