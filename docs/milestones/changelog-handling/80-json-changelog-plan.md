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
| 3 | Extract `Quotinator.Changelog` as a new standalone project | The changelog system has no dependency on Quotinator-specific models, services, or data infrastructure. Making it a separate project with no `Quotinator.Core` or `Quotinator.Data` reference keeps it reusable in future projects. Same pattern as `Quotinator.Data`. Dependency direction: `Quotinator.Api` → `Quotinator.Changelog`; `Quotinator.Core` does NOT reference it. |
| 4 | `ChangelogService` deserializes JSON; markdown parser removed | `CHANGELOG.md` becomes generated-only; no longer the runtime source. |
| 5 | Dedicated `ChangelogEntry` Blazor control | Single-release rendering extracted to `Components/Controls/ChangelogEntry.razor` + `.razor.cs`. `About.razor` loops and renders `<ChangelogEntry>` per release. Cleaner separation; the control is independently reviewable. |
| 6 | `FormatInline()` moves to `ChangelogEntry.razor.cs` | It belongs with the control that uses it, not the page. |
| 7 | `ChangelogEntry` falls through to technical sections when `Highlights` is empty | Valid path — nothing special, no alert. Shows `Added/Changed/Fixed` sections if present; shows nothing if those are also empty. This is correct behaviour for internal-only releases. |
| 8 | Schema validation test covers structural correctness | A test reads `changelog.json` and asserts every entry has `version`, `date`, and valid array types. Catches structural mistakes before they ship — parallel to `TranslationCompletenessTests`. Does not assert non-empty highlights. |
| 9 | No "do not edit" notice in `addon/CHANGELOG.md` | HA Store renders the file as-is; a generator notice would appear in the add-on listing and look wrong. |
| 10 | `<!-- GENERATED FILE -->` HTML comment added to `CHANGELOG.md` | GitHub renders it in the repo view but it is invisible in rendered markdown; makes it clear not to hand-edit. |
| 11 | Generation script is format-agnostic and driven by arguments | `scripts/changelog.csx` accepts `--input <path>`, `--output <path>`, and `--format <keepachangelog\|ha-addon>`. Running it twice with different `--format` and `--output` values produces both files. This makes the script reusable in any project that conforms to `schemas/changelog.schema.json` without modification. If full genericity cannot be achieved cleanly in dotnet-script, document the parameterisation points so a copy can be adapted with minimal effort. |
| 12 | **Pending architectural decision — namespace for reusable projects** | `Quotinator.Changelog` follows the existing `Quotinator.*` convention. A future architecture decision will evaluate whether reusable projects (currently `Quotinator.Data` and `Quotinator.Changelog`) should move to a different namespace and/or repository. No action required now; see memory note. All names in this milestone use `Quotinator.Changelog` pending that decision. |
| 13 | Generator supports `--lang` for output language; English is the default, not the only option | The script adds `--lang <ISO 639-1 code>` (default: `en`). When a non-English code is supplied, `highlights` is resolved from `translations.<code>.highlights`, falling back to the top-level `highlights` when no translation exists. This means `CHANGELOG.md` and `addon/CHANGELOG.md` can be generated in any language the translations support. The Quotinator defaults (both files in English) are a convenience, not an enforcement. Users are free to generate localised changelogs. The `<!-- GENERATED FILE -->` comment in `CHANGELOG.md` must include the language when non-English: `<!-- GENERATED FILE (nl) — edit … -->`. `addon/CHANGELOG.md` omits the notice regardless of language. |

## JSON schema

File: `schemas/changelog.schema.json`

