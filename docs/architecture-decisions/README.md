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
- **An ADR carries context and the rules in force — never its own history.** When a decision is refined, edit the affected section in place so the ADR reads as one current statement. Do not append a `## Revision — issue #N` section, leave a superseded paragraph standing with a forward-reference to its correction, or narrate the incident that prompted the change. A reader must not have to assemble the current rule from two places, and a history section goes stale in place: one such section described the changelog database as in-memory long after the same issue had already made it a file, and another named a table under a name the schema never shipped. Git history and commit messages are the record of *when* and *why* something changed; the ADR is the record of what is true now.
- **Header fields state the current fact only — never an accumulated history.** An `Updated:` field, if present, holds a single date, not a running parenthetical log of every issue that touched the file (`2026-06-28 (issue #121 — ...); 2026-07-11 (issue #157 — ...)`). Same principle as `docs/workflow/process.md`'s "Where information lives" rule for plan docs' `**Status:**` line — it applies to every ADR header field too. Listing the issues that shaped a decision (`**GitHub issues:** #227, #254, #309`) is a current fact and is fine; a dated log of what each one changed is not.

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
| 012 | [012-canonicalize-entity-ids-at-capture.md](012-canonicalize-entity-ids-at-capture.md) | External entity ids are canonicalized once, at the point of capture |
| 013 | [013-character-merge-algorithm.md](013-character-merge-algorithm.md) | Character merge algorithm: Type-anchored, Series-scoped global identity |
| 014 | [014-audit-trail-tables-do-not-purge-dangling-references.md](014-audit-trail-tables-do-not-purge-dangling-references.md) | Audit-trail tables don't purge dangling references; a destructive Reset needs its own export step |
| 015 | [015-domain-prefixed-table-naming.md](015-domain-prefixed-table-naming.md) | Domain-prefixed table naming: a namespace substitute for SQLite's lack of schema qualification |
| 016 | [016-class-naming-suffixes-and-enum-placement.md](016-class-naming-suffixes-and-enum-placement.md) | Class-naming suffixes (Entity/Request/Response/Dto) and enum placement |
| 017 | [017-join-capable-reads-use-joinqueryrepository.md](017-join-capable-reads-use-joinqueryrepository.md) | Join-capable reads use JoinQueryRepository/IJoinStrategy, even without an immediate capability gain |
| 018 | [018-system-content-in-quotinator-data.md](018-system-content-in-quotinator-data.md) | System-level content (notifications, changelog, future genre) belongs in Quotinator.Data |
| 019 | [019-central-package-version-management.md](019-central-package-version-management.md) | Every NuGet package version is declared once, centrally |
| 020 | [020-openapi-tags-are-declared-with-descriptions.md](020-openapi-tags-are-declared-with-descriptions.md) | Every OpenAPI tag an endpoint uses is declared with a description |
