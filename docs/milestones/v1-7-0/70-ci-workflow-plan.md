# Issue #70 — Refactor CI/Release workflows to use a shared reusable workflow

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

The `Build & Test` job (restore, build, test, publish smoke test) is duplicated verbatim between `.github/workflows/ci.yml` and `.github/workflows/release.yml`. Any change to the build or test steps must be made in both files.

## Why not `workflow_run`

`workflow_run` does not support tag filtering, and the CI workflow does not fire on tag pushes (`branches: [main]` filter), so chaining CI → Release via `workflow_run` is not possible.

## Approach

Extract the shared steps into a reusable workflow (`.github/workflows/_build-test.yml`) using the `workflow_call` trigger. Both `ci.yml` and `release.yml` call it via `uses: ./.github/workflows/_build-test.yml`.

This removes the copy-paste while preserving the safety guarantee that the release workflow re-runs tests against the exact tagged commit.

---

## Files to change

| File | Change |
|------|--------|
| `.github/workflows/_build-test.yml` | New — extracted shared build/test steps; `workflow_call` trigger |
| `.github/workflows/ci.yml` | Replace duplicated steps with `uses: ./.github/workflows/_build-test.yml` |
| `.github/workflows/release.yml` | Replace duplicated steps with `uses: ./.github/workflows/_build-test.yml` |

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Shared steps extracted to `_build-test.yml` with `workflow_call` trigger | Code review | `_build-test.yml` exists; trigger is `workflow_call`; contains restore/build/test/smoke steps |
| 2 | ⬜ | `ci.yml` calls shared workflow | Code review | `ci.yml` contains `uses: ./.github/workflows/_build-test.yml` |
| 3 | ⬜ | `release.yml` calls shared workflow | Code review | `release.yml` contains `uses: ./.github/workflows/_build-test.yml` |
| 4 | ⬜ | CI passes on push to feature branch | Live | GitHub Actions — CI workflow green on branch push |
| 5 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
