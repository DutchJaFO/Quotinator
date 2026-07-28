# #188 — Masterdata: GET /api/v1/masterdata/universes list + get-by-id

**Status:** Waiting for release
**GitHub issue:** #188
**Tiers required:** T1, T2
**Depends on:** #193, #195, #196, #179

---

## Spec requirements (corrected during planning review 2026-07-18)

1. `GET /api/v1/masterdata/universes` — paginated list, using `IListableRepository<UniverseEntity>`
   directly (no service layer), `Quotinator.Data.Models.PagedItems<T>` (not `PageResponse<T>` — that
   type does not exist), and the shared `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helper
   (not a "clamp" — out-of-range `pageSize`/`page` is rejected with 422, never silently clamped).
   Response items include `Id`, `Name`, `CompletenessStatus`.
2. `GET /api/v1/masterdata/universes/{id}` — single Universe by id, using the shared
   `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, route prefix
   `/api/v1/masterdata/universes` per CLAUDE.md's "Masterdata routing convention", and `[Description]`
   attributes.
4. No entity-specific filters yet — deferred per CLAUDE.md's "Entity-scoped filter-parameter
   convention" (#196): no consuming endpoint exists yet to justify wiring a real `resolveIdByName`.
5. Update `README.md` and `addon/DOCS.md`'s endpoint tables.
6. **`IListableRepository<UniverseEntity>` is already DI-registered** (`Program.cs:308`, added by
   #193 in anticipation of this issue) — this issue does *not* register it. The issue body's framing
   of this as "Universe's first-ever repository registration" describes already-completed groundwork
   this issue builds on, not a pending task of its own.

---

## Background — why this issue exists

`UniverseEntity` (`src/Quotinator.Engine/Entities/Universe.cs`), added by #179 per ADR 011 (the
Universe → Series → Source hierarchy), has no endpoint of any kind today. This is the simplest of the
five masterdata list/get-by-id issues (#184–#189, excluding #192) — Universe has the fewest fields
(`Name`, `CompletenessStatus`, `NoValueKnown`, plus `RecordBase`) and no FK to another masterdata
entity, unlike Series (→ Universe) or Source (→ Series).

**Verified before starting** (per this project's standing rule — #183/#193/#194/#195/#196 all had
errors caught this way):

- **DI registration claim (the issue's central "first-ever repository" framing) — checked directly**:
  `src/Quotinator.Api/Program.cs:308` already reads
  `builder.Services.AddSingleton<IListableRepository<UniverseEntity>, SqliteRepository<UniverseEntity>>();`,
  added by #193. The surrounding comment (`Program.cs:302-306`) confirms this was added specifically
  in anticipation of #184–#189. Confirmed: no new DI registration is needed by this issue. The issue
  text is corrected below.
- **`UniverseEntity`'s field list — checked directly** against
  `src/Quotinator.Engine/Entities/Universe.cs`: `Name` (`string`, unique), `ImportBatchId` (`Guid?`),
  `CompletenessStatus` (`SafeValue<CompletenessStatus?>`), `NoValueKnown`
  (`IReadOnlyList<string>`), plus `RecordBase`'s `Id`/`DateCreated`/`DateModified`/`DateDeleted`/
  `IsDeleted`. Matches the task brief exactly — no drift found.
- **`PageResponse<T>` does not exist.** The real type is `Quotinator.Data.Models.PagedItems<T>`
  (`public record PagedItems<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`,
  `TotalPages` computed, not `sealed`). `IListableRepository<T>.GetPageAsync` returns
  `Task<PagedItems<T>>` directly — confirmed in `src/Quotinator.Data/Repositories/IListableRepository.cs`.
- **The "pagination-clamp helper" does not clamp.** `PaginationParsing.TryParse` (`src/Quotinator.Api/
  Endpoints/Shared/PaginationParsing.cs`) rejects out-of-range `page`/`pageSize` with a 422
  `Results.Problem`, and `ValidatePageBeyondLast` separately rejects a page past the last one — neither
  clamps. Confirmed by reading the file directly.
- **`NotFoundResult.OkOrNotFound<T>`** (`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`) exists
  exactly as described: `entity is null ? 404 Problem : 200 Ok(entity)`.
- **`RateLimitPolicies.Api`** (`src/Quotinator.Constants/RateLimiting/RateLimitPolicies.cs`) and
  **`ApiTags.MasterData`** (`src/Quotinator.Constants/Api/ApiTags.cs`) both confirmed to exist.
- **Case-insensitive `{id}` matching is already structural, not something this issue needs to add.**
  `SqliteRepository<T>.GetByIdAsync(Guid id, ...)` (`src/Quotinator.Data/Repositories/
  SqliteRepository.cs:31-44`) normalises the parameter via `id.ToString("D").ToUpperInvariant()`
  before querying `WHERE Id = @id`, and stored ids are always written uppercase
  (`Guid.ToString("D").ToUpperInvariant()`, per CLAUDE.md's case-insensitivity rule). Because the
  parameter is typed `Guid` rather than the raw route string, a lowercase URL segment is normalised
  automatically the moment `Guid.TryParse` extracts it — the endpoint handler only needs to
  `Guid.TryParse` the route `{id}` into a `Guid` before calling `GetByIdAsync`, not add any
  case-handling of its own.
- **No existing masterdata endpoint file exists yet to copy from.** Grepped for
  `MapSourceEndpoints`/`MapCharacterEndpoints`/`MapPersonEndpoints`/`MapSeriesEndpoints` — none exist.
  #188 is the first of the five to actually implement, despite being numbered after #184–#187 in the
  issue list; the templates used below are `ConversationEndpoints.cs` (simple `GetById`) and
  `AdminEndpoints.cs`'s `/audit` handler (repository-direct pagination, no service layer — the closer
  shape match).
- **No `SafeValue<CompletenessStatus?>` JSON converter exists anywhere in the codebase.** Serializing
  `UniverseEntity` directly would leak `{"raw":...,"parsed":...}` onto the wire. Confirmed the
  established flattening pattern instead: every existing call site that reads a `CompletenessStatus`
  off an entity does `entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete` (see
  `ImportActionPlanner.cs`, `SqliteImportActionService.cs:966`, `QuoteSeedWriter.cs:215`) — never a
  global converter. `CompletenessStatus` itself already carries
  `[JsonConverter(typeof(JsonStringEnumConverter))]`, so once flattened to a plain
  `CompletenessStatus` value it serializes as a string with no further work.
- **Malformed-id-as-404 precedent confirmed**, not 422: `ImportEndpoints.cs:149-150`'s
  `/actions/{id}/decide` handler treats a `Guid.TryParse` failure on the route `{id}` as
  `ImportActionNotFound` (404), not a 422 parse error. `GetUniverseById` follows the same precedent —
  a malformed `{id}` and a well-formed-but-unknown `{id}` both produce the same 404 via
  `NotFoundResult.OkOrNotFound`, since there is no repository call to make either way.

No new discrepancies were found beyond the "first-ever repository registration" framing already
flagged going into this review — the rest of the issue's technical claims (helper names aside,
already corrected above) matched the code.

**Masterdata reference shape / soft-delete visibility — confirmed not applicable, added during
cross-plan review (2026-07-18):** Universe sits at the top of the Universe → Series → Source hierarchy
(ADR 011) — it has no FK of its own to another masterdata entity (the reverse is true: `Series.UniverseId`
points *at* Universe, which is #187's concern, not this issue's). This issue introduces no
`MasterDataReference`-typed field and no new joined-table reader. Soft-deleted visibility is still
relevant, though, and already satisfied structurally with no work needed: `RepositorySql.SelectPage`/
`SelectById` already filter `IsDeleted = 0` unconditionally, so a soft-deleted Universe is already
invisible via this endpoint for free, per CLAUDE.md's "Soft-deleted rows are invisible by default,
everywhere" convention. No `includeDeleted` opt-in is being built here — per that convention, added only
when a concrete consumer needs it.

---

## Steps

### 1. `UniverseResponse` DTO

**Status:** Done.

New file `src/Quotinator.Api/Models/UniverseResponse.cs`, namespace `Quotinator.Api.Models` (new
folder — first response DTO under `Quotinator.Api`, since every existing response DTO either lives in
`Quotinator.Core.Models` (`QuoteResponse`, backed by a service layer) or `Quotinator.Engine.Models`
(`ImportActionSummaryResponse`, also service-backed). Universe has no service layer — the endpoint
calls the repository directly, so `Quotinator.Core`/`Quotinator.Engine` are the wrong layer for its
response shape. Namespace matches folder path per CLAUDE.md's "File placement rule".

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Universe.</summary>
public sealed class UniverseResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The universe's name. Unique.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }
}
```

A static `FromEntity(UniverseEntity entity)` mapping method (or an inline `Select` at the call site —
decide during implementation, mirroring whichever style `AdminEndpoints.cs`/`ImportActionSummaryResponse`
prefers) does the flattening:

```csharp
Id                = entity.Id.ToString("D").ToUpperInvariant(),
Name              = entity.Name,
CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete
```

`.ToString("D").ToUpperInvariant()` — not a bare `.ToString()` — per the codebase-wide uppercase-Guid
convention #184/#185/#186's `SourceResponse`/`CharacterResponse`/`PersonResponse` all use (`Guid.ToString("D")`
defaults to lowercase, which would silently mismatch the stored-uppercase convention).

`ImportBatchId` and `NoValueKnown` are deliberately **not** included in the response — no existing
masterdata-style response exposes either today (`QuoteResponse` doesn't have an `ImportBatchId` or
`NoValueKnown` field), and the issue's own field list (`Id`, `Name`, `CompletenessStatus`) doesn't call
for them. If a future issue needs them, add them then.

### 2. `ApiMessages.UniverseNotFound` + i18n lockstep

**Status:** Done.

Add to `src/Quotinator.Constants/Api/ApiMessages.cs`:
```csharp
public const string UniverseNotFound = "ErrorUniverseNotFound";
```

Add to all three `i18ntext/UI.*.json` files (mirroring `ErrorConversationNotFound`'s exact style):
- `UI.en-GB.json`: `"ErrorUniverseNotFound": "No universe with the requested ID was found."`
- `UI.nl.json`: `"ErrorUniverseNotFound": "Er is geen universum gevonden met het opgegeven ID."`
- `UI.de.json`: `"ErrorUniverseNotFound": "Es wurde kein Universum mit der angegebenen ID gefunden."`

### 3. `UniverseEndpoints.cs`

**Status:** Done.

New file `src/Quotinator.Api/Endpoints/UniverseEndpoints.cs`, static class `UniverseEndpoints`,
extension method `MapUniverseEndpoints(this WebApplication app)`. Mirrors `AdminEndpoints.cs`'s
`/audit` handler (repository-direct pagination) for the list endpoint and `ConversationEndpoints.cs`'s
`GetById` for the single-item endpoint:

```csharp
internal static class UniverseEndpoints
{
    internal static void MapUniverseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/universes")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllUniverses")
             .WithSummary("List universes")
             .WithDescription(
                 "Returns a paginated list of universes. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every universe as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetUniverseById")
             .WithSummary("Universe by ID")
             .WithDescription("Returns a single universe by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        IListableRepository<UniverseEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every universe as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLastError is not null)
            return beyondLastError;

        var mapped = new PagedItems<UniverseResponse>(
            result.Items.Select(UniverseResponse.FromEntity).ToList(),
            result.Page, result.PageSize, result.TotalCount);

        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the universe.")] string id,
        IApiLocalizer localizer,
        IListableRepository<UniverseEntity> repository)
    {
        UniverseEntity? entity = Guid.TryParse(id, out var universeId)
            ? await repository.GetByIdAsync(universeId)
            : null;

        var response = entity is null ? null : UniverseResponse.FromEntity(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.UniverseNotFound);
    }
}
```

Add `app.MapUniverseEndpoints();` to `Program.cs` alongside the other four `Map*Endpoints()` calls
(`MapQuoteEndpoints`, `MapAdminEndpoints`, `MapImportEndpoints`, `MapConversationEndpoints`).

No `[Description]`/route change needed to `NumericParameterSchemaTransformer.NumericParamsByPath`
beyond adding `api/v1/masterdata/universes` for `page`/`pageSize` (mirroring the `api/v1/admin/audit`/
`api/v1/import/actions` entries #195 added) — required per CLAUDE.md's "Numeric query parameter
binding pattern" rule ("Add both the endpoint path and the parameter name... to
`NumericParameterSchemaTransformer.NumericParamsByPath`").

### 4. `FakeUniverseRepository`

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Fakes/FakeUniverseRepository.cs`, implementing
`IListableRepository<UniverseEntity>` (which extends `IRepository<UniverseEntity>` — full member list:
`GetByIdAsync`, `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync`, `GetPageAsync`). No
existing fake for `IListableRepository<T>` exists in the codebase to copy — designed here from the two
interface definitions read directly (`src/Quotinator.Data/Repositories/IRepository.cs`,
`IListableRepository.cs`), mirroring `FakeImportActionService`'s style (canned return values,
last-call-args recording only where a test needs it):

