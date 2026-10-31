# Reset wipes the entire database and does not reseed

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #156

## Preconditions

**Beyond the profile.** Both containers run with `Quotinator__IncludeDefaultSources=false`, so nothing bundled
is in the database and everything Reset has to remove is this document's own
[one quote](fixtures/reset-quote.json). Variant A also sets `Quotinator__AutoPurgeUserImportActions=false`: by
default a seeded file's import actions are purged straight after the seed, and an action list that is already
empty leaves Reset nothing to be seen wiping.

**That quote arrives by two routes, one per variant, in its own container:**

| Variant | Port | How the quote arrives | What only this variant can show |
|---|---|---|---|
| A, pre-filled | `18303` | A file in `{dataDir}/imports/`, seeded at first boot | Reset does not reseed: the file is still there for a reseed to read |
| B, API import | `19303` | `POST /api/v1/import` after startup | `preserveSchemaVersion=true` still wipes content |

**Two variants, so that a broken route blocks only its own.** Reset is the subject here, not either route. If one
variant cannot establish its quote, record it as blocked, naming the route, and run the other. What is lost is
that variant's column above. Each route has tests of its own, so this document asserts nothing about how a quote
arrived, only that it is present.

**This test destroys the database it runs against.** That is its subject, not a side effect.

Reset drops the *entire* database (there is no `System_`/`Import_`/`Audit_` protected-table concept)
and rebuilds via the baseline path, reversing #141's preserve-on-reset behaviour.

## Determinism

- **Waits for health, not a duration.**
- **Every count is exact where this document owns the content.** One quote and one source before Reset, none
  after. A bundled file cannot change them.
- **Counts are read from `/quotes` and `/masterdata/sources`, not `/api/v1/version`.** An import does not refresh
  the figures `/version` reports, so it can read `0` beside a quote that `/quotes` returns.
- **The post-Reset audit count is exactly `1`, not `0`.** Reset writes its own self-trace row
  (`Operation: Reset`) into the freshly rebuilt `Audit_Entry` table immediately after wiping it, the
  same pattern `DELETE /admin/audit` uses for its `Purged` trace. Expecting `0` here is the easy mistake and
  would report a false failure.
- **The audit trail and the import actions are only asserted to be non-empty before Reset.** How many rows a
  route writes belongs to that route's tests, and this document needs only that there is something to wipe.
- **A count taken after Reset passes on an empty table**, which is why each is first asserted non-empty in the
  same variant, before the Reset call.

## Explicitly not covered here

**That `preserveSchemaVersion=true` keeps the pre-Reset rows of both schema-version counters.** A database
created fresh takes the consolidated baseline path and records one collapsed row per counter, so it has no
granular history to preserve, and a counter rebuilt from scratch reads the same as one preserved. This document's
environment cannot tell them apart. `DatabaseInitializerTests.ResetAsync_PreserveSchemaVersionTrue_KeepsExistingConsumerVersionRows`
and `...AlsoKeepsExistingDataVersionRows` establish it against real SQLite by inserting a marker row each.

There is no live check for the `SeedSystemContentAsync` extension point. No real system or reference
table exists in production yet, and it is proven only via test-only fixtures, see #156's plan doc, so
nothing observable changes in a running container for that part.

## Steps

**Variant A, steps 1 to 5: the quote is pre-filled.**

### 1. Create the environment, with the quote waiting to be seeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-03a-data --delete C:\repos\Quotinator\.claude\temp\qt-db-03-out --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-quote.json C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\reset-quote.json --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-imports-manifest.json C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\manifest.json
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03a --port 18303 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-db-03a-data --env Quotinator__IncludeDefaultSources=false --env Quotinator__AutoPurgeUserImportActions=false
```

**Expected:** the app reports healthy, so the seed has finished.

**On failure:** this variant is blocked. Run its cleanup and go to variant B.

### 2. Record the starting state

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-before.json --equals totalCount=1 --equals items.0.id=9e3a6c02-5b7d-4f49-9c3a-8d024f912c01
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-before.json --equals totalCount=1
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-before.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-before.json --greater-than totalCount=0
```

**Expected:** the fixture's quote is the only quote, its source the only source, and the audit trail and import
actions both hold something to wipe.

