# A what's-new row written before the release state existed is backfilled, not re-announced

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #312

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-06`, on a bind-mounted directory rather
than the profile's named volume, so the file can be edited from the host while the app is
stopped). The Constrained defect is **a state, not a flag**: a what's-new row injected in the
pre-backfill shape, and the schema counter rolled back one step so the backfill replays over it.

**The database must have taken the incremental path, which is why this is Upgraded rather than
Fresh.** `ApplyMigrationPhaseAsync` records one `System_SchemaVersion` row per migration it applies, so
an upgraded database holds a row per version and deleting `MAX(Version)` rolls back exactly one. A
**fresh** database holds a single collapsed baseline row instead — deleting its maximum empties the
table, taking the recorded version to `0`.

**That difference is not cosmetic: it makes the whole scenario unrunnable.** Measured during #339's
full run against a Fresh container, the restart replayed from `0` and failed on the first statement —
`SQLite Error 1: 'there is already another table or index with this name: Audit_Entry'` — because
migration 1 creates a table the schema already has. The initializer then did exactly what the migration
policy requires: restored the pre-migration backup, left the database unchanged, and degraded to
`503 unhealthy`. The injected row was therefore untouched, which reads as "the backfill did not apply"
while the application was behaving correctly throughout. Running migration 10's SQL by hand against the
same file produced the documented result, which is what isolated the technique rather than the code.

`WhatsNewMetadataDto.ReleaseState` is a required property, so a row written by an earlier build cannot
be deserialized, cannot be identified, and would re-announce itself. A migration backfills it from the
convention that wrote those rows: a `version` key present meant a tagged release, absent meant the
unreleased section.

**Only databases carrying rows from an unreleased build are affected, so the state has to be
constructed** — a current database will never contain one naturally. That is why the defect is
constructed rather than provoked with a mount flag.

## Determinism

**This works only while the backfill is the newest Data migration.** Rolling back `MAX(Version)`
replays whichever migration is newest — once a later one lands, that is what replays and this scenario
proves nothing.

**The tell is the injected row's `Metadata` coming back unchanged.** If that happens, roll back far
enough to reach the backfill rather than pasting its version number in here — a hardcoded number would
go stale on its own and get "fixed" by editing a digit.

**But read the restart's log before concluding that.** An unchanged row is produced identically by
*the backfill ran and did not match* and by *the replay failed and the backup was restored*, and the
second leaves the app at `503 unhealthy` rather than serving. Step 3 asserts the health state for
exactly this reason.

**A restarted container's log still contains the previous boot's banner.** Waiting for a
`Quotinator ready` line in `docker logs` therefore returns immediately, matching the *first* boot —
which is how a degraded restart was first read as a successful one during #339's full run. Poll
`/api/v1/health` instead of waiting for a line that is already there.

**The injected row's `Metadata` is a JSON literal, so its SQL goes through a file.** Windows PowerShell
5.1 strips double quotes out of an argument on its way to a native process — a here-string included —
so passing this statement inline would store `{version:1.8.4}` and the backfill would then be tested
against data that is not the shape it exists for. See the index's *Every command is PowerShell*.

- `DELETE ... WHERE Version = (SELECT MAX(Version) ...)` is deliberately relative, so it stays correct
  after migrations are consolidated.
- The container is **stopped** before the injection and started afterwards; writing to the database file
  underneath a running process is a different scenario.
- **Do not assert which version replayed** — deleting `MAX(Version)` rolls back whichever migration is
  newest, so this stays correct after consolidation.

## Steps

### 1. Build a database that reached the current schema by migrating, not by baseline

Seed from the last published release, then let the current build replay its migrations over it. That
replay is what leaves one `System_SchemaVersion` row per version, which step 2's rollback needs:

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-06-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-06 --port 18506 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-06 --port 18506 `
  --image quotinator:local --bind $dataDir

docker stop -t 15 qt-notif-06
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS SchemaVersionRows FROM System_SchemaVersion"
```

**Expected:** the upgrade reports healthy — which `reenter`'s own readiness poll establishes before it
returns — and the counter holds **more than one row**, one per migration the replay applied.

**On failure:** a count of `1` means this database took the baseline path rather than the incremental
one, and step 2's rollback would then empty the table instead of undoing a single migration. The
restart would replay from `0` and fail on a table that already exists, leaving the app degraded and the
injected row untouched — which looks exactly like the backfill not working. Stop and build the database
by upgrading rather than from scratch.

**On failure, second case:** if the app never reports healthy, the database is not at the current
schema, and the rollback below would then be undoing something other than the backfill. Stop.

### 2. Inject a what's-new row in the pre-backfill shape, and roll back far enough to replay the backfill

```powershell
$fixture = "$PWD\.claude\temp\qt-notif-06.sql"
$sql = @'
INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDismissed, IsDeleted, Title, Metadata, MetadataKind)
VALUES (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0,
        'What''s new in v1.8.4', '{"version":"1.8.4"}', 'WhatsNew');
