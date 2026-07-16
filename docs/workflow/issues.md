# Issue Workflow

This document defines the required content for each issue type and how to handle issues discovered mid-milestone. It complements `process.md`, which governs how issues are worked on and closed once filed.

---

## Issue types

Three types are used in this project:

| Label | When to use |
|---|---|
| `bug` | Something that works today stops working, or produces incorrect output |
| `enhancement` | New behaviour, new capability, or a deliberate change to existing behaviour |
| `research` | A question that must be answered before implementation can be planned — findings recorded in a closing comment |

Every issue must have exactly one of these labels.

---

## Required content by type

**A `Definition of done` section is copied verbatim from the template for that issue's type below —
never customized, never given extra issue-specific bullets.** The specific requirements for an issue
belong in `What needs to be done` / `Expected behaviour` and the `Failing tests` / `Expected tests`
table, not restated as `Definition of done` checkboxes. The `Definition of done` list is a fixed
completion gate that looks the same on every issue of a given type — if it starts accumulating
issue-specific content, that content belongs somewhere else in the issue, not there.

**Critical, non-negotiable, and universal across Bug and Enhancement issues: unit tests always go red
before the fix/feature is implemented, and green after.** This is verified by an actual red test run —
not asserted, not inferred from "the code clearly didn't have this before." See `process.md` §
"Working on an issue" for the full red-to-green rule.

### Bug

```
## Description

One paragraph: what is broken, where it is observed, and what the impact is.

## Reproduction steps

Exact steps or command to trigger the bug. Must be repeatable.

## Expected behaviour

What should happen.

## Actual behaviour

What actually happens. Include error messages, stack traces, or wrong output verbatim.

## Failing tests

List the test(s) that demonstrate the bug — or state that they need to be written.
These tests must be red before any fix is written (see process.md § Working on an issue).

| Test class | Test method | Status before fix |
|---|---|---|
| ExampleTests | MethodName_Condition_ExpectedResult | ❌ |

## Definition of done

- [ ] Failing test(s) listed above are red before the fix is written
- [ ] Fix implemented
- [ ] All listed tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```

---

### Enhancement

```
## Background

Why this is needed. What problem it solves or what capability it adds.

## What needs to be done

Numbered list of requirements. Each requirement must be independently verifiable.
This list becomes the basis for the verification checklist in the plan doc (see process.md § Working on an issue).

## Expected tests

Tests that must be written and must start red before implementation begins.
Use this table to make the red-to-green contract explicit.

| Test class | Test method | Starts |
|---|---|---|
| ExampleTests | MethodName_Condition_ExpectedResult | ❌ |

Omit this section only if all verification is via live commands (rare).

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```

---

### Research

```
## Question

The specific question this issue must answer. One sentence.

## What to investigate

Numbered list of things to examine, test, or compare.

## How findings are recorded

All findings must be posted as a comment on this issue before closing.
The comment must directly answer the question and state a recommendation if applicable.

## Possible outcomes

Research issues may conclude with any of the following — record which applies in the closing comment:

- **New issues in the current milestone** — if the findings reveal work that fits the current milestone scope, file the issues and update `overview.md`
- **New milestone** — if the findings reveal a body of work that is out of scope for the current milestone, propose a new milestone and file its issues
- **Not feasible / rejected** — if the research concludes the approach is impossible or not worth pursuing, document why and close without further action
- **Architecture decision required** — if the findings lead to a significant technical decision, write an ADR in `docs/architecture-decisions/` and link it in the closing comment (see Architecture decisions below)

## Definition of done

- [ ] All items in the investigation list addressed
- [ ] Outcome clearly stated (see Possible outcomes above)
- [ ] Any new issues filed and linked in the closing comment
- [ ] Any ADR written and linked in the closing comment
- [ ] Findings and recommendation posted as a comment on this issue
```

---

## Architecture decisions

Any issue type — bug, enhancement, or research — may produce a significant technical decision that must be captured as an ADR.

Write an ADR in `docs/architecture-decisions/` when:
- A non-obvious trade-off is made between two or more viable approaches
- A decision constrains future work in a way that is not obvious from the code
- A decision reverses or supersedes an earlier ADR

ADR format and naming rules are in [`docs/architecture-decisions/README.md`](../architecture-decisions/README.md). In summary:
- File: `NNN-short-title.md`, numbered sequentially
- Fields: **Status**, **Date**, **Context**, **Decision**, **Consequences**
- Link the GitHub issue number in the ADR header
- Link the ADR in the GitHub issue closing comment
- ADRs are never deleted — superseded ones are marked **Superseded** and a new ADR is written

Add the new ADR to the index in `docs/architecture-decisions/README.md` in the same commit.

---

## Mid-milestone issue discovery

When a gap, risk, or dependency is identified while working on a milestone issue:

1. **File the issue immediately** using the appropriate template above. Assign it to the current milestone.
2. **Map its dependencies** — determine which open issues it blocks and which block it.
3. **Update `overview.md`** — add the new issue to the issue list and dependency graph; insert it in the correct position in the order of operations table.
4. **Create a plan doc** — use `{issue-number}-{safe-slug}-plan.md`. If the issue number is not yet known, file on GitHub first, then create the plan doc.
5. **Do not start work on the new issue** in the same session unless it is a hard blocker for the current issue. Note it and continue with the current issue.

If the new issue blocks the current issue:

- Stop work on the current issue.
- Update the current issue's plan doc to record the blocker.
- Begin the new issue in the next session following the full session-start checklist.

---

## Splitting an issue into sub-issues

GitHub tracks parent/child issue relationships natively — this is a real link, not a checklist
convention: the parent shows completion progress, and each sub-issue remains a full issue with its own
label, milestone, plan doc, tests, and Definition of done.

