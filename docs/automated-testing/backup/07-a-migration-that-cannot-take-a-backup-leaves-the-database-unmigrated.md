# A migration that cannot take a backup leaves the database unmigrated, and says why

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-07`, publishing `18387`, on a bind
directory. The prior image is the published `ghcr.io/dutchjafo/quotinator:1.8.3`, the release users
upgrade from, so the current build has migrations to apply. Before the current build starts, the backup
folder is filled to the budget, so the backup every migration takes first cannot be taken.

A migration changes the schema in a way only a backup can undo. Until #348 a failed pre-migration backup
was ignored and every pending migration ran unprotected. This proves the migration is refused instead,
that the application degrades and says which obstacle stopped it and where to read about it, that the
schema is left exactly as the prior build wrote it, and that removing the obstacle lets the same
database migrate.

## Determinism

- **The prior version is a published tag, not a milestone snapshot.** 1.8.3 is what users run today, and
  its schema is behind any build of this milestone, so pending migrations are guaranteed without editing
  a version row.
- **The obstacle is a full budget, made by one sparse file the size of the whole 1 GB default**, for the
  reason `backup/06` records: the backup taken before a migration is measured against the absolute
  ceiling, not the 90% operating quota.
- **The recorded schema version is read from `GET /api/v1/version`**, whose `database.schemaVersion` is
  the version recorded in the database, including after a refusal, and which answers while degraded.
  `execute-sql.csx` cannot read it: for a query it prints a row count, not a value (measured on the
  canary run, where comparing two such lines reported `unchanged=True` having measured nothing).
- **No migration number is asserted.** Only that the recorded version is unchanged by the refused start,
  and higher after the remedied one.
- **The log is read before each stop.**

## Steps

### 1. Let the prior release create its database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-07"
$out     = "C:\repos\Quotinator\.claude\temp\qt-backup-07-captures"
Remove-Item -Recurse -Force $dataDir, $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups", $out | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-07 --port 18387 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir

function Schema-Version { [int](Invoke-RestMethod "http://localhost:18387/api/v1/version").database.schemaVersion }
$before = Schema-Version
"before=$before"
docker logs qt-backup-07 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind $dataDir
```

**Expected:** the prior release reports healthy, `before` is above `0`, and the log read before the stop
finds nothing.

### 2. Fill the budget, and start the current build

```powershell
$stream = [System.IO.File]::Create((Join-Path $dataDir "backups\filler.db"))
$stream.SetLength([int64]1073741824)
$stream.Close()

dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 `
  --image quotinator:local --bind $dataDir --wait-listening

$health = dotnet script scripts/testing/http.csx -- --url "http://localhost:18387/api/v1/health" --wait-for 503 | ConvertFrom-Json
"status=$($health.status)"
"namesObstacle=$($health.reason.StartsWith('Database migration refused: no backup could be taken (BudgetExceeded).'))"
"linksEntry=$($health.reason.Contains('https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md'))"
"refusalLogged=$([bool](docker logs qt-backup-07 2>&1 | Select-String -SimpleMatch 'migration refused: no backup could be taken (BudgetExceeded)'))"
```

**Expected:** `status=unhealthy`, `namesObstacle=True`, `linksEntry=True` and `refusalLogged=True`.

**On failure:** a `200` means the migration ran without its backup, the defect this document exists to
catch.

### 3. Confirm the degraded page links the Knowledgebase entry

```powershell
$script = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
let link = null;
for (let i = 0; i < 40 && !link; i++) {
  link = [...document.querySelectorAll('a[target=_blank]')].find(a => a.href.includes('no-backup-could-be-taken'));
  if (!link) await sleep(250);
}
return { innerHeight: window.innerHeight, href: link?.href ?? null, text: link?.textContent.trim() ?? null };
'@
$page = $script | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18387/" `
  --out "$out\degraded-home.png" --height 1100 --script-stdin | ConvertFrom-Json
"innerHeight=$($page.innerHeight) href=$($page.href) text=$($page.text)"
```

**Expected:** a non-zero `innerHeight`, `href` naming
`https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md`, and
`text=How to resolve this (Knowledgebase)`. `degraded-home.png` shows the startup error popup with the
reason and the link.

### 4. Confirm nothing was migrated

```powershell
$refused = Schema-Version
"refused=$refused unchanged=$($refused -eq $before)"
docker logs qt-backup-07 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind $dataDir
```

**Expected:** `unchanged=True`, and the log read finds nothing.

**On failure:** a changed version with step 2 reporting a refusal means part of the migration ran before
it was refused, which is the partial state the refusal exists to prevent.

### 5. Remove the obstacle, and confirm the same database migrates

```powershell
Remove-Item (Join-Path $dataDir "backups\filler.db")
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 `
  --image quotinator:local --bind $dataDir
"quotes=$((Invoke-RestMethod 'http://localhost:18387/api/v1/quotes?pageSize=1').totalCount)"
"backups=$(@(Get-ChildItem "$dataDir\backups").Count)"
"migrated=$((Schema-Version) -gt $before)"
docker logs qt-backup-07 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
```

**Expected:** the restart reports healthy, `quotes` above `0`, `backups` at least `1` (the backup the
migration took first), `migrated=True`, and the log read finds nothing.

**This step is the remedy proven**, and the positive control: every step above asserts a refusal, and a
build refusing every migration would satisfy them all without this one.

## Canary: run red against the build before #348's migration refusal

Run 2026-09-26 against `4cfe1006`, the commit before step 8 of #348's plan, as `quotinator:canary348`:

| Step | Assertion | Canary |
|---|---|---|
| 1 | the prior release records a version | passes: `before=5` |
| 2 | the start degrades, naming the obstacle and the entry | **fails**: the migration ran and the start was healthy |
| 3 | the degraded page links the entry | **fails**: no link |
| 4 | the recorded version is unchanged | **fails**: `refused=9`, migrated with no backup taken |
| 5 | the remedied start takes a backup | **fails**: `backups=0`, nothing was left to migrate |

## Observed effect

**Measured 2026-09-26** against `quotinator:local`, upgrading from `1.8.3`.

With the budget full, the current build refuses the migration and degrades: `/health` answers `503`
with a reason beginning `Database migration refused: no backup could be taken (BudgetExceeded).`,
followed by the cause, the remedies and the Knowledgebase address, and the log records
`migration refused: no backup could be taken (BudgetExceeded)`. The degraded popup shows that reason with
a *How to resolve this (Knowledgebase)* link. The recorded version stays at `5`. With the filler removed,
the same volume takes one backup, migrates past `5`, and serves `795` quotes. No `[Runtime - Exception]`
line at any stop.

**Recorded, not asserted: the degraded popup's counts read zero.** Under *The database was left at its
last known-good state* it lists `0` for every table, although the database holds the release's `795`
quotes. The counts are read only once initialisation completes, and a refused migration stops it first.
`startup-and-degradation/05` records the same zeros for a different degraded state, so this is not new
with #348.

**The first canary run found a fault in this document.** It read the recorded version with
`execute-sql.csx`, which prints a row count for a query rather than its value, so comparing two such lines
reported `unchanged=True` having measured nothing. The version is now read from `GET /api/v1/version`.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-07"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind $dataDir
Remove-Item -Recurse -Force $dataDir, "C:\repos\Quotinator\.claude\temp\qt-backup-07-captures" -ErrorAction SilentlyContinue
```
