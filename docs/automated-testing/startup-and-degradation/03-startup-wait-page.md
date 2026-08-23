# Kestrel serves a wait page during initialisation instead of appearing dead

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #280

## Preconditions

**Beyond the profile.** This test starts its own container (`qt-startup-03` / `qt-startup-03-data`) because it
must issue requests *during* the startup window, before the profile's readiness poll would return — the
profile hands back an already-healthy app, which is precisely the state this test cannot observe from.
The volume must be new for the same reason it is new in the profile: against an already-seeded volume
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

### 1. Request the three surfaces during initialisation, before seeding completes

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-03 --port 18403 --no-wait
sleep 1
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:18403/api/v1/health"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:18403/api/v1/version"
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:18403/"
```

**Expected:**

- `/health` returns `503 {"status":"starting"}`
- `/version` returns `200 {"status":"starting","version":"..."}` — with no environment or database
  fields
- `/` returns `200` with a self-contained HTML wait page: auto-refresh meta tag, localized heading and
  body, no external assets. Never a hang, never a raw error.

**On failure:** a healthy `200 {"status":"healthy"}` here means seeding finished before the requests
landed — the window was missed rather than the wait page being broken (see Determinism). Stop and
re-run; do not lengthen the sleep.

### 2. Re-read health and version after seeding completes

```bash
until curl -sf http://localhost:18403/api/v1/health > /dev/null; do sleep 1; done
curl -s -w "\nHTTP %{http_code}\n" "http://localhost:18403/api/v1/health"
curl -s "http://localhost:18403/api/v1/version"
```

**Expected:**

- `/health` returns `200 {"status":"healthy"}`
- `/version` returns `200 {"status":"ready", ..., "database": {...}}` with real counts

### 3. Confirm Kestrel bound before the app's own banner

```bash
docker logs qt-startup-03 2>&1 | grep "Now listening on\|Server] listening on\|Quotinator ready"
```

**Expected:** **log ordering is itself an assertion.** `Microsoft.Hosting.Lifetime`'s own
`Now listening on` — Kestrel actually bound — must appear **before** the app's own
`[Server] listening on` / `Quotinator ready` banner. That ordering is what proves Kestrel accepted
connections for the whole wait-page window rather than only after it.

## Observed effect

Well established. The log ordering above *is* an observed effect, and the wait page's own content —
self-contained, auto-refreshing, localized — is the user-visible state during a window that would
otherwise look like a dead server.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-03
```
