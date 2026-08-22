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

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import"
```

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import?batchId=00000000-0000-0000-0000-000000000000"
```

## Expected output

**The bodyless call returns `422` with a `detail` field** — "you must provide either a file… or a
batchId", paraphrased per locale. **Not** a bare `400` with no `detail` at all.

**The `batchId` call returns `404`** (unknown batch) even with zero body and no `Content-Type`, proving
`batchId` mode never attempts to read the request body.

## Observed effect

Not yet established as a captured record.

**If this regresses to a bare `400`**, `POST /import`'s handler is binding `IFormFile`/`[FromForm]`
parameters automatically again instead of reading `HttpRequest` manually — see `ImportEndpoints.cs`'s
`HandleImportFromRequestAsync`.

## Cleanup

None.
