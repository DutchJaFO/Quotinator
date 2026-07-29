# #217 — Establish conflict-resolution coverage for every bundled source file

**Status:** Released
**GitHub issue:** #217
**Depends on:** none

---

## Description

Parent (tracking) issue. We cannot predict what our external sources will deliver, nor how a new
bundled file will interact with data already seeded from another — this issue forces every currently-
bundled file through `review`-policy duplicate resolution and a declarative conflict-resolution rule
file, so that unpredictability becomes tractable and directly prepares for the Data Enrichment
milestone's enrichment phase. It also rehearses, by hand, the exact review → export → decide → apply →
verify workflow the future UX milestone's management UI will present visually. Carries no
implementation of its own — see `issues.md` → "Splitting an issue into sub-issues" for why a parent's
plan doc mirrors `overview.md` rather than carrying Steps and a verification checklist.

**Startup seeding is disabled for the duration of this body of work**, via an internal constant in
`QuotinatorDatabaseInitializer.OnInitialisedAsync` gating its `SeedIfEmptyAsync` call (schema
migrations still run normally). Reverting this constant is the parent issue's own Definition of Done
item, tracked here rather than left to memory.

**Testing methodology — two Docker scenarios per bundled file:**
- **(a)** Clean database, only the file under review imported (`review` policy) → that file's own
  conflict-resolution file, for conflicts internal to the file itself.
- **(b)** Clean database, every previously-processed file already imported with its conflicts
  resolved, then the new file imported (`review` policy) → a conflict-resolution file for conflicts
  caused by interaction with existing data. Never applies to the first file processed.

File order: internal-first (`quotinator-curated.json`, `quotinator-series-universe.json`), then
external (`NikhilNamal17_popular-movie-quotes.json`, `vilaboim_movie-quotes.json`) — exact order
within each pair confirmed at kickoff. Whenever a conflict needs a developer decision, it is exported
via #163's `GET /import/actions/export` endpoint, kept as a working artifact, and also presented
inline as a markdown table in chat for review — genuinely ambiguous conflicts are asked one at a
time. This body of work also deliberately exercises the Modify path for non-conflicting field
corrections, not just conflict decisions proper.

---

## Sub-issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#177](https://github.com/DutchJaFO/Quotinator/issues/177) | ImportBatches.Status never set to Applied via the staged decide→apply flow, breaking reversal | Released | T1 ✅ T2 ✅ | [177-import-batch-status-applied-plan.md](177-import-batch-status-applied-plan.md) |
| [#181](https://github.com/DutchJaFO/Quotinator/issues/181) | Minimal per-source conflict-resolution rule file + curated field-override preload (scope widened to all 4 bundled files) | Released | T1 ✅ T2 ✅ | [181-minimal-conflict-resolution-rule-file-plan.md](181-minimal-conflict-resolution-rule-file-plan.md) |
| [#153](https://github.com/DutchJaFO/Quotinator/issues/153) | Declarative conflict-resolution file for recurring third-party source conflicts (Phase 2) | Released | T1 ✅ T2 ✅ | [153-declarative-conflict-resolution-plan.md](153-declarative-conflict-resolution-plan.md) |

---

## Dependency map

```
#177 (ImportBatches.Status fix) → requires nothing; unblocks #181 — #181's own per-file testing
  methodology needs a working POST /import/actions/reverse to iterate resolve→apply→reverse→retry
#181 (minimal per-source rule files, all 4 bundled files) → requires #177; unblocks #153 — #153's own
  plan doc explicitly builds on #181's shipped rule-file format rather than designing one from scratch
#153 (declarative/generated rule files, Phase 2) → requires #181; also requires #163 (shipped) for its
  rule-generation step's input shape (ImportActionFieldRow), already satisfied
```

Strictly sequential — each sub-issue depends on the one before it. No parallel work is possible within
this parent issue's own scope.

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #177 | ImportBatches.Status never set to Applied, breaking reversal | Released |
| 2 | #181 | Minimal per-source conflict-resolution rule file (all 4 bundled files) | Released |
| 3 | #153 | Declarative conflict-resolution file, Phase 2 | Released |

#177 first because the reversal it fixes is a hard prerequisite for #181's own iterative testing
methodology. #181 before #153 because #153 builds on #181's shipped rule-file format rather than
inventing one independently.
