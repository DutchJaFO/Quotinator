# Startup backs up only when there is real work, and respects the storage budget

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #277

## Preconditions

**Beyond the profile.** The data directory is *reused across restarts* rather than being a one-boot
environment: the whole test is about what successive startups against the same data directory do, and
a fresh container each time would never reach the states being checked. It therefore runs its own
container on a bind directory (`qt-startup-02`), so the host can count the backups, and the final step
**re-enters** that same directory with `Quotinator__MaxBackupStorageGb=0` set.

The sequence matters and each step depends on the one before it: fresh baseline → healthy restart →
Reset → restart-after-Reset → budget exceeded.

**A backup protects a specific risky action, not a startup.** One is taken before a migration and
before a reseed, so that a partial failure still leaves a working database and a user can recover:
which is why Reset is what the app offers after a failed migration. A startup with no migration pending
and nothing to seed puts nothing at risk. That is #277's gating, and it is why the count reads none,
none, `1`, `1`, `1` rather than incrementing on every start.

## Determinism

- **Waits for health, not a duration**, at every start.
- **The backup count is read from the folder at each step.** Checking only the final number would not
  distinguish which startup took which backup. One file per backup; its `-shm`/`-wal` sidecars do not
  match `*.db`.
- **The budget run re-enters the same directory rather than starting a second container.** The budget is
  configuration and cannot be applied to a running container, but `reenter` replaces the container
  while keeping the data, which is what this step needs.
- **The refused Reset is proven by the test's own content.** Step 4 stores the
  [sentinel quote](fixtures/sentinel-quote.json), and step 5 finds it still there after the refusal. A Reset
  that ran would have removed it, whatever the bundled sources hold.
- **The zero exception counts are given a positive control in the same run.** Step 4's malformed upload
  shows the log recording an exception it expects, so a zero read elsewhere in this run means something.

## Steps

### 1. Install a fresh baseline

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-02 --delete C:\repos\Quotinator\.claude\temp\qt-startup-02-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-02 --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-02-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-02 --port 18402 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-startup-02\quotinatordata.db --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-02\backups
```

**Expected:** the `file` command exits `0`: the database exists and there is no `backups` directory.
Nothing exists to lose.

**On failure:** a `backups` directory here means the directory was not new, so the run is not a baseline at
all, and every later step is measuring a different sequence. Stop and re-run from the first command.

### 2. Restart while healthy

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-startup-02
dotnet script scripts/testing/http.csx -- --url http://localhost:18402/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-first-restart.mark
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-02\backups
```

**Expected:** every command exits `0`: nothing thrown before the restart, the restarted application
healthy, and still no `backups` directory. No migration is pending and the content already exists, so
there is nothing to protect. The mark puts the restart's own documented shutdown lines behind what step 4
counts (index: *Read the log before the application stops*).

### 3. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18402/api/v1/admin/database/reset --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-startup-02\backups "*.db" 1
```

**Expected:** `200`, and exactly one backup. Reset backs up unconditionally, being the highest-risk
operation.

### 4. Restart immediately after the Reset, then store the test's own content

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-first-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-startup-02
dotnet script scripts/testing/http.csx -- --url http://localhost:18402/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-second-restart.mark
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-startup-02\backups "*.db" 1
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18402/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-second-restart.mark --pattern "thrown: JsonReaderException" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-control.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18402/api/v1/import --method POST --file docs/automated-testing/startup-and-degradation/fixtures/sentinel-quote.json --expect 200
```

**Expected:** nothing thrown since step 2's restart; the restarted application healthy; **still exactly one
backup**; the malformed upload answering `422` and its `JsonReaderException` logged; and the sentinel
stored.

**This restart takes no backup, and that is the application being right.** Startup seeds only a database
it *created*, not one that merely holds no rows, so the empty database a Reset leaves behind is not
re-seeded: nothing is written, nothing is at risk. That is #156's intent holding: an operator who resets
to start fresh with their own content is not made to re-accept the bundled dataset. The gate is **not**
based on whether a migration applied, which would miss a content load that had real work to do; this
particular restart has none.

### 5. Reset again with the backup budget already exceeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-02-out\after-control.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-02 --port 18402 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-02 --env Quotinator__MaxBackupStorageGb=0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18402/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-02-out\reset-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-02-out\reset-refused.json --equals backupObstacle=BudgetExceeded --contains remedies=/api/v1/admin/backups --contains remedies=MaxBackupStorageGb
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-startup-02\backups "*.db" 1
dotnet script scripts/testing/http.csx -- --url http://localhost:18402/api/v1/quotes/5e7a1c3d-9b2f-4e6a-8d0c-b1f2a3e4c5d6 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-02-out\sentinel-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-02-out\sentinel-after.json --equals source="Quotinator Test Harness"
```

**Expected:** nothing thrown before the stop; `409` with `backupObstacle` `BudgetExceeded` and remedies
naming the backup endpoints and the quota setting; still exactly one backup; and the sentinel still there,
because the refusal stopped before the destructive step.

**Reset refuses rather than proceeding without a backup**, unless the caller passes `allowNoBackup=true`.
[`../backup/05-a-full-quota-is-resolvable-from-inside-the-application.md`](../backup/05-a-full-quota-is-resolvable-from-inside-the-application.md)
asserts the remedies end to end; what this step keeps is that the budget is applied to a directory that
already holds a backup.

**On failure:** a second backup means the budget was not applied. Check that `reenter` (not `create`) was
used. A missing sentinel means the refused Reset ran anyway, which is the defect #348 removed.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

A fresh install and a healthy restart leave no `backups` directory. The Reset leaves one backup, the restart
after it none further. With the budget at `0`, the Reset answers `409`, obstacle `BudgetExceeded`, naming
removing old backups first, raising `Quotinator:MaxBackupStorageGb` second and `allowNoBackup=true` third;
the backup count stays at one and the sentinel quote stays. The malformed upload answers `422` with one
`JsonReaderException` logged, and no other exception was logged at any read.

## Canary

Build the commit before [#277](https://github.com/DutchJaFO/Quotinator/issues/277)'s first code
change, under a distinct tag, and run steps 1 to 3 against it.

Step 2 must find a `backups` directory holding a backup. Gating each action's backup on its own real-work
signal is what #277 added, so on that build a healthy restart with no migration pending and no content to
load backs up anyway, which is the waste this document exists to keep from returning.

**Step 3 is the control.** Reset backs up unconditionally, being the highest-risk operation, so a run where
step 3 finds no backup has broken backups rather than working gating, and step 2's reading would mean
nothing.

**Measured 2026-11-01** on `quotinator:canary-277`, built from `ed00ef2c`, steps 1 to 3 in order. Step 1 passes. Step 2 fails:
the healthy restart leaves a `backups` directory. Step 3 answers `200` and its exact count of `1` fails at `2`: step 2's
backup, and Reset's own beside it, so the control holds.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-02 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-02 --bind C:\repos\Quotinator\.claude\temp\qt-startup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-02 --delete C:\repos\Quotinator\.claude\temp\qt-startup-02-out
```

**Expected:** the `log` command exits `0`: `reenter` replaced the container in step 5, so its log starts
there, and a refusal is not an exception.
