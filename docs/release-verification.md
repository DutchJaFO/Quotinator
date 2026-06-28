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

**When required:** any change that touches `.razor`, `.razor.cs`, `_Imports.razor`, Blazor services, or middleware registered before the request pipeline reaches Blazor.

**Gate:** user starts the app in Visual Studio and confirms it starts without error; affected pages render correctly.

---

### T2 — Docker

**Environment:** local Docker build and run (`docker build` + `docker run`).

**What it catches:**
- Publish output completeness — missing `data/sources/`, missing static web assets, incorrect `COPY` paths in the Dockerfile
- Container startup errors (Kestrel binding, port config, missing environment variables that have no local fallback)
- Multi-arch build failures (linux/amd64, linux/arm64)
- Version number visible at `/api/v1/version` — a missing `Directory.Build.props` in the build context silently produces `1.0.0`

**When required:** any change that touches the Dockerfile, publish output, `Program.cs` startup, port or SSL configuration, or `Directory.Build.props`.

**Gate:** `docker build` succeeds; smoke-test commands return expected output:
```bash
docker run --rm -p 8080:8080 quotinator:local
curl -s http://localhost:8080/api/v1/health
curl -s http://localhost:8080/api/v1/version
curl -s http://localhost:8080/api/v1/quotes/random
```

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
