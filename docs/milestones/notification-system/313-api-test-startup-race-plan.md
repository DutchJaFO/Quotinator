# #313: Api tests can silently assert against the startup wait page instead of the endpoint under test

**Status:** Waiting for release
**GitHub issue:** #313
**Tiers required:** None
**Depends on:** #280

---

## Background

Found while verifying #312 (2026-08-15). A `GetAllConversations_PageZero_Returns422` failure appeared in
a solution-level run and passed on re-run; it was initially written off as a flake, which was wrong.

Since #280, Kestrel listens *before* startup initialisation finishes. Until
`StartupPhaseState.MarkComplete()` runs, `StartupWaitMiddleware` answers every non-exempt request with
`200 OK` and an HTML wait page: its exempt list is only `ApiRoutes.Health` and `ApiRoutes.Version`.
`WebApplicationFactory.CreateClient()` returns once the host is built, not once `Program.cs`'s
post-`StartAsync` work has finished, and **no test in the project waits for readiness**: all ~690 assert
immediately.

The intermittent red is the visible symptom. The real defect is the silent green: a test expecting
`200` passes against the wait page while asserting nothing about the endpoint it names.

## Authoritative-source cross-check

- **ADR 006** (sequential test execution by default), verified compliant *before* blaming parallelism:
  all eleven test projects carry `[assembly: DoNotParallelize]`, none carries `[assembly: Parallelize]`,
  no class opts in. Within-project execution is already sequential, so this is not an ADR 006 violation.
  ADR 006 governs *within-project* concurrency only and says nothing about solution-level runs, but its
  own Context records that the flake motivating it "only appeared when all test projects ran
  simultaneously", which is exactly the condition here. Step 4 closes that gap explicitly rather than
  leaving it to an unstated default.
- **#280's plan doc**: the listen-before-initialised model is deliberate and stays; the wait page is the
  intended behaviour for a real user hitting a starting app. Nothing here changes production behaviour.
- **`StartupWaitMiddlewareTests`**: constructs `StartupPhaseState` directly rather than through a
  factory, so it is unaffected by this change and keeps testing the middleware in isolation.

No conflict found.

## Design

### Wait once per factory, not once per client

`StartupPhaseState` is a singleton: once complete it stays complete. So the wait belongs to the
*factory*, not to each of the 376 `CreateClient()` call sites; making it per-call would be 376 edits
and would re-poll pointlessly on every one.

A shared `QuotinatorWebApplicationFactory : WebApplicationFactory<Program>` overrides `CreateHost`,
starts the host as usual, then blocks (bounded) until the app reports startup complete. Every
`CreateClient()` on that factory is then safe by construction, and all 376 call sites stay untouched;
only the 23 `new WebApplicationFactory<Program>()` construction sites change.

### Poll the state, not the HTTP endpoint

The issue text proposed polling `GET /api/v1/health` until it stops reporting `"starting"`. Reading
`StartupPhaseState.IsComplete` from the host's own DI container is strictly better: it is the exact flag
the middleware itself branches on, rather than a proxy for it, and it needs no HTTP round trip inside a
factory-construction path. `InternalsVisibleTo("Quotinator.Api.Tests")` is already configured, so the
internal type is reachable.

The wait is **bounded**: a genuinely stuck startup must fail the test with a clear message rather than
hang the suite.

### Sequential solution-level runs are a second, separate measure

`-m:1` removes the cross-project CPU contention that widens the race window. It is deliberately *not*
treated as the fix: it would mask the unguarded race rather than close it. Both land; the guard makes
tests correct, the flag makes runs reproducible.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

### 2. `QuotinatorWebApplicationFactory` with a bounded readiness wait
**Status:** ✅ Done

### 3. Point the 23 factory-construction sites at it
**Status:** ✅ Done

