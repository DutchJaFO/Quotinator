# A file left awaiting review raises an alert, and resolving it retires the alert

**Smoke:** no
**Environment:** Fresh
**Traces to:** #303

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-review-20`, publishing `19520`, on the
current build, created with `--env Quotinator__AdminApiKey=t2-303`.

A staged batch already tracked conflicts precisely, but only `/import/actions` and the container log
said so. This proves the alert appears, names the batch it reports, survives to the startup modal, is
retired when the review is resolved or its batch removed, and that `/import-review` stays reachable
while the database is degraded.

**`Environment: Fresh` is load-bearing here, not a default.** Against a database that already holds
quotes, this test's fixture does nothing at all: the files are discovered and a manifest is written for
them, and then seeding returns early on the non-empty Quotes table without reading them — see
[#368](https://github.com/DutchJaFO/Quotinator/issues/368). Do not relax the fresh-container
precondition until that is fixed; a run against a reused container proves nothing and passes its
zero-state checks for the wrong reason.

## Determinism

**The bundled content alone cannot produce a conflict, so this test supplies one.** Measured on the
first run: a container started normally reports `pending actions = 0`, and setting
`Quotinator__DefaultConflictPolicy=Review` does not change that. A conflict needs *existing* data that
disagrees with what is arriving, and a first seed inserts everything as an Add — there is nothing yet
to disagree with. The manifest's own per-file policy also overrides the config default, so the flag
never reaches the bundled files anyway.

Step 1 therefore bind-mounts a user-imports file that re-states an already-bundled quote id with
different text, under a `review` policy. The user-imports batch seeds after the bundled ones, so it
meets content that is already stored — which is the only shape that stages a decision.

`scripts/testing/stage-import-conflict.csx` writes that fixture, rather than this document composing it
inline. T1 hits the same wall for the same reason and needs the same file, and a fixture defined in two
places drifts — the copy that is not being run stops matching what the code does.

**Count this alert, never the total number of notifications.** Every count filters on `metadataKind` of
`importReviewPending`. PowerShell's `-eq` is case-insensitive, which is why this matches the API's
lower-cased `importreviewpending`.

**Count objects, not matching lines, and re-wrap in `@(...)` at the call site.** Both failure modes are
recorded on [`../notifications-and-changelog/10-reseed-recommendation-and-action.md`](../notifications-and-changelog/10-reseed-recommendation-and-action.md),
which found them the hard way — single-line JSON makes a line match report `1` however many exist, and
PowerShell 5.1 unrolls a single-element array so a bare `.Count` prints empty.

**`Obsolete` and `Resolved` are different outcomes and must be asserted apart.** An alert whose batch
was truncated was not reviewed; one whose actions were decided was. A test that only checked
`isDismissed` would pass for both and prove neither.

## Steps

### 1. Seed a database whose content leaves actions awaiting review

```powershell
$bind    = Join-Path $env:TEMP "qt-review-20-bind"
$imports = Join-Path $bind "imports"

# Writes a user-imports file re-stating a real bundled quote's id with different text, under a `review`
# policy. Shared with T1, which needs the same fixture for the same reason.
dotnet script scripts/testing/stage-import-conflict.csx -- --imports $imports

dotnet script scripts/testing/test-env.csx -- create --name qt-review-20 --port 19520 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-303 --bind $bind

$headers = @{ "X-Api-Key" = "t2-303" }
function Get-ReviewAlerts {
  $items = (Invoke-RestMethod "http://localhost:19520/api/v1/notifications?pageSize=0").items
  @($items | Where-Object { $_.metadataKind -eq 'importReviewPending' })
}
function Get-ActiveReviewAlerts { @(Get-ReviewAlerts | Where-Object { -not $_.isDismissed }) }
function Get-PendingActionCount {
  (Invoke-RestMethod "http://localhost:19520/api/v1/import/actions?status=Pending&pageSize=1").totalCount
}

