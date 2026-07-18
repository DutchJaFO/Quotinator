# #184 — Masterdata: GET /api/v1/masterdata/sources list + get-by-id

**Status:** In progress (step 10)
**GitHub issue:** #184
**Tiers required:** T1, T2
**Depends on:** #193, #195, #196

---

## Spec requirements (corrected during planning review 2026-07-18)

1. `GET /api/v1/masterdata/sources` — paginated list, using `IListableRepository<Source>.GetPageAsync`
   (already DI-registered) + `Quotinator.Data.Models.PagedItems<T>` (not `PageResponse<T>` — that type
   does not exist) + `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (rejects out-of-range input
   with 422; does not clamp). Response items are a new `SourceResponse` DTO with `Id`, `Title`, `Type`,
   `Date`, `Series` (nullable `MasterDataReference` — **not** a bare `SeriesId`, per CLAUDE.md's
   "Masterdata reference shape" convention added during this planning review — see Background),
   `CompletenessStatus`.
2. `GET /api/v1/masterdata/sources/{id}` — single Source by id, using `NotFoundResult.OkOrNotFound`.
   `{id}` matches case-insensitively (existing GUID parameter binding rule) — `Guid.TryParse` on the
   route parameter is inherently case-insensitive, and `IRepository<T>.GetByIdAsync` accepts a `Guid`,
   so no extra work is needed to satisfy this beyond parsing the route string to `Guid` before calling
   the repository.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes
   for OpenAPI/Scalar documentation.
4. No entity-specific filters yet (e.g. `?seriesId=`) — deferred per #196's entity-scoped
   filter-parameter convention (`CLAUDE.md` "Entity-scoped filter-parameter convention"), which exists
   but has no wired consumer yet; this issue does not become the first one to wire it.
5. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

---

## Background — why this issue exists

Sub-issue of #183. `Source` (`src/Quotinator.Engine/Entities/Source.cs`) has no read endpoint of any
kind today — `IRestorableRepository<Source>` exists but is only used for #59/#68 batch-undo reversal.
This issue adds the first public read access to Sources, preparing for CRUD in a later milestone and for
discovering duplicate/near-duplicate Sources (#182) once results can actually be listed.

**Verified before starting** (per this project's standing rule — #183/#193/#194/#195/#196 all had errors
caught this way):

- The issue's `PageResponse<T>` does not exist. Confirmed the real type is
  `Quotinator.Data.Models.PagedItems<T>` (`src/Quotinator.Data/Models/PagedItems.cs`) — `public record
  PagedItems<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`, not sealed, with a
  computed `TotalPages`. `IListableRepository<T>.GetPageAsync` returns `Task<PagedItems<T>>` directly.
- The issue's "shared pagination-clamp helper" does not exist as a clamp. Confirmed
  `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (`src/Quotinator.Api/Endpoints/Shared/
  PaginationParsing.cs`) reject out-of-range `pageSize`/`page` with 422 rather than clamping — matches
  #195's documented contract.
- `NotFoundResult.OkOrNotFound<T>(T? entity, IApiLocalizer localizer, string notFoundMessageKey) where T
  : class` confirmed present (`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`).
- **DI registration re-verified directly, not assumed**: `src/Quotinator.Api/Program.cs:309` —
  `builder.Services.AddSingleton<IListableRepository<Source>>(sp => (IListableRepository<Source>)
  sp.GetRequiredService<IRestorableRepository<Source>>());`. Present exactly as claimed. No new DI
  registration is needed for Source's repository.
- `RateLimitPolicies.Api` confirmed in `src/Quotinator.Constants/RateLimiting/RateLimitPolicies.cs`.
- `ApiTags.MasterData` confirmed in `src/Quotinator.Constants/Api/ApiTags.cs`, and confirmed it already
  has an OpenAPI tag description registered in `Program.cs`'s `document.Tags` list (added by #196,
  `Program.cs:72`) — so this issue needs no `Program.cs` tag-description change, only the two new
  endpoints and their `MapSourceEndpoints()` registration call.
- **`Source` entity fields re-verified directly against the current file** (entities can drift):
  `Title` (`string`), `Type` (`SafeValue<QuoteType?>`), `Date` (`SafeValue<DateTime?>`), `SeriesId`
  (`Guid?`), `ImportBatchId` (`Guid?`), `CompletenessStatus` (`SafeValue<CompletenessStatus?>`),
  `NoValueKnown` (`IReadOnlyList<string>`), plus `RecordBase`'s `Id`/`DateCreated`/`DateModified`/
  `DateDeleted`/`IsDeleted`. Matches the issue's assumed field list exactly — no drift found.
- **New discrepancy found, not previously flagged**: `NumericParameterSchemaTransformer.NumericParamsByPath`
  (`src/Quotinator.Api/OpenApi/NumericParameterSchemaTransformer.cs`) does not yet have an entry for
  `api/v1/masterdata/sources`. Per CLAUDE.md's "Rules for adding new numeric query parameter", this
  issue must add it (`page`/`pageSize` with `QueryParamDefaults.Page`/`QueryParamDefaults.PageSize`) —
  the issue body never mentions this step at all, matching the exact gap #194 found and #195 had to add
  explicitly for `/admin/audit` and `/import/actions`. Added as an explicit step below so it isn't missed
  again.
