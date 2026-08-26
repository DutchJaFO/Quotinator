# Degraded-state pages survive a genuine migration failure

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #293

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.2` tag**, chosen because `System_Notification` genuinely does not
exist on a real v1.8.2 database. It runs its own container and volume (`qt-startup-05` / `qt-startup-05-data`,
the name reused across the two runs), and the Constrained defect is intended to be
`--read-only` on the root filesystem with `/data` left writable — which is the part that no longer
works.

**Its premise is unreachable, measured 2026-08-18 and again 2026-08-26.** This test forces a migration
failure with `--read-only` on the root filesystem while `/data` stays a writable volume. #294
subsequently made exactly that arrangement survivable — the migration's temp files never touch disk, so
restricting every other path no longer causes a failure.

The setup here is byte-identical to
[`04-migration-replay-under-restricted-write.md`](04-migration-replay-under-restricted-write.md), which
asserts the app is **healthy** under it. Two tests cannot both be right about the same setup. The
measured behaviour is `200` healthy, so this document's own guard — *"health must be 503, confirming
the test actually reached the failure state"* — can never hold.

That guard was the right instinct and it is the reason the contradiction is provable rather than
merely suspected. What was missing is any step that *confirmed* the failure state before asserting
against it.

**#327 replaces this** with scenarios that provoke a real failure: a `:ro` volume mount with pinned WAL
sidecar state, a corrupt database file, and a schema version ahead of the application. #326 measured
that sidecar state — not a pending migration — is what decides whether a read-only mount degrades.

**The expectations below are stated as they stand, and the run fails against them.** That is the
correct signal while the technique no longer reaches the failure state: it is a real failure, not a
formality, and it clears when #327 gives the test a setup that provokes one. Do not soften the expected
status code to match what the container currently does — that would convert a failing test into a
passing one without changing anything the test is for.

## Determinism

Not established, and that is the defect. The original intended, but never pinned:

- a genuine migration failure — which the technique below no longer produces
- `System_Notification` genuinely absent — true on a real v1.8.2 database, so that part held

## Steps

### 1. Seed a real, unmodified v1.8.2 database

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-05 --port 18405 `
  --image ghcr.io/dutchjafo/quotinator:1.8.2

(Invoke-RestMethod "http://localhost:18405/api/v1/version").database.quotes
```

**Expected:** `quotes` is non-zero and the seed reports zero failures.

**On failure:** a partially-seeded volume would make everything below meaningless. Stop and re-seed
rather than proceeding.

### 2. Start the current build with a read-only root filesystem and read health

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 `
  --image quotinator:local --read-only --wait-listening

dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/health" --expect 503
```

**Expected:** `503` with `status=unhealthy`, confirming the test reached the failure state.

**On failure:** `200` healthy is what this setup actually measures today (see Observed effect), and
`--expect 503` ends the step there. That is the known, tracked contradiction, not a new result — stop
rather than running the degraded-page steps against a container that never degraded.

### 3. Read the degraded pages and the notifications API

```powershell
foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18405$path" --expect 200 --status
}
dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/notifications" --expect 503 --status
```

**Expected:** `/`, `/stats` and
`/notifications` all return `200` — never `500`, never a raw exception page.
`GET /api/v1/notifications` correctly returns `503`; API traffic stays gated while degraded, which is
the design
[`01-seeding-backup-degraded-startup-and-reset-recovery.md`](01-seeding-backup-degraded-startup-and-reset-recovery.md)
covers, and expected rather than a regression.

### 4. Read the three Blazor pages through a driver

**Driver step** — these are Blazor pages and the assertions are about what renders, so they are stated
as DOM reads rather than as "look at it", and a driver performs them unattended.

**Expected:** every row of the table below holds.

| Page | Assert |
|---|---|
| `/` | body text contains *started with a problem* and the real failure reason; **no** stack trace — `/at [A-Za-z.]+\(/` must not match |
| `/stats` | `document.title` contains `Statistics`; every rendered count is `0`; no stack trace |
| `/notifications` | body text contains *No notifications yet.*; `tbody tr` count is `0`; no stack trace |

**Console:** only `Failed to load resource: … 503` entries, from the API calls the pages make while
degraded. **Any other console error — a JS exception, a Blazor circuit error — is a failure**, and a
driver asserts that by filtering the console to errors and checking every one matches `503`.

**All four of these were confirmed automatable during #339's full run**, against a genuinely degraded
container reached by other means (a bind mount plus a host-side `DROP TABLE`, since this document's own
technique no longer degrades — see Preconditions). `/`, `/stats` and `/notifications` each returned
`200` and rendered exactly as above, with the console carrying only 503s. The step is written this way
so that whatever setup #327 gives it, the assertions are already runnable.

**One trap #327 should inherit:** a degraded container answers `/health` while still *starting*, and at
that point `/api/v1/notifications` returns `200` rather than `503`. Wait for the settled `unhealthy`
state, not merely for a response.

## Observed effect

**Measured 2026-08-18 and confirmed again 2026-08-26 during the PowerShell conversion: `200` healthy.**
The container does not degrade under this setup, so none of the degraded-page assertions above are
exercised at all.

The original incident this reproduced was real — a live HA v1.8.2 → v1.8.3-beta upgrade whose migration
failed partway through, leaving `NotificationSummary` (embedded in Home's modal) and `/notifications`
crashing instead of showing degraded UI. `System_Notification` genuinely does not exist on a real
v1.8.2 database (confirmed live:
`SELECT name FROM sqlite_master WHERE type='table' AND name='System_Notification'` returns no rows),
so the setup did once exercise `NotificationReader`'s missing-table fix and `DatabaseStatsSummary`'s
degraded-skip fix together. What no longer holds is the mechanism that made the migration fail.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-05
```
