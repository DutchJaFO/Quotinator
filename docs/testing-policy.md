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
- Render-only Blazor components with no code-behind logic
