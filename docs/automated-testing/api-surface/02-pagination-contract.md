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

> **Unresolved: the `Import_Action` half.** Fresh pins `Quotinator__AutoPurgeBundledImportActions` to
> the application's own default, `true`, which removes the bundled batches' `Import_Action` rows
> immediately after a successful seed. Under the profile as written, this test's `/import/actions`
> assertions therefore run against an empty table and invert — exactly the false failure Determinism
> describes, and indistinguishable from a genuine pagination defect.
>
> There are two honest resolutions and this document picks neither: declare
> `Quotinator__AutoPurgeBundledImportActions=false` as its own delta, or state plainly that its
> `/import/actions` assertions cannot be relied on and confine the contract check to `/quotes` and
> `/admin/audit`. Flagged, not silently chosen. See the index's *Depending on content is not the same
> as depending on another test*, and `database-lifecycle/02`, which already runs one container on the
> default and a second on `false` precisely to tell the two apart.

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

```bash
docker rm -f qt-api-02 2>/dev/null; docker volume rm qt-api-02-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-api-02 -p 18102:8080 -v qt-api-02-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18102/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Request every row with `pageSize=0`

```bash
curl -s "http://localhost:18102/api/v1/quotes?pageSize=0"
curl -s "http://localhost:18102/api/v1/admin/audit?pageSize=0" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:18102/api/v1/import/actions?pageSize=0"
```

**Expected:** on all three, `items` contains every row (not zero), and `pageSize` in the response
equals `totalCount`. That is the effective-size contract, not the literal `0` requested.

### 3. Request a page size above the maximum

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/quotes?pageSize=501"
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/admin/audit?pageSize=501" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/import/actions?pageSize=501"
```

**Expected:** all three return `422`. Above 500 is rejected, never silently clamped.

### 4. Omit `pageSize` and read the applied default

```bash
curl -s "http://localhost:18102/api/v1/admin/audit" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:18102/api/v1/import/actions"
```

**Expected:** both responses report `20`, not the endpoints' old default of `50`.

**On failure:** a `20` read off an empty table proves nothing — it is the default arriving by
default-through rather than by application, per Determinism. Confirm both tables have rows before
recording this either way.

### 5. Request a page beyond the last

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/quotes?pageSize=500&page=99"
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/admin/audit?pageSize=1&page=999999" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:18102/api/v1/import/actions?pageSize=1&page=999999"
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
this test. It was originally a `curl | grep` check here, and the first version of that command was
wrong — it assumed single-line JSON and never matched anything. Grepping a pretty-printed multi-line
body for a nested field is fragile and its pass/fail needs a human to eyeball the output.

It is now `OpenApiSpecEndpointTests`, a `WebApplicationFactory` test that fetches the real
`/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument` — so it runs
deterministically in every `dotnet test` instead of requiring a live container.

## Cleanup

```bash
docker rm -f qt-api-02 2>/dev/null
docker volume rm qt-api-02-data 2>/dev/null
```
