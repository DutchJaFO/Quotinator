# During initialisation Kestrel answers, refusing with 503 rather than appearing dead or claiming success

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #280, #419
**Fully green after:** [#438](https://github.com/DutchJaFO/Quotinator/issues/438) — step 1 asks
`/version` for `200` where its own prose says `503`, and never sends a browser `Accept`, so its
`autoRefresh` assertion cannot hold; steps 2, 3 and 4 pass

## Preconditions

**Beyond the profile.** This test starts its own container (`qt-startup-03` / `qt-startup-03-data`) because it
must issue requests *during* the startup window, before the profile's readiness poll would return: the
profile hands back an already-healthy app, which is precisely the state this test cannot observe from.
The volume must be new for the same reason it is new in the profile: against an already-seeded volume
startup completes almost immediately and the window this test observes does not exist.

## Determinism

**This test deliberately observes a transient state, and that is why it keeps a fixed `Start-Sleep`.**
The first set of requests must land *before* seeding completes.

Polling for readiness would defeat the test outright. **And polling for the `starting` state itself is
worse than the sleep, not better**, because a transient state may already have passed by the first poll, so
`--wait-for 503` would spend its whole timeout on a fast machine, where the sleep merely fails. A poll
is the right tool for waiting until something *becomes* true and stays true; it cannot catch a window
that has closed.

The exposure is a race: on a fast enough machine seeding could finish inside the first second and the
requests would hit a ready app. **That failure mode is loud, not silent**: the assertions require
`503` and `status=starting`, so catching the wrong state fails the test rather than passing it against
the wrong thing. A false negative, never a false positive. If it starts failing spuriously, the fix is
a larger seed or a slower start, not a longer sleep.

The second wait is an ordinary readiness wait and polls.

## Steps

### 1. Request the three surfaces during initialisation, before seeding completes

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-03 --port 18403 --no-wait
Start-Sleep 1

dotnet script scripts/testing/http.csx -- --url "http://localhost:18403/api/v1/health" --expect 503
$version = dotnet script scripts/testing/http.csx -- --url "http://localhost:18403/api/v1/version" --expect 200 | ConvertFrom-Json
"version status=$($version.status) hasDatabase=$($version.PSObject.Properties.Name -contains 'database')"

$page = dotnet script scripts/testing/http.csx -- --url "http://localhost:18403/" --expect 503 | Out-String
"autoRefresh=$([bool]($page -match '(?i)<meta[^>]+http-equiv\s*=\s*.refresh'))"
"externalAssets=$(([regex]::Matches($page, '(?i)(src|href)\s*=\s*["'']https?://')).Count)"
```

**Expected:**

- `/health` returns `503` with `{"status":"starting"}`
- `/version` returns `503`. It is gated like everything else (#419): it cannot report complete version
  information or a ready state before startup finishes, and a caller could otherwise reach for it as a
  readiness endpoint in `/health`''s place
- `/` returns `503` carrying the self-contained HTML wait page, with `autoRefresh=True` and
  `externalAssets=0`: it refreshes itself and pulls in nothing from outside. Never a hang, never a raw
  error, and never a `200` for a page the caller did not ask for

**On failure:** a `200` from `/health` means seeding finished before the requests landed: the window
was missed rather than the wait page being broken (see Determinism). Stop and re-run; do not lengthen
the sleep.

### 2. A write during that same window is refused, and the body follows what the caller asked for

The requests above are a browser''s. These are an API client''s, in the same window, from the same
container.

```powershell
$api = @{ "X-Api-Key" = "smoketest"; "Accept" = "application/json" }
try {
  Invoke-RestMethod "http://localhost:18403/api/v1/admin/database/reset?allowNoBackup=true" -Method POST -Headers $api -TimeoutSec 20
  "reset during startup = 200 UNEXPECTED"
} catch {
  $r = $_.Exception.Response
  "reset during startup = $([int]$r.StatusCode)  contentType=$($r.Content.Headers.ContentType)  retryAfter=$($r.Headers.RetryAfter)"
}

try { Invoke-RestMethod "http://localhost:18403/api/v1/quotes/random" -Headers $api -TimeoutSec 20 }
catch { "GET json during startup = $([int]$_.Exception.Response.StatusCode)  contentType=$($_.Exception.Response.Content.Headers.ContentType)" }
```

**Expected:** both report `503`. The reset carries `application/problem+json` and a `Retry-After`; the
JSON GET carries `application/problem+json` too, not the HTML page step 1 received.

**This is the defect #419 exists to remove.** Before it, this exact reset answered **`200` with
`text/html`**: an API caller was told its reset had succeeded, handed a web page, and nothing was reset.
Measured 2026-10-03.

**The two bodies in one window are the point.** Step 1 asked for a page and got one; this step asked for
data and got problem details. Either assertion alone passes against a middleware that serves one body to
everybody.

**On failure:** a `200` from either means the window was missed, as in step 1. A `503` carrying
`text/html` for the JSON request means the refusal ignores `Accept`.

### 3. Re-read health and version after seeding completes

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18403/api/v1/health" --wait-for 200 --status
(Invoke-RestMethod "http://localhost:18403/api/v1/health").status
$ready = Invoke-RestMethod "http://localhost:18403/api/v1/version"
"status=$($ready.status) quotes=$($ready.database.quotes)"
```

**Expected:** `/health` returns `200` and `healthy`; `/version` reports `status=ready` with a non-zero
`quotes` count: the same two fields that were absent in step 1, now populated.

### 4. Confirm Kestrel bound before the app's own banner

```powershell
$log = docker logs qt-startup-03 2>&1 | Out-String
$kestrel = $log.IndexOf('Now listening on')
$banner  = $log.IndexOf('Quotinator ready')
"kestrelAt=$kestrel bannerAt=$banner kestrelFirst=$(($kestrel -ge 0) -and ($banner -gt $kestrel))"
```

**Expected:** **log ordering is itself an assertion.** `kestrelFirst=True`:
`Microsoft.Hosting.Lifetime`'s own `Now listening on`, meaning Kestrel actually bound, appears
**before** the app's own `Quotinator ready` banner. That ordering is what proves Kestrel accepted
connections for the whole wait-page window rather than only after it. Compared by position rather than
read by eye, so a reversed order fails rather than being scrolled past.

## Observed effect

Well established. The log ordering above *is* an observed effect, and the wait page's own content,
self-contained, auto-refreshing, localized, is the user-visible state during a window that would
otherwise look like a dead server.

**#419 measured 2026-10-03**, red against `quotinator:canary419` (built from the commit before any of
this issue's code) and green against `quotinator:local`:

| In the startup window | canary (before) | local (after) |
|---|---|---|
| `POST /admin/database/reset` | `200`, `text/html` | `503`, `application/problem+json`, `Retry-After: 2` |
| `GET /quotes/random`, `Accept: application/json` | `200`, `text/html` | `503`, `application/problem+json` |
| `GET /`, `Accept: text/html` | `200` | `503`, `text/html`, still auto-refreshing, no external assets |
| `GET /version` | `200`, reduced shape | `503` |
| `GET /health` | `503 starting` | `503 starting`, unchanged |

After the same container reached ready, 11.5 s later: `/version` reported `status=ready`,
`hasDatabase=True`, `795` quotes, and the same reset answered `200`. No `[Runtime - Exception]` line in
the run.

The `200` with `text/html` for a reset is the whole of what this issue removes: an API caller was told
its write had succeeded, handed a web page, and nothing had been written.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-03
```
