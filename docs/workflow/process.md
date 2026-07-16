# Milestone Workflow Process

## Purpose

This document defines how we plan, execute, and close milestones. All workflow templates live in `docs/workflow/`. Per-milestone documents live in `docs/milestones/{milestone-slug}/`.

## Where information lives

Two rules prevent the same fact from being written in two places, drifting out of sync the moment one of them is updated and not the other:

- **`overview.md` carries status only, never detail.** For any given issue, `overview.md` states its current status and links to its plan doc. It never explains *what* was done, *why*, *when a tier was verified*, or *what a session found* — that is the plan doc's job.
- **A plan doc's numbered step sections and Verification table carry the detail, and their own status.** There is no separate "Step status" checklist — each step is its own subsection (`### N. Title`) with `**Status:**` as its first line, followed by the step's actual detail in that same place. Do not add prose sections (`Notes`, `Implementation notes`, session narratives) that restate what a step or verification row already documents — that is duplication, not a record of anything new. The one legitimate exception is a **Scope changes** section (see below): it records a *decision* — what moved, why, which issue now owns it — not a re-explanation of work already captured by a step or verification row.

**`overview.md`'s header `**Status:**` line and every plan doc's own header `**Status:**` line are exactly one of these, nothing added:**

| Status | Means |
|--------|-------|
| `Planning` | Scope/design still being worked out; no implementation started |
| `In progress` (plan docs with numbered steps: `In progress (step N)`, naming the step currently being worked) | Implementation under way; not yet fully verified |
| `Waiting for release` | Fully implemented and verified; not yet shipped in a tagged release |
| `Released` | Shipped in a tagged release |

No dates, no "See X for detail" pointers, no extra clauses of any kind. If something beyond the bare status word feels necessary, that need is itself the signal it belongs in a section of the doc, not the Status line — the reader is already looking at the document.

**This "no duplication" rule applies to every header field, not just Status.** `**Tiers required:**`, `**GitHub issue:**`, `**Depends on:**`, and any other header line state the bare fact only — no parenthetical justifying *why* (e.g. which files or migrations trigger a tier). That reasoning already lives in the plan doc's own Steps section; repeating it in the header creates a second copy that can silently drift out of sync the moment a step changes and the header doesn't get updated to match. If a header field ever tempts a "— because..." or "(touches X, Y, Z)" clause, that is the same signal as with Status: the content belongs in a body section, not the header.

This does not apply to an individual step section's own `**Status:**` line (e.g. `✅ Done`, `⬜ Not started`) — that is a separate, per-step concept already covered above, not the document-level header.

**`In progress` requires actual outstanding code/doc work.** Before setting or leaving an issue at `In progress`, check whether every step section and every verification row in its plan doc is already ✅. If they are, the issue is `Waiting for release` even if its Tiers column still shows an unverified tier (e.g. `T3 ⬜`) — Tiers and Status are separate axes. This matters specifically for T3: T3 verification (live HA supervisor) can only happen after a beta tag exists, so a T3-only gap is never something more code work can close right now. Calling that `In progress` implies work that doesn't exist. Re-check this whenever you review or update a plan doc's header status, not just when you first set it.

### Issue lifecycle

Every issue moves through exactly these four phases, in order — the status word above **is** the
phase name. Each phase has its own entry criteria and its own steps/checklist; do not treat "working
an issue" as one undifferentiated block of work, and do not defer a phase's steps to a later phase
just because they're documented near each other.

| Phase (status) | Entry criteria | Steps documented in | Checklist in |
|---|---|---|---|
| `Planning` | Issue filed, milestone assigned | "Working on an issue" → Planning, below | `checklist.md` → "Planning an issue" |
| `In progress` | Verification checklist exists in the plan doc | "Working on an issue" → Implementation, below | `checklist.md` → "Implementing an issue" |
| `Waiting for release` | Every plan-doc verification row is ✅ | "Completing an issue" → Waiting for release, below | `checklist.md` → "Before closing an issue" → Waiting for release |
| `Released` | Tag pushed, fix confirmed in the release artefact | "Completing an issue" → Released, below | `checklist.md` → "Before closing an issue" → Released |

