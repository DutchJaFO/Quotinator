# #313 — Api tests can silently assert against the startup wait page instead of the endpoint under test

**Status:** Waiting for release
**GitHub issue:** #313
**Tiers required:** T1, T2
**Depends on:** #280

---

## Background

Found while verifying #312 (2026-08-15). A `GetAllConversations_PageZero_Returns422` failure appeared in
a solution-level run and passed on re-run; it was initially written off as a flake, which was wrong.

Since #280, Kestrel listens *before* startup initialisation finishes. Until
`StartupPhaseState.MarkComplete()` runs, `StartupWaitMiddleware` answers every non-exempt request with
`200 OK` and an HTML wait page — its exempt list is only `ApiRoutes.Health` and `ApiRoutes.Version`.
`WebApplicationFactory.CreateClient()` returns once the host is built, not once `Program.cs`'s
post-`StartAsync` work has finished, and **no test in the project waits for readiness**: all ~690 assert
immediately.

The intermittent red is the visible symptom. The real defect is the silent green — a test expecting
`200` passes against the wait page while asserting nothing about the endpoint it names.

## Authoritative-source cross-check

- **ADR 006** (sequential test execution by default) — verified compliant *before* blaming parallelism:
  all eleven test projects carry `[assembly: DoNotParallelize]`, none carries `[assembly: Parallelize]`,
  no class opts in. Within-project execution is already sequential, so this is not an ADR 006 violation.
  ADR 006 governs *within-project* concurrency only and says nothing about solution-level runs — but its
  own Context records that the flake motivating it "only appeared when all test projects ran
  simultaneously", which is exactly the condition here. Step 4 closes that gap explicitly rather than
  leaving it to an unstated default.
- **#280's plan doc** — the listen-before-initialised model is deliberate and stays; the wait page is the
  intended behaviour for a real user hitting a starting app. Nothing here changes production behaviour.
- **`StartupWaitMiddlewareTests`** — constructs `StartupPhaseState` directly rather than through a
  factory, so it is unaffected by this change and keeps testing the middleware in isolation.

No conflict found.

## Design

### Wait once per factory, not once per client

`StartupPhaseState` is a singleton: once complete it stays complete. So the wait belongs to the
*factory*, not to each of the 376 `CreateClient()` call sites — making it per-call would be 376 edits
and would re-poll pointlessly on every one.

A shared `QuotinatorWebApplicationFactory : WebApplicationFactory<Program>` overrides `CreateHost`,
starts the host as usual, then blocks (bounded) until the app reports startup complete. Every
`CreateClient()` on that factory is then safe by construction, and all 376 call sites stay untouched —
only the 23 `new WebApplicationFactory<Program>()` construction sites change.

### Poll the state, not the HTTP endpoint

The issue text proposed polling `GET /api/v1/health` until it stops reporting `"starting"`. Reading
`StartupPhaseState.IsComplete` from the host's own DI container is strictly better: it is the exact flag
the middleware itself branches on, rather than a proxy for it, and it needs no HTTP round trip inside a
factory-construction path. `InternalsVisibleTo("Quotinator.Api.Tests")` is already configured, so the
internal type is reachable.

The wait is **bounded** — a genuinely stuck startup must fail the test with a clear message rather than
hang the suite.

### Sequential solution-level runs are a second, separate measure

`-m:1` removes the cross-project CPU contention that widens the race window. It is deliberately *not*
treated as the fix: it would mask the unguarded race rather than close it. Both land — the guard makes
tests correct, the flag makes runs reproducible.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

### 2. `QuotinatorWebApplicationFactory` with a bounded readiness wait
**Status:** ✅ Done

### 3. Point the 23 factory-construction sites at it
**Status:** ✅ Done

**Planned as its own commit, landed with steps 2/4/5 instead — the split was not achievable.** The
intent was to keep the mechanical change separate so the helper stayed reviewable on its own. But the
guard test from step 5 fails while any file still constructs the bare factory, and the mechanical change
cannot precede step 2 because it references a type that would not yet exist. Either ordering produces an
intermediate commit that does not pass — the same trap #312's steps 2/4 hit. One commit, deliberately.

### 4. `-m:1` for solution-level runs, documented
**Status:** ✅ Done

`CLAUDE.md`'s Commands section and Pre-Push Checklist, plus `docs/testing-policy.md`.

### 5. Regression test
**Status:** ✅ Done

Proves the guard actually holds: with startup incomplete, the factory must not hand back a client whose
endpoint request returns the wait page.

### 6. Full verification
**Status:** ✅ Done

Three consecutive full-solution runs with `-m:1`, all green (3,392 tests each). Repeated deliberately:
the failure this issue fixes was intermittent, so one green run demonstrates nothing.

**No T1/T2/T3 tier applies.** This issue changes test-harness code and documentation only — not one line
of `src/`. There is no runtime behaviour to confirm in Visual Studio, Docker, or a live add-on.

**The guard was proved load-bearing before being trusted, not assumed to be.** A throwaway canary test
(added, run, then removed) constructed the *unguarded* `WebApplicationFactory<Program>`, took a client,
and asserted `StartupPhaseState.IsComplete`. It failed **5 times out of 5** — on an idle machine, running
a single test, with no competing projects.

That result is worse than the issue's own framing. This was never a rare race: **every** Api test has
been starting before the app was ready, all along. They passed because the HTTP round-trip usually
outlasts the remaining startup work — luck with a comfortable margin, not correctness. Cross-project
contention did not create the defect; it merely consumed the margin until the defect became visible once.
Which is also why `-m:1` alone would have been the wrong response: it would have restored the margin and
re-hidden the problem.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A client from the shared factory never observes the startup wait page for an endpoint request | Unit test | Regression test added by step 5 |
| 2 | ✅ | A startup that never completes fails with a clear, bounded timeout rather than hanging the suite | Unit test | Regression test asserting the timeout message |
| 3 | ✅ | Every `WebApplicationFactory<Program>` construction in `Quotinator.Api.Tests` uses the readiness-aware factory | Unit test | A guard test greps the test sources, mirroring `SqlSourceScanTests`' own source-scanning precedent |
| 4 | ✅ | `StartupWaitMiddlewareTests` still exercises the incomplete-startup path (not accidentally "fixed" by the guard) | Unit test | Its existing tests still pass unchanged |
| 5 | ✅ | Solution-level runs execute test projects sequentially | Manual | `dotnet test --configuration Release -m:1` documented in `CLAUDE.md` and `docs/testing-policy.md` |
| 6 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 7 | ✅ | Full test suite green across repeated solution-level runs | Build | `dotnet test --configuration Release -m:1`, run repeatedly — the failure this issue fixes was intermittent, so a single green run does not demonstrate a fix |

---

## Relationship to existing issues

- **#280** — introduced the listen-before-initialised model. Its production behaviour is correct and
  unchanged here; only the test harness's assumption about it was wrong.
- **#312** — where this was found. #312's own verification runs are not fully trustworthy until this
  lands, which is why this issue is sequenced ahead of #312's remaining steps.
- **ADR 006** — related but not violated; step 4 extends its spirit to solution-level runs, which the
  ADR itself does not cover.
