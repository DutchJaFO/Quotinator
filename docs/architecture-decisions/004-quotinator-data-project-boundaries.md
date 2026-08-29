# ADR 004 — Quotinator.Data project boundaries and design intent

**Status:** Accepted  
**Date:** 2026-06-25  
**GitHub issues:** #115, #121, #157, #158, #206

---

## Context

`Quotinator.Data` was created as infrastructure for SQLite access (repositories, unit of work,
connection factory, type handlers). Over time it grew, and the boundary between what belongs in
`Quotinator.Core` and what belongs in `Quotinator.Data` was left implicit.

The question was where the *interfaces* and *domain models for database operations* live. Two
philosophies were considered:

- **Option A — Core owns abstractions:** `Quotinator.Core` defines all interfaces (including
  `IDatabaseInitializer`, `IImportBatchRepository`) and `Quotinator.Data` provides implementations.
  Core is the boundary.
- **Option B — Data owns infrastructure abstractions:** interfaces that abstract database-layer
  behaviour belong in the layer they abstract. Core defines only domain service contracts; Data
  defines its own interfaces.

A secondary concern: databases frequently need import and export tooling. Conflict resolution,
manifest policies, and batch import models are not domain concepts — they are reusable infrastructure.
Placing them in Core forces any future project using `Quotinator.Data` to carry Quotinator-specific
domain coupling.

---

## Decision

**Option B — Data owns its infrastructure abstractions.**

`Quotinator.Data` is a **generic, reusable data-access and import/export infrastructure library**. It
is not a Quotinator-specific persistence adapter.

```
Quotinator.Constants  ←  Quotinator.Core  ←  Quotinator.Api
                                 ↓
                         Quotinator.Data
```

### The governing test: does it interact with a consumer-defined entity?

A type belongs in `Quotinator.Core` only if it needs to interact with an entity the *consumer* defines
— `Quote`, `Source`, `Character`, `Person`, `Conversation`, `StageDirection`, `SoundCue`, or any
Quotinator-domain enum (`QuoteType`, `Genre`). "Interact with" means referencing the type directly,
joining against its table, or containing business rules that only make sense in terms of it.

Everything else — including seeding and the import/batch-tracking feature as a whole — is generic
infrastructure usable by any future consumer with its own schema, and belongs in `Quotinator.Data`,
regardless of how "import-flavoured" or "seeding-flavoured" it superficially looks. `ImportBatch`
bookkeeping (which batch, when, by what policy, how many records, current lifecycle status) never names
a consumer entity, so it belongs in Data. `ImportActionPlanner` and `SqliteQuoteImportService` exist
specifically to plan and write `Quote`/`Source`/`Character`/`Person`/`Conversation` rows, so they
belong in Core.

**Apply this test before looking at where a related or superficially-similar type already lives.** The
placement table below is a set of worked examples of applying the test, never the source of truth
itself — pattern-matching a neighbouring type's location instead of re-deriving the rule is how
`ImportBatchType` and `IImportBatchRepository` sat in the wrong project undetected, and how
`Sql.ImportBatches` was then moved the wrong way for the same reason.

### Placement

| What | Where | Rule |
|------|-------|------|
| Domain service interfaces (`IQuoteService`) | Core | Core defines the contract |
| Domain models and DTOs (`QuoteResponse`, masterdata response DTOs, `MasterDataReference`) | Core | Core owns domain models — one canonical location, no Core/Api split |
| Domain enums (`QuoteType`, `Genre`) | Core | Surfaced in service signatures |
| Domain import models (`SourceQuote`) | Core | Used by the legacy in-memory `QuoteService` |
| Quotinator-domain DB entities (`SourceEntity`, `QuoteEntity`, etc.) | Core | Reference both domain types and Data infrastructure directly |
| Quotinator-specific migrations and seeding | Core | `QuotinatorDatabaseInitializer` extends `DatabaseInitializer` |
| Quotinator-specific Dapper handler registration | Core | `QuotinatorDapperConfiguration` extends `DatabaseConfiguration` |
| SQLite implementation of `IQuoteService` | Core | `SqliteQuoteService` |
| Quotinator-domain SQL | Core | `Quotinator.Core.Queries.Sql` |
| Generic DB infrastructure (repositories, UoW, migrations base, type handlers, connection factory) | Data | Domain-agnostic; no Core reference |
| Generic import infrastructure (`SeedBatch`, `ManifestPolicy`, `ImportBatch` bookkeeping) | Data | Reusable across future projects |
| Interfaces abstracting database behaviour (`IDatabaseInitializer`, `IImportBatchRepository`) | Data | The abstraction belongs in the layer it abstracts |
| Generic infrastructure SQL | Data | `Quotinator.Data.Queries.Sql` |
| `DataPaths` | Data | Infrastructure constants used by `DatabaseInitializer` |
| DI wiring | Api | `Program.cs` registers Core types; no Dapper or SQLite in Api |

### SQL follows the same split as everything else

A SQL query string is not exempt because it is "just a string, not a Dapper type" — a query's
`FROM`/`JOIN` clauses hardcode a domain schema shape just as much as a Dapper entity class does, even
without a compile-time type reference. A query naming a Quotinator-domain table belongs in
`Quotinator.Core/Queries/Sql.cs`; a query touching only generic infrastructure or the tables
`Quotinator.Data` owns outright (`Audit_Entry`, `Import_Action`, `Audit_ChangeLogEntry`,
`Import_Batch`, `Schema`, `Joins`) stays in `Quotinator.Data/Queries/Sql.cs`.

This applies equally to any *code that assembles or executes* domain-specific SQL, not only the string
constants — a shared base-class method in `Quotinator.Data` that calls `Sql.Quotes.DeleteAll` belongs
in `QuotinatorDatabaseInitializer` instead.

### Conflict resolution is pluggable infrastructure

Import pipelines in many applications need to handle duplicate records. `DuplicateResolutionPolicy`
(skip, overwrite) and `ManifestPolicy` (per-entity-type policy) are pre-built strategy implementations
that live in `Quotinator.Data`. Callers select a strategy via configuration; the Data layer executes
it. New strategies can be added to Data without touching Core.

### Invariants

- `Quotinator.Data` must have zero `Quotinator.Core` project references and zero Quotinator-domain
  types. This is the invariant the whole ADR rests on.
- `Quotinator.Core` may reference `Quotinator.Data` directly.
- `Quotinator.Api` wires everything via DI. It may reference Core to register types, but must not
  contain business logic, Dapper, or SQLite.

---

## Consequences

- ADR 003's design goals remain in force and are not superseded — this ADR adds the project boundary
  rules ADR 003 did not address.
- Core carrying a direct `Quotinator.Data` dependency is deliberate. An earlier structure held Core to
  a second invariant — zero Dapper, zero SQLite, zero Data reference — which required a third project
  between them to hold domain entities and domain SQL. That invariant was retired: it manufactured a
  boundary with no justification once Data's own domain-agnostic invariant is honoured on its own, and
  it forced types that needed both a Core-owned domain type and a Data-owned type into `Quotinator.Api`
  where neither belonged.
- When adding new data-manipulation code, apply the consumer-entity-interaction test first, then check
  the placement table as a worked example.