while ((Invoke-RestMethod "http://localhost:19520/api/v1/quotes?page=1&pageSize=1").totalCount -lt 1) { Start-Sleep 2 }
"pending actions = $(Get-PendingActionCount)"
"active alerts   = $((Get-ActiveReviewAlerts).Count)"
```

**Expected:** `pending actions = 1`, `active alerts = 1`, naming `conflicting.json` with `origin=User`
and a `Pending: 1` count.

**On failure:** if `pending actions` is `0`, the id in the import did not match a bundled quote, so it
was an Add and nothing was staged. Stop — every later step would pass against an empty set and prove
nothing.

### 2. Confirm the alert names its batch, file and workload

```powershell
$alert = (Get-ActiveReviewAlerts)[0]
$payload = $alert.metadata | ConvertFrom-Json
"type=$($alert.type) appVersionId=$($alert.appVersionId)"
"file=$($payload.fileName) origin=$($payload.origin) batch=$($payload.batchId)"
$payload.counts | ForEach-Object { "   $($_.status): $($_.count)" }
```

**Expected:** `type=actionrequired`, a non-empty `appVersionId`, a real `batchId`, and at least one
status line with a non-zero count. No line may read a count of `0` — a state with nothing in it is
omitted rather than stored as a zero.

`batchId` is not decoration: it is what the alert's own dismissal matches on, so an alert that does not
carry one can never be retired by resolving its review.

### 3. Confirm it reaches the startup modal after a restart

```powershell
docker restart qt-review-20 | Out-Null
foreach ($i in 1..30) {
  try { if ((Invoke-RestMethod "http://localhost:19520/api/v1/health").status -eq 'healthy') { break } }
  catch { Start-Sleep 2 }
}
$html = (Invoke-WebRequest "http://localhost:19520/" -UseBasicParsing).Content
"alert text in modal: $($html.Contains('need your decision'))"
```

**Expected:** `True`. The modal is shown once per process run, so this needs the restart — the same
sequencing [`../notifications-and-changelog/11-clean-reseed-confirmation.md`](../notifications-and-changelog/11-clean-reseed-confirmation.md)
records for the confirmation half.

### 4. Confirm the review page is reachable and lists the work

```powershell
$page = (Invoke-WebRequest "http://localhost:19520/import-review" -UseBasicParsing).Content
"page served: $($page.Contains('Import review'))"
"lists conflicts: $($page.Contains('Keep existing'))"
```

**Expected:** both `True`. The page is server-rendered, so its content is in the HTML and needs no
browser automation.

### 5. Resolve the review, and confirm the alert records that it was done

The batch id is a query parameter, not a route segment — `/actions/discard?batchId=`, not
`/actions/{id}/discard`. The route-segment form returns `404`, which reads like "no such batch" rather
than "no such endpoint"; measured on this document's first run.

```powershell
$batchId = ((Get-ActiveReviewAlerts)[0].metadata | ConvertFrom-Json).batchId
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19520/api/v1/import/actions/discard?batchId=$batchId" | Out-Null

@(Get-ReviewAlerts | Where-Object { ($_.metadata | ConvertFrom-Json).batchId -eq $batchId }) |
  ForEach-Object { "isDismissed=$($_.isDismissed) dismissReason=$($_.dismissReason)" }
```

**Expected:** `isDismissed=True dismissReason=resolved`.

Discarding is a decision — the operator dealt with the batch by keeping none of it. `resolved` is what
separates that from a notification the user merely set aside, and from step 6's very different outcome.

### 6. Reseed twice, and confirm a removed batch's alert reads `Obsolete`

**Two reseeds, not one, and the order matters.** The first raises a fresh alert for its new batch while
the step-5 alert is already `resolved`; only the *second* truncates a batch whose alert is still
active, which is the sole path to `obsolete`. Running one reseed and expecting `obsolete` was this
document's own first-run error — the alert it checked had been resolved a step earlier.

```powershell
Invoke-RestMethod -Method Post -Headers $headers "http://localhost:19520/api/v1/admin/database/reseed" | Out-Null
Invoke-RestMethod -Method Post -Headers $headers "http://localhost:19520/api/v1/admin/database/reseed" | Out-Null

