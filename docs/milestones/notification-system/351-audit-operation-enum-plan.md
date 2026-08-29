# #351 — `AuditOperation` is a string-constant set where the project's convention is an enum

**Status:** Planning
**GitHub issue:** #351
**Tiers required:** T1, T2
**Depends on:** none — takes `BackupDeleted` from [#349](https://github.com/DutchJaFO/Quotinator/issues/349) if that lands first

---

## Description

`AuditEntryEntity.Operation` is a bare `string` backed by twelve `const string` members on a static
`AuditOperation` class, over a `TEXT NOT NULL` column with no `CHECK` constraint. The value set is
closed — it grows only when a new *kind* of auditable action is designed — which by this project's
convention makes it an enum, and by
[ADR 008](../../architecture-decisions/008-enum-backed-columns-require-check-constraints.md) makes the
column a `SafeValue<AuditOperation?>` with a `CHECK` naming every member, as `ImportActionStatus`,
`NotificationType` and `ChangeAction` already are.

Today nothing validates that a written operation is one the application recognises. A typo persists
silently into an append-only trail whose whole purpose is being read back long after the fact.

Found while planning #349 (2026-08-29), which adds a thirteenth member into the const-string shape
because converting inside an endpoint issue would have pulled a table rebuild in with it.

---

## Next action

**Refine this plan: settle the migration's placement, then write the verification checklist and the red
tests.**

The requirements are settled in the issue. What is not settled is whether the table rebuild is written
as its own numbered migration now and folded into this milestone's end-of-milestone consolidation, or
authored directly into that consolidation — a question about this milestone's migration assessment
rather than about this issue's behaviour, and one that cannot be answered before the other migrations
this milestone adds are known.

---

## Design

The conversion itself is mechanical and follows the established `SafeValue<TEnum?>` pattern. Two things
about it are not mechanical.

**Member names must equal the values already on disk.** The current constants' names differ from their
values — `AuditOperation.Insert` is `"Inserted"`, `SoftDelete` is `"SoftDeleted"`, `Backup` is
`"BackedUp"`. The stored text is what exists in every user's database, so the enum's members take the
stored form and the C# names change, not the other way round.

**The frozen-migration boundary is the last *released* migration.** This milestone consolidates its own
migrations before release, the same way #227 and #155 squashed theirs, so a rebuild written now is not
permanent — which is what makes this affordable inside this milestone rather than deferred. The same
boundary sets the scope of the "every existing value passes the new `CHECK`" requirement: a value only
ever written by an unreleased build on this branch is in nobody's database.

---

## Steps

Not yet written — see *Next action*. The migration's placement decides whether the rebuild is one step
or part of the milestone's consolidation, and writing the steps before that is settled would mean
writing them twice.

---

## Verification checklist

Not yet written — it is written once the steps above are, and before any implementation starts.
