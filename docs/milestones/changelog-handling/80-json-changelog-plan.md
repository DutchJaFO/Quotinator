# Plan: #80 — JSON-driven changelog system

## Problem

`CHANGELOG.md` and `addon/CHANGELOG.md` are hand-edited. The `### Highlights` rule (plain English, bullet list, no technical terms) is easy to get wrong — #79 was caused exactly by this. The two files have different formats and must be kept in sync manually.

## Current state (verified against code, 2026-06-20)

- `ChangelogService` lives in `Quotinator.Core/Services/ChangelogService.cs`
- Parses `CHANGELOG.md` with regex at startup via `AppContext.BaseDirectory + "CHANGELOG.md"`
- `CHANGELOG.md` is published via `<Content>` in `Quotinator.Api.csproj` (line 34–35) and `COPY CHANGELOG.md .` in `docker/Dockerfile` (line 18)
- `ChangelogRelease` model: `Version`, `Date`, `Highlights` (list of strings), `Sections` (list of `ChangelogSection(Category, Items)`)
- `About.razor` shows highlights as `<ul>` bullets when present; falls back to section badges when `Highlights.Count == 0`
- `About.razor.cs` has `FormatInline()` — converts markdown-like `[text](url)` and `` `code` `` in highlight strings to HTML
- `addon/CHANGELOG.md` uses a flat bullet-list format (no `### Added/Fixed/Changed` subsections); hand-maintained separately
- No `changelog.json` exists anywhere in the repo

## Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | JSON file at `src/Quotinator.Api/changelog.json` | Not at repo root (files there must be required). Not in `data/` (that is quote source data and the HA persistent volume). Lives inside the API project alongside the i18ntext JSON files; `.csproj` `<Content>` entry uses a local path with no `..\..\` traversal. The generation script references it as `src/Quotinator.Api/changelog.json` from repo root. |
| 2 | `highlights` in JSON is an optional array of strings | Empty or absent is valid — some releases have nothing user-facing to say. Each string is one plain-English sentence. Schema validates structure; content quality is a human gate. |
| 3 | `ChangelogService` stays in `Quotinator.Core` | No API or Blazor dependency in the service layer. |
| 4 | `ChangelogService` deserializes JSON; markdown parser removed | `CHANGELOG.md` becomes generated-only; no longer the runtime source. |
| 5 | Dedicated `ChangelogEntry` Blazor control | Single-release rendering extracted to `Components/Controls/ChangelogEntry.razor` + `.razor.cs`. `About.razor` loops and renders `<ChangelogEntry>` per release. Cleaner separation; the control is independently reviewable. |
| 6 | `FormatInline()` moves to `ChangelogEntry.razor.cs` | It belongs with the control that uses it, not the page. |
| 7 | `ChangelogEntry` falls through to technical sections when `Highlights` is empty | Valid path — nothing special, no alert. Shows `Added/Changed/Fixed` sections if present; shows nothing if those are also empty. This is correct behaviour for internal-only releases. |
| 8 | Schema validation test covers structural correctness | A test reads `changelog.json` and asserts every entry has `version`, `date`, and valid array types. Catches structural mistakes before they ship — parallel to `TranslationCompletenessTests`. Does not assert non-empty highlights. |
| 9 | No "do not edit" notice in `addon/CHANGELOG.md` | HA Store renders the file as-is; a generator notice would appear in the add-on listing and look wrong. |
| 10 | `<!-- GENERATED FILE -->` HTML comment added to `CHANGELOG.md` | GitHub renders it in the repo view but it is invisible in rendered markdown; makes it clear not to hand-edit. |

## JSON schema

File: `schemas/changelog.schema.json`

Each entry in the `releases` array:

```json
{
  "version": "1.4.1",
  "date": "2026-06-20",
  "highlights": [
    "Plain-English sentence about what changed.",
    "Another sentence if needed."
  ],
  "added": ["Technical detail for the developer changelog."],
  "changed": [],
  "fixed": [],
  "removed": [],
  "issues": [71, 78, 79],
  "cves": []
}
```

Field rules (enforced by schema and the schema validation test):
- `version` — required, semver string
- `date` — required, ISO 8601 date (`YYYY-MM-DD`)
- `highlights` — optional array of plain-English strings; may be empty or absent for internal-only releases; no CVE IDs, API paths, or class names (content quality is a human gate, not machine-checkable)
- `added`, `changed`, `fixed`, `removed` — optional arrays of technical strings (developer-facing); omit when unused
- `issues` — optional array of GitHub issue numbers (integers)
- `cves` — optional array of CVE ID strings (e.g. `"CVE-2025-6965"`)

