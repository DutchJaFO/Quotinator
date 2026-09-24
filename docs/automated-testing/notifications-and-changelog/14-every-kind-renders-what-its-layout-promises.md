# Every notification kind is produced by its own trigger and renders what its layout promises

**Smoke:** no
**Environment:** Fresh
**Traces to:** #308

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-notif-14`, publishing `19514`, created
with `--env Quotinator__AdminApiKey=t2-308` and a bind directory holding the shared conflict fixture in
its `imports/` folder. It is restarted three times and reset once — each of those is a trigger, not
scaffolding.

#308 defines a per-type layout across both surfaces. Two kinds — `ReseedRecommended` and
`SchemaVersionOvershoot` — had never been rendered under assertion at all, and the per-type decision
itself was only ever read off a lookup table no renderer consults. This asserts both halves for every
kind: that the kind's own trigger produces it, and that what a reader then sees matches the decision.

## Determinism

**Each kind arrives through its real trigger, not a constructed row.** A constructed row proves
rendering and nothing about the producer, and a producer that stopped writing would leave every
rendering assertion passing over a page nobody could reach in practice:

| Kind | Its trigger here |
|---|---|
| `Announcement` | a cold start on a fresh database |
| `WhatsNew` | the same cold start, from the bundled changelog |
| `ReseedFileApplied` | the same cold start, one per bundled file that applied cleanly |
| `ImportReviewPending` | the conflict fixture in `imports/`, staged by that seed |
| `SchemaVersionOvershoot` | a consumer schema version rolled one past this build, then a restart |
| `ReseedRecommended` | a database reset, which leaves the database empty |

**The reset comes last, and in its own step.** Reset rebuilds every table (#156), so it takes the other
five kinds with it — which is why it cannot share a page with them and why nothing after it may assume
they are still there.

**The overshoot's trigger is a version row, not a notification row.** Inserting `MAX(Version) + 1` into
`System_ConsumerSchemaVersion` is what
[`../startup-and-degradation/06-schema-version-ahead-of-the-application.md`](../startup-and-degradation/06-schema-version-ahead-of-the-application.md)
uses, and the producer is what writes the notification — so this still exercises the producer rather
than faking its output.

**The detail expectation is derived, not listed.** Which kinds carry structured detail is read from each
row's own payload — a kind has detail when its payload carries a non-empty `counts` array — so no list
here can go stale when a kind is added.

**`NotificationMetadataKind` is the source of the case list.** A kind added later must fail until it is
covered here; `NotificationTableTests.EveryLiveNotificationKind_IsNamedInTheVariantDocument` asserts
this document names every member.

**Close the browser tab before every stop**, and read the log before it, per the index's *Read the log
before the application stops*.

**A rendering claim is only evidence if someone can see it.** Every surface this document opens is
captured as well as read: one image of the whole surface, one per kind on it, and one more for each
detail it opens — `<kind>-<surface>.png` and `<kind>-<surface>-detail.png`, into
`.claude/temp/qt-notif-14/`, which is gitignored. Fifteen images, and none of them a tracked file: they
are this run's evidence, produced again by the next run.

The DOM snippets stay the machine-checkable half, per the index's *Every test must be able to run
unattended*: nothing passes or fails on an image. The image records what an assertion cannot describe —
spacing, wrapping, whether a body that reads correctly also looks like one — and it is the only way the
per-type decision reaches a reader who cannot produce these kinds themselves. Four of the six need a
rolled-forward schema version, a reset, or a staged conflict to exist at all, so this run is the only
place all six are ever together.

**The capture is a command, not an act of driving a browser.** `scripts/testing/capture-page.csx` runs
headless Edge over the DevTools Protocol: it loads a URL, evaluates the step's own JavaScript, prints
what that returns, and writes the PNG. So every image in this document comes from a line anyone can run
again, and the assertion and the picture describe the same moment because one call produced both.

**A kind is cropped to its own row by returning that row's rectangle.** The script uses an
`{x, y, width, height}` return value as the capture region, so `getBoundingClientRect()` on the row is
what bounds the image — no pixel bounds chosen by eye, and no hiding of neighbouring rows to fake an
isolated shot. A row whose rectangle has zero height is not one that rendered small but one the viewport
never laid out, and the index's *A count is evidence only if the instrument counts the right thing*
applies to a capture exactly as it does to a count.

**Match a title without an apostrophe in it.** The match text is interpolated into a single-quoted
JavaScript string, and `What's new` closes it — `SyntaxError: missing ) after argument list`, measured
2026-09-24. Match `new (unreleased)` instead.