```csharp
internal sealed class FakeUniverseRepository : IListableRepository<UniverseEntity>
{
    public PagedItems<UniverseEntity>? ReturnPage { get; set; }
    public UniverseEntity? ReturnById { get; set; }

    public Guid? LastRequestedId { get; private set; }

    public Task<UniverseEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        LastRequestedId = id;
        return Task.FromResult(ReturnById is not null && ReturnById.Id == id ? ReturnById : null);
    }

    public Task<PagedItems<UniverseEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(ReturnPage ?? new PagedItems<UniverseEntity>([], page, pageSize, 0));

    public Task InsertAsync(UniverseEntity entity, IUnitOfWork? unitOfWork = null) => Task.CompletedTask;
    public Task InsertManyAsync(IEnumerable<UniverseEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk) => Task.CompletedTask;
    public Task UpdateAsync(UniverseEntity entity, IUnitOfWork? unitOfWork = null) => Task.CompletedTask;
    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => Task.CompletedTask;
}
```

`ReturnById`'s id-equality check (rather than always returning the configured entity regardless of the
requested id) is deliberate — it's what makes
`GetUniverseById_UnknownId_Returns404`/`GetUniverseById_LowercaseId_MatchesCaseInsensitively`
distinguishable from each other with one fake instance per test.

