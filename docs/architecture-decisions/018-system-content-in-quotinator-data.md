# ADR 018 — System-level content belongs in Quotinator.Data

**Status:** Accepted
**Date:** 2026-08-12
**GitHub issue:** Notification system milestone (#14)

---

## Context

ADR 004 places a type in `Quotinator.Core` only if it interacts with a consumer-defined entity (`Quote`,
`Source`, `Character`, `Person`, etc.); everything else belongs in `Quotinator.Data`. That test has no
stated answer for content that is neither generic import/seeding bookkeeping nor consumer domain data,
but is still specific to what the *application itself* displays or needs to function — notifications,
changelog content, and reference/lookup data such as `Genre`. Without an explicit rule, this category
defaults to `Quotinator.Core` by habit rather than by the test ADR 004 already defines. A `Genre`-as-table
move is *not* a case of "no domain coupling," though — `Genre` is directly linked to `Quote` content via
the `QuoteGenres` join, so it stays in the main database regardless of which sub-pattern below it uses;
see "Database placement" for the distinction that actually matters.

---

## Decision

System-level content — content the application itself needs to function or display, that is not
consumer domain data — is `Quotinator.Data`-owned, per ADR 004's consumer-entity-interaction test
applied directly (no new test introduced). Two sub-patterns, by how the content is produced:

### Event-driven system content

Written by application code in response to a runtime event (a reseed completed, a schema mismatch was
detected, a version changed, a database was reset). `System_Notification` is the reference
implementation of this sub-pattern.

Each producer writes directly via the relevant `Quotinator.Data` writer interface (e.g.
`INotificationWriter`) at the call site of the triggering event, not from a separate location that
reconstructs the same information afterward. When the event is part of the generic import/seeding
machinery (`DatabaseInitializer` and its protected hooks), the write belongs alongside it — e.g. inside
a reseed's own per-file loop, not in an unrelated endpoint handler reading a snapshot report. When the
event has no relationship to import/seeding (e.g. an admin `Reset` action), the write stays at that
event's own call site instead.

### File-authored system content

Content authored as an editable file (JSON, matching the existing `data/sources/` convention) and
refreshed into a table at startup — the same shape as consumer domain seeding, applied to content the
application itself owns. `System_Changelog` (ADR 005) is the reference implementation; a future
`Genre`-as-table move would be a second consumer of the same *mechanism* — but not of its database
placement, see "Database placement" below.

The generic importer abstraction for this sub-pattern is designed by the issue implementing its first
consumer (`System_Changelog`), not by this ADR — a generic abstraction designed against one consumer
risks being wrong in ways only a second consumer reveals, the same reasoning ADR 017 applies to
`JoinQueryRepository`.

### Dependency edge: Quotinator.Data may depend on other dependency-isolated projects

`Quotinator.Data` may depend on a project that is itself dependency-isolated and carries no
Quotinator-domain types — e.g. `Quotinator.Changelog` (ADR 005), which depends only on the .NET BCL and
`Microsoft.Extensions.Logging.Abstractions`. This is narrower than "Data may depend on anything": it
permits depending only on a project that is already domain-agnostic, so `Quotinator.Data`'s own
domain-agnostic invariant (ADR 004) is preserved.

### Database placement: same database as domain content, unless nothing couples them

System-level content defaults to living in the same SQLite database as Quotinator's own domain content
(`quotinatordata.db`). A separate database is used instead when a content type has **no transactional
coupling** to domain data and **no relational link** to it — both conditions, not either:

- **Event-driven system content stays in the main database** when its trigger is part of the same
  operation that writes domain data — e.g. a notification written from inside a reseed's per-file loop
  needs to be in the same transaction/connection as the `Import_Batch`/`Import_Action` rows that loop
  also writes. `System_Notification` is the reference example.
- **A future `Genre`-as-table move also stays in the main database**, despite being file-authored system
  content — `Genre` is relationally linked to `Quote` via the `QuoteGenres` join, so the "no relational
  link" condition fails even though nothing writes it transactionally the way notifications are written.
  Domain coupling of either kind (transactional *or* relational) is enough to keep content in the main
  database.
- **File-authored system content moves to a separate database** only when it has no relationship to
  domain data at all — no foreign keys into quote/source/character tables, no joins, no shared
  transactions. `System_Changelog` is the reference example: authored externally, read-only at runtime,
  never linked to quote content in either direction. Keeping it separate keeps the main database free of
  content outside its own domain and lets the two be managed independently.

A separate database still uses the same generic `Quotinator.Data` infrastructure
(`AggregateRepository`, `JoinQueryRepository`/`IJoinStrategy`, migrations, etc.) — "separate database" is
a connection-factory and registration concern, not a reason to duplicate or skip infrastructure.

### Migration discipline

**Every database gets the same migration capability, without exception — including a separate database
introduced under "Database placement" above.** A content type having no *current* reason to change its
schema is not the same as it never needing to — the same reasoning CLAUDE.md's Schema migration policy
already applies project-wide. "Separate database" and "no transactional coupling to domain writes" are
reasons to isolate *where* content lives, never reasons to isolate it from the ability to evolve safely.

A database whose content is always fully regenerated from its own authored source, with nothing
persisted across restarts, may reasonably choose to **always initialize from a fresh baseline in
production** rather than ever replaying incremental migrations against real on-disk state — that is an
*operational* default a specific database's implementing issue can choose, not a reason to skip building
the incremental-migration path, baseline/incremental parity tests, or ADR 009 verification. The
capability must exist and stay correct (tested the same way the main database's does) even if, in
practice, a given deployment mode never exercises it — e.g. so a later persistent-storage variant of
that same database, or ordinary schema evolution during development, has a real path to build on instead
of retrofitting migration discipline after the fact.

---

## Consequences

- Event-driven notification producers whose trigger is part of the import/seeding machinery write from
  inside that machinery, not from a separate post-hoc call site.
- `System_Changelog` lives in its own database, separate from `quotinatordata.db`, with its own full
  migration capability — its implementing issue owns the concrete system-content-importer design, that
  database's own connection/initialization approach, and its own operational default for when
  migrations actually replay in production versus baseline-only.
- A future `Genre`-as-table move follows the file-authored *mechanism* but stays in the main database —
  its relational link to `Quote` via `QuoteGenres` means it never qualifies for separate-database
  placement the way `System_Changelog` does.
- `Quotinator.Data` gains a dependency on `Quotinator.Changelog` once `System_Changelog` is implemented —
  the first project reference `Quotinator.Data` has taken beyond the BCL/NuGet packages ADR 003/004
  already permit.
