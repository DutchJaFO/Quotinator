# Each applied migration says what it did, one notification per migration

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #371

## Preconditions

**Beyond the profile.** One container, `qt-notif-16` on `18516`, run on two images against the same
volume: `ghcr.io/dutchjafo/quotinator:1.8.3` first, which creates and seeds a released-shape database,
then this build, which migrates it.

**The previous published tag is the prior image, deliberately**, not the milestone base image. This is
the upgrade users are about to perform, and it is the only arrangement that shows a description
resolved by a *newer* build for a migration an *older* one applied, which is the property requirement 3
rests on: nothing about a migration's description is persisted.

The companion case, a database created from scratch, cannot share a database with this one and has its
own document: [*A created database says so*](15-a-created-database-says-so.md).

## Determinism

**The expected count is derived from this same run, never written down.** The application logs
`applying {Count} pending {Phase} migration(s)` once per phase, and the number the notifications are
compared against is the sum of those `Count` values read from this container's own log. A figure
written into this document would go stale the moment any milestone adds a migration, and would then be
"fixed" by editing the digit rather than by anyone checking what happened. For the same reason **no
step names a migration number or a phase's starting version**: see the index's *Never assert a specific
migration number or schema version*.

**No step asserts a total notification count.** The upgrade also writes what's-new notifications and
carries 1.8.3's own announcement forward. Every assertion here counts occurrences of the one kind this
document is about.

**Three numbers are read together, because the count alone cannot fail usefully.** A correct run and a
run that resolved every description to the same fallback produce the same count; only the number of
*distinct* bodies separates them, and only a check for empty bodies catches a description that resolved
to nothing.

**`databasecreated` must be absent, and that is correct rather than a miss.** The 1.8.3 container
created this database, and 1.8.3 has no such producer.

**Read the log before every stop**, per the index's *Read the log before the application stops*:
`reenter` replaces the container, so step 1 reads its `[Runtime - Exception]` count before step 2
re-enters, and step 2 counts only what was logged since.

## Steps

### 1. Create a released-shape database on the previous published tag

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-16 --port 18516 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3