- **New discrepancy found**: `ApiMessages.SourceNotFound` does not exist yet in
  `src/Quotinator.Constants/Api/ApiMessages.cs`, and no `ErrorSourceNotFound` key exists in any of the
  three `i18ntext/UI.*.json` files. Both must be added in this issue (mirroring
  `ConversationNotFound`/`ErrorConversationNotFound`).
- **Response DTO decision**: `Source`'s `SafeValue<T>`-wrapped fields have no `System.Text.Json`
  converter anywhere in the codebase — confirmed no `Converters.Add` for `SafeValue<T>` in `Program.cs`.
  Serializing the raw entity would leak `{"raw":..,"parsed":..}` onto the wire. Decision: a
  `SourceResponse` DTO, flattened to plain JSON-friendly types, mirroring `QuoteResponse`'s existing
  flattening pattern (`Type` via `.Parsed?.ToString().ToLowerInvariant() ?? .Raw.ToLowerInvariant()`, per
  `SqliteQuoteService.cs:258-260`). `CompletenessStatus` is the one exception: it already carries
  `[JsonConverter(typeof(JsonStringEnumConverter))]` on the enum itself
  (`src/Quotinator.Data/Entities/CompletenessStatus.cs:10`), so the DTO property can be typed as the enum
  directly (`CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete`, mirroring
  `SqliteImportActionService.cs:966`'s `?? CompletenessStatus.Incomplete` fallback) rather than flattened
  to a string by hand.
- **`Id`/`SeriesId` string convention confirmed**: response DTOs in this codebase type ids as `string`,
  not `Guid` (`QuoteResponse.Id`, `ConversationResponse.Id`), and the codebase's consistent conversion
  idiom is `guid.ToString("D").ToUpperInvariant()` (confirmed via `QuotinatorDatabaseInitializer.cs:265`,
  `ImportActionPlanner.cs` — 8 occurrences, `SqliteImportActionService.cs:613`,
  `SqliteQuoteImportService.cs:73,143`) — matching stored-uppercase convention. `SourceResponse.Id`/
  `SeriesId` follow the same idiom.
- **File placement confirmed**: no service layer sits between this endpoint and the repository (unlike
  `QuoteEndpoints`, which goes through `IQuoteService`), so `SourceResponse` has no reason to live in
  `Quotinator.Core` — nothing in Core needs it, and Core has no dependency on Engine. Placed at
  `src/Quotinator.Api/Models/SourceResponse.cs`, namespace `Quotinator.Api.Models` (new folder, per
  CLAUDE.md's "File placement rule": folder name = namespace segment).
- **No existing fake for `IListableRepository<Source>` or any `IListableRepository<T>`** in
  `tests/Quotinator.Api.Tests/Fakes/` — confirmed via directory listing (`CapturingLogger.cs`,
  `CaptureSink.cs`, `FakeQuoteImportService.cs`, `FakeImportActionService.cs`, `FakeQuoteService.cs`).
  `IListableRepository<T> : IRepository<T>` — `IRepository<T>`'s full member list, re-read directly:
  `GetByIdAsync`, `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync`, plus
  `IListableRepository<T>.GetPageAsync`. A new `FakeSourceRepository` must implement all six.
- **New design element, added during cross-plan review (developer directive, 2026-07-18)**:
  `SourceResponse.SeriesId` (bare `string?`) is replaced with `SourceResponse.Series`
  (`MasterDataReference?`) per CLAUDE.md's new "Masterdata reference shape" convention. Confirmed via
  research: **no existing query anywhere in the codebase joins `Sources.SeriesId` to `Series` to fetch
  the parent's `Name`** — every existing reference to `SeriesId` (`Sql.Sources.SelectExistingById`,
  `Sql.Series.CountActiveReferences`) treats it as a bare FK column. This issue writes the first one, via
  a new `ISourceSeriesReferenceReader` (see Step 3) — the same "resolver, not the generic repository"
  pattern #185 independently designed for `CharacterSources`, now formalised as a general convention
  rather than left as one issue's bespoke solution.
- **Soft-deleted visibility, confirmed structural**: `RepositorySql.SelectPage`/`SelectById`
  (`src/Quotinator.Data/Repositories/RepositorySql.cs`) already filter `IsDeleted = 0` unconditionally, no
  override exists anywhere in the codebase — so Sources themselves are already invisible once
  soft-deleted, for free, matching CLAUDE.md's "Soft-deleted rows are invisible by default, everywhere".
  The new `ISourceSeriesReferenceReader` query must independently apply the same rule to the *joined*
  `Series` row — a Source pointing at a soft-deleted Series must resolve `Series` to `null`, not surface
  a dangling reference. No `includeDeleted` opt-in is being built for this issue — per that same
  convention, it's added only when a concrete consumer needs it, not speculatively.

Conventions and constants only from #196 are being consumed here, not re-decided — this issue is the
convention's first real consumer for the "no filter yet" path.

---

## Steps

### 1. `SourceResponse` DTO

**Status:** Done.

New file `src/Quotinator.Api/Models/SourceResponse.cs`, namespace `Quotinator.Api.Models`:

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Source — a film, television series, book, or other
/// source from which quotes are drawn.</summary>
public sealed class SourceResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The title of the source in its original language.</summary>
    public required string Title { get; init; }

    /// <summary>Media category: <c>movie</c>, <c>tv</c>, <c>anime</c>, <c>book</c>, or <c>person</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Publication or release date, as precise as the source allows (e.g. <c>"1994"</c>,
    /// <c>"1994-06"</c>). <c>null</c> when unknown.</summary>
    public string? Date { get; init; }

    /// <summary>The series this source belongs to, if any (#179), as a minimal read-only reference — the
    /// series' <c>Id</c>/<c>Name</c> only, resolved via <see cref="ISourceSeriesReferenceReader"/> (Step 3).
    /// <c>null</c> for a standalone source, and <c>null</c> if the linked series has been soft-deleted
    /// (per CLAUDE.md's "Soft-deleted rows are invisible by default" convention — a dangling reference to
    /// a deleted series is never surfaced).</summary>
    public MasterDataReference? Series { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }
}
```

`CompletenessStatus` needs `using Quotinator.Data.Entities;` — that enum already carries
`[JsonConverter(typeof(JsonStringEnumConverter))]`. With no naming policy argument, that converter
serializes using the member's exact declared name (`"Incomplete"`, `"NeedsReview"`, `"Complete"`), not
lowercased — no further transform needed on the DTO side.

A private mapping method (`SourceEndpoints.ToResponse(Source source, MasterDataReference? series)`)
performs the flattening — `series` is resolved separately (Step 3) and passed in, since a single-table
`Source` mapping has no way to know its Series' `Name`:

```csharp
private static SourceResponse ToResponse(Source source, MasterDataReference? series) => new()
{
    Id                 = source.Id.ToString("D").ToUpperInvariant(),
    Title              = source.Title,
    Type               = source.Type.Parsed?.ToString().ToLowerInvariant()
                          ?? source.Type.Raw.ToLowerInvariant(),
    Date               = string.IsNullOrEmpty(source.Date.Raw) ? null : source.Date.Raw,
    Series             = series,
    CompletenessStatus = source.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
};
```

### 2. `ApiMessages.SourceNotFound` + i18n lockstep

**Status:** Done.

Add to `src/Quotinator.Constants/Api/ApiMessages.cs`:
```csharp
public const string SourceNotFound = "ErrorSourceNotFound";
```

Add `"ErrorSourceNotFound"` to all three `i18ntext/UI.*.json` files in the same commit (mirroring
`ErrorConversationNotFound`'s three entries):
- `UI.en-GB.json`: `"No source with the requested ID was found."`
- `UI.nl.json`: `"Er is geen bron gevonden met het opgegeven ID."`
- `UI.de.json`: `"Es wurde keine Quelle mit der angegebenen ID gefunden."`

`TranslationCompletenessTests` must stay green.

### 3. `MasterDataReference` type + `ISourceSeriesReferenceReader`

**Status:** Done.

**`MasterDataReference`** — if not already created by whichever of #184/#185/#187 lands first (all three
need it; create once, reuse), new file `src/Quotinator.Api/Models/MasterDataReference.cs`:
```csharp
namespace Quotinator.Api.Models;

