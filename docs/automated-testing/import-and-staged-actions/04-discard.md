# Discarding a staged batch marks every action discarded and applies nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154

## Preconditions

A staged batch with pending actions — the preview call below produces one under `review` policy.

## Determinism

- **`review` policy is required here**, unlike
  [`03-batch-id-mode-alias.md`](03-batch-id-mode-alias.md): the batch must have pending actions for
  discard to have anything to mark.
- The listing afterwards is scoped to that specific `batchId`, so other batches in the database cannot
  affect the result.

## Steps

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import/preview"
```

Copy the `batchId`, then:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/discard?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
```

## Expected output

Discard returns `204`. Every action in that batch shows `"status":"Discarded"`.

**Nothing was ever applied**, because creation is deferred to apply time — a discarded batch never
touched the domain tables at all.

## Observed effect

Not yet established as a captured record.

## Cleanup

None.
