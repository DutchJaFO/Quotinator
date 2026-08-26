# Migration replay survives an environment where only the data directory is writable

**Smoke:** no
**Environment:** Upgraded + Constrained
**Traces to:** #294

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published `ghcr.io/dutchjafo/quotinator:1.8.2`
tag**, not the milestone base image — this test is about the upgrade a real user performs from a real
historical release. It runs its own container and volume (`qt-startup-04` / `qt-startup-04-data`, the name reused
across the two runs), and the Constrained defect is `--read-only` on the root
filesystem with `/data` left writable.

The prior release matters for a specific reason: an earlier attempt at this test used a fresh baseline
database — empty tables, pure `INSERT`s — and passed identically whether the fix was present or not,
because a fresh insert has nothing to conflict with and never exercises the statement-journal code path
at all. The real incident happened during **migration replay against an already-populated database**, so
this test must start from one.

**The seeding run must have completed cleanly before proceeding** — a partially-seeded volume produces
a misleading result. The fact to establish is not a dataset size but that seeding finished with zero
failures: `quotes` is non-zero, and the seed's own report shows nothing `Pending`, `Blocked` or
`Stale`. Record the count you observe so the upgrade below can be compared against it.

## Determinism

- **The seeding image is the release immediately before the one introducing the migration under test**
  — `1.8.2` is the current value, not a fixed constant. Adjust it when testing a later migration; the
  point is the predecessor release, not that specific tag.
- **`--read-only` applies to the root filesystem while `/data` stays a writable volume.** That is the
  restriction under test: everything except the data directory is unwritable.
- **The second run is a `reenter`, not a `create`.** `create` wipes the volume, which would throw away
  the populated database this test exists to migrate — and the run would then pass for the same reason
  the discarded fresh-baseline attempt did, by never exercising the code path at all.
- **The seeded volume is consumed by the first run.** It upgrades the schema in place, so a second
  attempt against the same volume no longer exercises the migration at all. Clone it first if
  re-running.
- **Never assert specific migration version numbers.** What matters is that replay *completed* under the
  restricted environment, not which versions were involved — the counts move whenever a milestone adds a
  migration, so a hardcoded `Data vN → vM` goes stale on its own and gets "fixed" by editing a number
  rather than by anyone checking what happened.

**Known limitation, stated up front.** The real gap #294 theorizes — `/tmp/** rw` granting write but
not *lock* — has no Docker-mount equivalent. File locking is an LSM-level concept (AppArmor/SELinux),
not something `--read-only`/`ro`/`tmpfs` flags control, and Docker Desktop's WSL2 backend has no
AppArmor kernel support to test the real mechanism directly (confirmed live:
`/sys/module/apparmor/parameters/enabled` reads `N`, no `/sys/kernel/security/apparmor` securityfs).
`--read-only` is *stricter* than the real profile — it denies writes entirely rather than just locking
— so a pass here is strong evidence, not proof of the exact mechanism.

## Steps

### 1. Seed a populated database from the predecessor release

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-04 --port 18404 `
  --image ghcr.io/dutchjafo/quotinator:1.8.2

$seeded = (Invoke-RestMethod "http://localhost:18404/api/v1/version").database
"quotes=$($seeded.quotes) sources=$($seeded.sources)"
```

**Expected:** a non-zero `quotes` count, and a seed reporting zero failures — nothing `Pending`,
`Blocked` or `Stale`. `$seeded` is kept for step 2 to compare against.

**On failure:** a zero or partial count means the volume is only partially seeded, and the upgrade
below has nothing meaningful to replay against — a pass would then say nothing about the restricted
environment. Stop and re-seed rather than continuing.

### 2. Upgrade to the current build under the restricted environment

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-04 --port 18404 `
  --image quotinator:local --read-only --wait-listening

(dotnet script scripts/testing/http.csx -- --url "http://localhost:18404/api/v1/health" --expect 200 | ConvertFrom-Json).status
$upgraded = (Invoke-RestMethod "http://localhost:18404/api/v1/version").database
"quotes=$($upgraded.quotes) same=$($upgraded.quotes -eq $seeded.quotes)"

$log = docker logs qt-startup-04 2>&1 | Out-String
"migrationApplied=$(([regex]::Matches($log, 'migration applied')).Count)"
"sqliteErrors=$(([regex]::Matches($log, 'SqliteException|SQLite Error')).Count)"
```

**Expected:** `/health` returns `200` and `healthy`. `same=True` — the quote count is identical to what
the seeding run recorded, because migration replay must not lose content; that is a relationship
between the two runs rather than a number either of them should predict. `migrationApplied` is non-zero
and `sqliteErrors` is `0` — the fix means the migration's temp files never touch disk at all, so
restricting every other writable path does not matter.

The start waits for *listening* rather than healthy: if the migration fails this container answers
`503` by design, and waiting for `200` would burn the whole timeout before the assertions that name
the failure ever run.

## Observed effect

Established for the failing case, which is unusual and useful. Reverting the fix reproduces a real
`SqliteException` — `SQLite Error 10: 'disk I/O error'` was live-verified 2026-08-11 — plus the
degraded `/health` → `503 {"status":"unhealthy"}` and `/version` → `schemaVersion: 0, quotes: 0`
outcome the original incident showed.

That error code differs from the original incident's `SQLite Error 14: 'unable to open database file'`,
because `--read-only`'s full write-denial hits a different syscall than the real profile's
lock-denial would. Same class of failure, different code; an exact match is not expected.

## Confirming this test would have caught the original bug

Not required on every run — a one-time gut-check when this document itself changes.

In `Program.cs`, temporarily change `useMemoryTempStore: true` to `false` at `SqliteConnectionFactory`'s
DI registration site, rebuild, and repeat the upgrade against a **fresh clone** of the seeded volume:

```powershell
docker run --rm -v qt-startup-04-data:/from -v qt-startup-04-data-clone:/to alpine sh -c "cp -a /from/. /to/"
```

It must reproduce a genuine failure somewhere in `ApplyMigrationPhaseAsync`. Revert the flag to `true`
before committing anything.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-04
docker volume rm qt-startup-04-data-clone 2>$null
```

If the gut-check section above was run, confirm `useMemoryTempStore: true` has been restored in
`Program.cs` and the throwaway image rebuilt from it.
