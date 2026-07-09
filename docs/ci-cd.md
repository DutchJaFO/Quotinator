# CI/CD

Quotinator uses GitHub Actions for continuous integration and delivery.

---

## Workflows

### Shared build/test — `.github/workflows/_build-test.yml`

A reusable workflow called by both `ci.yml` and `release.yml` via `workflow_call`. Never triggered directly.

**Input:**

| Name | Type | Default | Purpose |
|---|---|---|---|
| `test-filter` | string | `TestCategory!=Live` | Passed to `dotnet test --filter` |

**Steps (always in this order):**

1. Checkout
2. Setup .NET 10.x
3. `dotnet restore`
4. `dotnet build --no-restore --configuration Release` — must produce 0 warnings, 0 errors
5. `dotnet test --no-build --configuration Release --verbosity normal --filter "<input>"` — must produce 0 warnings, 0 errors; all tests pass
6. Publish smoke test — runs `dotnet publish --no-restore` and asserts `data/sources/` is present and non-empty in the output

> CI does **not** build the Docker image. A broken Dockerfile is only caught by the release workflow or a local build. Always do a local `docker build` before tagging — see the Pre-Push Checklist in `CLAUDE.md`.

---

### CI — `.github/workflows/ci.yml`

Triggers on every push to `main` and every pull request targeting `main`.

Calls `_build-test.yml` as a single job named `build-and-test`. No other steps.

---

### Release — `.github/workflows/release.yml`

Triggers when a tag matching `v*.*.*` is pushed.

**Jobs:**

| Job | Runs when | Purpose |
|---|---|---|
| `enforce-beta-first` | Final tags only (no `-` in tag) | Fetches all tags; fails if no `vX.Y.Z-*` tag exists for this version |
| `build-and-test` | Always (after `enforce-beta-first`) | Calls `_build-test.yml` — runs build, test, and smoke test against the exact tagged commit |
| `build-and-push` | Always (after both above) | Builds multi-arch Docker image and pushes to GHCR; creates GitHub Release |

**Image published:** `ghcr.io/dutchjafo/quotinator`

Docker tags produced vary by release channel:

| Tag pushed | Docker tags produced | GitHub Release |
|---|---|---|
| `v1.7.0-beta` | `1.7.0-beta` only (no `latest`) | Pre-release |
| `v1.7.0` | `1.7.0`, `1.7`, `1`, `latest` | Full release |

Pre-release tags (containing `-`) never receive `latest`, `major`, or `major.minor` aliases.

**Prerequisites:**
- The `ghcr.io/dutchjafo/quotinator` package must be set to **Public** in GitHub package settings. The HA Supervisor pulls without credentials — a private package returns 401 and the add-on fails to install.
- A beta tag for the same version must exist before a final tag is pushed. The `enforce-beta-first` job enforces this automatically.

---

## Release process

See `docs/release-verification.md` for the full tier model (T1/T2/T3) and the milestone-close gate checklists in `docs/workflow/checklist.md`.

**Summary:**

1. Verify T1 (VS/local) and T2 (Docker build + smoke test)
2. Bump versions to `X.Y.Z-beta`, push beta tag → CI builds pre-release image
3. Install beta add-on in HA; verify T3 items
4. Bump versions to `X.Y.Z`, promote changelog, push final tag → CI builds full release

### Version files to update before any tag

All three must match the tag (without the `v` prefix):

| File | Field |
|---|---|
| `Directory.Build.props` | `<Version>` — the only file to edit; `AssemblyVersion` and `FileVersion` derive from it automatically |
| `addon/config.yaml` | `version` — HA Supervisor appends this as the Docker image tag when pulling from GHCR |
| `src/Quotinator.Api/resources/changelog.en.json` | new release entry at top of `releases` array (+ `nl.json`, `de.json` lockstep); regenerate `CHANGELOG.md` and `addon/CHANGELOG.md` |

---

## Branch protection

