# A what's-new row written before the release state existed is backfilled, not re-announced

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #312

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-06`, on a bind-mounted directory rather
than the profile's named volume, so the file can be edited from the host while the app is stopped),
run on three images in turn against the same database:

1. the published `ghcr.io/dutchjafo/quotinator:1.8.3`, which creates the released baseline;
2. `quotinator:data-v8`, built from `54bab6f7^` — the last commit whose Data schema stops before the
   what's-new backfill (migration 10) — which migrates that baseline to v8;
3. the current build, which replays everything from 9 onwards over it.

The Constrained defect is **a state, not a flag**: what's-new rows in the pre-backfill shape, sitting in
a database whose schema has not yet reached the backfill.

`WhatsNewMetadataDto.ReleaseState` is a required property, so a row written by an earlier build cannot
be deserialized, cannot be identified, and would re-announce itself. A migration backfills it from the
convention that wrote those rows: a `version` key present meant a tagged release, absent meant the
unreleased section.

**Only databases carrying rows from an unreleased build are affected, so the state has to be
constructed** — a current database will never contain one naturally. The v8 image supplies one row of
that shape on its own; step 2 adds three more whose outcomes differ.

## Determinism

**The database reaches the backfill by being upgraded to a schema that stops short of it — never by
rolling the schema counter back.** Until 2026-09-22 this document deleted `System_SchemaVersion` rows
from a fully migrated database so the backfill would replay. That only works while every migration
after the backfill can run a second time, and it stopped working once migrations 12, 16 and 19 — plain
`ALTER TABLE … ADD COLUMN`, which SQLite cannot make idempotent — landed after it. Measured as written:
two deletes replayed `version 20 → 22` and the injected row came back unchanged, reading exactly like a
broken backfill; enough deletes to reach 10 would replay those `ADD COLUMN`s, fail, restore the backup
and leave the app at `503`. An intermediate image has neither problem: nothing replays twice, and every
later migration runs once, in order, as it would on a real upgrade.

**`54bab6f7^` is pinned because the schema it stops at is history, not a moving target.** Migrations
are append-only, so the commit before the backfill was added stays the commit before the backfill.
Only consolidation could move it — the tell is step 1's `applying … (version 3 → N)` line showing an
`N` other than one below the backfill's own version.

**Three injected rows, because each backfill outcome needs its own evidence:**

| Row | Stored before | Must read after | What it proves |
|---|---|---|---|
| *Injected: tagged release* | `{"version":"1.8.4"}` | `releaseState` `Released` | a `version` key means a tagged release |
| *Injected: unreleased* | `{"highlights":1}` | `releaseState` `Unreleased` | its absence means the unreleased section |
| *Injected: states its own* | `{"version":"1.8.5","releaseState":"Unreleased"}` | unchanged | the backfill adds a missing key and never overwrites one |

**The third row is the one that distinguishes, and it has to contradict the convention to do so.** A
row with no `version` and `Unreleased` — what this document used before — reads the same whether the
backfill added the key or overwrote it, so it proved nothing about `json_insert`. With a `version`
present, an overwrite would turn it into `Released`.

**The injected rows' `Metadata` is a JSON literal, so their SQL goes through a file.** Windows
PowerShell 5.1 strips double quotes out of an argument on its way to a native process — a here-string
included — so passing the statement inline would store `{version:1.8.4}`. See the index's *Every
command is PowerShell*.

- The container is **stopped** before the injection; writing to the database file underneath a
  running process is a different scenario.
- **Read the log before every stop**, per the index's *Read the log before the application stops*.
- **Do not assert which version the current build replays from beyond the one below the backfill** —
  the upper end moves with every migration.

## Steps

### 1. Build the v8 image, and migrate a released database to it

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-06-data"
$wt      = "$PWD\.claude\temp\wt-data-v8"
# A folder left by an earlier run still holds its database, and the released image would start against it.
if (Test-Path $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

git worktree add --detach $wt 54bab6f7^
docker build -f "$wt\docker\Dockerfile" -t quotinator:data-v8 $wt

function Count-Thrown { @(docker logs qt-notif-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count }

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-06 --port 18506 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
"1.8.3: thrown=$(Count-Thrown)"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-06 --port 18506 `
  --image quotinator:data-v8 --bind $dataDir
