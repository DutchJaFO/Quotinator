# ADR 004 — Quotinator.Data project boundaries and design intent

**Status:** Superseded in part by ADR 004-A (Engine layer)  
**Date:** 2026-06-25  
**GitHub issue:** #115  
**Updated:** 2026-06-28 (issue #121 — introduced `Quotinator.Engine`; revised dependency direction)

---

## Context

`Quotinator.Data` was created as infrastructure for SQLite access (repositories, unit of work, connection factory, type handlers). Over time it has grown, and the boundary between what belongs in `Quotinator.Core` and what belongs in `Quotinator.Data` was left implicit. Issue #115 made this boundary explicit by moving all Dapper-dependent code out of Core into Data.

During that work, a broader question arose: where do the *interfaces* and *domain models for database operations* live? Two philosophies were considered:

- **Option A — Core owns abstractions:** `Quotinator.Core` defines all interfaces (including `IDatabaseInitializer`, `IImportBatchRepository`) and `Quotinator.Data` provides implementations. Core is the boundary.
- **Option B — Data owns infrastructure abstractions:** Interfaces that abstract database-layer behaviour belong in the layer they abstract. Core defines only domain service contracts; Data defines its own interfaces.

A secondary concern: databases frequently need import and export tooling. Conflict resolution, manifest policies, and batch import models are not domain concepts — they are reusable infrastructure. Placing them in Core forces any future project using `Quotinator.Data` to carry Quotinator-specific domain coupling.

---

## Decision

**Option B — Data owns its infrastructure abstractions.**

`Quotinator.Data` is established as a **generic, reusable data-access and import/export infrastructure library**. It is not a Quotinator-specific persistence adapter.

### What belongs in `Quotinator.Data`

- Database initialisation, schema migration, and seeding infrastructure
- Interfaces that abstract database behaviour (`IDatabaseInitializer`, `IImportBatchRepository`)
- Import/export infrastructure: conflict resolution strategies (`DuplicateResolutionPolicy`), per-entity policy configuration (`ManifestPolicy`), import result models (`SeedDuplicateRecord`, `SeedPreviewResult`, `SeedBatch`)
- Dapper entities (classes decorated with `Dapper.Contrib` attributes)
- Type handlers, repositories, connection factory, unit of work
- Enums that are scoped to the data/import layer (`ImportBatchType`)

### What belongs in `Quotinator.Core`

- Domain service interfaces (`IQuoteService`)
- Domain models and API response DTOs (`Quote`, `QuoteResponse`, `QuoteTranslation`)
- Domain enums surfaced in API responses or service method signatures (`QuoteType`, `Genre`)
- SQL query string constants (`Sql.cs`) — these are strings, not Dapper types; they remain testable from Core
- Seed/import configuration that is Quotinator-specific (source file paths, data directory layout)

### Invariant

`Quotinator.Data` must never reference `Quotinator.Core`. The dependency flows one way: Core → Data. Any type that would require Data to reference Core must either move to Data, or remain in Core.

### Conflict resolution is pluggable infrastructure

Import pipelines in many applications need to handle duplicate records. `DuplicateResolutionPolicy` (skip, overwrite) and `ManifestPolicy` (per-entity-type policy) are pre-built strategy implementations that live in `Quotinator.Data`. Callers select a strategy via configuration; the Data layer executes it. New strategies can be added to Data without touching Core.

---

## Consequences

- ADR 003 design goals remain in force and are not superseded — this ADR adds the project boundary rules that ADR 003 did not address.
- Issue #115 moves the following from `Quotinator.Core` to `Quotinator.Data`: `DatabaseInitializer`, `DapperConfiguration`, `IDatabaseInitializer`, `DuplicateResolutionPolicy`, `ManifestPolicy`, `SeedDuplicateRecord`, `SeedPreviewResult`, `SeedBatch`.
- When adding new data-manipulation code in future: check whether it belongs in Data (generic infrastructure) or Engine (Quotinator-domain SQLite implementation) first.
- `DataPaths.cs` (path constants for the data directory) remains in Data — these are infrastructure constants used by `DatabaseInitializer`.

---

## Revision — issue #121 introduced `Quotinator.Engine`

The original decision ("Core → Data") turned out to be incomplete. `Quotinator.Data` must be **domain-agnostic** — a generic, reusable library. This ruled out placing domain entity classes (`Source`, `QuoteEntity`, etc.) and `SqliteQuoteService` in Data, since they carry Quotinator-specific types (`Genre`, `QuoteType`, `IQuoteService`).

The revised architecture introduces **`Quotinator.Engine`** as a third project:

```
Quotinator.Constants  ←  Quotinator.Core  ←  Quotinator.Engine  ←  Quotinator.Api
                                                      ↑
                          Quotinator.Data  ←──────────┘
```

### Revised boundary rules

| What | Where | Rule |
|------|-------|------|
| Domain service interfaces (`IQuoteService`) | Core | Core defines the contract |
| Domain models and DTOs (`QuoteResponse`, `QuoteTranslation`) | Core | Core owns domain models |
| Domain enums (`QuoteType`, `Genre`) | Core | Surfaced in service signatures; must not drag in Data types |
| Domain import models (`SourceQuote`) | Core | Used by `QuoteService` (flat-file implementation) in Core |
| Generic DB infrastructure (repositories, UoW, migrations base, type handlers) | Data | Domain-agnostic; no Core reference |
| Generic import infrastructure (`SeedBatch`, `ManifestPolicy`) | Data | Reusable across future projects |
| Quotinator-domain DB entities (`Source`, `QuoteEntity`, etc.) | Engine | Reference both Core types and Data infrastructure |
| Quotinator-specific migrations and seeding | Engine | `QuotinatorDatabaseInitializer` extends `DatabaseInitializer` |
| Quotinator-specific Dapper handler registration | Engine | `QuotinatorDapperConfiguration` extends `DatabaseConfiguration` |
| SQLite implementation of `IQuoteService` | Engine | `SqliteQuoteService` — bridges Core and Data |
| DI wiring | Api | `Program.cs` registers Engine types; no Dapper or SQLite in Api |

### Invariants

- `Quotinator.Core` must have zero Dapper, zero SQLite, zero Data project references.
- `Quotinator.Data` must have zero Core project references and zero Quotinator-domain types.
- `Quotinator.Engine` may reference both Core and Data. It is the only project with this privilege.
- `Quotinator.Api` wires everything via DI. It may reference Engine (to register types) but must not contain business logic.
