# ADR 004 — Quotinator.Data project boundaries and design intent

**Status:** Accepted  
**Date:** 2026-06-25  
**GitHub issue:** #115

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
- Issue #115 moves the following from `Quotinator.Core` to `Quotinator.Data`: all Dapper entity classes, `DatabaseInitializer`, `SqliteImportBatchRepository`, `SqliteQuoteService`, `DapperConfiguration`, `IDatabaseInitializer`, `IImportBatchRepository`, `DuplicateResolutionPolicy`, `ManifestPolicy`, `SeedDuplicateRecord`, `SeedPreviewResult`, `SeedBatch`, `ImportBatchType`.
- When adding new data-manipulation code to Core in future: check whether it belongs in Data first. If it uses Dapper, references an entity, or is a reusable import/export concern, it belongs in Data.
- `DataPaths.cs` (path constants for the data directory) is a decision point: the database/backup constants belong in Data, but `DataProtectionFolder` is an ASP.NET Core concern used in `Program.cs`. To be resolved during #115 implementation.
