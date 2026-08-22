# `POST /import?batchId=` applies an already-staged batch without re-uploading

**Smoke:** no
**Traces to:** #154

## Preconditions

A running container with an admin key. The batch is staged by a preview call first — nothing else is
required.

## Determinism

- **`skip` policy is used deliberately**, so the staged batch leaves nothing pending and `apply`
  can return `200` rather than the `422` an undecided action would produce. Using `review` here would
  test the atomicity contract instead of the alias.
- The `batchId` must come from the preview response, not from an earlier test's batch.

## Steps

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"skip"}}' \
  "http://localhost:8080/api/v1/import/preview"
```

Copy the `batchId` from the response, then:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import?batchId=<batchId>"
```

## Expected output

`200`, and the previewed batch is applied — proving `batchId` mode is a genuine alias for
`POST /import/actions/apply`, not a dead code path.

## Observed effect

Not yet established as a captured record.

## Cleanup

None.
