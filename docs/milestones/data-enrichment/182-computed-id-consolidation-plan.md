# #182: Merge/consolidate entities whose computed id was affected by a data mistake

**Status:** In progress
**GitHub issue:** #182
**Depends on:** none

---

## Description

Every entity in this database takes its id from its own content, so a change to the content a hash is
built from produces a different entity rather than a changed one, and nothing re-points the foreign
keys that referred to the old id. This body of work is the mechanism that resolves that: recognising
two rows as one entity, re-keying a row whose governing content changed, and moving every dependent
with it. The sub-issues below are the concrete cases that mechanism has to serve.

---

## Sub-issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#421](https://github.com/DutchJaFO/Quotinator/issues/421) | A quote's id depends on whether the install was upgraded or fresh, after Migration009's dedupe | Planning | T1 ⬜ T2 ⬜ | [421-install-dependent-quote-id-plan.md](421-install-dependent-quote-id-plan.md) |
| [#220](https://github.com/DutchJaFO/Quotinator/issues/220) | Cross-file duplicate Quote rows caused by pre-correction StableId hashing | Planning | T1 ⬜ T2 ⬜ | none |

---

## Dependency map

#421 ─── (none): Planning
#220 ─── depends on #182's own mechanism, since no per-quote correction can merge two rows whose ids
were already fixed at conversion time: Planning

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #421 | A quote's id depends on whether the install was upgraded or fresh | Planning |
| 2 | #220 | Cross-file duplicate Quote rows caused by pre-correction StableId hashing | Planning |
