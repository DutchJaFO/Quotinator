# #269 — Adopt a project-wide pattern for expensive logging arguments (CA1873)

**Status:** Released
**GitHub issue:** #269
**Tiers required:** T1, T2
**Depends on:** #244 (split CA1873 out as its own follow-up, see #244's Step 10)

---

## Background

`CA1873` fires because the standard `ILogger` extension methods (`LogInformation`, `LogDebug`,
`LogCritical`) take a `params object?[]` — every call allocates and boxes that array *before* checking
whether the target log level is even enabled, regardless of how trivial the arguments are. Re-measured
fresh (temporarily escalating `dotnet_diagnostic.CA1873.severity = warning`, clean-rebuilding both
`bin`/`obj` first to avoid incremental-build caching masking stale results, then reverting): **52
occurrences**, matching #244's own 2026-08-08 count exactly — no growth since the issue was filed.
Confirmed by reading every flagged line: this fires even on calls with entirely trivial arguments
(`logger.LogInformation("[Api - GetAllCharacters] page={Page} pageSize={PageSize}", page, pageSize)`)
— it is not about genuinely expensive per-call computation, it is about the unconditional
array-allocation-and-boxing cost the non-generated overloads always pay. `[LoggerMessage]`
source-generated partial methods are the fix that eliminates this — the generated code checks
`IsEnabled` first, before touching any argument — matching official .NET guidance and #244's own
framing of it as "the modern .NET-recommended approach."

**Distribution across the 52 occurrences** (four projects, no test project has any):
- `Quotinator.Api` — 28: mostly `Endpoints/*.cs` (14 near-identical `page={Page} pageSize={PageSize}`
  or `id={Id}` entries across 8 masterdata endpoint files), plus `ImportEndpoints.cs`,
  `ImportRuleEndpoints.cs`, `RequestLoggingMiddleware.cs`, `Program.cs`, `StartupSummaryLogger.cs`
  (including the multi-line closing banner — a compile-time-constant raw string literal, converts the
  same as everything else, just with more parameters).
- `Quotinator.Core` — 9: all in `QuotinatorDatabaseInitializer.cs`.
- `Quotinator.Data` — 13: `DatabaseInitializer.cs` (9), `ManifestSeedPlanner.cs` (2),
  `SourceCacheUpdater.cs` (1).
- `Quotinator.Changelog` — 3: `ChangelogService.cs`.

## Decisions confirmed with the developer (2026-08-09)

**1. First proposal (a `LogMessages` class per project, no shared project) rejected — needed an
explicit documentation section, not just a fix.** Developer feedback: "make sure to document the
logging concepts in a markdown file so we can enforce the correct methods. KISS, DRY & SOLID." This
plan's `docs/logging.md` update (Step 6 below) is written as a standalone, enforceable rule — not a
retroactive note that a conversion happened.

**2. Second proposal (four independent per-project `LogMessages` classes with no shared code) rejected
— real cross-project duplication was about to reappear silently.** Developer feedback: "consider the
use of extension methods and the like so we have something that can be easily extended for our needs.
do remember that it needs to work at both data layer and custom projects, so re-use is critical."
Verified the actual `.csproj` dependency graph by reading every project's `<ProjectReference>` entries:
`Quotinator.Changelog` has **zero** references — it shares no existing project with `Quotinator.Data`
at all (`Quotinator.Core` → `Quotinator.Data` only; `Quotinator.Api` → `Core`, `Data`, `Constants`,
`Changelog`, the three converters; `Constants` and `Data` themselves have no references). A genuinely
shared, reusable set of logging extension methods usable from `Quotinator.Data` (domain-agnostic per
ADR 004) *and* every consumer including `Changelog` cannot live inside any project that already
exists — it needs a new common dependency. This is what produced the two-tier design below.

## Approach