**Planned as its own commit, landed with steps 2/4/5 instead: the split was not achievable.** The
intent was to keep the mechanical change separate so the helper stayed reviewable on its own. But the
guard test from step 5 fails while any file still constructs the bare factory, and the mechanical change
cannot precede step 2 because it references a type that would not yet exist. Either ordering produces an
intermediate commit that does not pass, the same trap #312's steps 2/4 hit. One commit, deliberately.

### 4. `-m:1` for solution-level runs, documented
**Status:** ✅ Done

`CLAUDE.md`'s Commands section and Pre-Push Checklist, plus `docs/testing-policy.md`.

### 5. Regression test
**Status:** ✅ Done

Proves the guard actually holds: `StartupReadinessTests.CreateClient_ReturnsOnlyAfterStartupIsComplete`
asserts `StartupPhaseState.IsComplete` at the moment the factory hands back a client. That flag is the
condition itself, so the test does not depend on winning or losing the race.

### 6. Full verification
**Status:** ✅ Done

Three consecutive full-solution runs with `-m:1`, all green (3,392 tests each). Repeated deliberately:
the failure this issue fixes was intermittent, so one green run demonstrates nothing.

**No T1/T2/T3 tier applies.** This issue changes test-harness code and documentation only, not one line
of `src/`. There is no runtime behaviour to confirm in Visual Studio, Docker, or a live add-on.

**The guard was proved load-bearing before being trusted, not assumed to be.** A throwaway canary test
(added, run, then removed) constructed the *unguarded* `WebApplicationFactory<Program>`, took a client,
and asserted `StartupPhaseState.IsComplete`. It failed **5 times out of 5**, on an idle machine, running
a single test, with no competing projects.

That result is worse than the issue's own framing. This was never a rare race: **every** Api test has
been starting before the app was ready, all along. They passed because the HTTP round-trip usually
outlasts the remaining startup work. That is luck with a comfortable margin, not correctness. Cross-project
contention did not create the defect; it merely consumed the margin until the defect became visible once.
Which is also why `-m:1` alone would have been the wrong response: it would have restored the margin and
re-hidden the problem.

### 7. Define the rule set and its fixtures, and show every layer red
**Status:** ✅ Done

Found 2026-09-24, re-verifying this issue against its own spec. `NoTestConstructsTheUnguardedWebApplicationFactory`
matches one literal: `new WebApplicationFactory<Program>()`. A temporary file constructing the bare
factory as `WebApplicationFactory<Program> f = new();` left it green; the same file in the classic
spelling turned it red. The target-typed form is the one IDE0090's boyscout rule rewrites every
explicitly-typed construction into. No file uses it today: the coverage holds, the guard does not. The
guard also had no positive control: nothing in any run showed its match could find anything, which
`docs/automated-testing/README.md` requires of a negative assertion.

A wider text match would repeat the defect: it recognises spellings, and the question is what gets
constructed. So the guard becomes three independent layers over one rule set, and none of them is
allowed to assume a shape "would not be written by accident" (developer decision, 2026-09-24).

**The rule set** is `UnguardedFactoryUsageKind`, in `tests/Quotinator.Api.Tests/Enums/`:

| Kind | What it catches |
|---|---|
| `DirectConstruction` | Construction of `WebApplicationFactory<Program>`, or of any type deriving from it other than the guarded factory, in any spelling: classic, qualified, aliased, target-typed in any position |
| `UnguardedSubclass` | Any type deriving from `WebApplicationFactory<Program>` other than `QuotinatorWebApplicationFactory` |
| `GenericConstruction` | The bare type passed as a type argument to a method whose type parameter carries a `new()` constraint, or to `Activator.CreateInstance<T>` |
| `TypeObtained` | `typeof(WebApplicationFactory<Program>)`, the entry point to every reflection-based construction |
| `ReflectionName` | A string holding the reflection name `WebApplicationFactory\`1`, the route through `Type.GetType` |

**The fixtures** live beside the tests as `.cs.txt` files. They are copied to the output and never
compiled into the test project, so they cannot trip the guard over the real sources. One file per case, each compiled
together with a fixture-local sealed `GuardedFactory`, which stands in for `QuotinatorWebApplicationFactory`
(the real one is internal to this assembly):

