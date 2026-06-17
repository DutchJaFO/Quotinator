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
3. Implement. Update plan doc step status as work progresses.
4. Before declaring done: re-read **every requirement** in the GitHub issue spec and verify each one is implemented and tested.

An issue is done only when every requirement in its GitHub spec is met. Partial implementation means the issue stays open.

---

## Completing an issue

1. Update the plan doc status to `Complete`.
2. Update the status column in `overview.md`.
3. Close on GitHub:
   ```
   gh issue close <N> --comment "<short note on what was done>"
   ```

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
