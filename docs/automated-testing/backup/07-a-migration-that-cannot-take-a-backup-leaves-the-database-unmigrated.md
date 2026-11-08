# A migration that cannot take a backup leaves the database unmigrated, and says why

**Smoke:** no
**Environment:** Upgraded + Constrained
**Deterministic:** yes
**Traces to:** #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-07`, publishing `18387`, on a bind
directory. The prior image is the published `ghcr.io/dutchjafo/quotinator:1.8.3`, the release users
upgrade from, so the current build has migrations to apply. Before the current build starts, the backup
folder is filled to the budget, so the backup every migration takes first cannot be taken.

A migration changes the schema in a way only a backup can undo. Until #348 a failed pre-migration backup
was ignored and every pending migration ran unprotected. This proves the migration is refused instead,
that the application degrades and says which obstacle stopped it and where to read about it, that the
schema is left exactly as the prior build wrote it, and that removing the obstacle lets the same
database migrate.

## Determinism

- **The prior version is a published tag, not a milestone snapshot.** 1.8.3 is what users run today, and
  its schema is behind any build of this milestone, so pending migrations are guaranteed without editing
  a version row.
- **The obstacle is a full budget, made by one sparse file the size of the whole 1 GB default**, for the
  reason `backup/06` records: the backup taken before a migration is measured against the absolute
  ceiling, not the 90% operating quota.
- **The recorded schema version is read from the file, with no container running.** DbInspector reads
  `System_ConsumerSchemaVersion` read-only and writes it as JSON (`--json-out`). The reading after the
  refusal is compared with the reading before it (`--equals-json`), and the reading after the remedied
  start, taken from `/api/v1/version` since that start is healthy, with the same earlier reading
  (`--greater-than-json`), so no migration number appears in this document.
- **The degraded start is waited on by its `status` field**, since the startup wait page answers `503`
  too.
- **The log is read before each stop.**

## Explicitly not covered here

- **The startup contract for this state**: the process alive, every database read gated, the OpenAPI
  surface and the degraded pages answering, and the remedy applied from inside the application.
  [`startup-and-degradation/12`](../startup-and-degradation/12-a-migration-refused-for-want-of-a-backup-degrades-instead-of-crashing.md)
  provokes the same refusal to assert that. This document asserts the backup contract.

## Steps

### 1. Let the prior release create its database, and record its version

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07 --delete C:\repos\Quotinator\.claude\temp\qt-backup-07-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-07\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-07-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-07 --port 18387 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db --sql "SELECT MAX(Version) AS Recorded FROM System_ConsumerSchemaVersion" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json --greater-than 0.Recorded=0
```

**Expected:** the prior release reports healthy, nothing is thrown before the stop, and the `json` command
exits `0`: the release recorded a version above `0`.

### 2. Fill the budget, and start the current build

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db 1073741824 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db 1073741824
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18387/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health.json --equals status=unhealthy --contains "reason=Database migration refused: no backup could be taken (BudgetExceeded)." --contains reason=https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "migration refused: no backup could be taken \(BudgetExceeded\)" --at-least 1
```

**Expected:** every command exits `0`: the filler is in place, the start degrades with a reason naming the
refused migration and its obstacle and linking the Knowledgebase entry, and the refusal is logged.

**On failure:** `healthy` means the migration ran without its backup, the defect this document exists to
catch.

### 3. Confirm the degraded page links the Knowledgebase entry

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18387/#no-backup-could-be-taken" --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.png --height 1100 --script-file scripts/testing/page-scripts/knowledgebase-link.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.json --greater-than innerHeight=0 --equals href=https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md --equals "text=How to resolve this (Knowledgebase)"
```

**Expected:** the `json` command exits `0`: a non-zero viewport, and a link to the entry reading *How to
resolve this (Knowledgebase)*. `degraded-home.png` shows the startup error popup with the reason and the
link.

### 4. Confirm nothing was migrated

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db --sql "SELECT MAX(Version) AS Recorded FROM System_ConsumerSchemaVersion" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-refused.json --equals-json 0.Recorded=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
```

**Expected:** nothing thrown before the stop, and the `json` command exits `0`: the version recorded after
the refused start is the version the release recorded.

**The version is read from the file, not from the API.** A degraded start reports no `database` block
(#428), and the claim being made is about what the file holds after a refused migration, so asking the
file is what the claim actually needs. The container is gone first, so nothing holds the file while it is
read.

**On failure:** a changed version with step 2 reporting a refusal means part of the migration ran before
it was refused, which is the partial state the refusal exists to prevent.

### 5. Remove the obstacle, and confirm the same database migrates

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet script scripts/testing/http.csx -- --url "http://localhost:18387/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\quotes.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\quotes.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-07\backups "*.db" 1
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-migrated.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-migrated.json --greater-than-json database.schemaVersion=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: the restart reports healthy, quotes above `0`, exactly one file in
`backups/` (the backup the migration took first, the filler being gone), a recorded version above the one
the release recorded, and nothing thrown.

**This step is the remedy proven**, and the positive control: every step above asserts a refusal, and a
build refusing every migration would satisfy them all without this one.

## Observed effect

Run 2026-11-08 on `feature/notification-system` (1.9.0-alpha), as written, every command exiting `0`:

| Step | Observed |
|---|---|
| 1 | 1.8.3 healthy, `0` exceptions; `Recorded = 5` |
| 2 | filler `1073741824` bytes; `status = unhealthy`, reason *Database migration refused: no backup could be taken (BudgetExceeded).* with the entry's link; the refusal logged |
| 3 | `innerHeight = 1100`, the link to `no-backup-could-be-taken.md` reading *How to resolve this (Knowledgebase)* |
| 4 | `0` exceptions; `Recorded = 5`, equal to step 1's reading |
| 5 | `totalCount = 795`; one backup, `quotinatordata_v5_…Z.db`, named for the version it protects; `schemaVersion = 9`, above step 1's `5`; `0` exceptions |

The canary below ran against this document's earlier PowerShell form. The converted steps assert the
same contract at the same points, so each failure it records falls on the converted step of the same
number.

## Canary

Run 2026-09-26 against `4cfe1006`, the commit before step 8 of #348's plan, as `quotinator:canary348`,
with this document's earlier PowerShell form:

| Step | Assertion | Canary |
|---|---|---|
| 1 | the prior release records a version | passes: `before=5` |
| 2 | the start degrades, naming the obstacle and the entry | **fails**: the migration ran and the start was healthy |
| 3 | the degraded page links the entry | **fails**: no link |
| 4 | the recorded version is unchanged | **fails**: `refused=9`, migrated with no backup taken |
| 5 | the remedied start takes a backup | **fails**: `backups=0`, nothing was left to migrate |

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07 --delete C:\repos\Quotinator\.claude\temp\qt-backup-07-out
```

**Expected:** the container and both directories are gone.