Limits: **100 sub-issues per parent, 8 levels of nesting**. Sub-issues may live in another repository.

### When to split

Split when an issue's requirements cannot be reviewed, verified, or delivered as one unit. Signals,
any one of which is usually enough:

- **Requirements span layers that fail independently** — e.g. a `Quotinator.Data` repository capability
  and a `Quotinator.Api` response contract land, break, and get reviewed separately.
- **Requirements have different dependencies** — if requirement 6 must land before requirement 4 but
  requirement 1 depends on neither, they are not one unit of work.
- **A pre-existing bug got folded into an enhancement** — the bug needs its own red test and its own
  `bug` label; burying it inside an enhancement's requirement list hides it from the bug's own
  reporting and makes "was this fixed?" unanswerable without reading the parent.
- **The verification checklist would not fit one plan doc coherently** — if the plan doc's Steps read
  as several unrelated sequences, they are several issues.

Do **not** split merely because an issue is long. A single coherent concern with ten tightly-coupled
requirements is still one issue. The test is independence, not size.

### Parent (tracking) issue shape

A parent issue carries no implementation of its own. Its body is the shared context every sub-issue
would otherwise repeat, plus the map:

```
## Background

Why this body of work exists. Shared context, findings, and measurements the sub-issues all rely on —
recorded once here rather than duplicated into each.

## Sub-issues

| # | Scope | Depends on |
|---|---|---|
| #NNN | One line: what this sub-issue delivers | — |

State the dependency reason where one exists — "B must land before C because ..." — not just the edge.

## Scope boundary

What is deliberately NOT in this body of work, and which issue owns it instead.

## Definition of done

- [ ] Every sub-issue listed above is closed
- [ ] Findings summarised in a closing comment
```

This is the **only** permitted deviation from the "Definition of done is copied verbatim" rule above,
and it exists because a parent has no code of its own — the red-to-green gate lives on each sub-issue,
where the code actually is. A parent issue still carries exactly one type label, chosen for the body of
work as a whole.

### Sub-issue shape

A sub-issue is an ordinary issue: full template for its own type (`bug` / `enhancement` / `research`),
its own `Definition of done` copied verbatim, its own plan doc, its own verification checklist. It
does not inherit or reference the parent's Definition of done.

Keep each sub-issue readable alone. Link the parent for shared context rather than restating it, but
state enough that a reader picking up only that sub-issue knows what they are building and why.

### Mechanics

```bash
# Create a new issue already parented
gh issue create --parent 183 --title "..." --body-file draft.md --milestone "..." --label enhancement

# Adopt an existing issue as a sub-issue (either direction)
gh issue edit 190 --parent 183
gh issue edit 183 --add-sub-issue 190,191

# Detach
gh issue edit 190 --remove-parent
gh issue edit 183 --remove-sub-issue 190
```

Requires `gh` 2.94.0 or later.

### Plan docs

**Each sub-issue gets an ordinary plan doc** — numbered Steps, a Verification checklist, the usual
header — named as usual (`{issue-number}-{safe-slug}-plan.md`) and added to `Quotinator.slnx`. Nothing
special applies to them.

**A parent gets a plan doc too, but shaped like a miniature `overview.md`, not like an issue plan.** A
parent has no Steps and no verification of its own — its content is the map. Mirror `overview.md`'s
structure (`checklist.md` → "Overview template") scoped to this body of work, omitting the sections
that only make sense at milestone level (tier definitions, PR merge plan):

```
# #NNN — <title>

**Status:** <Planning | In progress | Waiting for release | Released>
**GitHub issue:** #NNN
**Depends on:** <issues outside this parent, or none>

---

## Description

One paragraph: what this body of work delivers and why it is one body of work.

---

## Sub-issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#NNN](url) | Title | Planning | T1 ⬜ T2 ⬜ | [NNN-slug-plan.md](NNN-slug-plan.md) |

---

## Dependency map

#X → requires #Y; unblocks #Z — with the reason, not just the edge.

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #NNN | Title | Planning |
```

The same rules that govern `overview.md` govern this doc: **status only, never detail**. Every
requirement, finding, and tier-verification narrative belongs in the sub-issue's own plan doc — the
parent plan doc never restates it. Status uses the same four-word vocabulary, and the parent's own
Status is derived from its sub-issues: `In progress` while any is, `Waiting for release` once all are.

### overview.md

- **`overview.md` lists every sub-issue individually** in the Issue List and Order-of-operations
  tables — they are the units of work that get scheduled, verified, and closed. List the parent too,
  linking its plan doc, so the grouping is discoverable.
- **The dependency map records the sub-issues' real dependencies**, not the parent's. A downstream
  issue depends on the specific sub-issue that unblocks it, not on the parent as a whole.

### Closing

Sub-issues close individually under the normal two-gate rule (`process.md § Completing an issue`). The
parent closes only once every sub-issue is closed, with a closing comment summarising the body of work
as a whole rather than repeating each sub-issue's own findings.

---

## Relationship to the milestone workflow

The verification checklist table format, red-to-green rule, and bug-must-be-red-first requirement are defined in `process.md § Working on an issue`. This document does not repeat them — it defines what goes into the GitHub issue itself before a plan doc is created.

The closing comment format (verification table reproducing the plan doc results) is defined in `process.md § Completing an issue` and `checklist.md § Before closing an issue`.

**A "Definition of done" section's checkboxes are not just filled in once at filing time — they get ticked as the corresponding plan doc Verification table rows turn ✅, and every box must be ticked before the issue closes.** See `process.md § Completing an issue` for the `gh issue edit` mechanics.
