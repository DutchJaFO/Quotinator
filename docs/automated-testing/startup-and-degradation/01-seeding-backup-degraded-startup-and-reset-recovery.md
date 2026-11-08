# Only a risky start takes a backup, a broken schema degrades rather than crashes, and Reset recovers

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #254, #327, #305

## Preconditions

**Beyond the profile.** The data directory is a **bind mount** instead of the profile's named volume,
so the host can manipulate the SQLite file directly: the whole test turns on breaking the schema from
outside the container. It runs its own container (`qt-startup-01`, stopped and started around a host
edit), and the Constrained defect is a `DROP TABLE Quotinator_Quote` applied by the host while the
container is stopped.

`Quotinator.Tools.DbInspector` cannot be used here: it opens read-only (`Mode=ReadOnly`, see its
`README.md`) and cannot run the `DROP TABLE` this needs. `scripts/testing/execute-sql.csx` is the writable
counterpart and opens a normal connection.

Three separate startup states are exercised in sequence, and each depends on the previous one having
reached its own end state:

1. a fresh baseline seed (no backup expected)
2. an ordinary restart (backup expected)
3. a deliberately broken schema (degraded before anything is loaded, so no backup)

**This document asserted the opposite of
[`02-startup-backup-gating-and-storage-budget.md`](02-startup-backup-gating-and-storage-budget.md)
until 2026-08-23**: that an ordinary restart *does* take a backup, and that this was a deliberate
tradeoff rather than a defect. It was neither a contradiction nor a difference of setup: #277 gated
backups on each action's own real-work signal, and this document went on describing the behaviour from
before that. Its own justification named the missing gate that #277 supplied.

## Determinism

- **The bind path is an absolute Windows path, written out in full.** One directory, on one filesystem,
  with nothing translating it on the way to `docker`. The POSIX-style path this document used before
  depended on Git Bash rewriting it, and when that rewriting misfired the container silently bound
  `\Program Files\Git\data` and wrote nothing to the intended directory at all: after which the test
  reads an empty directory and reports nonsense.
- **The third start waits for *listening*, not for healthy.** That container is degraded by design and
  `/health` returns 503, so polling for a 200 would spend the whole timeout before failing for the
  wrong reason. The first two waits poll for healthy, because they are not degraded.
- **The container must be stopped before the host writes to the database file**, and started again
  afterwards. Editing a SQLite file underneath a running process is a different test with a different
  outcome.
- **The backup count is compared before and after**, not asserted as an absolute.
- **A backup protects a specific risky action, not a startup.** One is taken before a migration, so a
  partial failure still leaves a working database, and before a reseed, for the same reason. They exist
  so a user can recover, which is why Reset is what the app offers after a failed migration. A startup
  with no migration pending and nothing to seed puts nothing at risk, so it takes none.

## Steps

### 1. Seed a fresh container against a bind-mounted data directory

A folder left by an earlier run holds a database, and this step's baseline and backup checks need none,
so it is deleted first.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-01-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-01-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-01-data
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-01 --port 18401 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-01-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-01-data\quotinatordata.db --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-01-data\backups
```

**Expected:** the `file` command exits `0`: the database was created in the bind directory, and the
`backups` directory does not exist yet. A fresh database has nothing to lose, so no backup is taken.

**On failure:** no database in the bind directory means the bind mount did not take effect. Stop: every
step below reads and writes that directory, and against the wrong one they report nonsense.

### 2. Restart unchanged: nothing is at risk, so nothing is backed up

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-startup-01
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\after-restart.mark
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-01-data\backups
```

**Expected:** every command exits `0`: no exception thrown before the restart, the restarted app healthy,
and still no `backups` directory. The zero is believed because step 3 shows this same container's log
recording exceptions when they are thrown. The mark records where the log stood once the restarted app
was healthy, so step 3 counts only what came after the restart's own documented shutdown lines (index:
*Read the log before the application stops*). No migration is pending and the content
already exists, so neither risky action runs and there is nothing to protect against.

### 3. Break the schema on the host side, then restart

