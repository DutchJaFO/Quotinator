# #187 — Masterdata: GET /api/v1/masterdata/series list + get-by-id

**Status:** Planning
**GitHub issue:** #187
**Tiers required:** T1, T2
**Depends on:** #193, #195, #196, #179

---

## Spec requirements (corrected during planning review 2026-07-18)

1. `GET /api/v1/masterdata/series` — paginated list, using `IListableRepository<SeriesEntity>` directly
   (no service layer in between — this entity has no service, matching `AdminEndpoints`'s `/audit`
   shape, not `QuoteEndpoints.GetAll`'s) + `Quotinator.Data.Models.PagedItems<T>` (not `PageResponse<T>`
   — that type does not exist) + the shared `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helper
   (which rejects out-of-range `page`/`pageSize` with 422 — it does not clamp). Response items are a new
   `SeriesResponse` DTO (`Id`, `Name`, `UniverseId` nullable, `CompletenessStatus`) — never the raw
   `SeriesEntity`, whose `SafeValue<CompletenessStatus?>` field has no `System.Text.Json` converter.
2. `GET /api/v1/masterdata/series/{id}` — single Series by id, using the shared
   `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively — for free, via
   `SqliteRepository<T>.GetByIdAsync`'s existing uppercase-normalisation (see Background), not new logic
   this issue has to write.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes.
4. No entity-specific filters yet (e.g. `?universeId=`) — deferred per #196's entity-scoped
   filter-parameter convention (`CLAUDE.md` "Entity-scoped filter-parameter convention"), which is not
   consumed until #192.
5. `page`/`pageSize` on `api/v1/masterdata/series` must be registered in
   `NumericParameterSchemaTransformer.NumericParamsByPath` — **not mentioned in the issue's original
   "What needs to be done" list at all**, found during this planning review. Without it, the published
   OpenAPI type regresses from `integer|string` to bare `string`, exactly the #194 gap CLAUDE.md's
   "Numeric query parameter binding pattern" section warns every new numeric-param endpoint about.
6. Per CLAUDE.md's "Standard pagination contract", the endpoint must ship with the full eight-case
   pagination test matrix, not only the four tests the issue itself lists (see Background — the issue's
   own "Expected tests" table pre-dates that mandate).
7. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

---

## Background — why this issue exists

Sub-issue of #183 (via #193/#195/#196). `SeriesEntity` (`src/Quotinator.Engine/Entities/Series.cs`),
added by #179/ADR 011, has no repository or endpoint of any kind today from the API's point of view —
its `IListableRepository<SeriesEntity>` DI registration exists but nothing consumes it yet. This issue
gives Series its first read access, needed before #180's overlay-file work can be verified against real
API responses (today only DbInspector can) and before #192 (quote read path) or duplicate-Series
discovery becomes possible.

**Verified before starting** (per this project's standing rule — #183, #193, #194, #195, and #196 all
had errors caught this way):

- **The issue's central "first-ever repository registration" framing is wrong — already done, not
  pending.** `src/Quotinator.Api/Program.cs:307` already registers
  `builder.Services.AddSingleton<IListableRepository<SeriesEntity>, SqliteRepository<SeriesEntity>>();`,
  added by #193 in anticipation of this issue, with a comment (`Program.cs:302-306`) explicitly noting
  "`SeriesEntity`/`UniverseEntity` get their first repository of any kind here". This issue does not add
  DI registration — it adds the endpoint that finally calls the already-registered repository.
- `PageResponse<T>` does not exist anywhere in the codebase. The real type is
  `Quotinator.Data.Models.PagedItems<T>` (`public record PagedItems<T>(IReadOnlyList<T> Items, int Page,
  int PageSize, int TotalCount)`, not sealed, computed `TotalPages`), confirmed by reading
  `src/Quotinator.Data/Models/PagedItems.cs` directly.
