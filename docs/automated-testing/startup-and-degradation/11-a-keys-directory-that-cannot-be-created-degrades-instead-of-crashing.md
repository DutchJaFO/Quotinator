# A keys directory that cannot be created degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #326

## Preconditions

**Beyond the profile.** `/data` is writable, and a plain file sits where the `keys/` directory belongs, so
`Directory.CreateDirectory` for the DataProtection key ring throws at configuration time, before Kestrel
binds. Everything else in the data directory can be written: the database is created and seeded
normally. That is what separates this scenario from
[`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md), where nothing at
all can be written.

The container binds its data directory to a host path (`--bind`), so the file can be placed and removed
from the host, and uses `--own-keys`, so `test-env.csx` mounts no shared key ring over the file.

**The state this establishes, measured 2026-10-23.** The configuration-time guard records the failure as
the data-directory reason, startup continues, the database is created and seeded, and `/health` settles at
`503` once initialisation finishes.

## Determinism

- **A file at the `keys/` path, rather than a permission.** It makes `CreateDirectory` throw identically
  on every platform, which an ACL or a read-only sub-mount does not: a `keys/` directory that already
  exists read-only lets `CreateDirectory` succeed and moves the failure to the first key write.
- **`--own-keys` is required, not a preference.** Without it `test-env.csx` mounts the shared key ring at
  `/data/keys`, over the file, and the sabotage never reaches the application.
- **Wait for `unhealthy`, not for a `503`.** This container seeds a fresh database before it settles, and
  the startup wait page answers `503` with `status=starting` the whole time. A wait on the status code
  returns at once and every assertion after it reads a half-started application. Step 1 polls the
  `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established for the same reason.
- **The keys sabotage is checked before the database.** The configuration-time failure marks the
  application unhealthy before initialisation runs, and the first failure recorded wins, so a healthy
  database cannot overwrite the degraded state, and the reason read in step 1 is the keys failure's.
## Explicitly not covered here

- **The whole data directory unwritable.** That is
  [`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).
- **What the reason says, how the pages render, and what a Reset does.** This document proves the
  application says it is degraded instead of crashing, which is #327's contract. All three are recorded
  under *Observed effect*.
## Steps

### 1. Block `keys/`, start, and confirm it degraded rather than died

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-11 --delete C:\repos\Quotinator\.claude\temp\qt-startup-11-out --write-text C:\repos\Quotinator\.claude\temp\qt-startup-11\keys "not a directory"
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-11 --port 18411 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-11 --own-keys --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18411/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-11-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-11-out\health.json --equals status=unhealthy --not-empty reason
docker inspect -f '{{.State.Running}}' qt-startup-11
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-11\keys
```

**Expected:** the `json` command exits `0`, printing `status = unhealthy` and the reason;
`docker inspect` prints `true`; and the last `file` command exits `0`, `keys` still a file.

**`true` and `unhealthy` together are the never-crash contract**, for a failure that happened before
Kestrel bound. `keys` still being a file confirms the sabotage held rather than being replaced by a directory.

**On failure:** a container that is not running means the configuration-time failure killed the process,
which is the defect this document exists to catch. `status=healthy` means the file did not block
anything, so every step below asserts against a healthy container: stop. `starting` or `down` means the
wait gave up after 180 seconds.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18411/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**The database is healthy here, and the reads are gated anyway.** Health is one state: once startup marks
it failed, every database read refuses, whichever fault did it. That is the gate working as designed,
and it is why step 1's reason matters so much: it is the only place the operator learns which fault it
was.

**On failure:** a `200` from any of the seven means the gate is not consulting the degraded state, which
is the gating rule failing for a database that happens to work.

### 3. Remove the sabotage, and confirm the container is healthy

```
docker stop -t 15 qt-startup-11
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-11\keys
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-11 --port 18411 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-11 --own-keys
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18411/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18411/api/v1/version --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-directory C:\repos\Quotinator\.claude\temp\qt-startup-11\keys
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. The container reaches healthy, `/health` answers `200`, and the last `file` command exits `0`, `keys` now a directory.

**This is the positive control.** Without a passing case every assertion above would stay green against
a build that degraded on everything. With the path free, the application creates `keys/` itself.

**On failure:** a `503` here means the degradation was not specific to the blocked `keys/` path, which
makes every assertion above worthless.
## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`.

The process stays up and the database is created and seeded, while `/health` settles at `503` with the
data-directory reason: *"The data directory cannot be written … Restore write access to the data
directory and restart. A database Reset cannot resolve this: it writes too."* None of that describes this
fault: the data directory is writable, and the Reset runs. `/openapi/v1.json` answers `200` and all
seven database reads answer `503`.

**All three pages answer `500`, recorded rather than asserted**, failing to read the DataProtection key ring
(`ErrorOccurredWhileReadingKeyRing`, once per page request), as in
[`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).

**A Reset answers `200` and turns `/health` healthy while the fault remains.** Measured: after the reset,
`/health` `200`, `/api/v1/quotes` `200` with the emptied database, and `/` still `500`, with `keys` still
a file. The Reset path calls `DatabaseHealthState.MarkHealthy`, which clears every recorded failure
rather than only a database one, because #326 records the keys failure in the database's health state.

**Recovery route, recorded rather than asserted.** Reachable, and it runs, but it is not a remedy: nothing
the admin surface offers can create `keys/` while a file occupies its path. Removing the file and
restarting returns the container to `200` with every page rendering, which is step 3's positive control.

All three readings trace to one decision: #326 folds an uncreatable `keys/` directory into the database
health state with the data-directory reason. None of them is a crash, since the application still says it
is degraded; each is a finding for its own issue.

## Canary

A build from the commit before [#326](https://github.com/DutchJaFO/Quotinator/issues/326)'s first code
change, under a distinct tag, run through step 1. On that build the configuration-time `CreateDirectory`
for `keys/` throws before Kestrel binds: step 1's `docker inspect` reads `false` and nothing answers on
the port.

Step 3 is the positive control inside every run: it removes the sabotage and reads `200`, so a build that
degraded unconditionally fails there.

**Where the canary has been run, 2026-10-24.** Against `c8322590`, the commit before #326, as `quotinator:canary-326`: step 1 read `false` from `docker inspect` and `status=down`. Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 1 passed and step 2 read `200` from `/api/v1/version`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-11 --bind C:\repos\Quotinator\.claude\temp\qt-startup-11
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-11 --delete C:\repos\Quotinator\.claude\temp\qt-startup-11-out
```

**Expected:** the container is gone and the bind directory is removed, so the next run starts from
nothing.
