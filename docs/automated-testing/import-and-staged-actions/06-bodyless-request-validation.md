# A bodyless import request is rejected with an actionable message, not a bare 400

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154

## Preconditions

Nothing beyond the Fresh profile. No file, no `Content-Type`, no `batchId` — the absence *is* the
input.

## Determinism

**This can only be proven live, and that is the reason the test exists.**
`WebApplicationFactory`'s in-memory TestServer handles a bodyless request differently than real Kestrel
does, so the unit suite cannot establish this behaviour at all. A passing unit test here would be
evidence about the TestServer, not about the application.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-import-06 2>/dev/null; docker volume rm qt-import-06-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-import-06 -p 18606:8080 -v qt-import-06-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18606/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Post an import request with no body at all

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18606/api/v1/import"
```

**Expected:** `422` with a `detail` field — "you must provide either a file… or a batchId", paraphrased
per locale. **Not** a bare `400` with no `detail` at all.

### 3. Post a bodyless request naming an unknown `batchId`

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18606/api/v1/import?batchId=00000000-0000-0000-0000-000000000000"
```

**Expected:** `404` (unknown batch) even with zero body and no `Content-Type`, proving `batchId` mode
never attempts to read the request body.

## Observed effect

Not yet established as a captured record.

**If this regresses to a bare `400`**, `POST /import`'s handler is binding `IFormFile`/`[FromForm]`
parameters automatically again instead of reading `HttpRequest` manually — see `ImportEndpoints.cs`'s
`HandleImportFromRequestAsync`.

## Cleanup

```bash
docker rm -f qt-import-06
docker volume rm qt-import-06-data
```