**1. New project `Quotinator.Logging`, zero dependencies** (same structural role as
`Quotinator.Constants` — a small, single-purpose shared project, kept separate from `Constants` since
`[LoggerMessage]`-decorated extension methods are behaviour, not a passive constant list). References
`Microsoft.Extensions.Logging.Abstractions` only. Holds the **genuinely cross-project-reusable**
`[LoggerMessage]` extension methods — the ones whose *shape* (not message text) recurs identically
regardless of which project is calling:
```csharp
namespace Quotinator.Logging;

/// <summary>
/// Shared, cross-project logging message templates whose parameter shape — not message text — recurs
/// identically across Quotinator.Data, Quotinator.Core, Quotinator.Api, and Quotinator.Changelog.
/// </summary>
public static partial class LogMessages
{
    /// <summary>Logs a paginated query entry: subsystem tag plus raw page/pageSize query values.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The `[Subsystem - Phase]` prefix identifying the caller.</param>
    /// <param name="page">The raw, unparsed page query value.</param>
    /// <param name="pageSize">The raw, unparsed pageSize query value.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} page={Page} pageSize={PageSize}")]
    public static partial void LogPageQuery(this ILogger logger, string tag, string? page, string? pageSize);

    /// <summary>Logs an id-keyed query entry: subsystem tag plus the requested id.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The `[Subsystem - Phase]` prefix identifying the caller.</param>
    /// <param name="id">The requested id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} id={Id}")]
    public static partial void LogIdQuery(this ILogger logger, string tag, string id);
}
```

**`:l` only on `Tag`, not on `Page`/`PageSize`/`Id` — verified empirically, not assumed.** A throwaway
test (`logger.LogInformation("... page={Page} pageSize={PageSize}", "2", "20")`, no `:l`, run against
a real Serilog pipeline) confirmed the 15 existing call sites this replaces render their string
arguments **quoted** today (`page="2" pageSize="20"`) — `:l` was never present at any of them. `Tag`
needs `:l` for a different reason: it was never a template argument before (it was baked into the
literal message text, e.g. `"[Api - GetAllSources] page={Page}..."`), so it was never quoted either —
`:l` on the new `Tag` parameter exactly replicates that prior unquoted-literal-text rendering. Adding
`:l` to `Page`/`PageSize`/`Id` as well would have silently changed their rendering from quoted to
unquoted — a real behaviour change this conversion is not scoped to make, and a contradiction of this
plan's own "byte-identical output" verification requirement had it gone unnoticed.
`Quotinator.Data`, `Quotinator.Core`, `Quotinator.Api`, and `Quotinator.Changelog` each add a
`ProjectReference` to `Quotinator.Logging` — a deliberate, justified graph change, and the only new
edge any of them gains.

