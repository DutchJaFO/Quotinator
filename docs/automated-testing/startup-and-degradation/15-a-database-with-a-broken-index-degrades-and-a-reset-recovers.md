# A database whose index disagrees with its table degrades, and a Reset recovers

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #490, #457

## Preconditions

**Beyond the profile.** The database is the one the Fresh profile's own startup creates. While the container
is stopped, a small table and an index on it are added, and then one entry of the index is rewritten in place,
so the index no longer agrees with its table. The container binds its data directory to a host path
(`--bind`) so both changes can be made from the host.

**The damage is built through the documented file format, never by relying on how SQLite reacts to a
misuse.** `TestProbe damage --rewrite-last-index-key` calls the same helper the database check's unit tests
use, `DocumentedFormatFixtures.RewriteLastIndexKey`: it locates the index's root page through the schema table
and rewrites one integer key in the documented record format. It refuses, and the step stops, when its own
precondition does not hold.

**An index that disagrees with its table is the case only a full check finds.** `PRAGMA quick_check`, which
the Pages layer runs, documents that it does not compare index content with table content; the Indexes
layer's `PRAGMA integrity_check` does. Before #490, startup opened the database and nothing noticed.

**The added table is an extra object**, which leaves the schema intact (#305), so the damaged index is the
database's only fault.

## Determinism

- **Every change is made while the container is stopped, and stopped cleanly.** `docker stop -t 15` leaves
  no `-wal` holding data beside the database, so the next start checks the file in place.
- **The ranks are 10 to 50**, each stored as an 8-bit integer, and the index fits on its root page. The
  `damage` command checks both and refuses otherwise.
- **The restart waits for *listening*, then for `unhealthy`**, as [`14`](14-a-schema-that-does-not-match-its-version-degrades-and-names-it.md)
  does: the startup wait page answers `503` too.
- **Layers are read by position.** The check reports them in a fixed order: storage, format, journal, pages,
  indexes, references, schema. Indexes is position 4, and the main database is always reported first.
- **The reason is asserted for the Indexes layer's cause only**, the words #457 gave it, not its remedies,
  which belong to [`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md); the layer itself
  is asserted from the integrity endpoint.

## Explicitly not covered here

- **Every other layer's failure** (storage, its copy among them, format, journal, pages, references): proven by
  `SqliteDatabaseCheckTests`, one test per layer, against files built by the same helper.
- **The same database with the startup check at `fast`**, which starts healthy: [`16`](16-with-a-fast-startup-check-a-broken-index-starts-healthy-and-is-still-reported.md).

## Steps

### 1. Create a healthy database, and confirm every layer passes

A folder left by an earlier run holds a database that is already damaged or already repaired, and step 1 must
start from none, so it is deleted first.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-15-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-15-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-15-data --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-15-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-15 --port 18415 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-15-data
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-fresh.json --equals databases.0.database=main --equals databases.0.isSound=true --equals databases.0.layers.4.layer=indexes --equals databases.0.layers.4.status=passed --equals databases.0.isIntact=true
```

**Expected:** the environment reports healthy, and the `json` command exits `0`: the main database is sound,
its Indexes layer passed, and it is intact.

**On failure:** a fresh database that is not sound means the check disagrees with a file this build itself
created, and every degraded reading below would be about that instead of the damage. Stop.

### 2. Stop the container cleanly, add an indexed table, and break the index

```
docker stop -t 15 qt-startup-15
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-15-data\quotinatordata.db --sql "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL); CREATE INDEX IX_CheckFixture_Rank ON CheckFixture (Rank); INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);"
dotnet run --project tools/Quotinator.Tools.TestProbe -- damage --rewrite-last-index-key C:\repos\Quotinator\.claude\temp\qt-startup-15-data\quotinatordata.db IX_CheckFixture_Rank 100
```

**Expected:** the stop echoes the container name, `execute-sql.csx` reports success, and the `damage` command
exits `0`, naming the index and the value 100.

**On failure:** a `damage` command that exits `1` names the precondition that did not hold, and nothing was
written. Step 3 would read an ordinary healthy start. Stop.

### 3. Restart, and confirm the application degrades instead of crashing

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-15 --port 18415 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-15-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18415/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-15-out\health-broken.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-15-out\health-broken.json --equals status=unhealthy --contains "reason=an index no longer agrees with its table"
docker inspect -f '{{.State.Running}}' qt-startup-15
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/openapi/v1.json --expect 200 --status
```

**Expected:** the `json` command exits `0`: `status = unhealthy` with the reason naming the broken index (#457). `docker
inspect` prints `true`, `/quotes/random` answers `503` and the spec answers `200`.

**On failure:** `status=healthy` is the defect #490 exists to remove: a database whose index disagrees with
its table served as sound. A container that is not running means the check killed the process. `starting` or
`down` means the wait gave up after 180 seconds.

### 4. Confirm the endpoint names the layer, and that the schema is still intact

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-broken.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-broken.json --equals databases.0.isSound=false --equals databases.0.layers.3.layer=pages --equals databases.0.layers.3.status=passed --equals databases.0.layers.4.layer=indexes --equals databases.0.layers.4.status=failed --equals databases.0.isIntact=true
```

**Expected:** the `json` command exits `0`: the main database is not sound, Pages passed, Indexes failed, and
it is still intact.

**Pages passing and Indexes failing is the evidence the damage is the documented kind:** `quick_check` does
not compare index content with table content, and `integrity_check` does. The schema matches, so `isIntact`
stays `true`: the two answer different questions.

**On failure:** Pages failing means the damage reached the page structure and the fixture is not what this
document claims. A `503` means the admin exemption from the health gate no longer covers the endpoint.

### 5. Reset, and confirm the remedy works and the file was set aside

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18415/api/v1/admin/database/reset --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-15-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-15-out\reset.json --equals backup.outcome=SetAside --present backup.name
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18415/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-after-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-15-out\integrity-after-reset.json --equals databases.0.isSound=true --equals databases.0.layers.4.status=passed
```

**Expected:** the Reset answers `200` with `backup.outcome = SetAside` and the set-aside file's name, then
`/health` and `/quotes/random` answer `200` and the rebuilt database is sound.

**`SetAside` is ADR 028's routing:** a file whose Indexes layer failed is not a sound copy, so it is moved
into the `unreadable` folder rather than kept as a backup. This step is also the positive control for steps 3
and 4: the same container, repaired by the remedy the reason names.

**On failure:** `backup.outcome = Succeeded` means a file that is not a sound copy was kept as a backup. A
`409` means the Reset refused for want of somewhere to move the file, a different fault documented in
`backup/`.

## Observed effect

**Awaiting measurement for #457.** The reason below is the one measured before #457 changed it; #457's T2
run records the new one here.

**Measured 2026-11-17** against `quotinator:local` built from `2fd67742`.

The fresh database was sound, its Indexes layer passed, and it was intact. After the index was damaged, the
restart read `unhealthy` with the reason *"Database initialisation failed. …"*; the container was running,
`/quotes/random` answered `503` and the spec `200`. The endpoint reported the main database not sound: Pages
`passed`, Indexes `failed`, and the database intact, with the added table reported as one `extra`. The Reset
answered `200` with `backup.outcome = SetAside` and a file named
`quotinatordata_unreadable_20261117T051849116Z.db`; `/health` and `/quotes/random` then answered `200`, and the
rebuilt database was sound.

**Recorded rather than asserted:** the set-aside file's name says `unreadable`, though this file was readable
and only not a sound copy: the name is #482's, from before a file could be set aside for any other reason.
## Canary

Build the commit before #490's first code change, `342e0cfa`, under a distinct tag, and run steps 1 to 3
against it.

Step 1's `json` command must fail on `databases.0.isSound`: the endpoint reports no layers there. Run steps 2
and 3 anyway, without step 1's `json` command, because step 3 is the reading that matters: with the index
broken, `/health` must read `healthy`, the gap #490 closes.

Remove the container, image and worktree afterwards.

**#457's step.** `quotinator:canary-457`, built from `a004340f`, the commit before #457's first commit, run
through steps 1 to 3: step 3's `json` command must fail, because that build gives the generic reason for a
broken index.

**Where the canary has been run, 2026-11-15.** Against `342e0cfa`, as `quotinator:canary-490`: step 1's `json`
command failed on `databases.0.isSound`, `layers.4.layer` and `layers.4.status`, each absent. With the index
damaged, step 3's `wait-field` read `{"status":"healthy"}` with no reason, `docker inspect` printed `true`, and
`/api/v1/quotes/random` answered `200`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-15 --bind C:\repos\Quotinator\.claude\temp\qt-startup-15-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-15-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-15-out
```

**Expected:** the container and both directories are removed.