A phase's own steps run **as soon as that phase's entry criteria are met** — they do not wait for the
next phase's gate. This is what "Completing an issue" below means by "two phases, not one flat list,"
and it applies the same way to Planning/Implementation: don't bundle scope-checking work into the
implementation phase's steps, and don't defer implementation-phase step-status updates until the whole
issue looks done.

**The same "don't duplicate what git already tracks" principle applies to ADR headers.** An ADR's
`Updated:` field (see `docs/architecture-decisions/README.md`) holds one date, never an accumulated
parenthetical log of every issue that touched the file — that log is exactly what `git log` on the
file already is. This is why plan doc and ADR updates must land in their own commit, separate from
the code change that motivated them: a code commit that also silently rewrites a doc's header buries
the doc change inside an unrelated diff, and the git-history-as-source-of-truth argument above only
holds if the history is actually legible per-file. Going forward: when a step of work produces both a
code change and a plan-doc/ADR update, commit the code first, then the doc update as its own commit
(same issue number, `docs [#N]: ...` per the existing commit-message convention) — never combine them
just because they happened in the same session.

**Commit message format and content.** Title is `type [#N]: short summary` — `type` is one of `feat`
(new capability), `fix` (bug fix), `docs` (documentation-only change, no source files), `chore`
(tooling/dependency/config, no behaviour change), `refactor` (code-organisation change with no
behaviour change, e.g. moving a type between projects); `[#N]` is the GitHub issue number, or
multiple bracketed numbers (`[#69][#157]`) when a commit's work genuinely spans more than one issue.
Content differs by commit type:

- **Code commits (`feat`/`fix`/`refactor`/`chore`) are terse.** The diff and the code's own comments
  already explain *what* changed — a commit message that restates them (`"added X property to Y
  class"`, `"changed the loop to use LINQ"`) is redundant. State the *why* in one or two sentences,
  only when it isn't obvious from the diff itself (a bug's root cause, a design trade-off, which
  issue's requirement this satisfies). If there's nothing non-obvious to say, the title alone is a
  complete commit message — do not pad it with a body just to have one.
- **Documentation-only commits (`docs`) are the exception — write fuller content.** Since ADR/plan-doc
  headers no longer carry accumulated history (see above), the commit message for a `docs` commit *is*
  that document's historical record — there is no header field or code diff to fall back on for
  understanding what changed and why later. A `docs` commit message should describe what changed in
  the document and why in enough detail to stand alone in `git log`, the way the ADR's own `##
  Revision — issue #N` body section does for the document itself.

**Draft, review, then commit — every time, no exceptions.** Before running `git commit`, write the
full intended commit message to `.claude/temp/commit-draft.md` **and paste that same text directly
into the chat response** — the developer must be able to read the full draft in the conversation
itself, without opening a file or expanding a tool result. A `Read` tool call on the draft file does
not satisfy this: its output renders as a tool result, not as the assistant's own message text, and
has already been treated as "not really shown" once a developer had to say so explicitly (2026-07-14,
issue #175's body edit — the assistant `Read` the draft instead of pasting it, which is exactly the
gap this sentence exists to close). Only run the actual commit after explicit approval, via `git
commit -F .claude/temp/commit-draft.md` so the reviewed text and the committed text are identical by
construction. The `commit-msg` hook (`scripts/hooks/commit-msg`) enforces the mechanics of this — it
blocks any non-merge commit whose message doesn't exactly match `.claude/temp/commit-draft.md` — but
it cannot verify the review itself happened, only that the draft-then-commit sequence was followed. A
`post-commit` hook (`scripts/hooks/post-commit`) automatically deletes `.claude/temp/commit-draft.md`
right after a successful commit, so a leftover draft can't silently satisfy the `commit-msg` hook for
a later, unrelated, unreviewed commit — this is automated, not something to remember by hand. The
same draft-then-review rule applies to GitHub issue text (`gh issue create`/`gh issue edit`): write
the draft to a file, paste its full text directly into the chat response (same rule as above — not
merely readable via a tool call), get approval, then run the command against that file, then delete
the draft file the same way. There is no equivalent client-side hook for `gh` issue commands — neither the
review gate nor the cleanup — so that whole side is enforced by discipline, not tooling.

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
6. Create a per-issue plan doc for every issue in the milestone. A parent (tracking) issue gets one
   too, but shaped like a miniature `overview.md` — a sub-issue list, dependency map, and order of
   operations, with no Steps or Verification checklist of its own. See `issues.md` → "Splitting an
   issue into sub-issues".
