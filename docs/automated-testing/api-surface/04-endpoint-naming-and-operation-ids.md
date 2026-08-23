# Endpoint names and summaries follow the standard, including the breaking operationId renames

**Smoke:** no
**Environment:** Fresh
**Traces to:** #279

## Preconditions

Nothing beyond the Fresh profile.

An `operationId` becomes part of the published OpenAPI spec, which a generated client can depend on —
renaming one is a breaking change. This test confirms the renames landed and the old values are gone
everywhere, not just where they were edited.

The spec must be fetched after startup has finished — a fetch during initialisation returns the wait
page, not the spec. The profile's readiness poll is what gates that.

## Determinism

- **Named container** (`qt-api-04`), so the log assertion at the end reads the right container's output.
- **Waits for health, not for a duration.** This previously used `sleep 15`, a guess that would fail on
  a slower machine for a reason unrelated to what the test verifies, and waste time on a faster one.
- The negative assertions matter as much as the positive ones: the old operationIds must be absent
  from the **whole** spec, not merely absent from the two endpoints that were renamed.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-api-04 2>/dev/null; docker volume rm qt-api-04-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-api-04 -p 18104:8080 -v qt-api-04-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18104/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Fetch the published spec and check the two renamed operationIds

```bash
curl -s "http://localhost:18104/openapi/v1.json" > /tmp/spec279.json
grep -o '"operationId":"GetAllImportBatches"' /tmp/spec279.json
grep -o '"operationId":"GetAllFileResources"' /tmp/spec279.json
```

**Expected:** the spec contains `operationId: GetAllImportBatches` and
`operationId: GetAllFileResources` — the two breaking renames.

**On failure:** two silent greps can equally mean an empty or missing `/tmp/spec279.json`, which is
what a fetch during initialisation produces. Confirm the file has content before recording this as a
missing rename, and stop — every check below reads the same file.

### 3. Confirm the old operationIds are gone from the whole spec

```bash
grep -o '"operationId":"GetImportBatches"\|"operationId":"GetFileResources"' /tmp/spec279.json
```

**Expected:** the spec does **not** contain `GetImportBatches` or `GetFileResources` anywhere.

### 4. Check the List-endpoint summaries

```bash
grep -o '"summary":"List [a-z ]*"' /tmp/spec279.json | sort -u
```

**Expected:** every List-endpoint `summary` reads `"List x"`, lowercase plural noun. `"List people"`,
`"List quotes"` and `"List series"` must appear; `"All people (paginated)"`,
`"All quotes (paginated)"` and `"List Series"` (capitalised) must not.

### 5. Fetch a quote by id and read its log tag

```bash
curl -s "http://localhost:18104/api/v1/quotes/random" | grep -o '"id":"[a-f0-9-]*"' | head -1
# use that id:
curl -s "http://localhost:18104/api/v1/quotes/<id>" > /dev/null
docker logs qt-api-04 2>&1 | grep "GetQuoteById\|Api - GetById"
```

**Expected:** the log line reads `[Api - GetQuoteById]`, not the old, already-mismatched
`[Api - GetById]`.

### 6. Spot-check the GetById summaries in the Scalar UI

Visit `http://localhost:18104/scalar/v1` and spot-check a few GetById operations (Character, Quote,
Import batch, Captured import file).

**Expected:** every GetById summary reads `"X by ID"` with a capitalised `ID` — no `"...by id"`
remaining.

## Observed effect

Partially established: the log line is itself an observed effect and is asserted above. The rest of
what the container emits while serving these requests has not been captured.

## Cleanup

```bash
docker rm -f qt-api-04 2>/dev/null
docker volume rm qt-api-04-data 2>/dev/null
rm /tmp/spec279.json
```