## Implementation steps

### Step 1 — JSON schema

Write `schemas/changelog.schema.json`. Add it to `Quotinator.slnx` under the existing `/schemas/` folder.

Verify: schema file exists and is valid JSON Schema.

### Step 2 — Migrate existing entries to `src/Quotinator.Api/changelog.json`

Convert all entries in `CHANGELOG.md` (v1.4.1 down to v1.0.0-beta.1) to the JSON format. Each `### Highlights` bullet becomes a `highlights` array entry. Empty `added/changed/fixed/removed` arrays are omitted. `issues` and `cves` populated where known.

Verify: entry count in JSON matches version count in current `CHANGELOG.md`; JSON is valid against the schema.

### Step 3 — Schema validation test

Add `ChangelogSchemaTests` to `Quotinator.Api.Tests` (or `Quotinator.Core.Tests`). The test reads `src/Quotinator.Api/changelog.json` directly and asserts structural correctness:
- Every entry has a non-null, non-empty `version` string
- Every entry has a non-null, non-empty `date` string
- `highlights`, `added`, `changed`, `fixed`, `removed` — when present, are arrays (not null entries)
- `issues` — when present, contains only integers
- `cves` — when present, contains only strings

Does not assert that `highlights` is non-empty — empty is a valid state for internal releases.

Verify: `dotnet test --configuration Release --filter ChangelogSchema` passes against the migrated JSON.

### Step 4 — Update `ChangelogService` to read JSON

Replace the markdown parser with `System.Text.Json` deserialization.

- Path changes to `AppContext.BaseDirectory + "changelog.json"`
- Replace `Parse(string content)` with deserialization into a private DTO and mapping to `ChangelogRelease`
- `ChangelogSection` maps: `added` → `ChangelogSection("Added", ...)`, etc.
- Private DTO is not part of the public `IChangelogService` interface
- `ChangelogRelease` and `ChangelogSection` public records are unchanged

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 5 — Update `.csproj` and Dockerfile

In `Quotinator.Api.csproj`:
- Add `<Content Include="changelog.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` (local path — no traversal needed)
- The existing `CHANGELOG.md` `<Content>` entry can be removed; `CHANGELOG.md` is a generated file and no longer read at runtime

In `docker/Dockerfile`:
- Replace `COPY CHANGELOG.md .` with `COPY src/Quotinator.Api/changelog.json ./` (adjust COPY source path to match the build context)
- `CHANGELOG.md` is still copied to the image root for reference (keep `COPY CHANGELOG.md .`); it is not read by the app

Verify: `dotnet publish` output contains `changelog.json` and does not error. Docker build succeeds.

### Step 6 — `ChangelogEntry` Blazor control

Create `src/Quotinator.Api/Components/Controls/ChangelogEntry.razor` + `.razor.cs`.

The control receives a single `ChangelogRelease` as a parameter and renders:
- `<details>` with version and date in `<summary>`
- When `Highlights.Count > 0`: `<ul>` of highlight items (using `FormatInline`) + GitHub release link
- When `Highlights.Count == 0`: falls through to rendering technical sections (`Added`, `Changed`, `Fixed`, `Removed`) if any are present; renders nothing if those are also empty

`FormatInline` moves from `About.razor.cs` into `ChangelogEntry.razor.cs`.

`About.razor` replaces its inline `<details>` rendering block with `@foreach` over `ChangelogService.Releases` rendering `<ChangelogEntry Release="release" Index="i" />`.

The version filter search JS can stay in `About.razor` or be extracted into a `<script>` block in `ChangelogEntry` — keep it in `About.razor` for now since it operates on the parent container.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 7 — Unit tests for `ChangelogEntry` rendering paths

Add tests covering the two rendering paths:
- `Highlights` non-empty → highlights list and GitHub link are rendered; no section badges
- `Highlights` empty, sections present → section badges rendered; no highlights list
- `Highlights` empty, sections empty → entry renders only the `<summary>` header; no error

Use `bUnit` if already a dependency; otherwise note that these need a manual visual check and add a TODO comment.

Verify: `dotnet test --configuration Release` passes.

### Step 8 — Write `scripts/changelog.csx`

Follows the `seed.csx` pattern (dotnet-script; reads input, writes output files).

