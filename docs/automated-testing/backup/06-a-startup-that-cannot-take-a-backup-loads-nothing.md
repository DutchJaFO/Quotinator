# A startup that cannot take a backup loads nothing, and offers only the options that can run

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-06`, publishing `18386`, on a bind
directory so the test can place a file in `backups/` from the host. The database has its genre rows
removed and the backup quota filled, so the next start has content to load and no room for the backup
that must come first.

A startup loads content into a database that is missing some, and a backup is taken first because that
load writes what nothing else could restore. Until #348 a startup with no room for that backup loaded
nothing and said nothing: the application reported healthy, incomplete, with no explanation and no way
forward. This proves the refusal is reported as a notification, that it offers exactly the options that
can run, that each option works through the page, and that resolving the obstacle by hand brings back
the one option that keeps a restore point.

## Determinism

- **The obstacle is a full budget, made by one sparse file the size of the whole 1 GB default.** The
  backup a startup takes before loading content is measured against the absolute ceiling, not the 90%
  operating quota: filling to 95% leaves it room in the reserve, and the content loads (measured). At the
  ceiling it refuses with `BudgetExceeded`, and the notification's own check, which does use the
  operating quota, agrees. `SetLength` allocates without writing a gigabyte.
- **The filler is made the oldest backup.** Its write time is set to 2020, before any backup a cycle
  leaves behind. That makes removing the oldest backup enough to clear the quota, so *Remove the oldest
  backup, then back up and reseed* is offered; left newest, removing the oldest would free only a few
  megabytes and the option would rightly be withheld.
- **The pending load is the genre re-seed, and it is produced by removing the genre rows.** Since
  [#423](https://github.com/DutchJaFO/Quotinator/issues/423) a start only loads the configured files into
  a database it created itself, so an emptied quote table no longer gives a restart anything to load and
  a Reset no longer reaches this refusal at all. Quotes present with genres absent is the one state in
  which a start that did not create the database still loads content, which is `ReSeedGenresIfEmptyAsync`.
  It is reached by editing the database, which this document could previously avoid: the application has
  no operation that produces it, and that is the point of #423 rather than a gap in it.
- **The edit is made while the container is stopped.** `test-env.csx reenter` stops cleanly (`docker stop
  -t 15`) precisely so SQLite checkpoints and leaves no `-wal`/`-shm` sidecar behind; writing to the file
  underneath a running container would be writing to a database mid-checkpoint. So each cycle stops,
  edits, then re-enters.
- **Each option is proven in its own cycle.** An option that succeeds loads content and resolves the
  notification, so each of the three is reached by repeating the wipe, fill and restart.
  `Refuse-ContentLoad` below is that cycle, so every option starts from the same state.
- **Count this notification by its kind, never the total.** How many notifications exist depends on
  producers this test is not about.
- **The page is driven through the DOM, and a click is retried until Confirm appears.** Controls need
  the Blazor circuit, and a click issued before it connects is swallowed, as the running-action
  document records.
- **An option is waited on until its action has returned, as the page itself shows it.** While the
  executor call is in flight the page marks it running with a spinner, and removes that when the call
  returns (#367), so each script waits, at most 120 seconds, for the spinner to appear and then go.
  Nothing else is read for this: not the notification, the audit trail or the log. The results asserted
  afterwards (content present, notification resolved, backups and audit entries) are read once the
  action has finished.
- **Every list read here is wrapped in `@(...)`**, per the index's *A count is evidence only if the
  instrument counts the right thing*: a function returning one notification hands back the object, not a
  one-element array, and its `.Count` is empty in Windows PowerShell 5.1 (measured on the first run).
- **The startup popup renders once per process run**, so it is read before the Notifications page in
  each cycle that reads it.
- **The log is read before each stop.**
## Steps

### 1. Seed a healthy database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-06"
$out     = "C:\repos\Quotinator\.claude\temp\qt-backup-06-captures"
Remove-Item -Recurse -Force $dataDir, $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups", $out | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-06 --port 18386 `
  --image quotinator:local --bind $dataDir

