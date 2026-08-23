# Degraded-state pages survive a genuine migration failure

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #293

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.2` tag**, chosen because `System_Notification` genuinely does not
exist on a real v1.8.2 database. It runs its own container and volume (`smoke293` / `smoke293-data`,
the name reused across the two runs) rather than `qt-env`, and the Constrained defect is intended to be
`--read-only` on the root filesystem with `/data` left writable — which is the part that no longer
works.

**Its premise is unreachable, measured 2026-08-18.** This test forces a migration failure with
`--read-only` on the root filesystem while `/data` stays a writable volume. #294 subsequently made
exactly that arrangement survivable — the migration's temp files never touch disk, so restricting
every other path no longer causes a failure.

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

```bash
docker rm -f smoke293 2>/dev/null
docker volume rm smoke293-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name smoke293 -p 8080:8080 \
  -v smoke293-data:/data -e Quotinator__DataDir=/data \
  ghcr.io/dutchjafo/quotinator:1.8.2
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
docker stop -t 15 smoke293 && docker rm smoke293
```

**Expected:** `quotes` is non-zero and the seed reports zero failures.

**On failure:** a partially-seeded volume would make everything below meaningless. Stop and re-seed
rather than proceeding.

### 2. Start the current build with a read-only root filesystem and read health

```bash
MSYS_NO_PATHCONV=1 docker run -d --name smoke293 -p 8080:8080 \
  --read-only \
  -v smoke293-data:/data -e Quotinator__DataDir=/data \
  quotinator:local
until curl -s -o /dev/null http://localhost:8080/api/v1/health; do sleep 1; done
curl -s -w " [%{http_code}]\n" http://localhost:8080/api/v1/health
```

**Expected:** `/health` returns
`503 {"status":"unhealthy",...}`, confirming the test reached the failure state.

**On failure:** `200` healthy is what this setup actually measures today (see Observed effect). That is
the known, tracked contradiction, not a new result — stop rather than running the degraded-page steps
against a container that never degraded.

### 3. Read the degraded pages and the notifications API

```bash
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/stats"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/notifications"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/api/v1/notifications"
```

**Expected:** `/`, `/stats` and
`/notifications` all return `200` — never `500`, never a raw exception page.
`GET /api/v1/notifications` correctly returns `503`; API traffic stays gated while degraded, which is
the design
[`01-seeding-backup-degraded-startup-and-reset-recovery.md`](01-seeding-backup-degraded-startup-and-reset-recovery.md)
covers, and expected rather than a regression.

### 4. Visit the three Blazor pages in a real browser

Visit `http://localhost:8080/`, `http://localhost:8080/stats` and
`http://localhost:8080/notifications` in a real browser.

**Expected:**

- `/` renders `StartupErrorModal` (*Quotinator started with a problem*) with the real failure reason
  and all-zero stats, not a raw stack trace
- `/stats` renders the Statistics page with all-zero counts
- `/notifications` renders `No notifications yet.` — `NotificationReader` catching the missing-table
  exception and returning empty, which the page renders as an empty list rather than an unhandled
  exception

Browser console: `Failed to load resource: 503` entries are expected, from other API calls the page
makes while degraded. Anything else — a JS exception, a Blazor circuit error — is not.

## Observed effect

**Measured 2026-08-18: `200` healthy.** The container does not degrade under this setup, so none of the
degraded-page assertions above are exercised at all.

The original incident this reproduced was real — a live HA v1.8.2 → v1.8.3-beta upgrade whose migration
failed partway through, leaving `NotificationSummary` (embedded in Home's modal) and `/notifications`
crashing instead of showing degraded UI. `System_Notification` genuinely does not exist on a real
v1.8.2 database (confirmed live:
`SELECT name FROM sqlite_master WHERE type='table' AND name='System_Notification'` returns no rows),
so the setup did once exercise `NotificationReader`'s missing-table fix and `DatabaseStatsSummary`'s
degraded-skip fix together. What no longer holds is the mechanism that made the migration fail.

## Cleanup

```bash
docker rm -f smoke293 2>/dev/null
docker volume rm smoke293-data
```

The container and volume are this test's own, so restoring the profile clears nothing it made.