/// <summary>A minimal, read-only reference to a related masterdata entity — just enough to display
/// without a separate lookup. Fetch the full record via that entity's own masterdata endpoint for more
/// detail. See CLAUDE.md's "Masterdata reference shape" convention.</summary>
public sealed record MasterDataReference(string Id, string Name);
```

**`ISourceSeriesReferenceReader`** — new files `src/Quotinator.Engine/Repositories/
ISourceSeriesReferenceReader.cs`/`SourceSeriesReferenceReader.cs`, namespace `Quotinator.Engine
.Repositories` (same folder #185 introduces for `ICharacterSourceLinkReader` — not a new namespace).
Returns plain `(Guid Id, string Name)` tuples, not `Quotinator.Api.Models.MasterDataReference` directly —
`Quotinator.Engine` has no dependency on `Quotinator.Api`:

```csharp
namespace Quotinator.Engine.Repositories;

/// <summary>Resolves a Source's SeriesId to its Series' (Id, Name), filtered to an active (non-deleted)
/// Series only — never writes.</summary>
public interface ISourceSeriesReferenceReader
{
    /// <summary>The linked Series' (Id, Name) for one Source, or <c>null</c> if the Source has no Series
    /// or its Series has been soft-deleted.</summary>
    Task<(Guid Id, string Name)?> GetSeriesReferenceAsync(Guid sourceId);

    /// <summary>The linked Series' (Id, Name) for each of the given Sources, in one round-trip. A Source
    /// with no active Series link is absent from the result rather than mapped to a null entry — callers
    /// default missing keys to <c>null</c>.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, string Name)>> GetSeriesReferencesForManyAsync(IReadOnlyList<Guid> sourceIds);
}
```

New `Sql.Sources` queries in `src/Quotinator.Engine/Queries/Sql.cs`, matching the established
double-`IsDeleted`-gated join idiom (`Sql.Quotes.SelectBase`, `Sql.Characters.SelectIdBySourceAndName`):

```csharp
/// <summary>Active Series reference for one Source — #184's GetById join. No row if the Source has no
/// Series, or its Series has been soft-deleted.</summary>
internal const string SelectSeriesReferenceForSource =
    "SELECT ser.Id, ser.Name FROM Sources s " +
    "JOIN Series ser ON ser.Id = s.SeriesId AND ser.IsDeleted = 0 " +
    "WHERE s.Id = @sourceId AND s.IsDeleted = 0;";

