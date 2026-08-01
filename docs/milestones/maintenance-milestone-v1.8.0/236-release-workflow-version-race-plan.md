# #236 — Release workflow: HA can see a config.yaml version bump before the matching Docker image is pushed

**Status:** Waiting for release
**GitHub issue:** #236
**Tiers required:** N/A (process/documentation change only — no application code)
**Depends on:** Nothing

---

## Background

`.github/workflows/release.yml` triggers only on a git tag push (`on: push: tags: 'v*.*.*'`), then runs
`enforce-beta-first` → `build-and-test` → `build-and-push` in sequence — the last job builds the
multi-arch Docker image, pushes it to GHCR, verifies it's pullable on both platforms, then creates the
GitHub Release. Confirmed against recent real runs (`gh run list --workflow=release.yml`): this
consistently takes **~13–16 minutes** end to end (e.g. the v1.8.1-beta run: 19:49:16 → 20:04:52 UTC;
the v1.8.0 final run: 16:59:09 → 17:14:31 UTC).

Per `docs/workflow/checklist.md`'s Beta tag / Final tag sections and `CLAUDE.md`'s "Tagging a release"
workflow, the documented process bumps `addon(-beta)/config.yaml`'s version, merges that PR to `main`,
*then* pushes the tag. Home Assistant's supervisor reads the add-on store's `config.yaml` directly from
`main` via git, independent of GitHub Actions — so any supervisor that refreshes its store metadata
during that ~13–16 minute window sees a version with no matching image on GHCR yet, and an
install/update attempt fails outright (confirmed live during #166's T3 verification, 2026-07-30).

**Why the current order exists (not an oversight):** `checklist.md`'s Final tag section states it
explicitly — "tags are immutable once pushed; pushing a tag against un-merged version files burns a
patch version." Merging first guarantees the tag always points at exactly the reviewed, CI-passed code.
Any fix has to preserve that guarantee for everything that's actually *baked into the tagged build* —
it can only defer the parts of the version bump that are pure HA-facing metadata, never consumed by the
Docker image or the release workflow itself.

**Does this predate #166?** Yes. `git log -- addon/config.yaml` turns up `18e0c0f "Fix HA add-on
version to match published Docker image tag"` (2026-06-14, `v1.0.2`) — the exact same class of bug
(`config.yaml` declaring a version with no matching image on GHCR) hit this project once before,
before the beta/final split existed at all. #166 didn't create this race or change its size — the
release workflow behaves identically regardless of whether one or two `config.yaml` files exist; #166
only made it easier to *notice* (T3 verification of a brand-new beta channel is exactly when someone is
most likely to attempt an install during the window).

---

## What's baked into the tag vs. what isn't

The fix hinges on a distinction the issue itself didn't need to draw out, but the plan does:

| File | Consumed by | Must be merged *before* the tag? |
|---|---|---|
| Application source, `Directory.Build.props` `<Version>` | The Docker image itself (`AssemblyVersion`/`FileVersion`, `/api/v1/version`) | **Yes** — baked into the build the tag triggers |
| `changelog.en/nl/de.json`, root `CHANGELOG.md` | `release.yml`'s "Extract release notes from CHANGELOG.md" step reads root `CHANGELOG.md` directly | **Yes** — the release workflow itself depends on it |
| `addon/config.yaml` / `addon-beta/config.yaml` `version` | **HA supervisor only**, reading `main` directly — never read by the Docker image or the release workflow | **No** |
| `addon/CHANGELOG.md` / `addon-beta/CHANGELOG.md` | HA add-on's own changelog display only — not read by `release.yml` (only root `CHANGELOG.md` is) | **No** |

Only the two HA-facing-only rows can safely move to a follow-up PR merged *after* the tagged workflow
run is confirmed green — at which point `release.yml`'s own "Verify image is pullable (amd64 + arm64)"
step has already proven the image exists for both platforms. Everything the release workflow itself
needs stays exactly where it is today, so the "tag only ever points at fully-merged, CI-passed code"
guarantee is unaffected for anything that actually matters to what gets built.

---

## Decision

**Split the existing single version-bump PR into two, sequenced around the tag push:**

1. **Before-tag PR (today's process, minus two files):** `Directory.Build.props` version bump,
   `changelog.en/nl/de.json` version-entry promotion, regenerated root `CHANGELOG.md`. Merge to `main`,
   then push the tag — unchanged from today.
2. **Wait for the release workflow to complete green** — `gh run list --workflow=release.yml` (or
   watch it in the Actions UI). Its own "Verify image is pullable" step is the authoritative signal;
   don't merge step 3 before it passes.
3. **After-tag PR (new step):** bump `addon/config.yaml` (final tag) or `addon-beta/config.yaml` (beta
   tag) version to match the just-tagged version, and regenerate `addon/CHANGELOG.md`/
   `addon-beta/CHANGELOG.md` from the already-merged `changelog.en.json` (no content changes — just
   re-running the generator and committing the two addon-specific outputs). Merge to `main`.

This closes the gap entirely: `main` never advertises a `config.yaml` version to HA until the matching
image is already confirmed pullable on both architectures.

**Rejected alternatives** (per the issue's own options list):
- *Document the gap, advise waiting* — doesn't fix the user-facing install failure, just narrates it.
  A live supervisor refresh during the window still fails regardless of what a doc says.
- *Publish the image before merging the bump* — this is the same idea as the chosen fix, just phrased
  as "image first" instead of "config.yaml last." They converge on the same design once `config.yaml`
  is recognised as decoupled from the tagged build.

---

## 1. Update `CLAUDE.md`'s "Tagging a release" workflow

**Status:** ✅ Done

Rewrite steps 8–11 (`docs/workflow/checklist.md`'s Beta/Final tag sections separately handle the
beta-vs-final distinction already; this section is the generic overview and currently only mentions a
single `addon/config.yaml` bump, which was never updated for #166's two-file split — fix that
imprecision in the same edit) to reflect the two-PR sequence: release-prep PR (code, `Directory.Build.props`,
changelog) → merge → tag → wait for green → follow-up PR (`addon(-beta)/config.yaml`,
`addon(-beta)/CHANGELOG.md`) → merge.

## 2. Update `docs/workflow/checklist.md`'s Beta tag and Final tag sections

**Status:** ✅ Done

Move the `addon(-beta)/config.yaml version` and the corresponding `addon(-beta)/CHANGELOG.md`
regeneration out of the pre-tag checklist items into a new "After the release workflow completes"
sub-section under each of Beta tag and Final tag, explicit that this is a second, separate PR gated on
the workflow run showing green (image verified pullable).

## 3. Correct overview.md's #227 dependency scope

**Status:** ✅ Done

Not part of #236 itself, but found while planning it: `overview.md`'s Dependency map currently claims
#227 blocks *every* not-yet-implemented issue including #232 and #236 — both already proceeded
independently (confirmed no schema/class involvement in either), so that blanket claim is inaccurate.
Narrow it to the issues that actually touch renamed tables/entities/SQL.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `CLAUDE.md`'s "Tagging a release" section describes the two-PR sequence unambiguously, correctly distinguishing beta (`addon-beta/`) vs. final (`addon/`) | Live | Re-read the section after editing; confirmed no step still implies a single combined PR |
| 2 | ✅ | `docs/workflow/checklist.md`'s Beta tag and Final tag sections both have an explicit "after the workflow is green" follow-up step for `config.yaml`/`CHANGELOG.md` | Live | Re-read both sections after editing |
| 3 | ✅ | `overview.md`'s Dependency map no longer claims #227 blocks #232/#236 | Live | Re-read the Dependency map section after editing |
| 4 | ❌ | The new sequence is actually followed on the next real beta or final tag for this milestone | External verification | No unit test possible — this is process, not code. Confirmed only when the next real tag cycle happens: the config.yaml-bump PR must be observably a separate, later merge than the tag push, with the release workflow shown green in between. Record the PR numbers/timestamps in this plan doc's own status once it happens. |

Item 4 cannot be closed out in this planning/implementation session — there is no tag being pushed
right now. It stays ❌ until the milestone's own eventual release goes through this new sequence at
least once.
