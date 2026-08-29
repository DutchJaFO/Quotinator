# ADR 005 — Quotinator.Changelog project scope

**Status:** Accepted
**Date:** 2026-06-25  
**GitHub issues:** #80, #82, #309

---

## Context

Changelog content is authored as one JSON file per language and rendered in two places: the Blazor UI
and the generated `CHANGELOG.md` / `addon/CHANGELOG.md` markdown files. A decision was needed on whether
changelog loading and generation logic should live in `Quotinator.Api`, `Quotinator.Core`, or a
dedicated project.

Keeping it in `Quotinator.Api` conflates presentation with logic and prevents the
`scripts/changelog.csx` generator from using the same models without taking a dependency on the API
project. Keeping it in `Quotinator.Core` introduces changelog concerns into the domain layer, which has
nothing to do with quotes or data access.

---

## Decision

`Quotinator.Changelog` is a **standalone, dependency-isolated project** responsible for:

1. **Schema and models** — typed C# representation of the changelog JSON format (`ChangelogDocument`,
   `ChangelogRelease`, `ChangelogUnreleased`, etc.)
2. **Loading** — deserialising per-language changelog JSON files into typed models (`IChangelogService`)
3. **Formatting** — generating output from loaded models (markdown formats, generated-file headers)

### Scope boundary — what Quotinator.Changelog does NOT do

- No UI rendering — that is `Quotinator.Api`'s concern
- No direct database access — the project is schema, parsing and formatting only
- No domain logic — no knowledge of quotes, sources, genres, or any Quotinator domain concept
- No dependency on `Quotinator.Core` or `Quotinator.Data` — the project is intentionally isolated

### Dependency rule

`Quotinator.Changelog` may only depend on:
- .NET BCL (`System.*`)
- `Microsoft.Extensions.Logging.Abstractions` (for `ILogger<T>` injection)
- No NuGet packages that bring domain or persistence concerns

Consuming projects depend on `Quotinator.Changelog`; it never references them. Those consumers are
`Quotinator.Api`, `scripts/changelog.csx`, and `Quotinator.Data`, which parses the authored files
through this project before storing the result.

### Changelog content is database-backed system content

Content is **authored** as JSON — same schema (`schemas/changelog.schema.json`), same generator
(`scripts/changelog.csx`), same parsing (`IChangelogService`) — and **served** from the changelog
database, per [ADR 018](018-system-content-in-quotinator-data.md)'s file-authored system-content
pattern. The files live in a runtime-accessible location (`data/changelog/`), not compiled into the API
assembly, so content is a data concern rather than a deploy-time one.

`Quotinator.Changelog` itself still performs zero database access — that boundary is unchanged. A
separate `Quotinator.Data`-owned component depends on this project to parse the files, then writes the
result into `Changelog_Entry`/`Changelog_Line` (the `Changelog_` domain, per
[ADR 015](015-domain-prefixed-table-naming.md)). Consumers such as the About page and the startup
what's-new notification read from those tables rather than re-parsing JSON per request.

### Why a separate project

- The `scripts/changelog.csx` generator needs the same models and generation logic without pulling in
  the API or its dependencies
- `Quotinator.Changelog.Tests` can verify schema correctness and generation output in complete
  isolation — no web host, no database, no DI container required
- If Quotinator is ever published as a library or split into multiple services, the changelog component
  travels independently

---

## Consequences

- All changelog schema models, loading logic, and markdown generation live in `Quotinator.Changelog` —
  never in `Quotinator.Api` or `Quotinator.Core`
- `Quotinator.Data` depends on `Quotinator.Changelog` to parse authored content before storing it
- `Quotinator.Changelog.Tests` tests schema compliance and generation output without any web host
- New output formats (e.g. RSS, HTML fragment) are added to `Quotinator.Changelog/Formatting/` — not to
  the API project
- Any temptation to add domain concepts (quote types, language codes as enums, etc.) to
  `Quotinator.Changelog` must be resisted — the project is format/serialisation only