/// <summary>
/// Active Series references for a batch of Sources in a single round-trip — #184's list join, avoiding
/// one query per row across a page. A Source with no active Series link is simply absent from the result.
/// </summary>
internal const string SelectSeriesReferencesForSources =
    "SELECT s.Id AS SourceId, ser.Id AS SeriesId, ser.Name AS SeriesName FROM Sources s " +
    "JOIN Series ser ON ser.Id = s.SeriesId AND ser.IsDeleted = 0 " +
    "WHERE s.Id IN @sourceIds AND s.IsDeleted = 0;";
```

`SourceSeriesReferenceReader` implementation mirrors `CharacterSourceLinkReader`'s shape (Dapper
`QueryFirstOrDefaultAsync`/`QueryAsync` against `IDbConnectionFactory`, a private `record` row type for
the batch form's three-column result). Register in `Program.cs` alongside the other repository
registrations (near `Program.cs:310`):
```csharp
builder.Services.AddSingleton<ISourceSeriesReferenceReader, SourceSeriesReferenceReader>();
```

### 4. `SourceEndpoints.cs`

**Status:** Done.

New file `src/Quotinator.Api/Endpoints/SourceEndpoints.cs`, static class `SourceEndpoints`, extension
method `MapSourceEndpoints(this WebApplication app)`, mirroring `ConversationEndpoints.cs`'s structure
and `AdminEndpoints.cs`'s `/audit` handler's repository-directly-to-`PagedItems<T>` shape (no service
layer in between, unlike `QuoteEndpoints`):

```csharp
internal static class SourceEndpoints
{
    private sealed class Log { }

    internal static void MapSourceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/sources")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSources")
             .WithSummary("List sources")
             .WithDescription(
                 "Returns a paginated list of Sources — the films, television series, books, and other " +
                 "works quotes are drawn from. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSourceById")
             .WithSummary("Source by ID")
             .WithDescription("Returns a single Source by UUID. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Source> repository,
        ISourceSeriesReferenceReader seriesReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllSources] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var sourceIds        = result.Items.Select(s => s.Id).ToList();
        var seriesBySourceId = await seriesReader.GetSeriesReferencesForManyAsync(sourceIds);

        var items = result.Items
            .Select(s => ToResponse(s, seriesBySourceId.TryGetValue(s.Id, out var series)
                ? new MasterDataReference(series.Id.ToString("D").ToUpperInvariant(), series.Name)
                : null))
            .ToList();

        var response = new PagedItems<SourceResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the source.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Source> repository,
        ISourceSeriesReferenceReader seriesReader)
    {
        logger.LogInformation("[Api - GetSourceById] id={Id}", id);

        if (!Guid.TryParse(id, out var guid))
            return NotFoundResult.OkOrNotFound<SourceResponse>(null, localizer, ApiMessages.SourceNotFound);

        var source = await repository.GetByIdAsync(guid);
        if (source is null)
            return NotFoundResult.OkOrNotFound<SourceResponse>(null, localizer, ApiMessages.SourceNotFound);

        var seriesRef = await seriesReader.GetSeriesReferenceAsync(guid);
        var series    = seriesRef is { } s ? new MasterDataReference(s.Id.ToString("D").ToUpperInvariant(), s.Name) : null;

        return NotFoundResult.OkOrNotFound(ToResponse(source, series), localizer, ApiMessages.SourceNotFound);
    }

    private static SourceResponse ToResponse(Source source, MasterDataReference? series) => new() { /* see Step 1 */ };
}
```

`GetByIdAsync(Guid id, ...)` on `IRepository<T>` already only matches the requested id — `Guid.TryParse`
itself is case-insensitive on the input string, so no additional case-folding is needed at this layer;
the case-insensitive-matching burden is on the SQL layer beneath `SqliteRepository<T>`, which is
out of scope here (already correct, since `Source`'s repository is the existing `SqliteRepository<Source>`
via `IRestorableRepository<Source>`, unaffected by this issue).

Register the call in `Program.cs` alongside the other `Map*Endpoints()` calls (`Program.cs:539-542`):
```csharp
app.MapSourceEndpoints();
```

### 5. Register the OpenAPI numeric-param transformer path

**Status:** Done.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath` (found missing during planning — not in
the original issue text):
```csharp
["api/v1/masterdata/sources"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```
Without this, `page`/`pageSize` publish as bare `string` in the OpenAPI spec instead of
`integer|null` — the exact #194 gap.