- The "shared pagination-clamp helper" does not exist as a clamp. The real helper,
  `src/Quotinator.Api/Endpoints/Shared/PaginationParsing.cs`, **rejects** out-of-range `page`/`pageSize`
  with a 422 `Results.Problem` — confirmed by reading its source. `ValidatePageBeyondLast` is a second,
  separate check that can only run after the query, once `TotalPages` is known.
- `NotFoundResult.OkOrNotFound<T>(T? entity, IApiLocalizer localizer, string notFoundMessageKey) where T
  : class` (`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`) confirmed to exist, extracted from
  `QuoteEndpoints`/`ConversationEndpoints` by #195.
- `RateLimitPolicies.Api` (`src/Quotinator.Constants/RateLimiting/RateLimitPolicies.cs`) and
  `ApiTags.MasterData` (`src/Quotinator.Constants/Api/ApiTags.cs`, added by #196) both confirmed to
  exist exactly as the issue claims.
- `SeriesEntity`'s current field list, read directly from `src/Quotinator.Engine/Entities/Series.cs`:
  `Name` (string, unique), `UniverseId` (`Guid?`), `ImportBatchId` (`Guid?`), `CompletenessStatus`
  (`SafeValue<CompletenessStatus?>`), `NoValueKnown` (`IReadOnlyList<string>`), plus `RecordBase`'s `Id`/
  `DateCreated`/`DateModified`/`DateDeleted`/`IsDeleted`. Matches the corrections supplied for this
  review exactly — no drift found.
- The `Series` table's `CompletenessStatus` column (`QuotinatorMigrations.cs`) is
  `TEXT NOT NULL DEFAULT 'Incomplete' CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview',
  'Complete'))` — never actually `NULL` on a real row. This matters for the DTO design below: the
  established codebase idiom for reading this column (`row.CompletenessStatus.Parsed ??
  CompletenessStatus.Incomplete`, used identically in `SqliteImportActionService.cs:966`,
  `QuoteSeedWriter.cs:215`, and five call sites in `ImportActionPlanner.cs`) defaults defensively rather
  than modelling the DTO field as nullable — `SeriesResponse.CompletenessStatus` is a non-nullable
  `CompletenessStatus` enum, not `CompletenessStatus?`. `CompletenessStatus` itself already carries
  `[JsonConverter(typeof(JsonStringEnumConverter))]`, so no new converter is needed for this field —
  only `SafeValue<T>`'s own wrapper needed unwrapping, which the DTO mapping does directly.
- **New finding: `{id}` case-insensitivity requires no endpoint-side logic.**
  `SqliteRepository<T>.GetByIdAsync` (`src/Quotinator.Data/Repositories/SqliteRepository.cs:31-44`)
  already normalises its `Guid` parameter via `id.ToString("D").ToUpperInvariant()` before querying, and
  stored ids are always written uppercase. Since `Guid.TryParse` is itself case-insensitive and
  `Guid.ToString("D")` re-serialises to a canonical form regardless of input casing, the endpoint's own
  job is only to `Guid.TryParse` the route string and delegate — case-insensitivity is inherited for
  free from already-shipped, already-tested repository infrastructure. `GetSeriesById_
  LowercaseId_MatchesCaseInsensitively` proves the endpoint parses and delegates correctly, not that any
  new case-insensitivity logic was written.
- **New finding: a malformed (non-`Guid`) `{id}` needs an explicit decision.** Neither
  `QuoteEndpoints.GetById` nor `ConversationEndpoints.GetById` parses `{id}` to a `Guid` at all — both
  pass the raw string straight into a SQL `UPPER(Id) = UPPER(@id)` comparison via their service layer, so
  a malformed id simply matches no row and falls through to the existing 404 path with no special
  handling. `IListableRepository<T>.GetByIdAsync` takes a `Guid`, not a string, so `SeriesEndpoints`
  needs its own `Guid.TryParse` before calling it. Decided: on parse failure, treat it the same as "no
  match" and return the standard 404 via `NotFoundResult.OkOrNotFound` — consistent with the existing
  two `GetById` endpoints' behaviour (a malformed id is not "bad input" worth a 422, it is simply
  guaranteed not to match anything) rather than inventing a third, endpoint-specific error shape.
