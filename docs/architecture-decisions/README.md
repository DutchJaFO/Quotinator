# architecture-decisions/

Architecture Decision Records (ADRs) for Quotinator — one file per significant technical decision.

## Format

Each file follows the naming convention `NNN-short-title.md` and contains:

- **Status** — Proposed / Accepted / Superseded / Deprecated
- **Date** — when the decision was made
- **Context** — why the decision needed to be made
- **Decision** — what was decided
- **Consequences** — trade-offs, follow-on work, related issues

## Rules

- ADRs are never deleted. If a decision is reversed, the original ADR is marked **Superseded** and a new ADR is written.
- Number sequentially from `001`. Do not reuse numbers.
- Link related GitHub issues in the header.

## Index

| # | File | Title |
|---|---|---|
| 001 | [001-cve-2025-6965-sql-aggregate-guard.md](001-cve-2025-6965-sql-aggregate-guard.md) | CVE-2025-6965: SQL aggregate guard |
| 002 | [002-recordbase-on-all-tables.md](002-recordbase-on-all-tables.md) | RecordBase applies to all tables without exception |
| 003 | [003-unit-of-work-and-data-project-design-goals.md](003-unit-of-work-and-data-project-design-goals.md) | Unit of Work pattern and Quotinator.Data design goals |
| 004 | [004-quotinator-data-project-boundaries.md](004-quotinator-data-project-boundaries.md) | Quotinator.Data project boundaries and design intent |
| 005 | [005-quotinator-changelog-project-scope.md](005-quotinator-changelog-project-scope.md) | Quotinator.Changelog project scope |
| 006 | [006-sequential-test-execution-by-default.md](006-sequential-test-execution-by-default.md) | Sequential test execution by default |
| 007 | [007-cs1591-on-test-projects.md](007-cs1591-on-test-projects.md) | CS1591 enforcement on test projects |
