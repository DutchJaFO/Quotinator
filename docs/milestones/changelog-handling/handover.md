# Handover — Changelog Handling Milestone (#15)

**Written:** 2026-06-20  
**Branch:** `feature/changelog-handling` (5 commits ahead of origin/main)  
**Next action:** Step 3 — write `src/Quotinator.Api/changelog.json`

---

## What is done

| Step | Description | Status |
|------|-------------|--------|
| Step 1 | `schemas/changelog.schema.json` written and committed | ✅ |
| Step 2 | `CHANGELOG.md` and `addon/CHANGELOG.md` archived via `git mv` to `scripts/changelog-reference/` and committed | ✅ |

The two archived files are now the reference against which the generator output will be diffed. They must not be modified.

---

## What comes next (Steps 3–13 of #80, then #82)

### Immediate: Step 3 — Convert to `src/Quotinator.Api/changelog.json`

This is the manual content migration. Read `scripts/changelog-reference/CHANGELOG.md` and `scripts/changelog-reference/addon-CHANGELOG.md` and produce a single `src/Quotinator.Api/changelog.json` that validates against `schemas/changelog.schema.json`.

**Critical facts for the conversion:**

1. **Root CHANGELOG.md** has 27 entries: `1.5.1`, `1.5.0`, `1.4.3`, `1.4.2`, `1.4.1`, `1.4.0`, `1.3.0`, `1.2.2`, `1.2.1`, `1.2.0`, `1.1.0`, `1.0.15`, `1.0.14`, `1.0.13`, `1.0.12`, `1.0.11`, `1.0.10`, `1.0.9`, `1.0.8`, `1.0.7`, `1.0.6`, `1.0.5`, `1.0.4`, `1.0.3`, `1.0.2`, `1.0.1`, `1.0.0`

2. **Addon CHANGELOG.md** has the same versions plus `1.0.0-beta.1` (the oldest entry, not in the root changelog). Include `1.0.0-beta.1` in the JSON.

3. **Dates:** all release dates appear in `scripts/changelog-reference/CHANGELOG.md`. The most recent entries that weren't in the root markdown should have dates inferred from the addon markdown.

4. **`highlights` field** maps to the `### Highlights` bullet list in the root changelog. Each bullet (`- `) becomes one string. Remove the leading `- `.

5. **`added/changed/fixed/removed` fields** map to the corresponding `### Added`, `### Changed`, `### Fixed`, `### Removed` sections in the root changelog. Same per-bullet-to-string conversion.

6. **`addon/CHANGELOG.md` content** — the addon uses a flat bullet list with no subsection headers. Each bullet is typically a highlight or a `Fixed:` note. The addon does NOT have separate `added/changed/fixed` subsections. Map the addon's `Fixed:` bullets to `fixed` in the JSON, and all other non-"Fixed:" bullets to `added`/`changed` as appropriate. For the few versions where the content differs between root and addon, use the **root** changelog as the authoritative source for `added/changed/fixed/removed` — the addon view is derived from the JSON.

7. **`issues` and `cves`** — populate where the content makes the link obvious (e.g., CVE-2025-6965 appears in the 1.4.0 section). Most older releases can have empty arrays or be omitted.

8. **`translations`** — leave empty (`{}` or omit the field) for all entries except one proof-of-concept entry for #82. The next session should add at least one entry with `translations.nl` and `translations.de` as the proof of concept required by #82 verification row 1.

9. **JSON structure:** the root key is `"releases"` — an array ordered newest-first.

10. **File location:** `src/Quotinator.Api/changelog.json` (not in `data/`, not at repo root).

After writing the JSON, add a `<Content>` entry to `src/Quotinator.Api/Quotinator.Api.csproj` so it is copied to the publish output, and add `changelog.json` to the `/src/Quotinator.Api/` folder in `Quotinator.slnx`.

Verification row 3 (entry count matches reference, file validates against schema).

---

### Steps 4–13 (after Step 3 is committed)

See `docs/milestones/changelog-handling/80-json-changelog-plan.md` for the full description of each step. Brief summary:

| Step | What | Key output |
|------|------|-----------|
| 4 | Create `Quotinator.Changelog` project; move/rewrite `ChangelogService`; create `Quotinator.Changelog.Tests`; CVE folders on both | New project builds; no `Quotinator.Core`/`Quotinator.Data` reference |
| 5 | Update `.csproj` and Dockerfile to reference new project | Docker build succeeds |
| 6 | Schema validation test (`ChangelogSchemaTests`) | Passes under filter |
| 7 | Generator script — `keepachangelog` format; `--lang` support | Diff against reference is clean |
| 8 | Generator script — `ha-addon` format | Diff against reference is clean |
| 9 | Commit generated files; update `Quotinator.slnx` | Both markdown files at original paths |
| 10 | `ChangelogEntry` Blazor control; `About.razor` loops it | 3-path unit tests pass |
| 11 | Browser confirmation: About page lists all versions | Visual check |
| 12 | Update `CLAUDE.md` pre-push checklist | References `changelog.json` and both script invocations |
| 13 | Then #82: `GetReleasesForCulture`, `About.razor.cs` update, browser language switch | See `82-changelog-translations-plan.md` |