7. Commit the milestone folder to `main`.

### Step 2 — Create the feature branch

```bash
git checkout -b feature/{slug}
```

All code and plan doc updates go on this branch. Milestone content does not go directly to `main` after the initial commit — updates travel through the feature branch and merge with the code.

**The feature branch lives for the entire milestone.** Do not delete it when doing a partial merge to `main`. A partial merge is a sync point — the branch continues to exist as the workspace for remaining issues.

**Branch deletion is never done by the AI assistant.** Only the developer deletes branches, and only when they have decided the branch is no longer needed. Never use `--delete-branch` or `git branch -d` or any equivalent. If a PR needs to be merged, merge it without the delete flag and let the developer decide what happens to the branch.

---

## Session start

At the start of every session working on a milestone:

1. **Check remote branches before assuming the local view is complete.** Another session (e.g. a cloud session running in parallel) may have pushed branches that are not present locally. Fetch before creating any new branch:
   ```
   git fetch --all
   git branch -r | grep feature/
   ```
   Never create a new branch until you have confirmed no matching branch already exists on the remote. Creating a duplicate branch fragments work across two branches and makes merging unnecessarily difficult.

2. Check for new issues:
   ```
   gh issue list --milestone "<Milestone Name>" --state open --json number,title
   ```
3. Compare against `overview.md`. For any new issues: fetch the spec, create a plan doc, update the overview.
4. Review the plan docs for issues being worked on today.

---

## Working on an issue

An issue's own two working phases — see "Issue lifecycle" above. Planning ends and Implementation
begins once the plan doc's verification checklist exists and the issue's status moves to `In progress`.

### Planning

1. Read the full issue spec: `gh issue view <N>`
2. Read the plan doc.
3. **Cross-check the spec against the current authoritative sources — do this before writing any code.**

   Issues are written at a point in time. Prior issues in the same milestone may have introduced schemas, models, or design decisions that change what this issue should cover. Before accepting the spec as written:

   **Check sources in this order:**
   1. **`docs/architecture-decisions/`** — formal, numbered ADRs. An ADR can govern a design decision (e.g. entity/table shape) that the current issue never mentions. Copying an existing entity's shape is not a substitute for checking this — the existing entity may itself violate an ADR (see CLAUDE.md's "Authoritative sources" section for how this went wrong once already).
   2. **JSON schemas** (`schemas/`) — the machine-readable contract for any data file the issue touches. A feature not defined in the schema does not officially exist yet, regardless of what the code does.
   3. **Script / generator behaviour** — if the issue touches a generator (e.g. `changelog.csx`), read the script. The generator defines what the schema promises to consumers; its parameters and helper functions reveal the full intended scope.
   4. **C# models** — cross-reference the models against the schema. Gaps between them are bugs: a model property not in the schema is undocumented; a schema field not in the model is unimplemented.
   5. **Documentation** — any project-level README or design doc that describes the format to consumers.

   For each source, explicitly ask:
   - Does the issue reference every field or concept that now exists in the authoritative source?
   - Have prior issues introduced constructs (schema fields, model properties, generator parameters) that this issue should logically extend but does not mention?
   - Does every "scope exclusion" in the issue still make sense given what was actually built and documented?

   **Raise each mismatch to the user and get explicit confirmation on scope before proceeding.** Do not silently assume a gap is intentional. Do not silently assume a gap is out of scope. The procedure is: flag it, explain the conflict, wait for a decision.

   This step is the primary defence against implementing the wrong scope. It is not optional and must not be skipped even when the issue appears straightforward.