- **Violations** (21): classic; fully qualified; whitespace inside the generic; target-typed local,
  `using` declaration, field, property initialiser, expression-bodied return, method argument, lambda
  body and collection element; a `using` alias; a subclass declared, and declared and constructed; a
  generic subclass closed over `Program` only at construction; a `new()`-constrained helper;
  `Activator.CreateInstance<T>` called and as a method group; `Activator.CreateInstance(typeof(...))`;
  `Type.GetType` by reflection name, in a plain and an interpolated string.
- **Allowed** (7): the guarded factory, classic and target-typed; a `WithWebHostBuilder` result held
  in a variable typed as the bare factory; the bare type as a parameter and a collection element; the
  guarded factory through a `new()`-constrained helper; the bare factory's construction written in a
  comment and an ordinary string; `typeof` of the guarded factory and of the open definition.

Each violation's verdict is the exact set of kinds expected, so over-reporting fails as surely as missing.

Signatures first: `UnguardedFactorySourceAnalysis.Analyse` and `UnguardedFactoryAssemblyAnalysis.Analyse`
returned an empty `UnguardedFactoryUsageResult`, and `UnguardedFactoryRuntimeGuard` installed nothing.
Red run, 2026-09-24: 53 of 71 failed, every one on an assertion rather than a fixture failing to compile.
They were all 42 violation verdicts, the three guarded-construction counts, both real-project positive controls,
`GuardedFactory_IsSealed`, and all five runtime-guard tests. The 18 that passed were the allowed rows and
the real-project "found nothing" rows, which is why those count only once steps 8 and 9 have shown them
staying green against a real implementation.

### 8. Implement the source analysis (layer B)
**Status:** ✅ Done

Roslyn with a full semantic model. It resolves each object creation, base type, generic instantiation,
`typeof` and string to what it refers to, so spelling cannot matter. Adds `Microsoft.CodeAnalysis.CSharp`
to `Directory.Packages.props`, referenced by this test project only and never shipped.

**The compilation is the build's own, not an approximation.** The project uses `[GeneratedRegex]`, so
compiling its sources without the build's source generators fails to bind. A `WriteCompilationInputsForFactoryGuard`
target in the project file records what `CoreCompile` actually received (sources with the generated
global usings, references, analyzers and defines) to `compilation-inputs/` beside the test assembly, and
the analysis compiles from exactly that, running the same generators.

A semantic model over code that does not compile resolves nothing and would report nothing, so the
analysis is held to two positive controls on the real sources: zero error diagnostics, and at least one
construction of `QuotinatorWebApplicationFactory` resolved. The first caught a real divergence at once:
581 errors, because the analysis compilation had its own assembly name and `InternalsVisibleTo` admits
only `Quotinator.Api.Tests`. It now uses the real name.

### 9. Implement the assembly analysis (layer A)
**Status:** ✅ Done

Reflection over the compiled test assembly plus a walk of every method body's IL, using the base-class
library only. It sees what the compiler actually emitted: `newobj` against the bare factory's constructor, a
method instantiation carrying the bare type, `ldtoken` of the bare type, `ldstr` of the reflection name,
and every type's base chain. The fixtures reach it by emitting each one to an in-memory assembly, so
layer A and layer B judge exactly the same code, and every fixture must receive the same verdict from both.

Positive control on the real assembly: at least one `newobj` of `QuotinatorWebApplicationFactory` found,
and no operand left unresolved. An IL token the analysis could not resolve is a read failure, never a
silent skip.

Both static layers shown wired against the real project, not only against fixtures: a temporary test file
holding `WebApplicationFactory<Program> factory = new();` (the spelling the old guard missed) failed
`RealProject_Source_HasNoUnguardedFactoryUsage` (`ZzMutation.cs:9`) and
`RealProject_Assembly_HasNoUnguardedFactoryUsage` (`ZzMutation.Make IL_0000`). Removed afterwards.

