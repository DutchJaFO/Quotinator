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

### 1. Seed a fresh database and confirm cold start confirms each file

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
@(Get-Confirmations) | ForEach-Object { "  $($_.title) — $($_.body)" }
```

**Expected:** a non-zero `quotes seeded`, and one confirmation per bundled file that applied cleanly,
each naming its own file.

**Inverted 2026-09-02, and the step got stronger for it.** It previously expected
`confirmations after first seed = 0`, on the grounds that the startup modal's aggregate summary already
covered a fresh install. It does not — that summary carries no file names, no origin, and no
added-versus-updated split, and it is shown after a reseed too, where the confirmations are written
anyway. The finding that forced this: two runs against an empty database, identical per-file reports,
four confirmations from the UI and none at startup.

**The old form could not fail for the right reason**, which this document's own *Canary* section already
recorded: `= 0` passes against a build with no producer at all, so it proved nothing on its own and
depended on step 2 to give it meaning. A presence assertion cannot pass that way.

**Step 2 changes with it.** Confirmations now already exist when it runs, and dedupe means an
unchanged reseed adds none — so "at least 1" would be satisfied by this step's own rows. Step 2
therefore dismisses them before reseeding, which is what keeps its count attributable to the reseed.

### 2. Reseed, and confirm each cleanly-applied file

```powershell
$headers = @{ "X-Api-Key" = "t2-302" }

