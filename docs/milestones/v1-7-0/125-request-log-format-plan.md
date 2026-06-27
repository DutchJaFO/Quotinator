# Issue #125 — Fix request log format

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `feature/v1-7-0`  
**Tiers required:** T1

---

## Problem

The request log middleware emits a double-quote between the path and query string because `{Path}` and `{Query}` are separate structured properties and Serilog quotes each one:

```
"GET" "/api/v1/quotes/search""?q=back&genre=comedy" → 200 in 11ms
```

Root cause — `Program.cs`, inside the `logRequests` middleware block:

```csharp
requestLogger.LogInformation("{Method} {Path}{Query} → {Status} in {Ms}ms",
    context.Request.Method,
    context.Request.Path,
    context.Request.QueryString.Value,   // separate quoted token, no separator
    ...
```

Observed in the live HA add-on supervisor log on 2026-06-27 after updating to v1.7.0.

---

## Scope

### 1. Two-line format with per-request correlation ID

Replace the single post-request log line with two lines per request: one on arrival, one on completion.

Each request gets a short random ID (`Guid.NewGuid().ToString("N")[..8]` — 8 lowercase hex chars) that appears on both lines, making start/end pairs unambiguous even when long-running requests overlap with shorter ones.

**Format:**

```
[Api - Request] {id} {METHOD} {url}
[Api - Request] {id} {METHOD} {url} → {status} in {ms}ms
```

Both lines carry method and URL so either line is self-contained. The `→` on the end line distinguishes it visually from the start line.

`context.Request.QueryString.Value` is an empty string when there is no query string, so concatenating it with `Path` produces no trailing separator. See scope item 4 for the full class implementation.

### 2. Add `[Api - Request]` subsystem prefix

The current line has no `[Subsystem - Phase]` prefix, violating `docs/logging.md`. Add `[Api - Request]` to match the prefix pattern used throughout the codebase.

### 3. Extend to all endpoints — no exclusions

The current middleware only logs `/api/v1/quotes/*`. Remove the path filter entirely — every request enters the logging block unconditionally.

```csharp
// Before: only quote endpoints
if (!context.Request.Path.StartsWithSegments("/api/v1/quotes"))
{
    await next();
    return;
}
// After: remove this block entirely
```

Endpoint handlers (`[Api - Search]`, `[Api - Random]`, etc.) log parsed semantic parameters — `q="back" field=null type=[] lang=null`. These remain unchanged. They complement the middleware lines, which log HTTP-level facts.

### 4. Extract middleware to a testable class

The current middleware is an inline `app.Use(...)` lambda in `Program.cs`. Inline lambdas cannot be unit-tested — they require a full `WebApplicationFactory` to exercise. Extract to a named class so tests can instantiate it directly.

**New file:** `src/Quotinator.Api/Middleware/RequestLoggingMiddleware.cs`

```csharp
public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
        => _logger = logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var id  = Guid.NewGuid().ToString("N")[..8];
        var url = context.Request.Path + context.Request.QueryString.Value;

        _logger.LogInformation("[Api - Request] {Id} {Method} {Url}",
            id, context.Request.Method, url);

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        _logger.LogInformation("[Api - Request] {Id} {Method} {Url} → {Status} in {Ms}ms",
            id, context.Request.Method, url,
            context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
}
```

`Program.cs` registers and applies it conditionally:

```csharp
if (logRequests)
{
    builder.Services.AddSingleton<RequestLoggingMiddleware>();
    // ... after app.Build():
    app.UseMiddleware<RequestLoggingMiddleware>();
}
```

`IMiddleware` requires `AddSingleton` (or `AddScoped`) registration — it is not activated by convention alone.

### 5. Test infrastructure — `CaptureLogger<T>`

No new NuGet packages. Add one small test double to `tests/Quotinator.Api.Tests/`:

```csharp
// tests/Quotinator.Api.Tests/Helpers/CaptureLogger.cs
internal sealed class CaptureLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Add(formatter(state, exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
```

Tests use it like this:

```csharp
var logger     = new CaptureLogger<RequestLoggingMiddleware>();
var middleware = new RequestLoggingMiddleware(logger);
var context    = new DefaultHttpContext();

context.Request.Method = "GET";
context.Request.Path   = "/api/v1/health";

await middleware.InvokeAsync(context, ctx =>
{
    ctx.Response.StatusCode = 200;
    return Task.CompletedTask;
});

Assert.AreEqual(2, logger.Lines.Count);
StringAssert.Contains(logger.Lines[0], "GET /api/v1/health");
StringAssert.DoesNotMatch(logger.Lines[0], new Regex("→"));   // start line has no status
StringAssert.Contains(logger.Lines[1], "→ 200 in");
```

`DefaultHttpContext` is available via `Microsoft.AspNetCore.Http` — no additional package needed in the Api test project because `Microsoft.AspNetCore.Mvc.Testing` already pulls it in.

### 6. Document the secret-safety rule for what is captured

The security constraint is about **what data is captured**, not which routes are included. `POST /api/v1/admin/database/reset → 200 in 5ms` is safe to log — the path is not a secret; the API key is in the `X-Api-Key` header, which is never logged. The same applies to future `Authorization` and `Cookie` headers.

