# Open Issue Audit — Pending Work

**Created:** 2026-06-22  
**Context:** During the v1.6.2 release cycle we ran `gh issue list --state open` and found 56 open issues. We only read a small number of them. The rest were provisionally labelled "legitimately open in milestone" based solely on having a milestone assigned — that is not a real classification. This document tracks the work needed to do it properly.

---

## What needs to be done

### 1 — Read every open issue

For each issue below, run `gh issue view <N>` and determine its state:

| State | Meaning | Action |
|---|---|---|
| **Legitimately open** | Work not started or in progress | Confirm it is in the right milestone; no further action |
| **Deployment-pending** | Fix shipped, awaiting live HA add-on confirmation | Confirm it is in the post-deploy checklist in memory |
| **Done-not-closed** | Fix merged and confirmed, issue never formally closed | Close with verification table following `docs/workflow/checklist.md` |
| **Stale / superseded** | Requirements changed, work abandoned, or replaced by another issue | Post a comment documenting the decision, then close or re-label |

### 2 — Read the existing milestone overview files

Only two overview files exist (the other milestones have not been started):

- `docs/milestones/data-import-sources/overview.md` — covers issues in milestone #10 (Data Import & Sources)
- `docs/milestones/changelog-handling/overview.md` — covers issues in milestones #80 and #82

Check each issue's status column in the overview against the GitHub issue state. Any issue marked complete in an overview but still open on GitHub is done-not-closed.

### 3 — Update `changelog.en.json` with missing issue numbers

Each release entry in `src/Quotinator.Api/resources/changelog.en.json` has an `issues` array. Many past releases were committed before this field existed or before the convention was established — their arrays may be empty or incomplete.

