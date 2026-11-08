# Audit export, date-range discovery, and conflict-resolution data auto-purge

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #249

## Preconditions

**Beyond the profile.** This test runs **three containers of its own**, because each needs a different
configuration and a configuration is fixed at start: `qt-db-02-default` (`18302`) on the profile's
settings, `qt-db-02-noautopurge` (`20302`) with bundled auto-purge off, and `qt-db-02-cap` (`19302`) with
an export cap of one row. The audit activity the export and date-range steps read is produced by the
bundled seed, so nothing needs importing first; the purge-on-import and scoped-clear steps import files
of this test's own.

**One is a deliberate departure from the profile's settings:** `qt-db-02-noautopurge` sets
`Quotinator__AutoPurgeBundledImportActions=false`, where Fresh pins the application default `true`.
Comparing the two is the point of the test, which is why both appear explicitly rather than one being
left to a default.

## Determinism

- **Everything is read through the API**, so no step copies a database or stops a container to read
  one. The remaining import actions come from `GET /import/actions`, and the purge traces and change rows
  from `GET /admin/audit/export`, narrowed with `--where`.
- **The purge traces equal the number of seed batches**, one per batch. The count is derived from
  `GET /import/batches?type=seed` in the same run rather than fixed: the bundled file set changes, and
  this is a relationship, not a prediction.
- **Each configuration gets a fresh container with no prior data.** The auto-purge-off run in particular
  is meaningless against a volume where the on-by-default run already purged.
- **The purge-on-import step imports this test's own quotes**
  (`database-lifecycle/fixtures/audit-purge-purged.json` and `audit-purge-kept.json`), present in no
  bundled file, so each import is an Add with nothing to decide, and the two batches differ only in
  `purgeOnSuccess`.
- **The scoped-clear step imports the suite's conflict fixture with `newest-wins`**, which re-states a
  bundled quote with different text, so applying it writes real change rows for the clear to leave alone.
- **The log is read before each container is removed.**

## Steps

### 1. Start the default container, both auto-purge settings on

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-02-out --create-directory C:\repos\Quotinator\.claude\temp\qt-db-02-out
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-default --port 18302 --image quotinator:local
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-default --pattern "Quotinator ready" --exactly 1
```

**Expected:** the app reports healthy and the log records exactly one completed start.

**On failure:** the audit activity every check below reads is produced by that seeding. A container
that has not finished has nothing to export or date-range, and an empty result would read as an
endpoint defect. Stop.

### 2. Discover the audit date range, with no key

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/date-range --no-key --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\range-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\range-seeded.json --not-empty earliestDate --not-empty latestDate
```

**Expected:** `200` with both dates set, from the bundled seed's own activity. Called with `--no-key`
deliberately: no `X-Api-Key` is required here, matching `GET /admin/audit`, and sending one would leave
that untested.

### 3. Export the audit trail as a downloaded file

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/export --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-seeded.json --headers-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-seeded-headers.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-seeded-headers.json --contains "Content-Disposition=attachment; filename=" --contains Content-Disposition=quotinator-audit-export- --contains Content-Disposition=.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-seeded.json --present entries.0 --present changes.0
```

**Expected:** every command exits `0`: the response is an attachment named
`quotinator-audit-export-….json`, and both `entries` and `changes` are non-empty after a fresh seed.

### 4. Every bundled batch's actions are purged, each leaving a trace

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/import/actions?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-default.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/import/batches?type=seed&pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\batches.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\batches.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-seeded.json --where entries:tableName=Import_Action --where entries:operation=Purged --length-equals-json entries=C:\repos\Quotinator\.claude\temp\qt-db-02-out\batches.json#totalCount
```

**Expected:** every command exits `0`: no import action remains, and the export holds one `Purged` trace
on `Import_Action` per seed batch, even though the actions themselves are gone.

**The `0` remaining actions is the zero-failures assertion of this test**: nothing left `Pending`,
`Blocked` or `Stale` after a bundled seed. Step 5 is its positive control: the same seed with auto-purge
off leaves actions to count.

**The traces are what distinguish *purged* from *never staged*,** since an empty action list is produced
by both.

### 5. The same seed with bundled auto-purge off keeps its actions

```
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-noautopurge --port 20302 --image quotinator:local --env Quotinator__AutoPurgeBundledImportActions=false
dotnet script scripts/testing/http.csx -- --url "http://localhost:20302/api/v1/import/actions?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-noautopurge.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-noautopurge.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:20302/api/v1/admin/audit/export --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-noautopurge.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-noautopurge.json --where entries:tableName=Import_Action --where entries:operation=Purged --length-equals entries=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-noautopurge --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-noautopurge
```

**Expected:** every command exits `0`: actions remain and no purge trace was written, matching the
pre-#249 behaviour, and nothing was thrown.

**Step 4's trace count is this `0`'s positive control**, read through the same filter on the same kind of
export.

### 6. Reject an export above the row-count cap

