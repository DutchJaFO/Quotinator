# Testing Policy

Write unit tests for everything where it is relevant and possible. Every service, model method, endpoint handler, and utility should have corresponding tests.

## Framework

- **MSTest** — Visual Studio default

## Project structure

Every project in `src/` has a paired test project in `tests/` with the same name plus a `.Tests` suffix. This applies to infrastructure projects (`Quotinator.Data`, `Quotinator.Changelog`) exactly as it applies to feature projects. When a new `src/` project is created, its `tests/` counterpart is created in the same commit.

```
tests/
  Quotinator.Api.Tests/          # Endpoint integration tests (WebApplicationFactory)
  Quotinator.Changelog.Tests/    # Changelog schema and generation tests
  Quotinator.Core.Tests/         # Unit and service tests
  Quotinator.Data.Tests/         # SQLite integration tests (real DB, no fakes)
```

### CVE folder rule

Both `src/Quotinator.ProjectName/CVE/` and `tests/Quotinator.ProjectName.Tests/CVE/` are created when the project is created — not when a CVE is filed. The folder must exist before it is needed. A `.gitkeep` file holds the folder until the first CVE document is added.

## What to test

- All service methods (e.g. `QuoteService`)
- All endpoint handlers
- Model logic / validation (including computed properties like `QuoteResponse.IsTranslated`)
- Utility / helper functions
- **Translation completeness** — `Quotinator.Api.Tests` verifies that all i18n language files have the same keys as the English baseline and no empty values. These tests must pass on every build.

## Parallel execution

**Default: sequential.** No test project has `[assembly: Parallelize]`. Tests run sequentially within each project unless a class is explicitly opted in.

**Opt-in rule:** add `[Parallelize]` at the class level only when you can positively demonstrate the class is parallel-safe. The bar for "demonstrated safe" is:
- No global state written or read (Dapper type handlers, static caches, singletons)
- No shared filesystem resources (temp dirs, SQLite files) that could collide under concurrent access
- No `SqliteConnection.ClearAllPools()` in cleanup — that is a process-wide operation
- All assertions are on local state only

If you cannot confirm all four, leave the class sequential.

**Global state must only be written once, before tests run.** The only safe place to write global state is `[AssemblyInitialize]` in `MSTestSettings.cs`. Never write global state in `[ClassInitialize]` or `[TestInitialize]`.

**What counts as global state:**
- Any static/singleton mutation: caches, registries, logging sinks, Dapper type handlers.
- `SqlMapper.AddTypeHandler(...)` is the canonical example — Dapper's handler dictionary is a global static. All Dapper type handler registrations live in `[AssemblyInitialize]` in `MSTestSettings.cs` of the project that uses them.

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
