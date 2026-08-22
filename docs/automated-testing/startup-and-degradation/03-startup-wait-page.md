# Kestrel serves a wait page during initialisation instead of appearing dead

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #280

## Preconditions

A **fresh volume**, so the container has real seeding work to do. Against an already-seeded volume
startup completes almost immediately and the window this test observes does not exist.

## Determinism

**This test deliberately observes a transient state, and that is why it keeps a fixed `sleep`.** The
first set of requests must land *before* seeding completes.

Polling for readiness would defeat the test outright. **And polling for the `starting` state itself is
worse than the sleep, not better** — a transient state may already have passed by the first poll, so
the loop would hang forever on a fast machine, where the sleep merely fails. A poll is the right tool
for waiting until something *becomes* true and stays true; it cannot catch a window that has closed.

The exposure is a race: on a fast enough machine seeding could finish inside the first second and the
requests would hit a ready app. **That failure mode is loud, not silent** — the assertions require
`503 {"status":"starting"}`, so catching the wrong state fails the test rather than passing it against
the wrong thing. A false negative, never a false positive. If it starts failing spuriously, the fix is
a larger seed or a slower start, not a longer sleep.

The second wait is an ordinary readiness wait and polls.

## Steps

**Immediately after container start, before seeding completes:**

```bash
docker volume rm smoke280-data 2>/dev/null
docker rm -f smoke280
MSYS_NO_PATHCONV=1 docker run -d --name smoke280 -p 8080:8080 -v smoke280-data:/data \
  -e Quotinator__DataDir=/data quotinator:local
sleep 1
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/api/v1/health"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/api/v1/version"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/"
```

**After seeding completes:**

```bash
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:8080/api/v1/health"
curl -s "http://localhost:8080/api/v1/version"
docker logs smoke280 2>&1 | grep "Now listening on\|Server] listening on\|Quotinator ready"
```

## Expected output

**During initialisation:**

- `/health` returns `503 {"status":"starting"}`
- `/version` returns `200 {"status":"starting","version":"..."}` — with no environment or database
  fields
- `/` returns `200` with a self-contained HTML wait page: auto-refresh meta tag, localized heading and
  body, no external assets. Never a hang, never a raw error.

**After seeding:**

- `/health` returns `200 {"status":"healthy"}`
- `/version` returns `200 {"status":"ready", ..., "database": {...}}` with real counts

**Log ordering is itself an assertion.** `Microsoft.Hosting.Lifetime`'s own `Now listening on` — Kestrel
actually bound — must appear **before** the app's own `[Server] listening on` / `Quotinator ready`
banner. That ordering is what proves Kestrel accepted connections for the whole wait-page window rather
than only after it.

## Observed effect

Well established. The log ordering above *is* an observed effect, and the wait page's own content —
self-contained, auto-refreshing, localized — is the user-visible state during a window that would
otherwise look like a dead server.

## Cleanup

```bash
docker rm -f smoke280
docker volume rm smoke280-data
```
