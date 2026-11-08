# Upgrading from an intermediate schema version, not just the last release

**Smoke:** no
**Environment:** Upgraded
**Deterministic:** yes
**Traces to:** #312, #305

## Preconditions

**Beyond the profile.** The Upgraded prior state is built by the three builds a real installation passes
through, one after another, on one bind-mounted directory, under one container name (`qt-notif-03`,
publishing `18503`):

1. the published `ghcr.io/dutchjafo/quotinator:1.8.3` tag creates the database;
2. **the intermediate build**, `e94e9c82`, upgrades it: the commit before #312's first code change, built
   from its own tree as `quotinator:pre-312`, the same way a canary is built;
3. this build upgrades it from there.

**Nothing in the database is written by hand.** The intermediate build applies its own migrations and
records its own version, so the state this document upgrades from is one a real database has held, not a
copy of one written to look like it.

**This exists because upgrading from the last release alone missed a startup-killing bug.** The
released-database path starts from v1.8.3, where `System_AppVersion` does not exist at all, so a
pre-migration read of it hits the missing-table path and returns null. A database the intermediate build
has upgraded is a different state: the table exists, but the `Application` column a later migration adds
does not. A build that read that column before migrating crashed startup with `no such column:
Application`, and only a T1 run on a real dev database exposed it.

**Whenever a migration adds a column to a table that startup reads before migrating, verify the
intermediate state as well as the released one.** ADR 009 mandates the last *released* schema; that is a
floor, not a ceiling, and unreleased intermediate versions exist on every developer machine. Any commit can
be built, so any of them can be reproduced this way.

## Determinism

- **Each stage proves the state it leaves before the next one starts.** Step 2 shows the released database
  has no `System_AppVersion`. Step 3 shows the intermediate one has the table and lacks the `Application`
  column, by reading that column and getting the incident's own error. A run that reached the current
  build from any other state would be testing a different upgrade, and these reads are what rule it out.
- **The intermediate build is a fixed commit, built from its own tree.** Its own `docker/Dockerfile` is
  used: today's names `Directory.Packages.props`, which that tree does not have.
- **The current build's start waits for *listening*, then polls `status` for either outcome**, so a start
  that degrades is read rather than waited out.
- **No migration count or version number is asserted.** The states are proven by what each stage's schema
  can and cannot answer.
- **DbInspector reads the bind directory from the host** (`Mode=ReadOnly`, by design), so nothing writes
  to the database but the three builds.
- **The zero exception count is given a positive control in the same run**: the malformed upload in step 4
  shows the channel recording an exception it expects.

## Explicitly not covered here

**Which version the intermediate build records.** It is the version in that commit's own
`Directory.Build.props`, `1.8.3`, the same as the release's: the build predates the `-alpha` suffix the
milestone workflow now adds at milestone start. Step 5 reads the row it wrote as it is.

## Steps

### 1. Build the intermediate build

```
git worktree add --detach C:\repos\Quotinator\.claude\temp\wt-pre-312 e94e9c82
docker build -f C:\repos\Quotinator\.claude\temp\wt-pre-312\docker\Dockerfile -t quotinator:pre-312 C:\repos\Quotinator\.claude\temp\wt-pre-312
```

**Expected:** both commands exit `0`: the worktree is at `e94e9c82` and the image builds.

**On failure:** a worktree that already exists is left from an earlier run; remove it as *Cleanup* does and
repeat.

### 2. Create the released database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-03-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-03-out --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-03-data --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-03-out
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-03 --port 18503 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind C:\repos\Quotinator\.claude\temp\qt-notif-03-data
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-03-data\quotinatordata.db --sql "SELECT Version FROM System_AppVersion"
```

**Expected:** the release reaches healthy, and DbInspector exits `1` with `no such table:
System_AppVersion`: the released schema predates the table entirely.

**On failure:** a table that exists means the directory was not new, or the image is not the release.
Stop.

### 3. Upgrade it with the intermediate build

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-03 --port 18503 --image quotinator:pre-312 --bind C:\repos\Quotinator\.claude\temp\qt-notif-03-data
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-03-data\quotinatordata.db --sql "SELECT Version FROM System_AppVersion"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-03-data\quotinatordata.db --sql "SELECT Application FROM System_AppVersion"
```

**Expected:** the intermediate build reaches healthy; the first read lists one row, the version it recorded
for itself; the second exits `1` with `no such column: Application`.

**That error is the state this document exists for**: the table startup reads, without the column a later
migration adds. A second read that succeeds means the intermediate build is not the one named above. Stop.

