# A review row whose batch is gone offers only dismiss, and its alert says the action is no longer possible

**Smoke:** no
**Environment:** Fresh
**Traces to:** #369

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-orphan-27`, publishing `18627`, on the
current build, with its data directory bind-mounted so the database file can be edited from the host.

A review row outlives its batch when the batch row goes and its actions stay. A database that ran a
build before #372 holds such rows permanently — a reseed there deleted `Import_Batch` and kept
`Import_Action` — and nothing guarantees a future path will never do the same. No endpoint produces
the state today, so this test manufactures it: it stages a conflict, then deletes the batch row from the
host with the container stopped. That is the only honest way to reach a state no endpoint creates, and
it is why this behaviour has a live test as well as unit ones.

## Determinism

**The conflict comes from the shared fixture, not from bundled content.** Same reason as
[`20-pending-review-alert.md`](20-pending-review-alert.md): a first seed inserts everything as an Add,
so there is nothing yet to disagree with. `scripts/testing/stage-import-conflict.csx` supplies a
user-imports file that re-states a bundled quote's id with different text under a `review` policy.

**The batch row is deleted with the container stopped.** SQLite's WAL sidecars belong to the process
holding the file open, and editing it underneath a running container races that connection. Stop, edit,
start — the order
[`../startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md`](../startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md)
uses for the same reason.

**Foreign keys are switched off for the delete, because that is how the real state arose.** A staged
batch is referenced from `Import_FileResourceBatch`, so a plain `DELETE` fails with `FOREIGN KEY
constraint failed` — found on this document's first run, which therefore never produced an orphan and
proved nothing. The pre-#372 `TruncateDataAsync` ran `PRAGMA foreign_keys = OFF` before deleting every
batch and left those references dangling; step 2 does the same to one batch, so the state under test is
the one a legacy database actually holds rather than an approximation of it.

**The restart does not re-seed.** Seeding returns early against a database that already holds quotes
(see [#368](https://github.com/DutchJaFO/Quotinator/issues/368)), so the imports directory is not read
again and the orphan is not replaced by a fresh batch.

**Once the batch row is gone, the file name on the review page can only have come from the alert.** The
batch's own `Name` and the alert's `fileName` are the same string, which is why the deletion matters:
with the row gone, nothing else on the server still holds that name, so its presence on the page is
evidence of where it was read from.

**Count objects, not matching lines, and wrap a filtered result in `@(...)`** — see the index's *A count
is evidence only if the instrument counts the right thing*.

## Steps

### 1. Stage a conflict and record which batch it landed in

```powershell
$bind    = Join-Path $env:TEMP "qt-orphan-27-bind"
$imports = Join-Path $bind "imports"
dotnet script scripts/testing/stage-import-conflict.csx -- --imports $imports

dotnet script scripts/testing/test-env.csx -- create --name qt-orphan-27 --port 18627 --bind $bind

while ((Invoke-RestMethod "http://localhost:18627/api/v1/quotes?page=1&pageSize=1").totalCount -lt 1) { Start-Sleep 2 }

$alert   = @((Invoke-RestMethod "http://localhost:18627/api/v1/notifications?pageSize=0").items |
             Where-Object { $_.metadataKind -eq 'importReviewPending' -and -not $_.isDismissed })[0]
$payload = $alert.metadata | ConvertFrom-Json
$batchId = $payload.batchId
"file=$($payload.fileName) batch=$batchId"
"pending actions = $((Invoke-RestMethod "http://localhost:18627/api/v1/import/actions?status=Pending&pageSize=1").totalCount)"
```

**Expected:** `file=conflicting.json`, a real batch id, and `pending actions = 1`.

**On failure:** `pending actions = 0` means the fixture staged nothing. Stop — every later step would
pass against an empty page and prove nothing.

### 2. Remove the batch row, leaving its actions and its alert behind

```powershell
docker stop qt-orphan-27 | Out-Null
dotnet script scripts/testing/execute-sql.csx -- --db "$bind\quotinatordata.db" `
  --sql "PRAGMA foreign_keys = OFF; DELETE FROM Import_Batch WHERE LOWER(Id) = LOWER('$batchId');"
docker start qt-orphan-27 | Out-Null
while ($true) {
  try { if ((Invoke-RestMethod "http://localhost:18627/api/v1/health").status -eq 'healthy') { break } } catch { }
  Start-Sleep 2
}

try   { Invoke-RestMethod "http://localhost:18627/api/v1/import/batches/$batchId" | Out-Null; "batch lookup -> 200" }
catch { "batch lookup -> $($_.Exception.Response.StatusCode.value__)" }
"pending actions = $((Invoke-RestMethod "http://localhost:18627/api/v1/import/actions?status=Pending&pageSize=1").totalCount)"
```

**Expected:** `OK — 1 row(s) affected.`, then `batch lookup -> 404` and `pending actions = 1`. That pair
is the orphan: an action awaiting a decision whose parent row does not exist.

**On failure:** `0 row(s) affected` means the id did not match and nothing was removed — the rest of the
test would then be checking a perfectly healthy batch.

### 3. Confirm the review page names the file and offers only dismiss

The page is server-rendered on first load, so its content is in the HTML and needs no browser here.

```powershell
$page = (Invoke-WebRequest "http://localhost:18627/import-review" -UseBasicParsing).Content
"names the file:     $($page.Contains('conflicting.json'))"
"shows the batch id: $($page.Contains($batchId))"
"says batch is gone: $($page.Contains('Import batch no longer exists'))"
"offers Keep/Take:   $($page.Contains('Keep existing') -or $page.Contains('Take incoming'))"
"offers Dismiss:     $($page.Contains('Dismiss'))"
```