4. Check the dependency map in `overview.md`. Verify that all blocking issues are fully complete before starting — a partially-done dependency means this issue cannot be closed either.
5. For each requirement in the spec, create a verification checklist entry in the plan doc.
   This is part of planning — it must exist before implementation starts. Each entry must state:
   - **Unit test preferred:** if a unit test can cover it, name the exact test class and method.
     Write the test as part of the issue if it does not yet exist.
   - **Live test fallback:** if no unit test is possible, document the exact command to run
     and the observable output that confirms the requirement is met.
   - **External verification with audit trail:** when a requirement can only be verified by an
     external tool or process (e.g. a cloud session, a deployed HA add-on, a CI run), do not
     simply assert that it was verified. Capture the actual output as proof:
     1. Create `docs/milestones/{slug}/audit/issue-{N}/` in the branch.
     2. Write the raw command output to numbered log files (e.g. `log-1-build.txt`,
        `log-2-tests.txt`).
     3. Add a `README.md` in that folder summarising what was run, when, and what the result was.
     4. Commit the audit folder to the branch. The closing comment on the GitHub issue then
        references the audit folder as the source of evidence.

     This pattern applies only when no unit test or locally-runnable command is feasible.
     It is not a substitute for tests — it is the last resort when the execution environment
     itself is the thing under test (e.g. a hook that installs tooling in a remote container).

   Checklist items start red (test failing or command not yet passing) and turn green as
   implementation progresses. A green item means the test passes or the command produces
   the expected output — not that the code looks right.

   The verification checklist in the plan doc must use this table format:

   | # | Status | Requirement | Method | Verification |
   |---|--------|-------------|--------|--------------|
   | 1 | ❌ / ✅ | Description | Unit test / Live | Test class.Method or exact command + expected output |

   `Status` is a standalone column between `#` and `Requirement` — never embed ✅ or ❌ inside the Verification column. The `#` column is always plain sequential integers, in row order top to bottom — never lettered (`7a`, `7b`) and never numbered out of sequence with the row's actual position. A requirement discovered after the table already exists gets appended at the next integer (or the whole table renumbered if it belongs earlier), not a lettered insert. This applies equally to `overview.md`'s Order of operations table (see `checklist.md`).

   **A plan doc's steps are numbered sections, never a checklist.** A one-line checklist item is never enough room to describe a step properly — trying to fit detail into it forces a choice between cramming (unreadable) or a separate prose section that repeats the same content (duplication, the exact thing `Where information lives` warns against). Instead, each step is its own subsection — `### N. <short imperative title>` — with `**Status:** <state>` as the very first line of the section body, followed by whatever detail that step actually needs. No separate "Step status" list anywhere in the doc; the section *is* the status and the detail together, in one place. Numbered sequentially in real execution order — same rule as the Verification table: plain integers, never lettered, never out of position. A step discovered mid-implementation is inserted at its actual place in the sequence (renumbering everything after it), not appended out of order at the end.

   **Bug fixes:** before writing any fix, first confirm the bug is reproducible. Write a
   failing unit test that demonstrates the bug, or document the exact steps and observed
   output that prove it exists. The fix is complete only when that test passes or those
   steps no longer reproduce the bug.

### Implementation

1. Write every test named in the plan doc's verification checklist first and confirm each one is
   genuinely red against current code, per the red-before-green rule above.
2. Implement. Update each step section's `**Status:**` line as work progresses — this is the per-step
   record; there is no separate "what's left" list to maintain elsewhere.
3. Before declaring done: re-read **every requirement** in the GitHub issue spec and execute each
   documented verification step against the actual code. Every row in the plan doc's verification
   table should now be ✅ — if it is, the issue has reached the `Waiting for release` phase (see
   "Completing an issue" below); if any row is still ❌, implementation continues.

An issue is done only when every requirement in its GitHub spec is met and verified against actual code. Partial implementation means the issue stays open.

---

## New issues discovered during milestone work

When a new issue is identified during an active milestone session — whether it is a bug, improvement idea, or downstream dependency — file it immediately while the context is fresh. Before calling `gh issue create`:

