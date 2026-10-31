# Migration replay survives an environment where only the data directory is writable

**Smoke:** no
**Environment:** Upgraded + Constrained
**Deterministic:** yes
**Traces to:** #294

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published `ghcr.io/dutchjafo/quotinator:1.8.2`
tag**, not the milestone base image: this test is about the upgrade a real user performs from a real
historical release. It runs its own container and volume (`qt-startup-04` / `qt-startup-04-data`, the name reused
across the two runs), and the Constrained defect is `--read-only` on the root
filesystem with `/data` left writable.

The prior release matters for a specific reason: an earlier attempt at this test used a fresh baseline
database (empty tables, pure `INSERT`s) and passed identically whether the fix was present or not,
because a fresh insert has nothing to conflict with and never exercises the statement-journal code path
at all. The real incident happened during **migration replay against an already-populated database**, so
this test must start from one.

**The test owns the content it asserts on.** The predecessor release seeds its bundled sources, which is
what populates the database the migration replays against, but nothing here asserts what they contain:
that changes whenever a source or a correction does. Instead step 1 imports the test's own
[sentinel quote](fixtures/sentinel-quote.json) into the populated database, and step 2 asks whether the
replay kept it.

## Determinism

- **The seeding image is the release immediately before the one introducing the migration under test**
  since `1.8.2` is the current value, not a fixed constant. Adjust it when testing a later migration; the
  point is the predecessor release, not that specific tag.
- **`--read-only` applies to the root filesystem while `/data` stays a writable volume.** That is the
  restriction under test: everything except the data directory is unwritable.
- **The second run is a `reenter`, not a `create`.** `create` wipes the volume, which would throw away
  the populated database this test exists to migrate, and the run would then pass for the same reason
  the discarded fresh-baseline attempt did, by never exercising the code path at all.
- **The seeded volume is consumed by the first run.** It upgrades the schema in place, so a second
  attempt against the same volume no longer exercises the migration at all. Clone it first if
  re-running.
- **Never assert specific migration version numbers.** What matters is that replay *completed* under the
  restricted environment, not which versions were involved: the counts move whenever a milestone adds a
  migration, so a hardcoded `Data vN → vM` goes stale on its own and gets "fixed" by editing a number
  rather than by anyone checking what happened.

**Known limitation, stated up front.** The real gap #294 theorizes, `/tmp/** rw` granting write but
not *lock*, has no Docker-mount equivalent. File locking is an LSM-level concept (AppArmor/SELinux),
not something `--read-only`/`ro`/`tmpfs` flags control, and Docker Desktop's WSL2 backend has no
AppArmor kernel support to test the real mechanism directly (confirmed live:
`/sys/module/apparmor/parameters/enabled` reads `N`, no `/sys/kernel/security/apparmor` securityfs).
`--read-only` is *stricter* than the real profile, denying writes entirely rather than just locking,
so a pass here is strong evidence, not proof of the exact mechanism.

## Steps

### 1. Seed a populated database from the predecessor release

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-04-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-04 --port 18404 --image ghcr.io/dutchjafo/quotinator:1.8.2
dotnet script scripts/testing/http.csx -- --url http://localhost:18404/api/v1/import --method POST --file docs/automated-testing/startup-and-degradation/fixtures/sentinel-quote.json --expect 200
dotnet script scripts/testing/http.csx -- --url http://localhost:18404/api/v1/quotes/5e7a1c3d-9b2f-4e6a-8d0c-b1f2a3e4c5d6 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-04-out\sentinel-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-04-out\sentinel-before.json --equals source="Quotinator Test Harness"
```

**Expected:** the environment reports healthy, the import answers `200`, and the `json` command exits
`0`: the sentinel is stored in a populated predecessor database and readable by its id.

**On failure:** a predecessor that cannot reach healthy, or a sentinel that cannot be stored, leaves
step 2 with nothing to replay against or nothing to prove. Stop.

### 2. Upgrade to the current build under the restricted environment

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-04 --port 18404 --image quotinator:local --read-only --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18404/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-04-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-04-out\health.json --equals status=healthy
dotnet script scripts/testing/http.csx -- --url http://localhost:18404/api/v1/quotes/5e7a1c3d-9b2f-4e6a-8d0c-b1f2a3e4c5d6 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-04-out\sentinel-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-04-out\sentinel-after.json --equals source="Quotinator Test Harness"
dotnet script scripts/testing/http.csx -- --url http://localhost:18404/api/v1/notifications?pageSize=0 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-04-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-04-out\notifications.json --contains items=migrationapplied
```

**Expected:** every TestProbe command exits `0`. `/health` reports `healthy`; the sentinel from step 1
is still there, because migration replay must not lose content the test owns; and the notifications
list holds a notification of kind `migrationapplied`, which the application writes for each migration
it applies, so replay ran rather than being skipped. The fix means the migration's temp files never
touch disk at all, so restricting every other writable path does not matter.

**Nothing here reads a log line's wording, or asserts that no error was logged.** That replay
succeeded is shown by `healthy` and the notification, both responses; a failed migration answers
`503`. An empty error log would prove nothing on its own: a log without exceptions is not proof there
were none, and this step has no defect of its own to show the channel working.

**Nothing here counts quotes.** Migration009 (#374) deliberately deletes repeated quotes under one
Source, so a count of the bundled content moves with whatever duplicates the predecessor's sources held,
which is the bundled sources' property, not this test's subject. The sentinel has no duplicate, so it
must survive.

The start waits for *listening* rather than healthy: if the migration fails this container answers
`503` by design, and waiting for `200` would burn the whole timeout before the assertions that name
the failure ever run.

## Observed effect

**Measured 2026-10-27** against `quotinator:local` built from `00a9dfe2`, upgrading a `1.8.2` volume
holding the sentinel quote, with the root filesystem `--read-only`.

`/health` reads `healthy`, the sentinel quote is still there, and the notifications list holds one
`migrationapplied` notification per migration applied, among 33 notifications in all.

## Canary

Not required on every run: a one-time gut-check when this document itself changes.

In `Program.cs`, temporarily change `useMemoryTempStore: true` to `false` at `SqliteConnectionFactory`'s
DI registration site, rebuild, and repeat the upgrade against a **fresh clone** of the seeded volume:

```
docker run --rm -v qt-startup-04-data:/from -v qt-startup-04-data-clone:/to alpine sh -c "cp -a /from/. /to/"
```

It must reproduce a genuine failure somewhere in `ApplyMigrationPhaseAsync`. Revert the flag to `true`
before committing anything.

**Where the canary has been run, 2026-10-31.** Against `2f4abbd6` with the flag set to `false`, as `quotinator:canary-04`, upgrading a freshly seeded `1.8.2` volume: step 2 read `status=unhealthy` with the generic initialisation reason, where the unmodified build reads `healthy`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-04
docker volume rm --force qt-startup-04-data-clone
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-04-out
```

If the gut-check section above was run, confirm `useMemoryTempStore: true` has been restored in
`Program.cs` and the throwaway image rebuilt from it.
