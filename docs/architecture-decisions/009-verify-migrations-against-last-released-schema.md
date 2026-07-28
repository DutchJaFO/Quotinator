# ADR 009 — Migrations must be verified against the last published release's schema

**Status:** Accepted
**Date:** 2026-07-07
**GitHub issues:** #154, #155

---

## Context

Every migration in an active milestone accumulates on top of whatever schema state the
developer's local database already happens to be in. That local database has typically been
upgraded incrementally across the *entire* milestone's development — including intermediate
states that were written, applied, and sometimes edited before the milestone's own work was
finished. This project's own history already shows this happening once: `bcb59fb fix [#56]:
correct in-place edit of already-applied System_ImportConflicts migration`, inside the very
milestone (`Data Import & Sources`) this ADR was written during.

The existing schema-drift tests (`DataOwnedBaseline_And_IncrementalReplay_Produce...`,
`Baseline_And_IncrementalReplay_Produce...`) only prove one thing: replaying every migration
*from an empty database* produces the same result as the fresh-database baseline SQL. They do
**not** prove that the incremental migration path behaves correctly starting from a database that
actually matches a real, previously-shipped release — because no test or fixture in this project
starts from such a snapshot. A developer's long-lived local database is not that snapshot either:
by the time a milestone finishes, it may have passed through migration states that were themselves
later edited or abandoned, none of which a real user's database — upgrading directly from the last
actual release — would ever have passed through.

Per this project's own governing principle (see CLAUDE.md's "Authoritative sources" section): only
an issue that has shipped inside a *published* release counts as shipped. The corollary for
migrations specifically is that the only database state a migration path needs to prove itself
against is the one a real installation would actually have — the schema as it existed at the last
published release — not whatever a development machine's database happens to look like.

---

## Decision

**Before a milestone is considered ready to close, every migration it added must be verified by
applying them, in order, against a database matching the schema of the last actual published
release** — not against the accumulated local development database, and not only against the
existing from-empty schema-drift tests (which remain necessary but are not sufficient on their
own).

This means:

1. Obtain or reconstruct a database snapshot matching the real released schema (e.g. by checking
   out the release tag and running a fresh `InitialiseAsync` against an empty file, or restoring an
   actual shipped database backup).
2. Apply every migration the in-progress milestone has added, in order, against that snapshot.
3. Confirm the result matches what the from-empty incremental-replay and from-empty baseline paths
   already produce for the same final version — i.e., no drift specific to *upgrading from a real
   prior release* that the from-empty tests wouldn't have caught.
4. Give particular attention to any migration that is known to have been edited after being applied
   locally during development (this project's own tracked example: the #56 `System_ImportConflicts`
   incident) — confirm the final on-disk migration text is what a genuine prior-release database
   would actually receive, not what an already-mutated local database silently tolerated.

This is a milestone-closing gate, tracked per-milestone as its own issue (e.g. #155 for the "Data
Import & Sources" milestone) rather than a single one-time task — each milestone accumulates its
own new migrations and needs this check freshly.

---

## Reasoning

### From-empty tests and from-release tests catch different bugs

A from-empty schema-drift test proves internal consistency: "the migrations, replayed in order,
produce the same shape as the hand-written baseline." It says nothing about whether the *first* of
those migrations is compatible with what a real installation's database actually contains today.
A migration written and tested only against a local database that has already silently absorbed an
earlier mistake (an edited-in-place migration, a manually patched column) can pass every from-empty
test while still failing — or silently corrupting data — against a genuine user's database.

### The local development database is not a substitute

A long-lived local database accumulates whatever sequence of schema changes actually happened
during development, in whatever order they were tried, including ones later reverted or corrected.
It is convenient for iterating quickly, but it is not evidence that the migration path works
correctly for the population of databases that will actually run it — every one of which starts
from the last thing they actually installed, the published release.

### This is a cheap gate to add, expensive to skip

Reconstructing a released-schema snapshot (checking out a tag, running a fresh initialise) costs
minutes. Discovering an incompatibility after a release ships — against real user databases with no
easy rollback — costs substantially more, and is exactly the class of bug the from-empty tests are
structurally unable to catch.

---

## Consequences

- Every milestone must include a migration-review step before close, verifying the full
  incremental path from the last published release's schema — tracked as its own issue per
  milestone (not folded into whichever feature issue happens to touch migrations last).
- This does not replace the existing from-empty schema-drift tests — both are required; they catch
  different classes of error.
- `docs/database-conventions.md` references this ADR under "Migrations" as the do/don't summary;
  this ADR is the authoritative reasoning.
- Consider, as a follow-on (not decided here), whether a checked-in released-schema snapshot or a
  reconstructable script should become a permanent, automated test fixture rather than a manual
  per-milestone step — see #155 for where that decision will actually be made.
