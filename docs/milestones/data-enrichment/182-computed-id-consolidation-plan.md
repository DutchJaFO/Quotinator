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
| [#440](https://github.com/DutchJaFO/Quotinator/issues/440) | Consolidate and re-key entities whose derived id moved, and carry every dependent with it | Planning | T1 ⬜ T2 ⬜ | none |
| [#220](https://github.com/DutchJaFO/Quotinator/issues/220) | Cross-file duplicate Quote rows caused by pre-correction StableId hashing | Planning | T1 ⬜ T2 ⬜ | none |

---

## Dependency map

#421 ─── (none): Planning
#440 ─── (none); blocks #220: Planning
#220 ─── depends on #440, since no per-quote correction can match two rows whose ids were already
fixed at conversion time: Planning

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #421 | A quote's id depends on whether the install was upgraded or fresh | Planning |
| 2 | #440 | Consolidate and re-key entities whose derived id moved | Planning |
| 3 | #220 | Cross-file duplicate Quote rows caused by pre-correction StableId hashing | Planning |
