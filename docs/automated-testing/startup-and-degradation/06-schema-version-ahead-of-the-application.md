# A recorded schema version ahead of the build stays healthy and says so

**Smoke:** no
**Environment:** Fresh
**Traces to:** #327, #289

> **⚠ This document asserts a contract that is being reversed.**
> [#350](https://github.com/DutchJaFO/Quotinator/issues/350) establishes that an overshoot must
> **degrade** rather than run healthy: the missing migrations may have added, altered or removed things
> this build does not expect, so the schema's shape is unknown and serving from it is a foot gun. Every
> assertion below about `/health` returning `200` — and the step 3 note telling you not to "fix" a `503`
> — is superseded by that issue, which owns rewriting this document. It is left in place meanwhile
> because deleting it before its replacement exists would trade a wrong test for no test.

## Preconditions

**Beyond the profile.** The database is the one the Fresh profile's own startup creates, fully migrated,
then given one extra `System_ConsumerSchemaVersion` row *after* the container has stopped. The container
binds its data directory to a host path (`--bind`) so the row can be written from the host — a docker
volume would put the file somewhere `execute-sql.csx` cannot reach.

**This is deliberately not a degradation scenario**, and it is the only document in this category that
is not. Per #289 the application detects an overshoot on purpose and carries on: the schema is complete
and only the counter is stale, which is what happens after a migration squash against a database that
already applied the pre-squash migrations individually. Asserting `503` here would either fail outright
or get "fixed" by breaking correct behaviour.

## Determinism

- **The extra version is computed, never written as a literal.** Step 2 inserts `MAX(Version) + 1`, so
  the row is one beyond whatever this build actually migrated to. A literal would need re-deriving every
  time a milestone adds a migration, and would silently stop overshooting once the build caught up with
  it. This is the opposite choice from
  [`notifications-and-changelog/03`](../notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md),
  which necessarily names versions because its subject is a database sitting *between* two of them; here
  the subject is a relationship, so the relationship is what gets written.
- **The consumer counter is the one that moves.** `System_ConsumerSchemaVersion` tracks the app's own
  migrations, `System_SchemaVersion` tracks `Quotinator.Data`'s. Either overshooting sets the flag, and
  moving one keeps the construction minimal.
- **The row is added while the container is stopped.** Writing to the database under a running
  container races the app's own connections, and the result would depend on timing rather than on the
  state this test builds.
- **The second start waits for *healthy*, not merely listening.** Unlike the degradation documents in
  this category, healthy is the expected outcome — so the wait is itself part of the assertion, and a
  build that wrongly degraded fails here rather than at a later step.
- **Without step 2 there is no notification at all.** Confirmed in-process by
  `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot`,
  whose assertion fails when the version is recorded at the level the database already holds instead of
  one beyond it — so step 4 is discriminating rather than reporting a notification that is always there.

## Steps

### 1. Create a healthy, fully migrated database

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-06 --port 18406 `
  --image quotinator:local --bind $dataDir
```

**Expected:** the environment reports healthy, leaving a fully migrated database in `$dataDir`.

**On failure:** if the build cannot reach healthy on a fresh database, nothing below is about
overshoot — stop and fix that first.

### 2. Stop the container and record a version one beyond this build

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-06 --bind $dataDir

dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" `
  --sql "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) SELECT MAX(Version) + 1, '2026-08-27 00:00:00' FROM System_ConsumerSchemaVersion;"
```

**Expected:** `OK — 1 row(s) affected.`

**On failure:** a SQL error means the overshoot state was never built, and every step below would be
asserting against an ordinary healthy database — which proves nothing. Stop.

### 3. Restart, and confirm the application stays healthy

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-06 --port 18406 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/health" --expect 200 --status
```

**Expected:** the wait reports healthy and `/health` returns `200`.

**On failure:** a `503` is the regression this document exists to catch — an overshoot being treated as
a fault. The schema is complete; only the bookkeeping is stale, and the application must keep working.
Do not "fix" this by relaxing the expectation to `503`.

### 4. Confirm the overshoot is surfaced as a notification

```powershell
$n = dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/notifications" | ConvertFrom-Json
$overshoot = $n.items | Where-Object { $_.metadataKind -eq 'schemaversionovershoot' }
"found=$($null -ne $overshoot)"
"type=$($overshoot.type)"
"bodyNamesTheRemedy=$($overshoot.body -match 'database Reset')"
```

**Expected:** `found=True`, `type=actionrequired`, and `bodyNamesTheRemedy=True`.

**On failure:** health at `200` with no notification is the worse half of this defect — the application
noticed the overshoot and told nobody, leaving an operator with stale bookkeeping they cannot discover.
`found=False` fails the test even though step 3 passed.

### 5. Confirm the detection is in the log as well as the API

```powershell
docker logs qt-startup-06 2>&1 | Select-String -Pattern 'schema version overshoot detected'
```

**Expected:** one match, naming both the recorded and the known versions.

**On failure:** no match while step 4 passed means the notification came from somewhere other than this
startup's own detection — most likely a row left over from an earlier run against the same directory.
Re-run from step 1 with a fresh `$dataDir`.

## Observed effect

**Measured 2026-08-27** against `quotinator:local`, on the first run of this document.

`/health` returns `200`; the application is fully functional and the quote endpoints serve normally. The
log carries one warning at startup:

> `[Database - Init] schema version overshoot detected: recorded data v11 (known: v11), recorded app v6
> (known: v5) — schema is treated as complete, but a database Reset is recommended to true up the
> version bookkeeping`

and `GET /api/v1/notifications` carries an `actionrequired` entry titled *Recorded schema version is
ahead of this build*:

> This database's recorded schema version (data v11, app v6) is ahead of what this build expects —
> usually because a set of not-yet-released migrations were consolidated after this database already
> applied them individually (issue #289). The schema itself is complete and the app is working
> normally; running a database Reset (POST /api/v1/admin/database/reset) will true up the version
> bookkeeping.

**The version numbers above are observed output, not assertions.** They are recorded because an operator
reading a real log will see numbers there and should recognise the shape; no step compares against them,
and they move whenever a migration is added.

**The recovery route is reachable and it works.** Unlike
[`05`](05-degraded-pages-survive-a-migration-failure.md), where a Reset cannot fix an unwritable mount,
here the remedy the notification names is genuinely available: the database is healthy and writable, so
`POST /api/v1/admin/database/reset` both runs and resolves the stale counter. The notification also
clears itself once it does, via its `DatabaseReset` dismiss trigger.

## Cleanup

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-06 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
