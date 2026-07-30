# v1.8.0 — Milestone Overview

**GitHub milestone:** #18
**Type:** Maintenance milestone (catch-all for bugs and minor improvements, targeting v1.8.x releases)
**Previous maintenance milestone:** v1.7.0 (#17) — closed 2026-06-28

Unlike a feature milestone, issues here are not necessarily related to each other. Each issue gets
its own `feature/<slug>` branch and PR rather than sharing a single milestone branch.

---

## Issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#166](https://github.com/DutchJaFO/Quotinator/issues/166) | HA add-on: split into separate stable and beta sub-add-ons | Waiting for release | T1 ✅ T2 ✅ T3 ⬜ | [166-ha-addon-stable-beta-split-plan.md](166-ha-addon-stable-beta-split-plan.md) |
| [#197](https://github.com/DutchJaFO/Quotinator/issues/197) | MSTest analyzer diagnostics (e.g. MSTEST0068) are invisible to the 0-warnings build policy — no .editorconfig exists | Planning | Not yet determined | No plan doc yet |
| [#159](https://github.com/DutchJaFO/Quotinator/issues/159) | Document repository-is-C#-only tooling policy as an ADR | Planning | Not yet determined | No plan doc yet |
| [#146](https://github.com/DutchJaFO/Quotinator/issues/146) | Audit memory-only project conventions and move genuine ones into CLAUDE.md/docs | Planning | Not yet determined | No plan doc yet |
| [#208](https://github.com/DutchJaFO/Quotinator/issues/208) | Issue-creation process: always propose label + milestone in the same draft-review pass | Planning | Not yet determined | No plan doc yet |
| [#150](https://github.com/DutchJaFO/Quotinator/issues/150) | Audit: ensure all enum-valued POCO properties have matching DB CHECK constraints | Planning | Not yet determined | No plan doc yet |
| [#151](https://github.com/DutchJaFO/Quotinator/issues/151) | Should System_-prefixed provenance tables purge rows referencing Reset-wiped entities? | Planning | Not yet determined | No plan doc yet |
| [#156](https://github.com/DutchJaFO/Quotinator/issues/156) | Reset: use the fresh-database baseline script instead of drop-all-user-tables + replay | Planning | Not yet determined | No plan doc yet |
| [#222](https://github.com/DutchJaFO/Quotinator/issues/222) | Unicode-aware case-insensitive LIKE matching (accented/non-ASCII characters) | Planning | Not yet determined | No plan doc yet |
| [#148](https://github.com/DutchJaFO/Quotinator/issues/148) | OpenAPI: document response models for existing quote/admin endpoints | Planning | Not yet determined | No plan doc yet |
| [#227](https://github.com/DutchJaFO/Quotinator/issues/227) | Import-table naming standardization + general import-file content provenance (FileResource / FileResourceLine) | Planning | Not yet determined | No plan doc yet |
| [#178](https://github.com/DutchJaFO/Quotinator/issues/178) | Changelog: add an optional one-line quote to each release entry | Planning | Not yet determined | No plan doc yet |
| [#232](https://github.com/DutchJaFO/Quotinator/issues/232) | Reduce OS-level vulnerabilities in Docker base image (Docker Scout scan) | Planning | Not yet determined | No plan doc yet |

---

## Dependency map

None of the 13 issues block each other. The order below is based on risk, effort, and
conflict-avoidance (e.g. doing a broad mechanical test-file change before other issues add more
tests to the same files) rather than any hard dependency.

---

## Order of operations

1. **#166** — HA add-on stable/beta split (largest feature in this batch; started first at the
   maintainer's explicit direction, ahead of the smaller items below)
2. **#197** — .editorconfig / MSTest analyzer severities — broad mechanical change across 76 call
   sites in 25 test files; doing this early avoids conflicts with tests added by later issues
3. **#159** — ADR: repository-is-C#-only tooling policy (docs-only)
4. **#146** — Audit memory-only conventions → CLAUDE.md/docs (docs-only)
5. **#208** — Issue-creation process: label + milestone in the same draft pass (process/docs-only)
6. **#150** — Audit enum-valued POCO properties for missing CHECK constraints (one known gap already found)
7. **#151** — System_-prefixed provenance table purge-on-Reset policy decision
8. **#156** — Reset: baseline script instead of drop-all-user-tables + replay
9. **#222** — Unicode-aware case-insensitive LIKE matching (real correctness bug, medium effort)
10. **#148** — OpenAPI: document response models for quote/admin endpoints
11. **#227** — Import-table naming standardization + FileResource/FileResourceLine provenance (largest schema/structural change)
12. **#178** — Changelog: optional one-line quote per release entry
13. **#232** — Docker Scout OS vulnerability research (no confirmed code change yet)

---

## PR merge plan

Not applicable in the usual sense — each issue in this milestone gets its own `feature/<slug>`
branch and PR, merged independently as it completes its own T1/T2 verification, rather than a
single shared milestone branch.
