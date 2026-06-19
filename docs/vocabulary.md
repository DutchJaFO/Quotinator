# Vocabulary

This is the authoritative reference for abbreviations and domain terms used in this project.

**Policy:** Do not introduce a new abbreviation in code, comments, or documentation without adding
it to this file in the same commit. Domain terms that carry a project-specific meaning should also
be listed here, especially where a common word is used in a narrower sense than usual.

XML `<summary>` tags in source code are not affected by this policy — they are a build requirement
and follow standard C# documentation conventions.

---

## Abbreviations

| Abbreviation | Full form | Notes |
|---|---|---|
| API | Application Programming Interface | |
| ADR | Architecture Decision Record | See `docs/architecture-decisions/`. A short document capturing a significant technical decision: its context, what was decided, and the consequences. Numbered sequentially. |
| AST | Abstract Syntax Tree | Used in `docs/sql-safety.md` when describing SQL parser output. |
| CI | Continuous Integration | |
| CD | Continuous Delivery / Continuous Deployment | Used together as CI/CD in workflow docs. |
| CRUD | Create, Read, Update, Delete | Standard shorthand for the four basic data operations. |
| CVE | Common Vulnerabilities and Exposures | The identifier scheme used by the National Vulnerability Database for publicly known security issues. |
| DI | Dependency Injection | Used throughout in the context of ASP.NET Core's built-in container. |
| DTO | Data Transfer Object | An object used to carry data between layers without exposing the domain model directly. |
| FK | Foreign Key | A database column referencing the primary key of another table. |
| GHSA | GitHub Security Advisory | GitHub's identifier scheme for security advisories, often paired with a CVE identifier. |
| HA | Home Assistant | The home automation platform. Quotinator ships as a Home Assistant add-on. |
| ISO | International Organization for Standardization | Referenced in the context of ISO 639-1 (language codes) and ISO 8601 (date formats). |
| MCP | Model Context Protocol | The protocol used to expose Quotinator as a tool to AI assistants. |
| NVD | National Vulnerability Database | The US government repository of standards-based vulnerability management data. |
| PK | Primary Key | The unique identifier column on a database table. In this project always a UUID. |
| PR | Pull Request | A GitHub pull request proposing changes from one branch into another. |
| REST | Representational State Transfer | The architectural style used by the Quotinator HTTP API. |
| SMO | SQL Server Management Objects | Microsoft's .NET library for SQL Server administration. Mentioned in `docs/sql-safety.md` when explaining why the T-SQL parser was rejected. |
| SQL | Structured Query Language | |
| SSL | Secure Sockets Layer | Commonly used to mean TLS in configuration contexts; see TLS. |
| TLS | Transport Layer Security | The protocol securing HTTPS connections. |
| UI | User Interface | |
| UTC | Coordinated Universal Time | All timestamps in the database are stored in UTC. |
| UUID | Universally Unique Identifier | Version 4 (random) UUIDs are used as primary keys for all entities. |
| WAL | Write-Ahead Logging | The SQLite journal mode used by Quotinator. Improves concurrent read performance. |

---

## Domain terms

| Term | Definition |
|---|---|
| aggregate root | An entity that owns a cluster of related objects and is the single entry point for operations on that cluster. In the repository layer, an aggregate root repository may write to more than one table in a single transaction (see #75). |
| `character` | A fictional character in a film, series, book, or other fictional work who delivers a quote. Distinct from `person`. |
| `ImportBatch` | A single import operation — one run of the seed script or one call to the import endpoint. Tracks the provenance of records. Distinct from `SeedBatch`. |
| junction table | An associative table that implements a many-to-many relationship by holding pairs of foreign keys (e.g. `QuoteTag` linking `Quotes` to `Tags`). All junction tables in Quotinator extend `RecordBase` — see ADR 002. |
| `person` | A real-world individual (author, public figure) who said or wrote a quote. Distinct from `character`. |
| `RecordBase` | The abstract base class for all database-backed entities. Provides a UUID primary key and soft-delete audit columns (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`). |
| `SafeValue<T>` | A wrapper for database column values that may be imprecisely formatted (e.g. a date stored as `"1994"` rather than a full timestamp). Preserves the raw string alongside the parsed value. |
| `SeedBatch` | A group of source files processed together in a single seeding run, sharing a duplicate-resolution policy. Distinct from `ImportBatch`. |
| `source` | In the quote schema, `source` refers to the media title or occasion from which a quote is drawn — a film title, book title, TV series, or speech event. It does **not** mean an import data source. |
| `type` | The classification of a quote's origin. Valid values: `movie`, `tv`, `anime`, `book`, `person`. |