### 10. Seal the guarded factory
**Status:** ✅ Done

A subclass of `QuotinatorWebApplicationFactory` overriding `CreateHost` would skip the wait while passing
every rule above. Sealing it removes that route structurally, and `GuardedFactory_IsSealed` pins it.

### 11. Add the runtime guard (layer C)
**Status:** ✅ Done

What neither static layer can see: a factory constructed from a `Type` known only at run time. The test
assembly subscribes in `[AssemblyInitialize]` to the `Microsoft.Extensions.Hosting` `DiagnosticListener`,
which reports every in-process host build. `QuotinatorWebApplicationFactory` registers a marker service;
a host that carries `StartupPhaseState` (it is Program's) but not the marker was built by an unguarded
factory, and the guard throws, failing whichever test built it.

Its positive control is also the proof that the listener fires for a factory-built host at all: a bare
factory constructed from a runtime-only `Type`, inside a scope that expects the violation, is recorded.
Negative controls: the guarded factory records nothing, and a host that is not Program's records nothing.
Each also asserts the host *was* seen, so "nothing recorded" cannot mean the listener never fired.

Shown active for the whole run, not only for its own tests: with the marker registration removed from the
guarded factory, the ordinary endpoint test
`VersionEndpointTests.GetVersion_DatabaseStats_IncludesEveryEntityTypeCount` failed with the guard's #313 message, while its sibling that builds no host passed.
Restored afterwards.

### 12. Restate what the endpoint-request test proves
**Status:** ✅ Done

`EndpointRequest_ReachesEndpointRatherThanWaitPage` was described as the failure this guard prevents,
stated as an assertion. With the readiness wait removed from `QuotinatorWebApplicationFactory`, it passed
3 runs of 3, while `CreateClient_ReturnsOnlyAfterStartupIsComplete` failed 3 of 3. The HTTP round trip
outlasts the remaining startup work (exactly the margin the Background section describes), so this test
cannot observe the race.

It stays, as the positive control the flag test lacks: a client from the guarded factory is answered by
the endpoint itself (`422`, not the wait page's `200`). Its summary says that, and says the race is
detected by the flag test, so it no longer claims coverage it does not provide.

### 13. Describe the three layers where the guard is documented
**Status:** ✅ Done

`docs/testing-policy.md`'s *Parallel execution* section described #313's guard as "a source-scanning
guard test". It now names the three layers by what each sees. `IL` added to `docs/vocabulary.md`.

### 14. Re-run full verification
**Status:** ✅ Done

Build clean, and the full solution suite green across three consecutive `-m:1` runs, as step 6 required;
the reason for repeating it is unchanged. 2026-09-25: 0 warnings, 0 errors; each run 11 projects,
4,228 tests, 4,228 passed, none failed or skipped.

### 15. Give every "found nothing" test a positive half
**Status:** ✅ Done

Found 2026-09-25: step 7's red run left 18 new or changed tests green against the stubs, and they were
then described as controls "green by design". `docs/testing-policy.md` makes no such exception: every test
an issue adds or changes is red first, and mutation is the recovery, not a substitute. Red first was
achievable all along, as `GuardedFactory_IsNotRecorded` and `HostThatIsNotPrograms_IsNotRecorded` show:
each asserts nothing was recorded *and* that the host was seen, so each went red against the stub.

The same shape for every static "found nothing" test: `UnguardedFactoryUsageResult` gains
`InspectedCount`, the syntax nodes the source analysis examined or the IL instructions the assembly
analysis read. Every allowed-fixture test and every real-project test also asserts it is above zero, so
an analysis that looked at nothing fails instead of passing.

`Allowed-ConstructionInCommentAndString` changes with it. Its string was an unused `const`, which the
compiler never emits, so layer A had nothing to read there (the step 14 mutation left that one row green).
The string is now returned from a method, so it reaches the IL and both layers judge it.

`WaitUntilComplete_AlreadyComplete_ReturnsWithoutWaiting` had the same gap in another form: its summary
promised "returns immediately rather than paying the poll interval" and it asserted nothing. It now
counts how often the completion check is asked, and requires exactly once.

### 16. Remove the endpoint-request test
**Status:** ✅ Done

`EndpointRequest_ReachesEndpointRatherThanWaitPage` cannot go red against any state of this issue: it
passes against the unguarded factory too (step 12). What it shows, a client reaching a real endpoint, is
already shown by every endpoint test in the project, for example `ConversationEndpointsTests`' page-zero
`422`. A test with no red state proves nothing about #313, so it is deleted rather than kept as a
restated control.

### 17. Run every added or changed test red, then green
**Status:** ✅ Done

Against the signature state: both analyses returning an empty result, the runtime guard installing
nothing, `QuotinatorWebApplicationFactory` unsealed and without its wait or marker (its state before
this issue), and `StartupReadiness.WaitUntilComplete` returning at once with nothing done, the "return a
default" signature `docs/testing-policy.md` describes. Every test in `WebApplicationFactoryUsageGuardTests`,
`UnguardedFactoryRuntimeGuardTests` and `StartupReadinessTests` must fail on an assertion. Then the
implementation is restored, all of them pass, and the full solution suite runs green again.

Red run, 2026-09-25: 74 of 74 failed, every one on an assertion, none passing. Restored byte for byte
from a backup, no marker left; green: build 0 warnings, 0 errors, and those 74 plus `StartupWaitMiddlewareTests`
(78) all pass. Full solution suite with `-m:1`: 11 projects, 4,227 tests (one fewer than step 14, the
deleted test), 4,227 passed, none failed or skipped, 0 warnings, 0 errors.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A client from the shared factory is handed out only once startup is complete | Unit test + mutation | `StartupReadinessTests.CreateClient_ReturnsOnlyAfterStartupIsComplete`; with the wait removed from `QuotinatorWebApplicationFactory` it fails 3 runs of 3 |
| 2 | ✅ | A startup that never completes fails with a clear, bounded timeout rather than hanging the suite | Unit test | `StartupReadinessTests.WaitUntilComplete_NeverCompletes_ThrowsClearTimeoutRatherThanHanging`, with `...AlreadyComplete_ReturnsWithoutWaiting` as its positive control |
| 3 | ✅ | Every `WebApplicationFactory<Program>` construction in `Quotinator.Api.Tests` uses the readiness-aware factory | Unit test | `WebApplicationFactoryUsageGuardTests.RealProject_Source_HasNoUnguardedFactoryUsage` and `...RealProject_Assembly_HasNoUnguardedFactoryUsage`, plus layer C active for the whole run (row 16) |
| 4 | ✅ | `StartupWaitMiddlewareTests` still exercises the incomplete-startup path (not accidentally "fixed" by the guard) | Unit test | `StartupWaitMiddlewareTests`: constructs `StartupPhaseState` directly, no factory; all pass |
| 5 | ✅ | Solution-level runs execute test projects sequentially | Command | `Select-String -SimpleMatch -- '-m:1' CLAUDE.md, docs/testing-policy.md`: both files carry the documented full-suite command |
| 6 | ✅ | Full build clean | Build | `dotnet build --configuration Release`: 0 Warning(s), 0 Error(s) |
| 7 | ✅ | Full test suite green across repeated solution-level runs | Build | `dotnet test --configuration Release -m:1`, three consecutive runs, because the failure this issue fixes was intermittent and a single green run does not demonstrate a fix |
| 8 | ✅ | Layer B flags every violation fixture, with exactly the expected kinds | Unit test | `WebApplicationFactoryUsageGuardTests.Fixture_Violation_IsFlaggedBySourceAnalysis`, one case per fixture in `ViolationFixtures`; all 21 red before step 8 |
| 9 | ✅ | Layer A flags every violation fixture, with exactly the kinds layer B reports | Unit test | `WebApplicationFactoryUsageGuardTests.Fixture_Violation_IsFlaggedByAssemblyAnalysis`, the same 21 cases; all red before step 9 |
| 10 | ✅ | Neither static layer flags an allowed fixture | Unit test | `WebApplicationFactoryUsageGuardTests.Fixture_Allowed_IsNotFlaggedBySourceAnalysis` and `...ByAssemblyAnalysis`, one case per fixture in `AllowedFixtures` |
| 11 | ✅ | Layer B demonstrably resolved the real sources, rather than finding nothing because nothing bound | Unit test | `WebApplicationFactoryUsageGuardTests.RealProject_Source_CompilesWithoutErrors` (581 errors before the assembly-name fix) and `...RealProject_Source_FindsTheGuardedFactoryConstructions` |
| 12 | ✅ | Layer A demonstrably read the real assembly's IL | Unit test | `WebApplicationFactoryUsageGuardTests.RealProject_Assembly_ReadsEveryMethodBody` and `...RealProject_Assembly_FindsTheGuardedFactoryConstructions`; `Fixture_GuardedConstruction_IsCountedByBothLayers` holds both layers to exact counts |
| 13 | ✅ | The guarded factory cannot be subclassed | Unit test | `WebApplicationFactoryUsageGuardTests.GuardedFactory_IsSealed` |
| 14 | ✅ | Layer C records a Program host built by a bare factory constructed from a runtime-only `Type` | Unit test | `UnguardedFactoryRuntimeGuardTests.BareFactoryFromRuntimeType_IsRecorded` |
| 15 | ✅ | Layer C records nothing for the guarded factory, nor for a host that is not Program's, having seen each | Unit test | `UnguardedFactoryRuntimeGuardTests.GuardedFactory_IsNotRecorded` and `...HostThatIsNotPrograms_IsNotRecorded` |
| 16 | ✅ | Layer C is installed for the whole run, and outside an expecting scope it fails the build of the offending host | Unit test + mutation | `UnguardedFactoryRuntimeGuardTests.GuardIsInstalledBeforeAnyTest` and `...BareFactoryOutsideAnExpectation_FailsItsHostBuild`; with the marker registration removed, the ordinary `VersionEndpointTests.GetVersion_DatabaseStats_IncludesEveryEntityTypeCount` fails with the #313 message |
| 17 | ✅ | No test claims coverage of #313 that it cannot fail to provide | Command | `Select-String -SimpleMatch 'EndpointRequest_ReachesEndpointRatherThanWaitPage' -Path tests -Recurse -Include *.cs` returns nothing |
| 18 | ✅ | The testing policy describes the guard as its three layers | Command | `Select-String -SimpleMatch 'source-scanning guard' docs/testing-policy.md` returns nothing, and the three layer names each match once |
| 19 | ✅ | Every static "found nothing" test fails when the analysis looked at nothing | Unit test | `Fixture_Allowed_IsNotFlaggedBySourceAnalysis`/`...ByAssemblyAnalysis`, `RealProject_Source_*` and `RealProject_Assembly_*` each assert `InspectedCount` above zero |
| 20 | ✅ | Every test this issue adds or changes fails against the signature state, on an assertion | Unit test | Step 17's red run: all 74 of `WebApplicationFactoryUsageGuardTests`, `UnguardedFactoryRuntimeGuardTests` and `StartupReadinessTests` fail on assertions |

---

## Relationship to existing issues

- **#280**: introduced the listen-before-initialised model. Its production behaviour is correct and
  unchanged here; only the test harness's assumption about it was wrong.
- **#312**: where this was found. #312's own verification runs are not fully trustworthy until this
  lands, which is why this issue is sequenced ahead of #312's remaining steps.
- **ADR 006**: related but not violated; step 4 extends its spirit to solution-level runs, which the
  ADR itself does not cover.
