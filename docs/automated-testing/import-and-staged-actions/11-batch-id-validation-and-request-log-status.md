# A missing `batchId` is rejected with the right message, and the log reports the real status

**Smoke:** no
**Environment:** Fresh
**Traces to:** #19

## Preconditions

Beyond the Fresh profile: **request logging must be genuinely on**, which Fresh does not set.
`Quotinator__LogRequests=true` **and** `Quotinator__LogLevel=debug` — request logging is Debug-only
across every category (#244), so `LogRequests=true` alone registers the middleware without raising the
level, and the log assertion below would silently have nothing to read.

## Determinism

- **Both halves must be checked.** The status code and the *logged* status code were wrong
  independently, and a test asserting only the response would have passed while the log still lied.
- The happy path is re-run afterwards, so a fix that returns `422` unconditionally cannot pass.

## Steps

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/discard"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse"
```

Then read the container log for each of those requests:

```bash
docker logs qt-env
```

Finally re-run a normal `apply` with a real `batchId` — see
[`01-staged-action-review-workflow.md`](01-staged-action-review-workflow.md).

**No command — this document never stages a batch, so there is no `batchId` to apply.** Writing the
call out would mean inventing both the staging import and the id it returns.

## Expected output

- All three return `422` with `"detail":"You must provide a batchId."` — **never** the generic
  "Numeric parameters..." message.
- `docker logs` shows `→ 422` for each, **not** `→ 200`.
- The normal `apply` with a real `batchId` still returns `200`.

## Observed effect

**Found live via manual Visual Studio testing (T1), not by this suite** — worth recording, because it
is a class of defect a status-code check alone does not surface.

All three endpoints declared `batchId` as a required, non-nullable minimal-API parameter, so an omitted
`batchId` threw `BadHttpRequestException` at the binding layer before the handler ever ran. The global
safety net (`BadRequestExceptionHandler`) caught it and returned `422` — but with a message hard-coded
to numeric parameters, actively wrong for a missing `batchId`.

Separately, the completion log line for that same request read `→ 200`, not the `422` the client
actually received. `Program.cs` registered `UseExceptionHandler()` before `RequestLoggingMiddleware`,
so the exception unwound through the logging middleware's `finally` block — which reads
`context.Response.StatusCode`, still the untouched default at that point — before the exception handler
further out ever set the real status.

Fixed by declaring `batchId` as `string?` and validating it explicitly at the point of origin,
mirroring the numeric query parameter binding convention, and by moving `RequestLoggingMiddleware`'s
registration before `UseExceptionHandler()` so it wraps the exception handler rather than being wrapped
by it.

## Cleanup

No files are written, but the closing happy-path `apply` applies a real batch against the profile's
database, and the container carries the extra logging variables this test required. Restore the Fresh
profile before the next test rather than reusing this container.
