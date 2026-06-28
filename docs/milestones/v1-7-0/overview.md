# v1.7.0 Maintenance Milestone — Overview

Maintenance milestone for bugs and minor improvements. Issues target v1.7.x patch releases.

---

## Verification tier definitions

| Tier | Environment | What it catches |
|------|-------------|-----------------|
| **T1 — VS/local** | Visual Studio on Windows | Razor runtime errors (not caught by `dotnet build`), Blazor circuit startup, UI rendering, manual API interaction |
| **T2 — Docker** | `docker build` + `docker run` locally | Publish output completeness, container startup, Kestrel port binding, `data/sources/` presence in image |
| **T3 — HA add-on** | Live Home Assistant supervisor | Ingress routing, `X-Ingress-Path` middleware, supervisor volume mount at `/data`, DataProtection keys, SSL cert loading, cookie behaviour after container restart, supervisor log output |

Full tier definitions and classification rules: [`docs/release-verification.md`](../release-verification.md)

**An issue can only be closed after:**
1. It is included in a published release (beta or final as appropriate)
2. Every required tier for that issue is confirmed green
3. Explicit user confirmation is given to `gh issue close`

---

## Issue List

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#115](https://github.com/DutchJaFO/Quotinator/issues/115) | Refactor: move all Dapper dependencies out of Core into Data | ✅ Closed — v1.7.0 | T1 ✅ | [115-dapper-core-refactor-plan.md](115-dapper-core-refactor-plan.md) |
| [#111](https://github.com/DutchJaFO/Quotinator/issues/111) | Investigate flaky test | ✅ Closed — v1.7.0 | T1 ✅ | [111-flaky-test-plan.md](111-flaky-test-plan.md) |
| [#109](https://github.com/DutchJaFO/Quotinator/issues/109) | Search: field=author/character always return empty; type=person returns empty | ✅ Closed — v1.7.0 | T1 ✅ | [109-search-endpoint-plan.md](109-search-endpoint-plan.md) |
| [#117](https://github.com/DutchJaFO/Quotinator/issues/117) | Add .NET SDK to Claude Code remote execution environment via session-start hook | ✅ Closed — v1.7.0 | T1 ✅ | [117-dotnet-env-plan.md](117-dotnet-env-plan.md) |
| [#70](https://github.com/DutchJaFO/Quotinator/issues/70) | Refactor CI/Release workflows: reusable workflow + beta/final release process | 🟡 Code complete — pending T3 | T3 ⬜ | [70-ci-workflow-plan.md](70-ci-workflow-plan.md) |
| [#125](https://github.com/DutchJaFO/Quotinator/issues/125) | Fix request log format: double-quote between path and query string | 🟡 Code complete — pending release | T1 ✅ T3 ⬜ | [125-request-log-format-plan.md](125-request-log-format-plan.md) |
| [#126](https://github.com/DutchJaFO/Quotinator/issues/126) | Validation errors return 200 instead of 4xx | 🟡 Code complete — pending release | T1 ✅ | [126-validation-status-codes-plan.md](126-validation-status-codes-plan.md) |
| [#74](https://github.com/DutchJaFO/Quotinator/issues/74) | Add read-model query pattern to Quotinator.Data for join and projection queries | 🟢 All tiers green — pending close | T1 ✅ | [74-read-model-query-plan.md](74-read-model-query-plan.md) |
| [#75](https://github.com/DutchJaFO/Quotinator/issues/75) | Add master/detail repository pattern to Quotinator.Data | 🔴 Not started | T1, T2 | [75-master-detail-plan.md](75-master-detail-plan.md) |
| [#76](https://github.com/DutchJaFO/Quotinator/issues/76) | Add 1:1 relationship pattern to Quotinator.Data | 🔴 Not started | T1, T2 | [76-one-to-one-plan.md](76-one-to-one-plan.md) |
| [#77](https://github.com/DutchJaFO/Quotinator/issues/77) | Add many-to-many relationship pattern to Quotinator.Data | 🔴 Not started | T1, T2 | [77-many-to-many-plan.md](77-many-to-many-plan.md) |
| [#73](https://github.com/DutchJaFO/Quotinator/issues/73) | Audit trail: record who did what on which record in which table | 🟡 Code complete — pending release | T1 ✅ T2 ✅ | [73-audit-trail-plan.md](73-audit-trail-plan.md) |
| [#121](https://github.com/DutchJaFO/Quotinator/issues/121) | Refactor: remove Dapper dependency from SqliteQuoteService | 🔴 Not started | T1, T2 | (no plan doc yet) |

---

## Pending verification before close

These issues are code-complete but cannot be closed until a release ships and the listed tiers are confirmed.

### #70 — CI/Release workflow refactor
**Shipped in:** v1.7.0  
**Rows 1–9:** ✅ verified (code review + Actions run + T1 user confirmation)  
**Rows 10–11 remaining (T3):**

| Row | What to verify | How |
|-----|---------------|-----|
| 10 | Final tag **without** a prior beta tag → `enforce-beta-first` job fails; Docker build does not run | Push a test final tag `vX.Y.Z` with no matching `vX.Y.Z-*` tag; confirm the `enforce-beta-first` step fails in the [Actions tab](https://github.com/DutchJaFO/Quotinator/actions) |
| 11 | Normal beta → final cycle → `enforce-beta-first` passes; Docker image pushed with `latest`; full GitHub Release created | Push beta tag first, then final tag; confirm both releases appear correctly on [GitHub Releases](https://github.com/DutchJaFO/Quotinator/releases) |

Full audit commands: [70-ci-workflow-plan.md § How to audit and re-verify](70-ci-workflow-plan.md)

---

### #125 — Request log format fix
**Shipped in:** (next release)  
T1 ✅ verified 2026-06-27 (log lines show 8-char correlation ID, no double-quote, correct `→` separator; `/api/**` routes at Information in VS output)  

**T3 — HA add-on:**

| What to verify | How |
|---------------|-----|
| Supervisor log shows well-formatted request lines with no double-quote | Install the add-on from the beta/release image; make an API call; check **Supervisor → Log** for the request line |
| Correlation ID appears in both the arrival and completion lines in the supervisor log | Make a slow request (e.g. search with many results); confirm both lines are visible and share the ID |

Full verification table: [125-request-log-format-plan.md](125-request-log-format-plan.md)

---

### #73 — Audit trail
**Shipped in:** (next release)  
T1 ✅ verified 2026-06-27 (app started in VS; POST reseed produced `Reseeded` + `BulkInserted` entries per source file; GET audit returned correct shape; DELETE audit returned 401 without key / 204 with key)  
T2 ✅ verified 2026-06-27 (`docker build -f docker/Dockerfile -t quotinator:local .` — clean build)  
No T3 requirements for this issue.

---

### #126 — Validation errors return 4xx
**Shipped in:** (next release)  
T1 ✅ verified 2026-06-27 (`GET /api/v1/quotes/random?type=x` → 422 with `InvalidType` status and valid-values message; confirmed in Scalar UI + VS log)

Full verification table: [126-validation-status-codes-plan.md](126-validation-status-codes-plan.md)

---

## Dependency Map

```
#115  ─── (none) — ✅ closed v1.7.0
#111  ─── (none) — ✅ closed v1.7.0
#109  ─── (none) — ✅ closed v1.7.0
#117  ─── (none) — ✅ closed v1.7.0
#70   ─── (none) — code complete v1.7.0; rows 10–11 pending next beta→final cycle
#125  ─── (none) — code complete; pending next release + T1/T3 verification
#126  ─── (none) — code complete; pending next release + T1 verification
#73   ─── (none) — code complete; T1 ✅ T2 ✅ 2026-06-27; pending next release
#74   ─── depends on #73 (repository base class receives IAuditWriter + ICallerContext from day one)
#75   ─── depends on #74 (read-model pattern)
#76   ─── depends on #74 (read-model pattern); transaction concern shared with #75
#77   ─── depends on #74, #75, #76
#121  ─── depends on #73 (audit in place) + #74–#77 (full repository pattern established)
```

---

## Order of Operations

| Order | Issue | Reason |
|-------|-------|--------|
| 1 | ✅ **#115** — Dapper/Core refactor | Closed v1.7.0 |
| 2 | ✅ **#111** — Flaky test | Closed v1.7.0 |
| 3 | ✅ **#109** — Search field/type bug | Closed v1.7.0 |
| 4 | ✅ **#117** — .NET SDK hook | Closed v1.7.0 |
| 5 | 🟡 **#70** — CI refactor | Code complete v1.7.0; close after T3 rows 10–11 pass in the next beta→final cycle |
| 6 | 🟡 **#125** — Request log format fix | Code complete; T1 ✅; close after next release + T3 confirmed |
| 7 | 🟡 **#126** — Validation 4xx | Code complete; T1 ✅; close after next release |
| 8 | 🟡 **#73** — Audit trail | Code complete; T1 ✅ T2 ✅; close after next release |
| 9 | **#74** — Read-model pattern | After #73; repository base class receives `IAuditWriter` + `ICallerContext` in constructor |
| 10 | **#75** — Master/detail pattern | After #74; may add optional transaction parameter to `IRepository<T>` |
| 11 | **#76** — 1:1 pattern | After #74; transaction concern shared with #75 — do close together |
| 12 | **#77** — Many-to-many pattern | After #74, #75, #76 |
| 13 | **#121** — Remove Dapper from SqliteQuoteService | After #73 (audit) + #74–#77 (all patterns in place) |

---

## PR Merge Plan

| Issue | Safe to merge alone? | Notes |
|-------|---------------------|-------|
| #115 | ✅ Merged | Refactor with no behaviour change; included in v1.7.0. |
| #111 | ✅ Merged | Self-contained; included in v1.7.0. |
| #109 | ✅ Merged | Included in v1.7.0. |
| #117 | ✅ Merged | Included in v1.7.0. |
| #70  | ✅ Merged | CI-only; included in v1.7.0. Rows 10–11 pending close. |
| #125 | Yes | Small fix; no production logic affected. |
| #126 | Yes | Validation-only changes to endpoint handlers. |
| #73  | Yes | Independent; must merge before #74. |
| #74  | Yes (after #73) | New infrastructure; nothing currently calls it. |
| #75  | Yes (after #74) | New infrastructure. |
| #76  | Yes (after #74) | New infrastructure. |
| #77  | Yes (after #74–#76) | New infrastructure. |
| #121 | Yes (after #73 + #74–#77) | Depends on audit integration and full repository pattern. |
