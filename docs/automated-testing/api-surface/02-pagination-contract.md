# The pagination contract holds live on every paginated endpoint

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #195

## Preconditions

`/quotes`, `/admin/audit` and `/import/actions` share one pagination contract, and this proves it
holds live on all three rather than at the stub level.

The rows are the test's own: step 2 imports [25 quotes](fixtures/pagination-quotes.json) through
`POST /api/v1/import`, so a broken import blocks this document. That is the index's second honest
resolution for a precondition, and it is named here so a blocked run reads as that.

## Determinism

- **All three tables must hold more rows than one default page before anything is asserted.** On an
  empty table, page 1 of nothing is not "beyond the last page", so the `422` assertion inverts to a `200`
  and the test reports a false failure, and the default page size arrives without having limited anything.
  `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull` is the unit-level statement of
  the same behaviour. Step 3 asserts it for each table before reading anything else from it.
- **The fixture is what fills `/import/actions`.** The Fresh profile purges the bundled batches' actions after
  the seed, so this document needs no setting of its own, and the rows it reads there are its import's.
- **The import adds only a few rows to `/admin/audit`.** What takes that table past one page is the first boot
  itself, so step 3 is where a smaller first boot would show: as that precondition failing, not as a pagination
  defect. Nothing here depends on how many rows there are beyond that.
- **Every assertion relates a response to itself**, or to the fixed limits of the contract. No count of
  bundled quotes appears anywhere, so a change to a source cannot fail this document.
- The admin key `http.csx` sends is what the `/admin/audit` calls need.

## Explicitly not covered here

`page`/`pageSize` publishing as `integer` rather than `string` on the live spec is **not** part of
this test. `OpenApiSpecEndpointTests`, a `WebApplicationFactory` test, fetches the real `/openapi/v1.json`
through the full pipeline and asserts the type, so it runs deterministically in every `dotnet test`.

## Steps

### 1. Create this test's own environment

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-api-02-out
dotnet script scripts/testing/test-env.csx -- create --name qt-api-02 --port 18102 --image quotinator:local
```

**Expected:** the app reports healthy, so the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import this test's own quotes

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/import --method POST --file docs/automated-testing/api-surface/fixtures/pagination-quotes.json --expect 200 --status --body-out .claude/temp/qt-api-02-out/import.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/import.json --equals summary.imported=25 --equals summary.errors=0
```

**Expected:** all 25 quotes are imported, with no errors.

**On failure:** stop. `/import/actions` has no rows without this import.

### 3. Omit `pageSize` and read the applied default

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/quotes --expect 200 --status --body-out .claude/temp/qt-api-02-out/quotes-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/quotes-default.json --greater-than totalCount=20 --equals pageSize=20 --length-equals items=20
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/admin/audit --expect 200 --status --body-out .claude/temp/qt-api-02-out/audit-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/audit-default.json --greater-than totalCount=20 --equals pageSize=20 --length-equals items=20
dotnet script scripts/testing/http.csx -- --url http://localhost:18102/api/v1/import/actions --expect 200 --status --body-out .claude/temp/qt-api-02-out/actions-default.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/actions-default.json --greater-than totalCount=20 --equals pageSize=20 --length-equals items=20
```

**Expected:** on all three, more rows exist than one page holds, the response reports `20`, and `items` holds
exactly `20`. That is the default being applied rather than a table happening to be short.

**On failure:** a `totalCount` of `20` or less is the precondition failing, not the contract: every later
step would read a table too short to mean anything. Stop and report that.

### 4. Request every row with `pageSize=0`

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/quotes?pageSize=0" --expect 200 --status --body-out .claude/temp/qt-api-02-out/quotes-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/quotes-all.json --greater-than totalCount=20 --equals-field pageSize=totalCount --length-equals-field items=totalCount
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/admin/audit?pageSize=0" --expect 200 --status --body-out .claude/temp/qt-api-02-out/audit-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/audit-all.json --greater-than totalCount=20 --equals-field pageSize=totalCount --length-equals-field items=totalCount
dotnet script scripts/testing/http.csx -- --url "http://localhost:18102/api/v1/import/actions?pageSize=0" --expect 200 --status --body-out .claude/temp/qt-api-02-out/actions-all.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-api-02-out/actions-all.json --greater-than totalCount=20 --equals-field pageSize=totalCount --length-equals-field items=totalCount
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
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18102/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
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

| Request | `/quotes` | `/admin/audit` | `/import/actions` |
|---|---|---|---|
| No `pageSize` | `200`, `pageSize=20`, 20 items of 820 | `200`, `pageSize=20`, 20 items of 42 | `200`, `pageSize=20`, 20 items of 26 |
| `pageSize=0` | `200`, `pageSize=820`, 820 items | `200`, `pageSize=42`, 42 items | `200`, `pageSize=26`, 26 items |
| `pageSize=501` | `422` | `422` | `422` |
| `pageSize=1&page=999999` | `422` | `422` | `422` |

The 26 actions are the fixture's 25 quotes and the one source they share; the table read `0` before the import.
The audit table read `40` before the import, so the import itself added 2. These totals are this run's reading,
not expectations: only the fixture's import count and the relations above are asserted.

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
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete .claude/temp/qt-api-02-out
```

**Expected:** the container, its volume and the output directory are gone.
