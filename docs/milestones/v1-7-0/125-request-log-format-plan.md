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

1. **Fix the double-quote** — combine `{Path}` and `{Query}` into a single `{Url}` property (concatenate `context.Request.Path` and `context.Request.QueryString.Value` before passing to `LogInformation`) so the full URL renders as one quoted token.

2. **Add `[Api - Request]` subsystem prefix** — the current line has no `[Subsystem - Phase]` prefix, violating the logging standards in `docs/logging.md`. Add `[Api - Request]` to match the prefix pattern used elsewhere.

3. **Review the two-line-per-request pattern** — each API request currently emits two log lines:
   - Endpoint handler: `[Api - Search] q="back" field=null limit=null type=[] lang=null`
   - Middleware: `"GET" "/path""?query" → 200 in Xms`
   
   Decide whether both are needed and whether either carries redundant or missing context. The endpoint log captures parsed parameters; the middleware log captures HTTP-level facts (method, URL, status, duration). Both are useful and complementary — keep both, but ensure neither duplicates what the other already records.

**Out of scope:**
- Do not add a structured logging library (e.g. `UseSerilogRequestLogging`) — the hand-rolled middleware filters to `/api/v1/quotes/*` intentionally
- Do not change *what* is logged — only the format

---

## Expected log output after fix

```
INF: Quotinator.Requests[] [Api - Request] GET /api/v1/quotes/search?q=back&genre=comedy → 200 in 11ms
```

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | No double-quote between path and query string | Unit test | `RequestLogFormattingTests.LogLine_IncludesFullUrl_WithoutDoubleQuote` — assert rendered log line contains `search?q=back` not `search""?q=back` |
| 2 | ⬜ | `[Api - Request]` prefix present in log line | Unit test | `RequestLogFormattingTests.LogLine_HasApiRequestPrefix` — assert rendered line starts with `[Api - Request]` |
| 3 | ⬜ | Requests with no query string log cleanly (no trailing `""`") | Unit test | `RequestLogFormattingTests.LogLine_NoQuery_NoTrailingQuote` — assert line for path-only request has no trailing empty token |
| 4 | ⬜ | User starts app in VS; request log lines are readable in the output window | Live | Start app, hit `/api/v1/quotes/random` in browser, confirm log line in VS output matches expected format |
