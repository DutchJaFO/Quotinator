# Each bundled file still matches what its own converter produces

**Smoke:** no
**Environment:** Fresh
**Deterministic:** no
**Traces to:** #400

## Preconditions

Nothing beyond the profile yet. The comparison this document exists for is reached through the source refresh, and
`Quotinator:AutoUpdateSources` is off by default (#424), so a container seeds the checked-in copy; the step #400 delivers
will have to turn the refresh on for its own run.

**This document reads bundled content**, which the index's second rule otherwise reserves for
[`14`](14-fresh-seed-produces-zero-pending-actions.md). The bundled files are a moving target, so no repeatable test rests
on them; they are read only to verify that they still work. Doc 14 warns when upstream content makes the import fail or
a rule stale; this document warns when a bundled file no longer matches what its converter makes of the upstream source.
It was doc 14's step 5 until #503 (decision H): it is its own test, and no step of doc 14 used its result.

## Determinism

**What varies:** the upstream sources. This is the only place in the project allowed to go red because the outside world
changed rather than because this project broke (developer rule, 2026-09-08). Its `Deterministic:` field is settled when
[#400](https://github.com/DutchJaFO/Quotinator/issues/400) gives the step a command.

## Steps

### 1. Confirm each bundled file still matches what its own converter produces

**There is nothing to run here until [#400](https://github.com/DutchJaFO/Quotinator/issues/400) delivers this step.**
What it was first written to run could not run for anybody: it looped over a manifest property that does not exist, and
called a `--convert` option the application does not have. #400 item 6 replaces it with a runnable step, reached through
the source refresh.

**Expected:** a command comparing each bundled file with what its converter produces, and every file matching.
There is no such command, so this step fails
([ADR 029](../../architecture-decisions/029-a-failing-t2-step-blocks-the-issue.md): a step that runs no command
fails). The issue that owns the missing command is [#400](https://github.com/DutchJaFO/Quotinator/issues/400).

**On failure:** stop.

## Observed effect

Not yet measured: the step runs no command, so no bundled file has been compared with its converter.

## Canary

**This step has no canary**: it runs no command, so it fails on every build until #400 gives it one, and the canary is
written with that command.

## Cleanup

Nothing to clean up: no step creates anything yet.
