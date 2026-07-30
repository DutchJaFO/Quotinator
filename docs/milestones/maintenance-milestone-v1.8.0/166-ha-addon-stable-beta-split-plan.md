# #166 — HA add-on: split into separate stable and beta sub-add-ons

**Status:** Waiting for release
**GitHub issue:** #166
**Tiers required:** T1, T2, T3
**Depends on:** Nothing

---

## Background

Quotinator currently ships one HA add-on definition (`addon/`). The existing beta-tag workflow
(`docs/workflow/checklist.md` → "Beta tag (T1 + T2 gate)") bumps that single `config.yaml`'s version
to a `-beta` suffix, pushes a prerelease tag, then bumps it back to the final version at final tag —
so "beta" is a transient state of one add-on, not a standing, side-by-side-installable channel.

**Research done before writing this plan (2026-07-30):**

- **Nesting-depth question from the issue is resolved.** The issue flagged uncertainty about whether
  the HA supervisor's add-on discovery would find a `config.yaml` nested two levels deep
  (`addon/quotinator/`, `addon/quotinator_beta/`). Read the supervisor's own source directly
  (`supervisor/store/data.py`, `_find_app_configs`): it scans with `path.glob("**/config.*")` — a
  recursive, depth-unlimited glob (excluding dotfile-prefixed and `rootfs` path segments). Nesting
  works; it was never actually a constraint on layout choice.
