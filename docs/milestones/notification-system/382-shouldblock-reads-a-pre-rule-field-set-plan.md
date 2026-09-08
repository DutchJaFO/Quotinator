# #382 — `ShouldBlock` is evaluated against a pre-rule field set at four of five sites

**Status:** Planning
**GitHub issue:** #382
**Tiers required:** T1, T2
**Depends on:** [#377](https://github.com/DutchJaFO/Quotinator/issues/377)

**Next action: refine this plan.** One design question is open — *Open question* below — and the Steps
section is deliberately unwritten until it is answered. The measurement this issue needs (how many rows
the bundled corpus actually blocks this way) has not been taken either; per #377's own lesson, it is
taken during planning, not as a first step of execution.

---

## Description

`CompletenessGuard.ShouldBlock` decides whether a row a human has marked `Complete` blocks an import
rather than being silently changed. #168's comment at the Quote branch states the invariant it is meant
to have — *"ShouldBlock is evaluated against what would actually be WRITTEN (resolved), not the raw
incoming value"* — and that holds for the merge policies it was written for. It does not hold under
Review-with-rules.

`effectiveChanged` is computed **before** `ruleResolved` overwrites `resolved`, at four of the five
rule-consulting sites in `ImportActionPlanner.cs`: `:445` (Quote), `:1115` and `:1271` (Source's
explicit-id and natural-key branches), `:1791` (Universe), `:1977` (Series). `PlanSeasonsAsync` alone
computes it afterwards (`:2169` then `:2180`), so it is the model rather than a fifth defect.

The consequence is a real block an operator must clear by hand, for a change that would never have been
made: a `Complete` row whose only difference is a field a rule resolves back to the stored value is
staged `Blocked`.

**Found while planning #377, and deliberately kept out of it** (that issue's decision B). #377 computes
its own post-resolution field set for classification and leaves `ShouldBlock`'s input untouched, so it
changes no blocking behaviour — the two issues touch adjacent lines and nothing else.

**It should have been filed when it was found, not when #377 reached the step that filed it.**
`process.md` requires a defect found mid-milestone to be filed immediately; scheduling it as #377's
eighth step left it living only as prose in another issue's plan across that issue's whole
implementation. Recorded because the rule was read and then not followed.

---

## Open question

**Does correcting this change which rows are blocked, and is that acceptable on its own terms?**

By construction it must: the point is that a row blocked over a field a rule resolves away stops being
blocked. That is the fix. But `CompletenessGuard` exists to stop a human's completed review being
silently overwritten, so *narrowing* it is a change to a protection mechanism and needs to be a decision
rather than a side effect — which is exactly why #377 refused to take it in passing.

Two shapes, and the measurement below should inform the choice rather than the other way round:

| | What it means |
|---|---|
| **A** | Move `ShouldBlock` onto the post-resolution field set at all four sites, matching Season. The invariant #168 states becomes true everywhere. A `Complete` row is blocked only when something would actually be written to it. |
| **B** | Keep blocking on the pre-rule set deliberately, and correct #168's comment instead to say so — on the reasoning that a `Complete` row is worth pausing on whenever an import *disagrees* with it, whether or not a rule would resolve the disagreement away. |

**Neither is obviously right, and the argument for B is not weak**: a rule silently resolving a conflict
on a row a human marked `Complete` is arguably the case the guard most exists for. A is the invariant as
written; B is the invariant as perhaps intended.

---

## Measurement — not yet taken

Per #377's lesson (a plan resting on a prediction is a false plan), this issue does not get Steps until
the following is measured against the real bundled corpus, during planning:

1. How many actions are staged `Blocked` today whose resolved payload equals the stored one — the rows
   option A would unblock.
2. Whether any of them are `Complete` rows a human actually reviewed, as opposed to rows carrying the
   default `Incomplete`/`NeedsReview` status, which changes how much option B is protecting.

A temporary diagnostic test, deleted once its output is recorded here — #374's step 1 and #377's own
*Measurement* section are the worked examples.

---

## Steps

**Not written — blocked on the open question above.** Writing them now would mean choosing A or B by
implication, which is the developer's decision, not this document's.

---

## Verification checklist

**Not written — see Steps.** The pair it will need is already clear, and is recorded here so it is not
lost: a `Complete` row whose rule resolves to the stored value, and a `Complete` row whose rule resolves
to a different value and must still block. Season needs its own pair as the site that is already
correct, guarding against a change that "makes every branch consistent" by breaking the one that was
right — the same control #377's row 6 needed.