```
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-cap --port 19302 --image quotinator:local --env Quotinator__AdminAuditExportMaxRows=1
dotnet script scripts/testing/http.csx -- --url http://localhost:19302/api/v1/admin/audit/export --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-cap --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-cap
```

**Expected:** `422`, never a silently truncated file. Step 3 shows a fresh seed produces far more than
one combined row.

### 7. An import with `purgeOnSuccess=true` cannot be reversed

```
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18302/api/v1/import?purgeOnSuccess=true" --file docs/automated-testing/database-lifecycle/fixtures/audit-purge-purged.json --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-purged.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-purged.json --not-empty batchId
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18302/api/v1/import/actions?batchId={batchId}&pageSize=0" --value batchId=C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-purged.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-purged-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-purged-url.txt --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-purged.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-purged.json --equals totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18302/api/v1/import/actions/reverse?batchId={batchId}" --value batchId=C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-purged.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-db-02-out\reverse-purged-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-db-02-out\reverse-purged-url.txt --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-default --pattern "\[Runtime - Exception\]" --exactly 0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-default --mark C:\repos\Quotinator\.claude\temp\qt-db-02-out\after-reverse.mark
```

**Expected:** every command exits `0`: the import applies, its batch keeps no actions, reversing it, the
most recent batch, is refused `422` because there is nothing left to reverse, and the refusal throws
nothing ([ADR 022](../../architecture-decisions/022-exceptions-only-for-undetectable-conditions.md)).

