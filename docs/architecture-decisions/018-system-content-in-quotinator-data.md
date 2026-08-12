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
defaults to `Quotinator.Core` by habit rather than by the test ADR 004 already defines.

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
refreshed into a `System_*` table at startup — the same shape as consumer domain seeding, applied to
content the application itself owns. `System_Changelog` (ADR 005) is the reference implementation; a
future `Genre`-as-table move is a second consumer of the same pattern.

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

### Migration discipline

Every new `System_*` table follows CLAUDE.md's Schema migration policy exactly — its own migration,
matching baseline SQL, and schema-drift test extension, in the same commit. No exception exists for
system-level tables.

---

## Consequences

- Event-driven notification producers whose trigger is part of the import/seeding machinery write from
  inside that machinery, not from a separate post-hoc call site.
- `System_Changelog`'s implementing issue owns the concrete system-content-importer design.
- A future `Genre`-as-table move follows this pattern rather than requiring its own boundary decision.
- `Quotinator.Data` gains a dependency on `Quotinator.Changelog` once `System_Changelog` is implemented —
  the first project reference `Quotinator.Data` has taken beyond the BCL/NuGet packages ADR 003/004
  already permit.
