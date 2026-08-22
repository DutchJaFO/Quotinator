# Degraded-state pages survive a genuine migration failure

**Smoke:** no
**Traces to:** #293

> **This test cannot pass as written, and #327 is rewriting it.** It is carried into the new structure
> unchanged rather than silently dropped — see Preconditions for why it can never reach its own
> premise. Do not run it expecting a result; do not "fix" it by editing the expected status code.

## Preconditions

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

## Determinism

Not established, and that is the defect. The original intended, but never pinned:

- a genuine migration failure — which the technique below no longer produces
- `System_Notification` genuinely absent — true on a real v1.8.2 database, so that part held

## Steps

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

Seeds a real, unmodified v1.8.2 database. `quotes` must read `799` before proceeding.

```bash
MSYS_NO_PATHCONV=1 docker run -d --name smoke293 -p 8080:8080 \
  --read-only \
  -v smoke293-data:/data -e Quotinator__DataDir=/data \
  quotinator:local
until curl -s -o /dev/null http://localhost:8080/api/v1/health; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:8080/api/v1/health"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/stats"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/notifications"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/api/v1/notifications"
```

## Expected output

**As originally written — and unreachable, see Preconditions:**

`/health` returns `503 {"status":"unhealthy",...}`, confirming the test reached the failure state.
`/`, `/stats` and `/notifications` all return `200` — never `500`, never a raw exception page.
`GET /api/v1/notifications` correctly returns `503`; API traffic stays gated while degraded, which is
#254's design and expected rather than a regression.

Visiting the three Blazor pages in a real browser, the intended content was:

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
docker rm -f smoke293
docker volume rm smoke293-data
```