## Steps

### 1. Cold start with a conflict waiting, and confirm four kinds were produced

```powershell
$bind = Join-Path $env:TEMP "qt-notif-14-bind"
# A folder left by an earlier run still holds its database, and the container would start against it.
if (Test-Path $bind) { Remove-Item -LiteralPath $bind -Recurse -Force }
dotnet script scripts/testing/stage-import-conflict.csx -- --imports (Join-Path $bind "imports")

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-14 --port 19514 `
  --image quotinator:local --bind $bind --env Quotinator__AdminApiKey=t2-308
function Read-Thrown {
  $lines = @(docker logs qt-notif-14 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]')
  $new = @($lines | Select-Object -Skip $script:thrownSeen)
  $script:thrownSeen = $lines.Count
  "thrown=$($new.Count)"
  $new | ForEach-Object { '  ' + ($_.Line -split 'thrown: ')[1] }
}
$thrownSeen = 0

function Kinds {
  $items = (Invoke-RestMethod "http://localhost:19514/api/v1/notifications?pageSize=0").items
  @($items | Group-Object metadataKind | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' '
}

# Images from an earlier run would otherwise be read as this one's evidence.
$shots = Join-Path (Get-Location) ".claude\temp\qt-notif-14"
if (Test-Path $shots) { Remove-Item -LiteralPath $shots -Recurse -Force }
New-Item -ItemType Directory -Path $shots | Out-Null

function Capture-Row {
  param([string]$Surface, [string]$Match, [string]$Name, [switch]$Expand, [string]$Port = '19514')
  $url    = if ($Surface -eq 'popup') { "http://localhost:$Port/" } else { "http://localhost:$Port/notifications" }
  $scope  = if ($Surface -eq 'popup') { "document.querySelector('.modal')" } else { "document" }
  $width  = if ($Surface -eq 'popup') { 1000 } else { 1280 }
  $expand = if ($Expand) { "const d = row.querySelector('details.notification-detail'); if (d) d.open = true; await new Promise(r => setTimeout(r, 300));" } else { "" }
  @"
await new Promise(r => setTimeout(r, 800));
const rows = [...$scope.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const row  = rows.find(r => r.querySelector('.notification-title').textContent.includes('$Match'));
$expand
const b = row.getBoundingClientRect();
return { x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
         title: row.querySelector('.notification-title').textContent.trim(),
         opensDetail: !!row.querySelector('.notification-detail-open'),
         expandsInPlace: !!row.querySelector('details.notification-detail'),
         buttons: [...row.querySelectorAll('button')].map(x => x.textContent.trim()) };
"@ | dotnet script scripts/testing/capture-page.csx -- --url $url --out "$shots\$Name.png" --width $width --height 1400 --script-stdin
}

Kinds
Read-Thrown
```

**Expected:** `announcement=1 importreviewpending=1 reseedfileapplied=5 whatsnew=1`, and `thrown=0`.
Four kinds, each written by the producer its trigger runs.

**On failure:** a missing kind is a producer defect, not a rendering one — the rest of this document
would then asserting rendering for a row nobody can obtain. Stop and report which trigger produced
nothing.

### 2. Read the page: what each kind shows

Open `http://localhost:19514/notifications` and:

```js
const rows = [...document.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
rows.map(r => ({
  title: r.querySelector('.notification-title')?.textContent.trim() ?? '(none)',
  bodyLength: r.querySelector('.notification-body')?.textContent.trim().length ?? 0,
  titleInsideBody: (r.querySelector('.notification-body')?.textContent ?? '')
      .includes((r.querySelector('.notification-title')?.textContent ?? 'no-title').trim()),
  opensDetail: !!r.querySelector('.notification-detail-open'),
  expandsInPlace: !!r.querySelector('details.notification-detail'),
  buttons: [...r.querySelectorAll('button')].map(b => b.textContent.trim()).filter(t => t && t !== 'Details' && t !== 'Dismiss')
}))
```

Pair each row with its kind through the API, so the assertion is per kind rather than per position:

```powershell
(Invoke-RestMethod "http://localhost:19514/api/v1/notifications?pageSize=0").items | ForEach-Object {
  $p   = if ($_.metadata) { $_.metadata | ConvertFrom-Json } else { $null }
  $has = $p -and ($p.PSObject.Properties.Name -contains 'counts')
  "$($_.metadataKind) | title=$($_.title) | countEntries=$(if ($has) { @($p.counts).Count } else { 0 })"
}
```

**Ask whether the key exists before counting it.** `@($p.counts).Count` reads `1` for a payload with no
`counts` at all, because `@($null)` is a one-element array — so every kind would look as though it
carried detail. Measured here 2026-09-23: the announcement and what's-new rows reported `counts=1`
under that form and `0` under this one. See the index's *A count is evidence only if the instrument
counts the right thing*.

**Expected:** every row renders a non-empty body with its title as its own element and never inside it
(`titleInsideBody: false`); `expandsInPlace: false` throughout, because the page opens a dialog;
`opensDetail: true` for exactly the rows whose payload carries a non-empty `counts` array — the five
`reseedfileapplied` rows and the one `importreviewpending` row — and `false` for `announcement` and
`whatsnew`, whose payloads carry no counts; and the review row is the only one with an extra button,
reading `Decide`.

**That is the per-type layout decision, stated as what a reader sees.** Derive it from the payload when
a kind is added: a kind whose payload says nothing its body does not opens no detail.

**Capture the page, then each of the four kinds on it.** One of the five `reseedfileapplied` rows is
enough, since the five are one kind:

```powershell
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications" `
  --out "$shots\page.png" --width 1280 --height 1400 --full-page
Capture-Row -Surface page -Match "operation IDs"    -Name announcement-page
Capture-Row -Surface page -Match "new (unreleased)" -Name whatsnew-page
Capture-Row -Surface page -Match "reseeded cleanly" -Name reseedfileapplied-page
Capture-Row -Surface page -Match "needs review"     -Name importreviewpending-page
```

**Expected:** each call prints the row it found and its own rectangle, and the four match this step's
own reading — `buttons` of `Dismiss` alone for the announcement and what's-new rows, `Details,
Dismiss` for a reseeded file, `Details, Decide, Dismiss` for the review row.

### 3. Open a detail dialog and confirm it describes itself

```js
document.querySelector('.notification-detail-open').click();
await new Promise(r => setTimeout(r, 700));
const t = document.querySelector('.modal-body .notification-detail-table');
({ headers: [...t.querySelectorAll('th')].map(h => h.textContent.trim()),
   rows: [...t.querySelectorAll('tbody tr')].map(r => [...r.querySelectorAll('td')].map(c => c.textContent.trim())) })
```

**Expected:** a heading per column and one row per payload entry, each row's cell count matching the
headings. Close the dialog afterwards.

Capture it from a run of its own, which opens the dialog and shoots it in one call:

```powershell
@'
document.querySelector('.notification-detail-open').click();
await new Promise(r => setTimeout(r, 1200));
const dialog = document.querySelector('.modal-dialog');
const t = document.querySelector('.modal-body .notification-detail-table');
const b = dialog.getBoundingClientRect();
return { x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
         headers:  [...t.querySelectorAll('th')].map(h => h.textContent.trim()),
         bodyRows: [...t.querySelectorAll('tbody tr')].map(r => [...r.querySelectorAll('td')].map(c => c.textContent.trim())),
         footRows: [...t.querySelectorAll('tfoot tr')].map(r => [...r.querySelectorAll('td,th')].map(c => c.textContent.trim())) };
'@ | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications" `
  --out "$shots\reseedfileapplied-page-detail.png" --width 1280 --height 1000 --script-stdin
```

**Read `tfoot` separately, or the totals go unasserted.** The dialog renders a bold **Total** line that
is not a `tbody` row — measured 2026-09-24: `bodyRows` `2`, `footRows` `1`. A step counting only
`tbody tr` reads `2` where three lines render, and would keep passing if the totals stopped adding up.

**Assert the shape, not the column list** — the counts a breakdown carries have changed twice since
#308 (#374, #377), and naming them here would fail on the next addition while the behaviour is correct.

### 4. Trigger the overshoot, and confirm the fifth kind is produced and rendered

Close the browser tab, then:

```powershell
Read-Thrown
docker stop -t 15 qt-notif-14
$thrownSeen = @(docker logs qt-notif-14 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count
dotnet script scripts/testing/execute-sql.csx -- --db "$bind\quotinatordata.db" `
  --sql "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) SELECT MAX(Version) + 1, '2026-01-01 00:00:00' FROM System_ConsumerSchemaVersion;"
docker start qt-notif-14
dotnet script scripts/testing/http.csx -- --url "http://localhost:19514/api/v1/health" --wait-for 200 --status
Kinds
```

**Expected:** `thrown=0` before the stop, `OK — 1 row(s) affected.`, then the same four kinds plus
`schemaversionovershoot=1`.

Open `http://localhost:19514/notifications` and read the overshoot row with the snippet from step 2.

**Expected:** a non-empty body, its title its own element, `opensDetail: false` — its payload carries
version numbers, which its body already states — and one extra button reading `Reset the database`.

```powershell
Capture-Row -Surface page -Match "schema version" -Name schemaversionovershoot-page
```

**On failure:** no overshoot row means the producer did not run; the version bump is its only trigger,
so check the insert reported a row before reading anything into the rendering.

### 5. Read the startup popup, which renders once per process run

Close the browser tab, then:

```powershell
Read-Thrown
docker restart qt-notif-14
dotnet script scripts/testing/http.csx -- --url "http://localhost:19514/api/v1/health" --wait-for 200 --status
$thrownSeen = @(docker logs qt-notif-14 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count
```

Open `http://localhost:19514/` and, against the popup:

```js
const modal = document.querySelector('.modal');
[...modal.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body')).map(r => ({
  title: r.querySelector('.notification-title')?.textContent.trim() ?? '(none)',
  bodyLength: r.querySelector('.notification-body')?.textContent.trim().length ?? 0,
  expandsInPlace: !!r.querySelector('details.notification-detail'),
  opensDetail: !!r.querySelector('.notification-detail-open')
}))
```

**Expected:** the same five kinds, the same detail decision — `expandsInPlace: true` for exactly the
rows whose payload carries counts — and `opensDetail: false` throughout. That surface difference is
what #308 settled: a dialog on the page, an expander in the popup.

**Capture the popup, then each kind in it**, the same way as step 2 but against the modal's own rows.
The two detail-carrying kinds are captured expanded — that image is what shows the surface difference
as a difference, rather than as two booleans that happen to disagree:

```powershell
@'
await new Promise(r => setTimeout(r, 800));
const d = document.querySelector('.modal-dialog');
const rows = [...d.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const b = d.getBoundingClientRect();
return { x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
         rows: rows.length,
         anyButton: rows.some(r => r.querySelectorAll('button').length > 0),
         expanders: rows.filter(r => r.querySelector('details.notification-detail')).length,
         dialogs:   rows.filter(r => r.querySelector('.notification-detail-open')).length };
'@ | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/" `
  --out "$shots\popup.png" --width 1000 --height 1200 --script-stdin

Capture-Row -Surface popup -Match "operation IDs"    -Name announcement-popup
Capture-Row -Surface popup -Match "new (unreleased)" -Name whatsnew-popup
Capture-Row -Surface popup -Match "schema version"   -Name schemaversionovershoot-popup
Capture-Row -Surface popup -Match "reseeded cleanly" -Name reseedfileapplied-popup-detail  -Expand
Capture-Row -Surface popup -Match "needs review"     -Name importreviewpending-popup-detail -Expand
```

**Expected:** the whole-surface call reports `rows: 8`, `anyButton: false`, `expanders: 6`,
`dialogs: 0`, and every per-kind call reports `buttons` empty.

**The popup is per browser session, not per application run.** Measured 2026-09-24: two fresh sessions
against the same running container both rendered it, so these five captures need no restart between
them — each `capture-page.csx` call is its own session. The restart above is still what *produces* the
overshoot row; it is not what makes the popup appear.

**Count notification rows, not table rows.** An expander holds a detail table of its own, so an
unfiltered `tbody tr` returns those too — measured 2026-09-23: `49` rows where there are `9`, the extra
forty reporting an empty title and a zero-length body. Filtering on the presence of a
`.notification-body` cell keeps the selection to notification rows on either surface.

**Read `window.innerHeight` alongside any fit assertion.** The pane can report a zero-height viewport,
which makes every element read as off-screen — see the index's *A count is evidence only if the
instrument counts the right thing*.

**The popup offers no controls at all — no action button and no Dismiss.** `NotificationSummary` passes
neither `ShowActionColumn` nor `ShowDismissAction`, so every row there is read-only; the page passes
both. Assert the absence here: a row that grew a button in the popup would be a surface difference
nobody decided.

### 6. Confirm a body's line breaks survive on this surface

The what's-new body carries one line per changelog highlight. In the popup:

```js
const cell = [...document.querySelectorAll('.notification-body')].find(c => c.textContent.includes('\n'));
function lineBoxes(el) { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; }
const withPreLine = lineBoxes(cell);
cell.style.whiteSpace = 'normal';
const withNormal = lineBoxes(cell);
cell.style.whiteSpace = '';
({ whiteSpace: getComputedStyle(cell).whiteSpace, breaksHonoured: withPreLine > withNormal })
```

**Expected:** `whiteSpace: 'pre-line'` and `breaksHonoured: true`.

**This is why no per-kind line-break flag is needed:** the rule applies to every body, so a kind's
layout has nothing to declare about it.

### 7. Reset, and confirm the sixth kind is produced and rendered

The reset rebuilds every table, so the five kinds above are gone from this point on.

```powershell
Read-Thrown
Invoke-RestMethod -Method Post -Headers @{ 'X-Api-Key' = 't2-308' } `
  "http://localhost:19514/api/v1/admin/database/reset?allowNoBackup=true" | Out-Null
Kinds
```

**Expected:** `reseedrecommended=1` and nothing else.

Open `http://localhost:19514/notifications` and read that row with the snippet from step 2.

**Expected:** a non-empty body, its title its own element, `opensDetail: false`, and one extra button
reading `Reseed the database` — the operation's own name, never `Run`.

```powershell
Capture-Row -Surface page -Match "no quotes" -Name reseedrecommended-page
```

**On failure:** no recommendation means the reset's producer did not run, which is #304's behaviour
rather than this issue's rendering.

### 8. Render that last kind in the popup too, where nothing can reseed it away

The popup renders once per process run, so this kind needs a restart to reach it — and on the container
above that restart re-seeds the database, which **resolves the recommendation before it can be read**:
measured 2026-09-23, the row came back `isDismissed=True`, `dismissReason=resolved`,
`resolution=reseeded`. That re-seed is
[#423](https://github.com/DutchJaFO/Quotinator/issues/423) — a reset is undone by the next restart,
because startup loads the configured files whenever the quote table is empty rather than only on a
fresh install. #304's producer is behaving correctly on top of it: the load really did happen, so it
records the action as carried out.

A container with nothing to seed is the only place this kind survives a restart today. **When #423 is
fixed this step can drop its second container** and read the recommendation on the first one's popup,
since the restart will then leave the database as the reset left it.

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-14b --port 19515 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-308 --env Quotinator__IncludeDefaultSources=false
Invoke-RestMethod -Method Post -Headers @{ 'X-Api-Key' = 't2-308' } `
  "http://localhost:19515/api/v1/admin/database/reset?allowNoBackup=true" | Out-Null
docker restart qt-notif-14b
dotnet script scripts/testing/http.csx -- --url "http://localhost:19515/api/v1/health" --wait-for 200 --status
(Invoke-RestMethod "http://localhost:19515/api/v1/notifications?pageSize=0").items |
  ForEach-Object { "$($_.metadataKind) dismissed=$($_.isDismissed)" }
```

**Expected:** `reseedrecommended dismissed=False`, alongside the announcement and what's-new rows this
boot wrote. A reset on an empty container is the only trigger for this kind — measured: a cold start
with no sources writes none.

Open `http://localhost:19515/` and read the popup with step 5's snippet.

**Expected:** the recommendation renders its title and body, `expandsInPlace: false` — its payload
carries no counts — and **no buttons at all**, per the read-only popup above.

```powershell
Capture-Row -Surface popup -Match "no quotes" -Name reseedrecommended-popup -Port 19515
```

That completes the set: six kinds, both surfaces, fifteen images.

```powershell
docker logs qt-notif-14b 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-14b
```

**Expected:** only the restart's own two shutdown lines.

## Canary — run red against the build before #308

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Built from
`69c01776` — the commit before #308's first feature commit — as `quotinator:canary308c` via
`git worktree add` and `docker build`, 2026-09-23:

| Step | Assertion | Pre-work result |
|---|---|---|
| 1 | four kinds from the cold start | **fails** — three: `announcement`, `importreviewpending`, `whatsnew` |
| 2 | each row has a `.notification-body` cell | **fails** — `0` of `3` rows; the class does not exist |
| 2 | a title element distinct from the body | **fails** — `0` title elements |
| 2 | a detail control for the kinds carrying counts | **fails** — `0` dialogs and `0` expanders, for any kind |
| 2 | the review row's button names its action | **fails** — the only labels are `Run` and `Dismiss` |

Every per-kind assertion depends on markup that build does not emit, so the document cannot pass there
by accident. Container, image, bind directory and worktree removed afterwards.

## Observed effect

**Measured 2026-09-24**, against an image built from this branch, and captured this time as well as
read. Every kind reached the page through its own trigger, and every one rendered as its payload
implies:

| Kind | Trigger that produced it | Page | Popup |
|---|---|---|---|
| `Announcement` | cold start | body `261`, no detail, `Warning` badge, no button | same, read-only |
| `WhatsNew` | cold start | body `264`, no detail, `Information` badge | same; its body is the one carrying line breaks |
| `ReseedFileApplied` | cold start, five bundled files | detail dialog, 8 headings over 2 rows plus a `Total` row | expander in place, same table |
| `ImportReviewPending` | the conflict fixture | detail dialog, button `Decide` and a `Review each change` link | expander (`Pending 1`), no button |
| `SchemaVersionOvershoot` | a consumer version one past the build | body `201`, no detail, button `Reset the database` | body `201`, no detail, no button |
| `ReseedRecommended` | a database reset | body `160`, no detail, button `Reseed the database` | step 8, no detail, no button |

Detail opened for exactly the two kinds whose payload carries counts, on both surfaces, and for no
others. The popup reported `anyButton: false` across all 8 rows, against `Details, Decide, Dismiss` on
the page's review row. Line breaks: `pre-line`, `5` line boxes against `4` under `normal`. No container
logged an exception of its own; every line read was a stop's or restart's own `SocketException`.

Fifteen images: each surface whole, then each of the six kinds on each of the two surfaces, plus the
detail the dialog opens and the two the popup expands in place.

**One defect in the instrument, found and fixed rather than tolerated.** A capture that killed its
browser left every Blazor circuit half-open: thirteen captures against one container produced **72
`WebSocketException`s**, where the same container logged `0` before any capture ran — noise this suite
would have been generating for itself, in the middle of the log the document reads for real exceptions.
`capture-page.csx` now closes the browser over the protocol instead. Re-measured on a container of its
own: `0` before three captures and `0` after.

## Cleanup

Close the browser tab first.

```powershell
Read-Thrown
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-14 --bind $bind
Remove-Item -LiteralPath $bind -Recurse -Force
```

**Expected:** `thrown=0`.
