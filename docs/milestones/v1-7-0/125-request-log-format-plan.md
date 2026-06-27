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

### 1. Fix the double-quote

Combine `{Path}` and `{Query}` into a single `{Url}` property by concatenating `context.Request.Path` and `context.Request.QueryString.Value` before the `LogInformation` call. The full URL renders as one quoted token.

### 2. Add `[Api - Request]` subsystem prefix

The current line has no `[Subsystem - Phase]` prefix, violating `docs/logging.md`. Add `[Api - Request]` to match the prefix pattern used throughout the codebase.

### 3. Extend to all endpoints — no exclusions

The current middleware only logs `/api/v1/quotes/*`. This should be all endpoints. If an endpoint is being called, it must be visible in the log — whether it is a quote endpoint, admin endpoint, health check, or version endpoint.

Remove the path filter entirely:

```csharp
// Before: only quote endpoints
if (!context.Request.Path.StartsWithSegments("/api/v1/quotes"))
{
    await next();
    return;
}
```

After: no filter — every request enters the logging block. The middleware runs unconditionally.

### 4. Review the two-line-per-request pattern

Each API request currently emits two log lines (for quote endpoints):
- Endpoint handler: `[Api - Search] q="back" field=null limit=null type=[] lang=null`
- Middleware: `"GET" "/path""?query" → 200 in Xms`

Both are complementary and should be kept. With scope extended to all endpoints, the middleware line now also appears for admin and other routes. No duplication concern — endpoint handlers log semantic parameters, the middleware logs HTTP facts.

### 5. Document the secret-safety rule for what is captured

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

```
INF: Quotinator.Requests[] [Api - Request] GET /api/v1/quotes/search?q=back&genre=comedy → 200 in 11ms
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
| 1 | ⬜ | No double-quote between path and query string | Unit test | `RequestLogFormattingTests.LogLine_IncludesFullUrl_WithoutDoubleQuote` — assert rendered line contains `search?q=back`, not `search""?q=back` |
| 2 | ⬜ | `[Api - Request]` prefix present in log line | Unit test | `RequestLogFormattingTests.LogLine_HasApiRequestPrefix` — assert rendered line starts with `[Api - Request]` |
| 3 | ⬜ | Requests with no query string log cleanly (no trailing empty token) | Unit test | `RequestLogFormattingTests.LogLine_NoQuery_NoTrailingQuote` — assert path-only request has no trailing quote artifact |
| 4 | ⬜ | Admin routes are logged | Unit test | `RequestLogFormattingTests.AdminRoute_IsLogged` — assert `POST /api/v1/admin/database/reseed` produces a log line with method, path, status, duration |
| 5 | ⬜ | Health endpoint is logged | Unit test | `RequestLogFormattingTests.HealthEndpoint_IsLogged` — assert `GET /api/v1/health` produces a log line |
| 6 | ⬜ | `docs/logging.md` contains the secret-logging rules table | Code review | Rules table present; security rule is about captured data, not route filtering |
| 7 | ⬜ | No header values appear in any log line | Code review | `Program.cs` middleware logs only method, URL, status, duration — no `context.Request.Headers` access anywhere in the block |
| 8 | ⬜ | User starts app in VS; quote request, admin request, and health check all appear in log | Live | Hit `/api/v1/quotes/random`, `POST /api/v1/admin/database/seed/preview`, and `/api/v1/health` in sequence; confirm all three appear in log |