### 4. Start the current build against that intermediate state

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-03 --port 18503 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-03-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18503/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-notif-03-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-03-out\health.json --equals status=healthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-03 --pattern "no such column" --exactly 0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-03 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --url http://localhost:18503/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-03-out\integrity.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-03-out\integrity.json --equals databases.0.isIntact=true --length-equals databases.0.differences=0 --equals databases.1.isIntact=true --length-equals databases.1.differences=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18503/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-03-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-03-out\notifications.json --contains items=migrationapplied
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18503/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-03 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** every command exits `0`. `/health` reads `healthy`; the log holds no `no such column` and no
exception; the integrity check reports both databases intact, with no difference and no extra object; the
notifications list holds a `migrationapplied` notification, so the migrations ran; and the malformed upload
answers `422` with its `JsonReaderException` logged, which is the positive control for the zero count.

**`healthy` and intact together are the point.** The first is the startup #312 fixed surviving the read
that killed it; the second (#305) is the database it leaves matching the schema a fresh install has, so the
upgrade from this state is not only survived but complete.

**On failure:** `no such column` in the log is the defect itself, the version read moved before the
migrations again. `unhealthy` with a reason naming a table or column is an intermediate state the migration
path does not finish correctly: the integrity endpoint lists what differs.

### 5. Read the version history

```
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-03-data\quotinatordata.db --sql "SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber"
dotnet script scripts/testing/http.csx -- --url http://localhost:18503/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-03-out\version.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-03-out\version.json --equals-xml version=Directory.Build.props#Version
```

**Expected:** three rows, in this order, and the `json` command exits `0`, printing the running version:

| Application | Version | SequenceNumber | Written by |
|---|---|---|---|
| `Quotinator.Api` | `1.8.3` | `0` | the provenance migration, deliberately below the minimum |
| *(NULL)* | the version step 3 read | `1` | the intermediate build, before the `Application` column existed |
| `Quotinator.Api` | the version the `json` command printed | `2` | the running build, appended |

The intermediate build's row survives with its `Application` still `NULL`, and the current version is
**appended** rather than replacing it.

**The `1.8.3` row at sequence `0` is correct, not a defect.** `BackfillAnnouncementProvenance` inserts it
at `COALESCE(MIN(SequenceNumber), 2) - 1` on purpose: v1.8.3 predates `System_AppVersion` entirely, so every
row the table can already hold was written by a later build, and appending it would make "the version that
ran last" answer 1.8.3 on a machine that has since run newer ones.

**This expectation requires the running version to differ from `1.8.3`**, which it does from milestone
start (see `docs/workflow/checklist.md`'s *Version during development*). While the two were equal,
`RecordCurrentAsync` found the migration's own `Quotinator.Api | 1.8.3` row and appended nothing, so the
running build never appeared.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `cd33b5b0`, with `quotinator:pre-312` built
from `e94e9c82`.

| Stage | Reading |
|---|---|
| Released `1.8.3` | Healthy; `System_AppVersion` does not exist |
| Intermediate build | Healthy; one row, `1.8.3`; reading `Application` fails with `no such column: Application` |
| Current build | `healthy`; no `no such column` and no exception logged; both databases intact, no difference; `migrationapplied` notifications present; the malformed upload `422`, its `JsonReaderException` logged |
| Version history | `Quotinator.Api 1.8.3 0`, `NULL 1.8.3 1`, `Quotinator.Api 1.9.0-alpha 2` |

## Canary

Build `a15f3168^`, the commit before #312's fix *read the version after migrations*, under a distinct tag,
and run steps 1 to 4 with that image in step 4's `reenter` in place of `quotinator:local`. Step 4 must fail:
the build reads `System_AppVersion.Application` before migrating, against exactly the state step 3 proved,
and does not reach `healthy`.

Steps 2 and 3 are the control inside every run: they prove the starting state from what its schema can and
cannot answer, so a step 4 that passes did so from the state that broke.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-11-08.** Against `719568d1` (`a15f3168^`), as `quotinator:canary-03`:
steps 2 and 3 read as in every run, `no such table` and then `no such column: Application`. In step 4 the
container never answered: `reenter --wait-listening` gave up, `wait-field` read nothing, `docker inspect`
read `false` with exit code `139`, and the log held one `no such column` line.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-03 --bind C:\repos\Quotinator\.claude\temp\qt-notif-03-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-03-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-03-out
docker rmi quotinator:pre-312
git worktree remove --force C:\repos\Quotinator\.claude\temp\wt-pre-312
```

**Expected:** the container, both directories, the image and the worktree are gone.
