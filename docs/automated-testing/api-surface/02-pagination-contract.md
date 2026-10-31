# The pagination contract holds live on every paginated endpoint

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #195

## Preconditions

`/quotes`, `/admin/audit` and `/import/actions` share one pagination contract, and this proves it
holds live on all three rather than at the stub level.

**Beyond the profile.** The container runs with `Quotinator__IncludeDefaultSources=false`, so nothing bundled is
in the database. Every row is the test's own: step 2 imports [25 quotes](fixtures/pagination-quotes.json) through
`POST /api/v1/import`, so a broken import blocks this document. That is the index's second honest resolution for
a precondition, and it is named here so a blocked run reads as that.

## Determinism

- **Counts are exact where the fixture decides them.** 25 quotes, and 26 import actions: one per quote and one for
  the source they share. A bundled file cannot change them.
- **`/quotes` and `/import/actions` hold more rows than one default page**, so step 3 shows the default limiting
  what is returned rather than a table happening to be short.
- **`/admin/audit` cannot show that limit here.** One import writes only a couple of audit rows, so its step 3
  assertion is the reported `pageSize`, and the row count only that it is above `0`. The default reaching that
  reader is what distinguishes it from the old default, `50`.
- **On an empty table, page 1 of nothing is not "beyond the last page"**, so the `422` assertion inverts to a `200`
  and the test reports a false failure. `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull`
  is the unit-level statement of that behaviour. Step 3 is what establishes each table holds rows.
- The admin key `http.csx` sends is what the `/admin/audit` calls need.

## Explicitly not covered here

`page`/`pageSize` publishing as `integer` rather than `string` on the live spec is **not** part of
this test. `OpenApiSpecEndpointTests`, a `WebApplicationFactory` test, fetches the real `/openapi/v1.json`
through the full pipeline and asserts the type, so it runs deterministically in every `dotnet test`.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-api-02-out
dotnet script scripts/testing/test-env.csx -- create --name qt-api-02 --port 18102 --image quotinator:local --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import this test's own quotes

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/import --method POST --file C:\repos\Quotinator\docs\automated-testing\api-surface\fixtures\pagination-quotes.json --expect 200 --status
```

**Expected:** `200`. Step 3 reads what actually arrived.

**On failure:** stop. Every table below is empty without this import.

### 3. Omit `pageSize` and read the applied default

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\quotes-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\quotes-default.json --equals totalCount=25 --equals pageSize=20 --length-equals items=20
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/import/actions --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\actions-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\actions-default.json --equals totalCount=26 --equals pageSize=20 --length-equals items=20
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/admin/audit --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\audit-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\audit-default.json --greater-than totalCount=0 --equals pageSize=20
```

**Expected:** `/quotes` and `/import/actions` report `20` and return exactly `20` of their `25` and `26` rows.
`/admin/audit` holds rows and reports `20`.

### 4. Request every row with `pageSize=0`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/quotes?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\quotes-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\quotes-all.json --equals totalCount=25 --equals pageSize=25 --length-equals items=25
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/import/actions?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\actions-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\actions-all.json --equals totalCount=26 --equals pageSize=26 --length-equals items=26
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/admin/audit?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-api-02-out\audit-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-api-02-out\audit-all.json --greater-than totalCount=0 --equals-field pageSize=totalCount --length-equals-field items=totalCount
```

**Expected:** on all three, `items` holds every row, and `pageSize` in the response equals `totalCount`. That is
the effective-size contract, not the literal `0` requested.

### 5. Request a page size above the maximum

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/quotes?pageSize=501" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/admin/audit?pageSize=501" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/import/actions?pageSize=501" --expect 422 --status
```

**Expected:** all three return `422`. Above 500 is rejected, never silently clamped.

### 6. Request a page beyond the last

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/quotes?pageSize=1&page=999999" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/admin/audit?pageSize=1&page=999999" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/import/actions?pageSize=1&page=999999" --expect 422 --status
```

**Expected:** all three return `422`, each from a table step 3 showed holds rows.

### 7. Nothing unexpected was thrown

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-02 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18102/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-02 --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception has been logged by anything so far, the rejected requests above included. The
malformed upload then answers `422` and its `JsonReaderException` is logged, which is the positive control:
the same channel that read zero can see an exception, so the zero means something.

**The count comes first**, because the upload throws more than the type its control names.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the
line that names it.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| Request | `/quotes` | `/import/actions` | `/admin/audit` |
|---|---|---|---|
| No `pageSize` | `pageSize=20`, 20 items of 25 | `pageSize=20`, 20 items of 26 | `pageSize=20`, 2 rows |
| `pageSize=0` | `pageSize=25`, 25 items | `pageSize=26`, 26 items | `pageSize=2`, 2 items |
| `pageSize=501` | `422` | `422` | `422` |
| `pageSize=1&page=999999` | `422` | `422` | `422` |

**No exception was logged before the malformed upload**, the six rejected requests included. That upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#195](https://github.com/DutchJaFO/Quotinator/issues/195)'s first code
change, under a distinct tag, and run steps 1 to 6 against it.

Step 3 must read `50` on `/admin/audit` and `/import/actions`, the default #195 replaced. Step 4 is the other
half: `CLAUDE.md`'s *Standard pagination contract* records #195's own pass finding `SystemAuditReader` and
`SystemImportActionReader` translating `pageSize=0` into a literal `LIMIT 0`, so that build must return zero
rows where this one returns every row. Confirm which of the two readers shows it rather than assuming both do.

Step 5 is expected to pass there. A rejected oversized page size predates #195, so it is the control that keeps
step 3's reading attributable to the default rather than to a broken container.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-api-02-out
```

**Expected:** the container, its volume and the output directory are gone.
