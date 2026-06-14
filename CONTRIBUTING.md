# Contributing

Quotinator is primarily a personal homelab project. External contributions are welcome but should align with the project's goals and constraints.

## Before opening a pull request

- Read [CLAUDE.md](CLAUDE.md) — it describes the architecture, current phase, and what is intentionally out of scope
- Check the roadmap in [README.md](README.md) — v1 is read-only API only; auth, UI, and MCP are later phases
- For anything non-trivial, open an issue first to discuss the approach

## What's in scope for contributions

- Bug fixes in existing endpoints or the seed pipeline
- Additional quote data from properly licensed sources (see [SOURCES.md](SOURCES.md))
- Corrections to quote attribution or text
- Documentation improvements

## What's out of scope for v1

- Authentication
- Write endpoints
- The Blazor management UI
- MCP support
- Entity Framework or any database (flat-file JSON only in v1)

## Code style

- C# idiomatic .NET 10 — no unusual patterns
- No new NuGet packages without a clear reason
- All quotes must be real and accurately attributed — do not generate or invent quotes

## Closing issues

| Scenario | Protocol |
|---|---|
| Fix or feature lands in a new commit | Add `Fixes #N` (bug) or `Closes #N` (feature) to the commit message body — GitHub closes the issue automatically on push to `main` |
| Already fixed in a prior release (no new commit) | Close via `gh issue close N --comment "..."` — include the version, commit SHA, and a one-line explanation |

## Running the tests

```bash
dotnet test
```

All tests must pass before a PR will be reviewed.