1. **Decide the milestone** — ask the user which milestone the new issue belongs to. Do not assume it belongs to the current milestone. Present the options and wait for an explicit decision.
2. **Decide the branch** — if the issue belongs to the current milestone, it will be worked on the current feature branch. If it belongs to a different milestone, note it and leave it for that milestone's feature branch.
3. **Never file without a milestone** — an issue with no milestone is invisible to planning. Always assign one before creating.
4. **No feature milestone? Use the current maintenance milestone** — the maintenance milestone (currently v1.7.0) is the catch-all for bugs and minor improvements that do not belong to a feature milestone. See *Maintenance milestone* below for the full rules.

This rule applies to all issue actions: assigning, moving, labelling. Always ask; never assume.

**Release linking:** when closing an issue, confirm it is assigned to the milestone that matches the version it shipped in. If the release is already tagged and no matching milestone exists, add the issue number to the `issues` array of the corresponding entry in `changelog.en.json` (and `nl.json`, `de.json` lockstep) so the release stays traceable.

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

**Rule: never use any of these patterns in a commit message or PR body.** The same keywords in a PR description trigger auto-close when the PR is merged to the default branch. Issue closure is always done explicitly via `gh issue close <N> --comment "..."` after the full closing checklist is complete. An issue that auto-closes violates the workflow — it will have no closing verification comment and will show as closed without evidence of testing.

A `commit-msg` hook in `scripts/hooks/commit-msg` guards against this. Install it once per clone:

```bash
cp scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/commit-msg
```

---

## Process gap discovery

Whenever something about the workflow itself doesn't go as expected — a step feels undefined, a
rule seems to have been skipped, an artifact (a checkbox, a header field, a checklist item) turns
out to be stale or unmaintained — investigate it before moving on, rather than fixing the immediate
symptom and continuing. Two distinct outcomes are possible, and the fix is different for each:

1. **The rule already existed and was ignored.** Grep the relevant doc (`CLAUDE.md`,
   `docs/workflow/*.md`, `docs/*-conventions.md`, the relevant ADR) to confirm the rule's exact
   wording and location. If it was there and simply not followed, the fix is behavioural, not
   documentary — no doc change needed, but note the incident and, if it's likely to recur, consider
   whether the existing wording is discoverable enough (e.g. buried in an unrelated section).
2. **It's a genuine gap — the rule was never written down anywhere.** Add it to the document that
   should have carried it (see `Where information lives` above for which document owns which kind of
   fact). A genuine gap gets a doc fix in its own commit (see "Commit message format and content"
   above) — never left as an unwritten habit a future session has no way to discover.

**This check is a standing step when closing an issue or closing a milestone, not something done only
when a gap happens to be noticed.** At both points, ask: did anything about *how this issue/milestone
was worked* diverge from what's documented, or expose something the documentation never actually
covered? If so, resolve which of the two outcomes above applies before considering the close
complete. See `checklist.md`'s "Before closing an issue" and "Milestone close" sections for the
concrete checklist items this produces.

**How a discovered gap gets resolved is always the developer's decision — never the AI assistant's
own call.** An AI assistant's job here is investigation and presentation: identify the gap, classify
it (ignored-vs-genuine per the two outcomes above), and lay out what closing it would look like — not
to pick the resolution and implement it unprompted. This applies even when a resolution seems obvious
or "small" (e.g. adding one sentence to an existing section) — present it and wait for the developer
to say yes, the same as any other doc/process change. Silence or a general "handle it" is not the same
as an explicit decision on a specific proposed resolution.

---

## Completing an issue

An issue may only be **closed** when **all** of the following are true (see `issue-closure.md`'s
two-gate rule for the full criteria):

