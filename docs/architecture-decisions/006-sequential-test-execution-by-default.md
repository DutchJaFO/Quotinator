# ADR 006 — Sequential test execution by default

**Status:** Accepted  
**Date:** 2026-06-25  
**GitHub issue:** #111

---

## Context

During the v1.7.0 milestone, a flaky test failure was observed in `Quotinator.Data.Tests` on one run
out of 25 solution-level test runs. The failure was intermittent and could not be reproduced
in isolation against the single project — it only appeared when all test projects ran simultaneously.

The root cause of the original `#111` flaky test (within a single project) was concurrent
`[ClassInitialize]` methods both calling `DapperConfiguration.Configure()`, which writes to
Dapper's global static type-handler dictionary. That was fixed by moving the call to
`[AssemblyInitialize]`. The subsequent intermittent failure pointed to a broader class of risk:
any test that touches process-wide state (connection pools, static registries, shared file paths)
is potentially unsafe when test methods run concurrently.

Prior to this decision, all three active test projects had
`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`, making concurrent execution the
default for every test method in those projects. This meant correctness depended on every test
being independently safe — a property that was never verified, only assumed.

---

## Decision

**Sequential execution is the default for all test projects. Parallel execution is an explicit
opt-in, applied at the class level, only when the class can be positively demonstrated to be
parallel-safe.**

Concretely:

- No test project has `[assembly: Parallelize]`.
- A test class may add `[Parallelize]` at the class level only when all four of the following
  are true:
  1. No global state is written or read (Dapper type handlers, static caches, singletons).
  2. No shared filesystem resources — each test creates its own isolated temp directory and
     SQLite file in `[TestInitialize]` and deletes it in `[TestCleanup]`.
  3. No `SqliteConnection.ClearAllPools()` in cleanup — that is a process-wide operation that
     affects all pooled connections in the process.
  4. All assertions are on local, test-owned state only.

The four conditions exist because each one has been associated with an observed or plausible
failure mode in this codebase.

---

## Consequences

**Safety gained:** A test class that has not been reviewed against the four conditions cannot
accidentally run in parallel. The failure mode (intermittent, timing-dependent, hard to reproduce)
is prevented by construction rather than discovered in CI.

**Speed lost:** Sequential execution within a project is slower. In practice the test suite is
fast enough that this is not a concern: `Quotinator.Data.Tests` (the slowest project due to SQLite
integration tests) runs in under 10 seconds sequentially. If a future project accumulates enough
tests that sequential execution becomes a bottleneck, that is the right time to revisit this
decision and identify which classes can be safely parallelised.

**Opt-in is intentionally high-friction.** A developer adding `[Parallelize]` to a class must
consciously verify all four conditions. The friction is the point — it forces the question
"is this actually safe?" rather than inheriting a risky default.

---

## Revision — 2026-07-12

The original decision relied on the *absence* of `[assembly: Parallelize]` as the sequential-execution
guarantee, reasoning that MSTest's default (with neither attribute present) is already sequential. In
practice this left the guarantee implicit and per-project-inconsistent: only `Quotinator.Data.Tests`
and `Quotinator.Engine.Tests` actually carried an explicit `[assembly: DoNotParallelize]`;
`Quotinator.Core.Tests` had an assembly-setup file but was missing the attribute, and six other test
projects (`Api.Tests`, `Changelog.Tests`, `Constants.Tests`, `Converters.BasicJsonArray.Tests`,
`Converters.Csv.Tests`, `Converters.RegexArray.Tests`, `Data.Testing.Tests`, `Tools.DbInspector.Tests`)
had no assembly-level marker at all — correctness depended on an unstated default rather than a visible
declaration.

**Strengthened rule: every test project must carry an explicit `[assembly: DoNotParallelize]`,
regardless of whether it currently has any state-sensitive tests.** This makes "is parallel execution
enabled here" answerable by grepping one attribute per project, rather than by reasoning about SDK
defaults — and any future `[Parallelize]` opt-in on a specific class becomes a visible, deliberate
departure from an explicit baseline instead of an implicit one. All eleven active test projects now
carry the attribute (a twelfth directory, `Quotinator.Converters.NikhilNamal17.Tests`, is stale —
removed from the solution by #144 but its `obj/` folder was never deleted; not a live project).
