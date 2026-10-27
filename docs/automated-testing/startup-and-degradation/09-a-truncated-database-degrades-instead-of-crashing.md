# A truncated database degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** A fresh container is seeded, stopped, and its quote database cut to half its
length: a genuine SQLite file with a valid header and missing pages, which is what an interrupted write
or a filled disk actually leaves behind. The changelog database and `keys/` are left alone.

The container binds its data directory to a host path (`--bind`) rather than using a named volume, so
the file can be truncated from the host.

**Why this is its own document rather than a second case inside
[`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md).** A truncated database
and a file that is not a database are different faults reaching SQLite through different code paths,
and each has a variable its own `Determinism` has to pin. Folding them together would let one pass on
the other's behalf. The same split exists one category over, between
[`backup/01`](../backup/01-refuses-a-reset-when-the-source-cannot-be-read.md) and
[`backup/02`](../backup/02-refuses-a-reset-when-the-database-is-truncated.md), for the same reason.

## Determinism

- **Half the file, not a fixed byte count.** The bundled dataset's size moves between releases, so a
  literal length would silently stop truncating anything once the database grew past it.
- **The WAL sidecars are deleted.** A surviving `-wal` can carry enough recent pages for SQLite to open
  the database anyway, which would make the outcome depend on how recently the seed checkpointed.
- **The container is stopped before the file is touched.** `destroy` is `docker rm -f`, a kill rather
  than a clean stop, so the sidecars can survive it, which is why the bullet above deletes them rather
  than relying on a checkpoint.
- **Wait for `unhealthy`, not for a `503`.** Degrading is the expected outcome, so waiting for `200`
  would spend the whole timeout, and the startup wait page answers `503` with `status=starting`, so
  waiting on the status code can return before the degraded state is settled. Step 3 polls the `status`
  field, the technique [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
## Explicitly not covered here

- **How the degraded pages render, and what the recovery routes offer.** This document proves the
  application says it is degraded instead of crashing, which is #327's contract. The pages and the Reset
  are recorded under *Observed effect*, and [`backup/02`](../backup/02-refuses-a-reset-when-the-database-is-truncated.md)
  provokes this same state to assert the Reset path.
## Steps

### 1. Seed a healthy database and record its size

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-09 --delete C:\repos\Quotinator\.claude\temp\qt-startup-09-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/health --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-09-out\health-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-09-out\health-seeded.json --equals status=healthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-09\quotinatordata.db
```

**Expected:** every command exits `0`: the application reports `healthy`, and `quotinatordata.db` is a
file. Nothing here asserts what the database holds: the fault below is the file being cut short, so its
content is not this document's subject, and a count would depend on the bundled sources.

**On failure:** a fresh seed that cannot reach healthy is not this document's subject. Stop and fix
that first.

### 2. Stop the container and cut the database in half

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --truncate-half C:\repos\Quotinator\.claude\temp\qt-startup-09\quotinatordata.db --delete C:\repos\Quotinator\.claude\temp\qt-startup-09\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-startup-09\quotinatordata.db-shm
```

**Expected:** the `file` command exits `0`, reporting the new length as half the old one, both
megabytes rather than bytes.

**On failure:** a file-in-use error means the container did not stop. Re-run `destroy` and retry rather
than forcing the write.

### 3. Start the current build, and confirm it degraded rather than died

```
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-09 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18409/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-09-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-09-out\health.json --equals status=unhealthy --not-empty reason
docker inspect -f '{{.State.Running}}' qt-startup-09
```

**Expected:** the `json` command exits `0`, printing `status = unhealthy` and the reason, and
`docker inspect` prints `true`.

**These three are the point of this document.** `true` is the never-crash contract. `unhealthy` with a
reason is the application saying it is degraded, rather than the operator discovering it from failures.

**On failure:** a container that is not running means the fault killed the process, which is the defect
this document exists to catch. `status=healthy` means the truncation did not take, so re-run from step 2.
`starting` or `down` means the wait gave up after 180 seconds.

### 4. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18409/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven.

**A truncated database is the sharper case for gating than `08`'s**: the header is valid and some pages
are present, so an endpoint that answered anyway could return partial real data, which is harder to
recognise as wrong than an empty result.

**On failure:** a `200` with a body from any of the seven means an endpoint answered from a database the
initializer could not read. A `500` means it reached the database and threw rather than being gated.

### 5. Remove the sabotage, and confirm the container is healthy

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-09\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18409/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/version --expect 200 --status
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. the container reaches healthy and `/health` returns `200`.

**This is the positive control.** Without a passing case this document would stay green against a build
that degraded on everything.

**On failure:** a `503` here means the degradation is not specific to the truncation, which makes every
assertion above worthless.
## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`, seeded to 795 quotes and
truncated from 4,730,880 to 2,365,440 bytes.

The process stays up and `/health` settles at `503 unhealthy`, its reason naming the file-deletion
remedy. `/`, `/stats` and `/notifications` each return `200`,
`/openapi/v1.json` returns `200`, the home page carries *started with a problem*, and no page matches
`at [A-Za-z.]+\(`. Every data-serving endpoint answers `503`.

**The API surface is indistinguishable from `08`'s**, which is why each document pins its own variable
rather than one standing in for both: the faults differ at the SQLite boundary, and only the recipe
proves which one was provoked.

**`GET /api/v1/version` answers `503` here too**, with `"status":"degraded"` and no `database` block.
It did not when these documents were written: it answered `200` with `"status":"ready"` and every
count at `0`, and
[`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md)'s Observed effect records
that finding once, with #419's rule and the startup-state control behind it.

**The six admin endpoints that answer an unhandled `500` in this state** are recorded there too, for
the same reason: one finding, one place, rather than the same paragraph in both documents drifting
apart.

**Recovery route, recorded rather than asserted.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and answers `409 Conflict`. **Not workable as offered**: the reset refuses, handing back a
remedy that points at the file. Deleting the database file and restarting returns the container to
`200`, which is step 5's positive control.

## Canary

**A pre-issue build.** The commit before #327's first commit, under a distinct tag, run through every
step. That build predates the gating of `/version` while degraded, so step 4 reads `200` from
`/api/v1/version`.

**The positive control this run produces for itself.** Step 5 removes the truncation and reads `200` in
the same environment, so a build that degraded unconditionally fails there. Step 3's `unhealthy` is the
other half: it ends the run when the truncation did not take, so step 4 cannot pass against a healthy
container.

**Where the canary has been run, 2026-10-24.** Against `e4078444`, the commit before #327, as `quotinator:canary-327`: steps 1 to 3 passed, step 4 read `200` from `/api/v1/version`, and step 5 read `healthy`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-09 --delete C:\repos\Quotinator\.claude\temp\qt-startup-09-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
