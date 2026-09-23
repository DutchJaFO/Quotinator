# A notification renders its title and body as separate things, and keeps its line breaks

**Smoke:** no
**Environment:** Fresh
**Traces to:** #308

## Preconditions

**Beyond the profile.** Two containers of this test's own, each on a bind directory it creates and
removes:

- `qt-layout-13`, publishing `19313`, created with `--env Quotinator__AdminApiKey=t2-308`, whose
  imports folder holds the shared conflict fixture;
- `qt-layout-13d`, publishing `19314`, with `--read-only-data`, for the degraded case only.

#312 gave notifications a real `Title` and `Body`; until #308 both were rendered as one undifferentiated
cell, and Razor HTML-encodes the body so an embedded `\n` collapses to whitespace. This proves the title
is its own element, the body keeps its line breaks, and both hold on the `/notifications` page and in
the startup modal.

## Determinism

**Assert the computed style, never the class name.** A class can be present on an element the stylesheet
never reaches — that is exactly how #303's nav entry shipped with the class applied and no icon
rendered, caught only by a screenshot. `getComputedStyle(cell).whiteSpace` answers whether the rule
arrived; a line-box count answers whether it did anything.

**Every fixture this document reads is made here, not hoped for.**

- *A multi-line body* comes from a real producer: #81's what's-new body carries one line per highlight.
- *An untitled row*: no producer emits one any more — #319 backfilled titles onto the shipped
  announcement — but the column is nullable and a database written before #312 holds such rows, so
  step 1 clears one title directly.
- *An actionable row and a review to resolve* come from `scripts/testing/stage-import-conflict.csx`,
  whose file stages one pending quote change at cold start and raises the pending-review alert — see
  [`../import-and-staged-actions/20-pending-review-alert.md`](../import-and-staged-actions/20-pending-review-alert.md)
  for why the bundled sources cannot produce one. Until 2026-09-22 steps 6 and 7 assumed an alert and a
  recommendation nothing in this document created, and step 1 wrote through a `$bind` only step 9
  defined; the document could not be run as written.

**The page is read before the restart, the modal after it.** The modal renders once per process run —
#302's own T1 finding — so it needs a restart the page does not, and running the page's steps first
keeps both on one container.

**Close the browser tab before every stop**, and read the log before it — see the index's *Read the log
before the application stops*.

## Steps

### 1. Produce the notifications this document reads

```powershell
$bind = Join-Path $env:TEMP "qt-layout-13-bind"
# A folder left by an earlier run still holds its database, and the container would start against it.
if (Test-Path $bind) { Remove-Item -LiteralPath $bind -Recurse -Force }
dotnet script scripts/testing/stage-import-conflict.csx -- --imports (Join-Path $bind "imports")
dotnet script scripts/testing/test-env.csx -- create --name qt-layout-13 --port 19313 `
  --image quotinator:local --bind $bind --env Quotinator__AdminApiKey=t2-308
