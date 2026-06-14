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

Issues fall into two categories depending on how the fix can be verified.

### Code-verified issues

The fix is covered by automated tests or is verifiable locally without deploying to Home Assistant.

- Add `Fixes #N` (bug) or `Closes #N` (feature) to the commit message body
- GitHub closes the issue automatically when the commit lands on `main`

### Deployment-verified issues

The fix can only be confirmed in a live HA add-on — for example: ingress routing, supervisor log output, config panel options, or container restart behaviour.

**Before the release:**
- Add the issue to the post-deploy checklist in memory (`project_post_deploy_verification.md`) with steps to verify and the version/commit it was fixed in
- Do **not** add `Closes #N` to the commit — the issue stays open until confirmed in production

**After deploying and verifying:**

```bash
gh issue close N --comment "Verified in vX.Y.Z on YYYY-MM-DD. [One sentence: what was tested and what was observed.]"
```

- Remove the item from `project_post_deploy_verification.md` in memory

**What counts as deployment-only:**
- Any behaviour that only manifests through the HA ingress proxy
- Supervisor log output (timestamps, log level, banners)
- Add-on configuration panel (options, dropdowns)
- Container restart or data persistence behaviour
- HA-specific environment variables (`Quotinator__*` via `env_vars`)

## Running the tests

```bash
dotnet test
```

All tests must pass before a PR will be reviewed.