The schema is defined completely in #80 — including `translations` — so the content migration produces a finished JSON file that #82 only needs to read from. No schema changes in #82.

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
  "cves": [],
  "translations": {
    "nl": { "highlights": ["Dutch plain-English sentence."] },
    "de": { "highlights": ["German plain-English sentence."] }
  }
}
```

Field rules (enforced by schema and the schema validation test):
- `version` — required, semver string
- `date` — required, ISO 8601 date (`YYYY-MM-DD`)
- `highlights` — optional array of plain-English strings; may be empty or absent for internal-only releases; no CVE IDs, API paths, or class names (content quality is a human gate, not machine-checkable)
- `added`, `changed`, `fixed`, `removed` — optional arrays of technical strings (developer-facing); omit when unused
- `issues` — optional array of GitHub issue numbers (integers)
- `cves` — optional array of CVE ID strings (e.g. `"CVE-2025-6965"`)
- `translations` — optional object keyed by ISO 639-1 code; each value has an optional `highlights` array; mirrors the quote translation model; manually curated only, never auto-generated; ignored by the `ha-addon` format generator (HA Store has no multi-language changelog support)

## Implementation steps

### Step 1 — Design the JSON schema

Write `schemas/changelog.schema.json`. The schema must be complete — including the `translations` field — before any content migration begins. Add it to `Quotinator.slnx` under `/schemas/`.

Verify: file exists and is valid JSON Schema.

### Step 2 — Archive existing changelog files

`git mv` the existing changelog files to `scripts/changelog-reference/`. These become the reference for verifying generator output.

Files to archive:
- `CHANGELOG.md` → `scripts/changelog-reference/CHANGELOG.md`
- `addon/CHANGELOG.md` → `scripts/changelog-reference/addon-CHANGELOG.md`

Commit. The archived files must not be modified after this step — they are the golden reference.

Verify: both files present in `scripts/changelog-reference/`; originals gone from their previous locations.

### Step 3 — Convert existing entries to `src/Quotinator.Api/changelog.json`

Manually convert all entries from the archived `CHANGELOG.md` to the JSON format. Each `### Highlights` bullet becomes one string in the `highlights` array. `added`, `changed`, `fixed`, `removed` arrays populated from the corresponding sections. `issues` and `cves` populated where known. `translations` omitted for now — added in #82.

Verify: entry count in JSON matches version count in `scripts/changelog-reference/CHANGELOG.md`; JSON is valid against the schema.

### Step 4 — Create `Quotinator.Changelog` project and test project

**Source project** — create `src/Quotinator.Changelog/` as a new class library (net10.0). Move `ChangelogRelease`, `ChangelogSection`, `IChangelogService`, and `ChangelogService` out of `Quotinator.Core` and into this project in a single step. Update the service to deserialise `changelog.json` (replacing the markdown parser) in the same move — no intermediate state where the moved service still reads markdown.

- `ChangelogSection` maps: `added` → `ChangelogSection("Added", ...)`, etc.
- Private deserialization DTO lives in the project; not part of the public interface
- Path: `AppContext.BaseDirectory + "changelog.json"` (note: file lives in `Quotinator.Api` output; see constraint in Decisions)
- No reference to `Quotinator.Core` or `Quotinator.Data`
- `src/Quotinator.Changelog/CVE/` folder with a `README.md` explaining its purpose

**Test project** — create `tests/Quotinator.Changelog.Tests/` (net10.0, MSTest, same package set as other test projects). `ChangelogSchemaTests` lives here.
- `tests/Quotinator.Changelog.Tests/CVE/` folder with a `README.md` (CVE workflow requires both the source project and its test project to have a CVE folder)

Add a project reference from `Quotinator.Api` to `Quotinator.Changelog`. Remove now-empty type references from `Quotinator.Core`. Add both new projects to `Quotinator.slnx`.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors; `Quotinator.Changelog.csproj` has no reference to `Quotinator.Core` or `Quotinator.Data`.

### Step 5 — Update `.csproj` and Dockerfile

In `Quotinator.Api.csproj`:
- Add `<Content Include="changelog.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />`
- Remove the existing `<Content>` entry for `CHANGELOG.md` — it is no longer read at runtime

