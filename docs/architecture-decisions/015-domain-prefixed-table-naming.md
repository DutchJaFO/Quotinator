# ADR 015 — Domain-prefixed table naming: a namespace substitute for SQLite's lack of schema qualification

**Status:** Accepted
**Date:** 2026-08-01
**GitHub issues:** #227, #254, #309

---

## Context

SQLite has no schema-qualification mechanism — no `dbo.Table` (SQL Server) or `schema.table`
(PostgreSQL) equivalent. Every table lives in one flat namespace, so a prefix is the only available
substitute.

The project used a `System_` prefix informally from #141 onward without writing it down as a rule, and
it drifted: `System_` covered three unrelated concerns at once (the audit trail, the import mechanism's
bookkeeping, and residual infrastructure); `ImportBatch`'s class lived in `Quotinator.Data` while its
table was created and version-tracked by `Quotinator.Core`; and the consuming project's own domain
tables carried no prefix at all, giving a reader no signal which tables were Quotinator's versus generic
reusable infrastructure. #227 raised the inconsistency directly and proposed a single `Import_` prefix.
This ADR generalises that into the missing rule.

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

### A prefix names a domain — intent and ownership — never a database

Which database a table lives in is expressed structurally: the keyed `IDbConnectionFactory` a query
executes against, the initializer that creates the table, and the nested `Sql` class the query lives in.
It is never encoded in the table name.

A domain may occupy its own database, and one database may hold several domains. A domain's prefix does
not change when its tables move between databases.

### Standard domains for `Quotinator.Data` (the reusable library)

`Quotinator.Data` defines exactly three standard domain prefixes for its own tables:

| Domain | Covers | Tables today |
|---|---|---|
| `Import_` | Everything related to the import mechanism | `Import_Batch`, `Import_Action`, `Import_Conflict`, `Import_SourceFileOverride`, `Import_FileResource`, `Import_FileResourceLine` |
| `Audit_` | Everything related to the audit trail | `Audit_Entry`, `Audit_ChangeLogEntry` |
| `System_` | Everything else — true residual/generic infrastructure | `System_SchemaVersion`, `System_ConsumerSchemaVersion`, `System_Notification`, `System_AppVersion` |

**`ImportBatch` is fully `Quotinator.Data`-owned, table `Import_Batch`.** Its table is created by
`Quotinator.Data`'s `DataOwnedMigrations` and version-tracked in `System_SchemaVersion`, matching where
its class and enums live — not by the consumer's migration list and `System_ConsumerSchemaVersion`.

### A consuming application defines a domain per distinct concern it owns

Not one per project, and not exactly one per application. Quotinator's domains:

| Domain | Covers | Database |
|---|---|---|
| `Quotinator_` | Quote content and its masterdata — quotes, sources, characters, people, series, universes, conversations, and their translations | main |
| `Changelog_` | Changelog content — releases (`Changelog_Entry`), their line items (`Changelog_Line`), and that content's own schema version (`Changelog_SchemaVersion`) | changelog |

`Changelog_Entry`, not `Changelog_Changelog`: the prefix already says changelog, so the table part names
what one row is.

`Quotinator.Data`'s three domains above are unchanged and remain the library's own; a consumer does not
add to them. Adding a domain means adding a row to this table.

Every table `Quotinator.Core` owns carries `Quotinator_`: `Quotinator_Quote`, `Quotinator_Source`,
`Quotinator_Character`, `Quotinator_Person`, `Quotinator_Series`, `Quotinator_Universe`,
`Quotinator_Conversation`, `Quotinator_StageDirection`, `Quotinator_SoundCue`,
`Quotinator_ConversationLine`, `Quotinator_CharacterSource`, `Quotinator_QuoteTranslation`,
`Quotinator_SourceTranslation`, `Quotinator_CharacterTranslation`,
`Quotinator_StageDirectionTranslation`, `Quotinator_SoundCueTranslation`, `Quotinator_QuoteGenre`.

### Third-party expectations

A third party building their own consumer of `Quotinator.Data` is **not required** to adopt this
convention, use a matching single-prefix scheme, or mirror `Quotinator_`'s naming for their own domain
tables — this rule binds `Quotinator.Data`'s own tables (`Import_`/`Audit_`/`System_`) and the
Quotinator project's own tables (`Quotinator_`/`Changelog_`) specifically. It is a convention this
project follows and documents for its own clarity, not a constraint `Quotinator.Data` enforces on
anyone else's schema.

---

## Consequences

**A migration is frozen once any database has actually run it — not once it reaches a tagged release.**
"Unreleased" is not the test. A developer's own local database routinely runs an unreleased migration
long before it reaches a tag, and editing or squashing that migration afterward leaves it recorded as
"up to date" under a smaller migration count, so the edited work never runs there at all. Treat a
migration as frozen the first time it executes against any real database, including a developer's own.

This applies with extra force to **Data-owned** migrations: `System_SchemaVersion` is never wiped or
replayed by a Reset (see `DatabaseInitializer.DropAndRebuildAsync`), so a database stranded by an edited
Data-owned migration has no supported recovery path. Collapsing several migrations into one remains
correct only for migrations that are still purely theoretical — written in a plan doc, never executed
anywhere.

**One `Sql.cs` spans both databases**, so `Changelog_Entry` and `System_Notification` read as siblings
there. A consequence to accept rather than design around; if it becomes confusing, split `Sql.cs`.

**No mechanical guard checks table prefixes.** `SqlIdCaseGuard` and its siblings cover column-level
conventions only, so this rule is held by review.
