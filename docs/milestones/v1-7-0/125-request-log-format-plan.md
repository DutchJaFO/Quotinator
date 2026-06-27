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

### 3. Review the two-line-per-request pattern

Each API request currently emits two log lines:
- Endpoint handler: `[Api - Search] q="back" field=null limit=null type=[] lang=null`
- Middleware: `"GET" "/path""?query" → 200 in Xms`

Both are complementary — the endpoint log captures parsed semantic parameters, the middleware log captures HTTP-level facts (method, URL, status, duration). Keep both. Ensure neither duplicates what the other records.

### 4. Document and enforce the secret-safety boundary (new)

The current middleware filters to `/api/v1/quotes/*` only. This is a **security boundary**: admin routes (`/api/v1/admin/*`) carry the `X-Api-Key` header and must never be logged. Future auth routes will carry `Authorization` and `Cookie` headers. The filter being implicit and undocumented is a hazard — widening it without understanding the consequence would leak secrets.

Changes required:
- Add a named constant or clear comment in `Program.cs` that describes the path filter as a **deliberate security boundary**, not just a scope filter.
- Add the secret-logging rules below to `docs/logging.md` so they apply to all future logging work in this codebase, not just this middleware.

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

**The `/api/v1/quotes/*` path filter is a security boundary.** Any change that widens the logged path scope (e.g. to `/api/v1/*` or `*`) must explicitly verify that no secrets can enter the log via headers, query parameters, or request bodies on the newly-included routes.

---

## Expected log output after fix

```
INF: Quotinator.Requests[] [Api - Request] GET /api/v1/quotes/search?q=back&genre=comedy → 200 in 11ms
```

---

## Out of scope

- Do not add a structured logging library (e.g. `UseSerilogRequestLogging`) — the hand-rolled middleware filters to `/api/v1/quotes/*` intentionally and the filter is a security boundary
- Do not log request headers — `User-Agent` is captured via `ICallerContext` in the audit trail (#73), not by duplicating it here

---

## Request log vs. audit trail — important distinction

These are two separate outputs with different purposes and different security constraints:

| | Request log (`logRequests` middleware) | Audit trail (`AuditEntries` table) |
|---|---|---|
| **Output** | HA supervisor log (human-readable text) | SQLite database (queryable records) |
| **Admin routes** | **Excluded** — `X-Api-Key` must never appear in log files | **Included** — `reseed`, `reset`, and future admin actions are explicitly audited |
| **What is stored** | Method, URL, status code, duration | Operation name, agent identity (`User-Agent`), timestamp |
| **Secrets** | Never stored — path filter is a security boundary | Never stored — API key is authenticated but never recorded; only the agent is |

Admin routes being absent from the request log does not mean admin actions go unrecorded. See issue #73.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | No double-quote between path and query string | Unit test | `RequestLogFormattingTests.LogLine_IncludesFullUrl_WithoutDoubleQuote` — assert rendered line contains `search?q=back`, not `search""?q=back` |
| 2 | ⬜ | `[Api - Request]` prefix present in log line | Unit test | `RequestLogFormattingTests.LogLine_HasApiRequestPrefix` — assert rendered line starts with `[Api - Request]` |
| 3 | ⬜ | Requests with no query string log cleanly (no trailing empty token) | Unit test | `RequestLogFormattingTests.LogLine_NoQuery_NoTrailingQuote` — assert path-only request has no trailing quote artifact |
| 4 | ⬜ | Admin routes are never entered by the request log middleware | Unit test | `RequestLogFormattingTests.AdminRoute_IsNotLogged` — assert `GET /api/v1/admin/audit` produces no log line |
| 5 | ⬜ | `docs/logging.md` contains the secret-logging rules table | Code review | Rules table present; path filter documented as security boundary |
| 6 | ⬜ | Secret-safety boundary is documented in `Program.cs` at the filter | Code review | Comment at the path filter names the security constraint explicitly |
| 7 | ⬜ | User starts app in VS; request log lines are readable in the output window | Live | Start app, hit `/api/v1/quotes/random` in browser; confirm log line matches expected format; confirm no admin route appears in log |
