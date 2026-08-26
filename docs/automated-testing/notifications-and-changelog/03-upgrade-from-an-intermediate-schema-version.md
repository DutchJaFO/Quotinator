# Upgrading from an intermediate schema version, not just the last release

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior state is not an image at all but a **hand-built
intermediate**: the published `ghcr.io/dutchjafo/quotinator:1.8.3` tag creates the released baseline,
then one migration is applied by hand on top of it. Two containers of this test's own share one
bind-mounted directory — `qt-notif-03-183` (the released image, **no published port**) and
`qt-notif-03-current` (the current build, publishing `18503`) — with the promotion applied from the
host in between.

**This exists because upgrading from the last release alone missed a startup-killing bug.** The
released-database path starts from v1.8.3, where `System_AppVersion` does not exist at all — so a
pre-migration read of it hits the missing-table path and returns null. A database at data **v4 or v5**
is a different state entirely: the table exists but the columns a later migration adds do not. That
state crashed startup with `no such column: Application`, and only a T1 run on a real dev database
exposed it.

**Whenever a migration adds a column to a table that startup reads before migrating, verify the
intermediate state as well as the released one.** ADR 009 mandates the last *released* schema; that is
a floor, not a ceiling, and unreleased intermediate versions exist on every developer machine.

The intermediate state is built by hand, from the released image plus the migrations in between.

## Determinism

**This is the one place version numbers are unavoidable.** The scenario's whole subject is a database
sitting *between* two schema versions, so it has to name which one — unlike every other test here,
where a number would be an incidental assertion. The numbers below **describe the state being
constructed, not an expected outcome**, and need re-deriving whenever migrations are consolidated.

The tell that they have gone stale: the current build logs no pending migrations at all, or fails with
a "table already exists" error instead of starting. Both mean the hand-built state no longer sits where
this scenario needs it.

- The released container publishes no port and waits on its own log.
- The current build's wait terminates on **either** outcome — ready or unhandled exception — so a
  genuine crash fails the test rather than hanging it.
- **As elsewhere, assert that replay completed — never the migration count or the version numbers.**
- **The promotion is a write, so it uses `execute-sql.csx`, not `DbInspector`.** DbInspector is
  `Mode=ReadOnly` by design and cannot perform it — **do not "fix" the tool to allow writes.** Both run
  on the host against the bind directory, which is an absolute Windows path built from `$PWD` so that
  nothing translates it into a different directory on the way.

## Steps

### 1. Create the released baseline

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-03-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-03-183 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
while (-not (docker logs qt-notif-03-183 2>&1 | Select-String -SimpleMatch 'Quotinator ready')) { Start-Sleep 1 }
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-03-183 --bind $dataDir
```

**Expected:** the released image reaches `Quotinator ready`, leaving a v1.8.3 database in `$dataDir`.

**On failure:** if it never reaches `Quotinator ready`, there is no released baseline to promote — the
hand-applied SQL below would run against an empty or absent file and the current build would then be
starting from a state nobody constructed. Stop.

### 2. Hand-apply the migration that first creates `System_AppVersion`

One step past the baseline, plus a row the later column-adding migration must not destroy:

```powershell
dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" --sql @'
CREATE TABLE IF NOT EXISTS System_AppVersion (
    Id TEXT NOT NULL PRIMARY KEY, Version TEXT NOT NULL, DateCreated TEXT NOT NULL,
    DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0);
INSERT INTO System_AppVersion (Id, Version, DateCreated)
VALUES (lower(hex(randomblob(16))), '1.8.4', '2026-08-15 20:00:00');
INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (4, '2026-08-15 20:00:00');
'@
```

**Expected:** the script reports rows affected with no error, leaving the database at the intermediate
state — the table present, the pre-existing `1.8.4` row written, and the schema counter promoted.

**On failure:** a SQL error means the intermediate state was never built, and the current build
would then be upgrading the plain released baseline instead — which is the path
[`02-notification-metadata-and-provenance.md`](02-notification-metadata-and-provenance.md) already
covers, not this one. Stop.

### 3. Start the current build against that intermediate state

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-03-current --port 18503 `
  --image quotinator:local --bind $dataDir --no-wait

while (-not (docker logs qt-notif-03-current 2>&1 | Select-String -SimpleMatch 'Quotinator ready', 'Unhandled exception')) { Start-Sleep 1 }
docker logs qt-notif-03-current 2>&1 | Select-String -SimpleMatch 'no such column', 'Unhandled', 'pending', 'schema updated', 'Quotinator ready'
```

**Expected:** logs `applying … pending "Data" migration(s)`, then `schema updated`, and reaches
`Quotinator ready`.

**Must not log `no such column` or `Unhandled exception`.** Before the fix this terminated the process
during startup, *after* the changelog database had already initialised — so a partial, healthy-looking
log prefix is not evidence of a successful start. Check for `Quotinator ready` explicitly.

The wait matches **either** outcome, so a genuine crash ends the loop and fails at the assertion rather
than hanging on a container that will never become ready.

**On failure:** no pending migrations at all, or a "table already exists" error, means the hand-built
state no longer sits where this scenario needs it (see Determinism) — the numbers need re-deriving, not
the build investigating. Stop.

### 4. Read the version history

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber"
(Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText
```

**Expected:** three rows, in this order —

| Application | Version | SequenceNumber | Written by |
|---|---|---|---|
| `Quotinator.Api` | `1.8.3` | `0` | the provenance migration, deliberately below the minimum |
| *(NULL)* | `1.8.4` | `1` | the hand-built fixture, an unreleased build |
| `Quotinator.Api` | *(the version printed beneath)* | `2` | the running build, appended |

The pre-existing `1.8.4` row survives with its `Application` still `NULL`, and the current version is
**appended** rather than replacing it.

**The `1.8.3` row at sequence `0` is correct, not a defect.** `BackfillAnnouncementProvenance` inserts
it at `COALESCE(MIN(SequenceNumber), 2) - 1` on purpose: v1.8.3 predates `System_AppVersion` entirely,
so every row the table can already hold was written by a later build, and appending it would make "the
version that ran last" answer 1.8.3 on a machine that has since run newer ones. This step listed only
two rows until #339's full run and read the third as a failure.

**This expectation requires the development version to differ from `1.8.3`**, which it does from
milestone start — see `docs/workflow/checklist.md`'s *Version during development*, and which is why the
file is read in the same block rather than a value being written here. While the two were equal,
`RecordCurrentAsync` found the migration's own `Quotinator.Api | 1.8.3` row and appended nothing, so the
running build never appeared and `SelectMostRecent` answered the `1.8.4` fixture row. Measured both
ways: `1.8.3` produced no third row; `1.9.0-alpha` produced the table above.

## Observed effect

Well established for the failure: `no such column: Application`, terminating startup after the
changelog database had already initialised — which is what made the log look healthy up to the point it
died.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-03-183 --bind $dataDir
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-03-current --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```

`qt-notif-03-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
