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

## Relationship to the milestone workflow

The verification checklist table format, red-to-green rule, and bug-must-be-red-first requirement are defined in `process.md § Working on an issue`. This document does not repeat them — it defines what goes into the GitHub issue itself before a plan doc is created.

The closing comment format (verification table reproducing the plan doc results) is defined in `process.md § Completing an issue` and `checklist.md § Before closing an issue`.

**A "Definition of done" section's checkboxes are not just filled in once at filing time — they get ticked as the corresponding plan doc Verification table rows turn ✅, and every box must be ticked before the issue closes.** See `process.md § Completing an issue` for the `gh issue edit` mechanics.