In `docker/Dockerfile`:
- Add `COPY src/Quotinator.Api/changelog.json ./` so the app can read it at runtime
- `CHANGELOG.md` stays in the image root (keep existing `COPY CHANGELOG.md .`) as a human-readable reference when shelling into the container; it is not read by the app

Verify: `dotnet publish` output contains `changelog.json`; `docker build -f docker/Dockerfile -t quotinator:local .` exits 0.

### Step 6 — Schema validation test

Add `ChangelogSchemaTests` to `Quotinator.Changelog.Tests` (new test project, consistent with `Quotinator.Data` having its own test project). The test reads `src/Quotinator.Api/changelog.json` and asserts:
- Every entry has a non-null, non-empty `version` string
- Every entry has a non-null, non-empty `date` string
- `highlights`, `added`, `changed`, `fixed`, `removed` — when present, are arrays with no null entries
- `issues` — when present, contains only integers
- `cves` — when present, contains only strings
- `translations` — when present, each value has a `highlights` array with no null entries

Does not assert that `highlights` is non-empty — empty is valid for internal releases.

Verify: `dotnet test --configuration Release --filter ChangelogSchema` passes.

### Step 7 — Write generator: `keepachangelog` format

Write `scripts/changelog.csx` with argument support:
- `--input <path>` — path to the JSON file (default: `src/Quotinator.Api/changelog.json`)
- `--output <path>` — destination file path
- `--format <keepachangelog|ha-addon>` — output format
- `--lang <ISO 639-1 code>` — output language (default: `en`); resolves `highlights` from `translations.<code>.highlights`, falls back to top-level `highlights` when no translation exists; no language is enforced — English is a default convenience, not a requirement

Implement `keepachangelog` format first:
- When `--lang en` (default): HTML `<!-- GENERATED FILE — edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx -->` comment at line 1
- When `--lang <other>`: `<!-- GENERATED FILE (nl) — edit src/Quotinator.Api/changelog.json and run scripts/changelog.csx -->` — include the language code so readers know which language was generated
- Keep-a-Changelog header
- One `## [version] - date` block per entry with `### Highlights`, `### Added`, `### Changed`, `### Fixed`, `### Removed` subsections (omit empty subsections)
- `added/changed/fixed/removed` sections are always English (developer-facing); only `highlights` is language-resolved

If full parameterisation cannot be achieved cleanly in dotnet-script, document the configurable values as clearly labelled constants at the top of the script.

Verify: `dotnet-script scripts/changelog.csx -- --format keepachangelog --output CHANGELOG.md` produces output that diffs cleanly against `scripts/changelog-reference/CHANGELOG.md` (modulo the new generated-file comment on line 1 and any known inconsistencies in the original).

### Step 8 — Extend generator: `ha-addon` format

Add `ha-addon` format to the same script:
- One `## [version] - date` block per entry
- Highlights resolved via `--lang` (same resolution logic as `keepachangelog`), output as flat `- ` bullet items
- No subsection headers
- No generated-file notice (HA Store renders the file verbatim; a notice would appear in the add-on listing)

Verify: `dotnet-script scripts/changelog.csx -- --format ha-addon --output addon/CHANGELOG.md` produces output that diffs cleanly against `scripts/changelog-reference/addon-CHANGELOG.md`.

### Step 9 — Commit generated files and update solution

Once both formats are verified:
- Commit `CHANGELOG.md` and `addon/CHANGELOG.md` (generated, replacing the now-archived originals)
- Confirm `Quotinator.slnx` includes all new items (added in Step 4, but verify nothing was missed):
  - `src/Quotinator.Changelog/` project and `CVE/README.md` under `/src/Quotinator.Changelog/` and `/src/Quotinator.Changelog/CVE/` folders
  - `tests/Quotinator.Changelog.Tests/` project under `/tests/`
  - `scripts/changelog.csx` under `/scripts/`
  - `scripts/changelog-reference/CHANGELOG.md` and `scripts/changelog-reference/addon-CHANGELOG.md` under `/scripts/changelog-reference/`
  - `src/Quotinator.Api/changelog.json` under `/src/Quotinator.Api/` (or `/src/`)

