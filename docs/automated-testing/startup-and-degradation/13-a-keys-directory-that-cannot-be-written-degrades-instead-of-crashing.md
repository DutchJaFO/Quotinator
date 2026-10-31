# A keys directory that cannot be written degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** `/data` is a writable bind directory, and an empty host directory is mounted
read-only over `/data/keys`. The `keys/` directory therefore exists, so creating it succeeds, but no key
file can be written into it: the failure is in the key ring, not the data directory, and the database is
created and seeded normally.

The container is started with its own `docker run`, since `test-env.csx` has no option for a separate
mount at `keys/`. It carries the profile's settings and nothing else.

**The state this establishes.** A key ring that cannot be written fails on every page request, so the
application has to notice it at startup and report itself degraded, or the operator meets page failures
with nothing telling them why.

## Determinism

- **A read-only mount, not a missing directory.** A `keys/` path that cannot be created is
  [`11`](11-a-keys-directory-that-cannot-be-created-degrades-instead-of-crashing.md)'s subject, and
  fails at a different point: `CreateDirectory` throws there, and succeeds here.
- **The keys mount is an empty directory.** A key ring that already holds keys can be read but not
  rotated, which is a different, later failure.
- **Wait for `unhealthy` or `healthy`, never for a `503`.** The startup wait page answers `503` with
  `status=starting` while the fresh database seeds, so step 1 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.

## Explicitly not covered here

- **How the degraded pages render.** This document proves the application says it is degraded instead
  of crashing, which is #327's contract. The pages fail on the key ring in this state, as they do for an
  uncreatable `keys/`; that is [#454](https://github.com/DutchJaFO/Quotinator/issues/454)'s, and is
  recorded under *Observed effect*.

## Steps

### 1. Start with `keys/` mounted read-only, and confirm it degraded rather than died

```
docker rm -f qt-startup-13
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-13 --delete C:\repos\Quotinator\.claude\temp\qt-startup-13-keys --delete C:\repos\Quotinator\.claude\temp\qt-startup-13-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-13 --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-13-keys
docker run -d --name qt-startup-13 -p 18413:8080 -v C:\repos\Quotinator\.claude\temp\qt-startup-13:/data -v C:\repos\Quotinator\.claude\temp\qt-startup-13-keys:/data/keys:ro -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest -e Quotinator__AutoPurgeBundledImportActions=true -e Quotinator__AutoUpdateSources=false quotinator:local
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18413/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-13-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-13-out\health.json --equals status=unhealthy --not-empty reason
docker inspect -f '{{.State.Running}}' qt-startup-13
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-empty-directory C:\repos\Quotinator\.claude\temp\qt-startup-13-keys
```

**Expected:** the `json` command exits `0`, printing `status = unhealthy` and the reason;
`docker inspect` prints `true`; and the last `file` command exits `0`, the keys directory empty.

**`unhealthy` is the whole point of this document.** The key ring cannot be written, so every page
fails; an application reporting `healthy` here has not noticed that it is degraded. The empty keys
directory confirms the mount really refused.

**On failure:** a container that is not running means the fault killed the process. `status=healthy`
is the defect this document exists to catch: the application does not know it is degraded. Files in
the keys directory mean the mount was writable, so nothing below is about the fault. `starting` or `down` means
the wait gave up after 180 seconds.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18413/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven.

**The database is healthy here, and the reads are gated anyway**: once startup reports the application
degraded, every database read refuses, whichever fault did it.

**On failure:** a `200` from any of the seven means the degraded state is not gating the reads.

### 3. Remove the sabotage, and confirm the container is healthy

```
docker rm -f qt-startup-13
docker run -d --name qt-startup-13 -p 18413:8080 -v C:\repos\Quotinator\.claude\temp\qt-startup-13:/data -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest -e Quotinator__AutoPurgeBundledImportActions=true -e Quotinator__AutoUpdateSources=false quotinator:local
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/health --wait-for 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18413/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18413/api/v1/version --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-matches C:\repos\Quotinator\.claude\temp\qt-startup-13\keys "*.xml" 1
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. `/health` answers `200`, and the `file` command exits `0`, with at least one key file written.

**This is the positive control.** The same data directory with a writable `keys/` starts healthy and
writes its key ring, so the degraded state above is about the read-only mount and nothing else.

**On failure:** a `503` here means the degradation was not specific to the read-only `keys/`.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

The process stays up and `/health` settles at `503 unhealthy` with the data-directory reason, no key file
written. `/openapi/v1.json` answers `200`, all seven database reads `503`, and `/` `500`. With `keys/`
writable again, the same data directory starts healthy, writes one key file, and every one of the seven
reads answers `200`.

**The reason names the data directory, not `keys/`.** The data directory here is writable; the reason
is #326's, reused until [#455](https://github.com/DutchJaFO/Quotinator/issues/455) gives `keys/` a reason
of its own. The page failure is [#454](https://github.com/DutchJaFO/Quotinator/issues/454)'s. The
Knowledgebase entry
`every-page-fails-when-the-keys-directory-cannot-be-created.md` covers what an operator does about it.

## Canary

**A pre-fix build.** Run against the build from before #327's step 14 fix, step 1 reads
`status=healthy`: the application does not notice the key ring cannot be written.

**The positive control this run produces for itself.** Step 3 removes the sabotage and reads `200`, so a
build that degraded unconditionally fails there.

**Where the canary has been run, 2026-10-31.** Against `72b39c02`, the commit before the step 14 fix, as `quotinator:canary-327-keys`: step 1 read `status=healthy` with no reason and no key file. Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 1 read `status=healthy` with no reason.

## Cleanup

```
docker rm -f qt-startup-13
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-13 --delete C:\repos\Quotinator\.claude\temp\qt-startup-13-keys --delete C:\repos\Quotinator\.claude\temp\qt-startup-13-out
```

**Expected:** the container is gone and both directories are removed, so the next run starts from
nothing.
