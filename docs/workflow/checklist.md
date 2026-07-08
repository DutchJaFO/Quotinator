# Milestone Checklist Template

Use this as a starting checklist when kicking off a milestone. The process detail is in `process.md`.

---

## Milestone start

- [ ] Fetch all issues: `gh issue list --milestone "<Name>" --state all --limit 50 --json number,title,state`
- [ ] Read each issue spec: `gh issue view <N>` for **every** issue — do not skip any
- [ ] Map dependencies between issues
- [ ] Decide on an order of operations
- [ ] Create `docs/milestones/{slug}/overview.md` using the **Overview template** below
- [ ] Create per-issue plan docs for all issues (defer only if the issue is far in the dependency chain)
- [ ] Commit the milestone folder to `main`
- [ ] Create the feature branch: `git checkout -b feature/{slug}`

---

## Overview template

Every `overview.md` must contain these sections, in this order. `docs/milestones/v1-7-0/overview.md` is the canonical example — copy its structure for new milestones rather than inventing a new layout.

Per `process.md → Where information lives`: this document carries **status only**. Every requirement, date, tier-verification detail, and "what was found/changed" narrative belongs in the issue's own plan doc — `overview.md` never restates it.

1. **Header** — milestone name, GitHub milestone link, branch name, status: exactly one of `Planning` / `In progress` / `Waiting for release` / `Released` (see `process.md → Where information lives`), nothing added
2. **Description** — one paragraph on what the milestone delivers and why
3. **Verification tier definitions** — the T1/T2/T3 summary table (copy from `docs/release-verification.md`) plus the 3-step close rule: (1) included in a published release, (2) every required tier confirmed green, (3) explicit user confirmation before `gh issue close`
4. **Issue List** — one master table covering every issue in the milestone, columns: `#` (issue link), `Title`, `Status` (same fixed vocabulary as the header: `Planning` / `In progress` / `Waiting for release` / `Released` — nothing added), `Tiers` (inline per-issue, e.g. `T1 ✅ T2 ✅` or `T3 ⬜` — blank/`—` only after the issue has actually been assessed against `docs/release-verification.md`'s criteria, never guessed), `Plan doc` (link). This table, plus each linked plan doc, is the entire source of truth for "what's done and what's pending" — no separate detail section follows it.
5. **Dependency map** — the `#X → requires #Y; unblocks #Z` text block
6. **Order of operations** — numbered table; always plain sequential integers (`1, 2, 3, ...`) — never lettered sub-steps (`7a`, `7b`). Inserting an issue between two existing rows means renumbering everything below it, not appending a letter. Update whenever a completed issue changes the correct remaining sequence
7. **PR merge plan** — required by `process.md`, see below

A short flat per-issue index list of plan-doc links at the end is optional but encouraged.

---

## Session start

- [ ] Check remote branches before assuming the local view is complete — another session (e.g. a cloud session) may have created branches you do not have locally:
  ```
  git fetch --all
  git branch -r | grep feature/
  ```
  Never create a new branch until you have confirmed no matching branch already exists on the remote.
- [ ] Check for new issues: `gh issue list --milestone "<Name>" --state open --json number,title`
- [ ] Update `overview.md` and create plan docs for any issues added since last session
- [ ] For any new issue without a plan doc: confirm the no-plan-doc decision is logged in the GitHub issue and in `overview.md`
- [ ] Review plan docs for issues being worked on today

---

## Filing a new issue

- [ ] Assign a milestone before saving — an issue with no milestone is invisible to planning
- [ ] No feature milestone fits? Assign to the **current maintenance milestone** (v1.8.0 while it is open) — see `process.md → Maintenance milestone` for rules on when it gets replaced
- [ ] Ask the user which milestone if unsure — never assume

---

## Before closing an issue

- [ ] Verify all blocking/related issues in the dependency map are fully closed first
- [ ] Re-read the **full** issue spec: `gh issue view <N>`
- [ ] **Plan doc check** — either a plan doc exists, OR the GitHub issue and `overview.md` both contain an explicit note explaining why one was not needed (e.g. "pure content fix, no implementation decisions required"). A missing plan doc with no logged reason is never acceptable.
- [ ] If any requirement from the spec was deferred to a later issue: confirm a comment exists on the GitHub issue documenting what was deferred, why, and which issue owns it — a silent drop is never acceptable
- [ ] Confirm the plan doc spec and the GitHub issue spec are in agreement — either the scope is unchanged, or the plan doc has a **Scope changes** section and the issue has a matching comment
- [ ] Confirm the verification table covers every in-scope requirement. Each row must name either: the exact unit test (class + method), an exact live command and expected output, or — for documentation/content issues — the exact document/UI location the user must confirm. Status must be its own column between # and Requirement, never embedded in the Verification column.
- [ ] For bug fixes: confirm a failing test or reproducible steps existed before the fix was written — the bug must have been demonstrably red before turning green
- [ ] All unit tests named in the table pass (green)
- [ ] All live commands have been run and produced the expected output (green)
- [ ] **User manual test** — user starts the app in Visual Studio and confirms it starts without error. For documentation/content issues: user reads or views every item listed in the verification table and confirms each one explicitly.
- [ ] No requirement is still unconfirmed — if anything is unverified, the issue stays open
- [ ] **Changelog updated** — add the issue number to `unreleased.issues` in `changelog.en.json`; add at least one entry to `added`, `changed`, or `fixed`; add a `highlights` entry if the change is user-facing; update `nl.json` and `de.json` lockstep; regenerate `CHANGELOG.md` and `addon/CHANGELOG.md` via `scripts/changelog.csx`. This is part of closing — not a separate PR.
- [ ] **PR merged to `main`** — do not run `gh issue close` while still on the feature branch; the issue stays open until the merge lands
- [ ] Confirm all changes are merged to `main` and included in a tagged release
- [ ] **Release issue-list** — every release entry whose work traces back to this issue must carry the issue number in its `issues[]` array, including hotfix releases. If a release is already tagged, add the number to the matching entry in `changelog.en.json` (+ `nl.json`, `de.json` lockstep) and regenerate.
- [ ] Update the plan doc status to `Complete` (or note "no plan doc — by decision" if none exists)
- [ ] Update the status column in `overview.md`
- [ ] Re-verify the order of operations table — update if this issue's completion changes the correct sequence
- [ ] **User confirms closure** — show the user the closing comment and verification table and wait for explicit approval before running `gh issue close`
- [ ] Close: `gh issue close <N> --comment "<verification table>"`

---

## Before any merge to main

CI enforces build and test pass. The additional check before opening a PR:

- [ ] Every completed issue on the branch is either self-contained, or its incomplete dependencies leave it inert (new infrastructure that nothing currently calls)
- [ ] No in-progress issue leaves a partially-wired feature reachable under normal use (half-registered services, failing endpoints, broken migrations)
- [ ] If a dependency gap exists, confirm a workaround covers the gap until the remaining issues land (e.g. re-seed for data gaps)
- [ ] **Never use `--delete-branch`** — branch deletion is the developer's decision only, never the AI assistant's
- [ ] After merge: close only the fully verified issues; leave in-progress issues open

---

## Milestone close

Releases follow a two-stage model. See `docs/release-verification.md` for tier definitions (T1/T2/T3) and the full stage table.

- [ ] All issues verified: `gh issue list --milestone "<Name>" --state open` returns empty
- [ ] If the milestone added any migrations: full incremental migration path verified against a
      database matching the *last published release's* schema, not the accumulated dev database —
      see [ADR 009](../architecture-decisions/009-verify-migrations-against-last-released-schema.md)
- [ ] Build clean: `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] Tests pass: `dotnet test --configuration Release` — all tests pass, 0 warnings
- [ ] Changelogs updated (`CHANGELOG.md` and `addon/CHANGELOG.md`)
- [ ] Final PR merged to `main` (without `--delete-branch` — developer deletes the branch manually if desired)

### Beta tag (T1 + T2 gate)

A beta tag is mandatory for every release. The release workflow enforces this — pushing a final tag without a prior beta tag for the same version fails the workflow immediately.

- [ ] T1 verified: app starts in VS without error; affected Razor pages render correctly
- [ ] T2 verified: `docker build -f docker/Dockerfile -t quotinator:local .` succeeds; smoke-test commands return expected output
- [ ] `addon/config.yaml version` set to beta version (e.g. `1.7.0-beta`)
- [ ] `Directory.Build.props <Version>` set to beta version
- [ ] Changelog beta entry in `changelog.en.json` (+ `nl.json`, `de.json` lockstep); `CHANGELOG.md` and `addon/CHANGELOG.md` regenerated
- [ ] Push beta tag: `git tag vX.Y.Z-beta && git push origin vX.Y.Z-beta`
- [ ] Confirm GitHub Actions release workflow completes; pre-release created on GitHub with correct Docker image

### Final tag (T3 gate)

Push the final tag after T3 is verified in the live HA add-on.

- [ ] T3 verified: beta add-on installed in HA; all T3-classified requirements confirmed in the live supervisor
- [ ] `addon/config.yaml version` bumped to final version (e.g. `1.7.0`)
- [ ] `Directory.Build.props <Version>` bumped to final version (remove prerelease suffix; also remove the pinned `AssemblyVersion`/`FileVersion` lines — they are only needed when `<Version>` carries a suffix)
- [ ] Changelog final entry promoted from beta; `CHANGELOG.md` and `addon/CHANGELOG.md` regenerated
- [ ] **Version bump PR merged to `main` before tagging** — confirm the above three changes are on `main`, not just a local commit. Tags are immutable once pushed; pushing a tag against un-merged version files burns a patch version.
- [ ] Push final tag: `git tag vX.Y.Z && git push origin vX.Y.Z`
- [ ] Confirm GitHub Actions release workflow completes; full release created on GitHub; `latest` Docker tag updated
- [ ] Milestone closed on GitHub: `gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed`
