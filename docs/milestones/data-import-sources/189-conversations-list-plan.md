# #189 — Conversations: GET /api/v1/conversations list endpoint

**Status:** Waiting for release
**GitHub issue:** #189
**Tiers required:** T1, T2
**Depends on:** #193, #195

---

## Spec requirements (corrected during planning review 2026-07-19)

1. `GET /api/v1/conversations` — paginated list, using `IListableRepository<ConversationEntity>`
   (already DI-registered — see Background) directly, bypassing `IQuoteService`/`Quotinator.Core`
   entirely, mirroring #184–#188's masterdata endpoints and `AdminEndpoints`'s `/audit` handler — **not**
   `PageResponse<T>` (that type does not exist) and **not** the "shared pagination-clamp helper" (that
   helper does not clamp) — use `Quotinator.Data.Models.PagedItems<T>` +
   `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (rejects out-of-range `page`/`pageSize` with
   422; never clamps), exactly as #184–#188 do.
2. Response items are a new `ConversationSummaryResponse` DTO (`Quotinator.Api.Models` — **not** a reuse
   of `Quotinator.Core.Models.ConversationResponse`, which is a different, heavier shape already owned by
   the existing `GET /{id}` — see Background) with `Id`, `Description`, `CompletenessStatus`, and
   `LineCount` — never the full ordered line list.
3. `LineCount` is populated via a new join/aggregate query against `ConversationLines`, exposed through
   a new `IConversationLineCountReader` (single-id form for `GetById`... actually for `GetAll` only — see
   Background for why no `GetById` counterpart is needed), batched once per page (one query for the
   whole page, not one per row) — the same "resolver, not the generic repository" pattern #184's
   `ISourceSeriesReferenceReader`, #185's `ICharacterSourceLinkReader`, and #187's
   `ISeriesUniverseReferenceReader` each independently established this session. `LineCount` is a plain
   `int`, not a `MasterDataReference` — this is an aggregate count, not a reference to another masterdata
   entity, so CLAUDE.md's "Masterdata reference shape" convention does not apply here (see Background).
4. The new endpoint is added to the *existing* `ConversationEndpoints.cs` file (`GetAll` handler +
   `group.MapGet("/", GetAll)` line) — **not** a new file. Every prior masterdata issue in this batch
   (#184–#188) created a brand-new `{Entity}Endpoints.cs` because none of those entities had any endpoint
   before; Conversations already has `GetById` in `ConversationEndpoints.cs`, so this issue extends that
   file instead of duplicating the route-group setup.
5. Route registration order (`/` vs `/{id}`) needs no special handling — confirmed non-issue, see
   Background. This differs from `QuoteEndpoints`'s `/search`-before-`/{id}` precedent, which exists
   because `/search` and `/{id}` are both single-segment templates that *do* collide; `/` (zero segments
   after the group prefix) and `/{id}` (one segment) never collide regardless of registration order.
6. Both endpoints keep `RateLimitPolicies.Api` and `ApiTags.Conversations` (**not** `ApiTags.MasterData`
   — Conversations is a masterdata *consumer*, not a masterdata entity, per CLAUDE.md's "Masterdata
   routing convention" and the issue's own correct framing) — `[Description]` attributes on the new
   endpoint.
7. No entity-specific filters yet — deferred per CLAUDE.md's "Entity-scoped filter-parameter convention"
   (#196), same as every other issue in this batch.
8. `api/v1/conversations`'s `page`/`pageSize` must be registered in
   `NumericParameterSchemaTransformer.NumericParamsByPath` — **not mentioned in the issue's original
   "What needs to be done" list at all**, found during this planning review (the same gap #184–#188 each
   independently found for their own paths).
9. Per CLAUDE.md's "Standard pagination contract", the endpoint must ship with the full eight-case
   pagination test matrix, not only the three tests the issue itself lists.
10. Update `README.md` and `addon/DOCS.md`'s endpoint tables, and register the new `[Api - GetAllConversations]`
    log prefix in `docs/logging.md` (every #184–#188 endpoint did this).

---

## Background — why this issue exists

`GET /api/v1/conversations/{id}` already exists (`ConversationEndpoints.cs`, #69) and returns a
conversation's full ordered line list. There is no `GET /` list endpoint — confirmed via a direct read of
`ConversationEndpoints.cs` and every `Sql.Conversations.*`/`Sql.ConversationLines.*` query, all of which
are single-row, id-scoped. Conversations is a consumer of masterdata (built from Sources/Characters/
People via its lines), not a masterdata entity itself, so this endpoint deliberately stays under the
existing `/api/v1/conversations` route and `ApiTags.Conversations` tag rather than moving to
`/masterdata/` — CLAUDE.md's "Masterdata routing convention" section states this explicitly ("Conversations
is a consumer of masterdata... it does not move under `/masterdata/`").

**Verified before starting** (per this project's standing rule — every issue in #183's sub-issue set has
had at least one factual error caught this way):

- The issue's `PageResponse<T>` does not exist. Confirmed the real type is
  `Quotinator.Data.Models.PagedItems<T>`, same correction every other issue in this batch needed.
- The issue's "shared pagination-clamp helper" does not exist as a clamp. Confirmed
  `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (`src/Quotinator.Api/Endpoints/Shared/
  PaginationParsing.cs`) reject out-of-range `pageSize`/`page` with 422 rather than clamping.
