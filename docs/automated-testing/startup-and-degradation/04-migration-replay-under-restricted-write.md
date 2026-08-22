# Migration replay survives an environment where only the data directory is writable

**Smoke:** no
**Traces to:** #294

## Preconditions

**A seeded database from a real historical release**, not a fresh one. An earlier attempt at this test
used a fresh baseline database — empty tables, pure `INSERT`s — and passed identically whether the fix
was present or not, because a fresh insert has nothing to conflict with and never exercises the
statement-journal code path at all. The real incident happened during **migration replay against an
already-populated database**, so this test must start from one.

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

**Known limitation, stated up front.** The real gap #294 theorizes — `/tmp/** rw` granting write but
not *lock* — has no Docker-mount equivalent. File locking is an LSM-level concept (AppArmor/SELinux),
not something `--read-only`/`ro`/`tmpfs` flags control, and Docker Desktop's WSL2 backend has no
AppArmor kernel support to test the real mechanism directly (confirmed live:
`/sys/module/apparmor/parameters/enabled` reads `N`, no `/sys/kernel/security/apparmor` securityfs).
`--read-only` is *stricter* than the real profile — it denies writes entirely rather than just locking
— so a pass here is strong evidence, not proof of the exact mechanism.

## Steps

**Seed from the predecessor release:**

```bash
docker rm -f smoke294 2>/dev/null
docker volume rm smoke294-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name smoke294 -p 8080:8080 \
  -v smoke294-data:/data -e Quotinator__DataDir=/data \
  ghcr.io/dutchjafo/quotinator:1.8.2
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
docker stop -t 15 smoke294 && docker rm smoke294
```

**Upgrade under the restricted environment:**

```bash
MSYS_NO_PATHCONV=1 docker run -d --name smoke294 -p 8080:8080 \
  --read-only \
  -v smoke294-data:/data -e Quotinator__DataDir=/data \
  quotinator:local
until curl -s -o /dev/null http://localhost:8080/api/v1/health; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:8080/api/v1/health"
curl -s "http://localhost:8080/api/v1/version"
docker logs smoke294 2>&1 | grep "migration applied\|SqliteException\|SQLite Error"
```

## Expected output

`/health` returns `200 {"status":"healthy"}`. `/version` shows the **same** quote count as the seeding
run recorded above, and every other bundled count non-zero — migration replay must not lose content,
which is a relationship between the two runs rather than a number either of them should predict. The
logs show a `migration applied:` line and **no**
`SqliteException`/`SQLite Error` line — the fix means the migration's temp files never touch disk at
all, so restricting every other writable path does not matter.

**Never assert specific migration version numbers.** What matters is that replay *completed* under the
restricted environment, not which versions were involved — the counts move whenever a milestone adds a
migration, so a hardcoded `Data vN → vM` goes stale on its own and gets "fixed" by editing a number
rather than by anyone checking what happened.

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
docker run --rm -v smoke294-data:/from -v smoke294-data-clone:/to alpine sh -c "cp -a /from/. /to/"
```

It must reproduce a genuine failure somewhere in `ApplyMigrationPhaseAsync`. Revert the flag to `true`
before committing anything.

## Cleanup

```bash
docker rm -f smoke294
docker volume rm smoke294-data smoke294-data-clone 2>/dev/null
```
