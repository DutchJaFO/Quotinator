# v1.7.0 Maintenance Milestone — Overview

Maintenance milestone for bugs and minor improvements. Issues target v1.7.x patch releases.

---

## Issue List

| # | Title | Status | Plan doc |
|---|-------|--------|----------|
| [#115](https://github.com/DutchJaFO/Quotinator/issues/115) | Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data | ✅ Complete — closed | [115-dapper-core-refactor-plan.md](115-dapper-core-refactor-plan.md) |
| [#121](https://github.com/DutchJaFO/Quotinator/issues/121) | Refactor: remove Dapper dependency from SqliteQuoteService | 🔴 Open | (no plan doc yet) |
| [#111](https://github.com/DutchJaFO/Quotinator/issues/111) | Investigate flaky test in Quotinator.Core.Tests | ✅ Complete — closed | [111-flaky-test-plan.md](111-flaky-test-plan.md) |
| [#70](https://github.com/DutchJaFO/Quotinator/issues/70) | Refactor CI/Release workflows to use a shared reusable workflow | 🔴 Open | [70-ci-workflow-plan.md](70-ci-workflow-plan.md) |
| [#125](https://github.com/DutchJaFO/Quotinator/issues/125) | Fix request log format: double-quote between path and query string | 🔴 Open | [125-request-log-format-plan.md](125-request-log-format-plan.md) |
| [#109](https://github.com/DutchJaFO/Quotinator/issues/109) | Search: field=author and field=character always return empty; type=person returns empty | ✅ Complete — closed | [109-search-endpoint-plan.md](109-search-endpoint-plan.md) |
| [#74](https://github.com/DutchJaFO/Quotinator/issues/74) | Add read-model query pattern to Quotinator.Data for join and projection queries | 🔴 Open | [74-read-model-query-plan.md](74-read-model-query-plan.md) |
| [#75](https://github.com/DutchJaFO/Quotinator/issues/75) | Add master/detail repository pattern to Quotinator.Data for parent/child table relationships | 🔴 Open | [75-master-detail-plan.md](75-master-detail-plan.md) |
| [#76](https://github.com/DutchJaFO/Quotinator/issues/76) | Add 1:1 relationship pattern to Quotinator.Data | 🔴 Open | [76-one-to-one-plan.md](76-one-to-one-plan.md) |
| [#77](https://github.com/DutchJaFO/Quotinator/issues/77) | Add many-to-many relationship pattern to Quotinator.Data | 🔴 Open | [77-many-to-many-plan.md](77-many-to-many-plan.md) |
| [#117](https://github.com/DutchJaFO/Quotinator/issues/117) | Add .NET SDK to Claude Code remote execution environment via session-start hook | ✅ Complete — closed | [117-dotnet-env-plan.md](117-dotnet-env-plan.md) |
| [#73](https://github.com/DutchJaFO/Quotinator/issues/73) | Audit trail: record who did what on which record in which table | 🔴 Open | [73-audit-trail-plan.md](73-audit-trail-plan.md) |

---

## Dependency Map

```
#115  ─── (none) — ✅ complete
#111  ─── (none) — ✅ complete
#109  ─── (none) — ✅ complete
#117  ─── (none) — ✅ complete
#70   ─── (none) — open; rows 10–11 pending next beta→final release cycle
#125  ─── (none) — open; independent fix in Program.cs
#73   ─── (none) — auth dependency removed 2026-06-27; uses User-Agent header via ICallerContext
#74   ─── depends on #73 (repository base class takes IAuditWriter + ICallerContext from the start)
#75   ─── depends on #74 (read-model pattern)
#76   ─── depends on #74 (read-model pattern); transaction concern shared with #75
#77   ─── depends on #74, #75, #76
#121  ─── depends on #115 (✅ done), #73 (audit in place), and patterns decision (#74/#75/#76/#77)
```

---

## Order of Operations

| Order | Issue | Reason |
|-------|-------|--------|
| 1 | ✅ **#115** — Dapper/Core refactor | Complete |
| 2 | ✅ **#111** — Flaky test | Complete |
| 3 | ✅ **#109** — Search field/type bug | Complete |
| 4 | ✅ **#117** — .NET SDK hook | Complete |
| 5 | ✅ **#70** — CI refactor (partial) | Code complete and in v1.7.0; rows 10–11 (beta-enforcement workflow test) pending next release cycle |
| 6 | **#125** — Request log format fix | Independent; quick fix; the patch release that ships this is also the beta→final cycle that closes #70 rows 10–11 |
| 7 | **#73** — Audit trail | Before #74: `ICallerContext`, `IAuditWriter`, `AuditWriter`, and the `AuditEntries` migration must exist before repository base classes are written, so audit is built in from day one |
| 8 | **#74** — Read-model pattern | After #73; repository base class receives `IAuditWriter` + `ICallerContext` in constructor |
| 9 | **#75** — Master/detail pattern | After #74; may add optional transaction parameter to `IRepository<T>` |
| 10 | **#76** — 1:1 pattern | After #74; transaction concern shared with #75 — do close together |
| 11 | **#77** — Many-to-many pattern | After #74, #75, #76 (all three referenced in the spec) |
| 12 | **#121** — Remove Dapper from SqliteQuoteService | After #73 (audit) + #74–#77 (patterns); all infrastructure in place |

---

## PR Merge Plan

| Issue | Safe to merge alone? | Notes |
|-------|---------------------|-------|
| #115 | ✅ Merged | Refactor with no behaviour change; included in v1.7.0. |
| #111 | ✅ Merged | Self-contained; included in v1.7.0. |
| #109 | ✅ Merged | Included in v1.7.0. |
| #117 | ✅ Merged | Included in v1.7.0. |
| #70 | ✅ Merged (partial) | CI-only; included in v1.7.0. Rows 10–11 pending; issue stays open. |
| #125 | Yes | Small fix in `Program.cs`; no production logic affected. |
| #74 | Yes | New infrastructure; nothing currently calls it. |
| #75 | Yes | New infrastructure; nothing currently calls it. |
| #76 | Yes | New infrastructure; nothing currently calls it. |
| #77 | Yes | New infrastructure; nothing currently calls it. |
| #73 | Yes | Independent; must merge before #74 so audit is in the repository base class from the start. |
| #74 | Yes (after #73) | New infrastructure; nothing currently calls it. |
| #75 | Yes (after #74) | New infrastructure. |
| #76 | Yes (after #74) | New infrastructure. |
| #77 | Yes (after #74–#76) | New infrastructure. |
| #121 | Yes (after #73 + #74–#77) | Depends on audit integration and full repository pattern. |

---

## Notes

**#73 — Audit trail deferred:** Explicitly deferred in the issue spec to the auth milestone. `RecordBase` already preserves state (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`) on every row, so nothing is lost by deferring — the `who` column simply requires an authenticated user identity that does not exist yet. A comment documenting the deferral is on the issue.
