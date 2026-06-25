# Plan: #111 — Investigate flaky test in Quotinator.Core.Tests

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/111  
**Milestone:** v1.7.0  
**Status:** 🟡 Blocked by [#115](https://github.com/DutchJaFO/Quotinator/issues/115)

---

## Summary

One test in `Quotinator.Core.Tests` failed on the first run during the v1.6.4 pre-push checklist and passed on the second run with no code changes. The failing test name was not captured before the re-run.

---

## Root cause (identified)

Two race conditions existed in `Quotinator.Core.Tests` under method-level parallel execution:

**Race 1 — concurrent `DapperConfiguration.Configure()` calls (confirmed cause)**

Both `DatabaseInitializerTests` and `ImportBatchesTests` called `DapperConfiguration.Configure()` from `[ClassInitialize]`. Under `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`, both class initializers can run simultaneously. `DapperConfiguration.Configure()` calls `SqlMapper.AddTypeHandler(...)` which writes to Dapper's global static type-handler dictionary — not thread-safe for concurrent writes. Concurrent registration can corrupt the handler map or cause one handler to be lost, resulting in an intermittent type-mapping failure on any subsequent Dapper query.

**Race 2 — `SqliteConnection.ClearAllPools()` in `[TestCleanup]`**

Both cleanup methods called `ClearAllPools()`, which closes idle pooled connections across the entire process. Under parallel execution, one test's cleanup could clear a connection that was returned to the pool by another test mid-cleanup before that test's `Directory.Delete` had run. This was not reproducible in isolation but was identified during the investigation as a latent risk.

Note: `ClearAllPools()` only affects idle connections (already returned to the pool) — it cannot interrupt an active database operation. The risk is lower than initially assessed. `ClearAllPools()` was retained; `ClearPool(conn)` with an unopened connection was tested and does not release the pool in Microsoft.Data.Sqlite.

---

## Fix

- Added `[AssemblyInitialize]` to `MSTestSettings.cs` calling `DapperConfiguration.Configure()` once for the entire test run — matching the pattern already used in `Quotinator.Data.Tests/MSTestSettings.cs`.
- Removed `[ClassInitialize]` from `DatabaseInitializerTests` and `ImportBatchesTests`.
- Removed the now-unused `using Quotinator.Core.Data.TypeHandlers;` from both classes.

---

## Process note

The red step was not completed before applying the fix — the fix was identified from code inspection and applied directly. This is a process violation. As a consequence, regression tests were added (`DapperSetupTests`) that are deterministically red if `AssemblySetup.Initialize` is removed, providing the missing evidence that the handler registration is required. Future bug fixes must reproduce the red state before writing the fix.

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Race condition identified | Investigation | Two concurrent `[ClassInitialize]` calls to `SqlMapper.AddTypeHandler` — global static, not thread-safe |
| 2 | ✅ | `DapperConfiguration.Configure()` moved to `[AssemblyInitialize]` | Live | `MSTestSettings.cs` has `[AssemblyInitialize]`; both `[ClassInitialize]` methods removed |
| 3 | ✅ | Regression tests added that are red without `[AssemblyInitialize]` | Unit test | `DapperSetupTests.GuidHandler_RegisteredByAssemblySetup_DapperMapsGuidCorrectly`, `DapperSetupTests.SafeDateHandler_RegisteredByAssemblySetup_DapperMapsDateCorrectly` — both throw `InvalidCastException` if handlers are not registered |
| 4 | ✅ | `Quotinator.Core.Tests` passes 200/200 | Live | `dotnet test tests/Quotinator.Core.Tests --configuration Release` — 200 passed, 0 failed |
| 5 | ✅ | Full suite stable under repeated runs | Live | 5 consecutive full-suite runs — 398/398 passed on every run |
| 6 | ✅ | Build clean | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 7 | 🟡 | All Dapper-related test files moved to `Quotinator.Data.Tests`; no remaining parallel patterns | Live | `DatabaseInitializerTests` → `Data.Tests/Database/` ✅, `ImportBatchesTests` → `Data.Tests/Repositories/` ✅, `DapperSetupTests` → `Data.Tests/Helpers/` ✅, `SqliteQuoteServiceSearchTests` policy violations fixed ✅. 5-run stability check required after these moves — not yet done. |
