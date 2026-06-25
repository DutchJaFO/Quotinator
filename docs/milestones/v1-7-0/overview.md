# v1.7.0 Maintenance Milestone — Overview

Maintenance milestone for bugs and minor improvements. Issues target v1.7.x patch releases.

---

## Issue List

| # | Title | Status |
|---|-------|--------|
| [#109](https://github.com/DutchJaFO/Quotinator/issues/109) | Search: field=author and field=character always return empty; type=person returns empty | Complete — merged to main (PR #116), pending closure |
| [#70](https://github.com/DutchJaFO/Quotinator/issues/70) | Refactor CI/Release workflows to use a shared reusable workflow | Open |
| [#115](https://github.com/DutchJaFO/Quotinator/issues/115) | Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data | Open |
| [#111](https://github.com/DutchJaFO/Quotinator/issues/111) | Investigate flaky test in Quotinator.Core.Tests | Open — blocked by #115 |
| [#74](https://github.com/DutchJaFO/Quotinator/issues/74) | Add read-model query pattern to Quotinator.Data for join and projection queries | Open |
| [#75](https://github.com/DutchJaFO/Quotinator/issues/75) | Add master/detail repository pattern to Quotinator.Data for parent/child table relationships | Open |
| [#76](https://github.com/DutchJaFO/Quotinator/issues/76) | Add 1:1 relationship pattern to Quotinator.Data | Open |
| [#77](https://github.com/DutchJaFO/Quotinator/issues/77) | Add many-to-many relationship pattern to Quotinator.Data | Open |
| [#117](https://github.com/DutchJaFO/Quotinator/issues/117) | Add .NET SDK to Claude Code remote execution environment via session-start hook | In progress — merged to main (PR #118); rows 3–4 pending fresh-session verification |
| [#73](https://github.com/DutchJaFO/Quotinator/issues/73) | Audit trail: record who did what on which record in which table | Open — deferred to auth milestone |

---

## Dependency Map

```
#109  (search envelope fix)  — independent
#70   (CI workflow)          — independent
#115  (Dapper to .Data)      — blocks #111
#111  (flaky test)           — requires #115 first
#74   (read-model queries)   — foundational for #75, #76, #77
#75   (master/detail)        — document alongside #74; independent implementation
#76   (1:1 pattern)          — document alongside #74; independent implementation
#77   (many-to-many)         — references #74 for read-model collection loading
#117  (dotnet env setup)     — independent; unblocks full pre-push checklist in cloud sessions
#73   (audit trail)          — deferred; requires auth milestone + write endpoints
```

---

## Order of Operations

| Step | Issue | Reason |
|------|-------|--------|
| 1 | #109 | Already implemented on branch — merge to main first |
| 2 | #70 | Self-contained CI config change — quick win, low risk |
| 3 | #115 | Large refactor; unblocks #111; earlier = fewer merge conflicts |
| 4 | #111 | Investigate after #115 moves tests — migration may surface more patterns |
| 5 | #74 | Foundational read-model convention needed before documenting the others |
| 6 | #75 | Master/detail — implement first concrete pattern alongside #74 docs |
| 7 | #76 | 1:1 pattern — parallel with #75 / #77; order within these three is flexible |
| 8 | #77 | Many-to-many — references #74 read-model for loading related collections |
| 9 | #117 | dotnet env setup — unblocks full pre-push checklist in cloud sessions; do early |
| 10 | #73 | Deferred — blocked by auth milestone (no user identity available yet) |

---

## PR Merge Plan

Default assumption: each issue is self-contained enough to merge when complete without waiting for the full milestone.

| Issue | Safe to merge alone? | Notes |
|-------|---------------------|-------|
| #109 | Yes | Bug fix — independent. Merge as soon as PR is green. |
| #70 | Yes | CI-only change — no production code affected. |
| #115 | Yes | Refactor with no behaviour change; build + tests are the gate. |
| #111 | Yes (after #115) | Investigation/fix — self-contained once diagnosis is complete. |
| #74 | Yes | New infrastructure; nothing currently calls it. |
| #75 | Yes | New infrastructure; nothing currently calls it. |
| #76 | Yes | New infrastructure; nothing currently calls it. |
| #77 | Yes | New infrastructure; nothing currently calls it. |
| #117 | Yes | Configuration-only change — no production code affected. |
| #73 | Deferred | Will not merge in this milestone. |

---

## Notes

**#73 — Audit trail deferred:** Explicitly deferred in the issue spec to the auth milestone. `RecordBase` already preserves state (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`) on every row, so nothing is lost by deferring — the `who` column simply requires an authenticated user identity that does not exist yet. A comment documenting the deferral is on the issue.
