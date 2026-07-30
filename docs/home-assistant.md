# Home Assistant Add-on

Quotinator ships as a Home Assistant add-on. Users add the GitHub repository as a custom add-on source and install it directly from the HA add-on store.

---

## Repository structure

HA discovers add-ons by searching recursively for `config.yaml` files. No separate repository is needed — the main Quotinator repo serves as the add-on repository.

| File | Purpose |
|---|---|
| `repository.yaml` | Repository metadata (name, URL, maintainer) |
| `addon/config.yaml` | Add-on manifest — name, version, arch, ports, ingress |
| `addon/DOCS.md` | User-facing documentation shown in the HA UI (Documentation tab) |
| `addon/README.md` | Short description shown in the add-on store (Info tab) |
| `addon/CHANGELOG.md` | Add-on version history — flat bullet-list per version (HA convention; no `### Added/Fixed/Changed` subsections) |
| `addon/translations/en.yaml` | English config UI labels — baseline |
| `addon/translations/nl.yaml` | Dutch config UI labels |
| `addon/translations/de.yaml` | German config UI labels |
| `addon/icon.png` | Add-on icon (128×128 PNG) |
| `addon/logo.png` | Add-on logo (250×100 PNG) |

`addon-beta/` mirrors every file above exactly (same names, same purposes) for the beta channel — see
"Stable and beta channels" below.

---

## Stable and beta channels

The repository publishes **two** add-ons from the same image (`ghcr.io/dutchjafo/quotinator`):
`addon/` (`slug: quotinator`, always the current final release) and `addon-beta/`
(`slug: quotinator_beta`, name suffixed `(BETA)`, tracks the latest pushed prerelease tag). Both are
discovered from the same `repository.yaml` — HA's recursive `config.yaml` search (see "Repository
structure" above) finds both regardless of directory name. Both set `stage: stable`, so both are
always visible in the add-on store — the beta add-on is not gated behind HA's experimental/Advanced
Mode.

Each folder is fully self-contained — there is no shared or symlinked file between them (checked
against HA's own docs and the supervisor's source during #166's planning; neither documents or
guarantees cross-add-on file sharing, so each copy is real, independent content). This means every
section below that describes an `addon/*` file applies identically to the matching `addon-beta/*`
file, and any edit to one needs the matching edit to the other in the same commit — see
CLAUDE.md's "When adding or renaming an HA add-on config option" checklist.

A beta tag only bumps `addon-beta/config.yaml`'s version; a final tag only bumps `addon/config.yaml`'s
— see `docs/workflow/checklist.md`'s "Beta tag" / "Final tag" sections.

---

## Adding to Home Assistant

Users install either add-on by adding the repository URL in HA:

1. **Settings → Add-ons → Add-on Store → ⋮ → Repositories**
2. Add `https://github.com/DutchJaFO/Quotinator`
3. Find **Quotinator** (stable) or **Quotinator (BETA)** and click **Install** — both can be installed
   side by side, each with its own data directory

---

## GHCR package visibility

The `ghcr.io/dutchjafo/quotinator` package **must be set to Public** in GitHub package settings. The HA Supervisor pulls the image without credentials — a private package returns 401 and the add-on fails to install silently.

To change visibility: GitHub → your profile → **Packages** → **quotinator** → **Package settings** → **Change visibility → Public**.

This is a one-time manual step not controlled by code or CI.

---

## Ports

The container listens on two ports:

| Port | Purpose |
|---|---|
| `8080` | Direct access — user can override the host-side port in HA if `8080` is taken |
| `8099` | Home Assistant ingress — dedicated port for the HA sidebar integration |

---

## Ingress

Ingress embeds the UI directly into the HA sidebar. The HA supervisor handles URL routing and authentication automatically — no port forwarding or reverse proxy configuration is needed.

The ingress port (`8099`) is separate from the direct access port (`8080`) so both can be used simultaneously. The REST API is reachable under the ingress path for automations and scripts running inside HA; for external tools, enable the direct access port.

---

## Data persistence

The SQLite database and source files are stored inside the add-on data directory (`/data`), which HA maps to a persistent volume. The data survives add-on updates and restarts.

---

## Releasing a new version

The `version` field in `addon/config.yaml` (stable) or `addon-beta/config.yaml` (beta) must match the Docker image tag published to GHCR. The HA Supervisor appends this value as the image tag when pulling — a mismatch causes a 404 or 401 and the install fails.

See [`ci-cd.md`](ci-cd.md) for the full release process and the complete list of files to update before tagging.

---

## Add-on translations

HA renders the config panel in the user's language. The `addon/translations/` folder provides those translations.

### What can be translated

| Scope | Translatable | Notes |
|---|---|---|
| Config option names and descriptions | ✅ | Via `translations/<lang>.yaml` |
| Port descriptions | ✅ | Via `translations/<lang>.yaml` |
| `description` field in `config.yaml` | ❌ | Hard-coded string, no translation mechanism |
| `addon/DOCS.md` (Documentation tab) | ❌ | English only — no HA translation mechanism for markdown content |
| `addon/README.md` (Info tab) | ❌ | English only — same reason |
| `addon/CHANGELOG.md` (Changelog tab) | ❌ | English only — no per-language changelog mechanism exists in HA |

### Translation file format

```yaml
configuration:
  option_name:
    name: Display name shown in config UI
    description: Help text shown below the control
network:
  8080/tcp: Description of what this port is for
```

Keys must match the option names in `config.yaml` exactly. HA falls back to `en.yaml` for any key missing from another language file.

### Workflow — when adding or renaming a config option

When you add, remove, or rename an option in `addon/config.yaml`, update **all six** translation files in the same commit — the same option exists in `addon-beta/config.yaml` too:

1. `addon/translations/en.yaml` — English (baseline)
2. `addon/translations/nl.yaml` — Dutch
3. `addon/translations/de.yaml` — German
4. `addon-beta/translations/en.yaml` — same content as #1 (identical options)
5. `addon-beta/translations/nl.yaml` — same content as #2
6. `addon-beta/translations/de.yaml` — same content as #3

Missing a file will leave that language's config UI showing the raw option key instead of a human-readable label.

---

## Pending

- `addon/icon.png`/`addon/logo.png` and `addon-beta/icon.png`/`addon-beta/logo.png` — currently use the GitHub avatar as a placeholder; replace with final artwork before publishing to the official HA add-on store.