DELETE FROM System_SchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_SchemaVersion);
DELETE FROM System_SchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_SchemaVersion);
'@
[IO.File]::WriteAllText($fixture, $sql, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" --sql-file $fixture

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Title, Metadata FROM System_Notification WHERE MetadataKind = 'WhatsNew'"
```

**Expected:** no SQL error, and the injected row reads `{"version":"1.8.4"}` — **with its double
quotes**. The pre-backfill row is present and the schema counter has moved back far enough that the
what's-new backfill is among the migrations still to apply.

**The read-back is not decoration.** If the JSON arrived as `{version:1.8.4}`, the quotes were stripped
on the way to the process and the backfill would be tested against a shape it will never see — a
passing or failing result would say nothing either way. That is why the statement goes through
`--sql-file` and why this step reads the stored value rather than trusting the write.

**Two deletes, because the backfill is no longer the newest migration.** Determinism states the
condition; this is it having arrived. `BackfillWhatsNewReleaseState` is followed by
`BackfillCommonReleaseFields`, which handles the announcement and overshoot payloads and leaves a
what's-new row alone — so rolling back one replays only that later migration and the injected row comes
back unchanged. Measured during #339's full run: one delete replayed `version 10 → 11` and changed
nothing; two replayed `version 9 → 11` and produced the documented result.

**Delete one at a time until the backfill is in range, rather than hardcoding a version.** Two is
today's answer, not a constant — every migration added after the backfill adds one more. The relative
`MAX(Version)` form is what keeps this correct across consolidation; the *number of times* it is
repeated is what needs re-deriving, and step 3's `applying N pending "Data" migration(s) (version X →
Y)` line is what tells you whether the range now covers it.

**On failure:** a SQL error means the constructed state was never reached, so the restart below
replays nothing and any result it produces is meaningless. Stop.

### 3. Restart so the rolled-back migration replays over the injected row

```powershell
docker start qt-notif-06
dotnet script scripts/testing/http.csx -- --url "http://localhost:18506/api/v1/health" --wait-for 200 --status

(Invoke-RestMethod "http://localhost:18506/api/v1/health").status
docker logs qt-notif-06 2>&1 | Select-String -Pattern 'applying \d+ pending "Data" migration\(s\) \(version \d+ . \d+\)' | Select-Object -Last 1

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Title, Metadata FROM System_Notification WHERE MetadataKind = 'WhatsNew' ORDER BY Title"
```

**Expected:** `/health` returns `200` and `healthy`, and the last `applying` line shows a range
that **includes the what's-new backfill** — `version 9 → 11` when measured, against `version 10 → 11`
for a rollback that stopped one short.

**Poll `/health`, not the log.** A restarted container's log still holds the previous boot's
`Quotinator ready` banner, so a match on that line returns immediately and a degraded restart reads as a
successful one — which is how this was first misread during #339's full run. `--wait-for 200` polls the
live state, and gives up rather than hanging if it never arrives.

**A `503 unhealthy` here is the rollback having gone too far**, not a backfill defect: replaying from a
version the schema has already passed fails on the first statement that recreates an existing object,
and the initializer then restores its pre-migration backup and leaves the database untouched. The
injected row comes back unchanged, looking exactly like the backfill not working. Check the health
state before reading the row.

**Expected, once healthy:**

- Logs `applying … pending "Data" migration(s)` and the container serves.
- The injected row's `Metadata` becomes `{"version":"1.8.4","releaseState":"Released"}` — a `version`
  key present meant a tagged release under the convention that wrote those rows.
- A row that already states its own release state is **unchanged**. `json_insert` only adds a key that
  is missing, so replaying the chain cannot rewrite correct data. Both rows are listed by the same
  query for exactly this reason.

## Observed effect

**Captured 2026-08-25.** Against an upgraded database rolled back two migrations, the restart logged
`applying 2 pending "Data" migration(s) (version 9 → 11)`, stayed healthy, and left:

```text
What's new (unreleased)   {"releaseState":"Unreleased","contentHash":"2EE673F9"}
What's new in v1.8.4      {"version":"1.8.4","releaseState":"Released"}
```

The injected row gained `releaseState: Released` from its `version` key, and the row that already
stated its own release state came back byte-identical. That second line is the distinguishing
observation — it proves the backfill adds rather than overwrites, and it is easy to omit because a test
that only checks the injected row would pass either way.

**The failure mode is worth knowing, because it imitates a broken backfill exactly.** Rolled back too
far — which is what happens on a Fresh database, where deleting the single baseline row sets the
recorded version to `0` — the replay fails on `SQLite Error 1: 'there is already another table or
index with this name: Audit_Entry'`, the initializer restores its pre-migration backup, the database is
left untouched and the app degrades to `503`. The injected row then reads exactly as it would if the
backfill had run and done nothing. Only the health state and the `applying` range tell the two apart,
which is why step 3 asserts both.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-06 --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $fixture -ErrorAction SilentlyContinue
```

The data directory is a bind mount rather than a named volume, so removing the directory is what
removes its data.