function Kinds {
  $items = (Invoke-RestMethod "http://localhost:18516/api/v1/notifications?pageSize=0").items
  @($items | Group-Object metadataKind | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' '
}
function Thrown { "thrown=$(@(docker logs qt-notif-16 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count)" }

Kinds
@(Invoke-RestMethod "http://localhost:18516/api/v1/quotes?pageSize=0").totalCount
Thrown
```

**Expected:** the readiness poll inside `create` establishes that 1.8.3 started healthy; the quote
count is greater than zero, so this is a database with content to migrate rather than an empty one; and
`thrown=0`. Neither `migrationapplied` nor `databasecreated` appears, because neither producer exists
in that release.

**On failure:** if 1.8.3 cannot be pulled, stop and report it rather than substituting another tag.
The index's *Users skip versions* records that an unpullable tag is the trigger to revisit the approach,
not a reason to quietly change what the test upgrades from.

### 2. Upgrade to this build, and compare the notifications against the log

```powershell
Thrown
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-16 --port 18516 `
  --image quotinator:local

# The expectation, derived from this container's own log rather than written down.
$log = docker logs qt-notif-16 2>&1 | Out-String
$expected = ([regex]::Matches($log, 'applying (\d+) pending') |
             ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum).Sum
"migrations this run says it applied: $expected"

$applied = @((Invoke-RestMethod "http://localhost:18516/api/v1/notifications?pageSize=0").items |
             Where-Object { $_.metadataKind -eq 'migrationapplied' })
"migrationapplied notifications: $($applied.Count)"
"distinct bodies:                $(@($applied.body | Sort-Object -Unique).Count)"
"empty bodies:                   $(@($applied | Where-Object { -not $_.body }).Count)"
"phases:                         $((@($applied.metadata | ForEach-Object { ($_ | ConvertFrom-Json).phase } | Sort-Object -Unique)) -join ' ')"
Kinds
Thrown
```

**Expected:** `$expected` is greater than zero; the notification count equals it exactly, one per
migration; `distinct bodies` equals that same number, so no two notifications say the same thing;
`empty bodies` is `0`, so every one names what its migration did rather than only its version; both
`App` and `Data` appear among the phases, because this upgrade spans both lists; `databasecreated` does
not appear; and `thrown=0`.

**On failure:** a matching count with `distinct bodies` lower than it means descriptions are resolving
to the wrong migration or to a shared fallback, which the count alone cannot distinguish from a correct
run. A count of `0` against a non-zero `$expected` means the producer did not run at all.

### 3. Confirm the rows render on both surfaces, and carry no detail table

```powershell
$shots = Join-Path (Get-Location) ".claude\temp\qt-notif-16"
if (Test-Path $shots) { Remove-Item -LiteralPath $shots -Recurse -Force }
New-Item -ItemType Directory -Path $shots | Out-Null

@"
await new Promise(r => setTimeout(r, 800));
const rows = [...document.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const mine = rows.filter(r => r.querySelector('.notification-body').textContent.includes('Applied to this database'));
return { matched: mine.length,
         titled: mine.filter(r => r.querySelector('.notification-title')?.textContent.trim().length > 0).length,
         opensDetail: mine.filter(r => r.querySelector('.notification-detail-open')).length,
         expandsInPlace: mine.filter(r => r.querySelector('details.notification-detail')).length,
         viewport: window.innerHeight };
"@ | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18516/notifications" `
      --out "$shots\migrationapplied-page.png" --width 1280 --height 1400 --script-stdin

@"
await new Promise(r => setTimeout(r, 800));
const scope = document.querySelector('.modal');
const rows = [...scope.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const mine = rows.filter(r => r.querySelector('.notification-body').textContent.includes('Applied to this database'));
return { matched: mine.length,
         opensDetail: mine.filter(r => r.querySelector('.notification-detail-open')).length,
         expandsInPlace: mine.filter(r => r.querySelector('details.notification-detail')).length,
         viewport: window.innerHeight };
"@ | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18516/" `
      --out "$shots\migrationapplied-popup.png" --width 1000 --height 1400 --script-stdin
```

**Expected:** on the page, `matched` and `titled` both equal step 2's notification count, and both
`opensDetail` and `expandsInPlace` are `0`, because this kind declares no detail table. On the popup,
`matched` is non-zero with the same two zeros. Both report a non-zero `viewport`, or the measurement was
taken against a pane with no height and says nothing (see the index's *Does the browser actually have a
viewport?*).

**On failure:** `matched` below the count from step 2 means the page is not showing every row the API
returns, which is a paging or rendering defect rather than a producer one. `opensDetail` above `0`
contradicts `NotificationTableTests.EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`, and one
of the two is wrong.

### 4. Restart again, and confirm no migration is announced twice

```powershell
Thrown
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-16 --port 18516 `
  --image quotinator:local

$again = @((Invoke-RestMethod "http://localhost:18516/api/v1/notifications?pageSize=0").items |
            Where-Object { $_.metadataKind -eq 'migrationapplied' })
"migrationapplied notifications after a second restart: $($again.Count)"
Thrown
```

**Expected:** the same count step 2 read, unchanged. Nothing was pending this time, so nothing was
applied and nothing is reported; and even had the producer run, each migration's identity is its phase
and version, so none of them could be written twice. `thrown=0`.

**On failure:** a higher count is a dedupe failure. A lower one means notification rows are being
removed by a restart, which nothing in this path should do.

## Observed effect

**Measured 2026-10-11**, against an image built from this branch, after being run first against a
build from the commit before the work started.

1.8.3 started healthy with `799` quotes and wrote one notification of its own, carrying no
`metadataKind` at all (that release predates typed metadata). Upgrading to this build read:

```text
migrations this run says it applied: 29
migrationapplied notifications: 29
distinct bodies:                29
empty bodies:                   0
phases:                         App Data
kinds: announcement=1 migrationapplied=29 whatsnew=1
```

Both surfaces rendered all 29: the page reported `matched: 29, titled: 29, opensDetail: 0,
expandsInPlace: 0`, and the popup `matched: 29` with the same two zeros, each against a `1400`
viewport. A second restart left the count at `29`. `databasecreated` never appeared, as 1.8.3 created
that database. No container logged an exception of its own on any of the five reads.

**Against the pre-issue build the same upgrade read `migrations this run says it applied: 28` and
`migrationapplied notifications: 0`.** The derived expectation being non-zero there is what makes this
document's own comparison meaningful: a broken log pattern would have produced `0` against `0` and
passed while measuring nothing. The count differing between the two runs, 28 then 29, is this build's
own new migration, and the expectation tracked it without the document being edited.

## Canary

Build the commit before [#371](https://github.com/DutchJaFO/Quotinator/issues/371)'s first code
change, under a distinct tag, and run steps 2 to 4 against it. Before #371 neither the migration
record nor `System_SchemaVersion` could say anything beyond the fact that a numbered migration had
applied, so step 2 finds no notification naming what any migration did.

**Step 2 compares the notifications against the log rather than against a number this document
states.** Migration counts move whenever any milestone adds one and are consolidated before a release,
so the assertion is that every migration the log reports applying has a notification saying what it
did, derived in the same run.

Steps 2 and 4 are the pair: announced once per migration, and not again on a restart with nothing left
to apply. Step 3's absence of a detail table is stated alongside what the row does render.

Remove the container, image and worktree afterwards.

## Cleanup

```powershell
Thrown
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-16
docker image rm ghcr.io/dutchjafo/quotinator:1.8.3
```

**Expected:** `thrown=0` before the stop. The named volume goes with the container, so no database
survives this document. The pulled 1.8.3 image is removed because nothing else in the suite depends on
it being present rather than pullable.