- **New finding: the issue's own "Expected tests" table under-covers CLAUDE.md's now-mandatory
  pagination matrix.** CLAUDE.md's "Standard pagination contract" section (added by #195) states
  "Whenever a new paginated GET endpoint is added, it must ship with the full test matrix" — eight cases
  (`page=0`, malformed `page`, malformed `pageSize`, negative `pageSize`, `pageSize` above 500,
  `pageSize=0`, `pageSize` omitted, page beyond last) — explicitly so a new endpoint doesn't repeat the
  gap that had to be closed retroactively across `/quotes`, `/admin/audit`, and `/import/actions`. The
  issue's own table lists only four tests (none of the eight pagination cases). This plan's test list
  below adds the full matrix on top of the issue's four.
- **New finding: `NumericParameterSchemaTransformer.NumericParamsByPath` registration is required but
  absent from the issue's "What needs to be done" list.** `page`/`pageSize` will be declared `string?`
  per the numeric-param binding pattern; without registering `api/v1/masterdata/series` (with `page`/
  `pageSize` defaults) in the transformer's dictionary, the published OpenAPI type silently regresses to
  bare `string` — the exact #194 defect, on a brand-new endpoint this time rather than a retrofit. Added
  as its own step below, plus a live-pipeline `OpenApiSpecEndpointTests` case (the class explicitly
  built by #195 to prevent a registration existing only in the transformer's own unit tests without ever
  being wired into `Program.cs`'s real `AddOpenApi` pipeline).
- Confirmed no existing fake implements `IListableRepository<T>` anywhere in `tests/` — `Fakes/` contains
  `FakeQuoteService`, `FakeQuoteImportService`, `FakeImportActionService` only. `FakeSeriesRepository` is
  new, no prior example to copy structurally (see Step 5 for its designed shape).
- Confirmed no `src/Quotinator.Api/Models/` folder exists yet — `SeriesResponse` is the first response
  DTO placed directly under `Quotinator.Api` rather than `Quotinator.Core` (correct per CLAUDE.md's File
  placement rule: this endpoint has no service layer, so the DTO has no reason to live in
  `Quotinator.Core`, which `QuoteResponse` does only because `IQuoteService` is a Core-layer interface).

Conventions and infrastructure only — no new DI registration, no schema change, no repository code (all
already shipped by #179/#193/#195/#196).

---

## Steps

### 1. `SeriesResponse` DTO

**Status:** Not started.

New file `src/Quotinator.Api/Models/SeriesResponse.cs`, namespace `Quotinator.Api.Models`:

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Series.</summary>
public sealed class SeriesResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The series' name.</summary>
    public required string Name { get; init; }

    /// <summary>The universe this series belongs to, if any. <c>null</c> for a standalone series.</summary>
    public string? UniverseId { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public CompletenessStatus CompletenessStatus { get; init; }
}
```

Mirrors `Quotinator.Core.Models.QuoteResponse`'s flattening style (`Id` as `string`, not `Guid`).
`CompletenessStatus` is typed as the enum directly — it already carries
`[JsonConverter(typeof(JsonStringEnumConverter))]`, so it serialises as a plain string with no further
work. No global `SafeValue<T>` converter is introduced.

### 2. Not-found message key + i18n lockstep

**Status:** Not started.

Add to `src/Quotinator.Constants/Api/ApiMessages.cs`:
```csharp
public const string SeriesNotFound = "ErrorSeriesNotFound";
```
Add the key to all three `i18ntext/UI.*.json` files in the same commit, next to the existing
`ErrorConversationNotFound` entry (English baseline first, then `nl`/`de`), following the exact wording
style already used there (e.g. `"No conversation with the requested ID was found."` /
`"Er is geen conversatie gevonden met het opgegeven ID."` / `"Es wurde keine Konversation mit der
angegebenen ID gefunden."`).

### 3. `SeriesEndpoints.cs` — GetAll + GetById

**Status:** Not started.

New file `src/Quotinator.Api/Endpoints/SeriesEndpoints.cs`, static class `SeriesEndpoints`, mirroring
`AdminEndpoints`'s `/audit` handler (repository directly to `PagedItems<T>`, no service layer) for
`GetAll`, and `ConversationEndpoints.GetById`'s shape for `GetById`:

```csharp
namespace Quotinator.Api.Endpoints;

internal static class SeriesEndpoints
{
    internal static void MapSeriesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/series")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSeries")
             .WithSummary("List Series")
             .WithDescription(
                 "Returns a paginated list of Series. Maximum `pageSize` is 500; `pageSize=0` returns " +
                 "every Series as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSeriesById")
             .WithSummary("Series by ID")
             .WithDescription(
                 "Returns a single Series by ID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IListableRepository<SeriesEntity> repository,
        IApiLocalizer localizer,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result   = await repository.GetPageAsync(pageValue, pageSizeValue);
        var response = new PagedItems<SeriesResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);

        return PaginationParsing.ValidatePageBeyondLast(pageValue, response.TotalPages, localizer)
            ?? Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the Series.")] string id,
        IListableRepository<SeriesEntity> repository,
        IApiLocalizer localizer)
    {
        var entity = Guid.TryParse(id, out var guidId) ? await repository.GetByIdAsync(guidId) : null;
        return NotFoundResult.OkOrNotFound(entity is null ? null : ToResponse(entity), localizer, ApiMessages.SeriesNotFound);
    }

    private static SeriesResponse ToResponse(SeriesEntity entity) => new()
    {
        Id                 = entity.Id.ToString("D").ToUpperInvariant(),
        Name               = entity.Name,
        UniverseId         = entity.UniverseId?.ToString("D").ToUpperInvariant(),
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}
```

`.ToString("D").ToUpperInvariant()` — not a bare `.ToString()` — per the codebase-wide uppercase-Guid
convention #184/#185/#186's `SourceResponse`/`CharacterResponse`/`PersonResponse` all use (`Guid.ToString("D")`
defaults to lowercase, which would silently mismatch the stored-uppercase convention).

A malformed (non-`Guid`) `{id}` falls through `GetById` to `entity == null` and the standard 404 — no
separate 422 path, matching `QuoteEndpoints`/`ConversationEndpoints`'s existing behaviour (see
Background).