Main branch is protected by the **"Protect main"** ruleset (id `17924200`). Changes must go through a PR; the required status check must pass before merging.

**Current required status check:** `Build & Test / Build & Test`

This name is derived from the CI workflow calling the reusable workflow:
- The reusable workflow job is named `build-and-test` with display name `Build & Test`
- GitHub reports the check as `<caller-job-display-name> / <reusable-job-display-name>` = `Build & Test / Build & Test`
- The workflow name (`CI`) is **not** included in the check name

> **Important:** if a future refactor renames any job in `ci.yml` or `_build-test.yml`, the required check name will change and PRs will show "Build & Test — Expected — Waiting for status to be reported" and block all merges. Always verify after renaming — see Audit section below.

---

## Audit and verification

### Verify the reusable workflow wiring

```bash
# Confirm _build-test.yml has the workflow_call trigger and test-filter input
grep "workflow_call" .github/workflows/_build-test.yml
grep "test-filter" .github/workflows/_build-test.yml

# Confirm ci.yml calls it and has no duplicated dotnet steps
grep "uses:" .github/workflows/ci.yml
grep -c "dotnet build" .github/workflows/ci.yml    # expect 0

# Confirm release.yml calls it and enforce-beta-first is wired up
grep "uses:" .github/workflows/release.yml
grep "enforce-beta-first" .github/workflows/release.yml
grep "needs:" .github/workflows/release.yml
```

### Verify the required status check name matches CI output

Run after any CI workflow run:

```bash
# What the ruleset currently requires
gh api repos/DutchJaFO/Quotinator/rulesets/17924200 \
  --jq '.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context'

# What the last CI run actually reported
gh api repos/DutchJaFO/Quotinator/commits/main/check-runs \
  --jq '.check_runs[] | select(.app.slug=="github-actions") | .name'
```

If these do not match, update the ruleset:

```bash
# Edit the context value in the required_status_checks rule and PUT the full ruleset back
gh api repos/DutchJaFO/Quotinator/rulesets/17924200 -X PUT --input ruleset.json
```

### Verify the beta-before-final enforcement

```bash
# Confirm enforce-beta-first job exists and checks for prior beta tag
grep -A 20 "enforce-beta-first" .github/workflows/release.yml

# Confirm --prerelease is conditional on the tag containing '-'
grep "prerelease" .github/workflows/release.yml
```

**To test the failure case:** push a final tag `vX.Y.Z` for a version with no `vX.Y.Z-*` tag in the repo. The `enforce-beta-first` job must fail; `build-and-test` and `build-and-push` must not run. Verify in the Actions tab.

**To test the success case:** push `vX.Y.Z-beta` first (creates pre-release), then push `vX.Y.Z` (enforce-beta-first passes; full release created with `latest` tag).

### Verify the Docker smoke test

Build and run the image the same way the release workflow does:
```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run --rm -d -p 8080:8080 --name quotinator-test quotinator:local
```
Then run every command in CLAUDE.md's Pre-Push Checklist → step 6 ("Smoke-test the image") against
it. That checklist is the single authoritative, living smoke test suite — it is not duplicated here
so the two never drift apart; update it, not this file, whenever a new scenario needs covering.
```bash
docker stop quotinator-test
```

`/version` returning `1.0.0` instead of the current version means `Directory.Build.props` was missing from the build context.

The `field=author`, `field=character`, and `type=person` searches may return `{"status":"NoResults",...}` with the current dataset — that is expected behaviour, not a bug.

---

## Secrets

No additional secrets are needed. The release workflow uses `GITHUB_TOKEN`, which GitHub provides automatically with `contents: write` and `packages: write` permissions.

---

## Versioning

Follow [Semantic Versioning](https://semver.org/):

| Increment | When |
|---|---|
| `MAJOR` | Breaking API changes |
| `MINOR` | New features, backwards-compatible |
| `PATCH` | Bug fixes, documentation, CI changes |