- **Two real add-on repositories compared** — Music Assistant (`music-assistant/home-assistant-addon`,
  already cited in the issue) and a second example the developer found
  (`chrisuthe/Multi-SendSpin-Player-Container`). Both confirm the general shape (a `repository.yaml`/
  `repository.json` at repo root — Quotinator already has `repository.yaml` — plus self-contained
  sibling add-on folders, each with its own `config.yaml`/`DOCS.md`/`CHANGELOG.md`/`translations/`/
  icon/logo). They disagree on two specifics:
  - Music Assistant: **same** image repo for stable and beta (`image: ghcr.io/music-assistant/server`
    in both `config.yaml`s), channels differentiated only by the `version:`/`slug:`/`name:` fields.
    `stage: stable` in both — beta is a normal, always-visible sibling add-on.
  - Multi-SendSpin: **separate** image repos per channel (`...-hassio` vs `...-hassio-dev`), and
    `stage: experimental` on its dev channel (hidden unless the user enables HA's Advanced Mode).
- **Decision: follow Music Assistant's model, not Multi-SendSpin's.** Quotinator's `.github/workflows/release.yml`
  already builds one image (`ghcr.io/dutchjafo/quotinator`) and tags prereleases with their own semver
  tag (e.g. `1.8.0-beta1`, via `docker/metadata-action`'s `type=semver,pattern={{version}}`) from the
  same repo — this is already the Music Assistant shape, not the continuous-dev-branch shape
  Multi-SendSpin's separate image/experimental-stage model exists to support. No CI image-publishing
  change is needed; only the two `config.yaml`s' `version:` fields point at different tags of the same
  image, exactly like Music Assistant's.
- **No CI workflow change needed for version bumping.** `release.yml` never touches `config.yaml` at
  all — the beta/final version bump is a manual step in `checklist.md` ("Beta tag" / "Final tag"
  sections), done and committed to `main` before the tag is pushed. The split only changes *which*
  file that manual step edits (the beta one only for a beta tag, the stable one only for a final tag),
  not the mechanism.
- **Icon/logo are reused as-is.** Music Assistant's beta folder uses byte-identical `icon.png`/
  `logo.png` to its stable folder (confirmed via matching git blob SHAs) — no separate "BETA" badge
  artwork. Quotinator's beta add-on does the same: copy `addon/icon.png`/`logo.png` unchanged.

**Layout decision:** keep `addon/` unchanged as the stable add-on (avoids a git-history-disrupting
rename of existing, working files) and add a new sibling top-level folder `addon-beta/` for the beta
add-on — a flat sibling layout like Music Assistant's own repo root, without nesting both under
`addon/` (nesting is *possible* per the discovery research above, but not simpler than a flat sibling,
so there's no reason to add a directory level the issue's own alternative didn't require).

**Decision: `addon-beta/DOCS.md` and `README.md` get full, real content — no shared/shortcut file.**
Checked HA's official docs (`developers.home-assistant.io/docs/add-ons/presentation` and
`.../repository`) — both are silent on whether add-ons in the same repository can share content via
symlink or include. Read the supervisor's own source (`supervisor/apps/model.py`): `DOCS.md`/
`README.md` paths are read as plain filesystem paths, so a symlink would likely resolve transparently
— but that is an inference from implementation, not anything HA documents or guarantees as supported.
Combined with this repo being developed on Windows (committing a real symlink to git requires
Developer Mode/elevated privileges and `core.symlinks` enabled — fragile for a solo-maintained repo),
a shortcut is ruled out: each add-on folder is fully self-contained with real content, matching every
other file in Step 3. The drift risk this raises for future `addon/DOCS.md` edits is addressed by
extending the existing "adding or renaming an HA add-on config option" CLAUDE.md checklist (Step 5)
to cover content edits generally, not just config-option changes.

---

## Spec requirements (from the GitHub issue)

1. Split `addon/` into two self-contained add-on definitions, each with its own `config.yaml`,
   `DOCS.md`, `README.md`, `CHANGELOG.md`, `translations/`, icon/logo, `apparmor.txt`:
   - stable (current `slug: quotinator`, unchanged)
   - beta (new `slug: quotinator_beta`, name suffixed `(BETA)`, same image repository, distinguished
     by `version:` only)
2. Update the beta-tag / final-tag release checklist so a beta tag only bumps the beta add-on's
   `config.yaml`, and a final tag only bumps the stable add-on's `config.yaml`.
3. Confirm the HA supervisor actually discovers and lists both add-ons side by side from one
   repository (T3, live).

---

## Scope changes

**Widened 2026-07-30, developer direction:** while implementing the changelog-regeneration step
below, the developer flagged that `addon/CHANGELOG.md` has grown large (291 lines across 38 releases
at time of writing) — unwieldy in HA's add-on info panel. Decision: cap every generated changelog
(not only the two HA add-on ones — the root `CHANGELOG.md` too) to the 3 most recent releases, with
older history left in place on GitHub (linked from a closing note) rather than in the generated file
itself. This is unrelated to the stable/beta split itself but touches the same changelog-generation
step #166 already needed for the new `addon-beta/CHANGELOG.md`, so it's folded in here per the
developer's explicit choice rather than filed as a separate issue.

---

## Steps

**Numbered in actual execution order.** The changelog-cap scope addition above landed on the same
changelog-generation mechanism `addon-beta/CHANGELOG.md` (Step 3) needs, so the generator support and
the two changelogs that already existed independently of the beta folder (Steps 1–2) were done first
— `addon-beta/CHANGELOG.md` then just consumes an already-verified `--max-releases` flag in one pass
instead of being generated once and patched again after the flag landed.

### 1. Add `--max-releases` support to `scripts/changelog.csx`

**Status:** ✅ Done

Added a `--max-releases <N>` option (both `keepachangelog` and `ha-addon` formats): only the N most
recent releases are rendered; older ones are dropped with a closing note pointing to the GitHub
Releases page (`https://github.com/DutchJaFO/Quotinator/releases`). Reference-style compare links in
the `keepachangelog` footer still resolve correctly for the oldest *shown* release — its link compares
against the next older (unshown) release rather than falling back to a bare tag link, so no link ever
points at a nonexistent range. Verified manually against the real `changelog.en.json` with
`--max-releases 3` for both formats before wiring into real output paths (temp files, since verified
correct then deleted, not committed).

### 2. Regenerate all three changelogs with `--max-releases 3`

**Status:** ✅ Done. All three regenerated; `addon/CHANGELOG.md` and `addon-beta/CHANGELOG.md` diffed
identical beyond the generation timestamp header line.

Regenerate `CHANGELOG.md`, `addon/CHANGELOG.md`, and (once Step 3 creates it) `addon-beta/CHANGELOG.md`
with `--max-releases 3` added to each invocation:
```
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md --max-releases 3
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md --max-releases 3
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon-beta/CHANGELOG.md --max-releases 3
```
Update CLAUDE.md's Pre-Push Checklist regenerate-commands block (Step 5 below) to match, permanently
— every future regeneration uses `--max-releases 3` from now on, not just this one.

### 3. Create `addon-beta/` as a self-contained beta add-on

**Status:** ✅ Done

- `config.yaml`: `name: Quotinator (BETA)`, `slug: quotinator_beta`, `version: "1.8.0"` (matches the
  current stable image tag — no beta prerelease tag exists yet at time of writing; the next beta tag
  will bump this per Step 4), explicit `stage: stable` (matches Music Assistant — always visible, not
  gated behind HA's experimental/Advanced Mode). `image:` stays `ghcr.io/dutchjafo/quotinator` (same
  repo, per the Background decision).
- `icon.png`/`logo.png`/`apparmor.txt`: byte-identical copies of `addon/`'s — checksums verified equal.
  AppArmor's own `install_apparmor()` (confirmed by reading `supervisor/apps/app.py`) rewrites the
  profile's internal name to the installed add-on's own slug at install time via `adjust_profile()`,
  so the literal `profile quotinator {...}` line in the file needs no manual edit per add-on — the
  supervisor handles the rename automatically, and no profile-name collision between the two add-ons
  is possible.
- `translations/en.yaml`, `nl.yaml`, `de.yaml`: copied **unchanged**, byte-identical (checksums
  verified equal) — correcting an assumption made when this step was first written down. These files
  only cover config-option names/descriptions and the network port description (confirmed against
  CLAUDE.md's own documented translation scope), never the add-on's own `name:`/`description:` fields
  from `config.yaml` — and the options themselves are identical between stable and beta. There was
  nothing "(BETA)"-specific for these files to say.
- `DOCS.md`/`README.md`: full, real content (per Background decision — no symlink/shortcut), adapted
  from `addon/`'s copy with the name/slug swapped, a beta-channel framing note in the intro, and a
  note in the "Direct access port" and "Data" sections that the two add-ons use separate host ports
  and separate data directories.
- `CHANGELOG.md`: generated via `scripts/changelog.csx` with `--max-releases 3` (Step 2) — diffed
  identical to `addon/CHANGELOG.md` beyond the generation timestamp.

### 4. Update the beta-tag / final-tag checklist steps

**Status:** ✅ Done

In `docs/workflow/checklist.md`:
- "Beta tag (T1 + T2 gate)": `addon/config.yaml version` → `addon-beta/config.yaml version`, with an
  explicit note that the stable file is untouched by a beta tag; changelog regen line now names all
  three output files.
- "Final tag (T3 gate)": `addon/config.yaml version` stays pointed at `addon/` only (unchanged
  in substance — it was already the stable file), with the mirror-image note that `addon-beta/` is
  untouched by a final tag; changelog regen line updated the same way.
- Also updated two other checklist spots that referenced regenerating changelogs (`Filing a new
  issue`'s "Changelog `unreleased` entry added" item, and "Milestone close"'s changelog line) to name
  all three files — these weren't part of the original beta/final-tag scope but were the same class
  of stale reference once `addon-beta/CHANGELOG.md` existed.

### 5. Update CLAUDE.md and cross-referencing docs

**Status:** ✅ Done

**CLAUDE.md:**
- New "Home Assistant add-on: stable and beta channels" section (before "MCP (v3)") explaining the split.
- "When adding or renaming an HA add-on config option" checklist extended with a step 5 mirroring
  every change into `addon-beta/config.yaml` + `addon-beta/translations/{en,nl,de}.yaml`, plus a new
  paragraph broadening this to `DOCS.md`/`README.md` content edits generally.
- Key Files table: added `addon-beta/config.yaml` and `addon-beta/CHANGELOG.md` rows; relabelled the
  existing `addon/` rows "Stable".
- "Keeping API documentation in sync" checklist: "all three" → "all four", adding `addon-beta/DOCS.md`.
- Project structure tree: `addon-beta/` line added under `addon/`.
- Pre-Push Checklist: changelog regenerate commands now include `--max-releases 3` and the
  `addon-beta/CHANGELOG.md` invocation (both the main checklist block and the "Tagging a release"
  workflow section); "Versions in sync" step notes `addon-beta/config.yaml` is deliberately not part
  of the final-tag trio.

**Other docs found to have the same stale-singular-addon problem, fixed for consistency** (broader
than CLAUDE.md alone, but the identical drift-prevention concern once `addon-beta/` existed):
- `docs/home-assistant.md` — new "Stable and beta channels" section; repository-structure table,
  install instructions, translation-workflow list (3 files → 6), and "Pending" section all updated.
- `docs/ci-cd.md` — "Version files to update before any tag" table clarified: only one of the two
  add-ons' `config.yaml`s is bumped per tag, never both; changelog row names all three outputs.
- `docs/release-verification.md` — T3 "when required" list and the two-stage release table updated.
- `docs/troubleshooting.md`, `docs/workflow/issue-closure.md` — one-line pointer/command updates.
- `docs/workflow/release.md` — changelog commands and "never edit" line updated. **Found but
  deliberately not fixed here:** this file's own Step 6 bumps `addon/config.yaml` to the plain final
  version *before* Step 9 pushes the `-beta`-suffixed git tag, whereas `checklist.md` (the canonical,
  CLAUDE.md-cross-referenced version of this gate) has the version *string itself* carry `-beta`
  during the beta phase as two separate bump-and-commit cycles. Flagged inline in `release.md` as a
  pre-existing inconsistency between the two docs — unrelated to the add-on split, out of scope for
  #166 to resolve.

### 6. Register new files in `Quotinator.slnx`

**Status:** ✅ Done

Added a `/addon-beta/` solution folder (config.yaml, apparmor.txt, README.md, DOCS.md, CHANGELOG.md,
icon.png, logo.png, translations/{en,nl,de}.yaml) alongside the existing `/addon/` one, mirroring its
structure exactly. `dotnet build --configuration Release` (0 warnings/errors) and `dotnet test
--configuration Release --verbosity normal` (531 tests passed, 0 warnings/errors) both confirmed the
`.slnx` change is well-formed and nothing regressed.

### 7. T1 — Visual Studio

**Status:** ✅ Done

Developer ran the app in Visual Studio. Clean startup log: schema up to date (data v2, app v4),
source refresh succeeded for both external sources (NikhilNamal17, vilaboim — 200 responses), stats
matched T2's (799 quotes / 461 sources / etc.), version `1.8.0`, changelog loaded all 3 language
files, UI home page ready. No errors.

### 8. T2 — Docker build + smoke test

**Status:** ✅ Done

`docker build -f docker/Dockerfile -t quotinator:local .` succeeded. Ran the image
(`docker run -d -p 18080:8080 quotinator:local`); startup log showed version `1.8.0`, clean seed
(799 quotes / 461 sources / etc.), no errors. Smoke-tested: `GET /api/v1/health` → `{"status":"healthy"}`;
`GET /api/v1/version` → `1.8.0` with matching database stats; `GET /api/v1/quotes/random?n=1` → 200
with a real quote; `GET /scalar/v1` → 200. Confirms the app image itself is unaffected (the
`addon/`/`addon-beta/` folders are not part of the Docker build context's image layers — they are
consumed by the HA supervisor directly from the git repository, not baked into the image).

### 9. T3 — Live HA supervisor: both add-ons install side by side

**Status:** ⬜ Not started

Deployment-verified per CLAUDE.md's "Deployment-only issues" rule (HA add-on store behaviour).
After a beta tag exists (bumping `addon-beta/config.yaml` per Step 4): confirm the HA Add-on Store,
for a supervisor with Quotinator's repository added, lists **both** "Quotinator" and "Quotinator
(BETA)" as independently installable add-ons; confirm the beta add-on installs, starts, and reaches
the beta-tagged image; confirm the stable add-on is unaffected and still on its last final version.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `addon-beta/` exists as a complete, self-contained add-on definition | Live (review) | Directory listing matches `addon/`'s file set: `config.yaml`, `DOCS.md`, `README.md`, `CHANGELOG.md`, `apparmor.txt`, `icon.png`, `logo.png`, `translations/{en,nl,de}.yaml` |
| 2 | ✅ | Beta add-on uses `slug: quotinator_beta`, `name: Quotinator (BETA)`, same image repo, `stage: stable` | Live (review) | `addon-beta/config.yaml` content |
| 3 | ✅ | Beta-tag checklist step only touches `addon-beta/config.yaml`; final-tag step only touches `addon/config.yaml` | Live (review) | `docs/workflow/checklist.md` diff — 4 lines updated across "Filing a new issue", "Milestone close", "Beta tag", "Final tag" |
| 4 | ✅ | `scripts/changelog.csx --max-releases <N>` caps output to the N most recent releases (both formats), with correct compare links at the truncation boundary | Live | Verified manually against real `changelog.en.json` with `--max-releases 3`, both formats, before any real output file was touched |
| 5 | ✅ | `CHANGELOG.md`, `addon/CHANGELOG.md`, `addon-beta/CHANGELOG.md` all regenerated with `--max-releases 3`; `addon/CHANGELOG.md` and `addon-beta/CHANGELOG.md` are identical | Live | Ran all three `scripts/changelog.csx` invocations; `diff` confirmed the two `ha-addon`-format outputs identical beyond the generation timestamp header line |
| 6 | ✅ | CLAUDE.md documents the two-add-on split, the extended config-option/content checklist, and the permanent `--max-releases 3` regenerate commands | Live (review) | CLAUDE.md diff; also propagated to `docs/home-assistant.md`, `docs/ci-cd.md`, `docs/release-verification.md`, `docs/troubleshooting.md`, `docs/workflow/release.md`, `docs/workflow/issue-closure.md` |
| 7 | ✅ | `Quotinator.slnx` lists every `addon-beta/` file | Live (review) | `Quotinator.slnx` diff — 10 files added under a new `/addon-beta/` folder |
| 8 | ✅ | No regression | Unit test | `dotnet build --configuration Release` — 0 warnings, 0 errors; `dotnet test --configuration Release --verbosity normal` — 531 tests passed, 0 warnings, 0 errors |
| 9 | ✅ | T1 — app starts in Visual Studio without error | Live (T1) | Developer's own VS run — clean startup log, correct version/stats, source refresh succeeded, no errors |
| 10 | ✅ | T2 — Docker build + smoke tests pass | Live (T2) | `docker build` succeeded; container started cleanly (v1.8.0, 799 quotes seeded); `/api/v1/health`, `/api/v1/version`, `/api/v1/quotes/random`, `/scalar/v1` all returned expected output |
| 11 | ❌ | T3 — both add-ons visible and independently installable in a live HA supervisor from one repository | Live (T3) | Manual HA add-on store check after a beta tag is pushed |

---

## Notes

T1, T2, and T3 all required: T3 because this changes HA add-on store/discovery behaviour, which
CLAUDE.md classifies as deployment-verified; T1/T2 per this project's blanket rule to always run both
regardless of whether the change looks HA-add-on-store-specific.

**Status is `Waiting for release` despite row 11 (T3) still being ❌.** All code/doc steps (1–6) are
done, and T1/T2 are both confirmed — there is no more code work left to do right now. T3 can only be
verified after a beta tag exists and the beta add-on is installed in a live HA supervisor, which is a
release-timing gate, not something further implementation work in this session can close (per
`docs/workflow/process.md`'s rule that a T3-only gap never keeps an issue at `In progress`).

**Beta-tag prep done 2026-07-30** (`docs/workflow/checklist.md`'s "Beta tag" checklist items, not a
plan-doc step — recorded here since #166 is currently the only issue in this release):
`Directory.Build.props` and `addon-beta/config.yaml` bumped to `1.8.1-beta` (`AssemblyVersion`/
`FileVersion` pinned to `1.8.1.0` per the established pattern for a suffixed `<Version>`); a
`1.8.1-beta` release entry (not `unreleased` — added directly, since this issue reached `Waiting for
release` after the entry-writing point) added to `changelog.en.json` with `"issues": [166]`, plus
lockstep `nl.json`/`de.json` translations; all three markdown changelogs regenerated and diffed
identical where expected. Build and `ChangelogSchemaTests` both green. **Not yet done:** pushing the
beta tag itself — that requires explicit developer permission per this project's standing rule, and
hasn't been requested yet.
