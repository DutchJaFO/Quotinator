# A batch applied through the staged flow can be reversed

**Smoke:** no
**Environment:** Fresh
**Traces to:** #177

## Preconditions

A batch applied entirely through the staged review → decide → apply flow — that is, via
`POST /import/actions/apply` directly, **not** `POST /import`'s own single-shot path. The distinction is
the whole subject: only the staged path exhibited the defect.

The steps below stage that batch themselves rather than borrowing one, so this document runs alone.

## Determinism

- **Every pending action must be decided before `apply`.** With any left undecided, `apply` returns
  `422` by design and the reversal is never reached — a real failure would be indistinguishable from
  an incomplete setup.
- The batch must have been applied through `/import/actions/apply`, not `/import`. Applying via the
  single-shot path exercises a different code route and would not have shown the original bug.
- **The status codes are the whole assertion, and that is deliberate.** The defect was
  `Import_Batch.Status` never being set to `Applied` by the staged path, and `reverse` rejecting the
  batch as a result. A regression shows up directly as the status code, so no separate read-back is
  needed here.
- **`preview=true` returning `200` is not a claim that it changed nothing** — nothing in this run reads
  state before and after the preview. It is asserted only as "the preview route answers"; if the
  no-side-effect guarantee ever needs proving, that is a read-back this document does not currently have.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-import-02 2>/dev/null; docker volume rm qt-import-02-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-import-02 -p 18602:8080 -v qt-import-02-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18602/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch under `review`

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:18602/api/v1/import"
```

**Expected:** the response carries a `batchId` — every step below is scoped to it.

### 3. List this batch's pending actions

```bash
curl -s "http://localhost:18602/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0" \
  | grep -o '"id":"[^"]*"'
```

**Expected:** the action `id`s this batch staged — the set step 4 must decide in full.

### 4. Decide every one of them, then confirm none is left

Repeat the `decide` call for each `id` listed:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:18602/api/v1/import/actions/<id>/decide"
curl -s "http://localhost:18602/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0" \
  | grep -o '"totalCount":[0-9]*'
```

**Expected:** that count reads `0`.

**On failure:** with any action still pending, `apply` returns `422` by design and the reversal below is
never reached — so a genuine failure would be indistinguishable from an incomplete setup, which is the
trap this confirmation exists to close. Stop and decide the remainder.

### 5. Apply through the staged path

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18602/api/v1/import/actions/apply?batchId=<batchId>"
```

**Expected:** `200`.

### 6. Preview the reversal

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18602/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
```

**Expected:** `200`, never the `422` this issue reported.

### 7. Reverse the batch

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18602/api/v1/import/actions/reverse?batchId=<batchId>"
```

**Expected:** `200`, never the `422` this issue reported.

## Observed effect

The original defect: a batch applied entirely through the staged flow never had its own
`Import_Batch.Status` set to `Applied`, so `POST /import/actions/reverse` always rejected it with a bare
`422` even though the batch had genuinely applied.

**If this regresses**, `SqliteImportActionService.ApplyBatchAsync`'s `MarkImportBatchAppliedAsync` call
— gated on `TryApplyBatchAsync` returning `null` — is the one place that sets `Status`/`AppliedAt` for
every caller. Check it was not bypassed by a new caller of `ApplyBatchAsync` or `TryApplyBatchAsync`
added elsewhere.

## Cleanup

```bash
docker rm -f qt-import-02
docker volume rm qt-import-02-data
```
