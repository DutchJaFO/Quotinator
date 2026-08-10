# #287 — Convert IQuoteService and its implementations to fully async

**Status:** Planning
**GitHub issue:** #287
**Tiers required:** T1, T2
**Depends on:** none (a pure signature conversion, isolated to `IQuoteService` and its call graph)

---

## Spec requirements

1. `IQuoteService`'s 5 methods return `Task<T>` instead of `T`.
2. `SqliteQuoteService` — every method and private helper becomes `async Task<T>`, using Dapper's
   async API (`QueryAsync`, `QueryFirstOrDefaultAsync`, `ExecuteScalarAsync`) throughout.
3. `QuoteService` (legacy flat-file) matches the new interface via `Task.FromResult(...)` — no real
   async work, confirmed unregistered in production DI.
4. `FakeQuoteService` matches the new interface.
5. `QuoteEndpoints.GetById`/`ConversationEndpoints.GetById` become async; the other 3
   `QuoteEndpoints` handlers (already async) add `await`.
6. `QuoteCard.razor.cs`'s `LoadQuote()` becomes async.
7. All direct test call sites (~51, across 5 files) get `await`; enclosing test methods become
   `async Task` where not already.
8. No behaviour change anywhere in this issue.

---

## Background — why this issue exists

See #285's own investigation: `JoinQueryRepository<TResult>` is async-only (matching
`IRepository<T>`/`IListableRepository<T>`'s existing async convention), but `IQuoteService`/
`SqliteQuoteService` predate that convention (`SqliteQuoteService`'s "v2 SQLite backend" commit,
2026-06-14, before the generic async Data infrastructure existed) and stayed fully synchronous.
#285 cannot inject `JoinQueryRepository` into `BuildLineResponse` without this conversion happening
first — traced precisely: `BuildLineResponse` → `BuildConversationResponse` → both `GetConversation`
and `GetRandom` (which also embeds a conversation) → the full interface, per developer decision
(2026-08-10) to convert consistently rather than leave a partially-async interface.

**Verified before starting:**

- Confirmed via `grep`: `IQuoteService` has exactly 2 real implementations
  (`SqliteQuoteService`, `QuoteService`) plus 1 test double (`FakeQuoteService`).
- Confirmed `QuoteService`'s own doc comment: "Nothing registers this service in the running app; the
  real implementation is `Quotinator.Core.Services.SqliteQuoteService`" — safe to convert its 5
  methods to trivial `Task.FromResult(...)` wraps with zero behaviour risk.
- Confirmed exact production call sites: `QuoteEndpoints.cs` (4: `GetRandom` line 269, `GetById` line
  296, `Search` line 381, `GetAll` line 456), `ConversationEndpoints.cs` (1: `GetById` line 100),
  `QuoteCard.razor.cs` (1: `LoadQuote` line 33). Of the 4 `QuoteEndpoints` handlers, 3 (`GetRandom`,
  `Search`, `GetAll`) are already `async Task<IResult>` for unrelated reasons (`EntityFilterParsing`
  calls) — only `GetById` needs its own signature changed; the other 3 just gain an `await`.
  `ConversationEndpoints.GetById` needs its own signature changed too.
- Confirmed `QuoteCard.razor`'s `@onclick="LoadQuote"` binding needs no markup change — Blazor's
  `EventCallback` accepts an async delegate transparently.
- Confirmed exact test call-site counts via `grep -c`: `QuoteServiceTests.cs` (3),
  `SqliteQuoteServiceConversationTests.cs` (15), `SqliteQuoteServiceSearchTests.cs` (14),
  `SqliteQuoteServiceTests.cs` (15), `SqliteQuoteServiceUnicodeSearchTests.cs` (4) — 51 total.

---

## Approach

Mechanical, bottom-up conversion: `IQuoteService` interface first, then both implementations, then
`FakeQuoteService`, then every consumer (endpoints, the Blazor component, test call sites). No
behaviour changes at any step — each `Task<T>`-returning method's body is either an `await`-based
rewrite of already-synchronous Dapper calls (`SqliteQuoteService`) or a trivial `Task.FromResult`
wrap (`QuoteService`, `FakeQuoteService`).

`SqliteQuoteService`'s own conversion is the only non-mechanical part — every `connection.Query<T>`/
`QueryFirstOrDefault<T>`/`ExecuteScalar<T>` call becomes its Dapper async equivalent with `await`, and
every method signature gains `async Task<...>`. `BuildConversationResponse`'s
`lineRows.Select(lr => BuildLineResponse(...))` becomes a loop (or `Task.WhenAll` if concurrent
execution is safe against a single `IDbConnection` — it is not, SQLite connections are not safe for
concurrent use from multiple threads, so a sequential `foreach` with `await` is required, not
`Task.WhenAll`).

---

## Files touched

- `src/Quotinator.Core/Services/IQuoteService.cs`
- `src/Quotinator.Core/Services/SqliteQuoteService.cs`
- `src/Quotinator.Core/Services/QuoteService.cs`
- `tests/Quotinator.Api.Tests/Fakes/FakeQuoteService.cs`
- `src/Quotinator.Api/Endpoints/QuoteEndpoints.cs`
- `src/Quotinator.Api/Endpoints/ConversationEndpoints.cs`
- `src/Quotinator.Api/Components/Controls/QuoteCard.razor.cs`
- `tests/Quotinator.Core.Tests/Services/QuoteServiceTests.cs`
- `tests/Quotinator.Core.Tests/Services/SqliteQuoteServiceConversationTests.cs`
- `tests/Quotinator.Core.Tests/Services/SqliteQuoteServiceSearchTests.cs`
- `tests/Quotinator.Core.Tests/Services/SqliteQuoteServiceTests.cs`
- `tests/Quotinator.Core.Tests/Services/SqliteQuoteServiceUnicodeSearchTests.cs`

---

## Steps

### 1. Convert IQuoteService, QuoteService, FakeQuoteService
**Status:** ⬜ Not started

### 2. Convert SqliteQuoteService
**Status:** ⬜ Not started

### 3. Convert endpoint handlers and QuoteCard.razor.cs
**Status:** ⬜ Not started

### 4. Convert test call sites
**Status:** ⬜ Not started

### 5. Verify
**Status:** ⬜ Not started

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `IQuoteService` and both implementations compile async, same behaviour | Build + test | Full solution build/test |
| 2 | ⬜ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — all pass |
| 3 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 4 | ⬜ | T2 — live container's quote/conversation endpoints and the QuoteCard component still work | Live (T2) | Docker smoke test |

---

## Notes

None yet.
