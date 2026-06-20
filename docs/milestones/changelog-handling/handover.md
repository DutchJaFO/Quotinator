# Handover — Changelog Handling Milestone (#15)

**Written:** 2026-06-20 (updated end of session 2)
**Branch:** `feature/changelog-handling` (10 commits ahead of origin/main)
**Next action:** Step 5 — update Dockerfile

---

## What is done

| Step | Description | Status |
|------|-------------|--------|
| Step 1 | `schemas/changelog.schema.json` written and committed | ✅ |
| Step 2 | `CHANGELOG.md` and `addon/CHANGELOG.md` archived via `git mv` to `scripts/changelog-reference/` and committed | ✅ |
| Step 3 | `src/Quotinator.Api/changelog.json` written (28 releases, 1.0.0-beta.1–1.5.1); `.csproj` and `.slnx` updated | ✅ |
| Step 4 | `Quotinator.Changelog` project created; `ChangelogService` rewrites to read JSON; `Quotinator.Changelog.Tests` scaffolded; CVE `.gitkeep` folders added; `Quotinator.Api` references new project; old `ChangelogService.cs` deleted from `Quotinator.Core` | ✅ |
| Hotfix | `CHANGELOG.md` and `addon/CHANGELOG.md` restored to their original paths (were missing from disk, breaking VS) — these are copies from the archive and will be replaced by the generator in Step 9 | ✅ |

Build: 0 warnings, 0 errors. Tests: 195 passed.

---

## Key decisions made (carry forward)

- `scripts/changelog-reference/` holds the archived originals for generator diffing — **not in the slnx** (temporary test fixture)
- `CHANGELOG.md` and `addon/CHANGELOG.md` live at their permanent paths in the slnx — currently restored copies, replaced by generator output in Step 9
- `ChangelogRelease` now has `Issues`, `Cves`, and `Translations` fields (ready for Steps 6 and 13)
- Translations on 1.3.0 are the proof-of-concept for #82 — AI-generated, banner disclaimer planned
- `Quotinator.Changelog` has no dependency on `Quotinator.Core` or `Quotinator.Data`
- Generator runs at tag time; Step 13 documents this in `CLAUDE.md` pre-push checklist

---

## Steps remaining (5–13 + #82)

| Step | What | Key output |
|------|------|-----------|
| **5** | Update Dockerfile — remove old `COPY CHANGELOG.md .`; `changelog.json` is already copied via `COPY . .` | Docker build succeeds; `dotnet publish` output contains `changelog.json` |
| **6** | `ChangelogSchemaTests` in `Quotinator.Changelog.Tests` — validates every entry has version/date, arrays have no nulls, translations structure correct | `dotnet test --filter ChangelogSchema` passes |
| **7** | `scripts/changelog.csx` — `keepachangelog` format with `--lang` support | diff against `scripts/changelog-reference/CHANGELOG.md` is clean (modulo known inconsistencies) |
| **8** | Extend `scripts/changelog.csx` — `ha-addon` format | diff against `scripts/changelog-reference/addon-CHANGELOG.md` is clean |
| **9** | Commit generated files; confirm `.slnx` is complete | Both markdown files generated and committed at permanent paths |
| **10** | `ChangelogEntry` Blazor control; `About.razor` loops it; `FormatInline` moves from `About.razor.cs` | Build clean |
| **11** | Browser confirmation: About page lists all versions | Visual check |
| **12** | `ChangelogEntry` unit tests (3 rendering paths) | `dotnet test --configuration Release` passes |
| **13** | Update `CLAUDE.md` pre-push checklist; reference `changelog.json` and both `dotnet-script` invocations | Checklist updated |
| **#82** | `GetReleasesForCulture()` on `IChangelogService`; `About.razor.cs` reads culture; browser language switch shows translated highlights | See `82-changelog-translations-plan.md` |

---

## Step 5 detail