Add the secret-logging rules below to `docs/logging.md` so they apply to all future logging work in this codebase.

---

## Secret-logging rules (to be added to `docs/logging.md`)

These rules apply permanently to all log output in Quotinator:

| What | Rule |
|---|---|
| `X-Api-Key` header | Never log. Not the name, not the value. |
| `Authorization` header | Never log. Not the scheme, not the token. |
| `Cookie` header | Never log. Any cookie value may contain session or auth data. |
| `Set-Cookie` response header | Never log. |
| Query parameters on admin/auth routes | Never log. Auth tokens are sometimes passed as query parameters; the path filter must prevent these routes from entering any logging middleware. |
| Request body | Never log. May contain credentials, PII, or import data. |
| `User-Agent` value | Safe to log — it is identification, not authentication. |
| Quote search parameters (`q`, `field`, `type`, `genre`, `lang`) | Safe to log — they are non-sensitive search terms. |

**The security boundary is what is captured, not which routes are included.** Never log header values — `X-Api-Key`, `Authorization`, `Cookie`, `Set-Cookie`. The path and query string of any endpoint are safe to log. If query parameters ever carry secrets (e.g. `?token=...`), exclude only those parameters — do not exclude the route.

---

## Expected log output after fix

Single request:
```
11:00:00.000  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=back&genre=comedy
11:00:00.011  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=back&genre=comedy → 200 in 11ms
```

Overlapping requests:
```
11:00:00.000  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=love
11:00:00.001  [Api - Request] e5f6a7b8 GET /api/v1/health
11:00:00.002  [Api - Request] e5f6a7b8 GET /api/v1/health → 200 in 2ms
11:00:00.014  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=love → 200 in 14ms
```

Useful greps:
```bash
grep "a1b2c3d4"     # find start + end for one request
grep "→ 500"        # all failures
grep "→ 429"        # rate-limited requests
grep "in [0-9]\{4\}" # requests taking 1 second or more
```

---

## Out of scope

- Do not add a structured logging library (e.g. `UseSerilogRequestLogging`) — the hand-rolled middleware needs the health-exclusion filter and must never log headers
- Do not log request headers — never log `X-Api-Key`, `Authorization`, `Cookie`, or `Set-Cookie`; `User-Agent` is captured via `ICallerContext` in the audit trail (#73), not here

---

## Request log vs. audit trail — important distinction

These are two separate outputs with different purposes and different security constraints:

| | Request log (`logRequests` middleware) | Audit trail (`AuditEntries` table) |
|---|---|---|
| **Output** | HA supervisor log (human-readable text) | SQLite database (queryable records) |
| **Admin routes** | **Included** — all endpoints are logged | **Included** — `reseed`, `reset`, and future admin actions are explicitly audited |
| **What is stored** | Method, URL, status code, duration | Operation name, agent identity (`User-Agent`), timestamp |
| **Secrets** | Never stored — path filter is a security boundary | Never stored — API key is authenticated but never recorded; only the agent is |

All endpoints appear in the request log — the request log confirms that the endpoint was called. The audit trail records what was done (the operation and who triggered it). See issue #73.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Start line emitted on request arrival (before `next()`) | Unit test | `RequestLogFormattingTests.StartLine_EmittedBeforeResponse` — assert first log line has no `→` and no status code |
| 2 | ⬜ | End line contains URL, status, and duration | Unit test | `RequestLogFormattingTests.EndLine_ContainsStatusAndDuration` — assert second log line contains `→ 200 in` and `ms` |
| 3 | ⬜ | Start and end lines share the same correlation ID | Unit test | `RequestLogFormattingTests.BothLines_ShareCorrelationId` — assert the 8-char hex token is identical on both lines |
| 4 | ⬜ | URL combines path and query string without double-quote | Unit test | `RequestLogFormattingTests.Url_PathAndQueryCombined` — assert rendered URL contains `search?q=back` (no `""` between path and query) |
| 5 | ⬜ | Path-only requests log cleanly (no trailing separator) | Unit test | `RequestLogFormattingTests.Url_NoQuery_NoTrailingSeparator` — assert `/api/v1/health` with no query produces no trailing character |
| 6 | ⬜ | All endpoints are logged — including health and admin | Unit test | `RequestLogFormattingTests.AllRoutes_AreLogged` — assert `GET /api/v1/health` and `POST /api/v1/admin/database/reseed` both produce two log lines |
| 7 | ⬜ | `[Api - Request]` prefix present on both lines | Unit test | `RequestLogFormattingTests.BothLines_HavePrefix` — assert both start and end lines begin with `[Api - Request]` |
| 8 | ⬜ | No header values appear in any log line | Code review | `Program.cs` middleware block accesses only `Method`, `Path`, `QueryString`, `StatusCode`, and elapsed ms — no `Headers` access |
| 9 | ⬜ | `docs/logging.md` updated to reflect two-line format | Code review | Observability overview and request log section show the two-line format with correlation ID |
| 10 | ⬜ | User starts app in VS; overlapping request pairs visible and correlatable in log | Live | Hit several endpoints in quick succession; confirm start and end lines appear for each; confirm IDs match across pairs |
