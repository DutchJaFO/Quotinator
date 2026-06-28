# Release Procedure

This document covers how to cut a release once all changes destined for it are ready. It applies to both **patch releases** (a bug fix outside a milestone) and **milestone releases** (the final step of a milestone). Where the two differ, the difference is called out.

The pre-push checklist in `CLAUDE.md` is the mechanical gate — this document is the decision layer that precedes it.

---

## When to cut a release

Cut a release when:

- A bug fix is ready to ship (patch release)
- All issues in a milestone are closed and the milestone close checklist in `checklist.md` is complete (milestone release)

Do **not** cut a release mid-milestone unless the fix is urgent and can ship independently.

---

## Rule: every bug fix must have a GitHub issue

Before writing any code for a bug fix, there must be a GitHub issue tracking it. If one does not exist, create it first:

```bash
gh issue create --title "Bug: <short description>" --body "<steps to reproduce, error output, root cause if known>" --label bug
```

The issue number is referenced in the PR, the closing comment, and the verification table. A fix without an issue has no audit trail.

---

## Patch release procedure

### Step 1 — Ensure the fix is on a branch and tests are green

All changes go through a PR — never push directly to `main`. The fix branch should already exist. Confirm it builds and tests pass before proceeding:

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
```

Both must report `0 Warning(s)  0 Error(s)`.

### Step 2 — Update the `unreleased` section of `changelog.en.json`

`src/Quotinator.Api/resources/changelog.en.json` is the source of truth. **Never edit `CHANGELOG.md` or `addon/CHANGELOG.md` directly.**

**Before writing any entries, read `schemas/changelog.schema.json`** — it is the authoritative definition of every field, its type, and which fields are required. Do not infer the format from prior entries in the file or from git history; the schema may have changed since those were written.

Add or update the `unreleased` section to reflect all changes on the branch that are not yet in a release entry. This keeps the changelog current before the promotion step.

Rules for `highlights`:
- User-facing fixes always get a `highlights` entry in plain English — no API paths, class names, or CVE IDs as the sole description
- Security fixes always appear in `highlights` with the CVE ID so users can verify
- For purely internal fixes: `["Internal improvements — no user-facing changes."]`

Regenerate the markdown changelogs after every edit to `changelog.en.json`:

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
```

Verify structure: `dotnet test --filter ChangelogSchema`

### Step 3 — Run the open issue audit

Before checking Dependabot PRs, run the open issue audit (`docs/workflow/issue-audit.md`):

```bash
gh issue list --state open --limit 100 --json number,title,labels,milestone
```

Classify each open issue (legitimately open, deployment-pending, done-not-closed, or stale). Close any that are done but were never formally closed, following the closure criteria in `checklist.md`. This ensures the release does not ship while completed work sits unclosed.

### Step 4 — Check for Dependabot PRs

```bash
gh pr list --state open
```

Review any open Dependabot PRs. Merge all that are green before promoting the release — this ensures the changelog and release entry are complete, and avoids shipping a version that is immediately outdated. After merging, pull locally:

```bash
git pull
```

Add a dependency bump entry to the `unreleased` section if any Dependabot PRs were merged, then regenerate the changelogs again.

### Step 5 — Check for issues and CVEs to include

```bash
gh issue list --state open --label bug
```

Review open bug issues and any active CVE tracking docs in `src/[project]/CVE/`. Confirm whether any should be included in this release. If yes, address them on the branch and update `unreleased` accordingly. Regenerate changelogs if anything was added.

### Step 6 — Promote `unreleased` to a release entry and bump the version

Once `unreleased` is complete and all Dependabot and issue checks are done:

1. Move entries from `unreleased` into a new release entry at the top of `releases`
2. Set `version` and `date` on the new entry
3. Clear (or remove) the `unreleased` section

Three places must match the new version (without `v` prefix):

| File | Field |
|---|---|
| `Directory.Build.props` | `<Version>` — **the only file to edit for the version number** |
| `addon/config.yaml` | `version` |
| `changelog.en.json` | version entry added above |

`AssemblyVersion` and `FileVersion` are derived automatically — do not set them manually.

Increment following semver:
- Bug fix with no API or schema change → **patch** (e.g. `1.6.1` → `1.6.2`)
- New feature or non-breaking addition → **minor**
- Breaking change → **major**

