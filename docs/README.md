# Quotinator — Documentation

## Structure

| File | Contents |
|---|---|
| `ci-cd.md` | CI/CD workflows, release process, versioning |
| `docker.md` | Docker image, local Docker run, environment variables, build notes |
| `home-assistant.md` | HA add-on setup, ingress, ports, releasing |
| `localisation.md` | UI string localisation and quote-level translation |
| `openapi.md` | OpenAPI and Scalar setup, how to document endpoints, tags, and models |
| `running-locally.md` | How to start and verify the application in Visual Studio |
| `sql-safety.md` | SQL aggregate guard design — CVE-2025-6965, why regex, SQLite aggregate coverage |
| `testing-policy.md` | Test framework, project structure, what to test |
| `vocabulary.md` | Authoritative reference for abbreviations and domain terms |

| Folder | Contents |
|---|---|
| `architecture-decisions/` | Architecture Decision Records — numbered, one file per decision |
| `decisions/` | Lightweight design notes, spike writeups, open questions |

## Architecture Decision Records

| # | Title | Status |
|---|---|---|
| [001](architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md) | CVE-2025-6965: SQL aggregate guard | Accepted |
| [002](architecture-decisions/002-recordbase-on-all-tables.md) | RecordBase applies to all tables without exception | Accepted |

## Architecture Decision Record format

Files in `architecture-decisions/` follow the naming convention `NNN-short-title.md` (e.g. `001-flat-file-json-for-v1.md`).

Each ADR contains:
- **Status** — Proposed / Accepted / Superseded / Deprecated
- **Context** — why the decision needed to be made
- **Decision** — what was decided
- **Consequences** — trade-offs and follow-on work
