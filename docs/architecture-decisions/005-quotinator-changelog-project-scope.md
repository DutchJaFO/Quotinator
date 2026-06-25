# ADR 005 — Quotinator.Changelog project scope

**Status:** Accepted — open question on content file location (see below)  
**Date:** 2026-06-25  
**GitHub issues:** #80, #82

---

## Context

Changelog content is authored in `src/Quotinator.Api/resources/changelog.*.json` (one file per language) and rendered in two places: the Blazor UI and the generated `CHANGELOG.md` / `addon/CHANGELOG.md` markdown files. A decision was needed on whether changelog loading and generation logic should live in `Quotinator.Api`, `Quotinator.Core`, or a dedicated project.

Keeping it in `Quotinator.Api` conflates presentation with logic and prevents the `scripts/changelog.csx` generator from using the same models without taking a dependency on the API project. Keeping it in `Quotinator.Core` introduces changelog concerns into the domain layer, which has nothing to do with quotes or data access.

---

## Decision

`Quotinator.Changelog` is a **standalone, dependency-isolated project** responsible for:

1. **Schema and models** — typed C# representation of the changelog JSON format (`ChangelogRoot`, `ChangelogRelease`, `ChangelogUnreleased`, etc.)
2. **Loading** — deserialising per-language `changelog.*.json` files into typed models (`IChangelogService`)
3. **Formatting** — generating output from loaded models (markdown formats, generated-file headers)

### Scope boundary — what Quotinator.Changelog does NOT do

- No UI rendering — that is `Quotinator.Api`'s concern (Blazor components consume `IChangelogService`)
- No database access — changelog data lives in JSON files, never in SQLite
- No domain logic — no knowledge of quotes, sources, genres, or any Quotinator domain concept
- No dependency on `Quotinator.Core` or `Quotinator.Data` — the project is intentionally isolated

### Dependency rule

`Quotinator.Changelog` may only depend on:
- .NET BCL (`System.*`)
- `Microsoft.Extensions.Logging.Abstractions` (for `ILogger<T>` injection)
- No NuGet packages that bring domain or persistence concerns

Consuming projects (`Quotinator.Api`, `scripts/changelog.csx`) depend on `Quotinator.Changelog`. It never references them.

### Why a separate project

- The `scripts/changelog.csx` generator script needs the same models and generation logic without pulling in the API or its dependencies
- `Quotinator.Changelog.Tests` can verify schema correctness and generation output in complete isolation — no web host, no database, no DI container required
- If Quotinator is ever published as a library or split into multiple services, the changelog component travels independently

---

## Open question — where do the changelog JSON files live?

This ADR defines the *library* scope but does not resolve where the *content* (the `changelog.*.json` files) is stored. The current placement — `src/Quotinator.Api/resources/` — was inherited from the initial implementation and was never an explicit decision.

### The tension

The current approach treats changelog content as a **deploy-time artifact**: the JSON files are part of the source tree, versioned with the code, and embedded in the build output. Updating the changelog requires a code commit and a deployment.

An alternative is to treat changelog content as a **runtime artifact**: the JSON files live in a configurable location (e.g. the data directory alongside the database), loaded at startup like any other external data file. This would allow changelog content to be updated independently of code deployments — and opens the door to an external changelog management tool that operates outside the repository entirely.

### Options

| Option | Where files live | Update requires | Trade-offs |
|--------|-----------------|-----------------|------------|
| A (current) | `src/Quotinator.Api/resources/` | Code commit + deploy | Simple; content is versioned with code; developer workflow only |
| B | Data directory (`{dataDir}/changelog/`) | File update in data dir | Decoupled from deployments; HA add-on users could update without a release |
| C | External management tool + separate repo | Tool-managed, pulled at build or runtime | Maximum flexibility; significant complexity; may be over-engineering for a homelab project |

### What needs to be decided

1. Is changelog content a code concern (deploy-time) or a data concern (runtime)?
2. Should `IChangelogService` accept a configurable file path, or always resolve from embedded resources?
3. Is there value in an external changelog management tool for this project at all?

### Resolution

This question should be resolved in a follow-up decision — either by updating this ADR with the chosen option, or by opening a dedicated issue and referencing it here. Until resolved, Option A (current placement) remains in effect. No new code should embed assumptions about file location that would be hard to reverse.

---

## Consequences

- All changelog schema models, loading logic, and markdown generation live in `Quotinator.Changelog` — never in `Quotinator.Api` or `Quotinator.Core`
- `Quotinator.Api` references `Quotinator.Changelog` for `IChangelogService` injection into Blazor pages and API endpoints
- `Quotinator.Changelog.Tests` tests schema compliance and generation output without any web host
- New output formats (e.g. RSS, HTML fragment) are added to `Quotinator.Changelog/Formatting/` — not to the API project
- Any temptation to add domain concepts (quote types, language codes as enums, etc.) to `Quotinator.Changelog` must be resisted — the project is format/serialisation only