The script:
1. Reads `src/Quotinator.Api/changelog.json`
2. Writes `CHANGELOG.md` with an HTML `<!-- GENERATED FILE — edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx -->` comment at line 1, followed by the Keep-a-Changelog header, then one `## [version] - date` block per entry with `### Highlights`, `### Added`, `### Changed`, `### Fixed`, `### Removed` subsections (omitting empty subsections)
3. Writes `addon/CHANGELOG.md` with the HA flat-bullet format (one `## [version] - date` block per entry, highlights as bullet items — no subsection headers, no generated notice)

No `--dry-run` or `--no-fetch` flags needed.

Verify: run `dotnet-script scripts/changelog.csx` from repo root; diff the output against the current files (modulo the new generated-file comment in `CHANGELOG.md`).

### Step 9 — Commit generated files and update solution

- Commit the regenerated `CHANGELOG.md` and `addon/CHANGELOG.md`
- Add `src/Quotinator.Api/changelog.json` to `Quotinator.slnx` under `/src/` (or a new `/src/Quotinator.Api/` folder if it does not already exist)
- Add `scripts/changelog.csx` to `Quotinator.slnx` under `/scripts/`
- Add `schemas/changelog.schema.json` to `Quotinator.slnx` under `/schemas/` (or wherever that folder maps)

### Step 10 — Blazor page: visual confirmation

Run the app locally and open the About page. Confirm:
- All release versions appear
- Highlights render as plain-English bullet items
- GitHub release link appears per entry
- No empty-highlights warning appears (all real entries have highlights)

### Step 11 — Update CLAUDE.md and pre-push checklist

In `CLAUDE.md`:
- Replace the changelog editing rule: edit `src/Quotinator.Api/changelog.json`, run `scripts/changelog.csx`, commit the regenerated markdown files
- Add `scripts/changelog.csx` to the pre-push checklist before the build step
- Update the `### Highlights` rules to reference the JSON field and the integrity test gate

---

## Verification table

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `schemas/changelog.schema.json` written | Manual | File exists; valid JSON Schema |
| 2 | ❌ | `src/Quotinator.Api/changelog.json` contains all existing releases | Manual | Entry count matches `CHANGELOG.md` version count; validates against schema |
| 3 | ❌ | Schema validation test: every entry has required `version`, `date`, and valid array types | Unit test | `ChangelogSchemaTests` passes under `dotnet test --configuration Release` |
| 4 | ❌ | `ChangelogService` reads JSON; markdown parser removed | Build | `dotnet build --configuration Release`: 0 warnings, 0 errors |
| 5 | ❌ | `changelog.json` in publish output; Docker build succeeds | Publish + Docker | `dotnet publish` output contains `changelog.json`; `docker build -f docker/Dockerfile -t quotinator:local .` exits 0 |
| 6 | ❌ | `ChangelogEntry` control renders highlights correctly | Build + browser | 0 build warnings; About page shows all releases with highlight bullets |
| 7 | ❌ | `ChangelogEntry` renders correctly for all three paths (highlights present / sections fallback / both empty) | Unit test | `ChangelogEntry` tests cover all three rendering paths |
| 8 | ❌ | `scripts/changelog.csx` generates both markdown files correctly | Manual | Script output diff against current files shows only the new generated-file comment |
| 9 | ❌ | Generated markdown files committed; solution updated | Git | `CHANGELOG.md` and `addon/CHANGELOG.md` match script output; `.slnx` includes new files |
| 10 | ❌ | Blazor About page confirmed in browser | Browser | All versions listed; highlights shown; no warnings visible |
| 11 | ❌ | `CLAUDE.md` and pre-push checklist updated | Manual review | Changelog rule and checklist reference `src/Quotinator.Api/changelog.json` and `scripts/changelog.csx` |

---

## What the issue proposes that differs from this plan

- Issue says `data/changelog.json` — this plan uses `src/Quotinator.Api/changelog.json` (not a data/runtime directory; co-located with the project that publishes it)
- Issue says `highlights` is "1–3 sentences" (prose, single string) — this plan keeps it as an array of strings to preserve the existing rendering loop
- Issue does not mention a dedicated `ChangelogEntry` control — this plan extracts one
- Issue does not mention a dedicated `ChangelogEntry` control or rendering tests — both added here
- Issue does not mention a schema validation test — added here, parallel to `TranslationCompletenessTests`
