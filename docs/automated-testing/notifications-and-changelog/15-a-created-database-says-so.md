# A database created from scratch says so, and a routine restart does not say it again

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #371

## Preconditions

**Beyond the profile, nothing.** A first boot against an empty volume *is* the trigger: it is what
creates a database. This document imports no content of its own and introduces no defect.

The companion case, a migration applied to a database an earlier build created, cannot share a
database with this one and has its own document:
[*Each applied migration says what it did*](16-each-applied-migration-says-what-it-did.md). Creating a
database takes the baseline path, which applies no individual migration at all, so one container can
only ever show one of the two.

## Determinism

**No step asserts a total notification count.** How many notifications exist depends on which
producers exist and what the bundled changelog flags, both of which move every milestone. Every
assertion here counts occurrences of the one kind this document is about, which is the *cause* under
test.

**No step names a schema version.** The creation notification carries both of them, and a figure
written into this document would go stale the moment any milestone adds a migration, then get "fixed"
by editing the digit. What is asserted is that both fields are present and non-empty: see the index's
*Never assert a specific migration number or schema version*.

**`migrationapplied` must be absent throughout, and that is an assertion rather than an omission.** It
is what proves the baseline path reports no individual migration, which is the distinction the
companion document's count depends on.

**Read the log before every stop**, per the index's *Read the log before the application stops*:
`reenter` replaces the container, so step 1 reads its `[Runtime - Exception]` count before step 2
re-enters.

## Steps

### 1. Cold start on an empty volume, and confirm the creation notification

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-15 --port 18515 `
  --image quotinator:local

function Kinds {
  $items = (Invoke-RestMethod "http://localhost:18515/api/v1/notifications?pageSize=0").items
  @($items | Group-Object metadataKind | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' '
}
function Thrown { "thrown=$(@(docker logs qt-notif-15 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count)" }

Kinds
$created = @((Invoke-RestMethod "http://localhost:18515/api/v1/notifications?pageSize=0").items |
             Where-Object { $_.metadataKind -eq 'databasecreated' })
$created | Select-Object title, body, metadata
$payload = if ($created.Count -eq 1) { $created[0].metadata | ConvertFrom-Json } else { $null }
"dataSchemaVersion present: $($null -ne $payload.dataSchemaVersion)"
"appSchemaVersion present:  $($null -ne $payload.appSchemaVersion)"
Thrown
```

**Expected:** `databasecreated=1` among the kinds, `migrationapplied` absent entirely, and `thrown=0`.
The single row has a non-empty title and a non-empty body, and both payload fields report `True`.

**On failure:** `migrationapplied` present here means the baseline path reported migrations it never
applied. Stop, and report it against the companion document too: its count is derived on the assumption
that a created database contributes none.

### 2. Restart the same volume, and confirm it is not announced again

```powershell
Thrown
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-15 --port 18515 `
  --image quotinator:local

Kinds
Thrown
```

**Expected:** `databasecreated=1` still, not `2`, and `migrationapplied` still absent. `thrown=0` for
the replaced container. This startup found an existing database with nothing pending, so it had neither
event to report.

**On failure:** `databasecreated=2` is a dedupe failure rather than a producer failure: the event is
being re-reported instead of re-detected. Either reading means a routine restart is not silent.

### 3. Confirm the row renders, and carries no action

```powershell
$shots = Join-Path (Get-Location) ".claude\temp\qt-notif-15"
if (Test-Path $shots) { Remove-Item -LiteralPath $shots -Recurse -Force }
New-Item -ItemType Directory -Path $shots | Out-Null

@"
await new Promise(r => setTimeout(r, 800));
const rows = [...document.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const row  = rows.find(r => r.querySelector('.notification-body').textContent.toLowerCase().includes('no database was found'));
const b = row.getBoundingClientRect();
return { x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
         title: row.querySelector('.notification-title').textContent.trim(),
         titleInsideBody: row.querySelector('.notification-body').textContent
             .includes(row.querySelector('.notification-title').textContent.trim()),
         opensDetail: !!row.querySelector('.notification-detail-open'),
         buttons: [...row.querySelectorAll('button')].map(x => x.textContent.trim()),
         viewport: window.innerHeight };
"@ | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18515/notifications" `
      --out "$shots\databasecreated-page.png" --width 1280 --height 1400 --script-stdin
```

**Expected:** the row is found; `title` is non-empty and `titleInsideBody` is `false`, so the title is
its own element rather than repeated inside the body; `opensDetail` is `false`, because this kind
declares no detail table; `buttons` is `Dismiss` alone, because nothing resolves the fact that a
database was created; and `viewport` is non-zero, or the reading was taken against a pane with no
height and says nothing (see the index's *Does the browser actually have a viewport?*).

**On failure:** `opensDetail: true` contradicts
`NotificationTableTests.EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`, and one of the two
is wrong. An extra button means an action was wired to a notification that has nothing to act on.

## Observed effect

**Measured 2026-10-11**, against an image built from this branch, after being run first against a
build from the commit before the work started.

A first boot on an empty volume produced `announcement=1 databasecreated=1 reseedfileapplied=5
whatsnew=1`, and no `migrationapplied` at all. The one created row read:

```text
title : Database created
body  : No database was found, so a new one was created at data schema v28 and app schema v9.
        There was nothing to preserve.
```

Both payload fields were present. The restart left the kinds identical, `databasecreated` still `1`.
The row rendered with its title as its own element (`titleInsideBody: false`), no detail control, and
`Dismiss` as its only button, in a `974 x 83` rectangle against a `1400` viewport. No container logged
an exception of its own on any of the three reads.

**Against the pre-issue build the same first boot read `announcement=1 reseedfileapplied=5
whatsnew=1`**, with `databasecreated` absent, and the page carried no such row to find. The same
browser instrument did locate the announcement row in that run (`control: true`, 7 rows), so the
absence was the notification's and not the selector's.

## Canary

Build the commit before [#371](https://github.com/DutchJaFO/Quotinator/issues/371)'s first code
change, under a distinct tag, and run steps 1 and 3 against it. A database created from scratch said
nothing before #371, so step 1 finds no creation notification and step 3 has no row to render.

**Steps 1 and 2 are a pair and neither is worth running alone.** Step 1 asserts the announcement
appears on a cold start, step 2 asserts the same volume restarted does not announce it again. A build
that announced on every boot satisfies step 1 and fails step 2; one that never announces fails step 1
and passes step 2. Only together do they describe the behaviour.

Step 3 also asserts an absence, that the row carries no action, so it states what the row does carry in
the same breath rather than counting buttons and finding none.

Remove the container, image and worktree afterwards.

## Cleanup

```powershell
Thrown
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-15
```

**Expected:** `thrown=0` before the stop. The container's named volume goes with it, so no database
survives this document.
