# Handover — Changelog Handling Milestone (#15)

**Written:** 2026-06-21 (updated end of session 4)
**Branch:** `feature/changelog-handling`
**Next action:** Reference file reconciliation (see plan below) — then browser check (Step 12) — then PR

---

## Critical issue discovered this session

The `scripts/changelog-reference/` files are wrong.

During the previous session the reference files were overwritten using `tail -n +3 CHANGELOG.md > scripts/changelog-reference/CHANGELOG.md`, which made the reference match the **generator output**, not the **original hand-written content**. The `GeneratedChangelog_MinusNotice_MatchesReference` test therefore passes trivially — it compares the generator output against itself and proves nothing.

A VS diff between the last hand-written CHANGELOG.md (branch `main`, commit `226e0b47`) and the generated one (branch `feature/changelog-handling`, commit `8f72083e`) shows significant differences: different wording, different bullet counts per version, missing entries. Practically nothing matches.

**Root cause:** `changelog.json` highlights are short user-facing summaries. The old hand-written `CHANGELOG.md` had more detailed per-version entries spread across `added`/`changed`/`fixed`/`removed` sections. The generator faithfully renders what is in the JSON — which is less detailed than what was hand-written. The gap was hidden by the tautological reference.

---

## Plan for next session

Do these steps in order. Do not skip ahead. Do not attempt to unify both changelogs into one source yet — that decision is deferred until after we understand the actual diff.

### Phase 1 — CHANGELOG.md (main changelog)

**Step 1.** Check out the hand-written CHANGELOG.md from the last release that still had it hand-written. That is the `v1.5.1` tag on `main`.

```bash
git show v1.5.1:CHANGELOG.md > scripts/changelog-reference/CHANGELOG.md
```

This replaces the current (wrong) reference with the true original. Commit the updated reference file.

**Step 2.** Generate a `changelog.json` purely from the content of that CHANGELOG.md.

- English only. No translations. No `sectionHeaders` block. Keep it simple.
- Entries go into `highlights`, `added`, `changed`, `fixed`, `removed` exactly as they appear in the source file. Do not summarise or reword.
- Do NOT change the existing `changelog.json` at `src/Quotinator.Api/changelog.json` — generate a temporary file (e.g. `scripts/changelog-reference/changelog-from-reference.json`) so there is no risk of losing the current JSON.

**Step 3.** Run the generator against that temporary JSON and write the output to a temporary file:

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input scripts/changelog-reference/changelog-from-reference.json --output scripts/changelog-reference/CHANGELOG-generated.md
```

**Step 4.** Diff the original reference against the generated output:

```bash
diff scripts/changelog-reference/CHANGELOG.md scripts/changelog-reference/CHANGELOG-generated.md
```

Record exactly which lines differ and why. The goal: understand what the generator cannot faithfully reproduce and whether that is a data gap (missing content in JSON) or a format gap (generator does not emit certain markdown features).

---

### Phase 2 — addon/CHANGELOG.md

Repeat the same four steps for `addon/CHANGELOG.md` using `--format ha-addon`.

```bash
git show v1.5.1:addon/CHANGELOG.md > scripts/changelog-reference/addon-CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon --input scripts/changelog-reference/changelog-from-reference.json --output scripts/changelog-reference/addon-CHANGELOG-generated.md
diff scripts/changelog-reference/addon-CHANGELOG.md scripts/changelog-reference/addon-CHANGELOG-generated.md
```

---

### Phase 3 — Decide

Based on the diffs, decide:

- If the diffs are small and the generator is clearly right: update the real `changelog.json` with the missing detail and regenerate both files.
- If the diffs are large and the two changelogs need different sources: design that before touching any source file. Do not implement until the approach is agreed.

**Do not attempt the unified-source solution today** — the session stated explicitly that this decision is deferred.

---

## What NOT to do next session

- Do not regenerate `CHANGELOG.md` or `addon/CHANGELOG.md` (the real ones) until Phase 3 is decided.
- Do not update `changelog.json` until the diff analysis is complete.
- Do not "fix" the `GeneratedChangelog_MinusNotice_MatchesReference` test — it is wrong by construction and needs the correct reference file before it can be made meaningful.
- Do not open the PR yet.

---

## What is done (from previous sessions)

| Step | Description | Status |
|------|-------------|--------|
| Step 1 | `schemas/changelog.schema.json` written and committed | ✅ |
| Step 2 | `CHANGELOG.md` and `addon/CHANGELOG.md` archived to `scripts/changelog-reference/` | ✅ (but overwritten — see issue above) |
| Step 3 | `src/Quotinator.Api/changelog.json` written (28 releases, 1.0.0-beta.1–1.5.1) | ✅ |
| Step 4 | `Quotinator.Changelog` project created; `ChangelogService` reads JSON; `Quotinator.Changelog.Tests` scaffolded | ✅ |
| Step 5 | Dockerfile: no change needed | ✅ |
| Step 6 | `ChangelogSchemaTests`: version/date, no-null arrays, CVE format, translations, sectionHeaders, GeneratedChangelog test (currently tautological) | ✅ (test is misleading — fix in Phase 3) |
| Step 7–8 | `scripts/changelog.csx`: `keepachangelog` and `ha-addon` formats; `--lang`, `--input`, `--output`, `--format`, `--machine-translated` flags | ✅ |
| Step 9 | `CHANGELOG.md` and `addon/CHANGELOG.md` replaced with generator output | ✅ |
| Step 10 | `ChangelogEntry.razor` + `.razor.cs`; `About.razor` uses `<ChangelogEntry>` | ✅ |
| Step 11 | `ChangelogEntryTests` (12 tests) | ✅ |
| Step 12 | Browser visual confirmation | ❌ **PENDING** |
| Step 13 | `CLAUDE.md` pre-push checklist updated | ✅ |
| Schema redesign | Per-item `machineTranslated`, `sectionHeaders` block, `sourceLanguage`, full language neutrality | ✅ |
| slnx fix | Removed `/src/Quotinator.Api/` and `/src/Quotinator.Changelog/` folders — caused GUID collision with same-named projects | ✅ |

Build: 0 warnings, 0 errors. Tests: 336 passed (6 Changelog, 82 Api, 195 Core, 54 Data).

---

## Current project layout

```
src/Quotinator.Changelog/
  Quotinator.Changelog.csproj
  Models/
    ChangelogRelease.cs
    ChangelogReleaseTranslation.cs      (all 5 sections as IReadOnlyList<ChangelogTranslationItem>)
    ChangelogSection.cs
    ChangelogSectionHeaders.cs          (NEW this session)
    ChangelogTranslationItem.cs         (NEW this session — Text + MachineTranslated?)
  Services/
    IChangelogService.cs                (Releases, SourceLanguage, SectionHeaders)
    ChangelogService.cs

scripts/
  changelog.csx
  changelog-reference/
    CHANGELOG.md                        (WRONG — contains generator output, not original)
    addon-CHANGELOG.md                  (WRONG — same issue)

tests/Quotinator.Changelog.Tests/
  ChangelogSchemaTests.cs
```

---

## Workflow reminders

- Read `docs/workflow/checklist.md` for the session-start checklist
- Do NOT push to origin without explicit user approval
- Do NOT delete branches
- Failing tests must not land on `main` — mid-feature failures on this branch are acceptable
- Single PR for both #80 and #82
