# Reset wipes the entire database and does not reseed

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #156

## Preconditions

The content Reset has to remove is the test's own: step 2 imports [one quote](fixtures/reset-quote.json)
through `POST /api/v1/import`, so a broken import blocks this document. That is the index's second honest
resolution for a precondition, and it is named here so a blocked run reads as that.

**This test destroys the database it runs against.** That is its subject, not a side effect.

Reset drops the *entire* database (there is no `System_`/`Import_`/`Audit_` protected-table concept)
and rebuilds via the baseline path, reversing #141's preserve-on-reset behaviour.

## Determinism

- **Waits for health, not a duration.**
- **Every pre-Reset count is asserted above what it must become** before the Reset call. Asserting a table
  becomes empty proves nothing if it already was.
- **The post-Reset audit count is exactly `1`, not `0`.** Reset writes its own self-trace row
  (`Operation: Reset`) into the freshly rebuilt `Audit_Entry` table immediately after wiping it, the
  same pattern `DELETE /admin/audit` uses for its `Purged` trace. Expecting `0` here is the easy mistake and
  would report a false failure.
- **No count of bundled quotes appears anywhere.** The total is only asserted to be above `0` before Reset and
  exactly `0` after it, and the one quote this document names is its own.

## Explicitly not covered here

**That `preserveSchemaVersion=true` keeps the pre-Reset rows of both schema-version counters.** A database
created fresh takes the consolidated baseline path and records one collapsed row per counter, so it has no
granular history to preserve, and a counter rebuilt from scratch reads the same as one preserved. This document's
environment cannot tell them apart. `DatabaseInitializerTests.ResetAsync_PreserveSchemaVersionTrue_KeepsExistingConsumerVersionRows`
and `...AlsoKeepsExistingDataVersionRows` establish it against real SQLite by inserting a marker row each. What
step 9 establishes live is that the flag answers `200` and still wipes the content.

There is no live check for the `SeedSystemContentAsync` extension point. No real system or reference
table exists in production yet, and it is proven only via test-only fixtures, see #156's plan doc, so
nothing observable changes in a running container for that part.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-db-03-out
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03 --port 18303 --image quotinator:local
```

**Expected:** the app reports healthy, so the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import this test's own quote

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import --method POST --file docs/automated-testing/database-lifecycle/fixtures/reset-quote.json --expect 200 --status --body-out .claude/temp/qt-db-03-out/import.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/import.json --equals summary.imported=1 --equals summary.errors=0
```

**Expected:** the quote is imported, with no errors.

### 3. Record the starting state

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/version --expect 200 --status --body-out .claude/temp/qt-db-03-out/version-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/version-before.json --greater-than database.quotes=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18303/api/v1/quotes/search?q=Xyzzyreset&field=source" --expect 200 --status --body-out .claude/temp/qt-db-03-out/search-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/search-before.json --equals status=Ok --equals totalMatching=1
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out .claude/temp/qt-db-03-out/audit-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/audit-before.json --greater-than totalCount=1
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out .claude/temp/qt-db-03-out/actions-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/actions-before.json --greater-than totalCount=0
```

**Expected:** quotes exist and the fixture's quote is findable, the audit table holds more than the one row
Reset will leave, and the import left actions.

**On failure:** stop. Each of these is what a later step asserts has gone, and a table that is already empty
would pass that assertion without Reset doing anything.

### 4. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18303/api/v1/admin/database/reset --expect 200 --status --body-out .claude/temp/qt-db-03-out/reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** `200` with every row count `0` and no import reports. No reimport happens.

### 5. Read the quote count after Reset

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/version --expect 200 --status --body-out .claude/temp/qt-db-03-out/version-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/version-after.json --equals database.quotes=0
```

**Expected:** `0`, read from a separate endpoint rather than from Reset's own report of itself.

### 6. Read the audit trail after Reset

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out .claude/temp/qt-db-03-out/audit-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/audit-after.json --equals totalCount=1 --equals items.0.operation=Reset
```

**Expected:** exactly one row, Reset's own self-trace. The audit trail is wiped along with everything else, no
longer surviving Reset the way it did before #156.

### 7. Confirm the import's own traces have gone

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out .claude/temp/qt-db-03-out/actions-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/actions-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18303/api/v1/quotes/search?q=Xyzzyreset&field=source" --expect 200 --status --body-out .claude/temp/qt-db-03-out/search-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/search-after.json --equals status=NoResults
```

**Expected:** no import action remains and the fixture's quote cannot be found.

### 8. Confirm the empty database degrades rather than failing

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes/random --expect 200 --status --body-out .claude/temp/qt-db-03-out/random.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/random.json --equals status=NoResults --length-equals items=0
```

**Expected:** `200`, `status=NoResults` and no items, not `503`, and not real quote data.

### 9. Reset again with `preserveSchemaVersion=true`

A second import puts content back, so this Reset has something to wipe.

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import --method POST --file docs/automated-testing/database-lifecycle/fixtures/reset-quote.json --expect 200 --status --body-out .claude/temp/qt-db-03-out/import-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/import-again.json --equals summary.imported=1
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18303/api/v1/admin/database/reset?preserveSchemaVersion=true" --expect 200 --status --body-out .claude/temp/qt-db-03-out/reset-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/reset-preserve.json --equals quotes=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/version --expect 200 --status --body-out .claude/temp/qt-db-03-out/version-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-db-03-out/version-preserve.json --equals status=ready --equals database.quotes=0
```

**Expected:** the import succeeds, and the flag still wipes it: `200`, no quotes, and the application still ready.

### 10. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18303/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far, both Resets included. The malformed upload then
answers `422` and its `JsonReaderException` is logged, which is the positive control: the same channel that
read zero can see an exception, so the zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Reading | Before Reset | After Reset |
|---|---|---|
| `GET /api/v1/version`, `database.quotes` | `795` | `0` |
| `search?q=Xyzzyreset&field=source` | `status=Ok`, `totalMatching=1` | `status=NoResults` |
| `GET /api/v1/admin/audit`, `totalCount` | `42` | `1`, with `operation=Reset` |
| `GET /api/v1/import/actions`, `totalCount` | `2` | `0` |
| `GET /api/v1/quotes/random` | not read | `200`, `status=NoResults`, no items |

`POST /api/v1/admin/database/reset` answered `200`, with all ten row counts `0` and no reports. Importing the
fixture again and resetting with `preserveSchemaVersion=true` answered `200` with `quotes=0`, and
`/api/v1/version` then read `status=ready` and `database.quotes=0`.

The before-counts are this run's reading, not expectations: only the comparisons in step 3 are asserted.

**No exception was logged before the malformed upload**, both Resets included. That upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#156](https://github.com/DutchJaFO/Quotinator/issues/156)'s first code
change, under a distinct tag, and run steps 2 to 8 against it.

Step 5 must read a quote count above `0` rather than `0`. That is exactly the behaviour #156 removed:
`OnResetAsync` called `SeedIfEmptyInternalAsync`, so a reset reimported the bundled content on the caller's
behalf, and `CLAUDE.md`'s *Endpoint side-effect policy* records it as the defect. Step 6 must read more than
Reset's own single self-trace row for the same reason.

Step 3 is expected to pass there, and it is the control: both builds produce a normally seeded install with
the fixture imported, so step 5's reading is about what Reset did rather than about what the container started
with.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-03
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-db-03-out
```

**Expected:** the container, its volume and the output directory are gone.
