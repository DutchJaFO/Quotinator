# ADR 014 — Audit-trail tables don't purge dangling references; a destructive Reset needs its own export step

**Status:** Accepted
**Date:** 2026-08-01
**GitHub issues:** #151, #156, #249, #482

---

## Context

Four `System_`-prefixed tables each record an event about a domain entity via an `EntityId`/
`RecordId` column — `System_AuditEntries.RecordId`, `System_ImportConflicts.EntityId`,
`System_ImportActions.EntityId`, `System_ChangeLog.EntityId`. This ADR calls them the **audit-trail
tables** collectively. (Not "provenance tables" — this codebase already uses "provenance" for a
distinct, established meaning: where a row's *content* came from, tracked via `ImportBatch` and its
`Url`/`github` fields. Reusing it here for "tables that record what happened to a row" would recreate
the exact kind of term collision `docs/milestones/data-import-sources/58-import-batches-schema-plan.md`
already had to correct once, for "System" meaning two different things.)

#151 asked whether, given that domain entities can be replaced by a reimport, these tables should
**purge** (or flag) rows whose `EntityId`/`RecordId` no longer resolves to a live row — or whether a
dangling reference is the intended, permanent shape of a historical record.

**The issue's own framing needed correcting first.** It assumed a Reset/Reseed reimport always
assigns "brand-new UUIDs," so every pre-existing audit-trail row would go stale. That isn't how entity
ids work here: every domain entity id is a *deterministic* hash of normalised content or a natural
key, not a random UUID — `QuoteIdentity.StableId` (SHA-256 of normalised `quote|source` text) and
`EntityIdentity.SourceId`/`CharacterId`/`PersonId`/`SeriesId`/`UniverseId` (SHA-256 of normalised
natural-key parts). Reimporting the same source content after a wipe reproduces the *same* id it had
before — the reference stays valid. A dangling reference only actually arises when an entity's content
changes or is removed between the audit-trail row being written and the next reimport, or a correction
entry rewrites content without preserving its `id`.

`System_ImportActions` was missing from #151's own "Scope" section (which named only
`System_AuditEntries`, `System_ImportConflicts`, `System_ChangeLog`) despite having the identical
shape. Confirmed as an omission, not a deliberate exclusion, and folded into this decision's scope.

**A Reset is a larger question than a dangling row.** A Reset replaces the whole database with a new
one built from the fresh-database baseline, preserving no table selectively
([ADR 027](027-a-reset-recreates-the-database.md)). The audit-trail tables do not survive it: a Reset
empties them completely, not just the rows whose `EntityId` happens to go stale, so the entire record
of everything that happened before it is gone unless it was captured somewhere else first.

Reseed does not have that effect. It imports the configured source files without a prior wipe, adding
what is missing and reconciling what disagrees, and never touches a `System_`-prefixed table. The
dangling-reference scenario above (an entity's content genuinely changing between the audit-trail row
being written and a later reimport) applies to audit-trail rows referencing domain entities a reseed
later modifies.

A Reset reimports no domain content (quotes, sources, ...) afterwards either: per `CLAUDE.md`'s
"Endpoint side-effect policy (Single Responsibility)", its one job is rebuilding the database, and
whether to keep bundled content is the operator's call. The audit trail a Reset discards is therefore
lost against an empty database, not a freshly repopulated one.

---

## Decision

**Two separate questions, two separate answers:**

1. **Dangling `EntityId`/`RecordId` references within audit-trail rows that survive (a Reseed) are
   never purged, flagged, or updated.** A row states a fact about what
   happened at the time it happened — that fact does not become false because the entity it names was
   later replaced with a new id or ceased to exist. This applies uniformly across all four tables. An
   existence-check/purge mechanism was considered and rejected: it would require `Quotinator.Data`
   (which owns three of the four tables) to resolve an `EntityType` string to "does this id exist in
   some domain table" — exactly the domain knowledge ADR 004 keeps out of `Quotinator.Data` — to solve
   a scenario that, per the corrected premise above, is narrow (content edits/removals across
   reimports), not the common case originally assumed.

2. **A Reset discards the entire audit trail, not just dangling rows — accepted as the correct
   behaviour for Reset itself, on the condition that an export path exists.** A Reset is, by design,
   "start over completely". Losing history as a side effect of that is only acceptable because an
   operator who wants to keep it can export it beforehand, through `GET /api/v1/admin/audit/export`
   ([#249](https://github.com/DutchJaFO/Quotinator/issues/249)). **The product must never carry a
   destructive Reset without that export-before-reset path.**

---

## Consequences

**No purge/flag mechanism will be built for dangling references in a surviving audit-trail table.** A
future `System_`-prefixed table shaped as "one row, one event, one `EntityId`-shaped reference"
inherits this rule automatically.

**No release may carry a destructive, full-database Reset without a working export-before-reset path
for the audit trail.** Removing or breaking the export is a defect against this ADR, not a separate
decision.

**Tooling that reads these tables must expect dangling references regardless of the above.**
`tools/Quotinator.Tools.DbInspector` or any future admin/audit view over `System_AuditEntries`/
`System_ImportConflicts`/`System_ImportActions`/`System_ChangeLog` should treat an `EntityId` that
resolves to nothing as normal — not a bug to report.

**The premise correction is itself worth remembering.** Domain entity ids in this project are
content-derived hashes, not random UUIDs; a Reset/Reseed reimporting unchanged source content
reproduces identical ids throughout. This should be the default assumption when reasoning about any
future Reset/Reseed behaviour question.