---

## Design decisions made (key ones to remember)

### Language selection in the generator (Decision 13 — added this session)

The `--lang` argument on `scripts/changelog.csx` controls the output language for `highlights` in both formats. **English is the default, not the enforced language.** Users who want a Dutch or German changelog can run:

```
dotnet-script scripts/changelog.csx -- --format keepachangelog --output CHANGELOG.nl.md --lang nl
dotnet-script scripts/changelog.csx -- --format ha-addon --output addon/CHANGELOG.md --lang nl
```

This applies equally to `keepachangelog` and `ha-addon` formats. The `added/changed/fixed/removed` sections are always English (developer-facing). `ha-addon` has no subsections — only highlights — so `--lang` fully controls its content.

The `<!-- GENERATED FILE -->` comment in `keepachangelog` output includes the language code when non-English: `<!-- GENERATED FILE (nl) — … -->`.

`ha-addon` format never includes the generated-file notice (HA Store renders the file verbatim).

### No enforcement of English for generated changelogs

Both `CHANGELOG.md` and `addon/CHANGELOG.md` are generated in English by default as a convenience. The Quotinator project will keep them in English because the developer documentation convention is English and the HA Store is used globally. But the generator does not enforce this — it is a consumer choice.

### Translations in `changelog.json` are for the Blazor UI only

`translations` in the JSON is consumed by `IChangelogService.GetReleasesForCulture()` (issue #82) for the Blazor About page. The generator script also reads translations for the `highlights` field when `--lang` is non-English. Technical sections (`added/changed/fixed/removed`) remain English regardless.

### Single PR for both issues (#80 + #82)

Both issues ship as one PR. Merging #80 without #82 would leave the project in a transitional state during any release between merges.

### `Quotinator.Changelog` project — standalone, no app dependencies

No reference to `Quotinator.Core`, `Quotinator.Data`, or `Quotinator.Api`. Same pattern as `Quotinator.Data`. Dependency direction: `Quotinator.Api` → `Quotinator.Changelog`.

### CVE folders at project creation time

Per `docs/workflow/cve.md`: create `src/Quotinator.Changelog/CVE/` AND `tests/Quotinator.Changelog.Tests/CVE/` when the projects are created (Step 4). Include a `.gitkeep` in each. Add both to `Quotinator.slnx`.

---

## Reference file structure

```
scripts/changelog-reference/
  CHANGELOG.md              ← archived root changelog (27 versions, 1.0.0–1.5.1)
  addon-CHANGELOG.md        ← archived addon changelog (28 entries incl. 1.0.0-beta.1)

schemas/
  changelog.schema.json     ← JSON Schema Draft 2020-12; complete including translations

src/Quotinator.Api/
  changelog.json            ← to be created in Step 3 (not yet exists)
```

---

## Known inconsistencies in the archived reference files

These will cause the diff in Steps 7 and 8 to be non-clean. Document them rather than fixing the reference files:

- The root `CHANGELOG.md` has version comparison links at the footer only up to `1.0.12` — newer versions are missing their comparison links. The generator should produce a complete footer (all versions linked). This means the generated file will differ from the reference in the footer section, which is expected and documented here.
- Separator `---` lines between entries are inconsistent in the original (missing between some older entries). The generator should produce consistent separators. Expected diff.
- Versions `1.0.13` and `1.0.14` in the root changelog have `### Highlights` sections saying "Internal: …" (technical content) — these violate the current highlights rule. In the JSON, map them to the `changed` or `fixed` array and leave `highlights` empty (or use `- Internal improvements — no user-facing changes.`). This means the generated `### Highlights` will differ from the reference for those two versions. Document this as an intentional clean-up.

---

## Git state

Branch: `feature/changelog-handling`  
Commits ahead of origin: 5 (4 planning commits + Step 1 + Step 2)  
No uncommitted changes expected.

Verify with: `git log --oneline -6`

---

## Workflow reminders

- Read `docs/workflow/checklist.md` for the session-start checklist before beginning
- The milestone folder is `docs/milestones/changelog-handling/`
- Plan documents: `80-json-changelog-plan.md`, `82-changelog-translations-plan.md`, `overview.md`
- Do NOT push to origin without explicit user approval (memory rule)
- Do NOT delete branches (memory rule)
- Failing tests must not land on `main` (memory rule) — but mid-feature failing tests on `feature/changelog-handling` are acceptable while steps are incomplete
