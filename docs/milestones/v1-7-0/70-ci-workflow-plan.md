# Issue #70 — Refactor CI/Release workflows to use a shared reusable workflow

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `feature/v1-7-0`

---

## Problem

The `Build & Test` job (restore, build, test, publish smoke test) is duplicated verbatim between `.github/workflows/ci.yml` and `.github/workflows/release.yml`. Any change to the build or test steps must be made in both files.

Beyond the duplication, the release process has no formal model for the gap between "Docker image builds and tests pass" and "the HA add-on works correctly in a live supervisor". That gap is only caught by installing the add-on — which requires a pushed image — so we need a structured beta/final stage to bridge it.

## Why not `workflow_run`

`workflow_run` does not support tag filtering, and the CI workflow does not fire on tag pushes (`branches: [main]` filter), so chaining CI → Release via `workflow_run` is not possible.

---

## Scope

This issue covers two tightly coupled changes:

1. **Reusable workflow** — extract shared build/test steps into `_build-test.yml`; `ci.yml` and `release.yml` call it.
2. **Beta/final release process** — formalise two-stage releases (beta tag for HA testing, final tag after HA verified); update `release.yml` to handle both; document the verification tier model; update `checklist.md`.

---

## Part 1 — Reusable workflow

### Design

Extract the five shared steps into `.github/workflows/_build-test.yml` with a `workflow_call` trigger.

**Input:**

| Name | Type | Default | Purpose |
|---|---|---|---|
| `test-filter` | string | `TestCategory!=Live` | Passed to `--filter` on `dotnet test` |

**Steps (always in this order within the reusable job):**

1. Checkout
2. Setup .NET 10.x
3. `dotnet restore`
4. `dotnet build --no-restore --configuration Release`
5. `dotnet test --no-build --configuration Release --verbosity normal --filter "${{ inputs.test-filter }}"`
6. Publish smoke test (`dotnet publish --no-restore` + assert `data/sources/` non-empty)

**`needs:` reference** — GitHub Actions `needs:` references the **caller's job name**, not anything inside the reusable workflow. After refactor, `release.yml`'s Docker job must reference whatever job name calls the reusable workflow:

```yaml
jobs:
  build-and-test:                            # caller job name
    uses: ./.github/workflows/_build-test.yml
    with:
      test-filter: 'TestCategory!=Live'

  build-and-push:
    needs: [build-and-test]                  # references caller job name
```

**`--no-restore` rationale** — restore is always the first step in the reusable workflow, so `--no-restore` on build/publish and `--no-build` on test are always correct. No input flag needed; the sequence is fixed.

### Files changed (Part 1)

| File | Change |
|---|---|
| `.github/workflows/_build-test.yml` | New — `workflow_call` trigger; one optional `test-filter` input; all five shared steps with normalised step names |
| `.github/workflows/ci.yml` | Replace duplicated steps with `uses: ./.github/workflows/_build-test.yml` |
| `.github/workflows/release.yml` | Replace duplicated steps with `uses: ./.github/workflows/_build-test.yml`; rename caller job to `build-and-test`; update `needs:` on Docker job |

---

## Part 2 — Beta/final release process

### Verification tiers

Three tiers of testing exist. Each catches a different class of problem:

| Tier | Environment | What it catches |
|---|---|---|
| **T1 — VS/local** | Visual Studio on Windows | Razor runtime errors (not caught by `dotnet build`), Blazor circuit startup, UI rendering |
| **T2 — Docker** | `docker build` + `docker run` locally | Publish output completeness, container startup, Kestrel port binding, data/sources presence in image |
| **T3 — HA add-on** | Live Home Assistant supervisor | Ingress routing, `X-Ingress-Path` middleware, supervisor volume mount at `/data`, DataProtection keys, SSL cert loading, cookie behaviour after container restart |

**Rule for plan docs:** every issue plan doc must list which tiers are required for that issue. When closing, each required tier must be confirmed before the issue can close.

### Two-stage release model

