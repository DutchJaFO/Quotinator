# Testing Policy

Write unit tests for everything where it is relevant and possible. Every service, model method, endpoint handler, and utility should have corresponding tests.

## Framework

- **MSTest** — Visual Studio default

## Project structure

Test projects live under `tests/`, mirroring the source project they cover:

```
tests/
  Quotinator.Api.Tests/
  Quotinator.Core.Tests/
```

## What to test

- All service methods (e.g. `QuoteService`)
- All endpoint handlers
- Model logic / validation (including computed properties like `QuoteResponse.IsTranslated`)
- Utility / helper functions
- **Translation completeness** — `Quotinator.Api.Tests` verifies that all i18n language files have the same keys as the English baseline and no empty values. These tests must pass on every build.

## What to skip

- Pure DI wiring (no logic to assert)
- Razor components whose code-behind is a stub with no logic (every component must have a code-behind file, but testing a pure stub adds no value)

## Translation rules

Every string that appears in Razor markup must come from `@Text.KeyName` — never hardcode English (or any language) directly in `.razor` files. When adding a new UI string:

1. Add the key to `i18ntext/UI.en-GB.json` (the baseline — source of truth)
2. Add translations to `UI.de.json` and `UI.nl.json` in the same commit
3. Reference it in the component via `@Text.KeyName`

The `TranslationCompletenessTests` enforces key parity and non-empty values across all language files. It does **not** detect hardcoded strings in markup — that is a code review responsibility.

## OpenAPI documentation language

The Scalar API reference and OpenAPI spec (`/openapi/v1.json`) are intentionally English-only. OpenAPI 3.1 has no native localisation mechanism for spec content, Scalar has no UI language configuration, and developer tooling is English by convention globally. Do not add translated strings to endpoint descriptions, summaries, or parameter descriptions. Revisit only if the OpenAPI specification or Scalar add native localisation support.
