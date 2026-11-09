# A database that is not a database degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #482

## Preconditions

**Beyond the profile.** A fresh container is seeded, stopped, and its quote database replaced with
bytes that are not a SQLite database at all. The changelog database and `keys/` are left alone: the
fault under test is the quote database, and damaging more would make it ambiguous which one the
degraded state is about.

The container binds its data directory to a host path (`--bind`) rather than using a named volume, so
the file can be replaced from the host.

**The state this establishes.** SQLite is handed a file it cannot recognise, so
the initializer fails before any schema version is read. The application records the failure and
continues: Kestrel binds, `/health` answers `503`, and the Blazor pages render their degraded form.

## Determinism

- **The file is replaced, not truncated.** Truncation reaches SQLite through a different code path and
  is [`09`](09-a-truncated-database-degrades-instead-of-crashing.md)'s subject. Folding them together
  would let one pass on the other's behalf.
- **The WAL sidecars are deleted along with it.** A surviving `-wal` beside a garbage main file leaves
  SQLite with two disagreeing sources, and which one it reports first is timing rather than a property
  of this test.
- **The container is stopped before the file is touched.** Writing under a running container races its
  own connections, so the outcome would depend on when the write landed.
- **Wait for `unhealthy`, not for a `503`.** Degrading is the expected outcome, so waiting for `200`
  would spend the whole timeout, and the startup wait page answers `503` with `status=starting`, so
  waiting on the status code can return before the degraded state is settled. Step 3 polls the `status`
  field, the technique [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
## Explicitly not covered here

- **How the degraded pages render.** This document proves the application says it is degraded instead
  of crashing, which is #327's contract, and that the Reset its reason names recovers it (#482). The pages
  are recorded under *Observed effect*, and [`backup/01`](../backup/01-a-reset-sets-aside-a-database-it-cannot-read.md)
  provokes this same state to assert what the Reset does with the file.
## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-08 --delete C:\repos\Quotinator\.claude\temp\qt-startup-08-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/health --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-08-out\health-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-08-out\health-seeded.json --equals status=healthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db
```

**Expected:** every command exits `0`: the application reports `healthy`, and `quotinatordata.db` is a
file. Nothing here asserts what the database holds: the fault below is the file itself, so its content is
not this document's subject, and a count would depend on the bundled sources.

**On failure:** a fresh seed that cannot reach healthy is not this document's subject, and every
assertion below would be about the wrong fault. Stop and fix that first.

### 2. Stop the container and replace the database with non-database bytes

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --write-text C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db "this file is not a SQLite database" --delete C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db-shm --expect-length C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db 34
```

**Expected:** the `file` command exits `0`, reporting the file at 34 bytes.

**On failure:** a file still megabytes in size means the write did not land, and every step below would
assert against a healthy database. Stop rather than continuing.

### 3. Start the current build, and confirm it degraded rather than died

```
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-08 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18408/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-08-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-08-out\health.json --equals status=unhealthy --not-empty reason
docker inspect -f '{{.State.Running}}' qt-startup-08
```

**Expected:** the `json` command exits `0`, printing `status = unhealthy` and the reason, and
`docker inspect` prints `true`.

**These three are the point of this document.** `true` is the never-crash contract: the process is still
running after a failure that reached it before any schema version was read. `unhealthy` with a reason is
the application saying it is degraded, rather than the operator discovering it from failures.

**On failure:** a container that is not running means the fault killed the process, which is the defect
this document exists to catch. `status=healthy` means the file was not actually replaced, so re-run from
step 2 rather than letting the assertions below pass against a container that never degraded. `starting`
or `down` means the wait gave up after 180 seconds.

### 4. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18408/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven.

**The spec is the surface an operator recovers through**, so it has to stay reachable while degraded.
**The reads refuse rather than answer from a database the initializer could not open** (developer
direction, 2026-10-21): a `200` carrying zeroes cannot be told apart from an empty database.

**On failure:** a `200` with a body from any of the seven means an endpoint answered from a database the
initializer could not read. A `500` means it reached the database and threw rather than being gated.

### 5. Recover through the Reset the reason names

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18408/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/health --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-startup-08\backups\unreadable "*.db" 1
```

**Expected:** the Reset answers `200`, `/health` returns to `200`, and the replaced file is in the reserved
folder `backups/unreadable/`.

**The remedy works from inside the application** (#482). A degraded state an operator can only leave by
stopping the container and deleting a file is not one a Home Assistant add-on user can necessarily
resolve, which is what #327 requires of every degraded state.

**On failure:** a `409` with obstacle `SourceUnreadable` is the refusal #482 removed.

### 6. Remove the sabotage, and confirm the container is healthy

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18408/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/version --expect 200 --status
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. the container reaches healthy and `/health` returns `200`.

**This is the positive control.** Every assertion above is about a degraded container, so without a
passing case this document would stay green against a build that degraded on everything.

**On failure:** a `503` here means the degradation is not specific to the sabotage, which makes every
assertion above worthless.
## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`, the database replaced with 34
bytes of text.

The process stays up and `/health` settles at `503 unhealthy` with the generic initialisation-failure
reason. `/`, `/stats` and `/notifications` each return `200`, `/openapi/v1.json` returns `200`, and the
home page carries the degraded text with no stack trace. All seven database reads answer `503`.
`GET /api/v1/version` answers `503 {"status":"degraded","version":...,"environment":...}` with no
`database` block, and `200` when healthy.

**Six admin endpoints answer an unhandled `500` in this state, and this document does not assert
them:** `GET /api/v1/admin/database/seed/preview`, `GET /api/v1/admin/audit`,
`GET /api/v1/admin/audit/date-range`, `GET /api/v1/admin/audit/export`,
`POST /api/v1/admin/database/reseed` and `DELETE /api/v1/admin/audit`. The admin surface is exempt from
the health gate so it can be the recovery route; these six reach their handlers and throw, and two of
them are writes. `GET /api/v1/admin/backups` answers `200` in the same run, off the filesystem.
[#453](https://github.com/DutchJaFO/Quotinator/issues/453) owns the fix: narrowing the gate's exemption
to the reset and the backup category. ADR 022 governs it, since a condition the code has already
detected is being thrown rather than returned.

**Recovery route.** `POST /api/v1/admin/database/reset` reaches its handler rather than being gated away
by the `503`. Since #482 it rebuilds the database and sets the file aside: **measured 2026-11-09** against
`quotinator:local` built from the working tree at `271c4c54`, it answered `200`, `/health` returned to
`200`, and the 34-byte file was in `backups/unreadable/` as
`quotinatordata_unreadable_20261109T085231926Z.db`. Deleting the file and restarting also returns the container to
`200`, which is step 6's positive control.

**The reason's diagnosis does not fit this fault.** `/health` says the recorded schema version probably
does not match the on-disk schema, for a file that is not a database at all. Its first remedy, the
Reset, is now the one that works. Telling the two conditions apart is
[#457](https://github.com/DutchJaFO/Quotinator/issues/457).

## Canary

**A pre-issue build.** The commit before #327's first commit, under a distinct tag, run through every
step. That build predates the gating of `/version` while degraded, so step 4 reads `200` from
`/api/v1/version`.

**#482's step.** `quotinator:canary-482`, built from `69800a23`, the commit before #482, run through steps 1
to 5: step 5's Reset must answer `409` with obstacle `SourceUnreadable`. **Run 2026-11-09:** steps 1 to 4
passed, and step 5 read `409`, `/health` stayed `503`, and no `backups/unreadable/` existed.

**The positive control this run produces for itself.** Step 6 removes the sabotage and reads `200` in the
same environment, so a build that degraded unconditionally fails there. Step 3's `unhealthy` is the other
half: it ends the run when the sabotage did not land, so step 4 cannot pass against a healthy container.

**Where the canary has been run, 2026-10-31.** Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 3 read `unhealthy` with the generic reason, and step 4 read `200` from `/api/v1/version`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-08 --delete C:\repos\Quotinator\.claude\temp\qt-startup-08-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