**On failure:** the seed did not establish the content, so this variant is blocked. Run its cleanup and go to
variant B.

### 3. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18303/api/v1/admin/database/reset --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** `200` with every row count `0` and no import reports.

### 4. Confirm it was wiped, and that the waiting file was not reseeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\reset-quote.json
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-after.json --equals totalCount=1 --equals items.0.operation=Reset
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes/random --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-random.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-random.json --equals status=NoResults --length-equals items=0
```

**Expected:** the imports file is still on disk, and nothing was read back from it: no quote, no source, no import
action. The audit trail holds only Reset's own trace, and the empty database answers `200` with `status=NoResults`
rather than `503`.

### 5. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03a --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03a --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far. The malformed upload then answers `422` and its
`JsonReaderException` is logged, which is the positive control: the same channel that read zero can see an
exception, so the zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

**Variant B, steps 6 to 11: the quote is imported through the API.**

### 6. Create the environment and import the quote

```
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03b --port 19303 --image quotinator:local --env Quotinator__IncludeDefaultSources=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-quote.json --expect 200 --status
```

**Expected:** the app reports healthy and the import answers `200`.

**On failure:** this variant is blocked. Run its cleanup and report which route blocked it.

### 7. Record the starting state

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-before.json --equals totalCount=1 --equals items.0.id=9e3a6c02-5b7d-4f49-9c3a-8d024f912c01
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-before.json --equals totalCount=1
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-before.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-before.json --greater-than totalCount=0
```

**Expected:** the same starting state as step 2, reached by the other route.

**On failure:** the import did not establish the content, so this variant is blocked. Run its cleanup and report
which route blocked it.

### 8. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/admin/database/reset --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** `200` with every row count `0` and no import reports.

### 9. Confirm it was wiped

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-after.json --equals totalCount=1 --equals items.0.operation=Reset
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-after.json --equals totalCount=0
```

**Expected:** no quote, no source, no import action, and only Reset's own audit trace.

### 10. Reset again with `preserveSchemaVersion=true`

A second import puts content back, so this Reset has something to wipe.

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-quote.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-again.json --equals totalCount=1
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:19303/api/v1/admin/database/reset?preserveSchemaVersion=true" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset-preserve.json --equals quotes=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-preserve.json --equals totalCount=0
```

**Expected:** the quote is back after the import, and the flag still wipes it.

### 11. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03b --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03b --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** as in step 5, both Resets included.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Reading | A, before | A, after | B, before | B, after |
|---|---|---|---|---|
| `/quotes`, `totalCount` | `1` | `0` | `1` | `0` |
| `/masterdata/sources`, `totalCount` | `1` | `0` | `1` | `0` |
| `/admin/audit`, `totalCount` | `4` | `1`, `operation=Reset` | `2` | `1`, `operation=Reset` |
| `/import/actions`, `totalCount` | `2` | `0` | `2` | `0` |

Both Resets answered `200`, with all ten row counts `0` and no reports. In variant A the imports file was still on
disk afterwards, nothing had been read back from it, and `quotes/random` answered `200` with `status=NoResults` and
no items. In variant B a second import brought the quote back, and `preserveSchemaVersion=true` answered `200` and
left `/quotes` at `0`.

**No exception was logged in either variant before its malformed upload**, every Reset included. Each upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#156](https://github.com/DutchJaFO/Quotinator/issues/156)'s first code
change, under a distinct tag, and run variant A's steps 1 to 4 against it.

Step 4 must read `totalCount=1` from `/quotes` rather than `0`. That is exactly the behaviour #156 removed:
`OnResetAsync` called `SeedIfEmptyInternalAsync`, so a reset reimported content on the caller's behalf, and
`CLAUDE.md`'s *Endpoint side-effect policy* records it as the defect. The waiting imports file is what that
reseed would read, and it is why variant A exists: variant B has nothing on disk to reseed from, so it cannot
show this. Step 4 must also read more than Reset's own single audit row, since the trail survived Reset then.

Step 2 is expected to pass there, and it is the control: both builds seed the same file, so step 4's reading is
about what Reset did rather than about what the container started with.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-03a --bind
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-03b
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-03a-data --delete C:\repos\Quotinator\.claude\temp\qt-db-03-out
```

**Expected:** both containers, variant B's volume, variant A's data directory and the output directory are gone.
