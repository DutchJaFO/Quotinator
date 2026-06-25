# Issue #111 — Investigate flaky test in Quotinator.Core.Tests

**Milestone:** v1.7.0  
**Status:** Open — blocked by #115  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Observed

During the v1.6.4 release pre-push checklist, one test in `Quotinator.Core.Tests` failed on the first run and passed on the second with no code changes:

```
Failed!  - Failed: 1, Passed: 197, Skipped: 0, Total: 198
```

The failing test name was not captured before the re-run.

## Dependency on #115

Issue #115 moves `DatabaseInitializerTests`, `ImportBatchesTests`, and `DapperSetupTests` from `Quotinator.Core.Tests` to `Quotinator.Data.Tests`. This migration may surface additional parallel execution patterns that are currently masked by test placement. Work on #111 must begin after #115 is complete.

## Investigation approach

1. Run `dotnet test tests/Quotinator.Core.Tests --configuration Release` repeatedly (10+ times) to reproduce
2. Once the test name is captured, identify the root cause:
   - Shared static state (Dapper type handlers, global singletons)
   - Ordering sensitivity (test A must run before test B)
   - Timing/race condition (concurrent access to shared resource)
   - Randomness (non-deterministic data)
   - Temp file/resource collision (two tests writing to the same path)
3. Apply the appropriate fix:
   - Shared state → use `[AssemblyInitialize]` pattern and isolate global registration
   - Ordering sensitivity → make tests self-contained, or add `[TestCategory]` ordering
   - Temp file collision → use `Directory.CreateTempSubdirectory()` unique paths
   - Quarantine as last resort: `[Ignore("Flaky: ...")]` with a comment explaining the root cause and a linked tracking issue

## Notes

- The most likely suspect is Dapper type-handler registration via `DapperConfiguration.Configure()` — called in `[ClassInitialize]` or `[AssemblyInitialize]` across multiple test classes. Concurrent calls to `SqlMapper.AddTypeHandler` are not thread-safe, and MSTest runs test classes in parallel by default.
- The `testing-policy.md` rule on `[AssemblyInitialize]` global state must be followed: one project-wide call, not one per test class.
- After #115 moves the Dapper-dependent tests to `Quotinator.Data.Tests`, the flaky test may move with them or disappear from `Core.Tests` entirely.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Flaky test identified by name | Live | Run `dotnet test tests/Quotinator.Core.Tests --configuration Release` ×10; capture failing test name |
| 2 | ⬜ | Root cause documented | Plan doc | Root cause section added below once identified |
| 3 | ⬜ | Fix applied (or quarantine comment added) | Code review | Test is either fixed or `[Ignore]` with explanation |
| 4 | ⬜ | 10 consecutive runs pass | Live | `for i in $(seq 10); do dotnet test tests/Quotinator.Core.Tests --configuration Release; done` — all pass |
| 5 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |

---

## Root cause (to be filled in during investigation)

_TBD — complete after #115 is done and investigation runs._
