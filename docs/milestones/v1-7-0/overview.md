# v1.7.0 Maintenance Milestone — Overview

Maintenance milestone for bugs and minor improvements. Issues target v1.7.x patch releases.

---

## Issue List

| # | Title | Status | Plan doc |
|---|-------|--------|----------|
| [#115](https://github.com/DutchJaFO/Quotinator/issues/115) | Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data | 🔴 Open | [115-dapper-core-refactor-plan.md](115-dapper-core-refactor-plan.md) |
| [#111](https://github.com/DutchJaFO/Quotinator/issues/111) | Investigate flaky test in Quotinator.Core.Tests | 🟡 In progress (blocked by #115) | [111-flaky-test-plan.md](111-flaky-test-plan.md) |
| [#70](https://github.com/DutchJaFO/Quotinator/issues/70) | Refactor CI/Release workflows to use a shared reusable workflow | 🔴 Open | [70-ci-workflow-plan.md](70-ci-workflow-plan.md) |
| [#109](https://github.com/DutchJaFO/Quotinator/issues/109) | Search: field=author and field=character always return empty; type=person returns empty | ✅ Complete — closed | [109-search-endpoint-plan.md](109-search-endpoint-plan.md) |
| [#74](https://github.com/DutchJaFO/Quotinator/issues/74) | Add read-model query pattern to Quotinator.Data for join and projection queries | 🔴 Open | [74-read-model-query-plan.md](74-read-model-query-plan.md) |
| [#75](https://github.com/DutchJaFO/Quotinator/issues/75) | Add master/detail repository pattern to Quotinator.Data for parent/child table relationships | 🔴 Open | [75-master-detail-plan.md](75-master-detail-plan.md) |
| [#76](https://github.com/DutchJaFO/Quotinator/issues/76) | Add 1:1 relationship pattern to Quotinator.Data | 🔴 Open | [76-one-to-one-plan.md](76-one-to-one-plan.md) |
| [#77](https://github.com/DutchJaFO/Quotinator/issues/77) | Add many-to-many relationship pattern to Quotinator.Data | 🔴 Open | [77-many-to-many-plan.md](77-many-to-many-plan.md) |
| [#117](https://github.com/DutchJaFO/Quotinator/issues/117) | Add .NET SDK to Claude Code remote execution environment via session-start hook | ✅ Complete — closed | [117-dotnet-env-plan.md](117-dotnet-env-plan.md) |
| [#73](https://github.com/DutchJaFO/Quotinator/issues/73) | Audit trail: record who did what on which record in which table | 🔴 Open (deferred) | [73-audit-trail-plan.md](73-audit-trail-plan.md) |

---

## Dependency Map

```
#115  ─── (none) — foundational refactor; must complete before #111 can close
#111  ─── blocked by #115 (flaky test investigation is incomplete until all test files
           are in the correct projects and all parallel execution patterns are verified)
#70   ─── (none)
#109  ─── closed
#74   ─── (none)
#75   ─── depends on #74 (read-model pattern)
#76   ─── depends on #74 (read-model pattern); transaction concern shared with #75
#77   ─── depends on #74, #75, #76
#117  ─── closed
#73   ─── blocked by auth milestone (deferred — stays open as a placeholder)
```

---

## Order of Operations

| Order | Issue | Reason |
|-------|-------|--------|
| 1 | **#115** — Dapper/Core refactor | Must come first: moves `DatabaseInitializerTests` and `ImportBatchesTests` to `Quotinator.Data.Tests`, where all remaining parallel-execution patterns must be verified |
| 2 | **#111** — Flaky test (re-verify after #115) | One race pattern fixed; re-verify after #115 to confirm no additional patterns exist |
| 3 | **#70** — CI refactor | Independent; clean up workflow duplication early |
| 4 | **#74** — Read-model pattern | Foundational for #75, #76, and #77 |
| 5 | **#75** — Master/detail pattern | After #74; may add optional transaction parameter to `IRepository<T>` |
| 6 | **#76** — 1:1 pattern | After #74; transaction concern shared with #75 — do close together |
| 7 | **#77** — Many-to-many pattern | After #74, #75, #76 (all three referenced in the spec) |
| 8 | **#73** — Audit trail | Deferred; blocked by auth milestone — no work expected this milestone |

---

## PR Merge Plan

| Issue | Safe to merge alone? | Notes |
|-------|---------------------|-------|
| #115 | Yes | Refactor with no behaviour change; build + tests are the gate. |
| #111 | Yes (after #115) | Re-verify after #115; self-contained once confirmed. |
| #70 | Yes | CI-only change — no production code affected. |
| #74 | Yes | New infrastructure; nothing currently calls it. |
| #75 | Yes | New infrastructure; nothing currently calls it. |
| #76 | Yes | New infrastructure; nothing currently calls it. |
| #77 | Yes | New infrastructure; nothing currently calls it. |
| #73 | Deferred | Will not merge in this milestone. |

---

## Notes

**#73 — Audit trail deferred:** Explicitly deferred in the issue spec to the auth milestone. `RecordBase` already preserves state (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`) on every row, so nothing is lost by deferring — the `who` column simply requires an authenticated user identity that does not exist yet. A comment documenting the deferral is on the issue.
