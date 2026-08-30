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
- [ ] **Bump `Directory.Build.props`' `<Version>` to this milestone's target version with an `-alpha`
      suffix** (e.g. `1.9.0-alpha`), and commit it **on the feature branch** — see *Version during
      development* below

### Version during development

**A milestone's version is decided when the milestone starts, not when it is released.** Development
carries the target version with an `-alpha` suffix — `1.9.0-alpha` — and release preparation drops the
suffix.

**Why the bump cannot wait for release.** While development shares the released version number, the
running build is indistinguishable from the release it upgrades from, and anything keyed on
application-plus-version silently collapses the two. Found live during #339's full T2 pass: on a
v1.8.3 database, `BackfillAnnouncementProvenance` inserts its own `Quotinator.Api | 1.8.3` provenance
row, and `AppVersionTracker.RecordCurrentAsync` — append-if-new — then finds that row already present
and records nothing for the build that is actually running. `Sql.AppVersion.SelectMostRecent` therefore
answers a version that is not the one running. Re-run with `1.9.0-alpha`, the same scenario appends the
running build correctly.

It also removes manufactured setup from the suite: a test needing the two versions to differ currently
has to edit `Directory.Build.props` and rebuild the image just to express its own assertion.

**The suffix is not cosmetic.** It states in-band that this build is not a release — the concrete
worry that the official release does not exist yet. It is safe with this repository's existing version
derivation: verified 2026-08-25, building with `Version=1.9.0-alpha` produces `AssemblyVersion`
`1.9.0.0` and `FileVersion` `1.9.0.0` (the suffix is stripped for both), while `ProductVersion` carries
`1.9.0-alpha`. `CLAUDE.md`'s "derived automatically as `$(Version).0`" rule is unaffected.

**Commit it on the feature branch, never directly to `main`.** It reaches `main` when the milestone's
own PR merges, exactly like every other change. Two milestone branches running in parallel each carry
their own bump and conflict on a single line at merge, which is a signal worth having rather than a
hazard; branches targeting the same version do not conflict at all.

**Leaving it uncommitted is not an alternative.** The version would then differ per working copy: CI,
the release workflow and every other clone would still build the old number, so a T2 pass run anywhere
else hits the exact collision the bump exists to remove, and the suite passes locally while failing in
CI. The edit would also sit in `git status` for the whole milestone, where any commit can sweep it in
and any checkout can lose it.