### 4. Register `MapSeriesEndpoints()` in `Program.cs`

**Status:** Not started.

Add `app.MapSeriesEndpoints();` alongside the existing four `Map*Endpoints()` calls
(`Program.cs:539-542`). No new DI registration and no new `using` needed — `SeriesEntity` and
`IListableRepository<T>` are already imported for the existing DI registration at `Program.cs:307`, and
`using Quotinator.Api.Endpoints;` already covers the new extension method.

### 5. Register the OpenAPI transformer path

**Status:** Not started.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath`
(`src/Quotinator.Api/OpenApi/NumericParameterSchemaTransformer.cs`):
```csharp
["api/v1/masterdata/series"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```
Not mentioned anywhere in the issue's own text — found during this review (see Background finding 6).
Without it, `page`/`pageSize` publish as bare `string` in the OpenAPI spec instead of `integer|string`.

### 6. Test fixtures and tests

**Status:** Not started.

New file `tests/Quotinator.Api.Tests/Fakes/FakeSeriesRepository.cs` — the first fake implementing
`IListableRepository<T>` in this codebase (no prior example to copy). Backed by an in-memory
`List<SeriesEntity>` settable via a public property, implementing every `IRepository<T>`/
`IListableRepository<T>` member:
- `GetByIdAsync` — `_items.FirstOrDefault(x => x.Id == id && !x.IsDeleted)` (a `Guid` comparison is
  inherently case-insensitive — no string matching involved).
- `GetPageAsync` — real in-memory paging replicating the effective-`pageSize` contract
  (`pageSize == 0` returns every item as one page, `PageSize` in the result reports the actual count
  returned), matching `PagedItems<T>`'s documented contract exactly. This is required for the endpoint's
  own pagination-matrix tests to be meaningful — a fake that "echoes back whatever it's given" (the
  known-bad pattern CLAUDE.md's pagination contract section calls out) would produce false greens on
  `pageSize=0`.
- `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync` — minimal working implementations
  against the same in-memory list (not `NotImplementedException`), since a fake left partially
  implemented risks silently breaking a future test that exercises write paths.

New file `tests/Quotinator.Api.Tests/Endpoints/SeriesEndpointsTests.cs`, `CreateFactory()` registering
`FakeSeriesRepository` alongside the standard `IQuoteService`/`IDatabaseInitializer` boilerplate
(pattern from `AdminAuditEndpointTests.CreateFactory()`). Tests:

**From the issue's own list:**
- `GetAllSeries_ReturnsPaginatedResults`
- `GetSeriesById_ExistingId_ReturnsSeries`
- `GetSeriesById_UnknownId_Returns404`
- `GetSeriesById_LowercaseId_MatchesCaseInsensitively`

**Added per CLAUDE.md's mandatory pagination matrix (Background finding 5) — naming mirrors
`AdminAuditEndpointTests`'s existing `Audit_*` cases:**
- `GetAllSeries_PageZero_Returns422`
- `GetAllSeries_PageMalformed_Returns422`
- `GetAllSeries_PageSizeMalformed_Returns422`
- `GetAllSeries_PageSizeNegative_Returns422`
- `GetAllSeries_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllSeries_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllSeries_PageSizeOmitted_DefaultsTo20`
- `GetAllSeries_PageBeyondLast_Returns422DistinctDetail`

**Added per Background finding on malformed ids:**
- `GetSeriesById_MalformedId_Returns404NotBadRequest`

**Added to prove tag/rate-limit wiring (issue requirement 3) live rather than by inspection only:**
- `SeriesEndpoints_OnLiveSpec_TaggedMasterData` (extends `OpenApiSpecEndpointTests` or a small dedicated
  assertion against `/openapi/v1.json`'s `tags` array for both operations)

**Extend existing shared-infrastructure test files (both already exist, from #195):**
- `tests/Quotinator.Api.Tests/OpenApi/NumericParameterSchemaTransformerTests.cs` — new case(s) for
  `api/v1/masterdata/series`.
- `tests/Quotinator.Api.Tests/OpenApi/OpenApiSpecEndpointTests.cs` — two new `[DataRow]` entries:
  `("/api/v1/masterdata/series", "page")`, `("/api/v1/masterdata/series", "pageSize")`.

### 7. Documentation

**Status:** Not started.

Add a row for `GET /api/v1/masterdata/series` and `GET /api/v1/masterdata/series/{id}` to the REST API
Endpoints table in both `README.md` and `addon/DOCS.md`, following the existing row format/placement
(alongside the `/api/v1/conversations/{id}` row). Add `[Description]` attributes on the endpoint methods
themselves (done inline in Step 3).

### 8. Verify

**Status:** Not started.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full suite green, 0 warnings, 0 errors.

T1 (developer, Visual Studio): clean startup; `GET /api/v1/masterdata/series` and
`GET /api/v1/masterdata/series/{id}` both reachable and return the expected shape.

T2 (Docker): `docker build` + `docker run`, then exercise the new endpoints live:
```bash
curl -s "http://localhost:8080/api/v1/masterdata/series"
curl -s "http://localhost:8080/api/v1/masterdata/series?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/series?pageSize=999"
curl -s "http://localhost:8080/api/v1/masterdata/series/<a known lowercase Series id>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/series/00000000-0000-0000-0000-000000000000"
curl -s "http://localhost:8080/openapi/v1.json" | grep -A2 '"masterdata/series"'
```
Confirm `page`/`pageSize=999` returns 422, an unknown-but-well-formed id returns 404, a lowercase id for
a real row (seeded via #180's overlay file, if any Series rows exist by then) returns 200, and the live
OpenAPI spec publishes `page`/`pageSize` as `integer|string` under the new path. This scenario should be
added to CLAUDE.md's Pre-Push Checklist step 6 (living smoke-test list) once implemented, per this
project's standing practice.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `completenessStatus` serializes as a plain JSON value, never `{raw, parsed}` | Unit test | `GetSeriesById_ExistingId_ReturnsSeries` (shape assertion) |
| 2 | ❌ | `GET /api/v1/masterdata/series` returns a paginated list | Unit test | `GetAllSeries_ReturnsPaginatedResults` |
| 3 | ❌ | `GET /api/v1/masterdata/series/{id}` returns 200 for an existing id | Unit test | `GetSeriesById_ExistingId_ReturnsSeries` |
| 4 | ❌ | Unknown well-formed id returns 404 | Unit test | `GetSeriesById_UnknownId_Returns404` |
| 5 | ❌ | Lowercase id matches case-insensitively | Unit test | `GetSeriesById_LowercaseId_MatchesCaseInsensitively` |
| 6 | ❌ | Malformed (non-Guid) id returns 404, not a bare error | Unit test | `GetSeriesById_MalformedId_Returns404NotBadRequest` |
| 7 | ❌ | `page=0` returns 422 | Unit test | `GetAllSeries_PageZero_Returns422` |
| 8 | ❌ | Malformed `page` returns 422 | Unit test | `GetAllSeries_PageMalformed_Returns422` |
| 9 | ❌ | Malformed `pageSize` returns 422 | Unit test | `GetAllSeries_PageSizeMalformed_Returns422` |
| 10 | ❌ | Negative `pageSize` returns 422 | Unit test | `GetAllSeries_PageSizeNegative_Returns422` |
| 11 | ❌ | `pageSize` above 500 returns 422, never silently clamped | Unit test | `GetAllSeries_PageSizeAbove500_Returns422NotSilentClamp` |
| 12 | ❌ | `pageSize=0` returns every row with the effective count reported | Unit test | `GetAllSeries_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 13 | ❌ | Omitted `pageSize` defaults to 20 | Unit test | `GetAllSeries_PageSizeOmitted_DefaultsTo20` |
| 14 | ❌ | Page beyond the last returns 422, distinct from case 7 | Unit test | `GetAllSeries_PageBeyondLast_Returns422DistinctDetail` |
| 15 | ❌ | `page`/`pageSize` publish as `integer` on the live OpenAPI spec for `api/v1/masterdata/series` | Unit/Live | `NumericParameterSchemaTransformerTests` new case + `OpenApiSpecEndpointTests` new `[DataRow]` entries |
| 16 | ❌ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api` | Unit test | `SeriesEndpoints_OnLiveSpec_TaggedMasterData` |
| 17 | ❌ | `README.md` and `addon/DOCS.md` endpoint tables updated | Doc review | Both files contain the new rows |
| 18 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 19 | ❌ | T1 — app starts in Visual Studio; both endpoints reachable | Live (T1) | Developer confirmation |
| 20 | ❌ | T2 — both endpoints behave per contract on the built image | Live (T2) | `docker build` + `docker run`, curl matrix from Step 8 |

---

## Notes

This is the first issue to actually call the `IListableRepository<SeriesEntity>` registration #193 shipped
and the first real consumer of #195's `PagedItems<T>`/`PaginationParsing`/`NotFoundResult` and #196's
`ApiTags.MasterData`/routing convention outside their own unit tests — #196's own Notes section flagged
this explicitly ("the convention's real proof is #184–#189 and #192 following it... without re-deciding").
This plan follows all three without re-litigating any of them.

`SeriesResponse`'s `CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete` flattening is the first
time this established internal idiom (previously only used inside `Quotinator.Engine`'s import/service
code) crosses into a public API response DTO. #184–#186/#188 (Sources, Characters, People, Universes) all
carry the same `SafeValue<CompletenessStatus?>` field and should reuse this exact pattern rather than
re-deciding it per entity.

No entity-scoped filter (`?universeId=`) is added here — #196's convention exists but has no wired
consumer yet; #192 is where a real `resolveIdByName` delegate first gets built. Adding a filter to this
issue would be scope creep against #196's own explicit "no specific filter is wired to a real endpoint
here" boundary.

The malformed-id-returns-404 decision (Background) is a judgement call, not something explicitly
specified anywhere — flagged here in case a reviewer prefers 422 for structurally invalid input instead.
Chosen to match the two existing `GetById` endpoints' actual behaviour over introducing a third shape.

---

## Corrected issue text (for a future `gh issue edit`)

**Title:** Masterdata: GET /api/v1/masterdata/series list + get-by-id (unchanged)

**Body:**

```
## Background

Depends on #193 (listable-repository capability + DI registration), #195 (PagedItems<T>, pagination
parsing, not-found helper), #196 (ApiTags.MasterData, /masterdata/ routing convention), and #179
(Series/Universe schema).