### 5. `UniverseEndpointsTests.cs`

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Endpoints/UniverseEndpointsTests.cs`. `CreateFactory()` mirrors
`AdminAuditEndpointTests.cs`'s pattern (`FakeQuoteService` + `NoOpDatabaseInitializer` boilerplate +
the entity-specific fake, here `IListableRepository<UniverseEntity>`), per CLAUDE.md's "Endpoint test
pattern".

The four tests named in the issue, plus the full eight-case pagination matrix CLAUDE.md's "Standard
pagination contract" section requires of every new paginated GET endpoint (not previously listed as
its own line item in the issue, but mandatory per that section — flagged here so it isn't missed):

| Test method | Case |
|---|---|
| `GetAllUniverses_ReturnsPaginatedResults` | issue's own test — basic paginated response shape |
| `GetAllUniverses_PageZero_Returns422` | pagination matrix #1 |
| `GetAllUniverses_PageMalformed_Returns422` | pagination matrix #2 |
| `GetAllUniverses_PageSizeMalformed_Returns422` | pagination matrix #3 |
| `GetAllUniverses_PageSizeNegative_Returns422` | pagination matrix #4 |
| `GetAllUniverses_PageSizeAbove500_Returns422NotSilentClamp` | pagination matrix #5 |
| `GetAllUniverses_PageSizeZero_ReturnsAllRowsAsOnePage` | pagination matrix #6 |
| `GetAllUniverses_PageSizeOmitted_DefaultsTo20` | pagination matrix #7 |
| `GetAllUniverses_PageBeyondLast_Returns422DistinctDetail` | pagination matrix #8 |
| `GetUniverseById_ExistingId_ReturnsUniverse` | issue's own test |
| `GetUniverseById_UnknownId_Returns404` | issue's own test |
| `GetUniverseById_LowercaseId_MatchesCaseInsensitively` | issue's own test |
| `GetUniverseById_MalformedId_Returns404NotBadRequest` | malformed-id-as-404 precedent (Background) — not in the
  issue's own list, added because the endpoint design routes both cases through the same 404 path and
  that behaviour needs its own assertion, not just an inference from the well-formed-unknown case |
| `UniverseEndpoints_OnLiveSpec_TaggedMasterData` | proves requirement 3's tag/rate-limit wiring live
  rather than by code inspection only — added consistently across all five masterdata issues during
  cross-plan review, mirroring #187's identical `SeriesEndpoints_OnLiveSpec_TaggedMasterData` |

`GetUniverseById_ExistingId_ReturnsUniverse` must additionally assert `completenessStatus` serializes as
a plain JSON string value (e.g. `"Complete"`), never `{"raw":...,"parsed":...}` — the same shape
assertion #184/#185/#186/#187 each make explicit for their own `SafeValue<T>` fields.

### 6. Documentation

**Status:** Done.

- `README.md`'s REST API Endpoints table: add
  `| GET | \`/api/v1/masterdata/universes\` | All universes, paginated (\`page\`, \`pageSize\`) |` and
  `| GET | \`/api/v1/masterdata/universes/{id}\` | Universe by UUID |`, placed near the existing
  `/api/v1/conversations/{id}` row.
