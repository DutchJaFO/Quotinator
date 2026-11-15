# With a fast startup check, a broken index starts healthy and is still reported

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #490

## Preconditions

**Beyond the profile.** The container runs with `Quotinator__DatabaseStartupCheck=fast`, the setting the
add-on's `database_startup_check: fast` option maps to. Its database is damaged exactly as in
[`15`](15-a-database-with-a-broken-index-degrades-and-a-reset-recovers.md): a small table and an index are
added while the container is stopped, and one entry of the index is rewritten in place through
`TestProbe damage`, the same documented-format helper the unit tests use.

**What `fast` gives up is the Indexes and References layers at startup.** ADR 028 states it, and the add-on
option's description says so. The integrity endpoint and a Reset always check at full depth, whatever the
setting, so the fault is still found where an operator looks for it.

## Determinism

- **Every change is made while the container is stopped, and stopped cleanly**, as in `15`.
- **The setting is passed on every start**, `create` and `reenter` alike. A restart without it would check at
  the default, `Full`, and degrade, which is `15`'s scenario rather than this one.
- **The restart waits for *listening*, then reads the `status` field**, so it is never confused with the
  startup wait page's own `503`.
- **Layers are read by position**, as in `15`: indexes is position 4, and the main database is reported first.

## Explicitly not covered here

- **The setting's parsing** (case, an absent value, a value that names no depth): proven by
  `DatabaseStartupCheckSettingTests`.
- **The startup check itself skipping the two layers:** proven by
  `StartupResilienceTests.Startup_FastSetting_SkipsIndexesAndStaysHealthyOverABrokenIndex`, which reads the
  check startup kept. The application does not expose that check over HTTP.

## Steps

### 1. Create a healthy database with the fast startup check

A folder left by an earlier run holds a database that is already damaged or already repaired, and step 1 must
start from none, so it is deleted first.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-16-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-16-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-16-data --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-16-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-16 --port 18416 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-16-data --env Quotinator__DatabaseStartupCheck=fast
dotnet script scripts/testing/http.csx -- --url http://localhost:18416/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-fresh.json --equals databases.0.isSound=true --equals databases.0.layers.4.layer=indexes --equals databases.0.layers.4.status=passed
```

**Expected:** the environment reports healthy, and the `json` command exits `0`: the main database is sound,
and the endpoint checked Indexes, reporting it passed rather than skipped.

**On failure:** Indexes reported `skipped` means the endpoint followed the startup setting instead of always
checking at full depth. Stop.

### 2. Stop the container cleanly, add an indexed table, and break the index

```
docker stop -t 15 qt-startup-16
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-16-data\quotinatordata.db --sql "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL); CREATE INDEX IX_CheckFixture_Rank ON CheckFixture (Rank); INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);"
dotnet run --project tools/Quotinator.Tools.TestProbe -- damage --rewrite-last-index-key C:\repos\Quotinator\.claude\temp\qt-startup-16-data\quotinatordata.db IX_CheckFixture_Rank 100
```

**Expected:** the stop echoes the container name, `execute-sql.csx` reports success, and the `damage` command
exits `0`.

**On failure:** a `damage` command that exits `1` names the precondition that did not hold, and nothing was
written. Stop.

### 3. Restart with the fast check, and confirm the application stays healthy

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-16 --port 18416 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-16-data --env Quotinator__DatabaseStartupCheck=fast --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18416/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-16-out\health-fast.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-16-out\health-fast.json --equals status=healthy
dotnet script scripts/testing/http.csx -- --url http://localhost:18416/api/v1/quotes/random --expect 200 --status
```

**Expected:** the `json` command exits `0` with `status = healthy`, and `/quotes/random` answers `200`.

**On failure:** `unhealthy` means startup checked Indexes despite the setting: the option did not reach the
application, or the startup check ignored it.

### 4. Confirm the integrity endpoint still reports the broken index

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18416/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-broken.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-broken.json --equals databases.0.isSound=false --equals databases.0.layers.4.layer=indexes --equals databases.0.layers.4.status=failed
```

**Expected:** the `json` command exits `0`: the main database is not sound, and its Indexes layer failed.

**This is what `fast` promises:** startup skips the check, and an operator who asks still finds the fault.

**On failure:** Indexes `passed` means the damage did not take; Indexes `skipped` means the endpoint followed
the startup setting. Either way the setting's description would be wrong.

### 5. Reset, and confirm the file was set aside and the database is sound again

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18416/api/v1/admin/database/reset --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-16-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-16-out\reset.json --equals backup.outcome=SetAside
dotnet script scripts/testing/http.csx -- --url http://localhost:18416/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-after-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-16-out\integrity-after-reset.json --equals databases.0.isSound=true
```

**Expected:** the Reset answers `200` with `backup.outcome = SetAside`, and the rebuilt database is sound.

**A Reset checks at full depth whatever the setting:** with `fast`, it still finds the broken index, and so
sets the file aside rather than backing it up. This is also the positive control for step 4.

**On failure:** `Succeeded` means the Reset followed the startup setting and kept a file that is not a sound
copy as a backup.

## Observed effect

**Measured 2026-11-15** against `quotinator:local` built from `6cb7a4b9`.

With `Quotinator__DatabaseStartupCheck=fast`, the fresh database was sound and the endpoint reported Indexes
`passed`, not skipped. After the index was damaged, the restart read `{"status":"healthy"}` and
`/quotes/random` answered `200`. The endpoint reported the main database not sound, Indexes `failed` with
*"row 5 missing from index IX_CheckFixture_Rank"*. The Reset answered `200` with `backup.outcome = SetAside`,
and the rebuilt database was sound.

## Canary

Build the commit before #490's first code change, `342e0cfa`, under a distinct tag, and run steps 1 to 4
against it.

Step 1's `json` command must fail on `databases.0.isSound`: the endpoint reports no layers there. Run steps 2
to 4 anyway, without step 1's `json` command. Step 3 reads `healthy` there as it does here, since nothing
checked indexes before #490, so it is step 4 that must fail: the endpoint has no layer to report the broken
index with.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-11-15.** Against `342e0cfa`, as `quotinator:canary-490`: step 1's `json`
command failed on `databases.0.isSound`, `layers.4.layer` and `layers.4.status`, each absent. With the index
damaged, step 3 read `healthy` and `/quotes/random` answered `200`, as expected there. Step 4's `json` command
failed on the same three fields: the endpoint had no layer to report the broken index with.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-16 --bind C:\repos\Quotinator\.claude\temp\qt-startup-16-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-16-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-16-out
```

**Expected:** the container and both directories are removed.
