# The pagination contract holds live on every paginated endpoint

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #195

## Preconditions

`/quotes`, `/admin/audit` and `/import/actions` share one pagination contract, and this proves it
holds live on all three rather than at the stub level.

**`/admin/audit` and `/import/actions` must have rows before the assertions below mean anything** — two
of them invert on an empty table, see Determinism.

The container half of that is the Fresh profile's job. The rows are not: the profile's own first boot
is what would populate both tables — the bundled seed writes `Audit_Entry` rows, and stages
`Import_Action` rows per bundled batch.

**The `Import_Action` half needs one delta beyond the profile:**
`Quotinator__AutoPurgeBundledImportActions=false`, declared in step 1.

Fresh pins that setting to the application's own default, `true`, which removes the bundled batches'
`Import_Action` rows immediately after a successful seed. Without the delta this test's
`/import/actions` assertions run against an empty table and invert: page 1 of nothing is not beyond
the last page, so the `422` becomes a `200`, and the default page size arrives by defaulting-through
rather than by being applied.

**Measured both ways during #339's full run**, which is what settled it. Under the profile default,
`pageSize=0` returned `pageSize 0` with `totalCount 0` and page-beyond-last returned `200`. With the
delta: 1425 rows, `pageSize=0` returned `pageSize 1425` equal to `totalCount`, the default read `20`,
and page-beyond-last returned `422`. The endpoint was correct throughout — only the table was empty.

This document previously flagged the choice as unresolved and offered two ways out: declare the delta,
or drop the `/import/actions` assertions and confine the contract check to the other two endpoints.
The delta is chosen, because the alternative removes live coverage of one of the three endpoints whose
shared contract is the entire subject. `database-lifecycle/02` already runs one container on the
default and a second on `false` for the same reason.

## Determinism

- **Row counts are non-zero** in `Audit_Entry` and `Import_Action` before starting. On a table with
  zero rows, page 1 of nothing is not "beyond the last page", so the 422 assertion inverts to a 200
  and the test reports a false failure. `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull`
  is the unit-level statement of the same behaviour.
- **The default-page-size assertion needs populated tables too** — an empty table would return the
  default without proving it was applied rather than defaulted-through.
- Admin key present for the `/admin/audit` calls.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-02 --port 18102 `
  --env Quotinator__AutoPurgeBundledImportActions=false
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 1b. Confirm all three tables have rows before asserting anything

Two of the assertions below invert on an empty table, so this is a precondition rather than preamble:

```powershell
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18102/api/v1"
$endpoints = @{
  quotes  = "$base/quotes"
  audit   = "$base/admin/audit"
  actions = "$base/import/actions"
}
foreach ($name in $endpoints.Keys) {
  "$name totalCount = $((Invoke-RestMethod "$($endpoints[$name])?pageSize=1" -Headers $key).totalCount)"
}
```

**Expected:** all three report a non-zero `totalCount`.

**On failure:** a zero from `actions` means step 1's auto-purge delta did not take effect, and
every `/import/actions` assertion below then reports a false failure that looks exactly like a
pagination defect. Stop and re-create the container with the delta rather than recording the result.

### 2. Request every row with `pageSize=0`

```powershell
foreach ($name in $endpoints.Keys) {
  $all = Invoke-RestMethod "$($endpoints[$name])?pageSize=0" -Headers $key
  "$name items=$($all.items.Count) pageSize=$($all.pageSize) totalCount=$($all.totalCount)"
}
```

**Expected:** on all three, `items` contains every row (not zero), and `pageSize` in the response
equals `totalCount`. That is the effective-size contract, not the literal `0` requested.

### 3. Request a page size above the maximum

```powershell
foreach ($name in $endpoints.Keys) {
  dotnet script scripts/testing/http.csx -- --url "$($endpoints[$name])?pageSize=501" --expect 422 --status
}
```

**Expected:** all three return `422`. Above 500 is rejected, never silently clamped — and `--expect`
makes a `200` here end the step rather than being read past.

### 4. Omit `pageSize` and read the applied default

```powershell
foreach ($name in @('audit', 'actions')) {
  "$name pageSize = $((Invoke-RestMethod $endpoints[$name] -Headers $key).pageSize)"
}
```

**Expected:** both responses report `20`, not the endpoints' old default of `50`.

**On failure:** a `20` read off an empty table proves nothing — it is the default arriving by
default-through rather than by application, per Determinism. Confirm both tables have rows before
recording this either way.

### 5. Request a page beyond the last

```powershell
dotnet script scripts/testing/http.csx -- --url "$base/quotes?pageSize=500&page=99" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "$base/admin/audit?pageSize=1&page=999999" --expect 422 --status
dotnet script scripts/testing/http.csx -- --url "$base/import/actions?pageSize=1&page=999999" --expect 422 --status
```

**Expected:** all three return `422`.

**On failure:** a `200` here inverts on an empty table rather than indicating a pagination defect —
page 1 of nothing is not beyond the last page. Confirm the table has rows before reading this as a
failure of the contract.

## Observed effect

Not yet established as a captured record. What this test exists to catch is documented, though: both
the audit and import readers were found passing `pageSize=0` straight into `LIMIT @pageSize` rather
than translating it to `LIMIT -1`, during #195's own T2 pass. No unit test could have caught it — the
stub readers those tests use echo their input back instead of exercising real SQL.

## Explicitly not covered here

`page`/`pageSize` publishing as `integer` rather than `string` on the live spec is **not** part of
this test. It was originally a text-matching check on the published spec here, and the first version of
that command was wrong — it assumed single-line JSON and never matched anything. Matching a
pretty-printed multi-line body for a nested field is fragile and its pass/fail needs a human to eyeball
the output.

It is now `OpenApiSpecEndpointTests`, a `WebApplicationFactory` test that fetches the real
`/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument` — so it runs
deterministically in every `dotnet test` instead of requiring a live container.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-02
```