- `addon/DOCS.md`'s API Endpoints table: same two rows, matching that file's existing format.
- `[Description]` attributes on both endpoints (already drafted in Step 3) serve the Scalar/OpenAPI
  side of this requirement.

### 7. Verify

**Status:** Done for the automated portion. `dotnet build --configuration Release` → 0 warnings, 0
errors. `dotnet test --configuration Release --verbosity normal` → full suite green (1454 tests across
all projects), 0 warnings, 0 errors. Red/green proven live: temporarily commenting out
`app.MapUniverseEndpoints()` in `Program.cs` turned 12 of `UniverseEndpointsTests`' 14 tests red (the
remaining 2 — `GetUniverseById_MalformedId_Returns404NotBadRequest` and
`GetAllUniverses_PageBeyondLast_Returns422DistinctDetail` — pass coincidentally on a bare-404/framework-
routing basis, matching the same known limitation `SourceEndpointsTests`'/`SeriesEndpointsTests`'
equivalent tests already have); restoring the registration turned the full suite green again. T1/T2
below still require the developer/live Docker pass.

T1 (developer, Visual Studio): clean startup; `GET /api/v1/masterdata/universes` and
`GET /api/v1/masterdata/universes/{id}` both reachable and return the expected shape. Not yet run.

T2 (Docker): `docker build` + `docker run`, then the Step 7 curl matrix above. Not yet run.

