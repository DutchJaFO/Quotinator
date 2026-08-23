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

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-04 --port 18404 \
  --image ghcr.io/dutchjafo/quotinator:1.8.2
curl -s "http://localhost:18404/api/v1/version" | grep -o '"quotes":[0-9]*'
docker stop -t 15 qt-startup-04 && docker rm qt-startup-04
```

**Expected:** a non-zero `quotes` count, and a seed reporting zero failures — nothing `Pending`,
`Blocked` or `Stale`. Record the count; the upgrade step compares against it.

**On failure:** a zero or partial count means the volume is only partially seeded, and the upgrade
below has nothing meaningful to replay against — a pass would then say nothing about the restricted
environment. Stop and re-seed rather than continuing.

### 2. Upgrade to the current build under the restricted environment

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qt-startup-04 -p 18404:8080 \
  --read-only \
  -v qt-startup-04-data:/data -e Quotinator__DataDir=/data \
  quotinator:local
until curl -s -o /dev/null http://localhost:18404/api/v1/health; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:18404/api/v1/health"
curl -s "http://localhost:18404/api/v1/version"
docker logs qt-startup-04 2>&1 | grep "migration applied\|SqliteException\|SQLite Error"
```

**Expected:** `/health` returns `200 {"status":"healthy"}`. `/version` shows the **same** quote count as
the seeding run recorded, and every other bundled count non-zero — migration replay must not lose
content, which is a relationship between the two runs rather than a number either of them should
predict. The logs show a `migration applied:` line and **no** `SqliteException`/`SQLite Error` line —
the fix means the migration's temp files never touch disk at all, so restricting every other writable
path does not matter.

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

```bash
docker run --rm -v qt-startup-04-data:/from -v qt-startup-04-data-clone:/to alpine sh -c "cp -a /from/. /to/"
```

It must reproduce a genuine failure somewhere in `ApplyMigrationPhaseAsync`. Revert the flag to `true`
before committing anything.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-04
docker volume rm qt-startup-04-data-clone 2>/dev/null
```

If the gut-check section above was run, confirm `useMemoryTempStore: true` has been restored in
`Program.cs` and the throwaway image rebuilt from it.
