# Quotinator — Documentation

## Structure

| File | Contents |
|---|---|
| `api-endpoints.md` | Full REST API endpoint reference — every route, its query parameters, and behavior |
| `ci-cd.md` | CI/CD workflows, release process, versioning |
| `docker.md` | Docker image, local Docker run, environment variables, build notes |
| `home-assistant.md` | HA add-on setup, ingress, ports, releasing |
| `localisation.md` | UI string localisation and quote-level translation |
| `openapi.md` | OpenAPI and Scalar setup, how to document endpoints, tags, and models |
| `running-locally.md` | How to start and verify the application in Visual Studio |
| `sql-safety.md` | SQL aggregate guard design — CVE-2025-6965, why regex, SQLite aggregate coverage |
| `testing-policy.md` | Test framework, project structure, what to test |
| `troubleshooting.md` | Common problems and recovery procedures for Docker / standalone deployments |
| `vocabulary.md` | Authoritative reference for abbreviations and domain terms |

| Folder | Contents |
|---|---|
| `architecture-decisions/` | Architecture Decision Records — numbered, one file per decision |
| `decisions/` | Lightweight design notes, spike writeups, open questions (see `decisions/README.md`) |

## Architecture Decision Records

| # | Title | Status |
|---|---|---|
| [001](architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md) | CVE-2025-6965: SQL aggregate guard | Accepted |
| [002](architecture-decisions/002-recordbase-on-all-tables.md) | RecordBase applies to all tables without exception | Accepted |
| [003](architecture-decisions/003-unit-of-work-and-data-project-design-goals.md) | Unit of Work pattern and Quotinator.Data design goals | Accepted |
| [004](architecture-decisions/004-quotinator-data-project-boundaries.md) | Quotinator.Data project boundaries and design intent | Accepted |
| [005](architecture-decisions/005-quotinator-changelog-project-scope.md) | Quotinator.Changelog project scope | Accepted |
| [006](architecture-decisions/006-sequential-test-execution-by-default.md) | Sequential test execution by default | Accepted |
| [007](architecture-decisions/007-cs1591-on-test-projects.md) | CS1591 enforcement on test projects | Pending |
| [008](architecture-decisions/008-enum-backed-columns-require-check-constraints.md) | Enum-backed database columns require a matching CHECK constraint | Accepted |
| [009](architecture-decisions/009-verify-migrations-against-last-released-schema.md) | Migrations must be verified against the last published release's schema | Accepted |
| [010](architecture-decisions/010-repository-is-csharp-only.md) | Repository is C#-only; tooling scripts follow the same rule as application code | Accepted |
| [011](architecture-decisions/011-series-universe-hierarchy-and-character-source-identity.md) | Universe/Series/Season hierarchy and Character↔Source many-to-many identity | Accepted |
| [012](architecture-decisions/012-canonicalize-entity-ids-at-capture.md) | External entity ids are canonicalized once, at the point of capture | Accepted |
| [013](architecture-decisions/013-character-merge-algorithm.md) | Character merge algorithm: Type-anchored, Series-scoped global identity | Accepted |
| [014](architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) | Audit-trail tables don't purge dangling references; a destructive Reset needs its own export step | Accepted |
| [015](architecture-decisions/015-domain-prefixed-table-naming.md) | Domain-prefixed table naming: a namespace substitute for SQLite's lack of schema qualification | Accepted |
| [016](architecture-decisions/016-class-naming-suffixes-and-enum-placement.md) | Class-naming suffixes and enum placement | Accepted |
| [017](architecture-decisions/017-join-capable-reads-use-joinqueryrepository.md) | Join-capable reads use JoinQueryRepository/IJoinStrategy, even without an immediate capability gain | Accepted |
| [018](architecture-decisions/018-system-content-in-quotinator-data.md) | System-level content belongs in Quotinator.Data | Accepted |
| [019](architecture-decisions/019-central-package-version-management.md) | Every NuGet package version is declared once, centrally | Accepted |
| [020](architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md) | Every OpenAPI tag an endpoint uses is declared with a description | Accepted |
| [021](architecture-decisions/021-file-inputs-are-schema-validated.md) | Every file used as input is validated against the schema that defines it | Accepted |
| [022](architecture-decisions/022-exceptions-only-for-undetectable-conditions.md) | An exception is thrown only when nothing else can detect the condition, and every exception is logged | Accepted |
| [023](architecture-decisions/023-one-conflict-rule-entry-per-entity.md) | One conflict-rule entry per entity, and the recorded incoming value belongs to the field it governs | Accepted |
| [024](architecture-decisions/024-case-insensitivity-serves-identity-not-content-equality.md) | Case-insensitivity serves identity and lookup, never content equality | Accepted |

## Architecture Decision Record format

Files in `architecture-decisions/` follow the naming convention `NNN-short-title.md` (e.g. `001-flat-file-json-for-v1.md`).

Each ADR contains:
- **Status** — Proposed / Accepted / Superseded / Deprecated
- **Context** — why the decision needed to be made
- **Decision** — what was decided
- **Consequences** — trade-offs and follow-on work