function Count-Thrown { @(docker logs qt-layout-13 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count }

$h = @{ "X-Api-Key" = "t2-308" }
Invoke-RestMethod -Method Post -Headers $h "http://localhost:19313/api/v1/admin/database/reseed" | Out-Null

$items = (Invoke-RestMethod "http://localhost:19313/api/v1/notifications?pageSize=0").items
"titled   = $(@($items | Where-Object { $_.title }).Count)"
"untitled = $(@($items | Where-Object { -not $_.title }).Count)"
"multi-line bodies = $(@($items | Where-Object { $_.body -match "`n" }).Count)"
"review alerts = $(@($items | Where-Object { $_.metadataKind -eq 'importReviewPending' }).Count)"

"before the stop: thrown=$(Count-Thrown)"
docker stop qt-layout-13 | Out-Null
dotnet script scripts/testing/execute-sql.csx -- --db (Join-Path $bind "quotinatordata.db") `
  --sql "UPDATE System_Notification SET Title = NULL WHERE MetadataKind = 'Announcement';"
docker start qt-layout-13 | Out-Null
dotnet script scripts/testing/http.csx -- --url "http://localhost:19313/api/v1/health" --wait-for 200 --status
$afterRestart = Count-Thrown
"untitled now = $(@((Invoke-RestMethod 'http://localhost:19313/api/v1/notifications?pageSize=0').items | Where-Object { -not $_.title }).Count)"
```

**Expected:** `titled` at least `1`, `untitled = 0`, `multi-line bodies = 1` (what's-new),
`review alerts = 1`, `thrown=0`, `OK — 1 row(s) affected.`, `200`, and `untitled now = 1`.

The reseed is synchronous — its response arrives when it is done — so nothing waits after it. An earlier
revision slept five seconds here for no stated reason.

**On failure:** `multi-line bodies = 0` leaves step 4 nothing to measure, and `review alerts = 0` leaves
steps 6 and 7 nothing to act on. Stop either way.

### 2. Confirm the title is its own element, not a prefix inside the body

Open `http://localhost:19313/notifications` and read the DOM:

```js
const cell = document.querySelector('tbody tr td .notification-body');
const row  = cell.closest('td');
({ titleElements: row.querySelectorAll('.notification-title').length,
   titleIsInsideBody: cell.textContent.includes(row.querySelector('.notification-title')?.textContent ?? ' ') })
```

**Expected:** `titleElements: 1` and `titleIsInsideBody: false`. A title concatenated into the body
string would satisfy a screenshot and fail here, which is the point.

### 3. Confirm an untitled row renders its body and no empty title element

```js
const rows = [...document.querySelectorAll('tbody tr')];
rows.map(r => ({ hasTitle: !!r.querySelector('.notification-title'),
                 bodyText: r.querySelector('.notification-body')?.textContent.trim().length ?? 0 }))
```

**Expected:** exactly one row with `hasTitle: false` — step 1's — and **every** row with `bodyText > 0`.
The second half is the positive control: without it, a cell rendering nothing at all would satisfy "no
title element" perfectly.

### 4. Confirm a two-line body renders as two lines

**Counting line boxes needs a `Range`, and the count alone proves nothing.** `getClientRects()` on the
cell returns one border-box rect however many lines it holds — measured, so do not assert on it. And a
raw line count conflates wrapping with explicit breaks. The assertion is the A/B — the same cell
measured under `pre-line` and under `normal`:

```js
function lineBoxes(el) { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; }
const cell = [...document.querySelectorAll('.notification-body')].find(c => c.textContent.includes('\n'));
const withPreLine = lineBoxes(cell);
cell.style.whiteSpace = 'normal';
const withNormal = lineBoxes(cell);
cell.style.whiteSpace = '';
({ whiteSpace: getComputedStyle(cell).whiteSpace,
   linesWithPreLine: withPreLine, linesWithNormal: withNormal,
   breaksHonoured: withPreLine > withNormal })
```

**Expected:** `whiteSpace: 'pre-line'` and `breaksHonoured: true`. Measured 2026-09-22: `4` lines under
`pre-line` against `2` under `normal`. The figures move with window width; the inequality does not.

**On failure:** `whiteSpace: 'normal'` means the stylesheet never reached the element — check that the
component's `.razor.css` is being emitted into the published static web assets, not just that the class
is in the markup.

### 5. Confirm payload detail opens as a dialog on the page, as a table, and fits

**Resize the viewport to `420` high before running this** — at a normal window height nothing
overflows and the fit assertions pass without testing anything.

**Check `viewportHeight` is non-zero before believing any fit result.** The pane can report a
zero-height viewport, and every element is then "off-screen": measured 2026-09-23 on step 8, a correct
popup read `withinViewport: false` and `footerVisible: false` with `window.innerHeight: 0`. Set an
explicit size and re-measure — see the index's *A count is evidence only if the instrument counts the
right thing*.

```js
const pre = { expandersOnPage: document.querySelectorAll('details.notification-detail').length,
   openButtonsOnPage: document.querySelectorAll('.notification-detail-open').length,
   listsAnywhere: document.querySelectorAll('.notification-detail ul, .modal-body ul').length };
let result = null;
for (const b of document.querySelectorAll('.notification-detail-open')) {
  b.click();
  await new Promise(r => setTimeout(r, 700));
  const t = document.querySelector('.modal-body .notification-detail-table');
  const rows = t ? [...t.querySelectorAll('tbody tr')] : [];
  if (rows.length >= 6) {
    const dlg = document.querySelector('.modal-dialog').getBoundingClientRect(), body = document.querySelector('.modal-body');
    result = { headers: [...t.querySelectorAll('th')].map(h => h.textContent.trim()), rows: rows.length,
      dialogHeight: Math.round(dlg.height), viewportHeight: window.innerHeight,
      withinViewport: dlg.top >= 0 && dlg.bottom <= window.innerHeight,
      bodyScrolls: body.scrollHeight > body.clientHeight,
      headerVisible: document.querySelector('.modal-header').getBoundingClientRect().top >= 0,
      footerVisible: document.querySelector('.modal-footer').getBoundingClientRect().bottom <= window.innerHeight,
      usesOldBespokeMarkup: !!document.querySelector('.notification-detail-dialog, .notification-detail-backdrop') };
    break;
  }
  document.querySelector('.modal-footer button')?.click();
  await new Promise(r => setTimeout(r, 400));
}
({ pre, result })
```

**Expected:** `expandersOnPage: 0`, `openButtonsOnPage` at least `1`, `listsAnywhere: 0`; a breakdown of
six or more rows found; headings `Entity / Incoming / Added / Updated / Skipped / Unchanged / Resolved /
Reported`; and `withinViewport`, `bodyScrolls`, `headerVisible`, `footerVisible` all `true` with
`usesOldBespokeMarkup: false`. Measured 2026-09-22 at `420`: `dialogHeight: 364`, seven rows. Take a
screenshot, then close the dialog and restore the viewport.

**The loop looks for a breakdown long enough to overflow.** The first detail button may belong to a file
touching two types, which fits in any viewport and tests nothing about the cap.

**Headings read eight columns, not three.** This step expected `Entity / Added / Updated` until the
import work added the incoming, unchanged, resolved-to-existing and already-reported counts; the table
renders every count the payload carries.

**Assert fit to the viewport, not a `95vh` figure.** `ModalDialog` sets `max-height: 95vh` inline, but
Bootstrap's `.modal-dialog-centered` also sets `min-height: calc(100% - 3.5rem)`, and a larger
`min-height` overrides `max-height` — so the dialog is `viewport − 56px` tall whenever it overflows, and
`95vh` never binds. Measured 2026-09-22: `1218` in a `1274` viewport against a `95vh` of `1210.3`; `364`
in `420`; `664` in `720`. The `95vh` form held below about `1120px` and failed above it. What the step
exists for — the header and footer reachable, the body scrolling — is what it now asserts.

**A table, not a list** (developer, 2026-09-02): every entry has the same shape, so columns line the
numbers up where a bulleted sentence per row does not. `listsAnywhere` guards the regression.

**`usesOldBespokeMarkup` is what stops the duplication coming back.** A modal built by copying the
markup instead of using `ModalDialog` would satisfy every other assertion here while being exactly the
defect this step exists for.

**On failure:** an empty `result` means no breakdown was long enough, or the payload is stored but not
rendered. A non-zero `expandersOnPage` means the surfaces are the wrong way round — the page opens a
dialog and the popup expands in place (developer, 2026-09-02).

### 6. Confirm the action buttons say what they do, and ask before acting

```js
[...document.querySelectorAll('tbody button')].map(b => b.textContent.trim()).filter(Boolean)
```

**Expected:** no button reads `Run`, and the review alert's reads `Decide`.

Then click **Decide**, read the row's buttons, and — before choosing — count what is still pending:

```js
[...document.querySelectorAll('tbody tr')].find(r => r.textContent.includes('needs review'))
  .querySelectorAll('button').forEach(b => console.log(b.textContent.trim()))
```

```powershell
"pending before choosing = $((Invoke-RestMethod 'http://localhost:19313/api/v1/import/actions?status=Pending&pageSize=1').totalCount)"
```

**Expected:** the row offers `Keep existing`, `Take incoming` and `Cancel`, and `pending before
choosing = 1` — the click asked, and ran nothing.

**Those labels are not new strings.** A reseed recommendation reads *Reseed the database* and a reset
recommendation *Reset the database* — `AdminEndpoints`' own `.WithSummary(...)` text — and a review alert
*Decide*, the review page's `ImportReviewDecideColumn` heading. If a label diverges from the operation's
existing name, the reader has to work out whether it is the same operation.

**Do not run this step without an actionable row.** On a container whose notifications have no action,
"no button reads `Run`" holds vacuously — which is how it once passed on the pre-work build.

### 7. Confirm a resolved notification says how it was resolved

Click **Take incoming**, then select **All** and read:

```js
[...document.querySelectorAll('tbody tr')]
  .filter(r => r.querySelector('.notification-resolution'))
  .map(r => r.querySelector('.notification-resolution').textContent.trim())
```

```powershell
"pending after = $((Invoke-RestMethod 'http://localhost:19313/api/v1/import/actions?status=Pending&pageSize=1').totalCount)"
```

**Expected:** `["Took the values from the file"]` and `pending after = 0`. Take a screenshot of the row:
it reads `Done` beside a body still asking for the decision, and the resolution line is what corrects it
— the body is frozen at write time.

### 8. Confirm the same holds in the startup modal, where detail expands in place

Close the browser tab, then:

```powershell
"before the restart: new=$((Count-Thrown) - $afterRestart)"
docker restart qt-layout-13 | Out-Null
dotnet script scripts/testing/http.csx -- --url "http://localhost:19313/api/v1/health" --wait-for 200 --status
$afterRestart = Count-Thrown
```

**Expected:** `new=0`, then `200`.

Open `http://localhost:19313/` and, against the modal:

```js
const modal = document.querySelector('.modal');
const cell = modal.querySelector('tbody tr td .notification-body'), td = cell.closest('td');
function lineBoxes(el) { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; }
const c = [...modal.querySelectorAll('.notification-body')].find(x => x.textContent.includes('\n'));
const a = lineBoxes(c); c.style.whiteSpace = 'normal'; const b = lineBoxes(c); c.style.whiteSpace = '';
const d = modal.querySelector('details.notification-detail');
const out = { titleElements: td.querySelectorAll('.notification-title').length,
  titleIsInsideBody: cell.textContent.includes(td.querySelector('.notification-title')?.textContent ?? ' '),
  whiteSpace: getComputedStyle(c).whiteSpace, breaksHonoured: a > b,
  expandersInModal: modal.querySelectorAll('details.notification-detail').length,
  openButtonsInModal: modal.querySelectorAll('.notification-detail-open').length, collapsedByDefault: !d.open };
d.open = true;
out.headers = [...d.querySelectorAll('th')].map(h => h.textContent.trim());
modal.querySelectorAll('details.notification-detail').forEach(x => x.open = true);
await new Promise(r => setTimeout(r, 500));
const dlg = modal.querySelector('.modal-dialog').getBoundingClientRect(), body = modal.querySelector('.modal-body');
Object.assign(out, { withinViewport: dlg.top >= 0 && dlg.bottom <= window.innerHeight,
  bodyScrolls: body.scrollHeight > body.clientHeight,
  footerVisible: modal.querySelector('.modal-footer').getBoundingClientRect().bottom <= window.innerHeight });
out
```

**Expected:** `titleElements: 1`, `titleIsInsideBody: false`, `whiteSpace: 'pre-line'`,
`breaksHonoured: true`; `expandersInModal` at least `1`, `openButtonsInModal: 0`,
`collapsedByDefault: true`, the same eight headings as step 5; and, with every row expanded,
`withinViewport`, `bodyScrolls` and `footerVisible` all `true`. Take a screenshot. Measured 2026-09-22:
`11` expanders, `0` open buttons.

**The two surfaces must genuinely differ.** If both report the same control, the `DetailAsDialog`
parameter is not reaching one of them. The modal is size-constrained in a way the page is not, so a
layout that reads correctly on one can wrap or clip on the other — which is why steps 2 and 4 are
repeated here rather than assumed.

### 9. Confirm rendering survives a degraded startup

Close the browser tab first.

```powershell
$dbind = Join-Path $env:TEMP "qt-layout-13d-bind"
if (Test-Path $dbind) { Remove-Item -LiteralPath $dbind -Recurse -Force }
New-Item -ItemType Directory -Force $dbind | Out-Null
dotnet script scripts/testing/test-env.csx -- create --name qt-layout-13d --port 19314 `
  --image quotinator:local --bind $dbind --read-only-data --wait-listening

function Code($u) { try { (Invoke-WebRequest $u -UseBasicParsing -ErrorAction Stop).StatusCode }
                    catch { $_.Exception.Response.StatusCode.value__ } }
"health         -> $(Code 'http://localhost:19314/api/v1/health')"
"/notifications -> $(Code 'http://localhost:19314/notifications')"
"/about         -> $(Code 'http://localhost:19314/about')"
docker logs qt-layout-13d 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
```

**First expected:** `health -> 503`. If it is `200`, the container is not degraded and the rest of this
step is meaningless — `--readonly`, an earlier spelling, is silently ignored by `test-env.csx`.

**Expected:** parity with `/notifications`' current behaviour, not a bare `200` — measured 2026-09-01
and again 2026-09-22: `health 503`, `/notifications 500`, `/about 200`. The `500` is a pre-existing
DataProtection defect on a read-only data directory (#332, #336), not this issue's; asserting parity
passes now and keeps passing when that is fixed.

The log read before the stop shows what the read-only directory provokes: `IOException` and
`SqliteException` from startup, and the `CryptographicException` behind the `500`. That is this
environment's expected output, not shutdown noise and not a new finding.

## Canary — run red before any implementation existed

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Unlike
#302's, #303's and #367's canaries — each of which needed a `git worktree` and a second image build
after the fact — this one was **free**: the document was written at step 1 of the issue, when `HEAD`
was still the pre-work build. Run 2026-09-01 against `quotinator:local` at commit `52071f24`:

| Step | Assertion | Pre-work result |
|---|---|---|
| 2 | a `.notification-body` cell exists | **fails** — `0` |
| 2 | a `.notification-title` element exists | **fails** — `0` |
| 4 | computed `white-space` is `pre-line` | **fails** — `normal` |
| 4 | a multi-line body occupies ≥ 2 line boxes | **fails** — `1` |

**Step 1 also failed, and that was the document's fault rather than the build's** — it expected
untitled rows that no producer emits any more. Finding that before implementing is the whole argument
for writing the document first.

### The reopening's findings, against `7bc5bacc`

Built under `quotinator:canary308b`, 2026-09-02. Step numbers are the current ones; the steps were
reordered on 2026-09-22 so the page is read before the modal's restart.

| Step | Assertion | Pre-work result |
|---|---|---|
| 5 | a detail-open button exists on the page | **fails** — `0` |
| 8 | the modal offers a collapsible detail element | **fails** — `0` |
| 6 | no action button reads `Run` | **fails** — reads exactly `Run` |
| 7 | a resolved row shows how it resolved | **fails** — `0` resolution lines |

**Step 5's fit assertions have a different provenance.** They were added *because* the developer's own
T1 pass found the dialog overflowing, on the startup popup and again on the detail popup — a red
observation on a real build rather than a constructed one, not counted in the table above.
`usesOldBespokeMarkup` has no red run at all: it is a regression guard, and is recorded here as one.

**Step 6 needed a second attempt to be meaningful.** Run straight after a reseed it *passed* on the
pre-work build, because no notification offered an action at all. See step 6's own note.

## Cleanup

Close the browser tab first.

```powershell
"before the stop: new=$((Count-Thrown) - $afterRestart)"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-layout-13 --bind $bind
dotnet script scripts/testing/test-env.csx -- destroy --name qt-layout-13d --bind $dbind
Remove-Item -LiteralPath $bind -Recurse -Force
Remove-Item -LiteralPath $dbind -Recurse -Force
```

**Expected:** `new=0`. The bind directories are this test's own; `destroy` leaves them in place.