`dotnet build --configuration Release` → must be 0 warnings, 0 errors. `dotnet test --configuration
Release --verbosity normal` → full suite green, no regression. T1 (developer, Visual Studio) and T2
(Docker) both required per this project's standing practice (every issue runs T2 regardless of a
specific documented trigger — see #196's Notes). T2 smoke test:

```bash
curl -s "http://localhost:8080/api/v1/masterdata/universes"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/universes?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/universes/00000000-0000-0000-0000-000000000000"
```
First call: 200, `items`/`page`/`pageSize`/`totalCount`/`totalPages` shape, likely empty (no bundled
source populates Universe data yet — this is a genuinely empty table today, not a bug). Second call:
200, `pageSize` in the response equal to `totalCount` (0 today). Third call: 404 with a `detail`. Add
this matrix to `CLAUDE.md`'s living T2 smoke-test checklist in the same commit, per that section's own
rule that the list only grows.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `GET /api/v1/masterdata/universes` returns a paginated `PagedItems<UniverseResponse>` | Unit test | `UniverseEndpointsTests.GetAllUniverses_ReturnsPaginatedResults` |
| 2 | ✅ | `GET /api/v1/masterdata/universes/{id}` returns the universe for a known id | Unit test | `UniverseEndpointsTests.GetUniverseById_ExistingId_ReturnsUniverse` |
| 3 | ✅ | `GET /api/v1/masterdata/universes/{id}` returns 404 for an unknown well-formed id | Unit test | `UniverseEndpointsTests.GetUniverseById_UnknownId_Returns404` |
| 4 | ✅ | `GET /api/v1/masterdata/universes/{id}` matches case-insensitively | Unit test | `UniverseEndpointsTests.GetUniverseById_LowercaseId_MatchesCaseInsensitively` |
| 5 | ✅ | A malformed `{id}` also returns 404, not a 422/500 | Unit test | `UniverseEndpointsTests.GetUniverseById_MalformedId_Returns404NotBadRequest` |
| 6 | ✅ | The list endpoint satisfies CLAUDE.md's full eight-case pagination matrix | Unit test | `UniverseEndpointsTests`' 8 pagination-matrix tests (Step 5 table) |
| 7 | ✅ | `IListableRepository<UniverseEntity>` DI registration is unchanged by this issue (pre-existing, from #193) | Doc/code review | `Program.cs:309` — confirmed already present before this issue started |
| 8 | ✅ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api`, proven live | Unit test | `UniverseEndpoints_OnLiveSpec_TaggedMasterData` |
| 9 | ✅ | `completenessStatus` serializes as a plain JSON value, never `{raw, parsed}` | Unit test | `UniverseEndpointsTests.GetUniverseById_ExistingId_ReturnsUniverse` (shape assertion) |
| 10 | ✅ | `README.md` and `addon/DOCS.md` document both endpoints | Doc review | Both files' REST API Endpoints tables |
| 11 | ✅ | `ErrorUniverseNotFound` exists and is non-empty in all three `i18ntext/UI.*.json` files | Unit test | `TranslationCompletenessTests` (existing, regression) |
| 12 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite (1454 tests), 0 warnings, 0 errors |
| 13 | ✅ | T1 — app starts in Visual Studio; both endpoints behave as specified | Live (T1) | Developer confirmed 2026-07-19 — clean startup, GetAll/GetById/pagination contract all exercised live |
| 14 | ✅ | T2 — the built image serves both endpoints correctly | Live (T2) | `docker build` + `docker run`: list/get-by-id/not-found matrix per Step 7. Run 2026-07-19 as a combined pass across #184–#189/#204/#205 — endpoint reachable and returns the standard pagination shape (no Universe rows seeded in this bundled dataset, so the empty-list path was confirmed rather than a populated one) |

---

## Notes

This is the first of #184–#189/#192 to actually implement a masterdata endpoint, despite being
numbered after #184–#187 — no existing `Map*Endpoints` sibling could be copied wholesale, so
`UniverseEndpoints.cs`'s shape (repository-direct, no service layer) becomes the template the other
four list/get-by-id issues follow, alongside `AdminEndpoints.cs`'s `/audit` handler this plan drew
from directly. `EntityFilterParsing` (#196) is intentionally not wired up here — Universe is the top of
ADR 011's hierarchy and has no parent entity to filter by; a future filter would be *of* Universe (e.g.
"series in this universe"), which is Series' (#187's) endpoint to add, not this one's.

`ImportBatchId` and `NoValueKnown` are left out of `UniverseResponse` deliberately (Step 1) — revisit
only if a concrete consumer need arises, per this project's Simplicity priority.

The issue's Definition of Done references "closing comment" and issue-close mechanics — out of scope
for this plan doc per the standing rule that `gh issue close`/`gh issue edit` are separate, later,
human-approved steps.

---

## Corrected issue text (for a future `gh issue edit`)

**Title:** Masterdata: GET /api/v1/masterdata/universes list + get-by-id *(unchanged)*

**Body:**

```markdown
## Background

Depends on #183 (shared list-endpoint infrastructure, delivered via #193/#195/#196).

`UniverseEntity` (`src/Quotinator.Engine/Entities/Universe.cs`), added by #179, has no endpoint of any
kind today. `IListableRepository<UniverseEntity>` is already DI-registered (`Program.cs`, added by
#193 in anticipation of this issue) — this issue does not need to add that registration, only build
the endpoints on top of it. This issue gives Universe its first read access, needed before #180's
overlay-file work can be verified against real API responses and before duplicate-Universe discovery
becomes possible.

## What needs to be done

1. `GET /api/v1/masterdata/universes` — paginated list, calling #193's already-registered
   `IListableRepository<UniverseEntity>` directly (no service layer), returning
   `Quotinator.Data.Models.PagedItems<UniverseResponse>` via #195's shared
   `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helpers (reject out-of-range input with 422 —
   these do not clamp). Response items include `Id`, `Name`, `CompletenessStatus`.
2. `GET /api/v1/masterdata/universes/{id}` — single Universe by id, using #195's shared
   `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively (already structural via
   `SqliteRepository<T>.GetByIdAsync`'s `Guid`-typed parameter — no extra handling needed as long as
   the route string is `Guid.TryParse`d before the repository call). A malformed `{id}` also returns
   404, matching the existing `ImportEndpoints.cs` precedent for a route-level `Guid.TryParse` failure.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]`
   attributes. Register `api/v1/masterdata/universes` in `NumericParameterSchemaTransformer
   .NumericParamsByPath` for `page`/`pageSize`.
4. No entity-specific filters yet — deferred per #196's entity-scoped filter-parameter convention.
5. Update `README.md` and `addon/DOCS.md`'s endpoint tables.
6. Build a `UniverseResponse` DTO (`src/Quotinator.Api/Models/UniverseResponse.cs`) flattening
   `UniverseEntity`'s `SafeValue<CompletenessStatus?>` field to a plain `CompletenessStatus` — no
   `SafeValue<T>` JSON converter exists in this codebase; every other call site flattens with
   `entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete` and this DTO should match that
   convention rather than serializing the raw entity.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| New: `Quotinator.Api.Tests` | `GetAllUniverses_ReturnsPaginatedResults` | ❌ |
| New: `Quotinator.Api.Tests` | `GetUniverseById_ExistingId_ReturnsUniverse` | ❌ |
| New: `Quotinator.Api.Tests` | `GetUniverseById_UnknownId_Returns404` | ❌ |
| New: `Quotinator.Api.Tests` | `GetUniverseById_LowercaseId_MatchesCaseInsensitively` | ❌ |
| New: `Quotinator.Api.Tests` | Full 8-case pagination matrix per CLAUDE.md's "Standard pagination contract" | ❌ |

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