- **`IListableRepository<ConversationEntity>` is already DI-registered** (`Program.cs:313` —
  `builder.Services.AddSingleton<IListableRepository<ConversationEntity>>(sp =>
  (IListableRepository<ConversationEntity>)sp.GetRequiredService<IRestorableRepository
  <ConversationEntity>>());`) — no new DI registration is needed for the repository itself, only for the
  new `IConversationLineCountReader`.
- **`ConversationEntity` fields re-verified directly** (`src/Quotinator.Engine/Entities/
  ConversationEntity.cs`): `Description` (`string?`), `ImportBatchId` (`Guid?`), `CompletenessStatus`
  (`SafeValue<CompletenessStatus?>`), `NoValueKnown` (`IReadOnlyList<string>`), plus `RecordBase`'s
  `Id`/`DateCreated`/`DateModified`/`DateDeleted`/`IsDeleted`. **No FK to another masterdata entity** —
  confirmed by reading the full file — so no `MasterDataReference`-typed field is needed anywhere on
  `ConversationSummaryResponse`, unlike #184/#185/#187.
- **`GetById` uses `IQuoteService` (`Quotinator.Core`), not a repository directly** — deliberately kept
  that way; this issue does not touch `GetById`. The new `GetAll` bypasses `IQuoteService` entirely and
  calls `IListableRepository<ConversationEntity>` directly, mirroring #184–#188 and `AdminEndpoints`'s
  `/audit` handler — a summary/count view has no reason to route through the heavier line-assembly logic
  `IQuoteService.GetConversation` performs for the full-detail `GetById` response.
- **Naming collision, not previously flagged**: `Quotinator.Core.Models.ConversationResponse` already
  exists (the full-detail `GetById` shape — `Id`, `Description`, `Lines`). The new summary DTO cannot
  reuse that name or that namespace — it needs a distinct name (`ConversationSummaryResponse`) and lives
  in `Quotinator.Api.Models` alongside #184–#188's response DTOs (no service layer sits between the new
  `GetAll` handler and the repository, matching the same placement rule those five issues established:
  folder name = namespace segment, `Quotinator.Api.Models` for endpoints with no service layer in
  between).
- **`LineCount` query precedent found, adaptable directly**: `Sql.ConversationLines.SelectMembershipForQuote`
  (`src/Quotinator.Engine/Queries/Sql.cs:540-545`) already computes a per-conversation `TotalLines` via a
  correlated `COUNT(*)` subquery (`(SELECT COUNT(*) FROM ConversationLines cl2 WHERE
  cl2.ConversationId = cl.ConversationId AND cl2.IsDeleted = 0) AS TotalLines`) for a different purpose
  (a quote's own conversation memberships). This issue needs the equivalent count keyed the other
  direction — one row per Conversation, not per membership — via a new, batched `GROUP BY` query (see
  Step 1), following the established "resolver, not generic repository" pattern.
- **No `GetById`-side line-count reader is needed.** `ConversationResponse.Lines` (the full ordered line
  list `GetById` already returns) makes any separate `LineCount` field on that response redundant — a
  caller can just take `Lines.Count`. `IConversationLineCountReader` therefore only needs a *batched*
  form for `GetAll`'s page, not a single-id form for `GetById` — unlike #184/#185/#187's readers, which
  needed both. Confirmed by re-reading `ConversationResponse`/`GetById`'s existing code — no change to
  either is in scope for this issue.
- **Route registration order confirmed a non-issue**: `group.MapGet("/", GetAll)` (zero path segments
  after `/api/v1/conversations`) and the existing `group.MapGet("/{id}", GetById)` (one path segment)
  cannot collide under ASP.NET Core's routing regardless of registration order — this differs from
  `QuoteEndpoints`'s `/search`-before-`/{id}` precedent (CLAUDE.md's "Route registration order" section),
  which exists specifically because `/search` and `/{id}` are both single-segment templates. The issue's
  own text already anticipated this correctly ("unlikely here since `/` and `/{id}` don't collide"); this
  plan confirms it directly rather than leaving it as a hedge.