docker logs qt-notif-06 2>&1 | Select-String -Pattern 'applying \d+ pending "Data"'
"data-v8: thrown=$(Count-Thrown)"
docker stop -t 15 qt-notif-06
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT MAX(Version) AS DataVersion FROM System_SchemaVersion"
```

**Expected:** both starts report healthy — `reenter`'s own readiness poll establishes it — and both
reads `thrown=0`. The v8 build logs `applying 5 pending "Data" migration(s) (version 3 → 8)`, and
`DataVersion` is `8`.

**On failure:** a `DataVersion` other than one below the backfill's own version means the pinned commit
no longer stops where this scenario needs it (see Determinism). Stop — the upgrade below would then be
testing something other than the backfill.

### 2. Inject the three pre-backfill rows

```powershell
$fixture = "$PWD\.claude\temp\qt-notif-06.sql"
$sql = @'
INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDismissed, IsDeleted, Title, Metadata, MetadataKind) VALUES
  (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0, 'Injected: tagged release',  '{"version":"1.8.4"}', 'WhatsNew'),
  (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0, 'Injected: unreleased',     '{"highlights":1}', 'WhatsNew'),
  (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0, 'Injected: states its own', '{"version":"1.8.5","releaseState":"Unreleased"}', 'WhatsNew');
'@
[IO.File]::WriteAllText($fixture, $sql, [Text.UTF8Encoding]::new($false))
dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" --sql-file $fixture

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Title, Metadata FROM System_Notification WHERE MetadataKind = 'WhatsNew' ORDER BY Title"
```

**Expected:** `OK — 3 row(s) affected.`, and the three rows read back exactly as the table in
Determinism states them — **with their double quotes**. A fourth row, the v8 build's own
`What's new (unreleased)`, carries `{"version":null,…}` and no `releaseState`: the pre-backfill shape,
written naturally.

**The read-back is not decoration.** If the JSON arrived as `{version:1.8.4}`, the quotes were stripped
on the way to the process and the backfill would be tested against a shape it will never see.

### 3. Upgrade to the current build and read the rows

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-06 --port 18506 `
  --image quotinator:local --bind $dataDir
(Invoke-RestMethod "http://localhost:18506/api/v1/health").status
docker logs qt-notif-06 2>&1 |
  Select-String -Pattern 'applying \d+ pending "Data" migration\(s\) \(version \d+ . \d+\)' | Select-Object -Last 1

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Title, Metadata FROM System_Notification WHERE MetadataKind = 'WhatsNew' ORDER BY Title"
```

**Expected:** `healthy`, and an `applying` line starting at `version 8`. Then:

- *Injected: tagged release* reads `{"version":"1.8.4","releaseState":"Released"}`.
- *Injected: unreleased* reads `{"highlights":1,"releaseState":"Unreleased"}`.
- *Injected: states its own* is **unchanged** — still `Unreleased` although it carries a `version`.
- The v8 build's own row gains `"releaseState":"Unreleased"` — a JSON `null` version extracts as SQL
  `NULL`, so it counts as absent.

A second `What's new (unreleased)` row, written by the current build with `releaseState` already
present, is expected: its content hash differs from the v8 build's, because the unreleased changelog
section it summarises has changed since. That is new content, not a re-announcement.

**A `503 unhealthy` here is a failed replay, not a backfill defect** — the initializer restores its
pre-migration backup and the rows read exactly as they did in step 2. Check the health state before
reading them.

## Observed effect

**Captured 2026-09-22**, on this construction: the v8 build logged `version 3 → 8`, the current build
`applying 14 pending "Data" migration(s) (version 8 → 22)`, stayed healthy, and left:

```text
Injected: states its own  {"version":"1.8.5","releaseState":"Unreleased"}
Injected: tagged release  {"version":"1.8.4","releaseState":"Released"}
Injected: unreleased      {"highlights":1,"releaseState":"Unreleased"}
What's new (unreleased)   {"version":null,"contentHash":"2EE673F9","releaseState":"Unreleased"}
What's new (unreleased)   {"releaseState":"Unreleased","contentHash":"9042E675"}
```

The first line is the distinguishing observation: a row whose `version` would make it `Released` kept
the `Unreleased` it already stated.

## Cleanup

```powershell
"before the stop: thrown=$(Count-Thrown)"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-06 --bind $dataDir
Remove-Item -LiteralPath $dataDir -Recurse -Force
Remove-Item -LiteralPath $fixture -Force
git worktree remove $wt
docker image rm quotinator:data-v8
```

**Expected:** `thrown=0`. The data directory is a bind mount rather than a named volume, so removing the
directory is what removes its data. The worktree is detached, so removing it deletes no branch.
