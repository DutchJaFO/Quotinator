# Milestone Workflow Process

## Purpose

This document defines how we plan, execute, and close milestones. All workflow templates live in `docs/workflow/`. Per-milestone documents live in `docs/milestones/{milestone-slug}/`.

## Folder and file naming

Milestone slugs and plan file names use only lowercase letters, numbers, and hyphens — no spaces or special characters. Derived from the milestone title: replace spaces with hyphens, strip punctuation, lowercase everything.

Examples:
- "Data Import & Sources" → `data-import-sources`
- Issue plan files: `{issue-number}-{safe-slug}-plan.md`

---

## Starting a milestone

### Step 1 — Create the milestone folder on `main` before branching

1. Fetch all issues in the milestone:
   ```
   gh issue list --milestone "<Milestone Name>" --state all --limit 50 --json number,title,state
   ```
2. Read each issue spec in full:
   ```
   gh issue view <N>
   ```
3. Map dependencies between issues.
4. Decide on an order of operations.
5. Create `docs/milestones/{slug}/overview.md` (see `checklist.md` for the template).
6. Create a per-issue plan doc for every issue in the milestone.
7. Commit the milestone folder to `main`.

### Step 2 — Create the feature branch

```bash
git checkout -b feature/{slug}
```

All code and plan doc updates go on this branch. Milestone content does not go directly to `main` after the initial commit — updates travel through the feature branch and merge with the code.

---

## Session start

At the start of every session working on a milestone:

1. Check for new issues:
   ```
   gh issue list --milestone "<Milestone Name>" --state open --json number,title
   ```
2. Compare against `overview.md`. For any new issues: fetch the spec, create a plan doc, update the overview.
3. Review the plan docs for issues being worked on today.

---

## Working on an issue

1. Read the full issue spec: `gh issue view <N>`
2. Read the plan doc.
3. Check the dependency map in `overview.md`. Verify that all blocking issues are fully complete before starting — a partially-done dependency means this issue cannot be closed either.
4. For each requirement in the spec, create a verification checklist entry in the plan doc.
   This is part of planning — it must exist before implementation starts. Each entry must state:
   - **Unit test preferred:** if a unit test can cover it, name the exact test class and method.
     Write the test as part of the issue if it does not yet exist.
   - **Live test fallback:** if no unit test is possible, document the exact command to run
     and the observable output that confirms the requirement is met.
   Checklist items start red (test failing or command not yet passing) and turn green as
   implementation progresses. A green item means the test passes or the command produces
   the expected output — not that the code looks right.

   The verification checklist in the plan doc must use this table format:

   | # | Status | Requirement | Method | Verification |
   |---|--------|-------------|--------|--------------|
   | 1 | ❌ / ✅ | Description | Unit test / Live | Test class.Method or exact command + expected output |

   `Status` is a standalone column between `#` and `Requirement` — never embed ✅ or ❌ inside the Verification column.

   **Bug fixes:** before writing any fix, first confirm the bug is reproducible. Write a
   failing unit test that demonstrates the bug, or document the exact steps and observed
   output that prove it exists. The fix is complete only when that test passes or those
   steps no longer reproduce the bug.
5. Implement. Update plan doc step status as work progresses.
6. Before declaring done: re-read **every requirement** in the GitHub issue spec and execute each documented verification step against the actual code.

An issue is done only when every requirement in its GitHub spec is met and verified against actual code. Partial implementation means the issue stays open.

---

## New issues discovered during milestone work

When a new issue is identified during an active milestone session — whether it is a bug, improvement idea, or downstream dependency — file it immediately while the context is fresh. Before calling `gh issue create`:

1. **Decide the milestone** — ask the user which milestone the new issue belongs to. Do not assume it belongs to the current milestone. Present the options and wait for an explicit decision.
2. **Decide the branch** — if the issue belongs to the current milestone, it will be worked on the current feature branch. If it belongs to a different milestone, note it and leave it for that milestone's feature branch.
3. **Never file without a milestone** — an issue with no milestone is invisible to planning. Always assign one before creating.

