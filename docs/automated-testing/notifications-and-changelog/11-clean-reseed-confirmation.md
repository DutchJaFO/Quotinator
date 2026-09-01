# A reseed confirms each file that applied cleanly, once per result

**Smoke:** no
**Environment:** Fresh
**Traces to:** #302

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-notif-11`, publishing `19511`, on the
current build. Reseed requires `X-Api-Key`, so the container is created with
`--env Quotinator__AdminApiKey=t2-302` and every admin call below sends that header.

A reseed already reported a per-file breakdown, but only in the API response and the container log.
This proves the confirmation reaches the notification history, that it says what the file actually did
per entity type, that reseeding again does not duplicate it, and that dismissing it lets the next
reseed confirm afresh.

## Determinism

**The first seed must have finished before the reseed runs.** A reseed racing the initial seed would
confirm files the test never established were clean, so the wait polls the quote count for a non-zero
value rather than sleeping for a duration — the same gate
[`10`](10-reseed-recommendation-and-action.md) uses.

**Count this confirmation, never the total number of notifications.** How many notifications exist
depends on which producers are present and on what the bundled changelog flags for the running
version, both of which move every milestone. Every count here filters on `metadataKind` of
`reseedFileApplied`. PowerShell's `-eq` is case-insensitive, which is why this matches the API's
lower-cased `reseedfileapplied`.

**Count only *active* confirmations where the test is about dedupe.** `GET /notifications` returns the
full history, dismissed rows included. Step 4 dismisses rows and then expects new ones, so a count that
ignored `isDismissed` could never fall back and would pass whatever happened.

**Count objects, not matching lines, and re-wrap in `@(...)` at the call site.** These responses are
single-line JSON, so a line-based match reports `1` however many copies exist; and PowerShell 5.1
unrolls a single-element array on return, so a bare `$x.Count` prints empty for exactly one match.
Both failure modes are recorded on [`10`](10-reseed-recommendation-and-action.md), which found them the
hard way — every count here is wrapped, including the ones expected to be zero.

**The number of files is read, never predicted.** How many bundled files apply cleanly depends on the
manifest and on whether a file's conflicts are fully resolved by its rule file, both of which change
when a source is updated. Step 2 records the count it observes and every later step compares against
that recorded number rather than a literal.

## Steps

### 1. Seed a fresh database and confirm cold start says nothing per file

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-11 --port 19511 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-302

function Get-QuoteCount { (Invoke-RestMethod "http://localhost:19511/api/v1/quotes?page=1&pageSize=1").totalCount }
function Get-Confirmations {
  $items = (Invoke-RestMethod "http://localhost:19511/api/v1/notifications?pageSize=0").items
  @($items | Where-Object { $_.metadataKind -eq 'reseedFileApplied' -and -not $_.isDismissed })
}

while ((Get-QuoteCount) -lt 1) { Start-Sleep 2 }
"quotes seeded = $(Get-QuoteCount)"
"confirmations after first seed = $(@(Get-Confirmations).Count)"
```

**Expected:** a non-zero `quotes seeded`, and `confirmations after first seed = 0`.

The zero is the point, not a formality: the startup modal already reports aggregate counts on a fresh
install, and repeating that per file would be clutter. A non-zero count here also means step 2 could
not attribute what it sees to the reseed. Stop if it is not zero.

### 2. Reseed, and confirm each cleanly-applied file

```powershell
$headers = @{ "X-Api-Key" = "t2-302" }
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19511/api/v1/admin/database/reseed" | Out-Null

$confirmations = @(Get-Confirmations)
$expected = $confirmations.Count
"confirmations after reseed = $expected"
$confirmations | ForEach-Object { "type=$($_.type) appVersionId=$($_.appVersionId) title=$($_.title)" }
```

**Expected:** `confirmations after reseed` is at least 1, every row reporting `type=success` (the API
serializes the type in lower case), a non-empty `appVersionId`, and a non-empty title.

`appVersionId` is not decoration here. Provenance was stored but exposed nowhere before this issue, so
an unattributed notification was indistinguishable from an attributed one from outside the database. An
empty value means the initializer wrote without establishing the version first.

### 3. Confirm the breakdown covers more than quotes

```powershell
$payload = $confirmations[0].metadata | ConvertFrom-Json
"file=$($payload.fileName)"
$payload.counts | ForEach-Object { "$($_.entityType): added=$($_.added) modified=$($_.modified)" }
```

**Expected:** a non-empty `file`, and at least one non-quote `entityType` line. No line may read
`added=0 modified=0` — an untouched type is omitted rather than stored as a pair of zeros.

**Do not require `Quote` on every file.** Measured on this document's first run: of the four bundled
files, `quotinator-series-universe.json` reports `Source: added=69` and carries no `Quote` line at all.
That file is also the clearest evidence for why the breakdown exists — under the quote-only counts this
issue replaced, it would have confirmed itself as "0 added, 0 updated", reporting a silent no-op for a
file that had just added 69 Sources.

### 4. Reseed again, and confirm it does not duplicate

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19511/api/v1/admin/database/reseed" | Out-Null

"confirmations after second reseed = $(@(Get-Confirmations).Count) (expected $expected)"
```

**Expected:** the same count as step 2.

These notifications deliberately never expire, so a count that grows on every reseed is an unbounded
list nothing clears. Reseeding unchanged content produces the same per-file result, which is the same
confirmation the operator is already looking at.

### 5. Dismiss, reseed, and confirm it confirms afresh

```powershell
$all = (Invoke-RestMethod "http://localhost:19511/api/v1/notifications?pageSize=0").items
@($all | Where-Object { $_.metadataKind -eq 'reseedFileApplied' -and -not $_.isDismissed }) |
  ForEach-Object { Invoke-RestMethod -Method Post -Headers $headers `
    "http://localhost:19511/api/v1/notifications/$($_.id)/dismiss" | Out-Null }

"confirmations after dismissal = $(@(Get-Confirmations).Count)"

Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19511/api/v1/admin/database/reseed" | Out-Null

"confirmations after dismiss-then-reseed = $(@(Get-Confirmations).Count) (expected $expected)"
```

**Expected:** `confirmations after dismissal = 0`, then the step 2 count again.

This is the positive control step 4 needs. Without it, "does not duplicate" is indistinguishable from
"only ever writes once", and a confirmation the operator had dealt with would silently suppress every
later reseed — the failure mode dedupe-while-active exists to avoid.

### 6. Confirm the reseed's own dismissal did not sweep them away

```powershell
@(Get-Confirmations) | ForEach-Object { "isDismissed=$($_.isDismissed) expiresAt=$($_.expiresAt)" }
```

**Expected:** every line reads `isDismissed=False` with an empty `expiresAt`.

`POST /admin/database/reseed` dismisses every `Reseed`-triggered notification once the reseed returns.
A confirmation carrying that trigger would be wiped out by the very reseed that wrote it, and the only
place that shows is a live call where both run in order.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-11
```