The HA supervisor pulls the add-on image using the tag derived from `addon/config.yaml → version`. A beta tag allows pushing an image to GHCR and installing it as an HA add-on before promoting to a final release.

| Stage | Git tag example | `addon/config.yaml version` | Docker tags pushed | GitHub Release |
|---|---|---|---|---|
| Beta | `v1.7.0-beta` | `1.7.0-beta` | `1.7.0-beta` (no `latest`) | Pre-release (`--prerelease`) |
| Final | `v1.7.0` | `1.7.0` | `1.7.0`, `1.7`, `1`, `latest` | Full release |

The existing `enable=${{ !contains(github.ref, '-') }}` expression in `release.yml` already suppresses `latest` for pre-release tags. We extend the same condition to control `--prerelease` on the GitHub Release step.

**The only case where a final tag is pushed without a prior beta** is an issue that can only be verified in the HA add-on (T3-only) — i.e. no T1/T2 changes — where we know Docker and VS behaviour are unaffected. This is the exception, not the default.

### Beta → final promotion checklist (addition to `checklist.md`)

The milestone-close section of `checklist.md` gains two explicit gates:

**Before pushing a beta tag:**
- [ ] T1 verified: app starts in VS without error; Razor pages render correctly
- [ ] T2 verified: `docker build` succeeds; smoke-test commands return expected output
- [ ] `addon/config.yaml version` set to beta version (e.g. `1.7.0-beta`)
- [ ] `Directory.Build.props <Version>` set to beta version
- [ ] Changelog beta entry in `changelog.en.json` (+ nl/de lockstep); markdown regenerated
- [ ] Push beta tag: `git tag v1.7.0-beta && git push origin v1.7.0-beta`
- [ ] Confirm GitHub Actions release workflow completes; pre-release created on GitHub

**Before pushing a final tag (after HA add-on installed and verified):**
- [ ] T3 verified: HA add-on installed from beta image; all T3-classified requirements confirmed in the live add-on
- [ ] `addon/config.yaml version` bumped to final version
- [ ] `Directory.Build.props <Version>` bumped to final version
- [ ] Changelog final entry promoted from beta entry; markdown regenerated
- [ ] Push final tag: `git tag v1.7.0 && git push origin v1.7.0`
- [ ] Confirm GitHub Actions release workflow completes; full release created on GitHub

### Files changed (Part 2)

| File | Change |
|---|---|
| `.github/workflows/release.yml` | Add `--prerelease` flag conditional on tag containing `-`; add `enforce-beta-first` job that fails final tags with no prior beta tag |
| `docs/release-verification.md` | New — tier definitions, classification rules, how to declare tiers in plan docs |
| `docs/workflow/checklist.md` | Add beta/final gates to milestone-close section (as above) |
| `tests/Quotinator.Data.Tests/Helpers/DapperSetupTests.cs` | Fix CS1574: cref `MSTestSettings.Initialize` → `AssemblySetup.Initialize` |
| `tests/Quotinator.Data.Tests/MSTestSettings.cs` | Fix MSTEST0001: add `[assembly: Parallelize]` at method level |
| `CLAUDE.md` | Extend pre-push checklist: 0-warnings policy applies to `dotnet test` too |

---

## How to audit and re-verify

This issue covers GitHub Actions YAML and process documentation — neither can be unit tested in the conventional sense. The audit trail is a combination of PR diffs, Actions run links, and live release observations. Future sessions can use these methods to confirm the implementation is still correct after any refactor.

### Auditing the reusable workflow (rows 1–4)

```bash
# Confirm _build-test.yml exists and has the workflow_call trigger
grep "workflow_call" .github/workflows/_build-test.yml

# Confirm ci.yml calls it and has no duplicated steps
grep "uses:" .github/workflows/ci.yml
grep -c "dotnet build" .github/workflows/ci.yml   # expect 0

# Confirm release.yml calls it and needs: references the caller job
grep "uses:" .github/workflows/release.yml
grep "needs:" .github/workflows/release.yml

# Confirm the required status check name matches what GitHub reports
# (run after any CI run and check the check-run name via API)
gh api repos/DutchJaFO/Quotinator/commits/HEAD/check-runs --jq '.check_runs[] | .name'
```

