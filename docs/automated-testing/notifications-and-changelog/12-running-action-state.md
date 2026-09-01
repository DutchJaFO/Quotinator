# A running notification action says so, and cannot be started twice

**Smoke:** no
**Environment:** Fresh
**Traces to:** #367

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-367`, publishing `19367`, on the current
build, created with `--env Quotinator__AdminApiKey=t2-367`.

A reseed run from `/notifications` takes about 11 seconds, during which the row used to keep reading
`Active` with a live **Run** button — so the natural reading was that the click had done nothing, and a
second confirmed click performed a second full reseed. This proves the row reports the run while it is
happening, that the control is withdrawn for its duration, and that a process dying mid-run leaves
nothing stranded.

## Determinism

**The state is deliberately not persisted, so every assertion here is about one process.** #367's
executing state is a process-scoped in-memory registry, not a column — see its
[plan](../../milestones/notification-system/367-executing-notification-state-plan.md) for why a stored
marker was rejected. Step 4 is the test of that choice: what a restart clears is exactly what a stored
column would have left behind.

**Only the page path claims the registry.** `POST /admin/database/reseed` runs the same reseed without
going through `/notifications`, so it never marks anything executing. Steps 1–3 must be driven through
the page; an API call would pass while proving nothing.

**Expect the click to need retrying.** These controls need the Blazor circuit, and a click issued
before it connects is silently swallowed. The console shows `WebSocket connected to .../_blazor` when
it is ready. Measured here: three clicks were lost this way before the first landed, and the page
briefly showed Blazor's own `Retry`/`Resume` disconnect overlay. Clicking through the DOM
(`document.querySelectorAll('button')`) is more reliable than coordinates, because the pane rescales
and stale coordinates miss.

## Steps

### 1. Produce an action worth running

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-367 --port 19367 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-367

$h = @{ "X-Api-Key" = "t2-367" }
Invoke-RestMethod -Method Post -Headers $h "http://localhost:19367/api/v1/admin/database/reset" | Out-Null
Start-Sleep -Seconds 3
$items = (Invoke-RestMethod "http://localhost:19367/api/v1/notifications?pageSize=0").items
@($items | Where-Object { $_.dismissTriggerKey -eq 'reseed' -and -not $_.isDismissed }).Count
```

**Expected:** `1` — a reseed recommendation, whose action takes long enough for the state to be
observable.

### 2. Confirm the row reports the run while it is running

In the browser: open `http://localhost:19367/notifications`, click **Run**, then **Confirm**, and
screenshot immediately — within the same round trip if the tooling allows, since the window is about
11 seconds.

**Expected:** the Status badge reads **Running…**, and the row's **Run** control is gone, leaving only
**Dismiss**.

**On failure:** a row still reading `Active` with a live Run button is the original defect. Check that
the handler flushes a render *before* awaiting the executor — `StateHasChanged` alone only queues one,
and without the yield the circuit stays on the previous frame for the whole action. Every unit test
still passes in that state, which is why this step exists.

### 3. Confirm the run happened exactly once, and settles

```powershell
Start-Sleep -Seconds 20
(docker logs qt-367 2>&1 | Select-String "reseed requested").Count
$items = (Invoke-RestMethod "http://localhost:19367/api/v1/notifications?pageSize=0").items
@($items | Where-Object { $_.dismissTriggerKey -eq 'reseed' }) |
  ForEach-Object { "dismissed=$($_.isDismissed) reason=$($_.dismissReason)" }
```

**Expected:** exactly `1` reseed request, and the alert `dismissed=True reason=resolved`. On the page
with **All** selected, that row reads **Done** — not **Running…** and not **Dismissed**.

The count is the assertion that matters: the Run control being withdrawn is what makes a second click
impossible, and a second `reseed requested` would mean the withdrawal is cosmetic.

### 4. Confirm a restart during a run strands nothing

Reset again for a fresh action, start it from the page as in step 2, then kill the process mid-run:

```powershell
docker restart qt-367 | Out-Null
foreach ($i in 1..40) {
  try { if ((Invoke-RestMethod "http://localhost:19367/api/v1/health").status -eq 'healthy') { break } }
  catch { Start-Sleep 2 }
}
$html = (Invoke-WebRequest "http://localhost:19367/notifications" -UseBasicParsing).Content
"page contains 'Running' : $($html -match 'Running')"
(Invoke-RestMethod "http://localhost:19367/api/v1/quotes?page=1&pageSize=1").totalCount
```

**Expected:** `False`, and a quote count well below a full seed (measured: `13` — only the first bundled
file had landed), confirming the run really was interrupted rather than finishing first. A restart that
completed the reseed proves nothing about stranding.

**On failure:** a row still reading **Running…** after a restart means the state outlived the process
that owned the run — the notification is now permanently unrunnable, with no Run control and no way
back. That is the failure mode a stored column would have had, and this step is what keeps the
in-memory choice honest if anyone later persists it.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-367
```
