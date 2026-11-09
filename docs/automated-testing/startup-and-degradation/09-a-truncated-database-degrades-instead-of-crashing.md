# A truncated database degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #482

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
[`backup/01`](../backup/01-a-reset-sets-aside-a-database-it-cannot-read.md) and
[`backup/02`](../backup/02-a-reset-sets-aside-a-truncated-database.md), for the same reason.

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

- **How the degraded pages render.** This document proves the application says it is degraded instead
  of crashing, which is #327's contract, and that the Reset its reason names recovers it (#482). The pages
  are recorded under *Observed effect*, and [`backup/02`](../backup/02-a-reset-sets-aside-a-truncated-database.md)
  provokes this same state to assert what the Reset does with the file.
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

### 5. Recover through the Reset the reason names

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18409/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18409/api/v1/health --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-startup-09\backups\unreadable "*.db" 1
```

**Expected:** the Reset answers `200`, `/health` returns to `200`, and the truncated file is in the
reserved folder `backups/unreadable/`.

**The remedy works from inside the application** (#482). A degraded state an operator can only leave by
stopping the container and deleting a file is not one a Home Assistant add-on user can necessarily
resolve, which is what #327 requires of every degraded state.

**On failure:** a `409` with obstacle `SourceUnreadable` is the refusal #482 removed.

### 6. Remove the sabotage, and confirm the container is healthy

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

**Measured 2026-11-08** against `quotinator:local` built from `870a0502`, truncated from 4,661,248 to
2,330,624 bytes: the process stays up, `/health` settles at `503 unhealthy` with the generic
initialisation-failure reason, `/openapi/v1.json` returns `200`, and all seven database reads answer
`503`. The truncation is caught before the integrity check could run, so its reason is not the one #305
added.

The rest of this section is from the 2026-10-31 run against `00a9dfe2`, which read further than the steps
do: `/`, `/stats` and `/notifications` each return `200`, and the home page carries the degraded text with
no stack trace. `GET /api/v1/version` answered with `"status":"degraded"` and no `database` block.

**The API surface is indistinguishable from `08`'s**, which is why each document pins its own variable
rather than one standing in for both: the faults differ at the SQLite boundary, and only the recipe
proves which one was provoked.

**The six admin endpoints [`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md)
lists answer an unhandled `500` here too**, read in this run. That document holds the finding, so the
same paragraph does not drift apart in both.

**Recovery route.** Asserted by step 5 since #482, and not yet measured against a build carrying it.
Deleting the file and restarting also returns the container to `200`, which is step 6's positive control.

## Canary

**A pre-issue build.** The commit before #327's first commit, under a distinct tag, run through every
step. That build predates the gating of `/version` while degraded, so step 4 reads `200` from
`/api/v1/version`.

**#482's step.** `quotinator:canary-482`, built from `69800a23`, the commit before #482, run through steps 1
to 5: step 5's Reset must answer `409` with obstacle `SourceUnreadable`. **Run 2026-11-09:** steps 1 to 4
passed, and step 5 read `409`, `/health` stayed `503`, and no `backups/unreadable/` existed.

**The positive control this run produces for itself.** Step 6 removes the truncation and reads `200` in
the same environment, so a build that degraded unconditionally fails there. Step 3's `unhealthy` is the
other half: it ends the run when the truncation did not take, so step 4 cannot pass against a healthy
container.

**Where the canary has been run, 2026-10-31.** Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 3 read `unhealthy` with the generic reason, and step 4 read `200` from `/api/v1/version`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind C:\repos\Quotinator\.claude\temp\qt-startup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-09 --delete C:\repos\Quotinator\.claude\temp\qt-startup-09-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
