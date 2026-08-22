# Scripts

| Script | Purpose |
|---|---|
| `changelog.csx` | JSON → markdown. Generates `CHANGELOG.md`, `addon/CHANGELOG.md` and `addon-beta/CHANGELOG.md` from `data/changelog/changelog.en.json` |
| `testing/execute-sql.csx` | Runs arbitrary SQL against a Quotinator SQLite file with a writable connection — the counterpart to the read-only `Quotinator.Tools.DbInspector`. Test support only |
| `testing/sqlite-storage-probe.csx` | Probes how SQLite behaves on constrained storage. Test support only |

Run with [dotnet-script](https://github.com/dotnet-script/dotnet-script) from the repo root.

**A script that exists to support a test lives in `testing/`**, per
[ADR 010](../docs/architecture-decisions/010-repository-is-csharp-only.md) — so it is visible at a
glance which scripts the application and its workflows depend on.

> **`changelog-import.csx`, `changelog-upgrade.csx` and `changelog-reference/` were removed** (#339).
> They existed only for a round-trip fidelity check documented here, which had been broken since #309
> moved the changelog source to `data/changelog/` while its diff step still pointed at
> `src/Quotinator.Api/resources/changelog.json`. Nothing automated invoked them and CI installs no
> `dotnet-script`, so the check could not have run. A verification tool nothing exercises proves
> nothing. Git history keeps them.
>
> That left the real gap: **`changelog.csx` itself has no test**, despite writing three shipped files.
> Tracked as #340.

---

## changelog.csx — generator

Reads a per-language changelog JSON file and writes a markdown changelog in one of two formats.

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input data/changelog/changelog.en.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input data/changelog/changelog.en.json --output addon/CHANGELOG.md
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

