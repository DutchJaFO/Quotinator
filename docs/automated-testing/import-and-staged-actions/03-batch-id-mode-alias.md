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
- **That transition is the assertion, not the status code.** `batchId` mode is a genuine alias for
  `POST /import/actions/apply` only if the actions it names actually moved; a route that returned `200`
  and did nothing leaves them exactly as the first reading found them.
- **`pageSize=0` is required on both readings.** The default page is 20 and the curated file stages more
  than that, so a default-paged listing would compare two truncated samples and could agree while the
  batch was only partly applied.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-import-03 2>/dev/null; docker volume rm qt-import-03-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-import-03 -p 18603:8080 -v qt-import-03-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18603/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch by previewing the curated file under `skip`

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"skip"}}' \
  "http://localhost:18603/api/v1/import/preview"
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

### 3. Record the staged state before applying

```bash
curl -s "http://localhost:18603/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** the batch's actions are staged and none is `Applied`.

### 4. Apply by `batchId`

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18603/api/v1/import?batchId=<batchId>"
```

**Expected:** `200`.

### 5. Read the same listing again

```bash
curl -s "http://localhost:18603/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** every one of them reads `Applied`, and the total number of actions is unchanged between
the two readings.

## Observed effect

Not yet established as a captured record.

## Cleanup

```bash
docker rm -f qt-import-03
docker volume rm qt-import-03-data
```