`SeriesEntity` (`src/Quotinator.Engine/Entities/Series.cs`), added by #179, already has a
`IListableRepository<SeriesEntity>` DI registration (added by #193 in anticipation of this issue) but no
endpoint of any kind. This issue gives Series its first read access, needed before #180's overlay-file
work can be verified against real API responses and before duplicate-Series discovery becomes possible.

## What needs to be done

1. `GET /api/v1/masterdata/series` — paginated list, using the already-registered
   `IListableRepository<SeriesEntity>` (no service layer — call the repository directly, mirroring
   `AdminEndpoints`'s `/audit` handler) + `Quotinator.Data.Models.PagedItems<T>` + the shared
   `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helper (rejects out-of-range input with 422; it
   does not clamp). Response items are a new `SeriesResponse` DTO (`Id`, `Name`, `UniverseId` nullable,
   `CompletenessStatus`) in `Quotinator.Api.Models` — never the raw `SeriesEntity`.
2. `GET /api/v1/masterdata/series/{id}` — single Series by id, using the shared
   `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively via
   `SqliteRepository<T>.GetByIdAsync`'s existing normalisation — no new logic required for this.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes.
4. No entity-specific filters yet (e.g. `?universeId=`) — deferred per #196's entity-scoped
   filter-parameter convention.
5. Register `api/v1/masterdata/series` (with `page`/`pageSize` defaults) in
   `NumericParameterSchemaTransformer.NumericParamsByPath`, or the published OpenAPI type for these
   parameters regresses to bare `string`.
6. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| New: `Quotinator.Api.Tests` | `GetAllSeries_ReturnsPaginatedResults` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSeriesById_ExistingId_ReturnsSeries` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSeriesById_UnknownId_Returns404` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSeriesById_LowercaseId_MatchesCaseInsensitively` | ❌ |
| New: `Quotinator.Api.Tests` | Full 8-case pagination matrix per CLAUDE.md's "Standard pagination contract" | ❌ |
| New: `Quotinator.Api.Tests` | `GetSeriesById_MalformedId_Returns404NotBadRequest` | ❌ |
| Extended: `Quotinator.Api.Tests` | `NumericParameterSchemaTransformerTests` + `OpenApiSpecEndpointTests` new cases for `api/v1/masterdata/series` | ❌ |

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
