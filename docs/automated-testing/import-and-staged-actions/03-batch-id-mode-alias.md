# `POST /import?batchId=` applies an already-staged batch without re-uploading

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154

## Preconditions

Nothing beyond the Fresh profile — the batch is staged by the preview call in Steps.

## Determinism

- **`skip` policy is used deliberately**, so the staged batch leaves nothing pending and `apply`
  can return `200` rather than the `422` an undecided action would produce. Using `review` here would
  test the atomicity contract instead of the alias.
- The `batchId` must come from this test's own preview response. A batch id found lying around in the
  database belongs to something else and makes the result meaningless.
- **The action statuses are read before and after, and that is the whole assertion.** `skip` means no
  domain row changes, so the status code alone cannot distinguish a working alias from a handler that
  parses `batchId` and returns `200` without applying anything — which is precisely the regression this
  test names. `ImportActionStatus.Applied` means "this action's write landed on the consumer's own
  tables", so the transition to it is the observable that a dead code path cannot fake.

## Steps

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"skip"}}' \
  "http://localhost:8080/api/v1/import/preview"
```

Copy the `batchId` from the response, then record the staged state **before** applying:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

Apply by `batchId`, then read the same listing again:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

## Expected output

The apply call returns `200`.

**Before** — the batch's actions are staged and none is `Applied`. **After** — every one of them reads
`Applied`, and the total number of actions is unchanged between the two readings.

That transition is the assertion, not the status code. `batchId` mode is a genuine alias for
`POST /import/actions/apply` only if the actions it names actually moved; a route that returned `200`
and did nothing leaves them exactly as the first reading found them.

**`pageSize=0` is required on both readings.** The default page is 20 and the curated file stages more
than that, so a default-paged listing would compare two truncated samples and could agree while the
batch was only partly applied.

## Observed effect

Not yet established as a captured record.

## Cleanup

The previewed batch and its actions remain, applied — restore the Fresh profile before the next test.