Regenerate the changelogs one final time after promoting the release entry:

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
```

### Step 7 — Run the pre-push checklist and T1/T2 verification

```bash
dotnet build --configuration Release          # 0 warnings, 0 errors
dotnet test  --configuration Release --verbosity normal   # all pass
docker build -f docker/Dockerfile -t quotinator:local .  # must succeed
```

Smoke-test the local image (**T2 gate — mandatory whenever any code was changed**). The only reason to skip this is a release that touches only content files (changelog, documentation) with zero code changes.

```bash
docker run --rm -p 8080:8080 quotinator:local
curl -s http://localhost:8080/api/v1/health
curl -s http://localhost:8080/api/v1/version   # must return the new version
curl -s http://localhost:8080/api/v1/quotes/random
```

Also confirm **T1** — start the app in Visual Studio and verify it starts without error and affected pages render correctly, for any change that touches `.razor` files, Blazor services, or middleware.

See `docs/release-verification.md` for the full tier definitions and what each tier catches.

### Step 8 — Open a PR and merge to `main`

Push the branch and open a PR:

```bash
git push -u origin <branch-name>
gh pr create --title "Release vX.Y.Z" --body "<summary of changes>"
```

Do **not** use `Fixes #N`, `Closes #N`, or any GitHub auto-close keyword in the PR body — issues are closed explicitly after the release tag is confirmed.

Merge the PR once CI is green. Do **not** use `--delete-branch` — branch deletion is the developer's decision only.

### Step 9 — Push the beta tag

The release workflow enforces that a final tag cannot be pushed without a prior beta tag for the same version. This applies to every release — patch and milestone alike.

After the PR is merged and `main` is up to date locally:

```bash
git pull
git tag vX.Y.Z-beta
git push origin vX.Y.Z-beta
```

Confirm the GitHub Actions release workflow completes and a pre-release is created on GitHub with the correct Docker image.

> **Environment note:** Claude Code Desktop can push tags directly. Claude Code cloud and mobile environments receive a 403 on tag pushes — if running there, push the tag from a local terminal.

### Step 10 — Tag the final release (after T3 if required)

For issues that require **T3 verification** (HA add-on behaviour — see `docs/release-verification.md`): install the beta add-on in HA and confirm the T3-classified requirements before pushing the final tag.

For issues that do **not** require T3: the final tag may be pushed once the beta release workflow completes successfully and any T1/T2 verification is confirmed (Step 7).

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

Confirm the GitHub Actions release workflow completes, the full release is created on GitHub, and the `latest` Docker tag is updated.

### Step 11 — Confirm the fix in the release build, then close the issue

An issue is only closed when the fix is confirmed working in the actual release artefact — not just because the tag was pushed and CI passed. What "confirmed" means depends on what the issue touches:

| Issue type | Confirmation required before closing |
|---|---|
| API / logic bug | Smoke-test against the local Docker image (Step 7) — confirm the specific failure no longer occurs |
| Docker / container behaviour | Local Docker build and smoke-test (Step 7) |
| HA add-on behaviour (ingress, supervisor config, add-on panel, container restart) | Install the new release in the HA add-on and verify the behaviour there. These issues **cannot** be confirmed from a local Docker run alone. |
| Documentation / content only | User reads the updated content and confirms it is correct |

Once confirmed, close the issue with the verification table:

```bash
gh issue close <N> --comment "<verification table>"
```

The closing comment must include the 5-column verification table from `process.md`. See issue #61 for the canonical example.

Issues requiring HA add-on confirmation are tracked in the post-deploy verification checklist in memory (`project_post_deploy_verification.md`) until the developer confirms them in the live add-on.

---

## Milestone release procedure

A milestone release follows Steps 2–11 above, with one difference:

- The issue audit (Step 3) and Dependabot check (Step 4) are done at the start of the milestone's final release session, not inline — see the "Tagging a release" section in `CLAUDE.md` for the full sequence.
- The milestone close checklist in `checklist.md` contains the full beta/final gate sequence, including which T3 items to verify before the final tag.
- After Step 11, close the milestone on GitHub:
  ```bash
  gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed
  ```

---

## What not to do

- Do not fix a bug without a GitHub issue — create the issue first
- Do not push directly to `main` — all changes go through a PR
- Do not edit `CHANGELOG.md` or `addon/CHANGELOG.md` directly — they are generated files; always regenerate via `changelog.csx`
- Do not forget to regenerate after every edit to `changelog.en.json` — the markdown files must be committed alongside the JSON
- Do not use `Fixes #N` or `Closes #N` in commit messages or PR bodies — these trigger GitHub auto-close and bypass the verification comment requirement
- Do not bump `AssemblyVersion` or `FileVersion` manually — they are derived from `<Version>` in `Directory.Build.props`
- Do not push a tag before the PR is merged and `main` is up to date
- Do not push a final tag without a prior beta tag — the release workflow will fail; this applies to every release, patch and milestone alike
- Do not skip the smoke-test when code was changed — it is mandatory, not optional
- Do not close an issue based solely on CI passing or the tag being pushed — confirm the fix in the appropriate artefact (local Docker, or HA add-on for deployment-only issues)
- Do not close an HA add-on issue from a local Docker run — those require confirmation in the live add-on