### Auditing the release process controls (rows 5, 10–11)

```bash
# Confirm --prerelease is conditional on the tag containing '-'
grep "prerelease" .github/workflows/release.yml

# Confirm enforce-beta-first job exists and checks for prior beta tag
grep "enforce-beta-first" .github/workflows/release.yml
grep "v\${VERSION_BASE}-\*" .github/workflows/release.yml
```

**To test row 10 (failure case):** push a final tag `vX.Y.Z` for a version that has no `vX.Y.Z-*` tag in the repo. The `enforce-beta-first` job must fail and the Docker build must not run. Verify in the Actions tab — `build-and-test` and `build-and-push` should be skipped/not reached.

**To test row 11 (success case):** the normal beta → final release cycle. Push `vX.Y.Z-beta` first, confirm it creates a pre-release. Then push `vX.Y.Z` — `enforce-beta-first` should pass, Docker image should be built and pushed with `latest`, and a full GitHub Release should be created.

### Auditing the branch protection required check (lesson learned)

After any workflow refactor that renames a job, verify the required status check name still matches:

```bash
# Get the current required check name from the ruleset
gh api repos/DutchJaFO/Quotinator/rulesets/17924200 \
  --jq '.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context'

# Get the actual check name reported by the last CI run
gh api repos/DutchJaFO/Quotinator/commits/main/check-runs \
  --jq '.check_runs[] | select(.app.slug=="github-actions") | .name'
```

If these do not match, the PR will show "Build & Test — Expected — Waiting for status to be reported" and block merges. Update the ruleset via:

```bash
gh api repos/DutchJaFO/Quotinator/rulesets/17924200 -X PUT --input ruleset.json
```

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `_build-test.yml` exists with `workflow_call` trigger and `test-filter` input | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — file created; trigger is `workflow_call`; input `test-filter` with default `TestCategory!=Live` |
| 2 | ✅ | `_build-test.yml` contains all six shared steps with normalised names | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — Checkout, Setup .NET, Restore, Build, Test, Publish smoke test all present |
| 3 | ✅ | `ci.yml` calls shared workflow; no duplicated steps remain | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — `uses: ./.github/workflows/_build-test.yml` present; old inline steps absent |
| 4 | ✅ | `release.yml` calls shared workflow; Docker job `needs:` references caller job name | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — `uses:` present; `needs: [enforce-beta-first, build-and-test]` correct |
| 5 | ✅ | `release.yml` creates a pre-release GitHub Release for beta tags | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — `--prerelease` flag gated on `contains(github.ref, '-')` |
| 6 | ✅ | `docs/release-verification.md` exists and defines all three tiers | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — file created; T1/T2/T3 defined with what each catches and when required |
| 7 | ✅ | `checklist.md` milestone-close section includes mandatory beta and final gates | Code review | PR [#123](https://github.com/DutchJaFO/Quotinator/pull/123) diff — both gate blocks present; beta described as mandatory with no waiver |
| 8 | ✅ | CI passes on push to feature branch via reusable workflow | Live | [Actions run 28285604901](https://github.com/DutchJaFO/Quotinator/actions/runs/28285604901) — `Build & Test / Build & Test` succeeded in 59s via `_build-test.yml` |
| 9 | ✅ | User manual test — app starts without error (T1) | Live | Confirmed by user 2026-06-27 — app started in Visual Studio without error; Razor pages rendered correctly |
| 10 | ⬜ | Final tag without beta tag fails the release workflow | Live | Push a final tag `vX.Y.Z` with no prior `vX.Y.Z-*` tag; confirm `enforce-beta-first` fails in Actions; `build-and-test` and `build-and-push` must not run |
| 11 | ⬜ | Final tag with beta tag passes the release workflow | Live | Normal beta → final cycle for next milestone release; confirm `enforce-beta-first` passes; Docker image pushed with `latest`; full GitHub Release created |
