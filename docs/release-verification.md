# Release Verification Tiers

This document defines the three verification tiers used in the Quotinator release process. Every issue plan doc must declare which tiers apply. Every required tier must be confirmed before the issue can close or a release tag can be pushed.

---

## Tiers

### T1 — VS/local

**Environment:** Visual Studio on Windows, running `Quotinator.Api` directly.

**What it catches:**
- Razor component runtime errors — `dotnet build` reports 0 errors but `.razor` files can still reference stale namespaces or broken bindings that only surface when the Blazor circuit starts
- Blazor component rendering and interactive behaviour
- App startup errors visible in the VS output window
- Database/migration behaviour against a real, persistent SQLite file — unit tests run against a fresh temp database every time and can miss failure modes that only appear on an existing, previously-migrated database (e.g. a dropped table that never gets recreated, a migration that behaves differently against non-empty data)

**When required:** Always — every issue runs T1, not only when one of the triggers below applies. This
mirrors T2's own "always required" rule below (see #196's precedent, where the same narrower
trigger-matching reasoning was already corrected for T2); it's simply not how this project verifies
releases, regardless of trigger-matching. The trigger list still matters for what to pay closest
attention to beyond a basic "does it start and serve requests" check: any change that touches `.razor`,
`.razor.cs`, `_Imports.razor`, Blazor services, or middleware registered before the request pipeline
reaches Blazor; **or** any change to `DatabaseInitializer`/`QuotinatorDatabaseInitializer`, migration SQL,
or schema/table-wipe logic (reseed, reset, backup) needs a targeted check (affected page renders, the
specific migration/reset path is exercised) on top of the baseline, not instead of it.

**Gate:** user starts the app in Visual Studio and confirms it starts without error; affected pages render correctly. This is exclusively the developer's own action — an AI assistant never runs `dotnet run` itself to perform or substitute for this gate (see CLAUDE.md's Commands section).

---

### T2 — Docker

**Environment:** local Docker build and run (`docker build` + `docker run`).

**What it catches:**
- Publish output completeness — missing `data/sources/`, missing static web assets, incorrect `COPY` paths in the Dockerfile
- Container startup errors (Kestrel binding, port config, missing environment variables that have no local fallback)
- Multi-arch build failures (linux/amd64, linux/arm64)
- Version number visible at `/api/v1/version` — a missing `Directory.Build.props` in the build context silently produces `1.0.0`
- Schema/reset behaviour building and running end-to-end from a fresh container image, independent of the local dev environment — confirms the same migration/reset path works identically outside VS

**When required:** Always — every issue runs T2, not only when one of the triggers below applies. That
was tried once (#196, "T2 not required — no route/schema/startup change") and was wrong on two counts: it
missed that the change touched `Program.cs`, hitting a trigger below anyway, and it's simply not how this
project verifies releases regardless of trigger-matching. The trigger list still matters for what to
additionally exercise beyond the baseline smoke tests in CLAUDE.md's Pre-Push Checklist → step 6: any
change that touches the Dockerfile, publish output, `Program.cs` startup, port or SSL configuration,
`Directory.Build.props`; **or** any change to `DatabaseInitializer`/`QuotinatorDatabaseInitializer`,
migration SQL, or schema/table-wipe logic (reseed, reset, backup) needs a targeted check on top of the
baseline, not instead of it.

**Gate:** `docker build` succeeds; every command in CLAUDE.md's Pre-Push Checklist → step 6
("Smoke-test the image") returns expected output. That checklist is the single authoritative,
living smoke test suite — it is not duplicated here, so the two never drift apart. It already
covers health/version/random/search plus the full import/staged-action review workflow (list,
decide, undo, apply, discard, the `batchId`-mode alias, and case-insensitive query filters); update
it — not this file — whenever a new scenario needs covering.

When the change touches schema/reset logic, also exercise the affected admin endpoint(s) directly (e.g. `POST /api/v1/admin/database/reset`) against the running container and confirm the expected before/after state.

---

### T3 — HA add-on

**Environment:** live Home Assistant supervisor with the add-on installed from GHCR.

**What it catches:**
- HA ingress routing — `X-Ingress-Path` middleware, `<base href>` derivation, relative asset URLs resolving through the ingress proxy
- Supervisor volume mount at `/data` — database and DataProtection keys written to the persistent volume, not lost on container restart
- DataProtection key persistence — antiforgery tokens and Blazor circuit descriptors survive container restart
- SSL certificate loading from HA Let's Encrypt paths
- Cookie `Secure` flag behaviour with the HA supervisor as TLS terminator
- Add-on config panel — options and translations visible and correct in the HA UI
- Log output format — `[Subsystem - Phase]` prefixes visible in the supervisor log

**When required:** any change that touches ingress middleware, `X-Ingress-Path` handling, `PathBase`, `UseForwardedHeaders`, DataProtection, SSL/Kestrel config, `addon/config.yaml`, addon translation files, or log output format.

**Gate:** install the beta add-on in HA; confirm all T3-classified requirements for the release are working in the live add-on. Document confirmation in the closing comment.

---

## How to declare tiers in a plan doc

In the issue plan doc, add a **Tiers** line after the Status line:

```
**Tiers required:** T1, T2
```

or

```
**Tiers required:** T1, T2, T3
```

If an issue requires T3, it must go through a beta release before the final tag is pushed. See `docs/workflow/checklist.md → Milestone close` for the full gate sequence.

T1 and T2 are always required (see each tier's own "When required" above) — `**Tiers required:** T2` alone
or `**Tiers required:** T1` alone are not valid declarations for any issue that touches code; the minimum
is `**Tiers required:** T1, T2`.

---

## Two-stage release model

| Stage | Git tag | `addon/config.yaml version` | Docker tags | GitHub Release |
|---|---|---|---|---|
| Beta | `v1.7.0-beta` | `1.7.0-beta` | `1.7.0-beta` (no `latest`) | Pre-release |
| Final | `v1.7.0` | `1.7.0` | `1.7.0`, `1.7`, `1`, `latest` | Full release |

**T1 + T2 must be verified before pushing a beta tag.**  
**T3 must be verified before pushing a final tag.**  
**A beta tag is mandatory for every release, without exception.**

The release workflow enforces this: pushing a final tag (e.g. `v1.7.0`) without a prior beta tag (e.g. `v1.7.0-beta`) for the same version will cause the workflow to fail immediately.
