# Degraded-state pages survive a genuine migration failure

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #293

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.2` tag**, chosen because it leaves a genuinely un-migrated database —
`System_Notification` does not exist on it at all. One container name (`qt-startup-05`) is reused across
the two runs against one named volume, and the Constrained defect is **`/data` itself mounted
read-only** while a migration is pending.

**Two conditions have to hold together, and neither is sufficient alone** (measured 2026-08-27):

| `/data` | Migration pending | Result |
|---|---|---|
| Writable, root filesystem `--read-only` | yes | `200` healthy — this is #294's fix working |
| Read-only | no, already migrated | `200` healthy, with a `SQLite Error 8` logged |
| **Read-only** | **yes** | **`503` unhealthy, `SQLite Error 14`** |

**The third row is this test.** The initializer has real work to do and cannot write, so it degrades —
and `SQLite Error 14: 'unable to open database file'` is *the original incident's own error code*.

## Determinism

- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`04-migration-replay-under-restricted-write.md`](04-migration-replay-under-restricted-write.md)'s
  subject and the application survives it by design. Using that flag here reproduces `04` and asserts
  the opposite of it.
- **The prior image must leave a migration pending.** Run this against a volume the current build has
  already upgraded and it reports `200` with only a `SQLite Error 8` in the log — the write fails, but
  there is no migration for it to fail *during*, so nothing degrades. That is why step 1 seeds from a
  published tag and step 2 never re-runs against its own output.
- **WAL sidecar state does not decide this.** Measured both ways — cleanly stopped so the sidecars are
  checkpointed away, and force-killed so they survive — and both degrade. #326 found sidecar state
  decisive for a read-only mount *without* a pending migration; that is a different question from this
  one, and this document does not depend on it.
- **`reenter` stops the previous container cleanly** (`docker stop -t 15`, not `rm -f`), so which of the
  two sidecar states this runs in is pinned rather than incidental.
- **The second start waits for *listening*, not healthy.** Degrading is the expected outcome, so waiting
  for `200` would spend the whole timeout and then fail for the wrong reason.

**What this replaced, and why the old version could never pass.** Until 2026-08-27 this test forced the
failure with `--read-only` on the root filesystem, leaving `/data` writable — byte-identical to `04`'s
setup, which asserts the app is **healthy** under it. Two tests cannot both be right about the same
setup. Its own guard said *"health must be 503, confirming the test actually reached the failure
state"*, and that guard could never hold: measured `200` on 2026-08-18 and again on 2026-08-26.

The guard was the right instinct and is why the contradiction was provable rather than merely
suspected. What was missing was a step that *confirmed* the failure state before asserting against it —
and, as it turned out, a technique that produced one. The premise was never unreachable; the lever was
wrong.

**[#327](https://github.com/DutchJaFO/Quotinator/issues/327) proposed three replacement scenarios** — a
`:ro` volume mount with pinned WAL sidecar state, a corrupt database file, and a schema version ahead of
the application. This document now implements the first. The other two remain that issue's to add, and
its scope is worth revisiting rather than assumed.

## Steps

### 1. Seed a real, unmigrated v1.8.2 database

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-05 --port 18405 `
  --image ghcr.io/dutchjafo/quotinator:1.8.2

(Invoke-RestMethod "http://localhost:18405/api/v1/version").database.quotes
```

**Expected:** `quotes` is non-zero and the seed reports zero failures.

**On failure:** a partially-seeded volume would make everything below meaningless. Stop and re-seed
rather than proceeding.

### 2. Start the current build with `/data` read-only, and read health

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 `
  --image quotinator:local --read-only-data --wait-listening

$health = dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/health" --expect 503 | ConvertFrom-Json
"status=$($health.status)"

$log = docker logs qt-startup-05 2>&1 | Out-String
"sqliteError14=$(([regex]::Matches($log, "SQLite Error 14")).Count)"
```

**Expected:** `503` with `status=unhealthy`, and `sqliteError14` non-zero — the migration genuinely
could not write, which is the failure state every step below depends on.

**On failure:** a `200` means the volume was already migrated, so there was no pending migration for the
read-only mount to fail during — re-seed from the published tag rather than re-running step 2 against
its own output. `--expect 503` ends the step here either way, rather than letting the degraded-page
assertions run against a container that never degraded.

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
container. `/`, `/stats` and `/notifications` each returned `200` and rendered exactly as above, with
the console carrying only 503s.

**One trap to keep:** a degraded container answers `/health` while still *starting*, and at
that point `/api/v1/notifications` returns `200` rather than `503`. Wait for the settled `unhealthy`
state, not merely for a response — which is what step 2's `--expect 503` establishes before step 3
runs.

## Observed effect

**Measured 2026-08-27: `503 unhealthy`, with `SQLite Error 14: 'unable to open database file'`.**

That code matters. The original incident — a live HA v1.8.2 → v1.8.3-beta upgrade whose migration failed
partway through, leaving `NotificationSummary` (embedded in Home's modal) and `/notifications` crashing
instead of showing degraded UI — reported exactly `SQLite Error 14`. The `--read-only`-root technique
this replaced could not reproduce it: that arrangement denies writes at a different syscall and produced
`SQLite Error 10: 'disk I/O error'` when it degraded at all, which its own Observed effect recorded as
"same class of failure, different code; an exact match is not expected". Denying `/data` reproduces the
incident's code exactly.

`System_Notification` genuinely does not exist on a real v1.8.2 database (confirmed live:
`SELECT name FROM sqlite_master WHERE type='table' AND name='System_Notification'` returns no rows), so
this setup exercises `NotificationReader`'s missing-table fix and `DatabaseStatsSummary`'s
degraded-skip fix together — which is what the test is for.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-05
```