**The exception count fails today, and is tracked.** The refusal is reached by throwing
`ImportBatchStateException` for a condition the service has just checked, which ADR 022 forbids;
[#404](https://github.com/DutchJaFO/Quotinator/issues/404) converts it to a reported refusal, and
[#475](https://github.com/DutchJaFO/Quotinator/issues/475) is why the one exception is counted twice.
The mark taken after it lets step 10 assert that nothing *else* throws.

### 8. Positive control: the same import without the purge can be reversed

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18302/api/v1/import --file docs/automated-testing/database-lifecycle/fixtures/audit-purge-kept.json --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-kept.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18302/api/v1/import/actions?batchId={batchId}&pageSize=0" --value batchId=C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-kept.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-kept-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-kept-url.txt --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-kept.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\actions-kept.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18302/api/v1/import/actions/reverse?batchId={batchId}&preview=true" --value batchId=C:\repos\Quotinator\.claude\temp\qt-db-02-out\import-kept.json#batchId --out C:\repos\Quotinator\.claude\temp\qt-db-02-out\reverse-kept-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-db-02-out\reverse-kept-url.txt --expect 200 --status
```

**Expected:** every command exits `0`: the batch keeps its actions, and a reversal of it previews as
possible. `preview=true` changes nothing.

**This is what makes step 7's `0` and `422` the purge's doing**: an identical import, differing only in
`purgeOnSuccess`, keeps its actions and can be reversed.

### 9. An unscoped clear empties both audit tables, leaving only its own trace

```
dotnet script scripts/testing/http.csx -- --method DELETE --url http://localhost:18302/api/v1/admin/audit --expect 204 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/export --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-cleared.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-cleared.json --length-equals entries=1 --equals entries.0.operation=Purged --length-equals changes=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/date-range --no-key --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\range-cleared.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\range-cleared.json --equals-field earliestDate=latestDate --equals-json earliestDate=C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-cleared.json#entries.0.performedAt
```

**Expected:** every command exits `0`: after the clear the export holds one entry, the clear's own
`Purged` trace, and no change rows; and the date range is that one entry's moment, not the seed's
earlier activity.

**The unscoped clear empties `Audit_Change` too**, not only `Audit_Entry`: the defect T1 found was an
export and date range still showing data after a clear.

### 10. A table-scoped clear leaves `Audit_Change` untouched

```
dotnet script scripts/testing/stage-import-conflict.csx -- --imports C:\repos\Quotinator\.claude\temp\qt-db-02-out\fixture
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18302/api/v1/import --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\fixture\conflicting.json --duplicate-resolution newest-wins --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/export --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json --length-equals changes=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json --where entries:tableName=Import_Batch --present entries.0
dotnet script scripts/testing/http.csx -- --method DELETE --url "http://localhost:18302/api/v1/admin/audit?table=Import_Batch" --expect 204 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18302/api/v1/admin/audit/export --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-after-scoped.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-after-scoped.json --length-equals changes=1 --equals-json changes.0.id=C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json#changes.0.id --equals-json changes.0.occurredAt=C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json#changes.0.occurredAt --equals-json changes.0.oldValue=C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json#changes.0.oldValue --equals-json changes.0.newValue=C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-before-scoped.json#changes.0.newValue
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-02-out\export-after-scoped.json --where entries:tableName=Import_Batch --length-equals entries=1 --equals entries.0.operation=Purged
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-02-default --since-mark C:\repos\Quotinator\.claude\temp\qt-db-02-out\after-reverse.mark --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: the conflict import writes one change row and entries on
`Import_Batch`; a clear scoped to `Import_Batch` replaces those entries with its own single `Purged`
trace on that table, as the unscoped clear does, and leaves the change row as it was, its id, moment and
values unchanged; and nothing has thrown since step 7.

**The non-empty before-readings are what make this an assertion at all.** Run straight after step 9 the
change list is empty, and "unchanged" then holds equally well when a scoped clear wrongly wipes the
table, which is how this step read until #339's full run. The `Import_Batch` entries going is the scoped
clear having done something, so its leaving `changes` alone is not a clear that did nothing.

**The clear is scoped to `Import_Batch` because that is where the import's entries are.** Applying the
conflict writes a change row for the quote but no `Audit_Entry` row on `Quotinator_Quote`, so the
earlier form's clear scoped to that table removed nothing, and could not have shown it did anything.

**The change row is compared field by field, not whole**, because the export does not read a row's
`dateCreated` from the table: `Sql.SystemChangeLog.SelectInRange` selects no `RecordBase` column, so the
field holds the moment of the export, and two exports of one unchanged row differ there.

**Until #411 this step re-imported the curated file**, which since #373 changes nothing already stored
and so writes no change rows. The conflict fixture re-states a bundled quote with different text, so
applying it is a real change.

## Observed effect

**Measured 2026-11-08** on `feature/notification-system` (1.9.0-alpha), as written, every command
exiting `0` except step 7's exception count, the known failure tracked by #404:

| Step | Observed |
|---|---|
| 1 | `1` start logged |
| 2 | `earliestDate = 2026-11-08T20:57:52.08`, `latestDate = 2026-11-08T20:58:10.59` |
| 3 | `attachment; filename="quotinator-audit-export-20261108205814.json"`; entries and changes both present |
| 4 | `0` remaining actions; `5` seed batches; `5` `Purged` traces on `Import_Action` |
| 5 | `1507` remaining actions with auto-purge off; `0` traces; `0` exceptions |
| 6 | `422`; `0` exceptions |
| 7 | the purged batch `0` actions, its reversal `422`; **`2` exception lines, expected `0`** |
| 8 | the kept batch `2` actions, its reversal previewed `200` |
| 9 | `204`; one entry, `Purged`, `0` changes; the date range one moment, `20:59:25.84`, equal to that entry's |
| 10 | `1` change row; two `Import_Batch` entries before the scoped clear, its own `Purged` trace after; the change row's id, moment and values unchanged; `0` exceptions since step 7 |

The header carries no row count; the export's own `entries` and `changes` are what is read.

**The conversion found three things the earlier form could not see**, because it printed figures rather
than asserting on them:

- **Step 7's refusal throws.** `ReverseBatchAsync` checks that the batch has no actions and then throws
  `ImportBatchStateException`, which reaches the response as `422` and is logged twice as a thrown
  exception. ADR 022 forbids throwing for a condition just checked;
  [#404](https://github.com/DutchJaFO/Quotinator/issues/404) and
  [#475](https://github.com/DutchJaFO/Quotinator/issues/475) track it, and step 7 fails until #404 lands.
- **The earlier scoped clear could not have done anything.** It cleared `Quotinator_Quote`, where the
  conflict import writes no `Audit_Entry` row at all, so "the change rows are unchanged" held whether or
  not a scoped clear works. Step 10 now clears `Import_Batch`, where the import's entries are.
- **The export does not report a row's stored `RecordBase` dates.** Neither export query selects
  `DateCreated`, `DateModified`, `DateDeleted` or `IsDeleted`, so every row's `dateCreated` is the moment
  of the export: every entry of one export carries the same value, and one unchanged change row read
  twice differs there. Step 10 compares the change row field by field for that reason.

**The unscoped-clear behaviour was found live during T1**: date-range and export kept showing data
after a clear, because the endpoint had only ever cleared `Audit_Entry`, even though #249 treats both
tables as one combined concern everywhere else.

## Canary

**The purge half is controlled inside the run**, by steps 4 and 5, and 7 and 8. Steps 4 and 5 are the
same seed with `Quotinator__AutoPurgeBundledImportActions` the only difference: step 4 must read `0`
remaining actions and one trace per batch, step 5 more than `0` and no trace. Steps 7 and 8 are the same
import with `purgeOnSuccess` the only difference: one refuses its reversal, the other previews it. If
purging did nothing, each pair would read alike, and the document fails.

**The export half needs a pre-fix build.** Build the commit before
[#249](https://github.com/DutchJaFO/Quotinator/issues/249)'s first code change, under a distinct tag,
and run steps 2, 3 and 6 against it: the date-range and export endpoints do not exist there, so each
answers `404` rather than the header and the `422` cap this document asserts.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-default
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-noautopurge
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-cap
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-02-out
```

**Expected:** the three containers and the output directory are gone.
