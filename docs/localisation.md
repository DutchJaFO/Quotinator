# Localisation

Quotinator supports multiple languages in two distinct areas:

1. **Blazor UI strings** — page text, labels, and navigation, managed with `Toolbelt.Blazor.I18nText`
2. **Quote content** — the quote text and source title stored per-entry in `data/quotes.json`

These are handled differently by design: UI strings change with the user's browser language; quote translations are optional, curated per-entry, and served on request.

---

## Supported languages

| Code | Language |
|---|---|
| `en` | English (American) — default |
| `en-GB` | English (British) |
| `de` | German |
| `nl` | Dutch |

The default language is `en`. ASP.NET Core's request localisation middleware selects the language from the browser's `Accept-Language` header automatically. No configuration is required by the user.

---

## UI strings (`Toolbelt.Blazor.I18nText`)

### Package

`Toolbelt.Blazor.I18nText` — registered in `Program.cs` via `builder.Services.AddI18nText()`.

### Source files

Translation files live in `src/Quotinator.Api/i18ntext/` and follow the naming convention:

```
UI.<language-code>.json
```

| File | Language |
|---|---|
| `UI.en-GB.json` | English — baseline (source of truth) |
| `UI.de.json` | German |
| `UI.nl.json` | Dutch |

### Adding a new UI language

1. Copy `UI.en-GB.json` to `UI.<code>.json` (e.g. `UI.fr.json`)
2. Translate all values — **never leave a value empty**
3. Add the language code to the supported cultures list in `Program.cs`:

```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supported = new[] { "en", "en-GB", "de", "nl", "fr" };
    ...
});
```

The build will fail if any language file has missing or extra keys relative to `UI.en-GB.json` — the translation completeness tests in `Quotinator.Api.Tests` enforce this.

### Using localised strings in a Blazor component

```razor
@inject I18nText I18nText

@code {
    private Quotinator.Api.I18nText.UI Text = new();

    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);
    }
}
```

Then reference strings as `@Text.SomeKey`. The typed class is generated at build time from `UI.en-GB.json` — IntelliSense works.

### Rules

- **Never translate the app name** — `"Quotinator"` is a brand name and must appear as a literal string in components, not as a UI resource key.
- `UI.en-GB.json` is the single source of truth for which keys exist. All other language files must have exactly the same set of keys.

---

## Quote-level translations

Translations are stored directly in `data/quotes.json` alongside each entry:

```json
{
  "id": "...",
  "quote": "Here's looking at you, kid.",
  "originalLanguage": "en",
  "source": "Casablanca",
  "translations": {
    "nl": {
      "quote": "Hier kijk ik naar je, kind.",
      "source": "Casablanca"
    }
  }
}
```

### Schema

| Field | Required | Notes |
|---|---|---|
| `quote` | Yes | Translated quote text |
| `source` | No | Translated source title; omit when the title is identical in the target language |

### Requesting a language via the API

All read endpoints accept an optional `lang` query parameter:

```
GET /api/v1/quotes/random?lang=nl
GET /api/v1/quotes?lang=de
GET /api/v1/quotes/{id}?lang=nl
```

The response always includes three fields so consumers know exactly what they received:

| Field | Meaning |
|---|---|
| `language` | The language actually returned |
| `originalLanguage` | The language the quote was originally recorded in |
| `isTranslated` | `true` when `language ≠ originalLanguage` |

If the requested language has no translation for a given quote, the API falls back to `originalLanguage` silently. `isTranslated` will be `false` in that case.

### Rules

- **Translations are manually curated** — never auto-translate quote content. Inaccurate translations defeat the purpose of the project.
- **Only translate when you have a reliable source** — a published translation of the work, a subtitled official release, or a well-known localised version.
- Most quotes will only have the original entry. That is fine.
- `source` in a translation is optional. Omit it if the source title is unchanged in the target language (e.g. `"Casablanca"` is `"Casablanca"` in every language).

---

## Original language

Every quote has an `originalLanguage` field (ISO 639-1). This defaults to `"en"` and covers the vast majority of the dataset, since most source material is American English.

Non-English originals must set this field explicitly:

```json
{
  "quote": "Hasta la vista, baby.",
  "originalLanguage": "es",
  "source": "Terminator 2: Judgment Day"
}
```

> Note: although this line appears in an English-language film, it is spoken in Spanish. `originalLanguage` reflects the language of the quote text itself, not the language of the source work.

---

## Translation completeness tests

`tests/Quotinator.Api.Tests/I18nText/TranslationCompletenessTests.cs` runs two checks on every build:

1. **Key parity** — every language file has exactly the same keys as `UI.en-GB.json`. Adding a key to the baseline without updating all other files fails CI.
2. **No empty values** — no value in any language file may be blank.

These tests use the i18ntext source files directly via `CopyToOutputDirectory="Always"` in the test project, so no path configuration is needed.
