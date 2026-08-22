# The pagination contract holds live on every paginated endpoint

**Smoke:** yes
**Traces to:** #195

## Preconditions

`/quotes`, `/admin/audit` and `/import/actions` share one pagination contract, and this proves it
holds live on all three rather than at the stub level.

**`/admin/audit` and `/import/actions` must already have rows.** Run the import and staged-action
tests first. Two of the assertions below are only meaningful against a populated table — see
Determinism.

## Determinism

- **Row counts are non-zero** in `Audit_Entry` and `Import_Action` before starting. On a table with
  zero rows, page 1 of nothing is not "beyond the last page", so the 422 assertion inverts to a 200
  and the test reports a false failure. `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull`
  is the unit-level statement of the same behaviour.
- **The default-page-size assertion needs populated tables too** — an empty table would return the
  default without proving it was applied rather than defaulted-through.
- Admin key present for the `/admin/audit` calls.

## Steps

```bash
curl -s "http://localhost:8080/api/v1/quotes?pageSize=0"
curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=0" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:8080/api/v1/import/actions?pageSize=0"
```

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=501"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=501" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=501"
```

```bash
curl -s "http://localhost:8080/api/v1/admin/audit" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:8080/api/v1/import/actions"
```

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=500&page=99"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=1&page=999999" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=1&page=999999"
```

## Expected output

**`pageSize=0`** — on all three, `items` contains every row (not zero), and `pageSize` in the response
equals `totalCount`. That is the effective-size contract, not the literal `0` requested.

**`pageSize=501`** — all three return `422`. Above 500 is rejected, never silently clamped.

**`pageSize` omitted** — both responses report `20`, not the endpoints' old default of `50`.

**Page beyond the last** — all three return `422`.

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

None — this test only reads.
