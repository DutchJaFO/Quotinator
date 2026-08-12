# Release Verification Tiers

This document defines the verification tiers used in the Quotinator release process. T1/T2/T3 are
**per-issue** tiers — every issue plan doc must declare which apply, and every required tier must be
confirmed before that issue can close. T4 is a **per-milestone** tier — it has no per-issue declaration
of its own; it runs once, at milestone close, regardless of which issues the milestone contained.

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
additionally exercise beyond the baseline smoke tests in `docs/smoke-tests.md`: any
change that touches the Dockerfile, publish output, `Program.cs` startup, port or SSL configuration,
`Directory.Build.props`; **or** any change to `DatabaseInitializer`/`QuotinatorDatabaseInitializer`,
migration SQL, or schema/table-wipe logic (reseed, reset, backup) needs a targeted check on top of the
baseline, not instead of it.

**Gate:** `docker build` succeeds; every command in [`docs/smoke-tests.md`](smoke-tests.md)
returns expected output. That document (referenced from CLAUDE.md's Pre-Push Checklist → step 6) is
the single authoritative, living smoke test suite — it is not duplicated here, so the two never drift
apart. It already covers health/version/random/search plus the full import/staged-action review
workflow (list, decide, undo, apply, discard, the `batchId`-mode alias, and case-insensitive query
filters); update it — not this file — whenever a new scenario needs covering.

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

**When required:** any change that touches ingress middleware, `X-Ingress-Path` handling, `PathBase`, `UseForwardedHeaders`, DataProtection, SSL/Kestrel config, `addon/config.yaml` or `addon-beta/config.yaml`, addon translation files, or log output format.

**Gate:** install the beta add-on in HA; confirm all T3-classified requirements for the release are working in the live add-on. Document confirmation in the closing comment.

---

### T4 — Docker image freshness (milestone close)

**Environment:** local Docker build (`docker build --no-cache --pull`) + the Docker Scout CLI (`docker scout cves`).

**What it catches:** OS-layer vulnerabilities in the base image that drift over time independent of any
code change in this repository. An upstream base-image maintainer can republish a patched layer between
milestones, and nothing else in this project's verification tiers ever re-checks that — T1/T2 build
against whatever's cached locally, and no per-issue smoke test exercises the base image itself. Found
live during #232 (2026-08-01): the issue's own premise (23 reported vulnerabilities) was already stale
by the time the research ran — a plain `--no-cache` rebuild alone (no Dockerfile change) dropped the
count to 8, purely because Microsoft had republished a patched base layer since the image was last
built. Nothing caught the drift until someone happened to re-run the scan by hand (#250).

**Unlike T1/T2/T3, this is a standing per-milestone gate, not something an individual issue declares.**
No issue's `**Tiers required:**` line ever lists T4 — it runs once per milestone, at close, regardless
of which issues the milestone touched, the same way [ADR 009](architecture-decisions/009-verify-migrations-against-last-released-schema.md)'s
last-published-release migration check does. See `docs/workflow/checklist.md → Milestone close` for
where it sits in the close sequence.

**When required:** once per milestone, before the milestone is closed — never skipped, never
substituted by a T2 pass's own `docker build` (which permits a cached build and is not a freshness
check). Always uses `--no-cache --pull`, both flags together: `--no-cache` alone only disables layer
caching for the build's own `RUN` steps — it does **not** force Docker to re-pull the `FROM` base image,
which can still be served from the local image cache and silently mask an upstream update. `--pull`
is what actually forces a fresh base-layer pull. **Found live during v1.8.3's own milestone-close pass
(2026-08-12), a second time**: #250's own 2026-08-10 entry in `docs/security/README.md` had already hit
this exact confusion once (a stale local base layer produced 3 phantom `Microsoft.NETCore.App.Runtime`
CVEs, including one High, that vanished after a manual `docker pull` + rebuild) but the fix was only
ever noted in that scan's own writeup, never carried back into this gate's documented command — so the
same false alarm reproduced identically two milestones later. `--pull` is now part of the gate itself,
not something to remember to add by hand.

**Gate:**
```bash
docker build --no-cache --pull -f docker/Dockerfile -t quotinator:local .
docker scout cves quotinator:local
```
Compare the result against `docs/security/README.md`'s "Docker base image (OS packages)" table:
- **Result matches exactly** — update only the "Last scanned" date/note in the same commit as the
  milestone-close docs.
- **Result differs** (a CVE resolved, a new one appeared, severity changed) — update the table and the
  "Last scanned" note in the same commit. Escalate beyond a documentation update only if a listed CVE
  now shows an actual `Fixed version` (a real upstream fix became available) rather than `not fixed`.

No pass/fail severity threshold — these are OS-layer CVEs tracked as accepted residual risk (see
`docs/security/README.md`), not a release-blocking gate on their own; deciding a future threshold policy
is explicitly out of scope for this tier's own definition (see #250's own filing).

---

## Never verify against an ad-hoc or shared database

Neither T1 nor T2 may substitute a developer's own accumulated Visual Studio dev database, or a stale
`bin/`-folder test artefact, for a deliberately constructed verification target. An ad-hoc database's
schema version and history are unknown until something breaks and they have to be reverse-engineered —
and worse, a *convenient* database (e.g. one that happens to already be empty) can silently take an
easier code path (fresh-baseline creation) instead of the one actually under test (incremental
migration replay), producing a false-positive "verified" that never exercised the real scenario.

**How to apply:**
- **T2 (Docker)**: use a purpose-built backup or snapshot representing a specific, known starting state
  (e.g. a database matching a real released version) — never whatever state a container happens to have
  accumulated across runs. If a scenario needs "an existing v8 database," construct that state
  deliberately within the same verification run.
- **Migration/schema-path verification specifically** belongs in a hermetic unit test against a fresh,
  purpose-built in-memory or temp-file database — see `DatabaseInitializerTests.cs`'s
  `InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations` and
  `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` for the pattern: construct the
  starting schema state explicitly, then verify the transition, with no dependency on any pre-existing
  file.
- The dedicated "verify against the last published release's schema" check ([ADR 009](architecture-decisions/009-verify-migrations-against-last-released-schema.md))
  is a milestone-closing gate that reconstructs a real released snapshot on purpose — it is not
  something a per-issue T1 check can casually substitute for by hoping the dev database happens to be
  in the right state.

**Why this matters even when a check "looks" green:** T1's own dev database can happen to be freshly
reset, which means it silently takes the baseline-creation path instead of the incremental-migration
path a plan doc's verification row actually claims to be checking — a "confirmed working" result that
never touched the code path it was meant to prove.

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

**T4 is never listed on a `**Tiers required:**` line.** It is not an issue-level tier — see its own
"When required" above.

---

## Two-stage release model

| Stage | Git tag | Version bumped | Docker tags | GitHub Release |
|---|---|---|---|---|
| Beta | `v1.7.0-beta` | `addon-beta/config.yaml version` → `1.7.0-beta` | `1.7.0-beta` (no `latest`) | Pre-release |
| Final | `v1.7.0` | `addon/config.yaml version` → `1.7.0` | `1.7.0`, `1.7`, `1`, `latest` | Full release |

**T1 + T2 must be verified before pushing a beta tag.**  
**T3 must be verified before pushing a final tag.**  
**A beta tag is mandatory for every release, without exception.**

The release workflow enforces this: pushing a final tag (e.g. `v1.7.0`) without a prior beta tag (e.g. `v1.7.0-beta`) for the same version will cause the workflow to fail immediately.