**Nothing HA-facing moves early.** `addon/config.yaml`, `addon-beta/config.yaml` and the changelog are
untouched by this step and stay on the release sequencing [#236](https://github.com/DutchJaFO/Quotinator/issues/236)
defines.

**At release preparation, drop the suffix** — see *Milestone close* below, and `CLAUDE.md`'s "Tagging a
release" step 8, where `<Version>` must match the tag exactly.

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

- [ ] **Propose a type label (`bug`/`enhancement`/`research`) in the same draft as the title/body** —
      never file first and ask about the label as a separate follow-up. See `issues.md`'s "Issue types"
      table for which applies. A secondary label (`code style`, `.NET`, etc.) may be proposed alongside
      it when it genuinely fits — see `issues.md`'s labelling section for the adopted secondary set.
- [ ] Assign a milestone before saving — an issue with no milestone is invisible to planning
- [ ] No feature milestone fits? Assign to the **current maintenance milestone** (v1.9.0 while it is open) — see `process.md → Maintenance milestone` for rules on when it gets replaced
- [ ] Ask the user which milestone if unsure — never assume
- [ ] The `gh issue create` command itself includes `--label` and `--milestone` — approving the draft
      approves all three (title/body, label, milestone) in one pass, not label/milestone as a
      follow-up round after the issue already exists
- [ ] **If the issue concerns a condition that has a Knowledgebase entry, add the issue number to that
      entry** — and if the condition has no entry yet but warrants one (run the triage question in
      `docs/knowledgebase.md`), create it. This is what keeps a `QTN-INV` status honest: the guard test
      that no investigating entry points at a closed issue only works if the link was made when the
      issue was filed. Doing it later means doing it never.

---

## Planning an issue

Covers the `Planning` phase — see `process.md` → "Working on an issue" → Planning for the full detail
behind each item.

- [ ] Read the full issue spec: `gh issue view <N>`
- [ ] Read the plan doc (or confirm one is being created now)
- [ ] **Cross-check the spec against current authoritative sources before writing any code** — in order: `docs/architecture-decisions/` (ADRs), JSON schemas (`schemas/`), generator/script behaviour, C# models, project documentation. Raise any mismatch to the user and get explicit confirmation on scope before proceeding — never silently assume a gap is in-scope or out-of-scope.
- [ ] Check the dependency map in `overview.md` — verify all blocking issues are fully complete before starting
- [ ] **Verification checklist created in the plan doc** — one entry per requirement in the spec, each naming either the exact unit test (class + method) to be written, or the exact live command and expected output. Status is its own column; `#` is plain sequential integers, never lettered.
- [ ] **Plan doc steps are numbered sections** (`### N. <title>`), each with `**Status:**` as the first line — never a flat checklist
- [ ] For bug fixes: the plan identifies what the red test/reproduction will be before any fix is written
- [ ] Issue status set to `Planning` in both the plan doc header and `overview.md` while this work is in progress

---

## Implementing an issue

Covers the `In progress` phase — see `process.md` → "Working on an issue" → Implementation for the
full detail behind each item.

- [ ] Every test named in the plan doc's verification checklist is written first and confirmed red against current code before any production code changes
- [ ] **Any automated (T2) document the issue adds is confirmed red too** — run against a build from the commit before the work started, then torn down (container, image, worktree). See `process.md` → Implementation, step 1
- [ ] **Both kinds of test are updated where the issue touches them** — unit tests and `docs/automated-testing/` documents are equally in scope for an issue, never a follow-up
- [ ] Issue status set to `In progress` in both the plan doc header and `overview.md` (use `In progress (step N)` in the plan doc header while a specific step is active)
- [ ] Each plan-doc step's `**Status:**` line is updated as work on that step progresses — this is the only place step progress is tracked, no separate list
- [ ] Implementation makes each red test green, without weakening the test to pass
- [ ] Before considering implementation done: re-read every requirement in the GitHub issue spec and execute each documented verification step against the actual code — not just the tests, the live commands too
- [ ] Once every verification-table row is ✅, move to `checklist.md` → "Before closing an issue" → "Waiting for release" — do not leave these items for later

---

## Before closing an issue

Two phases — `Waiting for release` happens as soon as verification is genuinely complete (does not
wait for a release); `Released` is gated on a tagged, artefact-confirmed release. Do not defer
`Waiting for release` items to closing time; sync them immediately so the plan doc, `overview.md`, and
the GitHub issue always reflect current reality. See `process.md` → "Issue lifecycle" for how these two
fit alongside the earlier `Planning`/`In progress` phases.

### Waiting for release

Once the plan doc's Verification table is all ✅:

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
- [ ] No requirement is still unconfirmed — if anything is unverified, the issue stays open and none of the remaining `Waiting for release` items apply yet
- [ ] **Definition of done checkboxes ticked** — every box in the GitHub issue's own "Definition of done" section reflects the (already-verified) plan doc Verification table, except "Findings summarised in a closing comment" which stays unticked until the `Released` phase's close step actually happens; see `process.md` → "Completing an issue" for the `gh issue edit` mechanics. A box that can't honestly be ticked means the issue isn't done yet.
- [ ] **Process gap check** — did anything about how this issue was worked diverge from documented process, or expose something the docs never covered? If so, resolve per `process.md` → "Process gap discovery" (fix the doc if it's a genuine gap; otherwise no doc change, just note it) before considering this issue's closing complete.
- [ ] **Changelog `unreleased` entry added** — add the issue number to `unreleased.issues` in `changelog.en.json`; add at least one entry to `added`, `changed`, or `fixed`; add a `highlights` entry if the change is user-facing; update `nl.json` and `de.json` lockstep; regenerate **`CHANGELOG.md` only**, via `scripts/changelog.csx --max-releases 3`. Do this now, not at release time — the whole point of the `[Unreleased]` section is that entries accumulate as work completes, so promoting them later is a rename, not a writing exercise.
- [ ] **Do *not* regenerate `addon/CHANGELOG.md` or `addon-beta/CHANGELOG.md` here.** The add-on changelogs are generated only when a release is published (see `CLAUDE.md` → "Tagging a release", steps 12–14, and #236 for why they are deferred until the image is confirmed pullable). Regenerating them at this phase rewrites only their `GENERATED FILE [timestamp]` header — the `ha-addon` format renders no unreleased content today — which advertises a change that isn't there. **An `Unreleased` section in a published *stable* add-on changelog is always a missed step**, never normal: only a beta release can legitimately carry one. Making the beta channel actually *show* its unreleased section was considered and deferred (developer decision, 2026-08-21): it needs generator and schema changes, and beta phases here are currently short enough that nothing is lost by leaving it. Revisit if a beta phase ever runs long enough for testers to need it.
- [ ] Update the plan doc status to `Waiting for release` (or note "no plan doc — by decision" if none exists)
- [ ] Update the status column in `overview.md` to `Waiting for release`
- [ ] Re-verify the order of operations table — update if this issue's completion changes the correct sequence

### Released

Once the tag is pushed and the fix is confirmed in the release artefact:

- [ ] **PR merged to `main`** — do not run `gh issue close` while still on the feature branch; the issue stays open until the merge lands
- [ ] Confirm all changes are merged to `main` and included in a tagged release, and the fix is confirmed working in the appropriate artefact (Docker smoke-test, HA add-on, or user content review) — a pushed tag and green CI alone are not the confirmation (`issue-closure.md`'s Gate 2)
- [ ] Confirm this issue's `unreleased` changelog entry (added back at `Waiting for release`) was actually promoted into the tagged release's entry — see `CLAUDE.md`'s Pre-Push Checklist "When tagging a release" step
- [ ] **Release issue-list** — every release entry whose work traces back to this issue must carry the issue number in its `issues[]` array, including hotfix releases. If a release is already tagged, add the number to the matching entry in `changelog.en.json` (+ `nl.json`, `de.json` lockstep) and regenerate.
- [ ] **User confirms closure** — show the user the closing comment and verification table and wait for explicit approval before running `gh issue close`
- [ ] Close: `gh issue close <N> --comment "<verification table>"`
- [ ] Update the plan doc status to `Released` and update the status column in `overview.md` to match

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

Releases follow a two-stage model. See `docs/release-verification.md` for tier definitions (T1/T2/T3, plus the milestone-scoped T4) and the full stage table.

- [ ] All issues verified: `gh issue list --milestone "<Name>" --state open` returns empty
- [ ] **Process gap check** — across the whole milestone, did anything about the workflow itself repeatedly feel undefined or get skipped? A pattern visible only at milestone scope (not from any single issue) still needs the same investigate-and-decide treatment — see `process.md` → "Process gap discovery"
- [ ] If the milestone added any migrations: full incremental migration path verified against a
      database matching the *last published release's* schema, not the accumulated dev database —
      see [ADR 009](../architecture-decisions/009-verify-migrations-against-last-released-schema.md)
- [ ] If the milestone added any migrations: consolidate the accumulated set before release, the way
      #155 and #289 each consolidated their predecessors. What accumulated is read from
      `DatabaseInitializer.DataOwnedMigrations` and the consumer's own list against the last
      published release — never from a list maintained by hand
- [ ] **T4 — Docker image freshness**: `docker build --no-cache --pull -f docker/Dockerfile -t quotinator:local .`
      then `docker scout cves quotinator:local`; compare against `docs/security/README.md`'s "Docker
      base image (OS packages)" table and update it (at minimum the "Last scanned" date) in the same
      commit — see `docs/release-verification.md`'s T4 section. **`--pull` is required alongside
      `--no-cache`** — `--no-cache` alone does not force a fresh base-image pull and can silently
      reuse a stale cached base layer. Never skipped, never substituted by a T2 pass's own
      (cache-permitted) `docker build`.
- [ ] **`IDE0008` solution-wide trial** — temporarily move `dotnet_diagnostic.IDE0008.severity =
      warning` into `.editorconfig`'s solution-wide block, build, and count the warnings. If the count
      is small enough to clear as part of this milestone's close, keep it, clear the remainder, and
      **delete the per-file scoped section**; otherwise revert the escalation and record the measured
      count here so the trend is visible next milestone. Baseline: **14,286** (2026-08-15). See
      `CLAUDE.md` → "Variable declarations" for the full rule and why the scoped list exists
- [ ] Build clean: `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] Tests pass: `dotnet test --configuration Release` — all tests pass, 0 warnings
- [ ] Changelogs updated (`CHANGELOG.md`, `addon/CHANGELOG.md`, `addon-beta/CHANGELOG.md`) — optionally
      pick a fitting one-line release `quote` reflecting something notable about the release
- [ ] Final PR merged to `main` (without `--delete-branch` — developer deletes the branch manually if desired)

### Beta tag (T1 + T2 gate)

A beta tag is mandatory for every release. The release workflow enforces this — pushing a final tag without a prior beta tag for the same version fails the workflow immediately.

**Before the tag** (one PR, merged to `main` before pushing the tag):
- [ ] T1 verified: app starts in VS without error; affected Razor pages render correctly
- [ ] T2 verified: `docker build -f docker/Dockerfile -t quotinator:local .` succeeds; smoke-test commands return expected output
- [ ] `Directory.Build.props <Version>` set to beta version — **this is where the development `-alpha`
      suffix is dropped**, per *Version during development* under *Milestone start*. The version must
      match the tag exactly from here on
- [ ] Changelog beta entry in `changelog.en.json` (+ `nl.json`, `de.json` lockstep); `CHANGELOG.md` regenerated with `--max-releases 3`. **Do not regenerate `addon/CHANGELOG.md`/`addon-beta/CHANGELOG.md` yet.**
- [ ] Push beta tag: `git tag vX.Y.Z-beta && git push origin vX.Y.Z-beta`
- [ ] Confirm GitHub Actions release workflow completes; pre-release created on GitHub with correct Docker image; its own "Verify image is pullable (amd64 + arm64)" step passed

**After the workflow is green** (second, separate PR — see [#236](../milestones/maintenance-milestone-v1.8.0/236-release-workflow-version-race-plan.md) for why this can't be the same PR as above):
- [ ] `addon-beta/config.yaml version` set to beta version (e.g. `1.7.0-beta`) — the stable `addon/config.yaml` is not touched by a beta tag
- [ ] `addon-beta/CHANGELOG.md` regenerated with `--max-releases 3` from the already-merged `changelog.en.json` (`addon/CHANGELOG.md` is untouched by a beta tag)
- [ ] This PR merged to `main`

### Final tag (T3 gate)

Push the final tag after T3 is verified in the live HA add-on.

**Before the tag** (one PR, merged to `main` before pushing the tag):
- [ ] T3 verified: beta add-on installed in HA; all T3-classified requirements confirmed in the live supervisor
- [ ] `Directory.Build.props <Version>` bumped to final version (remove prerelease suffix; also remove the pinned `AssemblyVersion`/`FileVersion` lines — they are only needed when `<Version>` carries a suffix)
- [ ] Changelog final entry promoted from beta; `CHANGELOG.md` regenerated with `--max-releases 3`. **Do not regenerate `addon/CHANGELOG.md`/`addon-beta/CHANGELOG.md` yet.**
- [ ] **This PR merged to `main` before tagging** — confirm the above changes are on `main`, not just a local commit. Tags are immutable once pushed; pushing a tag against un-merged version files burns a patch version.
- [ ] Push final tag: `git tag vX.Y.Z && git push origin vX.Y.Z`
- [ ] Confirm GitHub Actions release workflow completes; full release created on GitHub; `latest` Docker tag updated; its own "Verify image is pullable (amd64 + arm64)" step passed

**After the workflow is green** (second, separate PR — see [#236](../milestones/maintenance-milestone-v1.8.0/236-release-workflow-version-race-plan.md) for why this can't be the same PR as above):
- [ ] `addon/config.yaml version` bumped to final version (e.g. `1.7.0`) — the beta `addon-beta/config.yaml` is not touched by a final tag
- [ ] `addon/CHANGELOG.md` regenerated with `--max-releases 3` from the already-merged `changelog.en.json` (`addon-beta/CHANGELOG.md` is untouched by a final tag)
- [ ] This PR merged to `main`
- [ ] Milestone closed on GitHub: `gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed`
