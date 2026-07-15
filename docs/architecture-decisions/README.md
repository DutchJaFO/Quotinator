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
- **Header fields state the current fact only — never an accumulated history.** An `Updated:` field, if present, holds a single date, not a running parenthetical log of every issue that touched the file (`2026-06-28 (issue #121 — ...); 2026-07-11 (issue #157 — ...)`). Git history and commit messages are the record of *when* and *why* something changed; a header field that duplicates that turns into a second, driftable copy of the same information. Same principle as `docs/workflow/process.md`'s "Where information lives" rule for plan docs' `**Status:**` line — it applies to every ADR header field too. The substance of a revision (what changed, why, what it corrects) belongs in a `## Revision — issue #N` body section, not the header.

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
| 008 | [008-enum-backed-columns-require-check-constraints.md](008-enum-backed-columns-require-check-constraints.md) | Enum-backed database columns require a matching CHECK constraint |
| 009 | [009-verify-migrations-against-last-released-schema.md](009-verify-migrations-against-last-released-schema.md) | Migrations must be verified against the last published release's schema |
| 010 | [010-repository-is-csharp-only.md](010-repository-is-csharp-only.md) | Repository is C#-only; tooling scripts follow the same rule as application code |
| 011 | [011-series-universe-hierarchy-and-character-source-identity.md](011-series-universe-hierarchy-and-character-source-identity.md) | Series/Universe hierarchy and Character↔Source many-to-many identity |
