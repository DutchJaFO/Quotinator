# Developer Documentation — Milestone Overview

**GitHub milestone:** #16  
**Type:** Living milestone (time-boxed cycles, ~30 days)  
**Cycle 1 start:** 2026-06-24  
**Cycle 1 due:** 2026-07-24  

Issues are added continuously as workflow/testing/doc gaps are found during other milestone work.

---

## Issue list

| # | Title | State |
|---|-------|-------|
| [#104](https://github.com/DutchJaFO/Quotinator/issues/104) | Workflow: add changelog update step to issue closing checklist | Open |
| [#93](https://github.com/DutchJaFO/Quotinator/issues/93) | Update testing-policy.md: document infrastructure project test pattern | Open |
| [#94](https://github.com/DutchJaFO/Quotinator/issues/94) | Define completeness criteria for living milestones | Open |

---

## Dependency map

All three issues are independent — no blocking order between them.

---

## Order of operations

1. **#104** — checklist and CLAUDE.md changes (most frequently referenced docs, fix the gap first)
2. **#93** — testing-policy.md update (isolated doc change)
3. **#94** — living milestone process definition (depends on the model decision, which is now decided: time-boxed cycles)

---

## PR merge plan

All three issues are self-contained documentation changes. They can merge together in a single PR. No early merge needed.

Branch: `feature/changelog-unreleased-workflow-rule` (also contains related changelog/workflow changes from the v1.6.4 hotfix session).