### 6. `FakeSourceRepository`

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Fakes/FakeSourceRepository.cs`, implementing
`IListableRepository<Source>`. In-memory `List<Source>` backing store, seeded via constructor parameter
(`IEnumerable<Source>? seed = null`), so tests can construct it with known fixtures:

- `GetPageAsync(page, pageSize, orderBy, unitOfWork)` — orders by `DateCreated` ascending (matching the
  documented default), paginates in memory, returns a real `PagedItems<Source>` with the effective
  `pageSize` (0 → all rows as one page), mirroring the real repository's documented contract so the fake
  cannot silently diverge from #195's `pageSize = 0` behaviour.
- `GetByIdAsync(id, unitOfWork)` — case-insensitive `Guid` match (`Guid` comparison is inherently
  case-insensitive once parsed, so a straight `==` on `Guid` values is already correct here — the
  case-insensitivity concern only applies to the *string* form before parsing, which the endpoint handles
  in Step 4), returns `null` if not found or `IsDeleted`.
- `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync` — implemented minimally (append/
  update/mark-deleted against the in-memory list) rather than `throw new NotImplementedException()`, so
  the fake is a genuine substitutable double per this project's testing conventions, not a partial stub
  that would break if a future test exercises a write path through it.

**`FakeSourceSeriesReferenceReader`** — new file `tests/Quotinator.Api.Tests/Fakes/
FakeSourceSeriesReferenceReader.cs`, implementing `ISourceSeriesReferenceReader`. Backed by a
constructor-supplied `IReadOnlyDictionary<Guid, (Guid Id, string Name)>` (Source id → Series reference);
a Source id absent from the dictionary resolves to `null`/no entry, exactly matching the real reader's
documented "absent, not null-valued" contract for both `GetSeriesReferenceAsync` and
`GetSeriesReferencesForManyAsync`. This is a resolver double, not a joined-table simulation — it never
needs to model soft-deletion itself, since the test simply omits a Series' entry from the seed dictionary
to represent "the Series doesn't resolve" (whether because it doesn't exist or because it was
soft-deleted; the reader's contract makes the two indistinguishable to its caller by design).

### 7. Endpoint tests

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Endpoints/SourceEndpointsTests.cs`. `CreateFactory` follows
`AdminAuditEndpointTests.cs`'s pattern (register `FakeQuoteService`, `NoOpDatabaseInitializer`, and a
`FakeSourceRepository` — real or caller-supplied — as `IListableRepository<Source>`).

Required tests (from the issue) plus the full eight-case pagination matrix CLAUDE.md's "Standard
pagination contract" mandates for every new paginated GET endpoint:

- `GetAllSources_ReturnsPaginatedResults`
- `GetAllSources_PageZero_Returns422`
- `GetAllSources_PageMalformed_Returns422`
- `GetAllSources_PageSizeMalformed_Returns422`
- `GetAllSources_PageSizeNegative_Returns422`
- `GetAllSources_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllSources_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllSources_PageSizeOmitted_DefaultsTo20`
- `GetAllSources_PageBeyondLast_Returns422DistinctDetail`
- `GetSourceById_ExistingId_ReturnsSource`
- `GetSourceById_UnknownId_Returns404`
- `GetSourceById_LowercaseId_MatchesCaseInsensitively`
- `GetSourceById_MalformedId_Returns404NotBadRequest` (mirrors the case-insensitive-id spirit: a
  non-Guid `{id}` segment must not throw or bare-400 — resolved to the same 404 path as "not found",
  consistent with `NotFoundResult`'s existing 404-only contract for this endpoint shape)
- `GetSourceById_UnknownDate_ReturnsNullNotEmptyString` — seeds a `Source` with `Date.Raw` empty and
  asserts the response's `date` field serializes as JSON `null`, not `""`, mirroring #186's identical
  `Person.DateOfBirth`/`DateOfDeath` null-handling test for the same `SafeValue<T>.Raw`-based mapping
  pattern (`Source.Date` and `Person.DateOfBirth`/`DateOfDeath` share the same nullable-string failure
  mode — both must be tested the same way).
- `SourceEndpoints_OnLiveSpec_TaggedMasterData` — extends `OpenApiSpecEndpointTests` (or a small
  dedicated assertion against `/openapi/v1.json`'s `tags` array for both operations), proving requirement
  3's tag/rate-limit wiring live rather than by code inspection only. Mirrors #187's identical test for
  `SeriesEndpoints` — added consistently across all five masterdata issues during cross-plan review, not
  unique to one.
- `GetSourceById_SourceHasSeries_ReturnsSeriesReference` — seeds a Source with a `SeriesId` and a matching
  `FakeSourceSeriesReferenceReader` entry, asserts the response's `series` field is `{id, name}` matching
  the seeded Series.
- `GetSourceById_SourceHasNoSeries_ReturnsNullSeries` — seeds a Source with no reader entry, asserts
  `series` serializes as JSON `null`.
- `GetSourceById_SeriesSoftDeleted_ReturnsNullSeries` — seeds a Source whose `SeriesId` is set, but omits
  the corresponding entry from `FakeSourceSeriesReferenceReader`'s seed dictionary (modelling a
  soft-deleted Series, per the reader's documented "absent means unresolved" contract), asserts `series`
  serializes as JSON `null`, not a dangling reference — proving CLAUDE.md's "Soft-deleted rows are
  invisible by default" convention holds for this new joined reference, not just for the Source row
  itself.
- `GetAllSources_MultipleSourcesWithSeries_BatchResolvesEachSeries` — seeds several Sources, some with a
  Series and some without, across one page; asserts each item's `series` field resolves independently and
  correctly, proving the batched `GetSeriesReferencesForManyAsync` path (not just the single-id
  `GetById` path) maps results back to the right Source.

`GetSourceById_LowercaseId_MatchesCaseInsensitively` seeds a `Source` with an explicit uppercase-stored
`Id`, requests it via a deliberately-lowercased id string, and asserts 200 with the matching `Id` in the
response — proving the case-insensitive rule end to end, not just at the repository layer.

**Response shape assertions** (proving Step 1's `SourceResponse` design actually prevents the
`SafeValue<T>` leak, not merely that the DTO type exists): `GetSourceById_ExistingId_ReturnsSource` must
additionally assert `type`/`completenessStatus` serialize as plain JSON string values (e.g. `"movie"`,
`"Complete"`), never `{"raw":...,"parsed":...}` — the same assertion #186's `PersonResponse` plan makes
explicit for its own `SafeValue<T>` fields.

### 8. Documentation

**Status:** Done.

Update `README.md`'s and `addon/DOCS.md`'s REST API Endpoints tables — add rows for
`GET /api/v1/masterdata/sources` and `GET /api/v1/masterdata/sources/{id}`, following the existing table
row style (see `README.md:143-146` for the pattern used by the neighbouring `/quotes` and
`/conversations` rows).

### 9. Solution file

**Status:** Done — no `Quotinator.slnx` change needed. All six new files
(`src/Quotinator.Api/Models/SourceResponse.cs`, `src/Quotinator.Api/Models/MasterDataReference.cs`,
`src/Quotinator.Engine/Repositories/ISourceSeriesReferenceReader.cs`,
`src/Quotinator.Engine/Repositories/SourceSeriesReferenceReader.cs`,
`src/Quotinator.Api/Endpoints/SourceEndpoints.cs`, plus the three new files under
`tests/Quotinator.Api.Tests/Fakes/` and `tests/Quotinator.Api.Tests/Endpoints/`) live inside an
existing SDK-style `<Project>` folder already referenced in `Quotinator.slnx`
(`src/Quotinator.Api/Quotinator.Api.csproj`, `src/Quotinator.Engine/Quotinator.Engine.csproj`,
`tests/Quotinator.Api.Tests/Quotinator.Api.Tests.csproj`), all of which glob `.cs` files
automatically — confirmed via `Quotinator.slnx`'s existing `<Folder Name="/src/">`/`/tests/">` entries
listing only the `.csproj` files themselves, never individual source files within them.

Add `src/Quotinator.Api/Models/SourceResponse.cs`, `src/Quotinator.Api/Models/MasterDataReference.cs` (if
not already added by whichever of #184/#185/#187 lands first), `src/Quotinator.Engine/Repositories/
ISourceSeriesReferenceReader.cs`/`SourceSeriesReferenceReader.cs`, `src/Quotinator.Api/Endpoints/
SourceEndpoints.cs`, and `tests/Quotinator.Api.Tests/Fakes/FakeSourceRepository.cs`/
`FakeSourceSeriesReferenceReader.cs`/`tests/Quotinator.Api.Tests/Endpoints/SourceEndpointsTests.cs` to
`Quotinator.slnx` if they are not automatically picked up by the existing project globs (verify by opening
the solution — most `.cs` files under an existing project folder are included automatically; only files
outside any project need an explicit `<Folder>` entry per CLAUDE.md's Visual Studio Solution section).

### 10. Verify

**Status:** Done — `dotnet build --configuration Release` reports 0 Warning(s)/0 Error(s); `dotnet test
--configuration Release --verbosity normal` reports the full suite green (343 tests, 0 Warning(s)/0
Error(s)). Confirmed red-before-green directly: temporarily commented out `app.MapSourceEndpoints();`
in `Program.cs` and reran `SourceEndpointsTests` — 14 of 19 tests failed (structural/count/id/tag
assertions genuinely exercise the new endpoint), then restored the registration and reran to confirm
all 19 green again. The remaining 5 tests that stayed green without the registration are absence-based
assertions (`GetSourceById_UnknownId_Returns404`, `GetSourceById_MalformedId_Returns404NotBadRequest`,
`GetSourceById_UnknownDate_ReturnsNullNotEmptyString`, `GetSourceById_SourceHasNoSeries_ReturnsNullSeries`,
`GetSourceById_SeriesSoftDeleted_ReturnsNullSeries`) — an unmapped route also returns 404 with a body
that vacuously satisfies "field is null or absent", a pre-existing characteristic of this codebase's
global `DefaultIgnoreCondition = WhenWritingNull` JSON option, not something specific to this issue.
Each has a positive-case counterpart that did go red
(`GetSourceById_ExistingId_ReturnsSource`/`GetSourceById_SourceHasSeries_ReturnsSeriesReference`),
which is what actually proves the mapping/join logic. T1/T2 (live verification) have not run yet — see
rows 24/25 below.

T2 (Docker) has deliberately **not** been run for this issue individually — the developer is running a
single combined T2 pass across all five masterdata issues (#184–#188) once all five are implemented,
per session instructions.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full suite green, 0 warnings, 0 errors. Confirm all listed expected tests started
red (verify by running them against a pre-implementation stash or by temporarily reverting
`SourceEndpoints.cs` registration) before implementation, then green after.

T2 (Docker): `docker build` + `docker run`, then:
```bash
curl -s "http://localhost:8080/api/v1/masterdata/sources"
curl -s "http://localhost:8080/api/v1/masterdata/sources?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources?pageSize=999"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/00000000-0000-0000-0000-000000000000"
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"masterdata/sources[^"]*"'
```
Confirm: default list returns 200 with `items`/`page`/`pageSize`/`totalCount`/`totalPages`;
`pageSize=0` returns every Source as one page; `pageSize=999` returns 422; an unknown id returns 404
with `ErrorSourceNotFound`'s message; the OpenAPI spec publishes `page`/`pageSize` as `integer|null` on
`api/v1/masterdata/sources`. Also fetch a real Source id via
`Quotinator.Tools.DbInspector` (`SELECT Id, Title FROM Sources WHERE IsDeleted = 0 LIMIT 1;`) and confirm
`GET /api/v1/masterdata/sources/{that id, lowercased}` returns 200 — live proof of case-insensitive
matching, not just the unit test. If any bundled/seeded Source has a `SeriesId`
(`SELECT Id, Title FROM Sources WHERE SeriesId IS NOT NULL AND IsDeleted = 0 LIMIT 1;`), fetch that
Source by id too and confirm the response's `series` field is `{id, name}`, not `null` — live proof the
`ISourceSeriesReferenceReader` join actually resolves, not just the fake-backed unit tests.

This project always runs T2 regardless of a documented trigger — this issue's own change to `Program.cs`
(the new `MapSourceEndpoints()` call) also independently satisfies `docs/release-verification.md`'s
"touches Program.cs startup" trigger.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `GET /api/v1/masterdata/sources` returns a paginated list of Sources | Unit test | `SourceEndpointsTests.GetAllSources_ReturnsPaginatedResults` |
| 2 | ✅ | `page=0` returns 422 | Unit test | `SourceEndpointsTests.GetAllSources_PageZero_Returns422` |
| 3 | ✅ | Malformed `page`/`pageSize` returns 422 | Unit test | `SourceEndpointsTests.GetAllSources_PageMalformed_Returns422`, `_PageSizeMalformed_Returns422` |
| 4 | ✅ | Negative `pageSize` returns 422 | Unit test | `SourceEndpointsTests.GetAllSources_PageSizeNegative_Returns422` |
| 5 | ✅ | `pageSize > 500` returns 422, never clamped | Unit test | `SourceEndpointsTests.GetAllSources_PageSizeAbove500_Returns422NotSilentClamp` |
| 6 | ✅ | `pageSize = 0` returns every row as one page | Unit test | `SourceEndpointsTests.GetAllSources_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 7 | ✅ | Omitted `pageSize` defaults to 20 | Unit test | `SourceEndpointsTests.GetAllSources_PageSizeOmitted_DefaultsTo20` |
| 8 | ✅ | A page beyond the last returns 422 with a distinct detail | Unit test | `SourceEndpointsTests.GetAllSources_PageBeyondLast_Returns422DistinctDetail` |
| 9 | ✅ | `GET /api/v1/masterdata/sources/{id}` returns the matching Source | Unit test | `SourceEndpointsTests.GetSourceById_ExistingId_ReturnsSource` |
| 10 | ✅ | An unknown id returns 404 | Unit test | `SourceEndpointsTests.GetSourceById_UnknownId_Returns404` |
| 11 | ✅ | A lowercase id matches an uppercase-stored id | Unit test | `SourceEndpointsTests.GetSourceById_LowercaseId_MatchesCaseInsensitively` |
| 12 | ✅ | A malformed `{id}` route segment returns 404, not an unhandled exception or bare 400 | Unit test | `SourceEndpointsTests.GetSourceById_MalformedId_Returns404NotBadRequest` |
| 13 | ✅ | An unknown `date` serializes as JSON `null`, not `""` | Unit test | `SourceEndpointsTests.GetSourceById_UnknownDate_ReturnsNullNotEmptyString` |
| 14 | ✅ | `type`/`completenessStatus` serialize as plain JSON values, never `{raw, parsed}` | Unit test | `SourceEndpointsTests.GetSourceById_ExistingId_ReturnsSource` (shape assertions) |
| 15 | ✅ | `page`/`pageSize` publish as `integer` in the OpenAPI spec for `api/v1/masterdata/sources` | Unit test | `NumericParameterSchemaTransformerTests` (new cases), `OpenApiSpecEndpointTests.PageParam_OnLiveSpec_PublishesIntegerType` |
| 16 | ✅ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api`, proven live | Unit test | `SourceEndpoints_OnLiveSpec_TaggedMasterData` |
| 17 | ✅ | `ApiMessages.SourceNotFound` exists and all three locale files carry `ErrorSourceNotFound` | Unit test | `TranslationCompletenessTests` |
| 18 | ✅ | A Source with a Series returns `series` as `{id, name}` | Unit test | `SourceEndpointsTests.GetSourceById_SourceHasSeries_ReturnsSeriesReference` |
| 19 | ✅ | A Source with no Series returns `series` as `null` | Unit test | `SourceEndpointsTests.GetSourceById_SourceHasNoSeries_ReturnsNullSeries` |
| 20 | ✅ | A Source whose Series has been soft-deleted returns `series` as `null`, not a dangling reference | Unit test | `SourceEndpointsTests.GetSourceById_SeriesSoftDeleted_ReturnsNullSeries` |
| 21 | ✅ | The list endpoint resolves each item's Series independently via the batched reader | Unit test | `SourceEndpointsTests.GetAllSources_MultipleSourcesWithSeries_BatchResolvesEachSeries` |
| 22 | ✅ | `README.md`/`addon/DOCS.md` document both new endpoints | Doc review | Endpoint tables updated |
| 23 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green (343 tests), 0 warnings, 0 errors |
| 24 | ❌ | T1 — app starts in Visual Studio; both endpoints reachable | Live (T1) | Developer confirmed |
| 25 | ❌ | T2 — the live contract holds against the built image, including a live Series reference resolving to `{id, name}` | Live (T2) | `docker build`/`docker run` matrix — see Step 10 |

---

## Notes

This issue is #196's filter-parameter convention's first opportunity to be *not* invoked — deliberately,
per the issue's own requirement 4. If a future issue (#185–#189, #192) needs `?seriesId=` on this
endpoint, it wires `EntityFilterParsing.ResolveAsync` in at that time rather than this issue speculatively
adding it.

`Source`'s underlying repository (`SqliteRepository<Source>` via `IRestorableRepository<Source>`) is
unmodified by this issue — its `GetPageAsync`/`GetByIdAsync` SQL already went through #193's/#195's own
verification (including the `pageSize = 0` → `LIMIT -1` fix), so this issue does not need to re-prove
that layer, only the new endpoint/DTO/mapping layer, plus the new `ISourceSeriesReferenceReader` join,
on top of it.

`MasterDataReference` (`src/Quotinator.Api/Models/MasterDataReference.cs`) is a shared type, not owned by
this issue specifically — #185 and #187 also need it. Whichever of the three lands first creates the
file; the other two reuse it rather than redefining it. If #184 lands first, note this explicitly in
#185's and #187's own plan docs so their own Step 1 doesn't recreate it.

---

## Corrected issue text (for a future `gh issue edit`)

```
## Background

Depends on #183 (shared list-endpoint infrastructure — generic `IListableRepository<T>`,
`Quotinator.Data.Models.PagedItems<T>`, pagination/not-found helpers, filter convention,
`/api/v1/masterdata/` routing convention).

`Source` (`src/Quotinator.Engine/Entities/Source.cs`) has no read endpoint of any kind today —
`IRestorableRepository<Source>` exists but is only used for #59/#68 batch-undo reversal. This issue
adds the first public read access to Sources, preparing for CRUD in a later milestone and for
discovering duplicate/near-duplicate Sources (#182) once results can actually be listed.

## What needs to be done

1. `GET /api/v1/masterdata/sources` — paginated list, using #183's `IListableRepository<Source>` +
   `Quotinator.Data.Models.PagedItems<T>` + the shared `PaginationParsing` helper (rejects
   out-of-range `page`/`pageSize` with 422 — it does not clamp). Response items include `Id`, `Title`,
   `Type`, `Date`, `Series` (nullable `MasterDataReference` — a minimal `{id, name}` record, per
   CLAUDE.md's "Masterdata reference shape" convention; never a bare `SeriesId`), `CompletenessStatus`,
   via a new `SourceResponse` DTO (the raw `Source` entity's `SafeValue<T>`-wrapped fields cannot be
   serialized directly).
2. `GET /api/v1/masterdata/sources/{id}` — single Source by id, using #183's shared
   `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively (per this project's existing
   GUID parameter binding rule).
3. New `ISourceSeriesReferenceReader` (`Quotinator.Engine.Repositories`) resolves a Source's `SeriesId`
   to its Series' `(Id, Name)`, filtered to an active (non-soft-deleted) Series only — a Source pointing
   at a soft-deleted Series resolves `Series` to `null`, per CLAUDE.md's "Soft-deleted rows are invisible
   by default" convention. Both a single-id and a batched form are needed (the list endpoint must not
   issue one query per row).
4. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes
   for OpenAPI/Scalar documentation (per `QuoteEndpoints.cs`'s existing pattern).
5. Register `api/v1/masterdata/sources`'s `page`/`pageSize` parameters in
   `NumericParameterSchemaTransformer.NumericParamsByPath` (per #194's registration requirement — easy
   to miss, as #195 found for `/admin/audit` and `/import/actions`).
6. Add `ApiMessages.SourceNotFound` (`"ErrorSourceNotFound"`) with lockstep translations in all three
   `i18ntext/UI.*.json` files.
7. No entity-specific filters yet (e.g. `?seriesId=`) — deferred to a future issue per #196's
   documented entity-scoped filter-parameter convention, added only when a concrete need exists.
8. Update `README.md` and `addon/DOCS.md`'s endpoint tables per this project's "Keeping API
   documentation in sync" rule.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| New: `Quotinator.Api.Tests` | `GetAllSources_ReturnsPaginatedResults` | ❌ |
| New: `Quotinator.Api.Tests` | `GetAllSources_PageSizeOmitted_DefaultsTo20` | ❌ |
| New: `Quotinator.Api.Tests` | `GetAllSources_MultipleSourcesWithSeries_BatchResolvesEachSeries` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_SourceHasSeries_ReturnsSeriesReference` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_SourceHasNoSeries_ReturnsNullSeries` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_SeriesSoftDeleted_ReturnsNullSeries` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_ExistingId_ReturnsSource` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_UnknownId_Returns404` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_LowercaseId_MatchesCaseInsensitively` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_MalformedId_Returns404NotBadRequest` | ❌ |
| New: `Quotinator.Api.Tests` | `SourceEndpoints_OnLiveSpec_TaggedMasterData` | ❌ |
| New: `Quotinator.Api.Tests` | `GetSourceById_UnknownDate_ReturnsNullNotEmptyString` | ❌ |

Plus the full eight-case pagination matrix CLAUDE.md's "Standard pagination contract" mandates for every
new paginated GET endpoint (page=0, malformed page/pageSize, negative pageSize, pageSize>500,
pageSize=0, pageSize omitted, page beyond last).

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