**Expected:** `names the file: True`, `shows the batch id: False`, `says batch is gone: True`,
`offers Keep/Take: False`, `offers Dismiss: True`.

`shows the batch id: False` is the assertion #303's fallback would fail — it rendered the id in the File
column whenever the batch row was missing. `names the file: True` alone cannot tell the two builds
apart only because the id and the name never both appear; the pair together can.

### 4. Confirm the alert reports its action as no longer possible

```powershell
$notifications = (Invoke-WebRequest "http://localhost:18627/notifications" -UseBasicParsing).Content
"names the file:           $($notifications.Contains('conflicting.json'))"
"state: no longer possible: $($notifications.Contains('Action no longer possible'))"
"offers its Decide action: $($notifications.Contains('>Decide</button>'))"
```

**Expected:** `names the file: True`, `state: no longer possible: True`, `offers its Decide action: False`.

`names the file: True` is the control here: it proves the alert being inspected is on the page at all,
so the other two are about that row rather than about an empty list. The state is what relays the fact
to the operator — withdrawing the Decide button alone would leave an empty cell that reads exactly like a
row that never had an action.

### 5. Dismiss the row, and confirm its batch is discarded and its alert resolved

**Browser, not `Invoke-WebRequest`.** The Dismiss control needs the Blazor circuit. Open
`http://localhost:18627/import-review`, give the circuit a moment (a click issued immediately after
navigating is silently swallowed — see
[`20-pending-review-alert.md`](20-pending-review-alert.md) step 9), click **Dismiss** on the row, and
take a screenshot of the result. Then:

```powershell
"pending actions = $((Invoke-RestMethod "http://localhost:18627/api/v1/import/actions?status=Pending&pageSize=1").totalCount)"
@((Invoke-RestMethod "http://localhost:18627/api/v1/import/actions?pageSize=0").items |
  Where-Object { $_.batchId -eq $batchId }) | ForEach-Object { "action status=$($_.status)  ($($_.entityType) $($_.actionType))" }
$after = @((Invoke-RestMethod "http://localhost:18627/api/v1/notifications?pageSize=0").items |
  Where-Object { $_.metadataKind -eq 'importReviewPending' -and ($_.metadata | ConvertFrom-Json).batchId -eq $batchId })[0]
"alert isDismissed=$($after.isDismissed) reason=$($after.dismissReason)"
```

**Expected:** the screenshot shows the page reading *Nothing is waiting for review.*; then
`pending actions = 0`; the batch's `Quote Modify` at `action status=Discarded` and its `Source Unchanged`
at `action status=Applied`; and `alert isDismissed=True reason=resolved`.

The `Source Unchanged` row is a plan-time no-op — the fixture restates a source already stored — and it
stays `Applied` because nothing was ever written for it, so a discard has nothing of it to undo
([#389](https://github.com/DutchJaFO/Quotinator/issues/389)). Before #389 that same row made the whole
batch read as already applied: the click raised an error and changed nothing.

Discarding settles the review as surely as deciding it would — the operator dealt with the batch by
keeping none of it — so the alert records `resolved`, not a user's plain dismissal.

### 6. Confirm the Status column still renders its states as words

**Browser, and a screenshot.** The question is what a person reads, and `NotificationDisplayStatus` is
referenced from a `.razor` switch whose type references the build does not reliably check. Open
`http://localhost:18627/notifications`, switch the filter to **All**, and take a screenshot.

```powershell
$all = (Invoke-RestMethod "http://localhost:18627/api/v1/notifications?pageSize=0").items
"rows = $(@($all).Count)"
```

**Expected:** the screenshot shows one badge per row and every badge carrying a word — the alert from
step 5 reads **Done**, the others **Active** — and the page shows as many rows as `rows =` reports. An
empty badge, or a page that fails to render at all, is the failure this step exists to catch.

## Canary — run red against the build before #369

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Run against
the build of `806a0ab0` (the last commit before #369's first code change) via `git worktree add` and
`docker build -t quotinator:canary369`, 2026-09-10:

| Step | Assertion | Pre-work result |
|---|---|---|
| 2 | `batch lookup -> 404`, `pending actions = 1` | passes — the orphan is a database state, not something #369 creates |
| 3 | `names the file: True` | **fails** — `False`; the File column shows the id |
| 3 | `shows the batch id: False` | **fails** — `True` |
| 3 | `says batch is gone: True` | **fails** — `False` |
| 3 | `offers Keep/Take: False` | **fails** — `True`; both impossible decisions are offered |
| 3 | `offers Dismiss: True` | **fails** — `False`; there is no such control |
| 4 | `names the file: True` | passes — the control; the alert is on the page on both builds |
| 4 | `state: no longer possible: True` | **fails** — `False`; the row reads Active |
| 4 | `offers its Decide action: False` | **fails** — `True` |
| 5 | Dismiss the row | **not reachable** — there is no Dismiss control to click |

Step 6 was not run against the canary: it guards the enum move #369 makes, which the pre-work build does
not contain, so there is nothing there for it to fail on.

The first canary run is not in this table because it was void — see *Determinism* on why step 2 now
switches foreign keys off. Container, image, bind mount and worktree removed afterwards.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-orphan-27
Remove-Item -Recurse -Force (Join-Path $env:TEMP "qt-orphan-27-bind")
```