The container stays the one step 1 created: it is already bound to this directory, so nothing needs
re-running. It is stopped only so the host can write to the database file safely:

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\before-stop.mark
docker stop qt-startup-01
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-01-data\quotinatordata.db --sql "PRAGMA foreign_keys=OFF; DROP TABLE Quotinator_Quote;"
docker start qt-startup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18401/api/v1/health --field status --in unhealthy --timeout 120 --out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health-degraded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health-degraded.json --equals status=unhealthy --contains reason=Quotinator_Quote
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\before-stop.mark --pattern "thrown: SocketException" --exactly 2
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\before-stop.mark --pattern "thrown: (?!SocketException)" --exactly 0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\degraded.mark
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-01-data\backups
docker inspect -f '{{.State.Running}}' qt-startup-01
```

**Expected:** every TestProbe command exits `0`, and `docker inspect` prints `true`. `/health` reads
`unhealthy` with a reason naming `Quotinator_Quote`; nothing is logged as thrown but the two
`SocketException`s from the stop; and there is still no `backups` directory.

**The broken schema is found before anything touches it** (#305). The integrity check runs after the
migrations and before the content load, finds the table missing, and degrades: no query reaches the
missing table, so nothing throws, and nothing is loaded, so there is nothing for a backup to protect.
Before #305 the same drop let seeding run into the missing table, which threw, backed up and restored.
That backstop still exists for what the check cannot foresee, and is proven where such a failure can be
produced: `DatabaseInitializerTests.InitialiseAsync_SeedingFailsOnAlreadyMigratedDatabase_BacksUpFirstAndRethrows`
(`Quotinator.Core.Tests`) for the backup and the failure, and
`DatabaseInitializerSeedRestoreTests.InitialiseAsync_SeedingFailsAfterWriting_RestoresThePreSeedDatabase`
(`Quotinator.Data.Tests`, with `InitialiseAsync_SeedingSucceeds_KeepsWhatItWrote` as its positive
control) for the restore.

**Nothing here reads a log line's wording.** The failure is shown by `/health` and the exception
channel's count, the absent backup by the directory.

Exactly one backup: the first this test has produced, because step 2 correctly took none. This is the
case a backup exists for: seeding was about to run against a database it could not repair, and the
backup is what let it restore rather than leave a broken one behind. One file per `CreateBackup` call;
its `-shm`/`-wal` sidecars are not separate backups.

`docker inspect` printing `true`: the app degrades, it does not crash.

**Every exception this step logs is one it expects, counted since the mark taken before the stop.** No
exception since the restart, before the stop. Then three kinds, and only these:

- **`SqliteException`, at least one.** The degraded start's own fault: thrown, then handled by the
  restore, which is this step's subject, not noise. Its count is not predicted, only its presence.
- **`SocketException`, exactly two.** The stop's documented cost, one per listening port (index: *Read
  the log before the application stops*; Knowledgebase:
  `transport-connection-cancelled-during-a-request`).
- **Any other type, zero.** An exception this document does not expect is an issue, and counting it
  here is what makes it visible rather than absorbed into a total.

The last mark records where the log stood once the degraded state settled, so the cleanup can prove
nothing after it threw.

**Wait for `unhealthy`, not for a `503`.** A starting app answers `503` too: the startup wait page
(#280) reports `status=starting` until initialisation finishes, so a wait on the status code returns at
once and everything after it reads a half-started app. Measured 2026-09-22 with the old
`--wait-for 503`: health read `starting` with no reason, `/quotes/random` answered the wait page's
`200`, and the container had been up one second.

**On failure:** `false` from `docker inspect` means the app crashed instead of degrading, which is the
defect this test exists to catch, and there is then no server left to answer the degraded-surface and
Reset steps below. Stop and record the exit rather than running them against nothing. A `wait-field`
failure naming `starting` or `(never read)` means the wait gave up after 120 seconds rather than hanging.

### 4. Confirm the degraded surface

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/health --expect 503 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health.json --equals status=unhealthy --contains "reason=database Reset"
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/quotes/random --expect 503 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\random-degraded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\random-degraded.json --equals status=unavailable --not-empty reason
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/version --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/ --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/stats --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/notifications --expect 200 --status
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18401/ --out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\home.png --script-file scripts/testing/page-scripts/degraded-home-text.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\home.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\home.json --equals degradedTextPresent=true --equals stackTracePresent=false
```

**Expected:** `/health` returns `503`, and its `json` command exits `0`: `status = unhealthy`, the
reason naming a database Reset. `/quotes/random` returns `503` and its `json` command exits `0`:
`status = unavailable` with its own `reason`, never a raw exception. The six other database reads return
`503` too. `/openapi/v1.json` returns `200`. `/`, `/stats` and `/notifications` return `200`, and the
home page's `json` command exits `0`: the degraded text present, no stack trace.

**This fault is the one where the reason's first remedy works.** The schema is broken but the file
opens, so a Reset can back it up and rebuild it, which step 6 does. The reason names it, and the
assertion is that it does.

**The pages and the spec are the other half of the contract.** The API refuses to answer from a broken
schema, while the pages render a degraded form that says what happened, and the spec stays reachable so
the operator can find the Reset. A `500` from any page is the degraded UI failing to render.

