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

**The `detail` field is asserted as present and non-empty, never against its exact wording.** The text
is localised, so it varies with the request's `Accept-Language`; what the test is about is that a
`detail` exists at all, which a bare `400` does not carry.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-06 --port 18606
$base = "http://localhost:18606/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Post an import request with no body at all

```powershell
$bodyless = dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" --expect 422 | ConvertFrom-Json
"status=$($bodyless.status) hasDetail=$([bool]$bodyless.detail) hasTraceId=$([bool]$bodyless.traceId)"
$bodyless.detail
```

**Expected:** `422`, `hasDetail=True` and `hasTraceId=True`, with a `detail` naming the missing input —
"you must provide either a file… or a batchId", paraphrased per locale. **Not** a bare `400` with no
`detail` at all.

### 3. Post a bodyless request naming an unknown `batchId`

```powershell
$unknown = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import?batchId=00000000-0000-0000-0000-000000000000" --expect 404 | ConvertFrom-Json
"status=$($unknown.status) hasDetail=$([bool]$unknown.detail)"
$unknown.detail
```

**Expected:** `404` (unknown batch) even with zero body and no `Content-Type`, proving `batchId` mode
never attempts to read the request body — again with its own `detail`.

## Observed effect

Captured 2026-08-23, running this document verbatim.

A bodyless `POST /import` returns an RFC 4918 problem document:

```json
{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Unprocessable Entity","status":422,
 "detail":"You must provide either a file to import or a batchId to apply an already-staged batch.",
 "traceId":"..."}
```

Naming an unknown `batchId` returns an RFC 9110 one:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,
 "detail":"No import batch exists with that id.","traceId":"..."}
```

**The `detail` text is what an operator actually sees**, and it is specific to the missing input rather
than generic — which is the whole point of the `422`-over-`400` distinction this test exists for. Both
carry a `traceId`, so a report of either can be tied back to its request in the log.

**If this regresses to a bare `400`**, `POST /import`'s handler is binding `IFormFile`/`[FromForm]`
parameters automatically again instead of reading `HttpRequest` manually — see `ImportEndpoints.cs`'s
`HandleImportFromRequestAsync`.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-06
```