The Dockerfile currently has (around line 18):
```
COPY CHANGELOG.md .
```
This must be removed — `CHANGELOG.md` is no longer read at runtime (the app reads `changelog.json`). `changelog.json` is already included via the earlier `COPY . .` layer in the Dockerfile (or the per-project copy structure — verify exact Dockerfile contents before editing).

Note from plan (Decision in Step 5): "CHANGELOG.md stays in the image root as a human-readable reference when shelling into the container" — but since it is now a generated file and the source of truth is `changelog.json`, keeping it in the image is optional. Simplest: just remove the `COPY CHANGELOG.md .` line and don't add it back.

Verify: `dotnet publish` output contains `changelog.json`; `docker build -f docker/Dockerfile -t quotinator:local .` exits 0.

---

## Step 6 detail

Test class `ChangelogSchemaTests` in `tests/Quotinator.Changelog.Tests/`. It reads `src/Quotinator.Api/changelog.json` directly (not via the service — the path is relative to the repo root, found by walking up from `AppContext.BaseDirectory`). Asserts:
- Every entry: non-null/empty `version` and `date`
- `highlights`, `added`, `changed`, `fixed`, `removed`: when present, arrays with no null entries
- `issues`: when present, contains only integers (already enforced by the type, but validate the count is sane)
- `cves`: when present, each matches `^CVE-\d{4}-\d{4,}$`
- `translations`: when present, each value has a `highlights` array with no null entries

Does NOT assert `highlights` is non-empty — empty is valid for internal releases.

---

## Step 7 detail — known diff exceptions

The generator output will differ from the reference in these documented ways (do NOT fix the reference files):
- Footer comparison links: reference has links only up to `1.0.12`; generator should produce complete footer for all versions. Expected diff.
- `---` separators between entries: inconsistent in the original; generator should produce consistent separators. Expected diff.
- Versions `1.0.13` and `1.0.14`: reference `### Highlights` sections say "Internal: …" (technical). In `changelog.json` these are normalised to "Internal improvements — no user-facing changes." so the generated output differs. Expected and intentional clean-up.

---

## New project layout

```
src/Quotinator.Changelog/
  Quotinator.Changelog.csproj    (net10.0, no external deps, InternalsVisibleTo: Quotinator.Changelog.Tests)
  Models/
    ChangelogRelease.cs          (Version, Date, Highlights, Sections, Issues, Cves, Translations)
    ChangelogReleaseTranslation.cs (Highlights)
    ChangelogSection.cs          (Category, Items)
  Services/
    IChangelogService.cs
    ChangelogService.cs          (reads changelog.json via System.Text.Json; private DTOs inside)
  CVE/
    .gitkeep

tests/Quotinator.Changelog.Tests/
  Quotinator.Changelog.Tests.csproj  (MSTest 4.2.3)
  CVE/
    .gitkeep
```

---

## Namespace changes consumers must know

- `IChangelogService`, `ChangelogService` moved from `Quotinator.Core.Services` → `Quotinator.Changelog.Services`
- `ChangelogRelease`, `ChangelogSection` moved from `Quotinator.Core.Services` → `Quotinator.Changelog.Models`
- `ChangelogReleaseTranslation` is new in `Quotinator.Changelog.Models`
- `About.razor.cs` now has both `using Quotinator.Changelog.Services;` and `using Quotinator.Core.Services;`
- `Program.cs` now has `using Quotinator.Changelog.Services;` alongside existing Core usings

---

## Git state

Branch: `feature/changelog-handling`
Commits ahead of origin: 10
No uncommitted changes.

Verify with: `git log --oneline -10`

---

## Workflow reminders

- Read `docs/workflow/checklist.md` for the session-start checklist
- Do NOT push to origin without explicit user approval
- Do NOT delete branches
- Failing tests must not land on `main` — mid-feature failures on this branch are acceptable
- Single PR for both #80 and #82
