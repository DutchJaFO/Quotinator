# The pagination contract holds live on every paginated endpoint

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #195

## Preconditions

`/quotes`, `/admin/audit` and `/import/actions` share one pagination contract, and this proves it
holds live on all three rather than at the stub level.

**`/admin/audit` and `/import/actions` must have rows before the assertions below mean anything** — two
of them invert on an empty table, see Determinism.

**Needing content is a legitimate precondition; inheriting it from an earlier test is not.** This
document currently does the latter — it assumes rows are already there.

> **Unresolved: which resolution this test uses.** Either it creates the rows itself as a setup step
> and then runs anywhere in any order, or it declares that a broken import path blocks it and says so
> plainly. The second is a real possibility here: these rows come from the application's own import,
> so an import defect takes this test down with it even though the pagination contract may be
> perfectly intact — which is the case for a prepared resource. Recorded for #339's audit; see the
> index's *Depending on content is not the same as depending on another test*.

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