### 5. Call Reset with a missing key while still degraded

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18401/api/v1/admin/database/reset --no-key --expect 401 --status
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18401/api/v1/admin/database/reset --api-key wrong-key --expect 401 --status
```

**Expected:** `401` both times, not `503`: confirming the health gate exempts `/api/v1/admin/*` from
the 503 gate entirely, rather than blocking the route and only letting an authenticated call through.

Both the missing key and the wrong key are tried: a route that answered `503` to one and `401` to the
other would still be broken, and testing only one could not tell.

### 6. Reset the database while degraded

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18401/api/v1/admin/database/reset --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0
```

**Expected:** `200`, and the `json` command exits `0`: a row-count summary of **all zeros**. It performs its own independent schema
rebuild, unaffected by the degraded state, and no longer reimports bundled or user quote content
afterwards (#156).

### 7. Confirm the app recovers without a restart

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/health --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health-recovered.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\health-recovered.json --equals status=healthy
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/quotes/random --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-01-out\random-recovered.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-01-out\random-recovered.json --equals status=NoResults --equals items=[]
dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18401/api/v1/version --expect 200 --status
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. `/health` returns `200`, its `json` command exiting `0` on `healthy`, proving `DatabaseHealthState.MarkHealthy()` clears
the degraded state rather than requiring a process restart. `/quotes/random` returns `200` and its
`json` command exits `0`: `status = NoResults` and no items, not `503`, and not real quote data, because
the database is genuinely empty after a Reset.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `870a0502`.

The fresh seed created the database and no `backups` directory, and the restart left it that way, with no
exception logged before either. After `Quotinator_Quote` was dropped, the restart logged 2
`SocketException` throws since the mark taken before the stop, and no other type. There is no `backups`
directory, and the container is running.

Degraded, `/health` read `unhealthy` with the reason *"The database's schema does not match the one its
recorded version defines: missing table Quotinator_Quote."*, followed by both remedies and the integrity
endpoint. All seven database reads answered `503`, the spec
`200`, and `/`, `/stats` and `/notifications` `200` with the degraded text and no stack trace. The reset
answered `401` to a missing and to a wrong key, then `200` with every count `0`, after which `/health`
read `healthy` and `/quotes/random` `NoResults` with no items. No exception was logged after the
degraded start settled.

**The three compounding gaps this came from**, all found during #254's own T1 pass:

1. Migration-version tracking detected a pending migration only by comparing recorded counts. Rewriting
   an unreleased migration's content in place (same slot, same final count) left an already-migrated
   database reading as "up to date" while its on-disk schema no longer matched. Seeding then crashed
   with no exception safety net, unlike the migration phase which already had one.
2. That uncaught exception propagated out of `Main` before Kestrel ever bound. Under IIS Express/ANCM
   it rendered a raw stack-trace page to whoever was looking at the browser. An initial fix caught it
   and exited the process cleanly, which broke the *only* documented remedy, since a fully-exited
   process has no server left to receive a Reset request.
3. Reset while degraded genuinely repairs the schema, but `DatabaseHealthState` is in-memory and does
   not observe that on its own. A first pass left the app reporting unhealthy forever after a
   successful Reset.

## Canary

Build the commit before [#254](https://github.com/DutchJaFO/Quotinator/issues/254)'s first code
change, under a distinct tag, and run steps 3 to 7 against it.

Step 4 must fail to report `503` with a populated reason. Degrading instead of crashing is what #254
established, so on that build a broken schema takes the start down and there is no degraded surface to
read: record whether the container exits or answers something other than a reasoned `503`, because the
two are different failures.

Steps 6 and 7 are the recovery half and must fail for a reason of their own: with no degraded state to
clear, there is nothing for `MarkHealthy()` to recover from without a restart.

**Step 2 is not this document's canary and belongs to a sibling.** Whether a healthy restart backs up
is [#277](https://github.com/DutchJaFO/Quotinator/issues/277)'s gating, proven in
[`02-startup-backup-gating-and-storage-budget.md`](02-startup-backup-gating-and-storage-budget.md). An
earlier version of this document described the pre-#277 behaviour and argued it was deliberate, which
is why the two are named separately here.

Remove the container, image and worktree afterwards.

**Step 3 must drop the table the build under test has.** The commit before #254 predates the domain
prefixes, so its quote table is `Quotes`. Dropping `Quotinator_Quote` there breaks nothing, and the build
then reads `healthy` for a reason that has nothing to do with this test. Neither commit below has the
exception channel, so step 3's counts do not apply to them.

**Where the canary has been run, 2026-10-31.** Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 3 reached `unhealthy` with the generic reason, and step 4 read `200` from `/api/v1/version`. Against `210d6830`, the commit before #254, as `quotinator:canary-254`, dropping `Quotes`: the container exited with code `139` and never answered, so there was no degraded surface to read.

## Cleanup

Read the log before the stop, per the index's *Read the log before the application stops*. Every line
belongs to step 3's degraded start, counted once it reported `unhealthy`; none may come from the Reset
or the recovery after it:

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\degraded.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-01 --bind C:\repos\Quotinator\.claude\temp\qt-startup-01-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-01-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-01-out
```

**Expected:** the `log` command exits `0`: nothing has been logged as thrown since step 3's degraded
state settled, through the Reset and the recovery after it.

This test's data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
