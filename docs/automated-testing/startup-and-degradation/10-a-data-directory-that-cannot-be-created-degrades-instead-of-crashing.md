# A data directory that cannot be created degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #326

## Preconditions

**Beyond the profile.** `/data` is mounted read-only (`--read-only-data`) and the data directory is
configured one level beneath it (`Quotinator__DataDir=/data/quotinator`), so the directory does not exist
and cannot be created. This is the earliest degraded path the application has: `Directory.CreateDirectory`
fails at configuration time, before Kestrel binds, and so does the `keys/` directory inside it. Nothing
has ever been written, so there is no database to open either.

The container binds its data directory to a host path (`--bind`), so the host can confirm nothing was
written into it.

**The state this establishes.** The configuration-time guard records the failure, Kestrel binds, and the
initializer's own attempt to open the database then fails too. `/health` answers `503` with the
data-directory reason.

## Determinism

- **The parent mount is read-only, rather than a file placed where the directory belongs.** Both make
  `CreateDirectory` throw, and `StartupResilienceTests` uses the file, which is deterministic on every
  platform. This document uses the real-world cause instead, the one an operator actually meets.
- **`create` with `--read-only-data` gives the container its own key ring**, since a brand-new volume
  that cannot be written has none to share (`test-env.csx`'s note on `--read-only-data`). So no `keys/`
  directory exists or can be created, which is what the pages recorded under *Observed effect* fail on.
- **The data directory is configured with `--unset` and then `--env`**, so the profile's own
  `Quotinator__DataDir=/data` is removed rather than relying on the later `-e` winning.
- **Wait for `unhealthy`, not for a `503`.** The startup wait page answers `503` with
  `status=starting` until initialisation finishes, so a wait on the status code can return before the
  degraded state is settled. Step 1 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established for the same reason.
  Initialisation fails within a second here, since there is nothing to seed, but a wait that is right
  only because it happens to be fast is a guess.
- **No WAL sidecar state to pin.** No database file exists, so there is nothing for SQLite to replay.
## Explicitly not covered here

- **How the degraded pages render, and what the recovery routes offer.** This document proves the
  application says it is degraded instead of crashing, which is #327's contract. Both are recorded under
  *Observed effect*, and `backup/04` provokes an unwritable backup folder to assert the Reset path.
- **A file where the directory belongs.** That sabotage is
  `StartupResilienceTests.Startup_DataDirectoryCannotBeCreated_*`, in-process, where it is
  deterministic across platforms.
## Steps

### 1. Start with the data directory unobtainable, and confirm it degraded rather than died

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-10 --delete C:\repos\Quotinator\.claude\temp\qt-startup-10-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-10
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-10 --port 18410 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-10 --read-only-data --unset Quotinator__DataDir --env Quotinator__DataDir=/data/quotinator --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18410/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-10-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-10-out\health.json --equals status=unhealthy --not-empty reason
docker inspect -f '{{.State.Running}}' qt-startup-10
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-empty-directory C:\repos\Quotinator\.claude\temp\qt-startup-10
```

**Expected:** the `json` command exits `0`, printing `status = unhealthy` and the reason;
`docker inspect` prints `true`; and the `file` command exits `0`, the bind directory empty.

**`true` and `unhealthy` with a reason are the point of this document.** `true` is the never-crash
contract, for a failure that happened before Kestrel bound. `unhealthy` with a reason is the application
saying it is degraded. The empty bind directory confirms the mount really refused: a directory
that was created would mean the sabotage did not land.

**On failure:** a container that is not running means the configuration-time failure killed the process,
which is the defect this document exists to catch. `status=healthy`, or files in the bind directory, means
`/data` was writable, so every step below would assert against a healthy container: stop. `starting` or
`down` means the wait gave up after 180 seconds.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18410/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**The spec is how the way out is discovered**, so a degraded container that stopped serving it would
leave the operator with no documented next step. The seven endpoints read the database, which does not
exist, so each refuses rather than answering from nothing.

**On failure:** a `200` with a body from any of the seven means an endpoint answered without a database,
and whatever it returned is not a result. A `500` means it reached the database and threw rather than
being gated.

### 3. Remove the sabotage, and confirm the container is healthy

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-10 --port 18410 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-10 --unset Quotinator__DataDir --env Quotinator__DataDir=/data/quotinator
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18410/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18410/api/v1/version --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-10\quotinator\quotinatordata.db
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. The container reaches healthy, `/health` answers `200`, and the `file` command exits `0`: the database was created.

**This is the positive control.** Every assertion above is about a degraded container, so without a
passing case this document would stay green against a build that degraded on everything. The same
configuration on a writable mount creates the directory and the database.

**On failure:** a `503` here means the degradation was not specific to the read-only mount, which makes
every assertion above worthless.
## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

The process stays up, `/health` answers `503` with the data-directory reason, and the host directory stays
empty. `/openapi/v1.json` answers `200` and all seven database-reading endpoints answer `503`. By the end
of the reads below, the log held 12 `IOException` and 27 `SqliteException` throws.

**All three pages answer `500`, recorded rather than asserted**, while `/health` correctly reports the
fault, so the application still says it is degraded. That is
[#454](https://github.com/DutchJaFO/Quotinator/issues/454)'s, not this document's contract. The same holds
for an uncreatable `keys/` alone, which
[`11`](11-a-keys-directory-that-cannot-be-created-degrades-instead-of-crashing.md) covers.

**Recovery route, recorded rather than asserted.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and refuses with `409`, obstacle `DestinationDirectoryNotWritable`, naming *restore write
access* first. **Not workable**: a Reset writes. The refusal's second remedy, *Retry with
`allowNoBackup=true`*, answers an unhandled `500` here; this document does not assert it, so as not to
write the `500` in as the contract. Remounting `/data` writable and restarting returns the container to
`200`, which is step 3's positive control.

## Canary

A build from the commit before [#326](https://github.com/DutchJaFO/Quotinator/issues/326)'s first code
change, under a distinct tag, run through step 1. #326 is what turned this failure into a degraded state,
so on that build the configuration-time `CreateDirectory` throws before Kestrel binds: step 1's
`docker inspect` reads `false` and nothing answers on the port.

Step 3 is the positive control inside every run: it removes the sabotage and reads `200`, so a build
that degraded unconditionally fails there.

**Where the canary has been run, 2026-10-31.** Against `c8322590`, the commit before #326, as `quotinator:canary-326`: nothing answered on the port, and `docker inspect` read `false` with exit code `139`. Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 1 read `unhealthy` with the data-directory reason, and step 2 read `200` from `/api/v1/version`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-10 --bind C:\repos\Quotinator\.claude\temp\qt-startup-10
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-10 --delete C:\repos\Quotinator\.claude\temp\qt-startup-10-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run starts from
nothing.