For each release, cross-reference the issues resolved in that release (PR body, commit messages, milestone close date) and add any missing issue numbers to the `issues` array. Regenerate both markdown changelogs after every edit:

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
```

Run `dotnet test tests/Quotinator.Changelog.Tests --filter ChangelogSchema` after each batch of edits to verify structure.

---

## Pre-classified issues (do not re-read these)

| # | Title | Classification | Notes |
|---|---|---|---|
| #106 | Reset Database crash loop | Deployment-pending | Fixed v1.6.2; in post-deploy checklist |
| #103 | Mobile hamburger overlap | Deployment-pending | Fixed v1.6.1; in post-deploy checklist |
| #10 | Antiforgery decryption errors | Deployment-pending | Fixed v1.0.7; in post-deploy checklist |
| #104 | Add changelog step to closing checklist | Legitimately open | `checklist.md` still missing this step |

---

## Issues to read (grouped by milestone)

### No milestone assigned
- #70 — Refactor CI/Release workflows to use a shared reusable workflow
- #73 — Audit trail: record who did what on which record in which table
- #74 — Add read-model query pattern to Quotinator.Data
- #75 — Add master/detail repository pattern to Quotinator.Data
- #76 — Add 1:1 relationship pattern to Quotinator.Data
- #77 — Add many-to-many relationship pattern to Quotinator.Data
- #100 — Improve startup banner: server URLs, config summary, and DB init sequence

### Milestone 2 — Blazor: Quote Management
- #14 — Feature: explicit quote flagging and censoring
- #15 — Auth: API key authentication for write endpoints and MCP
- #16 — Write endpoints: POST/PUT/DELETE /api/v1/quotes
- #17 — Blazor management UI: Quotes
- #25 — Write endpoints: POST/PUT/DELETE /api/v1/persons
- #26 — Write endpoints: POST/PUT/DELETE /api/v1/characters
- #27 — Write endpoints: POST/PUT/DELETE /api/v1/sources
- #28 — Blazor management UI: Persons
- #29 — Blazor management UI: Characters
- #30 — Blazor management UI: Sources
- #40 — UI: dark/light mode theming
- #43 — Read endpoints: GET /api/v1/persons, /characters, /sources
- #47 — Export endpoint: GET /api/v1/quotes/export
- #50 — Rate limiting: apply stricter limits to write and import endpoints
- #52 — Blazor management UI: export quotes page
- #53 — Blazor management UI: search and filter controls on quotes list

### Milestone 3 — MCP server
- #18 — MCP server at /mcp using official .NET MCP SDK
- #23 — MCP server can be activated, default off
- #24 — MCP server has its own API token
- #42 — MCP: client setup guide and config helper
- #88 — MCP: lock down response contract, error shape, and search metadata before first merge

### Milestone 4 — Data Enrichment
- #5 — Audit and enrich quote fields (character, author, genres, date)
- #6 — Add curated translations for quotes (starting with Dutch)
- #19 — Enrichment script: fill missing quote fields using TMDB, Open Library, and Wikidata
- #35 — Enrichment script: TMDB (movies and TV)
- #36 — Enrichment script: Open Library (books)
- #37 — Enrichment script: Wikidata (people)
- #38 — Research: additional enrichment sources
- #39 — Research: genre taxonomy and relationships
- #46 — Enrichment: TVDB provider for TV quotes
- #49 — Enrichment: AniList provider for anime quotes

### Milestone 5 — User management and access rights
- #34 — Research: user model and access rights design

### Milestone 6 — Authentication: Home Assistant add-on
- #31 — Research: HA Supervisor auth flow for add-on login

### Milestone 7 — Authentication: Standalone (local accounts)
- #32 — Research: Standalone local account options

### Milestone 8 — Authentication: OAuth / OIDC
- #33 — Research: OAuth / OIDC provider options

### Milestone 9 — Documentation & Integrations
- #41 — Docs: integration examples for MagicMirror², Home Assistant, and LLM clients

### Milestone 10 — Data Import & Sources
Read `docs/milestones/data-import-sources/overview.md` first — status column is the starting point.
- #45 — Import endpoint: POST /api/v1/quotes/import
- #55 — Schema: record completeness flag and per-field verified-absent markers
- #56 — Audit log: provenance and change history across all entity types
- #57 — Seed script: deduplication strategy is inconsistent and silent (bug)
- #58 — Schema: ImportBatches table and provenance link on all entity records
- #59 — Admin: targeted soft-reset and restore by import batch
- #62 — Config and startup seeder: folder-based data discovery with configurable import path
- #63 — Import manifest: ordered file list for bundled sources and user imports
- #64 — Import conflict resolution: per-import policy with configurable defaults
- #65 — Import endpoint: preview/dry-run mode
- #67 — Schema: Conversations, ConversationLines, StageDirections, SoundCues
- #68 — Curated JSON format: conversations, stageDirections, soundCues sections
- #69 — API: conversation membership in QuoteResponse, GET /conversations/{id}, random dedup

### Milestone 11 — Blazor: Import UI
- #44 — Blazor management UI: bulk import quotes from JSON or CSV
- #60 — Blazor management UI: import batches page
- #66 — Blazor management UI: side-by-side import conflict review

### Milestone 12 — Blazor: Dashboard & Statistics
- #48 — Stats endpoint: GET /api/v1/stats
- #51 — Blazor management UI: dashboard and overview
- #54 — Statistics: dedicated stats page and extended API

### Milestone 14 — Notification system
- #81 — Startup notification: import warnings and what's new after upgrade
- #83 — Research: notification system design

### Milestone 16 — Developer Documentation
- #93 — Update testing-policy.md: document infrastructure project test pattern
- #94 — Define completeness criteria for living milestones
- #104 — Workflow: add changelog update step to issue closing checklist *(pre-classified: legitimately open)*

---

## Commands to use when resuming

```bash
# List all open issues (refresh the list in case new ones were filed)
gh issue list --state open --limit 100 --json number,title,labels,milestone

# Read a specific issue
gh issue view <N>

# Close a done-not-closed issue (always with a comment)
gh issue close <N> --comment "<verification table>"
```

---

## Suggested order

1. Read the two existing milestone overview files first — they may instantly classify several issues.
2. Work through milestone 10 (Data Import & Sources) next — it has the most open issues and a complete overview.
3. Work outward to the other milestones, leaving the no-milestone issues for last.
4. Update `changelog.en.json` as each past release's issues become clear — batch by release.
