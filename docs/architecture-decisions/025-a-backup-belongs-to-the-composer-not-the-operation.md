# ADR 025 — A backup belongs to whoever composes the work, never to the operation itself

**Status:** Accepted
**Date:** 2026-10-10
**GitHub issues:** #348, #349, #423

---

## Context

Taking a backup is a secondary action. It is not part of reseeding, resetting or migrating; it is a
precaution someone takes *around* one of those, because the step about to run writes what nothing else
could restore.

Where that precaution lives was decided while #348 was built, and recorded in one place only: a comment
at `NotificationActionExecutor`'s reseed case, reading *"ReseedAsync never backs up on its own, since a
function does one thing. This caller offered the user the reseed, so it composes the steps."* Deriving
the rule from there meant reading three call paths and inferring the principle behind them.

That cost was paid during #423. The seed gate moved, and the startup content load stopped happening on a
database the run did not create. Reading `ReseedAsync` and `POST /api/v1/admin/database/reseed` as
unguarded, the assistant reported a hole in #348's protection and proposed moving a backup into
`ReseedAsync`. That would have put a secondary action inside a primitive and taken a second backup on
the notification path, where one is already composed. The proposal was wrong because the rule was
nowhere a reader would find it, not because the code was unclear about what it did.

## Decision

**A backup is placed by who is composing the work, in three cases, and nowhere else.**

**An endpoint never takes one.** A backup is unrelated to an endpoint's own job, so bundling one is the
second, independent decision `CLAUDE.md`'s *Endpoint side-effect policy* already forbids: the caller
asked for the operation, not for a data-retention decision made on their behalf. `POST
/api/v1/admin/database/reseed` calling `ReseedAsync` with no backup is correct and must stay that way.
An API caller that wants a restore point composes one itself, through
`POST /api/v1/admin/backups/create`.

**A UX action composes one, step by step as it completes them.** A flow offered to a user in the
application is a composition, and the backup is one of its steps. It is offered as an explicit choice
rather than performed silently, the refusal comes before anything is written, and a user who declines
has that recorded: `NotificationActionExecutor.PrepareReseedAsync` is the worked example, offering
back-up-then-reseed, remove-oldest-then-reseed, or reseed-without-backup with an
`AuditOperation.BackupSkipped` entry naming the obstacle they accepted.

**Startup takes its own, for the work it performs unattended.** A pending migration and a content load
into a database this run created have no user composing them, so the machinery performing the step takes
the backup and refuses the step when it cannot
(`ApplyMigrationsAsync`, `RunInitialisedHookAsync`, reported through `BackupGuardedStep`). `ResetAsync`
is the same case under operator control rather than at startup: it drops every table, so it takes its
own backup and `allowNoBackup` is the operator accepting responsibility for going without.

**A function that performs one operation does not back up.** `ReseedAsync` is the canonical primitive:
it reseeds, and that is all.

## Consequences

An operation reachable both from an endpoint and from a UX flow backs up on one path and not the other.
That asymmetry is the decision working, not a gap, and must not be "fixed" by pushing the backup down
into the shared operation.

A new destructive operation answers one question to place its backup: is there a composer? A user-facing
flow composes it; an unattended startup step takes its own; a bare endpoint or a primitive takes none.

Removing a backup from a path that should have one is a real defect, and this ADR is what distinguishes
that from the asymmetry above. The distinguishing test is whether anybody else in that path is in a
position to take it.

#423 is the recorded instance of the cost of leaving this undocumented, and the reason this ADR exists
rather than a fourth code comment.
