# A batch applied through the staged flow can be reversed

**Smoke:** no
**Environment:** Fresh
**Traces to:** #177

## Preconditions

A batch applied entirely through the staged review → decide → apply flow — that is, via
`POST /import/actions/apply` directly, **not** `POST /import`'s own single-shot path. The distinction is
the whole subject: only the staged path exhibited the defect.

Re-import the curated file under `review` as in
[`01-staged-action-review-workflow.md`](01-staged-action-review-workflow.md) and decide every pending
action until none remain, then run the steps below.

**No command — the setup import and the per-action `decide` calls are not reproduced here, only
referenced, so this document cannot be run on its own as written.**

## Determinism

- **Every pending action must be decided before `apply`.** With any left undecided, `apply` returns
  `422` by design and the reversal is never reached — a real failure would be indistinguishable from
  an incomplete setup.
- The batch must have been applied through `/import/actions/apply`, not `/import`. Applying via the
  single-shot path exercises a different code route and would not have shown the original bug.

## Steps

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```

## Expected output

`apply` returns `200`. **Both `reverse` calls — preview and real — also return `200`**, never the `422`
this issue reported.

## Observed effect

The original defect: a batch applied entirely through the staged flow never had its own
`Import_Batch.Status` set to `Applied`, so `POST /import/actions/reverse` always rejected it with a bare
`422` even though the batch had genuinely applied.

**If this regresses**, `SqliteImportActionService.ApplyBatchAsync`'s `MarkImportBatchAppliedAsync` call
— gated on `TryApplyBatchAsync` returning `null` — is the one place that sets `Status`/`AppliedAt` for
every caller. Check it was not bypassed by a new caller of `ApplyBatchAsync` or `TryApplyBatchAsync`
added elsewhere.

## Cleanup

The staged batch and its actions remain, applied and then reversed — restore the Fresh profile before
the next test.
