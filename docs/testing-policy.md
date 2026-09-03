# Testing Policy

Write unit tests for everything where it is relevant and possible. Every service, model method, endpoint handler, and utility should have corresponding tests.

## Framework

- **MSTest** — Visual Studio default

## MSTest analyzer policy

`MSTest.Analyzers` (bundled with the `MSTest` package) ships its own official severity presets as
`MSTestAnalysisMode` values (`None`/`Default`/`Recommended`/`All`) — see the `mstest-*.globalconfig`
files inside the `MSTest.Analyzers` NuGet package. This project pins `<MSTestAnalysisMode>Recommended</MSTestAnalysisMode>`
in `Directory.Build.props` rather than hand-writing per-rule severities in an `.editorconfig`, so the
project automatically picks up whatever future MSTest releases add to their own curated "Recommended"
set instead of silently missing them (issue #197: `MSTEST0068`/`MSTEST0046`/`MSTEST0037` had all been
sitting at the analyzer's default `suggestion` severity — invisible to `dotnet build` at any verbosity,
regardless of whether an `.editorconfig` existed, since nothing had ever escalated them).

**Why not the full `All` mode:** `Recommended` was chosen over `All` after actually measuring the
difference — `All` additionally enables `MSTEST0049` ("flow `TestContext.CancellationToken` into
async calls that accept one") at a severity that, empirically, produced *more* violations on its own
(1,708) than every other currently-relevant rule combined. `Recommended` already includes `MSTEST0049`
at `warning`, so this project already enforces it going forward — the `All` vs. `Recommended`
difference is a small number of stricter analyzer/style preferences, not this rule.

**No project-level `NoWarn` suppressions for any `MSTEST0NNN` rule.** A rule that isn't right for this
project belongs in a documented decision (like this section), not a silent per-project suppression —
the six test projects that suppressed `MSTEST0037` were all found to have zero actual justification
for it once traced back (a copy-pasted "style-only analyzer" comment, not a considered exemption), and
the suppression was removed project-wide when #197 adopted `Recommended` mode fully.

## Project structure

Every project in `src/` has a paired test project in `tests/` with the same name plus a `.Tests` suffix. This applies to infrastructure projects (`Quotinator.Data`, `Quotinator.Changelog`) exactly as it applies to feature projects. When a new `src/` project is created, its `tests/` counterpart is created in the same commit.

```
tests/
  Quotinator.Api.Tests/             # Endpoint integration tests (WebApplicationFactory)
  Quotinator.Changelog.Tests/       # Changelog schema and generation tests
  Quotinator.Constants.Tests/       # Tests for route and constant definitions
  Quotinator.Core.Tests/            # Unit tests for domain logic and in-memory service
  Quotinator.Data.Example/          # Concrete example implementations of Data patterns (not a test runner)
  Quotinator.Data.Testing.Tests/    # Tests for the Data.Testing helper library
  Quotinator.Data.Tests/            # Integration tests for Data infrastructure (real SQLite, no fakes)
  Quotinator.Engine.Tests/          # Integration tests for Engine (SqliteQuoteService, migrations)
```

`Quotinator.Data.Example` is not a test runner — it contains concrete implementations of Data patterns used by test projects as realistic examples. It lives in `tests/` because it has no production use but is not itself an MSTest project.

### CVE folder rule

Both `src/Quotinator.ProjectName/CVE/` and `tests/Quotinator.ProjectName.Tests/CVE/` are created when the project is created — not when a CVE is filed. The folder must exist before it is needed. A `.gitkeep` file holds the folder until the first CVE document is added.

### Database seeder/migration code needs real SQLite integration tests

Any new method added to `DatabaseInitializer`/`QuotinatorDatabaseInitializer` that writes to SQLite
needs a corresponding test that runs against a real, temp-file SQLite database — not only a unit test
that exercises the method in isolation with mocked dependencies. `NoOpDatabaseInitializer` (used in
endpoint tests, see `CLAUDE.md`'s "Endpoint test pattern") deliberately skips all real DB logic, which
means a bug in the seeder itself — an FK constraint failure, a Guid-casing mismatch, a migration that
throws — is invisible to the rest of the test suite and only surfaces at actual runtime.

**Why:** a Guid case mismatch (`guid.ToString()` producing lowercase while a write path stored
uppercase) caused a live `SQLite Error 19: FOREIGN KEY constraint failed` on first startup, while every
one of the (then) 155 tests passed — the bug was only ever caught by actually running the app.

**How to apply:** use a temp directory and a real SQLite file created in `[TestInitialize]`, call
`SqliteConnection.ClearAllPools()` in `[TestCleanup]` before deleting it (pooled connections otherwise
hold the file open), and see `DatabaseInitializerTests.cs` for the established pattern.

## What to test

- All service methods (e.g. `QuoteService`)
- All endpoint handlers
- Model logic / validation (including computed properties like `QuoteResponse.IsTranslated`)
- Utility / helper functions
- **Translation completeness** — `Quotinator.Api.Tests` verifies that all i18n language files have the same keys as the English baseline and no empty values. These tests must pass on every build.

## Every test proves the positive result as well as the negative

A test that only asserts a failure passes against a build that fails everything. Whatever a test
provokes, it must also establish what the working case looks like — otherwise the suite reports
coverage it is not providing.

Found live in #348: four T2 documents each sabotaged the backup path and asserted the resulting
`409 Conflict`. All four were green, and **all four would have stayed green against a build that
refused every reset**, because not one of them contained a passing case.

**For a document or test that provokes a fault, the positive control is normally removing the sabotage
and confirming success in the same environment** — which conveniently does a second job: it proves the
remedy the failure message *names* actually resolves the condition, rather than being advice nobody
checked. #348's four became: delete the unreadable file → `200`; give the volume room → `200`; remount
the directory writable → `200`.

**For unit tests the same rule is cheaper** — a success control beside the failure variants, the
below-threshold case beside the above-threshold one, the override proceeding beside the refusal that
does not.

This is not the same as showing an assertion *can* fail (the mutation step under "Bug fixes" above), and
neither substitutes for the other: mutation proves the test is wired to the behaviour, a positive
control proves the behaviour is not simply failing everywhere.

## Red first means signatures first — a test that cannot compile is not a red test

**Always test red first.** In a compiled language that has a consequence worth stating, because it is
where the rule quietly fails: a test referencing a type, method or property that does not exist yet does
not go red, it breaks the build, and nothing in the project runs at all. There is no way to write the
test first and observe it failing without something for it to bind to.

So the order for new behaviour is three steps, not two:

1. **Create the signature only** — the class with its properties, the method with its parameters and
   return type, throwing `NotImplementedException` or returning a default. No behaviour.
2. **Write the test, run it, and watch it fail** — on the assertion, not on the build.
3. **Implement.**

**The trap is step 1 sliding into step 3.** Having opened the file to add a signature, writing the real
body is one keystroke away and feels like the same task — and then the test passes on its first run and
has proven nothing. Found live in #375 (2026-09-03) on a four-line pure function: `EntityIdentity.SeasonId`
was implemented while its signature was being added, and all four of its tests were green before they had
ever been red.

**If it happens anyway, mutation is the recovery, not a substitute.** Break the implementation in the
specific way the test exists to catch, confirm that test fails while its siblings still pass, then revert.
That establishes the test is wired to the behaviour, which is most of what the red run would have shown —
but it does not establish that the test was written against the requirement rather than against the code
already in front of you, and only writing it first does that.

## Red first applies to automated tests, not only unit tests

An automated (T2) document added for an issue is a test, and it goes red before it goes green like any
other. Writing it and then running it against the finished build establishes that something happens; it
establishes nothing about whether the document would have caught the absence it exists for. Those are
different claims, and only the second is worth the run.

The mechanics are the canary already described under *Bug fixes* below — `git worktree add` the commit
before the work started, `docker build` it under a distinct tag, run the document's own steps, confirm
it fails where it should, then remove the container, image and worktree. **That procedure is not
bug-specific**: a new feature's document needs it for exactly the same reason, and a new feature is
where it is easiest to skip, because there is no "before" behaviour anyone remembers wanting to see.

Found live in #304, where a new live document was written, run green, and reported as verification — it
was only afterwards run against a pre-issue build, which showed the reset producing no notification at
all and the field one step asserted not existing. That run is what made the document evidence.

**Unit tests stay the first choice**, for the reasons in *Prefer verification that needs no live
environment* in `docs/automated-testing/README.md`: they run on every build and cost nothing to repeat.
The live tier covers what genuinely cannot be reached in-process — and when a requirement turns out to
be unobservable there *too*, that is a finding about the design rather than about the test. A value
visible only in rendered HTML usually needs to reach an API response before any live test can assert it.

## A distinction the code makes is a distinction that can be proven

If the application distinguishes two states, the means to reach both exists — so a member, branch or
outcome with no test is a gap to close, never a reason to remove the distinction.

**Do not merge or delete a case because testing it looks inconvenient**, and do not argue for merging
from the fact that two cases produce the same user-facing text today: a distinction describes a *cause*,
and identical wording now says nothing about whether it should stay identical.

Found live in #348 (developer decision, 2026-08-28), where three `BackupOutcome` members had no test and
merging two of them was proposed on exactly that reasoning. Pursuing them instead found two defects
worse than the one the issue was filed for — a reset that destroyed the database and returned `200`, and
a pre-flight check that succeeded without testing anything, because `Directory.CreateDirectory` is a
no-op on a directory that already exists and returns happily on a read-only mount.

**Before concluding a case is unreachable, check what replication tooling already exists.** Most of it
does: `scripts/testing/test-env.csx`'s `--read-only`, `--read-only-data` and `--tmpfs-data` flags,
`scripts/testing/sqlite-storage-probe.csx`'s measured storage-failure techniques, and the
`docs/automated-testing/` environment profiles. A case that genuinely resists both a unit test and a
container is reported as unreachable *with what was tried*, never quietly left untested.

## Parallel execution

**Default: sequential.** No test project has `[assembly: Parallelize]`. Tests run sequentially within each project unless a class is explicitly opted in. See [ADR 006](architecture-decisions/006-sequential-test-execution-by-default.md) for the rationale — this policy exists because of observed flaky test failures caused by concurrent execution of tests that touch process-wide state.

**Opt-in rule:** add `[Parallelize]` at the class level only when all four of the following are true:
1. No global state written or read (Dapper type handlers, static caches, singletons)
2. No shared filesystem resources — each test creates its own isolated temp directory and SQLite file in `[TestInitialize]` and deletes it in `[TestCleanup]`
3. No `SqliteConnection.ClearAllPools()` in cleanup — that is a process-wide operation
4. All assertions are on local, test-owned state only

If you cannot confirm all four, leave the class sequential. The friction is intentional.

**Cross-project: also sequential, via `-m:1`.** ADR 006 governs concurrency *within* a project. At the
solution level MSBuild runs the test projects concurrently, which ADR 006 does not cover — so the
documented full-suite command is `dotnet test --configuration Release --verbosity normal -m:1`. Drop the
flag and CPU contention widens timing windows, producing failures that vanish when a project is run alone
(exactly the symptom ADR 006's own Context describes).

**`-m:1` is a reproducibility measure, not a correctness fix.** A test that passes only because projects
ran one at a time is still broken; the flag just stops the noise from masking which. #313 is the worked
example: cross-project contention surfaced an intermittent `Quotinator.Api.Tests` failure, and the actual
cause was a real unguarded startup race — every test constructed its `WebApplicationFactory` and issued
requests before the app had finished starting, so responses could come from the startup wait page instead
of the endpoint. Measured on an idle machine, the unguarded factory saw startup incomplete on **5 of 5**
runs; most tests passed only because the HTTP round-trip happened to outlast startup. That was fixed on
its own terms (`QuotinatorWebApplicationFactory`, which waits for `StartupPhaseState.IsComplete`, plus a
source-scanning guard test that stops a new test file reintroducing the bare factory). Treat an
intermittent failure as a defect to diagnose, never as noise to suppress.

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

   **Negative/absence assertions need a canary, not just a red-before-fix run — and this applies equally to live/T2 smoke-test verification, not only unit tests.** A check of the form "X is not present" (a unit test asserting a message no longer contains a specific string, or a live smoke-test step confirming a response no longer shows old wording) can go red-then-green against the real bug while still being weak — it never proves the thing that's supposed to be present instead (e.g. an interpolated parameter) actually is, and for a live check specifically, it never proves the *same repro steps* would actually have caught the original bug rather than happening to already avoid it.

   For a unit test: after the real fix is green, deliberately mutate the fixed code to reintroduce a *plausible* variant of the bug (e.g. drop a string interpolation entirely and hardcode a value instead), confirm the test fails with a clear assertion message, then revert the mutation (`git checkout` the file — never leave it in) and reconfirm green.

   For a live/T2 smoke-test step: run the *exact same* repro steps against a build from the commit immediately before the fix (e.g. `git worktree add <path> <pre-fix-sha>`, `docker build` from that worktree with a distinct tag), confirm the old/broken behaviour actually appears, then tear down the canary worktree/image/container and re-confirm the real fix's build still shows correct behaviour. This is on top of the red-before-fix run in step 1, not a replacement for it — it validates the *test or smoke-test method's* sensitivity, not the fix's correctness.
2. **Write the fix.** The test must turn green.
3. **Check for related coverage gaps.** A bug often reveals an untested code path, not just one missing assertion. Ask: what other inputs or states could trigger the same class of failure? Add tests for those too.

   **And fix every instance of the defect's shape, not only the one observed failing.** Grep the solution for the *expression*, the call pattern, the format string — not just the file the bug was reported in. A defect that appeared once by chance usually exists wherever the same code was written, and the remaining instances tend to *look* correct, because they pass for an accidental reason.

   Two live cases, both in #304 (2026-08-30). A PowerShell `.Count` on an unwrapped single-object result printed empty; the fix went in at the one site that failed, while three others stayed broken — they return `0` correctly for the empty case and only fail when the expected value is exactly one, which is precisely what a well-targeted assertion produces. And a UTC-rendered-as-local timestamp was fixed in `NotificationTable` while `DatabaseStatsSummary` kept the identical bug, found only because the developer asked whether an existing helper had been looked for.

   Where the same expression is repeated across call sites, extract it once rather than fixing it twice: a repeated expression is how the second instance came to exist, and leaving it repeated is how the third will.
4. **All tests from steps 1–3 must be committed in the same PR as the fix.** A fix without a regression test is incomplete.

The test project for the data layer (`Quotinator.Core.Tests`, `Quotinator.Data.Tests`) uses Dapper directly for test setup — the same reason the production data layer does. Add Dapper as an explicit `PackageReference` in any test project that manipulates SQLite state directly.

## Test fixture conventions

### A GUID/id case-insensitivity fixture must contain a hex letter

Any test proving a value round-trips correctly regardless of stored casing (`UPPER(...)` in SQL,
`.ToUpper()`/`.ToUpperInvariant()` in C#, a hand-typed "mixed-case" fixture) must use a GUID/id literal
containing at least one alphabetic hex character (`a`–`f`, either case). An all-digit literal — e.g.
`"11111111-1111-4111-8111-111111111111"` — is unchanged by `UPPER()`/`ToUpper()`, so the test silently
proves nothing about case-insensitivity even though it looks like a real one and passes.

**Why:** found live in #213 — a new repository test used exactly that all-digit literal as the
"uppercase" fixture for an id column. Fixed by switching to a literal like
`"aabbccdd-1234-4abc-8def-1234567890ab"` (contains `a`–`f`). Applies to every `*Id`-suffixed column
under [ADR 012](architecture-decisions/012-canonicalize-entity-ids-at-capture.md)'s case-insensitivity
convention. Prefer a literal that *starts* with a hex letter so the mismatch is impossible to miss on
a glance.

## Endpoint test conventions

Two gaps found live during #163's T2 pass turned out not to be one-off mistakes but missing conventions — each had already been solved once elsewhere in the codebase, but nothing forced the next endpoint to inherit the fix.

### File-upload endpoints need a genuinely bodyless-request test

Any endpoint accepting `IFormFile`/`[FromForm]` input must have a test that sends a request with **no body and no `Content-Type` at all** — not just an empty multipart form. Minimal API's automatic form binding requires a form content-type to even attempt binding; a request with none fails at the framework's own routing/binding layer, not as a normal thrown exception, bypassing `BadRequestExceptionHandler` and producing a bare, uninformative `400` instead of the endpoint's own validation message. An empty-form test built with `MultipartFormDataContent` (e.g. a `BuildForm(includeFile: false)`-style helper) does **not** exercise this path — it still sends a real, if empty, multipart body, so it never reaches the genuinely bodyless case.

Test it with `client.PostAsync(url, content: null)` — see `ImportEndpointTests.Import_NoBodyAndNoBatchId_Returns422` for the established pattern. This gap shipped on `POST /import` first (fixed by binding `HttpRequest` manually and checking `HasFormContentType` before reading the form), then shipped again on a newer file-upload endpoint (`POST /import/actions/bulk-decide`, #163) — the fix pattern already existed in the codebase, but no test existed yet on the new endpoint to require it. When adding any new file-upload endpoint, copy the bodyless-request test alongside it from the start, not after a bug report.

### JSON round-trip tests must exercise the app's actual serialization configuration, not bare `JsonSerializer`

A test asserting that data round-trips through JSON across an HTTP boundary (e.g. "endpoint A's response parses back into a valid request for endpoint B") must go through the real pipeline — a `WebApplicationFactory` client call — or explicitly pass the `JsonSerializerOptions` the app actually registers (`Program.cs`'s `ConfigureHttpJsonOptions`, currently camelCase). A test that calls `JsonSerializer.Serialize`/`Deserialize` directly with no options on both the write and read side will silently agree with itself on `System.Text.Json`'s case-sensitive, PascalCase-only library default — proving nothing about whether the real app can round-trip its own output, since the real app's HTTP responses are camelCase.

This class of bug is invisible to any in-process test that bypasses the framework's real JSON configuration — only a live HTTP round trip (T2, or a `WebApplicationFactory`-backed test using the app's real `HttpClient`) can catch it. If a unit-test-level round trip is used for speed instead, it must explicitly pass the app's real `JsonSerializerOptions`, never the library default. #163's export→bulk-decide round trip is the concrete case this rule comes from: the unit-test-level round trip used bare `JsonSerializer` calls and stayed green throughout development, while the identical round trip failed immediately over a real HTTP request until the mismatch was fixed.

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