@(Get-ReviewAlerts) | ForEach-Object {
  "$((($_.metadata | ConvertFrom-Json)).batchId.Substring(0,8))  isDismissed=$($_.isDismissed)  reason=$($_.dismissReason)"
}
"active alerts = $((Get-ActiveReviewAlerts).Count)"
```

**Expected:** three alerts and all three outcomes visible at once — one active, one `reason=obsolete`,
one `reason=resolved` — with `active alerts = 1`.

That single line is the requirement itself: an inactive notification explains what happened to it
without anyone reading the audit trail. `obsolete` and `resolved` are different events — a batch
truncated out from under its alert was never reviewed — and a history that collapsed them would tell
the reader something untrue.

`active alerts = 1` across three seeding runs is what keeps the design bounded: a batch id is part of
an alert's identity and is a fresh GUID per batch, so every reseed necessarily raises a new alert.
Retiring the removed ones is the only thing stopping them accumulating.

### 7. Confirm the page still answers while the database is degraded

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-review-20
dotnet script scripts/testing/test-env.csx -- create --name qt-review-20d --port 19521 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-303 --read-only-data --wait-listening

foreach ($path in '/notifications','/import-review','/about','/stats') {
  try { $r = Invoke-WebRequest "http://localhost:19521$path" -UseBasicParsing; "$path -> $($r.StatusCode)" }
  catch { "$path -> $($_.Exception.Response.StatusCode.value__)" }
}
```

**Expected:** `/import-review` behaves exactly as `/notifications` does. `/about` and `/stats` answer
`200`.

**Both interactive pages currently answer `500`, and that is a known pre-existing defect, not this
issue's.** Measured here on the first run: `/notifications -> 500` and `/import-review -> 500`, while
`/about -> 200` and `/stats -> 200`. The cause is not the health gate — the exemption works, and both
paths are exempt — but `@rendermode InteractiveServer`, whose component-descriptor encryption needs
DataProtection, which cannot create `/data/keys` on a read-only mount:

```
System.Security.Cryptography.CryptographicException: An error occurred while trying to encrypt the provided data.
 ---> System.IO.IOException: Read-only file system : '/data/keys'
```

This test asserts parity rather than `200` on purpose. `/import-review` is exempt for the same reason
`/notifications` is — it is where an operator sees what is unresolved, which is exactly what they need
when the database is degraded — and when the underlying defect is fixed, both pages become reachable
together. A row asserting `200` here would have to be marked failing for a fault #303 did not cause and
does not own.

### 8. Confirm both dismiss reasons render as words, not as "Dismissed"

**Browser, not `Invoke-WebRequest`.** The status cell is rendered by an interactive circuit and the
question is what a person reads, so this step is driven and screenshotted rather than string-matched.

Against a container that has one resolved and one obsoleted alert (step 5 resolves one; a reseed while
the other is still active obsoletes it), open `http://localhost:19520/notifications` and switch the
filter to **All**.

**Expected:** the two inactive rows read **Done** and **No longer applicable** — different words for
different outcomes. Both reading "Dismissed" is the failure this step exists to catch: an alert whose
batch was truncated was never reviewed, and one whose actions were decided was, and an operator must be
able to tell those apart without opening the audit trail.

### 9. Confirm a decision reaches the data from both surfaces

Two controls can settle a review, and both must carry the decision all the way through. Capture the
stored value first so the change is proven rather than assumed:

```powershell
$a = (Invoke-RestMethod "http://localhost:19520/api/v1/import/actions?status=Pending&pageSize=0").items[0]
"stored   = $((Invoke-RestMethod "http://localhost:19520/api/v1/quotes/$($a.entityId)").quote)"
"incoming = $($a.incomingFields.quoteText)"
```

Then, in the browser: on `/notifications` use one alert's **Run → Take incoming**, and on
`/import-review` use another row's **Take incoming**.

```powershell
(Invoke-RestMethod "http://localhost:19520/api/v1/import/actions?pageSize=0").items |
  Select-Object @{n='Batch';e={$_.batchId.Substring(0,8)}}, status
"stored now = $((Invoke-RestMethod "http://localhost:19520/api/v1/quotes/$($a.entityId)").quote)"
```

**Expected:** every touched batch reads `Applied` — **not `Decided`** — the stored text now equals what
was incoming, and each alert is dismissed with reason `resolved`.

**On failure:** a batch left at `Decided` with its alert still `Active` is the defect this step was
written for (found 2026-09-01). Both controls decided without applying, so the operator's choice never
reached the data and the alert kept asking for a decision they had already made. Dismissal is wired to
`ApplyBatchAsync`/`DiscardBatchAsync`, never to deciding, so the stale alert is the visible symptom of
the unapplied batch — check the status before concluding the notification is at fault.

**Give the page a moment before clicking.** These controls need the Blazor circuit; a click issued
immediately after navigating is silently swallowed and the row simply stays `Pending`. Re-read the page
and click again rather than concluding the control is broken — measured here on the first attempt.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-review-20
dotnet script scripts/testing/test-env.csx -- destroy --name qt-review-20d
```
