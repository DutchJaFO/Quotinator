# Reset wipes the entire database and does not reseed

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #156
**Fully green after:** [#468](https://github.com/DutchJaFO/Quotinator/issues/468): step 8 reads every `/version` count as `0` until then, because an API import does not refresh them. See the [Knowledgebase entry](../../knowledgebase/version-counts-are-stale-after-an-import.md)

## Preconditions

**Beyond the profile.** Both containers run with `Quotinator__IncludeDefaultSources=false`, so nothing bundled
is in the database. Everything Reset has to remove is this document's own [fixture](fixtures/reset-content.json),
which holds rows in every content category. Variant A also sets `Quotinator__AutoPurgeUserImportActions=false`: by
default a seeded file's import actions are purged straight after the seed, and an action list that is already
empty leaves Reset nothing to be seen wiping.

**The fixture arrives by two routes, one per variant, in its own container:**

| Variant | Port | How the content arrives | What only this variant can show |
|---|---|---|---|
| A, pre-filled | `18303` | A file in `{dataDir}/imports/`, seeded at first boot | Reset does not reseed: the file is still there for a reseed to read |
| B, API import | `19303` | `POST /api/v1/import` after startup | `preserveSchemaVersion=true` still wipes content |

**Two variants, so that a broken route blocks only its own.** Reset is the subject here, not either route. If one
variant cannot establish its content, record it as blocked, naming the route, and run the other. What is lost is
that variant's column above. Each route has tests of its own, so this document asserts nothing about how the
content arrived, only that it is present.

**This test destroys the database it runs against.** That is its subject, not a side effect.

Reset drops the *entire* database (there is no `System_`/`Import_`/`Audit_` protected-table concept)
and rebuilds via the baseline path, reversing #141's preserve-on-reset behaviour.

**A new content category changes what Reset has to be shown wiping.** When one is added, in the same work: give
the fixture rows for it, read it before and after Reset in both variants, and assert its counter in Reset's response.

`RepositoryStructureTests.TheResetDocument_CoversEveryContentCategory` fails until that is done. It takes the
counters from `DatabaseSeedSummaryResponse` and the routes from `docs/api-endpoints.md`, so it needs no list of
its own. It sees only what those two publish, so a feature that stores data with neither a counter nor a list
route is not caught by it, and still belongs here.

## Determinism

- **Waits for health, not a duration.**
- **Every category's count is exact, and every one is read before Reset.** Asserting a table becomes empty proves
  nothing if it already was. The fixture decides the counts, so a bundled file cannot change them.
- **Each category is read from its own endpoint, and `/api/v1/version` must agree with them.** Both are held to the
  fixture's counts before Reset and to `0` after it. They are separate sources: the list endpoints query the rows, and
  `/version` and Reset's response report held counters, so a counter reading `0` does not by itself show the rows have
  gone, and a list reading rows does not show the counters are right.
- **Reset's own notification is exact.** Every earlier notification goes with the wipe, and the only one left is the
  reseed recommendation Reset writes afterwards, so a count of `1` also shows the earlier ones were removed.
- **The fixture's counts are not one per entry.** Its quotes bring a source and a person the fixture does not list,
  and the same character name in the film and in the series is two characters, since nothing relates the two.
- **The post-Reset audit count is exactly `1`, not `0`.** Reset writes its own self-trace row
  (`Operation: Reset`) into the freshly rebuilt `Audit_Entry` table immediately after wiping it, the
  same pattern `DELETE /admin/audit` uses for its `Purged` trace. Expecting `0` here is the easy mistake and
  would report a false failure.
- **The audit trail and the import actions are only asserted to be non-empty before Reset.** How many rows a
  route writes belongs to that route's tests, and this document needs only that there is something to wipe.

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

**Variant A, steps 1 to 5: the content is pre-filled.**

### 1. Create the environment, with the content waiting to be seeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-db-03a-data --delete C:\repos\Quotinator\.claude\temp\qt-db-03-out --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\reset-content.json --copy C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-imports-manifest.json C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\manifest.json
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03a --port 18303 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-db-03a-data --env Quotinator__IncludeDefaultSources=false --env Quotinator__AutoPurgeUserImportActions=false
```

**Expected:** the app reports healthy, so the seed has finished.

**On failure:** this variant is blocked. Run its cleanup and go to variant B.

### 2. Record the starting state

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-before.json --equals totalCount=4
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-before.json --equals totalCount=3
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/characters --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-characters-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-characters-before.json --equals totalCount=3
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/people --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-people-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-people-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/series --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-series-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-series-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/seasons --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-seasons-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-seasons-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/universes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-universes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-universes-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/stagedirections --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-stagedirections-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-stagedirections-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/soundcues --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-soundcues-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-soundcues-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/conversations --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-conversations-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-conversations-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-before.json --greater-than totalCount=1
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-before.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/notifications --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-notifications-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-notifications-before.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-version-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-version-before.json --equals database.quotes=4 --equals database.sources=3 --equals database.characters=3 --equals database.people=2 --equals database.series=2 --equals database.seasons=2 --equals database.universes=2 --equals database.stageDirections=2 --equals database.soundCues=2 --equals database.conversations=2
```

**Expected:** every category holds exactly what the fixture gives it, `/version` reports the same counts, and the
audit trail, import actions and notifications hold something for Reset to remove.

**On failure:** the seed did not establish the content, so this variant is blocked. Run its cleanup and go to
variant B.

### 3. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18303/api/v1/admin/database/reset --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** `200` with every row count `0` and no import reports.

### 4. Confirm every category was wiped, and that the waiting file was not reseeded

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-db-03a-data\imports\reset-content.json
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-quotes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-sources-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/characters --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-characters-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-characters-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/people --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-people-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-people-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/series --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-series-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-series-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/seasons --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-seasons-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-seasons-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/universes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-universes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-universes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/stagedirections --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-stagedirections-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-stagedirections-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/masterdata/soundcues --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-soundcues-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-soundcues-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/conversations --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-conversations-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-conversations-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-audit-after.json --equals totalCount=1 --equals items.0.operation=Reset
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-actions-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/quotes/random --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-random.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-random.json --equals status=NoResults --length-equals items=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/notifications --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-notifications-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-notifications-after.json --equals totalCount=1 --equals items.0.metadataKind=reseedrecommended --equals items.0.type=actionrequired --equals items.0.isDismissed=false
dotnet script scripts/testing/http.csx -- --url http://localhost:18303/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-version-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\a-version-after.json --equals status=ready --equals database.quotes=0 --equals database.sources=0 --equals database.characters=0 --equals database.people=0 --equals database.series=0 --equals database.seasons=0 --equals database.universes=0 --equals database.stageDirections=0 --equals database.soundCues=0 --equals database.conversations=0
```

**Expected:** the imports file is still on disk, and nothing was read back from it: every category reads `0`, on its
own endpoint and on `/version`, with no import action. The audit trail holds only Reset's own trace, the only
notification is Reset's undismissed reseed recommendation, and the empty database answers `200` with
`status=NoResults` rather than `503`.

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

**Variant B, steps 6 to 12: the content is imported through the API.**

### 6. Create the environment and import the content

```
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03b --port 19303 --image quotinator:local --env Quotinator__IncludeDefaultSources=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json --expect 200 --status
```

**Expected:** the app reports healthy and the import answers `200`.

**On failure:** this variant is blocked. Run its cleanup and report which route blocked it.

### 7. Record the starting state

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-before.json --equals totalCount=4
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-before.json --equals totalCount=3
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/characters --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-characters-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-characters-before.json --equals totalCount=3
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/people --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-people-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-people-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/series --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-series-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-series-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/seasons --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-seasons-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-seasons-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/universes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-universes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-universes-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/stagedirections --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-stagedirections-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-stagedirections-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/soundcues --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-soundcues-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-soundcues-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/conversations --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-before.json --equals totalCount=2
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-before.json --greater-than totalCount=1
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-before.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/notifications --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-notifications-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-notifications-before.json --greater-than totalCount=0
```

**Expected:** the same starting state as step 2, reached by the other route.

**On failure:** the import did not establish the content, so this variant is blocked. Run its cleanup and report
which route blocked it.

### 8. `/version` agrees with what the import added

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-version-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-version-before.json --equals database.quotes=4 --equals database.sources=3 --equals database.characters=3 --equals database.people=2 --equals database.series=2 --equals database.seasons=2 --equals database.universes=2 --equals database.stageDirections=2 --equals database.soundCues=2 --equals database.conversations=2
```

**Expected:** the same counts step 7 read from the list endpoints.

**On failure:** every count reading `0` is [#468](https://github.com/DutchJaFO/Quotinator/issues/468), a known issue
described in the [Knowledgebase](../../knowledgebase/version-counts-are-stale-after-an-import.md). Record it and carry
on, since nothing below reads this step. Any other reading is not #468 and is reported in its own right.

### 9. Reset the database

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/admin/database/reset --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset.json --equals quotes=0 --equals sources=0 --equals characters=0 --equals people=0 --equals series=0 --equals seasons=0 --equals universes=0 --equals stageDirections=0 --equals soundCues=0 --equals conversations=0 --length-equals reports=0
```

**Expected:** `200` with every row count `0` and no import reports.

### 10. Confirm every category was wiped

```
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/sources --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-sources-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/characters --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-characters-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-characters-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/people --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-people-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-people-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/series --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-series-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-series-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/seasons --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-seasons-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-seasons-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/universes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-universes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-universes-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/stagedirections --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-stagedirections-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-stagedirections-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/masterdata/soundcues --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-soundcues-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-soundcues-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/conversations --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-audit-after.json --equals totalCount=1 --equals items.0.operation=Reset
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-actions-after.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/notifications --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-notifications-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-notifications-after.json --equals totalCount=1 --equals items.0.metadataKind=reseedrecommended --equals items.0.type=actionrequired --equals items.0.isDismissed=false
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/version --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-version-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-version-after.json --equals status=ready --equals database.quotes=0 --equals database.sources=0 --equals database.characters=0 --equals database.people=0 --equals database.series=0 --equals database.seasons=0 --equals database.universes=0 --equals database.stageDirections=0 --equals database.soundCues=0 --equals database.conversations=0
```

**Expected:** every category reads `0`, on its own endpoint and on `/version`, with no import action, only Reset's
own audit trace, and Reset's undismissed reseed recommendation as the only notification.

### 11. Reset again with `preserveSchemaVersion=true`

A second import puts content back, so this Reset has something to wipe. This step reads the two ends of the
fixture's dependency chain, quotes and the conversations built from them: steps 7 and 10 are what cover each category.

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\database-lifecycle\fixtures\reset-content.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-again.json --equals totalCount=4
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:19303/api/v1/admin/database/reset?preserveSchemaVersion=true" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-reset-preserve.json --equals quotes=0 --equals conversations=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-quotes-preserve.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url http://localhost:19303/api/v1/conversations --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-preserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-db-03-out\b-conversations-preserve.json --equals totalCount=0
```

**Expected:** the quotes are back after the import, and the flag still wipes them and the conversations.

### 12. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03b --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19303/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-db-03b --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** as in step 5, both Resets included.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Read | A, before | A, after | B, before | B, after |
|---|---|---|---|---|
| `/quotes` | `4` | `0` | `4` | `0` |
| `/masterdata/sources` | `3` | `0` | `3` | `0` |
| `/masterdata/characters` | `3` | `0` | `3` | `0` |
| `/masterdata/people` | `2` | `0` | `2` | `0` |
| `/masterdata/series` | `2` | `0` | `2` | `0` |
| `/masterdata/seasons` | `2` | `0` | `2` | `0` |
| `/masterdata/universes` | `2` | `0` | `2` | `0` |
| `/masterdata/stagedirections` | `2` | `0` | `2` | `0` |
| `/masterdata/soundcues` | `2` | `0` | `2` | `0` |
| `/conversations` | `2` | `0` | `2` | `0` |
| `/admin/audit` | `7` | `1`, `operation=Reset` | `5` | `1`, `operation=Reset` |
| `/import/actions` | `24` | `0` | `24` | `0` |
| `/notifications` | `4` | `1`, `metadataKind=reseedrecommended` | `3` | `1`, `metadataKind=reseedrecommended` |
| `/version`, all ten counts | same as the list reads above | `0` | **`0`, not the list reads above** | `0` |

**Step 8 failed as #468 describes:** straight after the import, `/version` read `0` for all ten counts while the list
endpoints returned the rows. Every other step passed.

Both Resets answered `200`, with all ten row counts `0` and no reports. In variant A the imports file was still on
disk afterwards, nothing had been read back from it, and `quotes/random` answered `200` with `status=NoResults` and
no items. In variant B a second import brought the four quotes back, and `preserveSchemaVersion=true` answered `200`
and left both `/quotes` and `/conversations` at `0`.

**No exception was logged in either variant before its malformed upload**, every Reset included. Each upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#156](https://github.com/DutchJaFO/Quotinator/issues/156)'s first code
change, under a distinct tag, and run variant A's steps 1 to 4 against it.

Step 4 must read the fixture's counts from `/quotes` and the other categories rather than `0`. That is exactly the
behaviour #156 removed: `OnResetAsync` called `SeedIfEmptyInternalAsync`, so a reset reimported content on the
caller's behalf, and `CLAUDE.md`'s *Endpoint side-effect policy* records it as the defect. The waiting imports file
is what that reseed would read, and it is why variant A exists: variant B has nothing on disk to reseed from, so it
cannot show this. Step 4 must also read more than Reset's own single audit row, since the trail survived Reset then.
The fixture may name categories that build predates, which would fail step 2 there instead: remove those sections
from the canary's copy and say so in its reading.

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