- Every requirement in the GitHub issue spec is implemented and tested, **or** any deferred requirements are documented via a comment on the issue (see Scope changes above)
- All related/blocking issues it depends on are themselves closed
- All changes are merged to `main`
- The changes are included in a **tagged release, and the fix is confirmed working in the appropriate release artefact** — a pushed tag and green CI are preconditions, not the confirmation itself (`issue-closure.md`'s Gate 2)

**Timing on feature branches:** while working on a feature branch, an issue can reach "spec complete, all tests green" status before the PR is merged. Do not run `gh issue close` at that point. The issue stays open until the PR is merged to `main`. Once merged, an issue is `Waiting for release` — it stays open until a tag ships and the artefact confirmation above happens, then close.

**Two phases, not one flat list.** The first group of steps below happens as soon as verification is genuinely complete — it does not wait for a release, and skipping straight to "closing work" risks leaving these undone for a long time. The second group is gated on the release criteria above.

### Waiting for release

Do these immediately once the plan doc's Verification table is all ✅ — regardless of release timing:

1. Update the plan doc status to `Waiting for release` (the fixed status vocabulary is `Planning` / `In progress` / `Waiting for release` / `Released` — see "Where information lives" above; there is no `Complete` value).
2. Update the status column in `overview.md` to match.
3. Re-verify the order of operations table — a soon-to-ship issue may unblock others or change the correct sequence. Update the table if needed before picking the next issue.
4. **Tick every checkbox in the GitHub issue's own "Definition of done" section** — each one should already correspond to a ✅ row in the plan doc's Verification table; ticking is a mechanical sync, not a new judgment call. There is no per-checkbox `gh` command for this: fetch the current body (`gh issue view <N> --json body -q .body`), replace each remaining `- [ ]` with `- [x]`, and write it back (`gh issue edit <N> --body-file -` or `--body "<full updated body>"`). A box that cannot honestly be ticked means the issue is not actually done — resolve that before proceeding, not by leaving the box unchecked and closing anyway. The one checkbox that stays unticked at this point is "Findings summarised in a closing comment" — that becomes true only once the `Released` phase's close step below actually happens.
5. **Add the issue's changelog entry to the `unreleased` section** of `changelog.en.json` (+ `nl.json`/`de.json` lockstep) — this is the whole point of a Keep a Changelog `[Unreleased]` section: entries accumulate as work completes so promoting them at release time is a rename, not a writing exercise. Do not wait for the tag. See "Pre-Push Checklist" in `CLAUDE.md` for the exact format.

### Released

Do these once the tag is pushed and the artefact confirmation criteria above are met:

1. Confirm the release actually included this issue's already-added `unreleased` entry — promote it as part of the release-tagging step (see `CLAUDE.md`'s Pre-Push Checklist), not written fresh here.
2. Show the user the closing comment (the same verification table, reproduced in full) and get explicit approval, then close on GitHub:
   ```
   gh issue close <N> --comment "<short note on what was done>"
   ```
3. Update the plan doc status to `Released` and update `overview.md` to match.

---

## PR merge plan in overview.md

Every milestone `overview.md` must contain a **PR merge plan** section. The default assumption is that the full milestone is completed before any code merges to `main`. Departures from that default must be explicitly evaluated and recorded in the plan.

The PR merge plan answers: *which issues, if any, are safe to merge to `main` before the milestone is complete, and why?*

For each candidate for an early merge, record:
- Which issues it depends on for full functionality
- Whether those incomplete dependencies leave it inert on `main` (new infrastructure nothing currently calls)
- Whether a workaround exists for the gap (e.g. re-seed for data gaps)

The order of operations table drives this evaluation — an issue that is early in the dependency chain and has no incomplete issues that call its outputs is the most likely candidate for early merge.

---

## What makes an issue safe to include in a PR

The CI pipeline and branch protection already enforce that `main` builds and tests pass on every merge. The question is not "does it break?" — the pipeline answers that. The question is: **is this issue ready to ship on its own?**

An issue is safe to include in a PR if either:

1. **It is self-contained** — its changes work fully without any other incomplete issue, or
2. **Its incomplete dependencies leave it inert** — the issue adds infrastructure that existing behaviour does not depend on. A new table, a new repository, or a new seeder step that nothing currently calls is safe to include even if the feature that *uses* it is not done yet.

An issue is **not** safe to include without its dependencies when:

- The issue's output is only reachable or meaningful through another incomplete issue (e.g. a write endpoint that the Blazor UI for it is not done, where the endpoint itself is the deliverable)
- The issue leaves a partially-wired feature that returns errors or behaves incorrectly under normal use

