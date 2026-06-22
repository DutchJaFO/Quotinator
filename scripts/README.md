# Changelog scripts

Three scripts manage the Quotinator changelog workflow:

| Script | Direction | Purpose |
|---|---|---|
| `changelog.csx` | JSON → markdown | Generate `CHANGELOG.md` and `addon/CHANGELOG.md` from a `changelog.json` source |
| `changelog-import.csx` | markdown → JSON | Import an existing markdown changelog into the JSON format for round-trip verification |
| `changelog-upgrade.csx` | markdown → JSON | One-time migration tool: assembled `changelog.json` from Quotinator's two hand-written reference changelogs during the v1.5.1 upgrade |

Both scripts are run with [dotnet-script](https://github.com/dotnet-script/dotnet-script) from the repo root.

---

## changelog.csx — generator

Reads `src/Quotinator.Api/resources/changelog.json` and writes a markdown changelog in one of two formats.

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.json --output addon/CHANGELOG.md
```

### Options

| Option | Default | Description |
|---|---|---|
| `--format <name>` | *(required)* | `keepachangelog` or `ha-addon` |
| `--input <path>` | *(required)* | JSON source file |
| `--output <path>` | stdout | Destination file path |
| `--audience <name>` | `ha-addon` | Audience key for `ha-addon` format `audienceHighlights` lookup. Pass a custom key (e.g. `customer`) to produce output tailored to a different audience. |
| `--fallback <bool>` | `true` | When a release has no highlights content: `true` skips the Highlights section; `false` emits `--fallback-message` instead. For `ha-addon`, "no content" means no `audienceHighlights.<audience>` key; for `keepachangelog` it means an empty or absent `highlights` array. |
| `--fallback-message <text>` | `"No user-facing changes."` | Message emitted when `--fallback false` and a release has no highlights content. |
| `--lang <code>` | `en` | ISO 639-1 language code; resolves from `translations.<code>.*` with fallback to source language |
| `--machine-translated <bool>` | `true` | Default `machineTranslated` value for translation items that do not specify the property |
| `--line-endings <style>` | `lf` | Line ending style for the output file: `lf` or `crlf` |

### Formats

**`keepachangelog`** — full section output with `### Highlights`, `### Added`, `### Changed`, `### Fixed`, `### Removed` headers, `---` separators between versions, and footer link references.

**`ha-addon`** — flat bullet list per version, highlights only, no section headers, no footer links. Matches the format expected by the Home Assistant add-on store.

### Audience highlights (`audienceHighlights`)

The `ha-addon` format checks for an `audienceHighlights.<audience>` key in each release (where `<audience>` is the value of `--audience`, defaulting to `ha-addon`) before falling back to `highlights`:

| State | `--fallback true` (default) | `--fallback false` |
|---|---|---|
| Key absent | Use standard `highlights` | Emit `--fallback-message` (default: `"No user-facing changes."`) |
| Key present, array empty (`[]`) | Emit `"No user-facing changes."` | Emit `"No user-facing changes."` |
| Key present, array non-empty | Use those items | Use those items |

This lets a single `changelog.json` produce tailored output for different audiences without duplicating content. Use `--fallback false` when generating for an audience that has not been explicitly configured — every unconfigured entry will emit the fallback message rather than leaking standard highlights to that audience. The same flag applies to `keepachangelog`: releases with no highlights emit the fallback message in the Highlights section instead of omitting it.

---

## changelog-import.csx — importer

Parses an existing markdown changelog and writes a `changelog.json`-compatible JSON file. Intended for onboarding existing changelogs into the Quotinator system and for round-trip verification (import → generate → diff against original).

```bash
dotnet-script scripts/changelog-import.csx -- --format keepachangelog --input scripts/changelog-reference/CHANGELOG.md --output scripts/changelog-reference/changelog-from-reference.json
dotnet-script scripts/changelog-import.csx -- --format ha-addon        --input scripts/changelog-reference/addon-CHANGELOG.md --output scripts/changelog-reference/addon-changelog-from-reference.json
```

### Options

| Option | Default | Description |
|---|---|---|
| `--format <name>` | *(required)* | `keepachangelog` or `ha-addon` |
| `--input <path>` | *(required)* | Source markdown file |
| `--output <path>` | stdout | Destination JSON file |
| `--highlights-only` | off | Strip `added`, `changed`, `fixed`, `removed`; keep only `highlights` |
| `--line-endings <style>` | `lf` | Line ending style for the output file: `lf` or `crlf` |

### Formats

**`keepachangelog`** — recognises `### Highlights`, `### Added`, `### Changed`, `### Fixed`, `### Removed` section headers and maps bullets to the corresponding JSON array. Unknown `###` headings are silently ignored. Footer link references and `---` separators are skipped.

**`ha-addon`** — no section headers expected; all bullets map to `highlights`.

### `--highlights-only`

Strips all sections except `highlights` from the output. Useful for producing a side-by-side comparison of highlight text between two source changelogs before deciding which wording becomes canonical.

```bash
dotnet-script scripts/changelog-import.csx -- --format keepachangelog --highlights-only --input scripts/changelog-reference/CHANGELOG.md    --output scripts/changelog-reference/changelog-highlights.json
dotnet-script scripts/changelog-import.csx -- --format ha-addon        --highlights-only --input scripts/changelog-reference/addon-CHANGELOG.md --output scripts/changelog-reference/addon-changelog-highlights.json
```

---

---

## changelog-upgrade.csx — one-time migration tool

**Quotinator-specific.** Used during the v1.5.1 → changelog-json standard upgrade to assemble `src/Quotinator.Api/resources/changelog.json` from Quotinator's two hand-written reference changelogs. Kept for historical reference; not intended as a reusable tool.

```bash
dotnet-script scripts/changelog-upgrade.csx
```

No options — all paths are hardcoded to the Quotinator reference files.

### What it does

1. Imports `scripts/changelog-reference/CHANGELOG.md` → `changelog-root.json` (full keepachangelog)
2. Imports `scripts/changelog-reference/CHANGELOG.md` → `changelog-highlights.json` (highlights only)
3. Imports `scripts/changelog-reference/addon-CHANGELOG.md` → `changelog-ha-addon.json` (ha-addon)
4. Builds `target-changelog.json` by:
   - Copying all content from `changelog-root.json` **without** the `highlights` section
   - Adding ha-addon bullets as the standard `highlights` field for each matching version
   - Adding root highlights as `audienceHighlights.ha-addon` for each matching version
   - Appending any versions present in `changelog-ha-addon.json` but absent from root (oldest last)

### Output field mapping

| Field in `target-changelog.json` | Source |
|---|---|
| `highlights` | `changelog-ha-addon.json` bullets for that version |
| `audienceHighlights.ha-addon` | `changelog-highlights.json` bullets for that version |
| `added`, `changed`, `fixed`, `removed` | `changelog-root.json` sections |

---

## Integration test — verifying script changes

Run this whenever `changelog-import.csx`, `changelog-upgrade.csx`, or `changelog.csx` is modified. It verifies that the scripts correctly reconstruct a `changelog.json`-equivalent from the reference markdown files.

### Step 1 — Regenerate from reference markdown

```bash
dotnet-script scripts/changelog-upgrade.csx
```

This re-runs the full import + assembly pipeline and writes `scripts/changelog-reference/target-changelog.json`.

### Step 2 — Diff against the live changelog

```bash
diff scripts/changelog-reference/target-changelog.json src/Quotinator.Api/resources/changelog.json
```

### Step 3 — Review the known delta

The diff will always contain fields that cannot be recovered from markdown. These differences are expected and must not be treated as regressions:

| Field | Recoverable from markdown? | Notes |
|---|---|---|
| `issues` | No — manual only | `#N` in prose is ambiguous; must be set by hand |
| `cves` | Yes — auto-detected | Regex `CVE-\d{4}-\d{4,}` applied to all section text |
| `audienceHighlights` | Yes — assembled from both markdowns | Present in `target-changelog.json` |
| `translations` | No — not in markdown | Must be added manually to `changelog.json` |
| `unreleased` | Yes — if `[Unreleased]` block exists | Not present in the reference markdowns |
| `sectionHeaders` / `sourceLanguage` | No — global settings | Not recoverable from content |

Any diff line **outside** this known delta indicates a regression in the scripts.

### Step 4 — Promote if clean

If the diff contains only expected delta, no action is needed. If you changed the scripts to produce additional structured output (e.g. auto-detecting issue numbers), promote `target-changelog.json` to `src/Quotinator.Api/resources/changelog.json`:

```bash
cp scripts/changelog-reference/target-changelog.json src/Quotinator.Api/resources/changelog.json
```

Review the result before committing — `changelog.json` may contain manually curated fields (`issues`, `translations`) that must be preserved.

---

## Reference files

`scripts/changelog-reference/` holds hand-written originals and derived comparison files:

| File | Description |
|---|---|
| `CHANGELOG.md` | Hand-written original — never overwrite |
| `addon-CHANGELOG.md` | Hand-written original — never overwrite |
| `changelog-root.json` | Full import of `CHANGELOG.md` (keepachangelog) — intermediate, regenerated by `changelog-build.csx` |
| `changelog-highlights.json` | Highlights-only import of `CHANGELOG.md` — intermediate, regenerated by `changelog-build.csx` |
| `changelog-ha-addon.json` | Full import of `addon-CHANGELOG.md` (ha-addon) — intermediate, regenerated by `changelog-build.csx` |
| `changelog-from-reference.json` | Full import of `CHANGELOG.md` (keepachangelog) — standalone reference copy |
| `addon-changelog-from-reference.json` | Full import of `addon-CHANGELOG.md` (ha-addon) — standalone reference copy |
| `addon-changelog-highlights.json` | Highlights-only import of `addon-CHANGELOG.md` — standalone reference copy |
| `target-changelog.json` | Final assembled output of `changelog-build.csx` — review before promoting to `changelog.json` |

The reference markdown files are the source of truth for the hand-written originals. They must never be overwritten by any script, test, or generator.