- **`NumericParameterSchemaTransformer.NumericParamsByPath` registration is a real, un-flagged
  requirement** — the same gap #184–#188 each independently found for their own paths. Not mentioned in
  the issue's own text. Added as its own step below.
- **CLAUDE.md's eight-case pagination test matrix is not covered by the issue's own "Expected tests"
  table** — it lists 3 tests total, none of which cover `page=0`, malformed `page`/`pageSize`, negative
  `pageSize`, `pageSize` above 500, `pageSize=0` effective-size reporting, or page-beyond-last. Expanded
  in Step 5 below.
- **`ApiMessages.ConversationNotFound`/`ErrorConversationNotFound` already exist** (#69) — no new
  not-found message is needed; a list endpoint has no not-found case (an empty page is a valid 200, not
  a 404).
- **`ApiTags.Conversations` and `RateLimitPolicies.Api` already confirmed** in use by the existing
  `MapConversationEndpoints()` — no change needed to either.
- **Masterdata reference shape / soft-delete visibility — confirmed not applicable / already structural**,
  per this session's convention (CLAUDE.md): `ConversationEntity` has no FK-valued field to another
  masterdata entity (see field list above), so no `MasterDataReference` is introduced. Soft-deleted
  visibility is already satisfied structurally with no new work: `RepositorySql.SelectPage`/`SelectById`
  already filter `IsDeleted = 0` unconditionally, so a soft-deleted Conversation is already invisible via
  the new endpoint for free. The new `IConversationLineCountReader` query must independently filter
  `ConversationLines.IsDeleted = 0` when counting (already planned in Step 1's SQL) — a soft-deleted line
  must not inflate `LineCount`, matching the same principle (though this is a line-count correctness
  concern, not a dangling-reference concern, since there's no reference being resolved here).

Conventions and infrastructure only — no new DI registration for the repository itself (already shipped
by #193), no schema change, no `ApiMessages`/i18n change (reusing #69's existing `ConversationNotFound`
is irrelevant here since a list never 404s).

---

## Steps

### 1. Two new `Sql.ConversationLines` queries + `IConversationLineCountReader`

**Status:** Done.

Add to `internal static class ConversationLines` in `src/Quotinator.Engine/Queries/Sql.cs`, alongside
`SelectMembershipForQuote`. Batched form only (see Background for why no single-id form is needed):

```csharp
/// <summary>
/// Active line counts for a batch of Conversations in a single round-trip — #189's list join, avoiding
/// one query per row across a page. A Conversation with zero active lines is simply absent from the
/// result — callers default missing keys to 0.
/// </summary>
internal const string SelectLineCountsForConversations =
    "SELECT cl.ConversationId, COUNT(*) AS LineCount FROM ConversationLines cl " +
    "WHERE cl.ConversationId IN @conversationIds AND cl.IsDeleted = 0 " +
    "GROUP BY cl.ConversationId;";
```

New files `src/Quotinator.Engine/Repositories/IConversationLineCountReader.cs`/
`ConversationLineCountReader.cs`, namespace `Quotinator.Engine.Repositories` (same folder #184/#185/#187
introduced):

```csharp
namespace Quotinator.Engine.Repositories;

/// <summary>Resolves each Conversation's active line count for masterdata-style summary read endpoints
/// (#189) — never writes.</summary>
public interface IConversationLineCountReader
{
    /// <summary>Active line counts for each of the given Conversations, in one round-trip. A Conversation
    /// with zero active lines is absent from the result rather than mapped to a zero entry — callers
    /// default missing keys to 0.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetLineCountsForManyAsync(IReadOnlyList<Guid> conversationIds);
}
```

Implementation mirrors #184's `SourceSeriesReferenceReader`'s batch-form shape (Dapper `QueryAsync`
against `IDbConnectionFactory`, a private `record` row type). Register in `Program.cs` alongside the
other repository registrations:
```csharp
builder.Services.AddSingleton<IConversationLineCountReader, ConversationLineCountReader>();
```

### 2. `ConversationSummaryResponse` DTO

**Status:** Done.

New file `src/Quotinator.Api/Models/ConversationSummaryResponse.cs`, namespace `Quotinator.Api.Models`:

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for one item in the paginated Conversations list — a lighter summary
/// than the full ordered line list <c>GET /api/v1/conversations/{id}</c> returns
/// (<see cref="Quotinator.Core.Models.ConversationResponse"/>), to avoid loading every conversation's
/// full line list (with translations) on a single paginated page.</summary>
public sealed class ConversationSummaryResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>Optional human-readable label for the conversation.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }

    /// <summary>The number of active lines in this conversation. Fetch the full ordered line list via
    /// <c>GET /api/v1/conversations/{id}</c> for more detail.</summary>
    public required int LineCount { get; init; }
}
```

A private mapping method in `ConversationEndpoints` performs the flattening:

```csharp
private static ConversationSummaryResponse ToSummaryResponse(ConversationEntity entity, int lineCount) => new()
{
    Id                 = entity.Id.ToString("D").ToUpperInvariant(),
    Description        = entity.Description,
    CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    LineCount          = lineCount,
};
```

### 3. Extend `ConversationEndpoints.cs` with `GetAll`

**Status:** Done.

Add to the existing `src/Quotinator.Api/Endpoints/ConversationEndpoints.cs` (do not create a new file —
see Background):

```csharp
group.MapGet("/", GetAll)
     .WithName("GetAllConversations")
     .WithSummary("List conversations")
     .WithDescription(
         "Returns a paginated list of conversation summaries — Id, Description, CompletenessStatus, and " +
         "line count. Fetch the full ordered line list via GET /{id}. See CLAUDE.md's \"Standard " +
         "pagination contract\" for page/pageSize semantics.");
