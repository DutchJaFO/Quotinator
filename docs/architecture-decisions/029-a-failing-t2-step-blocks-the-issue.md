# ADR 029 — A failing T2 step blocks the issue

**Status:** Accepted
**Date:** 2026-11-21
**GitHub issues:** #503, #490

---

## Context

The T2 suite (`docs/automated-testing/`) is how an issue proves, against a real container, what no unit
test can reach. An issue's T2 scope is the designated smoke set plus the documents relevant to it, and the
issue moves to *Waiting for release* only once that scope passes.

The suite had three ways to report a pass while a step was red:

- **A header that excused named steps.** A document could carry `Fully green after: #NNN`, naming steps
  that would stay red until another issue landed. It was meant to be transient. It spread to seven
  documents, four of them in the smoke set, and one had excused a step that ran nothing for two months.
- **A note that said to carry on.** An *On failure* paragraph could name a known bug and tell the reader to
  record it and continue. The step was then counted as passing.
- **A step that could not fail.** A step that ran no command, printed a value its *Expected* stated without
  asserting it, or passed or failed depending on timing, reported a pass whenever it happened to.

#490 recorded `T2 ✅` and moved to *Waiting for release* with two failing smoke steps in its scope, each
excused by a header and a carry-on note. The record was not false about what the reader had been told to
do. It was false about what the run read.

## Decision

### A T2 result is reported as it read

A step passed, or it failed. No header, note or record turns a failed step into a passed one, or into a
third state such as *known*, *expected* or *inconclusive*.

### What a failing step is

A step fails when any of these is true:

- its result does not match its *Expected*;
- it runs no command;
- it prints a value its *Expected* states, without asserting it;
- it gives different results on different runs of the same build.

The last is not a lesser kind of failure. A test whose result varies cannot be trusted either way, and the
remedy is to make it deterministic, never to accept whichever reading a run produced.

### A failing step blocks every issue whose T2 scope includes it

A red step blocks each issue whose T2 scope includes it, until the issue that owns the step is fixed. That
owner becomes a dependency of every issue it blocks, recorded in the blocked issue's plan (`Depends on:`)
and in its milestone's `overview.md` dependency map. A failing step means a bug that has to be fixed, or a
dependency that was not completed; it is never a reason to record the result as anything other than red.

An issue with a red step in its T2 scope does not reach *Waiting for release*. One already there when such a
step is found is set back, in its plan doc, in `overview.md` and in its GitHub Definition of done.

### A document that cannot be green yet stays red

A document written against the finished behaviour can have steps no current build satisfies, because the
issue that delivers that behaviour has not landed. The step stays red. It is never weakened to the
behaviour the current build produces, which would bake in the defect the owning issue exists to correct,
and it is never excused.

### Rejected

- **A transient header naming the issue that will make a step green.** It reads as a promise and is
  enforced as an exemption. Nothing removes it when the named issue slips or closes, and a reader running
  the suite cannot tell an excused red step from a regression.
- **Recording a known failure and carrying on.** The run does reach its later steps, but the result
  reported for the excused step is not the one it read, which is the defect this ADR exists to remove.

## Consequences

- A milestone cannot release while any of its issues has a red step in scope. An owning issue in another
  milestone holds this one up until it lands there; whether it moves into this milestone is decided per
  issue.
- `RepositoryStructureTests.AutomatedTestDocuments_CarryNoFullyGreenAfterHeader` and
  `AutomatedTestDocuments_NeverExcuseAFailingStep` hold the documents to this ADR, and
  `AutomatedTestingSuiteTests` proves both rules against supplied documents.
- `docs/automated-testing/README.md` defines the template and the run scopes, and points here for what a
  result is. Every other document that mentions T2 results points here rather than restating this.
