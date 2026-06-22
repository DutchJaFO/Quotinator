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

## Parallel execution

All test projects run with `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` — every test method runs concurrently. This is fast but requires discipline:

**Global state must only be written once, before tests run.** The only safe place to write global state is `[AssemblyInitialize]` in `MSTestSettings.cs`. Never write global state in `[ClassInitialize]` or `[TestInitialize]` — those run in parallel and will race.

**What counts as global state:**
- `SqlMapper.AddTypeHandler(...)` — Dapper's handler dictionary is a global static; registering from multiple `[ClassInitialize]` methods causes intermittent failures under parallel execution. All Dapper type handler registrations live in `AssemblySetup.RegisterTypeHandlers` in `Quotinator.Data.Tests/MSTestSettings.cs`.
- Any other static/singleton mutation (caches, registries, logging sinks).

**Each test must own its own resources.** Database tests create a temp directory and SQLite file in `[TestInitialize]` and delete them in `[TestCleanup]`. Never share a file path or connection between tests.

## Tests must not modify source data

Unit tests must never write to or overwrite the source data they read. Tests must be repeatable: running them a second time must produce the same result as the first. A test that modifies its own input data corrupts the source material and invalidates every subsequent run.

This applies to reference files, seed data, JSON fixtures, and any other file a test reads as its expected input. If a test needs a known starting state, that state is created explicitly at the start of the test (e.g. in `[TestInitialize]` or as a local temp file) and torn down at the end. It is never written to a shared file that other tests or tools also depend on.

## Bug fixes

Every bug fix must be accompanied by tests that close the gap the bug exposed. The requirement applies whether the bug was found in production or during development.

**Mandatory steps (in this order):**

1. **Reproduce the bug with a failing test before writing any fix.** A test that was green before the bug existed and is now red proves the bug is real and gives you a clear pass/fail gate. If a unit test is not possible (e.g. the bug only manifests in a deployed HA add-on), document the exact steps and observed output that reproduce it.
2. **Write the fix.** The test must turn green.
3. **Check for related coverage gaps.** A bug often reveals an untested code path, not just one missing assertion. Ask: what other inputs or states could trigger the same class of failure? Add tests for those too.
4. **All tests from steps 1–3 must be committed in the same PR as the fix.** A fix without a regression test is incomplete.

The test project for the data layer (`Quotinator.Core.Tests`, `Quotinator.Data.Tests`) uses Dapper directly for test setup — the same reason the production data layer does. Add Dapper as an explicit `PackageReference` in any test project that manipulates SQLite state directly.

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