```

```csharp
private static async Task<IResult> GetAll(
    IApiLocalizer localizer,
    ILogger<Log> logger,
    IListableRepository<ConversationEntity> repository,
    IConversationLineCountReader lineCountReader,
    [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
    [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
{
    logger.LogInformation("[Api - GetAllConversations] page={Page} pageSize={PageSize}", page, pageSize);

    if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
        return pageError!;

    var result = await repository.GetPageAsync(pageValue, pageSizeValue);

    var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
    if (beyondLast is not null)
        return beyondLast;

    var conversationIds  = result.Items.Select(c => c.Id).ToList();
    var lineCountsById    = await lineCountReader.GetLineCountsForManyAsync(conversationIds);

    var items = result.Items
        .Select(c => ToSummaryResponse(c, lineCountsById.TryGetValue(c.Id, out var count) ? count : 0))
        .ToList();

    var response = new PagedItems<ConversationSummaryResponse>(items, result.Page, result.PageSize, result.TotalCount);
    return Results.Ok(response);
}
```

`IListableRepository<ConversationEntity>` and `IConversationLineCountReader` join the existing `GetById`
handler's dependencies as new parameters on `GetAll` only — `GetById` is untouched.

### 4. Register the OpenAPI numeric-param transformer path

**Status:** Done.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath`:
```csharp
["api/v1/conversations"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```
Without this, `page`/`pageSize` publish as bare `string` in the OpenAPI spec instead of `integer|null` —
the same gap #184–#188 each independently found for their own paths.

### 5. Test fixtures and tests

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Fakes/FakeConversationRepository.cs`, implementing
`IListableRepository<ConversationEntity>`, mirroring #184's `FakeSourceRepository` shape (in-memory
`List<ConversationEntity>`, real effective-`pageSize` paging contract, minimal working write methods).

New file `tests/Quotinator.Api.Tests/Fakes/FakeConversationLineCountReader.cs`, implementing
`IConversationLineCountReader`. Backed by a constructor-supplied
`IReadOnlyDictionary<Guid, int>` (Conversation id → line count); a Conversation id absent from the
dictionary resolves to 0 via the reader's documented "absent means zero" contract, mirroring #184's
`FakeSourceSeriesReferenceReader` precedent.

Extend the existing `tests/Quotinator.Api.Tests/Endpoints/ConversationEndpointsTests.cs` — add
`FakeConversationRepository`/`FakeConversationLineCountReader` registrations to `CreateFactory()`
alongside the existing `FakeQuoteService`/`NoOpDatabaseInitializer` registrations (do not create a new
test class file, matching Step 3's "extend, don't duplicate" decision).

Tests, combining the issue's own three with the full eight-case pagination matrix and the coverage gaps
found this session:

- `GetAllConversations_ReturnsPaginatedResults` (issue)
- `GetAllConversations_ReturnsSummaryNotFullLineList` (issue) — asserts the response has no `lines`
  property, only `id`/`description`/`completenessStatus`/`lineCount`
- `GetAllConversations_LineCountMatchesActualLineCount` (issue)
- `GetAllConversations_ConversationWithNoLines_ReturnsZeroLineCount` — proves the "missing key defaults
  to 0" contract from Step 1's `GetLineCountsForManyAsync` doc comment
- `GetAllConversations_MultipleConversationsWithLines_BatchResolvesEachCount` — proves the batched path
  maps counts back to the right conversation, not just that the reader returns data in isolation
- `GetAllConversations_PageZero_Returns422`
- `GetAllConversations_PageMalformed_Returns422`
- `GetAllConversations_PageSizeMalformed_Returns422`
- `GetAllConversations_PageSizeNegative_Returns422`
- `GetAllConversations_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllConversations_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllConversations_PageSizeOmitted_DefaultsTo20`
- `GetAllConversations_PageBeyondLast_Returns422DistinctDetail`
- `ConversationEndpoints_OnLiveSpec_GetAllTaggedConversations` — extends `OpenApiSpecEndpointTests` (or a
  small dedicated assertion), proving `GetAll` is tagged `ApiTags.Conversations` (not `MasterData`) and
  rate-limited `RateLimitPolicies.Api`, live rather than by code inspection only

**Response shape assertion** (proving Step 2's `ConversationSummaryResponse` design actually prevents the
`SafeValue<T>` leak, matching #184–#188's identical assertion for their own `SafeValue<T>` fields):
`GetAllConversations_ReturnsPaginatedResults` must additionally assert `completenessStatus` serializes as
a plain JSON string value, never `{"raw":...,"parsed":...}`.

**Extend existing shared-infrastructure test files:**
- `tests/Quotinator.Api.Tests/OpenApi/NumericParameterSchemaTransformerTests.cs` — new case(s) for
  `api/v1/conversations`.
- `tests/Quotinator.Api.Tests/OpenApi/OpenApiSpecEndpointTests.cs` — two new `[DataRow]` entries:
  `("/api/v1/conversations", "page")`, `("/api/v1/conversations", "pageSize")`.

### 6. Documentation

**Status:** Done.

Add a row for `GET /api/v1/conversations` to the REST API Endpoints table in both `README.md` and
`addon/DOCS.md`, following the existing row format/placement (immediately above the existing
`GET /api/v1/conversations/{id}` row). Add the new `[Api - GetAllConversations]` prefix to
`docs/logging.md`'s "Defined prefixes" table (per every #184–#188 precedent and CLAUDE.md's Logging
Standards boyscout rule).

### 7. Solution file

**Status:** Done.

Add the new files (`src/Quotinator.Engine/Repositories/IConversationLineCountReader.cs`/
`ConversationLineCountReader.cs`, `src/Quotinator.Api/Models/ConversationSummaryResponse.cs`,
`tests/Quotinator.Api.Tests/Fakes/FakeConversationRepository.cs`/`FakeConversationLineCountReader.cs`) to
`Quotinator.slnx` if not automatically picked up by the existing project globs — verify by opening the
solution (per #184–#188's precedent, this has not been needed for any `.cs` file landing inside an
existing project folder, but check regardless).

### 8. Verify

**Status:** Done for the automated portion. `dotnet build --configuration Release` → 0 warnings, 0
errors. `dotnet test --configuration Release --verbosity normal` → full solution (10 projects, 1475
tests) green, 0 warnings, 0 errors. Confirmed every new/extended test starts red: temporarily reverted
the `group.MapGet("/", GetAll)` line — 14 of 20 tests in `ConversationEndpointsTests` failed (the new
`GetAll*`/live-spec tests; the 6 pre-existing `GetById_*` tests stayed green), then restored the line and
re-ran to confirm all 20 pass. T1/T2 not yet run — see header.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full suite green, 0 warnings, 0 errors. Confirm all listed expected tests started
red before implementation (e.g. by temporarily reverting the new `group.MapGet("/", GetAll)` line).

T2 (Docker): `docker build` + `docker run`, then:
```bash
curl -s "http://localhost:8080/api/v1/conversations"
curl -s "http://localhost:8080/api/v1/conversations?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/conversations?pageSize=999"
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"conversations[^"]*"'
```
Confirm: default list returns 200 with `items`/`page`/`pageSize`/`totalCount`/`totalPages`, and each item
carries `lineCount` (no `lines` array); `pageSize=0` returns every Conversation as one page; `pageSize=999`
returns 422; the OpenAPI spec publishes `page`/`pageSize` as `integer|null` on `api/v1/conversations`.
Also confirm the existing `GET /api/v1/conversations/{id}` still returns the full `lines` array unchanged
(regression check that `GetById` was genuinely untouched) and that a real Conversation with a known line
count (via `Quotinator.Tools.DbInspector`) shows the matching `lineCount` in the list response.

This project always runs T2 regardless of a documented trigger — this issue's own change to `Program.cs`
(the new `IConversationLineCountReader` registration) also independently satisfies
`docs/release-verification.md`'s "touches Program.cs startup" trigger.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `GET /api/v1/conversations` returns a paginated list of summaries | Unit test | `ConversationEndpointsTests.GetAllConversations_ReturnsPaginatedResults` |
| 2 | ✅ | Response items carry `lineCount`, never the full `lines` array | Unit test | `ConversationEndpointsTests.GetAllConversations_ReturnsSummaryNotFullLineList` |
| 3 | ✅ | `lineCount` matches the actual active line count | Unit test | `ConversationEndpointsTests.GetAllConversations_LineCountMatchesActualLineCount` |
| 4 | ✅ | A Conversation with no lines returns `lineCount: 0`, not omitted/null | Unit test | `ConversationEndpointsTests.GetAllConversations_ConversationWithNoLines_ReturnsZeroLineCount` |
| 5 | ✅ | The list endpoint resolves each item's count independently via the batched reader | Unit test | `ConversationEndpointsTests.GetAllConversations_MultipleConversationsWithLines_BatchResolvesEachCount` |
| 6 | ✅ | `page=0` returns 422 | Unit test | `ConversationEndpointsTests.GetAllConversations_PageZero_Returns422` |
| 7 | ✅ | Malformed `page`/`pageSize` returns 422 | Unit test | `GetAllConversations_PageMalformed_Returns422`, `_PageSizeMalformed_Returns422` |
| 8 | ✅ | Negative `pageSize` returns 422 | Unit test | `ConversationEndpointsTests.GetAllConversations_PageSizeNegative_Returns422` |
| 9 | ✅ | `pageSize > 500` returns 422, never clamped | Unit test | `ConversationEndpointsTests.GetAllConversations_PageSizeAbove500_Returns422NotSilentClamp` |
| 10 | ✅ | `pageSize = 0` returns every row as one page | Unit test | `ConversationEndpointsTests.GetAllConversations_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 11 | ✅ | Omitted `pageSize` defaults to 20 | Unit test | `ConversationEndpointsTests.GetAllConversations_PageSizeOmitted_DefaultsTo20` |
| 12 | ✅ | A page beyond the last returns 422 with a distinct detail | Unit test | `ConversationEndpointsTests.GetAllConversations_PageBeyondLast_Returns422DistinctDetail` |
| 13 | ✅ | `completenessStatus` serializes as a plain JSON value, never `{raw, parsed}` | Unit test | `ConversationEndpointsTests.GetAllConversations_ReturnsPaginatedResults` (shape assertion) |
| 14 | ✅ | `page`/`pageSize` publish as `integer` in the OpenAPI spec for `api/v1/conversations` | Unit test | `NumericParameterSchemaTransformerTests` (new cases), `OpenApiSpecEndpointTests.PageParam_OnLiveSpec_PublishesIntegerType` (new `DataRow`s) |
| 15 | ✅ | `GetAll` tagged `ApiTags.Conversations` (not `MasterData`), proven live | Unit test | `ConversationEndpoints_OnLiveSpec_GetAllTaggedConversations` |
| 16 | ✅ | Existing `GetById` behaviour unchanged (full `lines` array, no regression) | Unit test | Existing `ConversationEndpointsTests.GetById_*` tests still pass unmodified |
| 17 | ✅ | `README.md`/`addon/DOCS.md` document the new endpoint; `docs/logging.md` carries the new prefix | Doc review | Files updated |
| 18 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite (10 projects, 1475 tests) green, 0 warnings, 0 errors |
| 19 | ✅ | T1 — app starts in Visual Studio; endpoint reachable | Live (T1) | Developer confirmed 2026-07-19 — clean startup, GetAll/pagination contract exercised live, correct lineCount values, against the already-fixed build (both bugs in this row's Notes were caught by the preceding T2 pass, not this one). Developer's own eyeballing of the live T1 output separately caught the missing Description on one seeded conversation — fixed in quotinator-curated.json |
| 20 | ✅ | T2 — the live contract holds against the built image, including a real line-count match | Live (T2) | `docker build`/`docker run` matrix — see Step 8. Run 2026-07-19: found and fixed two genuine live-only bugs in `IConversationLineCountReader` — see Notes |

---

## Notes

Unlike #184–#188, this issue does not introduce a new top-level `{Entity}Endpoints.cs` file — it extends
the existing `ConversationEndpoints.cs`, since `GetById` already exists there. This is the first issue in
the #183 sub-issue set to add a list endpoint to an entity that already had a `GetById`.

`IConversationLineCountReader` deliberately has no single-id form (unlike #184/#185/#187's readers) —
`GetById`'s existing `ConversationResponse.Lines.Count` already gives a caller the same information for a
single conversation, so a redundant reader method would have no consumer. If a future issue needs a
single-id line count without loading the full line list, add the method then, not speculatively here.

`ConversationSummaryResponse` is a genuinely new type, not a trimmed-down `ConversationResponse` — the
two DTOs share no properties beyond `Id`/`Description` by coincidence of both describing a Conversation,
not by design; keeping them structurally separate avoids ever needing to make `Lines` nullable/optional
on a type documented as "the full ordered line list."

**Two genuine bugs found during T2 (2026-07-19), neither catchable by the fake-backed unit tests, both
fixed and covered by new real-SQLite tests in `ConversationLineCountReaderTests.cs`
(`tests/Quotinator.Engine.Tests/Repositories/`):**

1. **Dapper materialization failure, live 500.** Two independently-confirmed causes, verified against
   official documentation/source after the initial empirical diagnosis (per CLAUDE.md's "Authoritative
   sources" policy — the first pass here was reasoned from observed behaviour alone, then checked):
   - **Dapper skips every registered `SqlMapper.TypeHandler` (including `GuidHandler`) when the target
     type has a parameterized constructor whose parameter count matches the query** — confirmed via
     [DapperLib/Dapper#461](https://github.com/StackExchange/Dapper/issues/461): "the `Parse` method is
     only called if it is being serialised to a type with a parameterless constructor. If a constructor
     exists which initialises all the properties the type handler is not called." A positional record
     like the original `LineCountRow(Guid ConversationId, int LineCount)` hits exactly this path — Dapper
     requires a constructor matching the *raw* database column types, not the handler-converted ones.
   - `LineCount` is a correlated-subquery expression column with no SQLite-declared type. Per
     [Microsoft's own Sqlite type-mapping docs](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types),
     `System.Byte[]` maps to SQLite `BLOB` — and (confirmed via
     [aspnet/Microsoft.Data.Sqlite#433](https://github.com/aspnet/Microsoft.Data.Sqlite/issues/433)) a
     column with no declared type takes `BLOB` affinity by default, which is what a query with no
     matching row falls back to when there's nothing to sample a runtime type from. `CAST(... AS
     INTEGER)` forces a real `INTEGER`/`Int64` result once a row exists, but doesn't change the
     zero-row fallback — this is why `Byte[]` and `Int64` were both observed across otherwise-identical
     queries, not true "inconsistency."
   Fixed by reading rows via `QueryAsync` (dynamic), not a typed record, and converting each field
   explicitly (`ConversationLineCountReader.cs`) — dynamic access involves neither constructor-matching
   nor declared-type schema inference, so it's immune to both causes rather than working around either
   one specifically. The `CAST(... AS INTEGER)` stays in the SQL as documentation of intent even though
   the dynamic-row read is what actually makes the fix robust.
2. **Case-sensitivity bug, silent wrong-answer (not an exception) — `lineCount: 0` for every conversation.**
   `#68`'s curated JSON conversations were seeded with their file-authored lowercase ids preserved verbatim
   (per CLAUDE.md's case-insensitivity convention: an import file's own explicit id is under no obligation
   to match the codebase's usual stored-uppercase convention), while the reader's `@conversationIds`
   parameter is bound via the globally-registered `GuidHandler`, which always uppercases. The original
   `cl.ConversationId IN @conversationIds` was therefore an exact-case comparison that matched nothing, for
   every real (lowercase-stored) conversation — confirmed live via `Quotinator.Tools.DbInspector` against a
   copy of the running container's database (`docker cp` the `.db`+`.db-wal`+`.db-shm` files together — a
   WAL-mode SQLite database's committed data lives partly in the `-wal` sidecar file, not the main `.db`
   file alone). Fixed with `UPPER(cl.ConversationId) IN @conversationIds`, matching this project's
   established case-insensitive-id-comparison convention. This is a second, independent instance of the
   same class of bug CLAUDE.md's "GUID/enum/id comparisons are case-insensitive by default" section already
   tracks recurring across `Sql.Sources`/`Sql.People` (#180) — worth flagging there too.
3. **Not a bug, a false lead investigated and ruled out along the way**: whether `AssemblyInitialize`
   (`Quotinator.Engine.Tests`) actually registers `GuidHandler` before an isolated/filtered test run —
   confirmed it does (it runs once per test-process regardless of `--filter`), so the materialization
   failure in (1) was never actually about a missing handler registration, despite initially looking like
   one from the error text alone.

---

## Corrected issue text (for a future `gh issue edit`)

```
## Background

Depends on #193 (generic listable repository + DI) and #195 (pagination contract + helpers) — not #183
directly, which ships nothing of its own, and not #196, since Conversations stays under its own route/tag
(it is a masterdata *consumer*, not a masterdata entity — see CLAUDE.md's "Masterdata routing convention").

`GET /api/v1/conversations/{id}` already exists (`ConversationEndpoints.cs`) and returns a conversation's
full ordered line list (quotes, stage directions, sound cues). There is no `GET /` list endpoint —
confirmed via a direct read of `ConversationEndpoints.cs` and every `Sql.Conversations.*`/
`Sql.ConversationLines.*` query, all of which are single-row, id-scoped.

## What needs to be done

1. `GET /api/v1/conversations` — paginated list, using the already-registered
   `IListableRepository<ConversationEntity>` directly (bypassing `IQuoteService` — a summary view has no
   reason to route through full line-assembly logic) + `Quotinator.Data.Models.PagedItems<T>` + the
   shared `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helper (rejects out-of-range input with
   422; does not clamp). Response items are a new `ConversationSummaryResponse` DTO
   (`Quotinator.Api.Models` — a distinct type from the existing `Quotinator.Core.Models.ConversationResponse`)
   with `Id`, `Description`, `CompletenessStatus`, and `LineCount` — never the full ordered line list.
2. A new `IConversationLineCountReader` (batched form only — see plan doc's Notes for why no single-id
   form is needed) backed by a new `Sql.ConversationLines` query, to fetch each conversation's active line
   count for a whole page in one round-trip — avoid N+1.
3. Extend the *existing* `ConversationEndpoints.cs` with the new `GetAll` handler — do not create a new
   endpoints file. `GetById` is untouched.
4. Route registration order (`/` vs `/{id}`) needs no special handling — the two templates cannot collide
   regardless of order.
5. Both endpoints keep `RateLimitPolicies.Api` and `ApiTags.Conversations` — the new endpoint is not
   tagged `ApiTags.MasterData`.
6. No entity-specific filters yet — deferred per CLAUDE.md's entity-scoped filter-parameter convention.
7. Register `api/v1/conversations`'s `page`/`pageSize` parameters in
   `NumericParameterSchemaTransformer.NumericParamsByPath`, or the published OpenAPI type regresses to
   bare `string`.
8. Update `README.md` and `addon/DOCS.md`'s endpoint tables, and register the new
   `[Api - GetAllConversations]` log prefix in `docs/logging.md`.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| Extended: `Quotinator.Api.Tests` | `GetAllConversations_ReturnsPaginatedResults` | ❌ |
| Extended: `Quotinator.Api.Tests` | `GetAllConversations_ReturnsSummaryNotFullLineList` | ❌ |
| Extended: `Quotinator.Api.Tests` | `GetAllConversations_LineCountMatchesActualLineCount` | ❌ |
| Extended: `Quotinator.Api.Tests` | `GetAllConversations_ConversationWithNoLines_ReturnsZeroLineCount` | ❌ |
| Extended: `Quotinator.Api.Tests` | `GetAllConversations_MultipleConversationsWithLines_BatchResolvesEachCount` | ❌ |
| Extended: `Quotinator.Api.Tests` | `ConversationEndpoints_OnLiveSpec_GetAllTaggedConversations` | ❌ |

Plus the full eight-case pagination matrix CLAUDE.md's "Standard pagination contract" mandates for every
new paginated GET endpoint (page=0, malformed page/pageSize, negative pageSize, pageSize>500, pageSize=0,
pageSize omitted, page beyond last).

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests (including existing `GetById` tests, unmodified)
- [ ] Findings summarised in a closing comment
```
