# Changelog Handling — Milestone Overview

**GitHub milestone:** #15  
**Branch:** `feature/changelog-handling`  
**Status:** Not started

---

## Description

Replace hand-edited markdown changelogs with a JSON-driven system. A single `src/Quotinator.Api/changelog.json` file becomes the source of truth; a generation script produces both markdown files from it. The Blazor UI reads JSON directly — no markdown parsing. Issue #82 extends the schema with per-language translated highlights.

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
| 1 | #80 | Replace hand-edited changelogs with JSON-driven changelog system | Not started |
| 2 | #82 | Changelog: translated highlights for frontend display | Not started |

---

## PR merge plan

**Default assumption:** both issues completed before any merge to `main`.

### Evaluation

| Issue | Safe to merge early? | Reasoning |
|-------|---------------------|-----------|
| #80 | ✅ Yes | Self-contained — replaces the markdown-parsing runtime path with JSON deserialization. #82 depends on it but is inert on `main` without it. |
| #82 | ❌ Only after #80 | Adds translation resolution on top of #80. Must be merged together with or after #80. |

---

## Plan documents

- [#80 — JSON-driven changelog system](80-json-changelog-plan.md)
- [#82 — Translated highlights for frontend display](82-changelog-translations-plan.md)
