# ADR 015 — Domain-prefixed table naming: a namespace substitute for SQLite's lack of schema qualification

**Status:** Accepted
**Date:** 2026-08-01
**GitHub issue:** #227

---

## Context

SQLite has no schema-qualification mechanism — no `dbo.Table` (SQL Server) or `schema.table`
(PostgreSQL) equivalent. Every table lives in one flat namespace. This project has been using a
`System_` prefix as an informal substitute since #141, but the convention was never written down as a
deliberate rule, and it drifted as a result:

- `System_` currently covers three genuinely different concerns under one label: the audit/history
  tables ADR 014 calls "audit-trail tables" (`System_AuditEntries`, `System_ChangeLog`), the import
  mechanism's own bookkeeping (`System_ImportConflicts`, `System_ImportActions`,
  `System_SourceFileOverrides`), and true residual infrastructure (`System_SchemaVersion`,
  `System_ConsumerSchemaVersion`).
- `ImportBatch` — the C# class — lives in `Quotinator.Data/Entities/` alongside its own
  `ImportBatchType`/`ImportBatchStatus` enums, but its `[Table("ImportBatches")]` table is created by
  `Quotinator.Core`'s migrations and tracked via `System_ConsumerSchemaVersion` (the *consumer's* own
  counter), not `Quotinator.Data`'s `DataOwnedMigrations`. Nobody decided this split on purpose — it's
  a byproduct of `ImportBatches` predating the Data/Engine project split (#143) and never being
  revisited after.
- Every `Quotinator.Core`-owned domain table (`Quotes`, `Sources`, `Characters`, `People`, ...) carries
  no prefix at all — internally consistent in isolation, but it gives a reader no visual signal that
  these are Quotinator's own tables versus generic reusable infrastructure.

#227 raised the `System_`/`ImportBatches` naming inconsistency directly (found while building #153) and
proposed a single `Import_` prefix as a partial fix. This ADR generalizes that into the actual missing
rule: a full, deliberate domain-prefix convention, not a one-off rename.

---

## Decision

### Why prefixes exist

They act as **namespaces that separate domains within the database and signal intent** — the same job
`dbo.`/`schema.` qualification does in a server-grade RDBMS, substituted for SQLite's lack of that
feature. A reader scanning `sqlite_master` (or this project's own `Sql.cs`) should be able to tell,
from the table name alone, which subsystem owns a table and what kind of table it is, without needing
to already know the codebase.

### How they're defined

- **Table name:** `[Domain]_[TableName]`, singular. A plural name implies a single row describes
  multiple entities, which is never true of a normal table — `Quotinator_Person`, not
  `Quotinator_People`; `MyApp_Order`, not `MyApp_Orders`.
- **Class name:** the table's own name, unprefixed and singular, plus whatever suffix this project's
  class-naming convention requires — see
  [ADR 016](016-class-naming-suffixes-and-enum-placement.md) for that rule. This ADR fixes the *table*
  name only; it does not decide the C# class name.

### Standard domains for `Quotinator.Data` (the reusable library)

`Quotinator.Data` defines exactly three standard domain prefixes for its own tables:

| Domain | Covers | Tables today |
|---|---|---|
| `Import_` | Everything related to the import mechanism | `Import_Batch` (reclassified — see below), `Import_Action`, `Import_Conflict`, `Import_SourceFileOverride`, and any future import-file-provenance table (e.g. #227's own proposed `Import_FileResource`/`Import_FileResourceLine`) |
| `Audit_` | Everything related to the audit trail | `Audit_Entry`, `Audit_ChangeLogEntry` |
| `System_` | Everything else — true residual/generic infrastructure | `System_SchemaVersion`, `System_ConsumerSchemaVersion` |

**`ImportBatch` is reclassified as fully `Quotinator.Data`-owned, table `Import_Batch`.** This is not
just a rename — its table creation moves from `Quotinator.Core`'s migration list into
`Quotinator.Data`'s `DataOwnedMigrations`, and its version tracking moves from
`System_ConsumerSchemaVersion` to `System_SchemaVersion`. This resolves the class-vs-table ownership
split found in this ADR's own research, matching where the class and its enums already live.

### The consuming project's own domain

Each project that consumes `Quotinator.Data` picks exactly one top-level domain prefix for all of its
own domain tables. **Quotinator's is `Quotinator_`.** Every table `Quotinator.Core` currently owns
gets it: `Quotinator_Quote`, `Quotinator_Source`, `Quotinator_Character`, `Quotinator_Person`,
`Quotinator_Series`, `Quotinator_Universe`, `Quotinator_Conversation`, `Quotinator_StageDirection`,
`Quotinator_SoundCue`, `Quotinator_ConversationLine`, `Quotinator_CharacterSource`,
`Quotinator_QuoteTranslation`, `Quotinator_SourceTranslation`, `Quotinator_CharacterTranslation`,
`Quotinator_StageDirectionTranslation`, `Quotinator_SoundCueTranslation`, `Quotinator_QuoteGenre`.
(`People` → `Quotinator_Person`, `QuoteGenres` → `Quotinator_QuoteGenre`, etc. — every table becomes
singular under the same rule that applies to `Quotinator.Data`'s own tables.)

### Third-party expectations

A third party building their own consumer of `Quotinator.Data` is **not required** to adopt this
convention, use a matching single-prefix scheme, or mirror `Quotinator_`'s naming for their own domain
tables — this rule binds `Quotinator.Data`'s own tables (`Import_`/`Audit_`/`System_`) and the
Quotinator project's own tables (`Quotinator_`) specifically. It is a convention this project follows
and documents for its own clarity, not a constraint `Quotinator.Data` enforces on anyone else's schema.

---

## Consequences

**This is a major, database-wide refactor** — every table in the schema is renamed, every `[Table(...)]`
attribute updated, every SQL string in `Sql.cs`/`RepositorySql.cs` updated, and every entity class
renamed to match both this ADR's table names and ADR 016's class-suffix convention. Given the scale and
disruption, this is being done as the **first work item in the v1.8.0 maintenance milestone**, ahead of
every other issue already queued, rather than folded in alongside unrelated work.

**Migration squashing applies the same way it did at the end of the Data Import & Sources milestone
(#155).** The "never edit an already-applied migration" rule only protects migrations that reached a
real, tagged release — anything added since the last shipped tag is still safe to rewrite, reorder, or
squash. The implementation plan must inventory exactly which Data-owned and Consumer-owned migrations
are still unreleased as of when this work starts, and collapse the rename into the smallest number of
new migrations possible, the same way #155 collapsed eleven Consumer and twelve Data migrations down
to one each. Both baselines (`DataBaselineSql` and `QuotinatorMigrations.Baseline`) must reflect the
final domain-prefixed names directly, so a fresh install never passes through the old names at all.

**The full table-by-table and class-by-class mapping and migration-squash inventory are
implementation-planning work, not decided exhaustively here** — this ADR fixes the table-naming rule
and the standard domains; the plan doc enumerates every concrete rename (table and class) and the
exact migration sequence.

**`GetUserTables`'s Reset-exclusion pattern needs revisiting as part of the plan**, since it currently
matches only `System\_%` — once `Import_`/`Audit_` exist as separate prefixes for tables that must
keep the same Reset-exclusion behaviour `System_` tables have today (pre-#156), the pattern (or the
underlying mechanism) needs to recognise all three, not just `System_`.
