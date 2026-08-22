# Endpoint names and summaries follow the standard, including the breaking operationId renames

**Smoke:** no
**Traces to:** #279

## Preconditions

An `operationId` becomes part of the published OpenAPI spec, which a generated client can depend on —
renaming one is a breaking change. This test confirms the renames landed and the old values are gone
everywhere, not just where they were edited.

The container must have finished starting before the spec is fetched — a fetch during startup returns
the wait page, not the spec.

## Determinism

- **Named container** (`smoke279`), so the log assertion at the end reads the right container's output.
- **Waits for health, not for a duration.** This previously used `sleep 15`, a guess that would fail on
  a slower machine for a reason unrelated to what the test verifies, and waste time on a faster one.
- The negative assertions matter as much as the positive ones: the old operationIds must be absent
  from the **whole** spec, not merely absent from the two endpoints that were renamed.

## Steps

```bash
docker rm -f smoke279
MSYS_NO_PATHCONV=1 docker run -d --name smoke279 -p 8080:8080 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/openapi/v1.json" > /tmp/spec279.json
grep -o '"operationId":"GetAllImportBatches"' /tmp/spec279.json
grep -o '"operationId":"GetAllFileResources"' /tmp/spec279.json
grep -o '"operationId":"GetImportBatches"\|"operationId":"GetFileResources"' /tmp/spec279.json
grep -o '"summary":"List [a-z ]*"' /tmp/spec279.json | sort -u
```

**Log tag consistency** — fetch a real quote by id and check the log line:

```bash
curl -s "http://localhost:8080/api/v1/quotes/random" | grep -o '"id":"[a-f0-9-]*"' | head -1
# use that id:
curl -s "http://localhost:8080/api/v1/quotes/<id>" > /dev/null
docker logs smoke279 2>&1 | grep "GetQuoteById\|Api - GetById"
```

**Scalar UI** — visit `http://localhost:8080/scalar/v1` and spot-check a few GetById operations
(Character, Quote, Import batch, Captured import file).

## Expected output

- The spec contains `operationId: GetAllImportBatches` and `operationId: GetAllFileResources` — the two
  breaking renames — and does **not** contain `GetImportBatches` or `GetFileResources` anywhere.
- Every List-endpoint `summary` reads `"List x"`, lowercase plural noun. `"List people"`,
  `"List quotes"` and `"List series"` must appear; `"All people (paginated)"`,
  `"All quotes (paginated)"` and `"List Series"` (capitalised) must not.
- Every GetById summary reads `"X by ID"` with a capitalised `ID` — no `"...by id"` remaining.
- The log line reads `[Api - GetQuoteById]`, not the old, already-mismatched `[Api - GetById]`.

## Observed effect

Partially established: the log line is itself an observed effect and is asserted above. The rest of
what the container emits while serving these requests has not been captured.

## Cleanup

```bash
docker rm -f smoke279
rm /tmp/spec279.json
```