$key  = @{ "X-Api-Key" = "smoketest" }
$base = "http://localhost:18386/api/v1"
function Quote-Count { (Invoke-RestMethod "$base/quotes?pageSize=1").totalCount }
function Refused-Notifications {
  @((Invoke-RestMethod "$base/notifications?pageSize=0").items | Where-Object { $_.metadataKind -eq 'backuprefused' -and -not $_.isDismissed })
}
# The pending load is the genre re-seed, so the genre table is this document's evidence of a load
# having run or having been refused. No endpoint reports it, so it is read from the file with
# DbInspector, read-only: execute-sql.csx runs ExecuteNonQuery and cannot return a count.
function Genre-Count {
  @(dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
      --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre") -join "`n"
}
"quotes=$(Quote-Count) genres=$(Genre-Count)"
```

**Expected:** the environment reports healthy, `quotes` above `0`, and `Genre-Count` printing DbInspector's
own `Genres` header above a count above `0`.

**This is the positive control for the whole document.** With room for a backup, a startup loads content;
everything below asserts what happens without that room.

### 2. Define the refusing cycle, and run it

```powershell
function Refuse-ContentLoad {
  $filler = Join-Path $dataDir "backups\filler.db"
  Remove-Item $filler -ErrorAction SilentlyContinue

  docker logs qt-backup-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'

  # Stopped first, and cleanly, so SQLite checkpoints and the edit is not made underneath a running
  # container. reenter would stop it too, but only after this edit had already been written.
  docker stop -t 15 qt-backup-06
  dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" `
    --sql "DELETE FROM Quotinator_QuoteGenre;"

  $stream = [System.IO.File]::Create($filler)
  $stream.SetLength([int64]1073741824)
  $stream.Close()
  (Get-Item $filler).LastWriteTimeUtc = [datetime]'2020-01-01'

  dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-06 --port 18386 `
    --image quotinator:local --bind $dataDir
}

Refuse-ContentLoad
"health=$(dotnet script scripts/testing/http.csx -- --url "$base/health" --status)"
"quotes=$(Quote-Count)"
Genre-Count
"refusalLogged=$([bool](docker logs qt-backup-06 2>&1 | Select-String -SimpleMatch 'seeding refused: no backup could be taken (BudgetExceeded)'))"
```

**Expected:** the log read before the stop finds nothing, the restart reports healthy, then `health=200`,
`quotes` still above `0`, `Genres` `0` and `refusalLogged=True`.

**On failure:** `Genres` above `0` means the content loaded without its backup: the refusal this whole
document is about did not happen. A health other than `200` means the refusal degraded the application,
which a content-load refusal must not do; the schema is intact. `quotes=0` means the wipe removed more
than the genre rows, and every assertion below reads a state this document never set up.

### 3. Confirm the refusal is a notification offering exactly the options that can run

```powershell
$refused = @(Refused-Notifications)
"found=$($refused.Count)"
"type=$($refused[0].type)"
"clearsOnReseed=$($refused[0].dismissTriggerKey -eq 'reseed')"
"offered=$($refused[0].availableActions -join ',')"
```

**Expected:** `found=1`, `type=actionrequired`, `clearsOnReseed=True`, and
`offered=removeoldestbackupthenreseed,reseedwithoutbackup`.

**The absence matters as much as the presence.** `backupthenreseed` is not offered because no backup can
be taken; listing it would offer an option that then refuses, which is the defect #348 was reopened for.

**On failure:** `found=0` with step 2 passing is the pre-#348 behaviour: refused, and silent.

### 4. Confirm both surfaces render the options and the Knowledgebase link

```powershell
$rowScript = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
let row = null;
for (let i = 0; i < 40 && !row; i++) {
  row = [...document.querySelectorAll('tr')].find(r => r.textContent.includes('Content was not loaded'));
  if (!row) await sleep(250);
}
if (!row) return { found: false };
const r = row.getBoundingClientRect();
return { found: true, innerHeight: window.innerHeight,
         buttons: [...row.querySelectorAll('button')].map(b => b.textContent.trim()),
         links: [...row.querySelectorAll('a[target=_blank]')].map(a => a.href),
         x: r.x, y: r.y, width: r.width, height: r.height };
'@
$popup = $rowScript | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/" `
  --out "$out\popup.png" --height 1100 --script-stdin | ConvertFrom-Json
$page  = $rowScript | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications" `
  --out "$out\page.png" --height 1100 --script-stdin | ConvertFrom-Json
"popup found=$($popup.found) links=$($popup.links -join ',') buttons=$($popup.buttons.Count) innerHeight=$($popup.innerHeight)"
"page  found=$($page.found) links=$($page.links -join ',') innerHeight=$($page.innerHeight)"
"page  buttons=$($page.buttons -join ' | ')"
```

**Expected:** both surfaces find the row with a non-zero `innerHeight`, and both link
`https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md`. The
popup is read-only, so `buttons=0`. The page's buttons are
`Remove the oldest backup, then back up and reseed | Reseed without a backup | Dismiss`, with no
`Back up, then reseed`. `popup.png` and `page.png` show the row.

### 5. Reseed without a backup, through the page: asked first, then run and recorded

```powershell
$backupsBefore = @(Get-ChildItem "$dataDir\backups").Count
$without = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
const row = () => [...document.querySelectorAll('tr')].find(r => r.textContent.includes('Content was not loaded'));
const btn = t => [...(row()?.querySelectorAll('button') ?? [])].find(b => b.textContent.trim() === t);
for (let i = 0; i < 40 && !row(); i++) await sleep(250);
let tries = 0;
while (!btn('Confirm') && tries++ < 20) { btn('Reseed without a backup')?.click(); await sleep(500); }
const asked = [...row().querySelectorAll('.form-text')].map(e => e.textContent.trim());
const askedLinks = [...row().querySelectorAll('a[target=_blank]')].map(a => a.href);
btn('Confirm').click();
let sawRunning = false, finished = false;
for (let i = 0; i < 240 && !finished; i++) {
  await sleep(500);
  const running = document.querySelector('.badge.bg-info .spinner-border') !== null;
  sawRunning = sawRunning || running;
  finished = sawRunning && !running;
}
return { asked, askedLinks, sawRunning, finished };
'@
$result = $without | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications" `
  --out "$out\without-backup.png" --script-stdin | ConvertFrom-Json
"asked=$($result.asked -join ' / ')"
"askedLinks=$($result.askedLinks.Count) sawRunning=$($result.sawRunning) finished=$($result.finished)"
Genre-Count
"stillRefused=$(@(Refused-Notifications).Count) backupsAdded=$(@(Get-ChildItem "$dataDir\backups").Count - $backupsBefore)"
"skipLogged=$([bool](docker logs qt-backup-06 2>&1 | Select-String -SimpleMatch 'reseed proceeding WITHOUT a backup (BudgetExceeded)'))"
"skipAudited=$(@((Invoke-RestMethod "$base/admin/audit?table=Database&pageSize=0").items | Where-Object { $_.operation -eq 'BackupSkipped' }).Count)"
```

**Expected:** `asked` names the obstacle (`BudgetExceeded`) before anything ran, with the Knowledgebase
link beside it (`askedLinks` at least `1`), then `sawRunning=True`, `finished=True`, `Genres` back above
`0`, `stillRefused=0`, `backupsAdded=0`, `skipLogged=True` and `skipAudited=1`.

**On failure:** a reseed that ran without `asked` naming the obstacle took the user's permission without
telling them what they were agreeing to.

### 6. Remove the oldest backup, then back up and reseed, through the page

```powershell
Refuse-ContentLoad
"refused=$(@(Refused-Notifications).Count)"
Genre-Count

$remove = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
const row = () => [...document.querySelectorAll('tr')].find(r => r.textContent.includes('Content was not loaded'));
const btn = t => [...(row()?.querySelectorAll('button') ?? [])].find(b => b.textContent.trim() === t);
for (let i = 0; i < 40 && !row(); i++) await sleep(250);
let tries = 0;
while (!btn('Confirm') && tries++ < 20) { btn('Remove the oldest backup, then back up and reseed')?.click(); await sleep(500); }
btn('Confirm').click();
let sawRunning = false, finished = false;
for (let i = 0; i < 240 && !finished; i++) {
  await sleep(500);
  const running = document.querySelector('.badge.bg-info .spinner-border') !== null;
  sawRunning = sawRunning || running;
  finished = sawRunning && !running;
}
return { sawRunning, finished };
'@
$result = $remove | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications" `
  --out "$out\remove-oldest.png" --script-stdin | ConvertFrom-Json
"sawRunning=$($result.sawRunning) finished=$($result.finished) fillerGone=$(-not (Test-Path "$dataDir\backups\filler.db"))"
Genre-Count
$ops = (Invoke-RestMethod "$base/admin/audit?table=Database&pageSize=0").items.operation
"removalAudited=$(@($ops | Where-Object { $_ -eq 'BackupDeleted' }).Count) backupAudited=$(@($ops | Where-Object { $_ -eq 'BackedUp' }).Count)"
```

**Expected:** `refused=1` with `Genres` `0` after the cycle, then `sawRunning=True`, `finished=True`,
`Genres` back above `0`, `fillerGone=True`, `removalAudited=1` and `backupAudited=1`.

**On failure:** `fillerGone=True` with `Genres` still `0` means the backup was removed and nothing was
loaded: a restore point given up for nothing.

### 7. Resolve the obstacle by hand, and confirm backing up first is offered and works

```powershell
Refuse-ContentLoad
Remove-Item "$dataDir\backups\filler.db"
"offered=$(@(Refused-Notifications)[0].availableActions -join ',')"

$backup = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
const row = () => [...document.querySelectorAll('tr')].find(r => r.textContent.includes('Content was not loaded'));
const btn = t => [...(row()?.querySelectorAll('button') ?? [])].find(b => b.textContent.trim() === t);
for (let i = 0; i < 40 && !row(); i++) await sleep(250);
let tries = 0;
while (!btn('Confirm') && tries++ < 20) { btn('Back up, then reseed')?.click(); await sleep(500); }
btn('Confirm').click();
let sawRunning = false, finished = false;
for (let i = 0; i < 240 && !finished; i++) {
  await sleep(500);
  const running = document.querySelector('.badge.bg-info .spinner-border') !== null;
  sawRunning = sawRunning || running;
  finished = sawRunning && !running;
}
return { sawRunning, finished };
'@
$backupsBefore = @(Get-ChildItem "$dataDir\backups").Count
$result = $backup | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications" `
  --out "$out\back-up-first.png" --script-stdin | ConvertFrom-Json
"sawRunning=$($result.sawRunning) finished=$($result.finished) backupsAdded=$(@(Get-ChildItem "$dataDir\backups").Count - $backupsBefore)"
Genre-Count
```

**Expected:** `offered=backupthenreseed`, the only option once a backup can be taken, then `sawRunning=True`, `finished=True`,
`Genres` back above `0` and `backupsAdded=1`.

**This step is the remedy proven.** The Knowledgebase entry tells an operator with a full quota to remove
old backups; done by hand here, it brings back the option that keeps a restore point, and that option
loads the content.

## Canary: run red against the build before #348's startup reporting

Run 2026-09-26 against `4cfe1006`, the commit before step 8 of #348's plan, as `quotinator:canary348`,
through steps 4 (steps 5 to 7 click controls that build does not have).

**This canary ran the Reset-based setup this document no longer uses** (#423). Its three substantive
reds are about reporting, not about how the refusal was reached, so they hold as written; the row below
that reads `quotes=0` is the one whose evidence the re-base changes. Re-running the canary needs an
image built from `4cfe1006` and has not been done since.

| Step | Assertion | Canary |
|---|---|---|
| 2 | the content load is refused | passes, on that build's own Reset-based setup: `quotes=0` |
| 2 | the refusal is logged | **fails**: that build words the line `seeding refused — no backup could be taken`, since reworded |
| 3 | one `backuprefused` notification | **fails**: `found=0` |
| 3 | it offers exactly the options that can run | **fails**: nothing to offer them |
| 4 | both surfaces render the row and the Knowledgebase link | **fails**: `found=False` on both |

Step 3 is the substantive red: that build refused the load and told nobody, the defect this document
exists to catch. Step 2's log line fails there on wording alone.

## Observed effect

**Re-measured 2026-10-10** against `quotinator:local`, on the genre re-seed setup #423 moved this
document to. Every step passes as written.

The seeded container reports `quotes=795` and `Genres` `25`. With the genre rows removed and the budget
full, the next start loads nothing and stays healthy (`health=200`, `quotes=795` unchanged, `Genres` `0`),
and logs `seeding refused: no backup could be taken (BudgetExceeded)`. One `actionrequired` notification,
*Content was not loaded: no backup could be taken*, offers
`removeoldestbackupthenreseed,reseedwithoutbackup` over REST, and the page renders exactly those two as
buttons, plus `Dismiss`, with the Knowledgebase link beside them; the read-only startup popup renders the
row and the link with `buttons=0`.

*Reseed without a backup* first shows *No backup can be taken right now (BudgetExceeded), so this reseed
will leave no restore point…* with the link, then reseeds `Genres` back to `25`, writes no backup
(`backupsAdded=0`), logs `reseed proceeding WITHOUT a backup (BudgetExceeded)` and records one
`BackupSkipped` audit entry. *Remove the oldest backup, then back up and reseed* removes the filler
(`fillerGone=True`), takes one backup and reseeds to `25`; both are audited (`removalAudited=1
backupAudited=1`). With the filler removed by hand, only *Back up, then reseed* is offered, and it writes
one backup (`backupsAdded=1`) and reseeds to `25`. No `[Runtime - Exception]` line at any stop.

**The earlier run, 2026-09-26**, measured the same behaviour through the Reset-based setup, reading
`quotes=0` where this one reads `Genres` `0` and `795` quotes where this one reads `Genres` `25`. What
follows was found on those first runs and is kept because each item is still held by a test.

**The first runs found two application defects and three in this document.**

The application logged the obstacle quoted, `("BudgetExceeded")`: Serilog quotes a string property
unless the template marks it literal, and the unit tests read it through a logger that does not, so they
passed. All five lines naming an obstacle now use `{Obstacle:l}`, held by
`BackupObstacleLogRenderingTests`, which renders through Serilog.

And a reseed resolved its notification while it was still running. Seeding applies one batch per file,
and each successful batch dismissed every `Reseed`-trigger notification, so the notification read as
resolved after the first file, with `13` of `795` quotes loaded and the reseed still going. A
notification is updated after the action that settles it has run, never during it: a seed run no longer
dismisses anything, and its owner does once the run has returned (the notification's own action, the
admin reseed endpoint, and a startup that loaded content). Held by
`DatabaseInitializerTests.ReseedAsync_LeavesTheRefusalForItsCallerToResolve` and
`InitialiseAsync_ContentLoadedAfterAnEarlierRefusal_ResolvesTheRefusal`, whose fixture had been building
the import path with a notification writer that did nothing, which is why no unit test had seen it.

The document's own faults are in *Determinism*: a one-element list read without `@(...)`, a resolved row
waited on after it had left the view, and a leftover filler that made the next cycle's own Reset refuse.

**Also measured, and recorded rather than asserted: a startup's content-load backup is measured against
the absolute ceiling, not the 90% operating quota.** A 95% filler leaves it room in the reserve, and the
content loads. The notification's options consult the operating quota, so the two can disagree between
90% and 100%. This document fills to the ceiling, where both refuse.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-06"
docker logs qt-backup-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-06 --bind $dataDir
Remove-Item -Recurse -Force $dataDir, "C:\repos\Quotinator\.claude\temp\qt-backup-06-captures" -ErrorAction SilentlyContinue
```