# Dismiss what the cold start wrote, so what appears next is attributable to the reseed alone.
@(Get-Confirmations) | ForEach-Object {
  Invoke-RestMethod -Method Post -Headers $headers `
    "http://localhost:19511/api/v1/notifications/$($_.id)/dismiss" | Out-Null
}
"confirmations after dismissing cold start's = $(@(Get-Confirmations).Count)"

Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19511/api/v1/admin/database/reseed" | Out-Null

$confirmations = @(Get-Confirmations)
$expected = $confirmations.Count
"confirmations after reseed = $expected"
$confirmations | ForEach-Object { "type=$($_.type) appVersionId=$($_.appVersionId) title=$($_.title)" }
```

**Expected:** `confirmations after dismissing cold start's = 0`, then `confirmations after reseed` at
least 1, every row reporting `type=success` (the API serializes the type in lower case), a non-empty
`appVersionId`, and a non-empty title.

**The dismissal is what keeps this step meaningful, and it was added 2026-09-02 with step 1's
inversion.** Once cold start writes confirmations too, `at least 1` is satisfied by rows step 1 already
produced — so a build whose reseed path wrote nothing at all would still pass. Clearing them first
means the count can only come from the reseed. This is the same behaviour
`DatabaseInitializerTests.Reseed_AfterDismissal_WritesTheConfirmationAgain` asserts at unit level,
exercised here against a real reseed.

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

### 7. Confirm all four seeding variants, from real configuration

Steps 1–6 only ever exercise one of the four states seeding can be in. Which files a reseed sees comes
from configuration and from what is on disk, and neither is reachable from a unit test — a unit test
hands the initializer a batch list directly, so it can never prove that
`Quotinator__IncludeDefaultSources=false` actually produces an empty one, nor that a file dropped in
`{dataDir}/imports/` becomes a user-imports batch.

Each variant gets its own container, because the state is fixed at startup.

**Each variant needs its own bind directory, not just its own container.** `destroy` deliberately
leaves a bind directory in place — *"Its bind directory is this test's own to delete"* — so two
variants pointed at one directory share a **database**, and the second starts with the first's quotes
and notifications already in it. That is the opposite of what this step is for.

```powershell
function New-BindRoot($name) {
  $root = Join-Path $env:TEMP "qt-notif-11-bind-$name"
  Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
  $imports = Join-Path $root "imports"
  New-Item -ItemType Directory -Force $imports | Out-Null
  Copy-Item data/sources/quotinator-curated.json $imports -Force
  Copy-Item data/sources/manifest.json $imports -Force -ErrorAction SilentlyContinue
  $root
}

function Confirmations($port) {
  $items = (Invoke-RestMethod "http://localhost:$port/api/v1/notifications?pageSize=0").items
  @($items | Where-Object { $_.metadataKind -eq 'reseedFileApplied' -and -not $_.isDismissed })
}
function ReseedAndCount($name, $port, $extra) {
  dotnet script scripts/testing/test-env.csx -- create --name $name --port $port `
    --image quotinator:local --env Quotinator__AdminApiKey=t2-302 @extra
  Invoke-RestMethod -Method Post -Headers @{ "X-Api-Key" = "t2-302" } `
    "http://localhost:$port/api/v1/admin/database/reseed" | Out-Null
  $c = @(Confirmations $port)
  "$name -> $($c.Count) confirmation(s)"
  $c | ForEach-Object {
    $p = $_.metadata | ConvertFrom-Json
    "    $($p.fileName)  origin=$($p.origin)  counts=$(@($p.counts).Count)"
  }
  dotnet script scripts/testing/test-env.csx -- destroy --name $name | Out-Null
}

ReseedAndCount "qt-notif-11a" 19512 @("--env","Quotinator__IncludeDefaultSources=false")
ReseedAndCount "qt-notif-11b" 19513 @()
ReseedAndCount "qt-notif-11c" 19514 @("--env","Quotinator__IncludeDefaultSources=false","--bind",(New-BindRoot "c"))
ReseedAndCount "qt-notif-11d" 19515 @("--bind",(New-BindRoot "d"))
```

**Expected:**

| Variant | Container | Confirmations |
|---|---|---|
| No files | `11a` | `0`, and `/health` still `healthy` |
| Bundled only | `11b` | the step 2 count |
| User imports only | `11c` | one, `origin=User`, naming the file placed in `imports/` |
| Bundled + user imports | `11d` | the step 2 count plus one |

**`11d` must show the same file name twice, once per origin.** Copying a bundled file into `imports/`
is the ordinary way a user customises one, so both directories hold `quotinator-curated.json`; the two
confirmations are told apart by `origin`, not by name. The user copy reports `counts=0` because the
bundled copy applied that content first — that empty breakdown is kept deliberately, since it still
shows which sections were used.

**Read `origin` from the payload, not the count alone.** Before `origin` existed, these two rows were
distinguishable only by their breakdowns happening to differ — two same-named files that both applied
nothing shared an identity, and the second was silently suppressed. That is the defect this variant
found, and asserting only the total would not have caught it.

The zero is as load-bearing as the others. A reseed with nothing configured must be a clean no-op that
still answers `200` — not an error, and not a confirmation reporting that nothing happened. And `11c`
is what proves the confirmation is not a bundled-content feature: the clean-apply branch it is written
from sits immediately after an auto-purge step that *does* branch on origin, so origin is live in this
code path.

**On failure:** if `11c` reports `0`, check the imports directory actually reached `/data/imports`
inside the container before concluding the producer is origin-gated — a bind mount that did not land
looks identical from the API.

**If `11d` reports six rather than five, check the bind directory before anything else.** Found
2026-09-02 while re-running this document for #302's reopening: `11c` and `11d` shared one bind
directory, so `11d` inherited `11c`'s database — it started with quotes already present, skipped
cold-start seeding entirely, and its reseed's five landed on top of `11c`'s one. The symptom is a
sixth row, `quotinator-curated.json origin=User counts=7`, which no single run produces; the
container log confirms it by showing a reseed with no cold-start seeding before it. The product was
correct throughout. This was latent before the reopening for the same reason step 1 was weak — with
cold start silent there was simply less to leak.

### 8. Confirm they reach the startup modal after a restart

`/notifications` is only one of the two surfaces an active notification appears on. The other is the
startup popup on the home page, which is shown once per process run — so a reseed cannot populate it in
the run that performed the reseed, and this step needs a restart.

The modal is server-rendered, so its content is in the HTML of `/` and needs no browser automation.

```powershell
docker restart qt-notif-11 | Out-Null
foreach ($i in 1..30) {
  try { if ((Invoke-RestMethod "http://localhost:19511/api/v1/health").status -eq 'healthy') { break } }
  catch { Start-Sleep 2 }
}

$html = (Invoke-WebRequest "http://localhost:19511/" -UseBasicParsing).Content
"confirmation text in modal: $($html.Contains('reseeded with nothing left to review'))"
foreach ($f in 'quotinator-curated.json','vilaboim_movie-quotes.json',
               'NikhilNamal17_popular-movie-quotes.json','quotinator-series-universe.json') {
  "  $f -> $($html.Contains($f))"
}
```

**Expected:** `confirmation text in modal: True`, and every bundled file name present.

**Negative control — dismiss every confirmation, restart, and confirm the modal drops them:**

```powershell
@(Get-Confirmations) | ForEach-Object {
  Invoke-RestMethod -Method Post -Headers $headers `
    "http://localhost:19511/api/v1/notifications/$($_.id)/dismiss" | Out-Null
}
docker restart qt-notif-11 | Out-Null
foreach ($i in 1..30) {
  try { if ((Invoke-RestMethod "http://localhost:19511/api/v1/health").status -eq 'healthy') { break } }
  catch { Start-Sleep 2 }
}
$dismissed = (Invoke-WebRequest "http://localhost:19511/" -UseBasicParsing).Content
"modal shows dismissed confirmations: $($dismissed.Contains('reseeded with nothing left to review'))"
```

**Expected:** `False`. Without this half, the positive assertion above would pass just as happily
against a page that renders every notification ever written, or against a substring that happens to
appear somewhere else in the markup.

**The control was "a container that has never reseeded" until 2026-09-02, and #302's reopening
invalidated it** — a fresh install now confirms each file it seeds, so that container is no longer in
the state the control needed. Dismissal produces that state instead, and targets the named failure mode
more directly: an active-only query that is not actually filtering is exactly what shows a dismissed
row. Note the restart — the modal renders once per process run, so the dismissal must precede one.

**On failure:** check `/notifications` first. If the confirmations are there but not in the modal, the
fault is in the modal's own active-notification query, not in this issue's producer.

## Canary — run red against the build before #302

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Run against
`aed54b2d` (#302's last commit before its first `feat`) via `git worktree add` and
`docker build -t quotinator:canary302`, 2026-09-01:

| Step | Assertion | Pre-work result |
|---|---|---|
| 1 | `confirmations after first seed = 0` | **passes — and that is a weakness, see below** |
| 2 | `confirmations after reseed >= 1` | **fails** — `0`, no producer exists |

**Step 1 passes on a build with no feature at all, because it asserts an absence.** It cannot
distinguish "correctly suppressed on the first seed" from "this was never built", so on its own it is
not evidence of anything — the same trap `docs/testing-policy.md` records for negative/absence
assertions generally. Step 2 is what gives step 1 its meaning: only once confirmations are known to
appear on a reseed does their absence on a first seed say something. **Do not reorder or drop step 2**
believing step 1 covers the behaviour.

Container, image and worktree removed afterwards.

### Step 1's own canary, after the 2026-09-02 inversion

The weakness above was resolved by removing the behaviour, not by strengthening the assertion around
it. Step 1 now asserts a presence, so it has a red of its own — and unlike the original it needed no
worktree, because the gate was still in place on `HEAD` at the moment the inverted step was written:

| Step | Assertion | Result against the gated build |
|---|---|---|
| 1 | one confirmation per cleanly-applied file at cold start | **fails** — `quotes seeded = 799`, `confirmations after first seed = 0` |

That is the T1 finding reproduced exactly: the seeding ran, four files applied, and nothing was said.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-11
Remove-Item -Recurse -Force (Join-Path $env:TEMP "qt-notif-11-bind")
```