**When dependencies are not yet done:** check whether a workaround exists for the gap. For example, if quote data can only be added via seeding, a re-seed covers any data-integrity gap from an incomplete import feature — buying time to finish the dependent issues before the gap matters in production.

Issues not yet started or still in progress stay open after a partial merge. Only fully verified issues (spec complete + tests green + PR merged) may be closed.

---

## Closing a milestone

A milestone is closed only when all issues are resolved or explicitly closed with documented justification.

1. Confirm: `gh issue list --milestone "<Milestone Name>" --state open`
2. **If the milestone added any migrations:** verify the full incremental migration path against a
   database matching the *last published release's* schema, not the accumulated local dev database
   — see [ADR 009](../architecture-decisions/009-verify-migrations-against-last-released-schema.md).
   File this as its own tracked issue in the milestone (see #155 for a worked example) rather than
   folding it into whichever feature issue happens to touch migrations last.
3. Run the full pre-push checklist (`CLAUDE.md`): build, tests, Docker build.
4. Push to `main`, tag the release.
5. Close the milestone on GitHub:
   ```
   gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed
   ```

---

## Maintenance milestone

There is always exactly **one** open maintenance milestone at a time. It is the catch-all for bugs and minor improvements that do not belong to a feature milestone.

**Current maintenance milestone:** v1.8.0 — issues here are expected to release as v1.8.x patch versions.

### Rules

**When creating the milestone**, define the expected version range in the milestone title and description (e.g., "v1.7.0 — bugs and minor improvements, targeting v1.7.x releases").

**When to replace the maintenance milestone** — three triggers, all require the same action (open a new maintenance milestone, move all open issues there, close the old one):

1. **All issues closed and shipped** — every issue in the current milestone has been released. Open a new maintenance milestone for the next version (e.g., v1.7.0 → v1.8.0).

2. **An issue requires a version outside the current range** — if an issue added to the maintenance milestone would require a version bump beyond the current range (e.g., a breaking change needing v2.0.0 while the maintenance milestone targets v1.7.x), create a separate milestone for it, move the issue there, and if the remaining issues in the maintenance milestone are all within range, it stays open.

3. **A feature milestone release pushes the version outside the range** — if a feature milestone ships and its release version is higher than the maintenance milestone's range (e.g., a feature milestone releases as v2.0.0 while the maintenance milestone is v1.7.0 targeting v1.7.x), the maintenance milestone can no longer accept new issues at the old version range. Open a new maintenance milestone at the next patch of the new version (e.g., v2.1.0), move all open issues there, and close the old maintenance milestone.

### Checklist when replacing the maintenance milestone

1. Open the new milestone on GitHub with a title and description that names the version range
2. Move all open issues from the old milestone to the new one: `gh issue edit <N> --milestone "<new>"`
3. Close the old milestone: `gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed`
4. Update `docs/workflow/process.md` — change "Current maintenance milestone" above to the new one
5. Update `checklist.md` — change the maintenance milestone reference in "Filing a new issue"
6. Update the memory entry `project_milestone_naming.md`

---

## Living milestones

A living milestone has no fixed scope endpoint — issues are added continuously as gaps are found during other milestone work. The **Developer Documentation** milestone (#16) is the current example.

**Time-boxed cycle model:**

- Each cycle runs for approximately 30 days, or until all open issues in the milestone are resolved — whichever comes first.
- At cycle start: set a due date on the milestone:
  ```
  gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f due_on="YYYY-MM-DDT00:00:00Z"
  ```
- At cycle end — **if at least one issue was closed during the cycle:** close the milestone and open a new one (e.g. "Developer Documentation — Cycle 2"). Reassign any remaining open issues to the new milestone. Update the new milestone's description to note the cycle number and start date.
- At cycle end — **if zero issues were closed:** extend the due date by another 30 days. A cycle with no progress produces no useful boundary — do not close and reopen just to reset the clock.

The closing gate for a living milestone is:
> Due date reached **and** at least one issue closed this cycle — OR — all current open issues resolved.

Do not apply the standard "all issues resolved" gate to living milestones.
