# Changelog Handling — Milestone Overview

**GitHub milestone:** #15  
**Branch:** `feature/changelog-handling`  
**Status:** In progress

---

## Description

Replace hand-edited markdown changelogs with a JSON-driven system. A single `src/Quotinator.Api/resources/changelog.json` file becomes the source of truth; a generation script produces both markdown files from it. The Blazor UI reads JSON directly — no markdown parsing. Issue #82 extends the schema with per-language translated highlights.

---

## Dependency map

```
#80 (JSON changelog system) → prerequisite for #82 (requires changelog.json and IChangelogService)
#82 (translated highlights) → requires #80
```

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #80 | Replace hand-edited changelogs with JSON-driven changelog system | Complete — pending issue close |
| 2 | #82 | Changelog: translated highlights for frontend display | Not started |

---

## PR merge plan

**Decision: complete both issues before any merge to `main`.**

The purpose of this milestone is to eliminate manual changelog editing. Merging #80 without #82 would leave the changelog files in a transitional state during the window between merges — any release in that window would require reasoning about whether to hand-edit the markdown or hold off. That is exactly the problem this milestone solves. The two issues ship as a single PR.

---

## Post-merge: complete translations

Issue #82 requires **at least one** proof-of-concept entry with Dutch and German `highlights` translations. After the PR is merged, add Dutch and German translations to **all** entries in `changelog.json` that have user-facing highlights — not just the proof-of-concept entry. This is not a gate for this PR; it is the first follow-up task after the merge.

---

## Plan documents

- [#80 — JSON-driven changelog system](80-json-changelog-plan.md)
- [#82 — Translated highlights for frontend display](82-changelog-translations-plan.md)