**2. Each project keeps its own `[Project].Logging.LogMessages`** (`src/Quotinator.Api/Logging/`,
`src/Quotinator.Core/Logging/`, `src/Quotinator.Data/Logging/`, `src/Quotinator.Changelog/Logging/`,
following this project's file-placement rule) for messages whose *text* is genuinely bespoke to that
subsystem — `Import*`/`RequestLoggingMiddleware`/`Program.cs`/`StartupSummaryLogger` (Api);
`QuotinatorDatabaseInitializer`'s seeding messages (Core); `DatabaseInitializer`'s migration/backup
messages, `ManifestSeedPlanner`, `SourceCacheUpdater` (Data); `ChangelogService`'s load messages
(Changelog). Extension methods on `ILogger`, not partial methods inside each calling class — zero
`partial` modifier changes needed on any of the ~15 existing classes that log today; every call site
becomes a one-line swap (`logger.LogInformation("...", args)` → `logger.LogXxx(args)`) with the
rendered output byte-identical to today.

**Deliberately *not* forced into the shared project**: the "count of items processed" messages
(`QuotinatorDatabaseInitializer`'s "seeding complete — {Count} file(s) processed" /
"genre re-seed complete — {Count} genre rows processed", `ChangelogService`'s "{Count} language
file(s) loaded") share a parameter *shape* (tag + count) but describe genuinely different events with
different surrounding phrasing — forcing them into one generic templated method would trade away
clear, specific message text for a DRY win with no real duplication behind it. `LogPageQuery`/
`LogIdQuery` are shared because 15 call sites already say the *exact same thing* modulo tag — that is
real, present duplication; the count messages are not.

**3. No explicit `EventId` assignment.** This project's log-navigation convention is the
`[Subsystem - Phase]` text prefix (`docs/logging.md`), not numeric event IDs — no existing scheme to
extend, and inventing one now (40-some unique numbers to track) adds ceremony with no consumer. Every
`[LoggerMessage]` attribute omits `EventId`.

**4. Every call site updated** to call the new extension method instead of the raw `LogXxx(...)` call
— exact rendered text preserved, including the `{Placeholder:l}` literal-format specifier on every
string parameter (`docs/logging.md`'s existing Serilog-quoting rule).

**5. Escalate `dotnet_diagnostic.CA1873.severity = warning` in `.editorconfig`** once the codebase is
clean of occurrences — appended after the existing `CA1068` entry, matching every other rule #244
escalated.

**6. Document the pattern as an explicit, enforceable rule in `docs/logging.md`** — a standalone
"Logging call-site pattern" section, structured like this project's other centralisation policies
(`Sql.cs`'s SQL policy, `i18ntext/*.json`'s UI string policy in `CLAUDE.md`):

- **The rule, stated directly**: any logging call that takes template arguments must go through a
  `[LoggerMessage]`-decorated extension method — never call `ILogger.LogInformation`/`LogDebug`/
  `LogWarning`/`LogCritical` directly with arguments. A bare, argument-free call (e.g. the opening
  banner) is exempt — CA1873 does not flag those, and there is nothing to allocate ahead of the level
  check.
- **Where a new method belongs, stated as a decision procedure**: first check whether
  `Quotinator.Logging`'s shared `LogMessages` already covers the new call site's *shape* (same
  parameter types, same structural intent — e.g. "a subsystem tag plus a paginated page/pageSize
  pair"). If so, reuse it with a different `tag` argument rather than declaring a near-duplicate
  method. Only when the message text is genuinely specific to one subsystem does it belong in that
  project's own `Logging/LogMessages.cs` instead.
- **Why this is the enforcement mechanism, not just a convention**: `CA1873` is escalated to `warning`
  (Step 5) — the 0-warnings build policy means a future direct `LogInformation(...)` call with
  arguments fails the build immediately, the same way every other escalated analyzer rule in this
  project is enforced. The documentation explains why and how to comply; the analyzer is what actually
  blocks a regression. No separate guard test is needed — `CA1873` already covers this surface
  project-wide.

**`CA1873` never flags `LogWarning`/`LogError`, confirmed by direct experiment, contradicting its own
documented description.** Microsoft's own CA1873 docs describe the rule as firing on "expensive"
logging arguments uniformly across logging methods, with no stated level restriction. A controlled
probe (three calls — `LogWarning`, `LogError`, `LogInformation` — identical `string.Join(...)`
argument, same method, same file) showed only the `LogInformation` call flagged; `LogWarning`/
`LogError` were silent for the exact same "expensive" argument. This matches the original 52-occurrence
measurement exactly (zero of the many `LogWarning`/`LogError` calls across the whole codebase were ever
flagged, regardless of argument shape) — not a coincidence of which arguments those particular calls
happened to use. `docs/logging.md`'s new rule section scopes itself accordingly: only
`LogInformation`/`LogDebug`/`LogTrace`/`LogCritical` are covered, with `LogWarning`/`LogError` called
out as a deliberate, analyzer-driven exception rather than silently omitted.

**A `[LoggerMessage]` conversion alone does not eliminate CA1873 when an argument expression is itself
non-trivial (a method call, not a bare identifier/member access) — confirmed live, not assumed.**
`[LoggerMessage]`'s generated method checks `IsEnabled` *inside* the callee, but C# always evaluates
every argument expression at the call site *before* invoking any method — so `logger.LogFileReport(
fileName, FormatReport(report))` still calls `FormatReport(report)` unconditionally, regardless of
whether the target `[LoggerMessage]` method goes on to skip its own body. Escalating `CA1873` (Step 5)
surfaced this directly: 7 of the 52 converted call sites (`ChangelogService.cs`, `DatabaseInitializer.
cs` ×2, `ManifestSeedPlanner.cs`, `QuotinatorDatabaseInitializer.cs` ×2, `ImportRuleEndpoints.cs`) pass
a `string.Join(...)`/`Path.GetFileName(...)`/`FormatReport(...)`/`LogSanitizer.ForLog(...)` invocation
as an argument and re-triggered the warning even after conversion. Fixed by wrapping each of those 7
call sites in an explicit `logger.IsEnabled(LogLevel.Information)` check — the officially correct
remedy for a call whose argument is genuinely expensive to compute, layered on top of (not a
replacement for) the `[LoggerMessage]` conversion itself. The other 45 call sites needed no such guard
— their arguments are bare identifiers or simple member-access reads (`page`, `pageSize`,
`quotes.Count`), which the analyzer treats as cheap enough not to flag.

**DRY** — the shared `Quotinator.Logging` project holds the two genuinely-repeated shapes exactly
once, reusable from every consumer including the data layer, so the same 15-line duplication this
issue found does not silently reappear next time a paginated endpoint is added anywhere in the
solution. **KISS** — one attribute plus one partial method declaration per distinct message shape,
extension methods on `ILogger` (no new abstraction layer beyond the one small project, no wrapper
interface, no DI registration — `[LoggerMessage]` is a source generator, not a runtime service).
**SOLID** (single-responsibility, at both class and project level) — each `LogMessages` class does
exactly one thing, the shared project's only job is holding cross-cutting logging shapes, and callers
depend on these the same way they already depend on `ILogger` itself.

## Steps

### Step 1 — Plan doc, slnx, overview.md
**Status:** ✅ Done

### Step 2 — Create `Quotinator.Logging` and `Quotinator.Logging.Tests`
**Status:** ✅ Done

Per `docs/testing-policy.md`'s project-pairing rule ("Every project in `src/` has a paired test project
in `tests/`... applies to infrastructure projects exactly as it applies to feature projects... created
in the same commit") — no exception is carved out for a project this small, so
`Quotinator.Logging.Tests` was created alongside it (5 tests), asserting `LogPageQuery`/`LogIdQuery`
render the expected text through a real Serilog sink (`docs/logging.md`'s existing "unit tests must use
Serilog's actual rendering" rule). Both added to `Quotinator.slnx`.

### Step 3 — Add `ProjectReference`s
**Status:** ✅ Done

`Quotinator.Data`, `Quotinator.Core`, `Quotinator.Api`, `Quotinator.Changelog` each reference
`Quotinator.Logging`.

### Step 4 — Per-project `LogMessages` classes + convert all 52 call sites
**Status:** ✅ Done

One `Logging/LogMessages.cs` per project for bespoke messages (`src/Quotinator.Api/Logging/`,
`src/Quotinator.Core/Logging/`, `src/Quotinator.Data/Logging/`, `src/Quotinator.Changelog/Logging/`);
every one of the 52 compiler-confirmed `LogInformation`/`LogDebug`/`LogCritical` calls with template
arguments converted to call either the shared or the project-local extension method (final distribution
by project: Api 29, Data 12, Core 8, Changelog 3 — a few off from the plan's original per-file estimate
but the same 52 total, reconciled against a fresh `dotnet build` capture, not the earlier manual grep).
One real bug caught during conversion: `SourceEndpoints.cs` was initially missed entirely (an `Edit`
was drafted but never applied) — caught by the CA1873 re-escalation still flagging its two untouched
call sites, not by manual review. Rendered output confirmed byte-identical — all 33
`StartupSummaryLoggerTests` pass unchanged, and the `:l`-specifier decision (`Tag` gets it since it
replaces literal text; `Page`/`PageSize`/`Id` do not, since they were never quoted-suppressed at the 15
call sites this replaces — verified empirically, not assumed, via a throwaway Serilog-rendering probe)
is documented directly in `LogMessages.cs`'s own XML docs.

### Step 5 — Escalate CA1873
**Status:** ✅ Done

Escalating surfaced a genuine gap `[LoggerMessage]` alone doesn't close: 7 of the 52 converted call
sites pass a non-trivial expression (`string.Join(...)`, `Path.GetFileName(...)`, `FormatReport(...)`,
`LogSanitizer.ForLog(...)`) as an argument — C# evaluates that expression at the call site regardless of
what the generated method's body does, so `CA1873` re-fired on the exact same 7 locations even after
conversion. Fixed by wrapping each in an explicit `logger.IsEnabled(LogLevel.Information)` check.
Separately confirmed by direct experiment that `CA1873` never flags `LogWarning`/`LogError` in this SDK
version regardless of argument complexity, contradicting the rule's own documented description — see
`docs/logging.md`'s new section for the full finding. Full clean rebuild: 0 warnings, 0 errors.

### Step 6 — `docs/logging.md` update
**Status:** ✅ Done

New "Logging call-site pattern" section: the rule, the shared-vs-project-local decision procedure, the
`IsEnabled`-guard-for-expensive-arguments nuance, and the `LogWarning`/`LogError` scope exception —
each backed by a concrete, verified finding from this issue's own implementation, not asserted blind.

### Step 7 — Full verification (T1, T2) + changelog
**Status:** ✅ Done — T1, T2, and build/test verification all confirmed

Full clean rebuild (`bin`/`obj` deleted first): 0 warnings, 0 errors, `CA1873` escalated. Full test
suite: 3194 tests across all 10 projects, 0 failures, including all 33 `StartupSummaryLoggerTests` and
the new 5 `Quotinator.Logging.Tests`. Grep sweep confirmed the only remaining raw `LogInformation`/
`LogDebug`/`LogCritical` calls are either argument-free (exempt) or `LogWarning`/`LogError` (out of
this rule's scope, per Step 5's finding).

**T1 (2026-08-09):** confirmed by the developer — clean Visual Studio start, normal startup log
output.

**T2 (2026-08-09): full 32-section `docs/smoke-tests.md` pass (the suite is now
`docs/automated-testing/`), all sections verified live against a
rebuilt image.** Given no behaviour change was intended, this pass specifically watched for any
divergence in logged output. One genuine bug was caught and fixed mid-pass: `SourceEndpoints.cs` had
been drafted for conversion but the edit was never applied — its two `LogInformation` calls were still
raw, only surfaced when the CA1873 re-escalation flagged them again during Step 5. No other regression
found. Two real, expected, and harmless observations, documented rather than "fixed":

- **Docker/production console log lines gained a visible `SourceContext[{ Id, Name }]` prefix** for
  every converted call (e.g. `DatabaseInitializer[{ Id: 1849141447, Name: "LogCreatingSchemaAtBaseline"
  }]`). Traced to `Program.cs`'s `{SourceContext}[{EventId:0}]` template segment — already present,
  unchanged by this issue — which previously rendered empty because raw `LogInformation` calls always
  had `EventId = default`, a case Serilog's MEL bridge omits from the log event entirely.
  `[LoggerMessage]` auto-generates a named `EventId` per method, so every converted line now populates
  it — the same pattern the untouched built-in `HttpClient` logger lines already showed. Confirmed
  harmless: every smoke-test log assertion uses substring `grep`, and all of section 29's ordered,
  multi-line log-content checks (`backup complete` → `seeding failed` → `pre-seed backup restored` →
  the FTL message with its attached `SqliteException`) matched exactly.
- Section 2's "curated file re-import produces two pending actions" note is stale (now produces all 13
  — the curated file has grown since that note was written); confirmed unrelated to this issue, since
  the mechanism itself (ambiguousFields, decide/undo/apply) behaved correctly regardless of count.

All log-content assertions in sections 27 (`[Database - Stats]`) and 29 (backup/restore/failure
sequence) — the two sections most directly exercising this issue's converted call sites — matched
byte-for-byte.

---

## Verification

No schema impact, no behavioural change to what is logged or how it renders — matches #244's own
CA-rule steps (mechanical/build-verified, no new production-logic test methods needed beyond
`Quotinator.Logging.Tests`'s own coverage of the two shared methods).

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `Quotinator.Logging` + `Quotinator.Logging.Tests` created, added to `Quotinator.slnx` | Build | Step 2 |
| 2 | ✅ | Four consumer projects reference `Quotinator.Logging` | Build | Step 3 |
| 3 | ✅ | All 52 call sites converted; rendered output byte-identical | Unit test | `StartupSummaryLoggerTests.cs` (33/33) + Step 4 |
| 4 | ✅ | Full build 0 warnings/0 errors with `CA1873` escalated | Build | Step 5 |
| 5 | ✅ | Full test suite green | Unit test | 3194 tests, 0 failures — Step 7 |
| 6 | ✅ | Grep sweep: zero remaining direct templated `LogXxx` calls outside the 5 `LogMessages` classes (or the documented `LogWarning`/`LogError` exception) | Manual | Step 4 |
| 7 | ✅ | `docs/logging.md` documents the enforceable call-site pattern | Manual | Step 6 |
| 8 | ✅ | T1 (developer's own Visual Studio run) | Live | Confirmed clean start, normal startup log output (2026-08-09) |
| 9 | ✅ | T2 (Docker smoke tests) | Live | Full 32-section pass, 2026-08-09 — see Step 7 |

---

## Relationship to existing issues

- **#244** — CA1873's 52 occurrences were found and measured there; Step 10 of that plan split this
  work out explicitly as its own follow-up rather than growing #244 into a logging-pattern redesign.