This rule applies to all issue actions: assigning, moving, labelling. Always ask; never assume.

---

## Scope changes and deferrals

If during planning or implementation a requirement from the GitHub issue spec is deferred to a later issue:

1. Post a comment on the GitHub issue documenting what was deferred, why, and which issue it moves to.
2. Update the plan doc with a **Scope changes** section listing the same information.
3. Update the downstream issue's plan doc to reflect the deferred work — describe what the upstream issue delivered and what the downstream issue needs to decide or build on top of it.
4. The closing verification table covers only the requirements that remain in scope. Deferred items are listed separately with a pointer to the issue that owns them.

An issue may only close when its GitHub issue page reflects the actual scope — either the spec was never changed, or a comment documents every deferral. Never close an issue whose spec contains requirements that were silently dropped.

---

## GitHub auto-close behavior

GitHub closes an issue automatically when a commit merged to the default branch contains any of the following patterns in its message (case-insensitive):

```
close #N    closes #N    closed #N
fix #N      fixes #N     fixed #N
resolve #N  resolves #N  resolved #N
```

Ending a commit title with `(#N)` links the commit to the issue and can also trigger auto-close depending on how the merge reaches the default branch.

**Rule: never use any of these patterns in a commit message.** Issue closure is always done explicitly via `gh issue close <N> --comment "..."` after the full closing checklist is complete. A commit that accidentally closes an issue violates the workflow — the issue will have no closing verification comment and will show as closed without evidence of testing.

A `commit-msg` hook in `scripts/hooks/commit-msg` guards against this. Install it once per clone:

```bash
cp scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/commit-msg
```

---

## Completing an issue

An issue may only be closed when **all** of the following are true:

- Every requirement in the GitHub issue spec is implemented and tested, **or** any deferred requirements are documented via a comment on the issue (see Scope changes above)
- All related/blocking issues it depends on are themselves closed
- All changes are merged to `main`
- The changes are included in a tagged release

**Timing on feature branches:** while working on a feature branch, an issue can reach "spec complete, all tests green" status before the PR is merged. Do not run `gh issue close` at that point. The issue stays open until the PR is merged to `main`. Once merged, verify one final time that the main branch is green, then close.

Steps:

1. Update the plan doc status to `Complete`.
2. Update the status column in `overview.md`.
3. Re-verify the order of operations table — a completed issue may unblock others or change the correct sequence. Update the table if needed before picking the next issue.
4. **After the PR is merged to `main`:** close on GitHub:
   ```
   gh issue close <N> --comment "<short note on what was done>"
   ```

---

## Merges to main must never break the application

Every merge to `main` — whether a full milestone, a partial milestone, a hotfix, or a dependency bump — must leave `main` in a working state:

- `dotnet build --configuration Release` — 0 warnings, 0 errors
- `dotnet test --configuration Release` — all tests pass, 0 warnings
- The application starts and serves requests correctly

This applies regardless of how complete the milestone is. Incomplete issues are fine on `main` as long as their in-progress code does not break existing behaviour. Incomplete work must be either not yet wired into the request path, or safely inert (e.g. a new table that existing endpoints do not touch). Never merge half-registered services, failing migrations, or endpoints that return 500.

Issues that are not yet started or still in progress stay open after a partial merge. Only fully verified issues (spec complete + tests green + PR merged) may be closed.

---

## Closing a milestone

A milestone is closed only when all issues are resolved or explicitly closed with documented justification.

1. Confirm: `gh issue list --milestone "<Milestone Name>" --state open`
2. Run the full pre-push checklist (`CLAUDE.md`): build, tests, Docker build.
3. Push to `main`, tag the release.
4. Close the milestone on GitHub:
   ```
   gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed
   ```