### Step 10 — `ChangelogEntry` Blazor control

Create `src/Quotinator.Api/Components/Controls/ChangelogEntry.razor` + `.razor.cs`.

The control receives a single `ChangelogRelease` parameter and renders:
- `<details>` with version and date in `<summary>`
- `Highlights.Count > 0`: `<ul>` of highlight items (via `FormatInline`) + GitHub release link
- `Highlights.Count == 0`, sections present: section badges rendered
- `Highlights.Count == 0`, sections empty: only the `<summary>` header; no error

`FormatInline` moves from `About.razor.cs` into `ChangelogEntry.razor.cs`.

`About.razor` replaces its inline `<details>` rendering block with `@foreach` over `ChangelogService.Releases` rendering `<ChangelogEntry Release="release" Index="i" />`.

Verify: `dotnet build --configuration Release`: 0 warnings, 0 errors.

### Step 11 — Unit tests for `ChangelogEntry` rendering paths

Add tests in `Quotinator.Api.Tests` covering all three rendering paths:
- `Highlights` non-empty → highlights list and GitHub release link rendered; no section badges
- `Highlights` empty, sections present → section badges rendered; no highlights list
- `Highlights` empty, sections empty → only `<summary>` header rendered; no error

Verify: `dotnet test --configuration Release` passes.

### Step 12 — Blazor page: visual confirmation

Run the app locally and open the About page. Confirm:
- All release versions appear
- Highlights render as plain-English bullet items
- GitHub release link appears per entry
- Version filter search still works

### Step 13 — Update CLAUDE.md and pre-push checklist

- Replace the changelog editing rule: edit `src/Quotinator.Api/changelog.json`, run `scripts/changelog.csx` (both formats), commit the regenerated markdown files
- Add the two `dotnet-script` invocations to the pre-push checklist before the build step
- Update `### Highlights` rules to reference the JSON field and the schema test gate

---

## Verification table

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `schemas/changelog.schema.json` exists | Automated | `test -f schemas/changelog.schema.json` |
| 2 | ✅ | `changelog.json` is structurally valid against the schema | Automated | `dotnet test --filter ChangelogSchema` — 6 tests pass |
| 3 | ✅ | Generator runs without error for both formats | Automated | `dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/changelog.json --output /dev/null` and `--format ha-addon` both exit 0 |
| 4 | ❌ | `CHANGELOG.md` and `addon/CHANGELOG.md` are generated files | Automated | Both files contain the `GENERATED FILE` notice (`grep "GENERATED FILE" CHANGELOG.md addon/CHANGELOG.md`) |
| 5 | ✅ | Blazor page reads JSON; markdown parsing removed | Automated | `dotnet test --filter ChangelogEntry` — 12 tests pass |
| 6 | ❌ | Blazor page visually confirmed | Manual | All versions listed; highlights display correctly |
| 7 | ✅ | Pre-push checklist and `CLAUDE.md` updated | Manual review | `CLAUDE.md` references `src/Quotinator.Api/changelog.json` and `--input` in both generator commands |

---

## What the issue proposes that differs from this plan

- Issue says `data/changelog.json` — this plan uses `src/Quotinator.Api/changelog.json` (not a data/runtime directory; co-located with the project that publishes it)
- Issue says `highlights` is "1–3 sentences" (prose, single string) — this plan keeps it as an array of strings to preserve the existing rendering loop
- Issue does not mention a dedicated `ChangelogEntry` control — this plan extracts one
- Issue does not mention a dedicated `ChangelogEntry` control or rendering tests — both added here
- Issue does not mention a schema validation test — added here, parallel to `TranslationCompletenessTests`
