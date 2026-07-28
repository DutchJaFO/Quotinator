# ADR 004 — Quotinator.Data project boundaries and design intent

**Status:** Revised (see inline revisions below) — the Engine layer introduced by the #121 revision was retired by #206  
**Date:** 2026-06-25  
**GitHub issue:** #115  
**Updated:** 2026-07-19

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

---

## Revision — issue #157 closed the `Sql.cs` placement gap

Line 43 above ("SQL query string constants (`Sql.cs`) — these are strings, not Dapper types; they
remain testable from Core") predates the Engine revision and was never updated when `Quotinator.Engine`
was introduced. In practice `Sql.cs` stayed in `Quotinator.Data` by default, and every
Quotinator-domain query added since (`Quotes`, `Characters`, `Sources`, `Conversations`, etc.) copied
that placement without anyone checking it against this ADR — the exact "existing code is not a
validated decision" failure mode CLAUDE.md warns about via the `SystemAuditEntry` incident.

**A SQL query string constant follows the same domain/generic split as everything else in this ADR,
regardless of the fact that it is "just a string, not a Dapper type":** a query naming a
Quotinator-domain table (`Quotes`, `Sources`, `Characters`, `People`, `Conversations`,
`ConversationLines`, `StageDirections`, `SoundCues`, and their translation/detail tables) is
domain-specific SQL and belongs in `Quotinator.Engine/Queries/Sql.cs` — `internal static class Sql`
in namespace `Quotinator.Engine.Queries` — alongside the entity classes that use it. A query touching
only generic infrastructure (`Schema`, `Joins`, the `Queries` example, and the tables
`Quotinator.Data` itself owns outright — `SystemAudit`, `SystemImportActions`, `SystemChangeLog`,
`ImportBatches`) stays in `Quotinator.Data/Queries/Sql.cs`. The original "just a string" reasoning
undersold the coupling: a query's `FROM`/`JOIN` clauses hardcode a domain schema shape just as much as
a Dapper entity class does, even without a compile-time type reference.

**Correction (issue #158):** this revision originally listed `ImportBatches` alongside the genuinely
domain-specific tables above — wrong, caught immediately afterward in the same investigation.
`ImportBatches` never interacts with a consumer-defined entity; it is generic import/seed bookkeeping,
the same category as `SeedBatch`/`ManifestPolicy`. See the next revision below for the test that
would have caught this the first time.

---

## Revision — issue #158: the consumer-entity-interaction test

This ADR's "What belongs in `Quotinator.Data`" list and the revised boundary table are illustrative
examples, not an exhaustive checklist to pattern-match against — and treating them as one is exactly
how they went stale twice: `ImportBatchType` and `IImportBatchRepository` were both named explicitly
in this ADR's original 2026-06-25 text as Data examples, yet both sat in `Quotinator.Engine` from the
#121 Engine split onward, undetected until #158, because nobody re-derived the rule from first
principles — they pattern-matched the existing (wrong) location of the `ImportBatch` entity instead.
#157 made the identical mistake in the opposite direction days earlier: it moved `Sql.ImportBatches`
*into* Engine specifically because the entity was already there, again pattern-matching a location
instead of testing it.

**The governing test, going forward:** a type belongs in `Quotinator.Engine` only if it needs to
interact with an entity the *consumer* defines — `Quote`, `Source`, `Character`, `Person`,
`Conversation`, `StageDirection`, `SoundCue`, or any Quotinator-domain enum (`QuoteType`, `Genre`).
"Interact with" means referencing the type directly, joining against its table, or containing
business rules that only make sense in terms of it. Everything else — including seeding and the
import/batch-tracking feature as a whole — is generic convenience infrastructure usable by any future
consumer with its own schema, and belongs in `Quotinator.Data`, regardless of how "import-flavoured"
or "seeding-flavoured" it superficially looks. `ImportBatch` bookkeeping (which batch, when, by what
policy, how many records, current lifecycle status) never names a consumer entity — it belongs in
Data. `ImportActionPlanner` and `SqliteQuoteImportService`, by contrast, exist specifically to plan
and write `Quote`/`Source`/`Character`/`Person`/`Conversation` rows — they belong in Engine.

Apply this test *before* looking at where a related or superficially-similar type already lives.
"What belongs in `Quotinator.Data`" (above) and the revised boundary table remain useful as worked
examples of applying the test, not as the source of truth themselves.

This also applies to any *code that assembles or executes* domain-specific SQL, not only the string
constants themselves — found while doing this split: `DatabaseInitializer.TruncateDataAsync`
(the shared base class in `Quotinator.Data`) directly called `Sql.Quotes.DeleteAll`,
`Sql.Conversations.DeleteAll`, and similar Quotinator-domain deletes. It moved to
`QuotinatorDatabaseInitializer` (`Quotinator.Engine`), its only caller.

---

## Revision — issue #206 merged `Quotinator.Engine` back into `Quotinator.Core`

The #121 revision above introduced `Quotinator.Engine` as a third project because `Quotinator.Data`
had to stay domain-agnostic (no Quotinator-specific types), while `Quotinator.Core` was held to a
separate, freestanding invariant: zero Dapper, zero SQLite, zero `Quotinator.Data` reference. Something
needed to hold Quotinator-domain entities and SQL that referenced both Core's domain types and Data's
Dapper/SQLite infrastructure at once, so a third project absorbed that role.

**That second invariant — not the first — is what's retired here.** `Quotinator.Data`'s domain-agnostic
boundary (Data must never reference Core; Data carries no Quotinator-specific types) is unaffected and
remains exactly as this ADR already describes. What changes is that `Quotinator.Core` no longer avoids
depending on `Quotinator.Data` — it depends on it directly, the same way `Quotinator.Engine` did, and
everything `Quotinator.Engine` held (domain entities, domain SQL, the SQLite-backed service
implementations, the reference-resolving readers) moves into `Quotinator.Core` unchanged in content,
renamespaced only.

**Why the "Core stays Dapper-free" invariant wasn't worth keeping**: found while planning issue #192
(exposing Series/Universe on the quote read path). `QuoteResponse` (Core) needed the same
`MasterDataReference` shape #184 had defined in `Quotinator.Api.Models` for the masterdata response
DTOs, but every one of those DTOs also needed `Quotinator.Data.Entities.CompletenessStatus`. Under the
three-project split, no single project could see a Core-owned reference type and a Data-owned
completeness type at once except `Quotinator.Api` — which is why #184's response DTOs had ended up
there instead of alongside `QuoteResponse`, and why #192 couldn't cleanly extend `QuoteResponse` without
either duplicating a type or reaching for an increasingly awkward workaround. The underlying problem
wasn't where any specific type lived; it was that the three-project split manufactured a boundary that
had no real justification once `Quotinator.Data`'s own domain-agnostic invariant (the actual reason
Engine was introduced) is honoured on its own. The boundary had also already blurred in practice
independent of #192: `Quotinator.Core.Tests.csproj` referenced `Quotinator.Engine.csproj` directly, and
several `Quotinator.Engine`-testing files (`QuotinatorMigrationsTests`, `SqliteQuoteServiceTests` and
its siblings) already lived under `Quotinator.Core.Tests` before this merge, filed under a `Data/`
folder matching neither project's actual namespace.

### Revised boundary rules (supersedes the #121 revision's table)

| What | Where | Rule |
|------|-------|------|
| Domain service interfaces (`IQuoteService`) | Core | Core defines the contract |
| Domain models and DTOs (`QuoteResponse`, masterdata response DTOs, `MasterDataReference`) | Core | Core owns domain models — one canonical location, no Core/Api split |
| Domain enums (`QuoteType`, `Genre`) | Core | Surfaced in service signatures |
| Domain import models (`SourceQuote`) | Core | Used by the legacy in-memory `QuoteService` |
| Generic DB infrastructure (repositories, UoW, migrations base, type handlers) | Data | Domain-agnostic; no Core reference |
| Generic import infrastructure (`SeedBatch`, `ManifestPolicy`, `ImportBatch` bookkeeping) | Data | Reusable across future projects |
| Quotinator-domain DB entities (`Source`, `QuoteEntity`, etc.) | Core | Reference both domain types and Data infrastructure directly |
| Quotinator-specific migrations and seeding | Core | `QuotinatorDatabaseInitializer` extends `DatabaseInitializer` |
| Quotinator-specific Dapper handler registration | Core | `QuotinatorDapperConfiguration` extends `DatabaseConfiguration` |
| SQLite implementation of `IQuoteService` | Core | `SqliteQuoteService` |
| Quotinator-domain SQL (`Sql.cs`) | Core | `Quotinator.Core.Queries.Sql` — same #157/#158 consumer-entity-interaction test still applies |
| DI wiring | Api | `Program.cs` registers Core types; no Dapper or SQLite in Api |

### Revised invariants

- `Quotinator.Core` may reference `Quotinator.Data` (this is the invariant that changed).
- `Quotinator.Data` must have zero Core project references and zero Quotinator-domain types
  (unchanged from the #121 revision).
- `Quotinator.Api` wires everything via DI. It may reference Core (to register types) but must not
  contain business logic (unchanged).

The #121 revision's own project graph diagram, invariant list, and "What belongs in
`Quotinator.Engine`" guidance are superseded by this section — left in place above as history, per this
project's append-only ADR convention, not because they still describe the current structure.
