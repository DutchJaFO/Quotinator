# ADR 014 — Audit-trail tables don't purge dangling references; a destructive Reset needs its own export step

**Status:** Accepted
**Date:** 2026-08-01
**GitHub issue:** #151

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

Today, all four audit-trail tables are excluded from the tables a Reset drops
(`Sql.Schema.GetUserTables`) and survive Reset/Reseed unconditionally — tested
(`ResetAsync_PreservesExistingImportConflictRows`,
`ResetAsync_AfterInitialise_PreservesExistingAuditEntries`) and documented (CLAUDE.md's "No
exception-based migration recovery"). #151 asked whether, given that domain entities get wiped and
reimported with new ids on Reset, these tables should **purge** (or flag) rows whose `EntityId`/
`RecordId` no longer resolves to a live row — or whether a dangling reference is the intended,
permanent shape of a historical record.

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

**A second, larger fact changes what "Reset" even means to these tables going forward.** #156
proposes that Reset stop preserving *any* table selectively and instead drop the entire database and
rebuild it in one step from the fresh-database baseline script — the same baseline a brand-new install
uses. Once #156 ships, the audit-trail tables no longer survive Reset at all: a destructive Reset
empties them completely, not just the rows whose `EntityId` happens to go stale. That is a materially
bigger loss than the "purge dangling rows" question #151 originally asked, and it means the entire
audit/history record of everything that happened before that Reset is gone unless it was captured
somewhere else first.

Reseed (`TruncateDataAsync`) is unaffected by any of this — confirmed by #156's own research, it only
ever deletes rows from named *domain* tables (Quotes, Sources, Characters, ...) and never touches a
`System_`-prefixed table at all. The dangling-reference scenario above (content change/removal across
a reimport) still applies to audit-trail rows referencing Reseed-wiped-and-reimported domain entities,
regardless of how #156 resolves — only Reset's own wholesale-loss behaviour is new.

---

## Decision

**Two separate questions, two separate answers:**

1. **Dangling `EntityId`/`RecordId` references within audit-trail rows that do survive (today's Reset,
   and Reseed either way) are never purged, flagged, or updated.** A row states a fact about what
   happened at the time it happened — that fact does not become false because the entity it names was
   later replaced with a new id or ceased to exist. This applies uniformly across all four tables. An
   existence-check/purge mechanism was considered and rejected: it would require `Quotinator.Data`
   (which owns three of the four tables) to resolve an `EntityType` string to "does this id exist in
   some domain table" — exactly the domain knowledge ADR 004 keeps out of `Quotinator.Data` — to solve
   a scenario that, per the corrected premise above, is narrow (content edits/removals across
   reimports), not the common case originally assumed.

2. **Once #156 ships, a destructive Reset will discard the entire audit trail, not just dangling rows —
   this is accepted as the correct behaviour for Reset itself, on the condition that an export path
   exists first.** Reset becomes, by design, "start over completely" (matching #156's own stated
   rationale). Losing history as a side effect of that is only acceptable if an operator who wants to
   keep it has a way to do so beforehand. This ADR does not implement that export mechanism — it
   requires its own design (target format, what "a dedicated audit-history folder" means operationally,
   whether it runs automatically before every destructive Reset or only on request) — but it establishes
   that **a destructive Reset must not ship (via #156) without an accompanying export-before-reset path**
   for the audit-trail tables. Tracked as [#249](https://github.com/DutchJaFO/Quotinator/issues/249),
   sequenced before or alongside #156's implementation.

No code changes follow from this ADR directly. It records the decision on point 1 (already
implemented, now confirmed permanent) and the design constraint on point 2 (not yet implemented,
scoped to a new issue).

---

## Consequences

**No purge/flag mechanism will be built for dangling references in a surviving audit-trail table.** A
future `System_`-prefixed table shaped as "one row, one event, one `EntityId`-shaped reference"
inherits this rule automatically.

**#156 may not ship a destructive, full-database-rebuild Reset without a working export-before-reset
path for the audit trail.** This is a hard dependency from #156's implementation onto #249, not just a
nice-to-have — recorded here so it isn't lost between the two issues.

**Tooling that reads these tables must expect dangling references regardless of the above.**
`tools/Quotinator.Tools.DbInspector` or any future admin/audit view over `System_AuditEntries`/
`System_ImportConflicts`/`System_ImportActions`/`System_ChangeLog` should treat an `EntityId` that
resolves to nothing as normal — not a bug to report.

**The premise correction is itself worth remembering.** Domain entity ids in this project are
content-derived hashes, not random UUIDs; a Reset/Reseed reimporting unchanged source content
reproduces identical ids throughout. This should be the default assumption when reasoning about any
future Reset/Reseed behaviour question.
