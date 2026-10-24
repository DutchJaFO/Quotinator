# A database that is not a database degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** A fresh container is seeded, stopped, and its quote database replaced with
bytes that are not a SQLite database at all. The changelog database and `keys/` are left alone: the
fault under test is the quote database, and damaging more would make it ambiguous which one the
degraded state is about.

The container binds its data directory to a host path (`--bind`) rather than using a named volume, so
the file can be replaced from the host.

**The state this establishes, measured 2026-10-21.** SQLite is handed a file it cannot recognise, so
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

- **How the degraded pages render, and what the recovery routes offer.** This document proves the
  application says it is degraded instead of crashing, which is #327's contract. The pages and the Reset
  are recorded under *Observed effect*, and [`backup/01`](../backup/01-refuses-a-reset-when-the-source-cannot-be-read.md)
  provokes this same state to assert the Reset path.
## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-08 --delete C:\repos\Quotinator\.claude\temp\qt-startup-08-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-08-out\version.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-08-out\version.json --greater-than database.quotes=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-08\quotinatordata.db
```

**Expected:** every command exits `0`: a quote count above `0`, and `quotinatordata.db` is a file.

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

### 5. Remove the sabotage, and confirm the container is healthy

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

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`, seeded to 795 quotes and then
replaced with 34 bytes of text.

The process stays up and `/health` settles at `503 unhealthy`, its reason naming the file-deletion
remedy. `/`, `/stats` and `/notifications` each return `200`,
`/openapi/v1.json` returns `200`, the home page carries *started with a problem*, and no page matches
`at [A-Za-z.]+\(`. Every database-reading endpoint is gated: `/api/v1/quotes`,
`/api/v1/quotes/random`, `/api/v1/quotes/search`, `/api/v1/conversations`, `/api/v1/notifications`,
`/api/v1/masterdata/sources` and `/api/v1/version` all answer `503`.

**`GET /api/v1/version` reached that list by being a defect this document found.** On the build this
document was first written against it answered `200` with `"status":"ready"` and a `database` block
reporting every count as `0`, so a caller could neither tell an empty database from an unopenable one
nor see that the application was not ready. [`03`](03-startup-wait-page.md) records the rule that
broke, from #419: the endpoint is gated *while starting* precisely because "a caller could otherwise
reach for it as a readiness endpoint in `/health`'s place", and during startup it does answer `503`.
The gate existed and the degraded state was not behind it. It now answers
`503 {"status":"degraded","version":...,"environment":...}` with no `database` block, and it still
answers `200` when healthy.

**Six admin endpoints answer an unhandled `500` in this state, and this document does not assert
them.** Measured 2026-10-21, each returning a bare *"An error occurred while processing your request"*
and a trace id: `GET /api/v1/admin/database/seed/preview`, `GET /api/v1/admin/audit`,
`GET /api/v1/admin/audit/date-range`, `GET /api/v1/admin/audit/export`,
`POST /api/v1/admin/database/reseed` and `DELETE /api/v1/admin/audit`. The admin surface is exempt
from the health gate so it can be the recovery route, which is why the reset reaching its handler matters
handler; these six reach theirs and throw, and two of them are writes.

Correct in the same run, which is what shows the fault is specific rather than the surface being
broken: `POST /api/v1/admin/backups/create` answers `409`, and `GET /api/v1/admin/backups` and
`/backups/status` answer `200` off the filesystem. `POST /api/v1/admin/sources/refresh` answers `200`,
having really fetched from GitHub, since it writes files and never reads the database.

That is the same defect class #348 removed from `POST /admin/database/reset`, which returned an
unhandled `500` for its own reason until it was taught to refuse with a stated obstacle, and the
endpoints carrying #348's preflight are exactly the ones answering correctly.
[#453](https://github.com/DutchJaFO/Quotinator/issues/453) owns the fix: narrowing the gate's exemption
to the reset and the backup category, which is the repair route an operator can actually use. ADR 022
governs it, since a condition the code has already detected is being thrown rather than returned.

**Recovery route, recorded rather than asserted.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and answers `409 Conflict` rather than being gated away by the `503`. **Not workable as
offered**: the reset refuses, naming the obstacle and handing back a remedy that points at the file.
Deleting the database file and restarting returns the container to `200`, which is step 5's positive control.

**The reason names a remedy that works, second.** `/health` carries the generic initialisation-failure
reason, which recommends a database Reset first, and that reset refuses. Its second remedy, *stopping
the app, deleting the database file, and restarting*, is the one that works. The reason's diagnosis does not fit this fault, though: it says the recorded schema version
probably does not match the on-disk schema, for a file that is not a database at all. Contrast
[`05`](05-degraded-pages-survive-a-migration-failure.md), where #326 gave the unwritable-directory fault
its own reason, matched on SQLite error codes 14 and 8; an unreadable file raises neither, so it falls
through to the generic text.

## Canary

**A pre-issue build.** The commit before #327's first commit, under a distinct tag, run through every
step. That build predates the gating of `/version` while degraded, so step 4 reads `200` from
`/api/v1/version`.

**The positive control this run produces for itself.** Step 5 removes the sabotage and reads `200` in the
same environment, so a build that degraded unconditionally fails there. Step 3's `unhealthy` is the other
half: it ends the run when the sabotage did not land, so step 4 cannot pass against a healthy container.

**Where the canary has been run, 2026-10-24.** Against `e4078444`, the commit before #327, as `quotinator:canary-327`: steps 1 to 3 passed, step 4 read `200` from `/api/v1/version`, and step 5 read `healthy`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind C:\repos\Quotinator\.claude\temp\qt-startup-08
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-08 --delete C:\repos\Quotinator\.claude\temp\qt-startup-08-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
