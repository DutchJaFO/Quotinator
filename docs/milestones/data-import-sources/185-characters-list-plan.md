# #185 — Masterdata: GET /api/v1/masterdata/characters list + get-by-id

**Status:** Planning
**GitHub issue:** #185
**Tiers required:** T1, T2
**Depends on:** #193, #195, #196, #179

---

## Spec requirements (corrected during planning review 2026-07-18)

1. `GET /api/v1/masterdata/characters` — paginated list, using `IListableRepository<Character>
   .GetPageAsync` (already DI-registered) + `Quotinator.Data.Models.PagedItems<T>` (not `PageResponse<T>`
   — that type does not exist) + `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (rejects
   out-of-range input with 422; does not clamp). Response items are a new `CharacterResponse` DTO with
   `Id`, `Name`, `CompletenessStatus`, and `Sources` — a list of minimal `MasterDataReference` (`{id,
   name}`) records, **not** bare `SourceIds`, per CLAUDE.md's "Masterdata reference shape" convention
   added during this planning review (see Background) — populated via a new join query against
   `CharacterSources`, batched once per page (not once per row).
2. `GET /api/v1/masterdata/characters/{id}` — single Character by id, same `CharacterResponse` shape,
   using `NotFoundResult.OkOrNotFound`. `{id}` matches case-insensitively — `Guid.TryParse` on the route
   string is inherently case-insensitive, and `SqliteRepository<T>.GetByIdAsync` always uppercases the id
   before binding it (`id.ToString("D").ToUpperInvariant()`), so no extra work is needed beyond parsing
   the route string to `Guid` before calling the repository.
3. A new join query/queries against `CharacterSources` (joined through to `Sources` for `Title`), exposed
   through a new `ICharacterSourceLinkReader` (design decision — see Background) — single-id form for
   `GetById`, batch form for `GetAll` (one query for the whole page, not one per row). Returns each
   linked Source's `(Id, Title)`, not a bare `Guid`, since the response must surface a display name.
4. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes
   for OpenAPI/Scalar documentation.
5. No entity-specific filters yet (e.g. `?sourceId=`) — deferred per #196's entity-scoped
   filter-parameter convention (`CLAUDE.md` "Entity-scoped filter-parameter convention"), which exists
   but has no wired consumer yet; this issue does not become the first one to wire it.
6. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

---

## Background — why this issue exists

Sub-issue of #183. `Character` (`src/Quotinator.Engine/Entities/Character.cs`) has no read endpoint
today. Since #179 (ADR 011), a Character links to its Source(s) via the `CharacterSources` join table
(`CharacterSourceEntity`), not a direct FK — the response shape for this issue must expose that
many-to-many relationship rather than a single `SourceId`, or the endpoint would misrepresent #179's own
schema change. This is the most design-heavy of the masterdata list issues (#184/#185/#186/#187/#188):
every sibling entity (Source, Person, Series, Universe) reads straight off its own repository with no
join, but Character requires one.

**Verified before starting** (per this project's standing rule — #183/#193/#194/#195/#196/#184 all had
errors caught this way; #184's own plan doc, the direct structural sibling for this issue, was read and
used as the template):

- The issue's `PageResponse<T>` does not exist. Confirmed the real type is
  `Quotinator.Data.Models.PagedItems<T>` (`src/Quotinator.Data/Models/PagedItems.cs`) — `public record
  PagedItems<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`, not sealed, with a
  computed `TotalPages`. `IListableRepository<T>.GetPageAsync` returns `Task<PagedItems<T>>` directly.
- The issue's "shared pagination-clamp helper" does not exist as a clamp. Confirmed
  `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (`src/Quotinator.Api/Endpoints/Shared/
  PaginationParsing.cs`) reject out-of-range `pageSize`/`page` with 422 rather than clamping.
- `NotFoundResult.OkOrNotFound<T>(T? entity, IApiLocalizer localizer, string notFoundMessageKey) where T
  : class` confirmed present (`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`).
- **DI registration re-verified directly, not assumed**: `src/Quotinator.Api/Program.cs:310` —
  `builder.Services.AddSingleton<IListableRepository<Character>>(sp => (IListableRepository<Character>)
  sp.GetRequiredService<IRestorableRepository<Character>>());`. Present exactly as claimed. No new DI
  registration is needed for Character's own repository.
- `RateLimitPolicies.Api` confirmed in `src/Quotinator.Constants/RateLimiting/RateLimitPolicies.cs`.
  `ApiTags.MasterData` confirmed in `ApiTags.cs`, and confirmed it already has an OpenAPI tag description
  registered in `Program.cs`'s `document.Tags` list (added by #196) — this issue needs no `Program.cs`
  tag-description change, only the two new endpoints and their `MapCharacterEndpoints()` registration.
- **`Character` entity fields re-verified directly against the current file** (entities can drift):
  `Name` (`string`), `ImportBatchId` (`Guid?`), `CompletenessStatus` (`SafeValue<CompletenessStatus?>`),
  `NoValueKnown` (`IReadOnlyList<string>`), plus `RecordBase`'s `Id`/`DateCreated`/`DateModified`/
  `DateDeleted`/`IsDeleted`. Matches the issue's assumed field list. Confirmed `CharacterSourceEntity`
  (`src/Quotinator.Engine/Entities/CharacterSourceEntity.cs`) is a pure junction row: `CharacterId`,
  `SourceId`, plus `RecordBase` — no content field of its own, consistent with ADR 011 §2.
- **`Sql.cs` (`Quotinator.Engine.Queries`) re-read in full.** `internal static class CharacterSources`
  (lines 229–238) has exactly `InsertIfNotExists` and `DeleteForCharacter` — confirmed no existing query
  returns Source ids for a Character. The closest precedent for the join direction is
  `Sql.Characters.SelectIdBySourceAndName` (Source → Character, opposite of what this issue needs).
- **New discrepancy found, not previously flagged (same gap #184 independently found for
  `api/v1/masterdata/sources`)**: `NumericParameterSchemaTransformer.NumericParamsByPath`
  (`src/Quotinator.Api/OpenApi/NumericParameterSchemaTransformer.cs`) has no entry for
  `api/v1/masterdata/characters`. Per CLAUDE.md's "Rules for adding new numeric query parameter", this
  issue must add it. The issue body never mentions this step — matching the exact class of gap #194
  found and #195/#184 each had to add explicitly. Added as its own step below.
- **New discrepancy found**: `ApiMessages.CharacterNotFound` does not exist yet, and no
  `ErrorCharacterNotFound` key exists in any of the three `i18ntext/UI.*.json` files. Both must be added
  in this issue, mirroring `ConversationNotFound`/`ErrorConversationNotFound` and #184's
  `SourceNotFound`/`ErrorSourceNotFound`.
- **`GetByIdAsync`'s case-insensitivity mechanism confirmed by reading the implementation, not assumed**:
  `SqliteRepository<T>.GetByIdAsync` (`src/Quotinator.Data/Repositories/SqliteRepository.cs:31-44`)
  builds `param = new { id = id.ToString("D").ToUpperInvariant() }` before querying
  `RepositorySql.SelectById(TableName)` (`WHERE Id = @id AND IsDeleted = 0` — plain, case-sensitive SQL).
  Because the C# call site always uppercases before binding, and every stored id is written uppercase via
  the same idiom (`GuidHandler.SetValue`, `src/Quotinator.Data/Helpers/GuidHandler.cs:26`), the match
  always succeeds regardless of the input string's original case — *provided* the caller parses the route
  string to an actual `Guid` first (`Guid.TryParse` is itself case-insensitive on the input). This differs
  from the `Sql.Sources`/`Sql.People` raw-string `UPPER(Id) = UPPER(@id)` pattern, which exists because
  those queries take an already-string id from an import JSON file, never a parsed `Guid` — no
  contradiction, just two different call shapes reaching the same case-insensitive outcome.
- **Response DTO decision**: `Character`'s `SafeValue<T>`-wrapped `CompletenessStatus` field has no
  `System.Text.Json` converter anywhere in the codebase — confirmed no `Converters.Add` for `SafeValue<T>`
  in `Program.cs`. Serializing the raw entity would leak `{"raw":..,"parsed":..}` onto the wire. Decision:
  a `CharacterResponse` DTO, flattened to plain JSON-friendly types, mirroring `QuoteResponse`'s existing
  flattening pattern and #184's `SourceResponse` precedent. `CompletenessStatus`
  (`src/Quotinator.Data/Entities/CompletenessStatus.cs:10`) already carries
  `[JsonConverter(typeof(JsonStringEnumConverter))]`, so the DTO property is typed as the enum directly
  (`CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete`) rather than flattened to a string by
  hand — same idiom #184 used for `SourceResponse.CompletenessStatus`.
- **`Id`/`SourceIds` string convention confirmed**: response DTOs in this codebase type ids as `string`,
  not `Guid` (`QuoteResponse.Id`, `ConversationResponse.Id`, #184's `SourceResponse.Id`/`SeriesId`), via
  `guid.ToString("D").ToUpperInvariant()`. `CharacterResponse.Id` and each entry in `SourceIds` follow the
  same idiom.
- **File placement confirmed**: no service layer sits between this endpoint and the repository (unlike
  `QuoteEndpoints`, which goes through `IQuoteService`), so `CharacterResponse` has no reason to live in
  `Quotinator.Core`. Placed at `src/Quotinator.Api/Models/CharacterResponse.cs`, namespace
  `Quotinator.Api.Models` (same folder #184 created for `SourceResponse.cs` — not a new folder, since
  #184 is the earlier-numbered sibling and creates it first).
- **No existing fake for `IListableRepository<Character>` or any `IListableRepository<T>`** in
  `tests/Quotinator.Api.Tests/Fakes/` — confirmed via directory listing (`CapturingLogger.cs`,
  `CaptureSink.cs`, `FakeQuoteImportService.cs`, `FakeImportActionService.cs`, `FakeQuoteService.cs`; #184
  will add `FakeSourceRepository.cs` but that is Source-typed, not reusable for `Character`).
  `IListableRepository<T> : IRepository<T>` — `IRepository<T>`'s full member list, re-read directly:
  `GetByIdAsync`, `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync`, plus
  `IListableRepository<T>.GetPageAsync`. A new `FakeCharacterRepository` must implement all six.

**Design decision — the join-query dependency (the crux of this issue, not boilerplate)**: introduce a
new interface `ICharacterSourceLinkReader` (single method for `GetById`, batch method for `GetAll`),
implemented against two new fixed-shape `Sql.CharacterSources` queries. Placed in
`src/Quotinator.Engine/Repositories/` (new folder — Engine has no `Repositories/` folder yet; its
existing folders are `Entities/`, `Database/`, `Services/`, `Queries/`, `Helpers/`, `Models/`), namespace
`Quotinator.Engine.Repositories`, mirroring `Quotinator.Data.Repositories`'s naming convention. This
belongs in `Quotinator.Engine`, not `Quotinator.Data`, because `Quotinator.Data` must stay domain-agnostic
(ADR 004) — `CharacterSources`/`Sources` are Quotinator-domain tables, and the query text itself must live
in `Quotinator.Engine.Queries.Sql` alongside every other Quotinator-domain query, per that file's own
`<remarks>`. Unlike `SystemAuditReader` (`Quotinator.Data.Repositories`), which inherits
`SqliteRepositoryBase<SystemAuditEntry>` purely to reach the protected `Factory` field, the new
`CharacterSourceLinkReader` takes `IDbConnectionFactory` directly in its constructor instead of
inheriting `SqliteRepositoryBase<CharacterSourceEntity>` — inheriting would resolve an unused `TableName`/
`ValidColumnNames` (reflection-derived from `[Table]`) for a class whose queries name two tables
(`CharacterSources` and `Sources`) inline and never use `SELECT *` against a single entity shape. A
plain constructor dependency is simpler and equally correct here.

- **New design element, added during cross-plan review (developer directive, 2026-07-18)**:
  `CharacterResponse.SourceIds` (bare `IReadOnlyList<string>`) is replaced with `CharacterResponse
  .Sources` (`IReadOnlyList<MasterDataReference>`) per CLAUDE.md's new "Masterdata reference shape"
  convention — this was in fact the concrete motivating case for that convention: the original design's
  bare-id list was pointed out as an avoidable regression, since `SelectSourceIdsForCharacter(s)`'s own
  join through `Sources` already fetches `Title` for free (it must join to `Sources` anyway, purely to
  filter a soft-deleted Source's id out of the result — see Step 1). `ICharacterSourceLinkReader`'s
  return types change from bare `Guid`/`IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>` to `(Guid Id,
  string Name)`/`IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>` tuples — never the
  `Quotinator.Api.Models.MasterDataReference` DTO directly, since `Quotinator.Engine` has no dependency
  on `Quotinator.Api`. `MasterDataReference` itself may already exist by the time this issue lands (#184
  and #187 also need it) — reuse the existing file rather than redefining it; see Notes.
- **Soft-deleted visibility, already correct, now explicitly confirmed**: the existing join design (Step
  1) already filters `Sources.IsDeleted = 0` in the `ON` clause — a Character linked to a soft-deleted
  Source was already excluded from `sourceIds` before this redesign, for the unrelated reason that a
  dangling id would otherwise be meaningless to the caller. This redesign changes *what* is returned per
  matched row (`(Id, Title)` instead of a bare `Id`), not *which* rows match — no additional soft-delete
  filtering is needed, and this is now also the concrete first example of CLAUDE.md's "Soft-deleted rows
  are invisible by default" convention being satisfied by pre-existing code rather than newly added for it.

Conventions and constants from #196 are being consumed here, not re-decided.

---

## Steps

### 1. Two new `Sql.CharacterSources` queries

**Status:** Not started.

Add to `internal static class CharacterSources` in `src/Quotinator.Engine/Queries/Sql.cs`, alongside the
existing `InsertIfNotExists`/`DeleteForCharacter`. Both are fixed shapes (no dynamic WHERE-clause
variants), so plain `const string` — no `SqlQueryGuardTests.AssembledQueryCases` coverage is needed (that
guard exists for factory methods with caller-supplied clause variants; see `Sql.cs`'s own `<remarks>`).
Both join to `Sources` (not just filter `CharacterSources.IsDeleted`) so a soft-deleted Source's id never
leaks into a `sourceIds` response — same reasoning `Sql.Sources.CountActiveReferences` and the
`SelectBase`/`CountForRandomBase` quote queries already apply when joining through `Sources`:

```csharp
/// <summary>Active (SourceId, SourceTitle) pairs linked to one Character — #185's GetById join. Selects
/// Title alongside Id since the join through Sources (needed to exclude a soft-deleted Source) already
/// has it for free, and the response must surface a display name per CLAUDE.md's "Masterdata reference
/// shape" convention.</summary>
internal const string SelectSourceReferencesForCharacter =
    "SELECT s.Id, s.Title FROM CharacterSources cs " +
    "JOIN Sources s ON s.Id = cs.SourceId AND s.IsDeleted = 0 " +
    "WHERE cs.CharacterId = @characterId AND cs.IsDeleted = 0;";

/// <summary>
/// Active (CharacterId, SourceId, SourceTitle) rows for a batch of Characters in a single round-trip —
/// #185's list join. Dapper expands @characterIds from any IEnumerable&lt;Guid&gt; automatically (same
/// pattern as RepositorySql.SelectByIds), avoiding one query per row across a page.
/// </summary>
internal const string SelectSourceReferencesForCharacters =
    "SELECT cs.CharacterId, s.Id AS SourceId, s.Title AS SourceTitle FROM CharacterSources cs " +
    "JOIN Sources s ON s.Id = cs.SourceId AND s.IsDeleted = 0 " +
    "WHERE cs.CharacterId IN @characterIds AND cs.IsDeleted = 0;";
```

### 2. `ICharacterSourceLinkReader` + `CharacterSourceLinkReader`

**Status:** Not started.

New files `src/Quotinator.Engine/Repositories/ICharacterSourceLinkReader.cs` and
`CharacterSourceLinkReader.cs`, namespace `Quotinator.Engine.Repositories` (see Background for the
placement rationale):

```csharp
namespace Quotinator.Engine.Repositories;

/// <summary>Reads the CharacterSources join for masterdata read endpoints (#185) — never writes. Returns
/// plain (Id, Name) tuples, not Quotinator.Api.Models.MasterDataReference directly — Quotinator.Engine has
/// no dependency on Quotinator.Api; the Api-layer endpoint maps the tuple to the DTO.</summary>
public interface ICharacterSourceLinkReader
{
    /// <summary>Active (Id, Title) references for every Source linked to one Character.</summary>
    Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId);

    /// <summary>
    /// Active (Id, Title) Source references for each of the given Characters, in one round-trip. A
    /// Character with no links is absent from the result rather than mapped to an empty list — callers
    /// default missing keys to an empty array.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds);
}
```

```csharp
namespace Quotinator.Engine.Repositories;

/// <inheritdoc cref="ICharacterSourceLinkReader"/>
public sealed class CharacterSourceLinkReader : ICharacterSourceLinkReader
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Initialises the reader with the connection factory.</summary>
    public CharacterSourceLinkReader(IDbConnectionFactory factory) => _factory = factory;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<SourceRow>(Sql.CharacterSources.SelectSourceReferencesForCharacter, new { characterId });
        return rows.Select(r => (r.Id, r.Title)).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds)
    {
        if (characterIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>();

        using var conn = _factory.CreateConnection();
        conn.Open();
        var rows = await conn.QueryAsync<LinkRow>(Sql.CharacterSources.SelectSourceReferencesForCharacters, new { characterIds });
        return rows.GroupBy(r => r.CharacterId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<(Guid Id, string Name)>)g.Select(r => (r.SourceId, r.SourceTitle)).ToList());
    }

    private sealed record SourceRow(Guid Id, string Title);

    private sealed record LinkRow(Guid CharacterId, Guid SourceId, string SourceTitle);
}
```

`Sql.CharacterSources.*` is `internal` to `Quotinator.Engine` — this reader lives in the same assembly, so
no visibility change is needed (same as every other `Sql.cs` consumer in Engine).

Register in `Program.cs` alongside the other repository registrations (near `Program.cs:310`):
```csharp
builder.Services.AddSingleton<ICharacterSourceLinkReader, CharacterSourceLinkReader>();
```

### 3. `CharacterResponse` DTO

**Status:** Not started.

New file `src/Quotinator.Api/Models/CharacterResponse.cs`:

```csharp
using Quotinator.Data.Entities;

namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Character — a fictional character who delivers a quote, possibly across multiple Sources (#179).</summary>
public sealed class CharacterResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The character's name in the source's original language.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }

    /// <summary>Every Source this character appears in (#179's many-to-many), as minimal read-only
    /// references (<c>{id, name}</c> only — see <see cref="MasterDataReference"/>). Empty, never null,
    /// when the character has no linked Source. A Source that has been soft-deleted is never included
    /// (per CLAUDE.md's "Soft-deleted rows are invisible by default" convention).</summary>
    public IReadOnlyList<MasterDataReference> Sources { get; init; } = [];
}
```

A private mapping method in `CharacterEndpoints` performs the flattening:

```csharp
private static CharacterResponse ToResponse(Character character, IReadOnlyList<(Guid Id, string Name)> sources) => new()
{
    Id                 = character.Id.ToString("D").ToUpperInvariant(),
    Name               = character.Name,
    CompletenessStatus = character.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    Sources            = sources.Select(s => new MasterDataReference(s.Id.ToString("D").ToUpperInvariant(), s.Name)).ToList(),
};
```

### 4. `ApiMessages.CharacterNotFound` + i18n lockstep

**Status:** Not started.

Add to `src/Quotinator.Constants/Api/ApiMessages.cs`:
```csharp
public const string CharacterNotFound = "ErrorCharacterNotFound";
```

Add `"ErrorCharacterNotFound"` to all three `i18ntext/UI.*.json` files in the same commit (mirroring
`ErrorConversationNotFound`'s three entries and #184's `ErrorSourceNotFound`):
- `UI.en-GB.json`: `"No character with the requested ID was found."`
- `UI.nl.json`: `"Er is geen personage gevonden met het opgegeven ID."`
- `UI.de.json`: `"Es wurde keine Figur mit der angegebenen ID gefunden."`

`TranslationCompletenessTests` must stay green.

### 5. `CharacterEndpoints.cs`

**Status:** Not started.

New file `src/Quotinator.Api/Endpoints/CharacterEndpoints.cs`, static class `CharacterEndpoints`, extension
method `MapCharacterEndpoints(this WebApplication app)`, mirroring `ConversationEndpoints.cs`'s structure
and #184's `SourceEndpoints.cs` (repository directly to `PagedItems<T>`, no service layer in between):

```csharp
internal static class CharacterEndpoints
{
    private sealed class Log { }

    internal static void MapCharacterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/characters")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllCharacters")
             .WithSummary("List characters")
             .WithDescription(
                 "Returns a paginated list of characters, each with the Sources it appears in (#179) as " +
                 "minimal {id, name} references. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName("GetCharacterById")
             .WithSummary("Character by ID")
             .WithDescription("Returns a single character with the Sources it appears in. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Character> repository,
        ICharacterSourceLinkReader linkReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllCharacters] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var characterIds     = result.Items.Select(c => c.Id).ToList();
        var linksByCharacter = await linkReader.GetSourceReferencesForManyAsync(characterIds);

        var items = result.Items
            .Select(c => ToResponse(c, linksByCharacter.TryGetValue(c.Id, out var sources) ? sources : []))
            .ToList();

        var response = new PagedItems<CharacterResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the character.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Character> repository,
        ICharacterSourceLinkReader linkReader)
    {
        logger.LogInformation("[Api - GetCharacterById] id={Id}", id);

        if (!Guid.TryParse(id, out var characterId))
            return NotFoundResult.OkOrNotFound<CharacterResponse>(null, localizer, ApiMessages.CharacterNotFound);

        var character = await repository.GetByIdAsync(characterId);
        if (character is null)
            return NotFoundResult.OkOrNotFound<CharacterResponse>(null, localizer, ApiMessages.CharacterNotFound);

        var sources = await linkReader.GetSourceReferencesAsync(characterId);
        return NotFoundResult.OkOrNotFound(ToResponse(character, sources), localizer, ApiMessages.CharacterNotFound);
    }

    private static CharacterResponse ToResponse(Character character, IReadOnlyList<(Guid Id, string Name)> sources) => new() { /* see Step 3 */ };
}
```

A malformed `{id}` route segment (not a well-formed GUID) resolves to the same 404 path as "not found",
matching `NotFoundResult`'s existing 404-only contract for this endpoint shape — not a 422. This
deliberately does **not** reuse `EntityFilterParsing`'s "malformed id → 422" behaviour: that convention
governs an *optional query-parameter filter*, where 422 signals bad input on a parameter the caller chose
to supply. `{id}` here is the primary route resource identifier, and `QuoteEndpoints.GetById`/
`ConversationEndpoints.GetById` both already established the precedent that a `GetById` route never
validates id format and never returns 422 for it — it simply doesn't match, so 404. #184's
`SourceEndpoints.GetById` follows the same precedent. Kept consistent here rather than introducing a third
behaviour for id-shaped route parameters.

Register the call in `Program.cs` alongside the other `Map*Endpoints()` calls (`Program.cs:539-542`):
```csharp
app.MapCharacterEndpoints();
```

### 6. Register the OpenAPI numeric-param transformer path

**Status:** Not started.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath` (found missing during planning — not in
the original issue text, same class of gap #184 independently found for `api/v1/masterdata/sources`):
```csharp
["api/v1/masterdata/characters"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```
Without this, `page`/`pageSize` publish as bare `string` in the OpenAPI spec instead of `integer|null`.

### 7. `FakeCharacterRepository` + a link-reader test double

**Status:** Not started.

New file `tests/Quotinator.Api.Tests/Fakes/FakeCharacterRepository.cs`, implementing
`IListableRepository<Character>`. In-memory `List<Character>` backing store, seeded via constructor
parameter (`IEnumerable<Character>? seed = null`):

- `GetPageAsync(page, pageSize, orderBy, unitOfWork)` — orders by `DateCreated` ascending (the documented
  default), paginates in memory, returns a real `PagedItems<Character>` with the effective `pageSize`
  (0 → all rows as one page), mirroring the real repository's `pageSize = 0` contract so the fake cannot
  silently diverge from #195's behaviour.
- `GetByIdAsync(id, unitOfWork)` — straight `Guid` equality match (already case-insensitive once parsed;
  see Background), returns `null` if not found or `IsDeleted`.
- `InsertAsync`, `InsertManyAsync`, `UpdateAsync`, `SoftDeleteAsync` — implemented minimally against the
  in-memory list (not `throw new NotImplementedException()`) so the fake is a genuine substitutable
  double, matching #184's `FakeSourceRepository` precedent.

The `ICharacterSourceLinkReader` test double is a small inline stub class inside
`CharacterEndpointsTests.cs` itself (`StubCharacterSourceLinkReader`), not a `Fakes/` file — mirroring the
existing inline `StubAuditReader` in `AdminAuditEndpointTests.cs`. Unlike the repository fake (which needs
to be a reusable, correctly-paginating in-memory store shared across many test methods), the link reader's
test double only needs to echo back a caller-supplied
`Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>` — a single constructor parameter is enough, so a
full `Fakes/` file would be unwarranted ceremony for this one.

### 8. Endpoint tests

**Status:** Not started.

New file `tests/Quotinator.Api.Tests/Endpoints/CharacterEndpointsTests.cs`. `CreateFactory` follows
`AdminAuditEndpointTests.cs`'s pattern (register `FakeQuoteService`, `NoOpDatabaseInitializer`, a
`FakeCharacterRepository` as `IListableRepository<Character>`, and a `StubCharacterSourceLinkReader` as
`ICharacterSourceLinkReader` — real or caller-supplied for each).

Required tests (from the issue) plus the full eight-case pagination matrix CLAUDE.md's "Standard
pagination contract" mandates for every new paginated GET endpoint, plus join-specific and id-parsing
cases this issue's design introduces beyond what the issue body itself lists:

- `GetAllCharacters_ReturnsPaginatedResults` (issue)
- `GetAllCharacters_IncludesSourceReferencesForEachCharacter` (issue)
- `GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences` (issue)
- `GetCharacterById_MultipleSourceLinks_ReturnsAllOfThemWithNames` (issue)
- `GetCharacterById_UnknownId_Returns404` (issue)
- `GetCharacterById_MalformedId_Returns404NotBadRequest` — mirrors #184's equivalent; a non-Guid `{id}`
  segment must not throw or bare-400
- `GetCharacterById_LowercaseId_MatchesCaseInsensitively` — seeds a Character with an explicit
  uppercase-stored `Id`, requests it via a deliberately-lowercased id string, asserts 200 with the
  matching `Id` in the response
- `GetAllCharacters_CharacterWithNoSourceLinks_ReturnsEmptySourcesArray` — proves the "missing key
  defaults to empty array, not null/omitted" contract from Step 2's `GetSourceReferencesForManyAsync` doc
  comment
- `GetAllCharacters_PageZero_Returns422`
- `GetAllCharacters_PageMalformed_Returns422`
- `GetAllCharacters_PageSizeMalformed_Returns422`
- `GetAllCharacters_PageSizeNegative_Returns422`
- `GetAllCharacters_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllCharacters_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllCharacters_PageSizeOmitted_DefaultsTo20`
- `GetAllCharacters_PageBeyondLast_Returns422DistinctDetail`

`GetAllCharacters_IncludesSourceReferencesForEachCharacter` and `GetCharacterById_MultipleSourceLinks_
ReturnsAllOfThemWithNames` are the two tests that actually exercise the join batching — seed the stub link
reader with a multi-entry dictionary of `(Id, Name)` tuples and assert every reference round-trips into
the response's `sources` array as `{id, name}`, proving the mapping in `GetAll`/`GetById` reads the
reader's result correctly (including the name, not just the id) rather than just proving the reader itself
returns data (which would be a weaker, reader-only test).

**Response shape assertion** (proving Step 3's `CharacterResponse` design actually prevents the
`SafeValue<T>` leak, matching #184/#186's identical assertion for their own `SafeValue<T>` fields):
`GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences` must additionally assert
`completenessStatus` serializes as a plain JSON string value (e.g. `"Complete"`), never
`{"raw":...,"parsed":...}`.

**Soft-deleted Source exclusion** — `GetCharacterById_SourceSoftDeleted_ExcludedFromSources`: seeds a
Character whose stub link reader omits an otherwise-known Source (modelling the join's `Sources.IsDeleted
= 0` filter excluding it), asserts the response's `sources` array does not contain it — proving CLAUDE.md's
"Soft-deleted rows are invisible by default" convention holds for this join, consistent with #184's
equivalent test for its own `ISourceSeriesReferenceReader`.

**Live tag/rate-limit proof** (added consistently across all five masterdata issues during cross-plan
review, mirroring #187's `SeriesEndpoints_OnLiveSpec_TaggedMasterData`): `CharacterEndpoints_
OnLiveSpec_TaggedMasterData` extends `OpenApiSpecEndpointTests` (or a small dedicated assertion against
`/openapi/v1.json`'s `tags` array for both operations), proving requirement 4's tag/rate-limit wiring
live rather than by code inspection only.

### 9. Documentation

**Status:** Not started.

Update `README.md`'s and `addon/DOCS.md`'s REST API Endpoints tables — add rows for
`GET /api/v1/masterdata/characters` and `GET /api/v1/masterdata/characters/{id}`, following the existing
table row style (see `README.md:143-146` for the pattern used by the neighbouring `/quotes` and
`/conversations` rows; `addon/DOCS.md:27-30` for its two-column variant).

### 10. Solution file

**Status:** Not started.

Add the new files (`src/Quotinator.Engine/Repositories/ICharacterSourceLinkReader.cs`,
`CharacterSourceLinkReader.cs`, `src/Quotinator.Api/Models/CharacterResponse.cs`,
`src/Quotinator.Api/Models/MasterDataReference.cs` — if not already added by whichever of #184/#185/#187
lands first, `src/Quotinator.Api/Endpoints/CharacterEndpoints.cs`, `tests/Quotinator.Api.Tests/Fakes/
FakeCharacterRepository.cs`, `tests/Quotinator.Api.Tests/Endpoints/CharacterEndpointsTests.cs`) to
`Quotinator.slnx` if not automatically picked up by the existing project globs — verify by opening the
solution. `Quotinator.Engine/Repositories/` is a new folder inside an existing project (source files under
a project folder are visible through the project node automatically per CLAUDE.md's Visual Studio
Solution section — no new `<Folder>` entry needed unless the folder sits outside any project).

### 11. Verify

**Status:** Not started.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full suite green, 0 warnings, 0 errors. Confirm all listed expected tests started
red before implementation.

T2 (Docker): `docker build` + `docker run`, then:
```bash
curl -s "http://localhost:8080/api/v1/masterdata/characters"
curl -s "http://localhost:8080/api/v1/masterdata/characters?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters?pageSize=999"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters/00000000-0000-0000-0000-000000000000"
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"masterdata/characters[^"]*"'
```
Confirm: default list returns 200 with `items`/`page`/`pageSize`/`totalCount`/`totalPages`, and each item
carries a `sources` array of `{id, name}`; `pageSize=0` returns every Character as one page; `pageSize=999`
returns 422; an unknown id returns 404 with `ErrorCharacterNotFound`'s message; the OpenAPI spec publishes
`page`/`pageSize` as `integer|null` on `api/v1/masterdata/characters`. Also fetch a real Character known
to have more than one Source link via `Quotinator.Tools.DbInspector` (`SELECT c.Id, c.Name, COUNT(*) FROM
Characters c JOIN CharacterSources cs ON cs.CharacterId = c.Id WHERE c.IsDeleted = 0 AND cs.IsDeleted = 0
GROUP BY c.Id HAVING COUNT(*) > 1 LIMIT 1;` — the bundled Gandalf rows from #169's research are a likely
candidate) and confirm `GET /api/v1/masterdata/characters/{that id, lowercased}` returns 200 with all of
that Character's Sources in `sources`, each carrying both `id` and a non-empty `name` — live proof of the
join actually resolving `Title` (not just id), and of case-insensitive matching, not just the unit tests'
stubbed data.

This project always runs T2 regardless of a documented trigger — this issue's own change to `Program.cs`
(the new `MapCharacterEndpoints()` call and `ICharacterSourceLinkReader` registration) also independently
satisfies `docs/release-verification.md`'s "touches Program.cs startup" trigger.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `GET /api/v1/masterdata/characters` returns a paginated list of Characters | Unit test | `CharacterEndpointsTests.GetAllCharacters_ReturnsPaginatedResults` |
| 2 | ❌ | Each list item includes the Sources it appears in as `{id, name}` references, batched in one join query per page | Unit test | `CharacterEndpointsTests.GetAllCharacters_IncludesSourceReferencesForEachCharacter` |
| 3 | ❌ | A Character with no Source links returns an empty `sources` array, not null/omitted | Unit test | `CharacterEndpointsTests.GetAllCharacters_CharacterWithNoSourceLinks_ReturnsEmptySourcesArray` |
| 4 | ❌ | `GET /api/v1/masterdata/characters/{id}` returns the matching Character with its Sources | Unit test | `CharacterEndpointsTests.GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences` |
| 5 | ❌ | A Character linked to multiple Sources returns all of them with names | Unit test | `CharacterEndpointsTests.GetCharacterById_MultipleSourceLinks_ReturnsAllOfThemWithNames` |
| 6 | ❌ | An unknown id returns 404 | Unit test | `CharacterEndpointsTests.GetCharacterById_UnknownId_Returns404` |
| 7 | ❌ | A malformed `{id}` route segment returns 404, not an unhandled exception or bare 400 | Unit test | `CharacterEndpointsTests.GetCharacterById_MalformedId_Returns404NotBadRequest` |
| 8 | ❌ | A lowercase id matches an uppercase-stored id | Unit test | `CharacterEndpointsTests.GetCharacterById_LowercaseId_MatchesCaseInsensitively` |
| 9 | ❌ | `page=0` returns 422 | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageZero_Returns422` |
| 10 | ❌ | Malformed `page`/`pageSize` returns 422 | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageMalformed_Returns422`, `_PageSizeMalformed_Returns422` |
| 11 | ❌ | Negative `pageSize` returns 422 | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageSizeNegative_Returns422` |
| 12 | ❌ | `pageSize > 500` returns 422, never clamped | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageSizeAbove500_Returns422NotSilentClamp` |
| 13 | ❌ | `pageSize = 0` returns every row as one page | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 14 | ❌ | `pageSize` omitted defaults to 20 | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageSizeOmitted_DefaultsTo20` |
| 15 | ❌ | A page beyond the last returns 422 with a distinct detail | Unit test | `CharacterEndpointsTests.GetAllCharacters_PageBeyondLast_Returns422DistinctDetail` |
| 16 | ❌ | `completenessStatus` serializes as a plain JSON value, never `{raw, parsed}` | Unit test | `CharacterEndpointsTests.GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences` (shape assertion) |
| 17 | ❌ | A Source excluded by the join's soft-delete filter never appears in `sources` | Unit test | `CharacterEndpointsTests.GetCharacterById_SourceSoftDeleted_ExcludedFromSources` |
| 18 | ❌ | `page`/`pageSize` publish as `integer` in the OpenAPI spec for `api/v1/masterdata/characters` | Unit test | `NumericParameterSchemaTransformerTests` (new cases) |
| 19 | ❌ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api`, proven live | Unit test | `CharacterEndpoints_OnLiveSpec_TaggedMasterData` |
| 20 | ❌ | `ApiMessages.CharacterNotFound` exists and all three locale files carry `ErrorCharacterNotFound` | Unit test | `TranslationCompletenessTests` |
| 21 | ❌ | `README.md`/`addon/DOCS.md` document both new endpoints | Doc review | Endpoint tables updated |
| 22 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 23 | ❌ | T1 — app starts in Visual Studio; both endpoints reachable | Live (T1) | Developer confirmed |
| 24 | ❌ | T2 — the live contract holds against the built image, including a real multi-Source Character with resolved names | Live (T2) | `docker build`/`docker run` matrix — see Step 11 |

---

## Notes

This issue is #196's filter-parameter convention's second opportunity to be *not* invoked (after #184),
deliberately, per requirement 5. If a future issue needs `?sourceId=` on this endpoint, it wires
`EntityFilterParsing.ResolveAsync` in at that time.

`Character`'s underlying repository (`SqliteRepository<Character>` via `IRestorableRepository<Character>`)
is unmodified by this issue — its `GetPageAsync`/`GetByIdAsync` SQL already went through #193's/#195's own
verification (including the `pageSize = 0` → `LIMIT -1` fix), so this issue only needs to prove the new
endpoint/DTO/mapping layer and the new `CharacterSources` join, not re-verify that underlying layer.

The batch join (`GetSourceReferencesForManyAsync`) is the one piece of this issue with no sibling
precedent anywhere else in the codebase — every existing "resolve a related id" query
(`Sql.Characters.SelectIdBySourceAndName`, `Sql.Sources.SelectExistingById`, etc.) operates on a single
row, never a caller-supplied batch of ids via `IN @ids`. `RepositorySql.SelectByIds` (`Quotinator.Data`)
is the only other place in the codebase using the `IN @ids`-expansion pattern, and it operates generically
over `T`, not a join. #184 independently introduces the same single-id/batch shape for its own
`ISourceSeriesReferenceReader` — both are one-off, entity-specific readers rather than a shared
generalised abstraction; if a third masterdata issue needs the same "batch of ids → grouped related
references" shape again, consider generalising then, not speculatively now.

`MasterDataReference` (`src/Quotinator.Api/Models/MasterDataReference.cs`) is a shared type, not owned by
this issue specifically — #184 and #187 also need it. Whichever of the three lands first creates the
file; the other two reuse it. If #184 lands first (it is the lower-numbered, and its own plan doc already
anticipates this), this issue's own Step 3 does not recreate it.

---

## Corrected issue text (for a future `gh issue edit`)

```
## Background

Depends on #183 (shared list-endpoint infrastructure — generic `IListableRepository<T>`,
`Quotinator.Data.Models.PagedItems<T>`, pagination/not-found helpers, filter convention,
`/api/v1/masterdata/` routing convention).

`Character` (`src/Quotinator.Engine/Entities/Character.cs`) has no read endpoint today. Since #179, a
Character links to its Source(s) via the `CharacterSources` join table (`CharacterSourceEntity`), not a
direct FK — the response shape for this issue must expose that many-to-many relationship rather than a
single `SourceId`, or the endpoint would misrepresent #179's own schema change (see ADR 011).

## What needs to be done

1. `GET /api/v1/masterdata/characters` — paginated list, using #183's `IListableRepository<Character>` +
   `Quotinator.Data.Models.PagedItems<T>` + the shared `PaginationParsing` helper (rejects out-of-range
   `page`/`pageSize` with 422 — it does not clamp). Response items include `Id`, `Name`,
   `CompletenessStatus`, and a `sources` array of minimal `MasterDataReference` (`{id, name}`) records
   populated via a join against `CharacterSources` — never a bare `sourceIds` array of ids, per CLAUDE.md's
   "Masterdata reference shape" convention — via a new `CharacterResponse` DTO (the raw `Character`
   entity's `SafeValue<T>`-wrapped fields cannot be serialized directly).
2. `GET /api/v1/masterdata/characters/{id}` — single Character by id, same join-array shape, using #183's
   shared `NotFoundResult.OkOrNotFound` helper. `{id}` matches case-insensitively (per this project's
   existing GUID parameter binding rule).
3. A new `ICharacterSourceLinkReader` (single-id and batch-for-a-page forms) backed by two new
   `Sql.CharacterSources` queries joined through to `Sources`, to fetch each linked Source's `(Id, Title)`
   for one or many Character ids in a single round-trip — avoid N+1 (one query per page, not one per row).
   A Source excluded by the join's own `Sources.IsDeleted = 0` filter is never surfaced (per CLAUDE.md's
   "Soft-deleted rows are invisible by default" convention).
4. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes.
5. Register `api/v1/masterdata/characters`'s `page`/`pageSize` parameters in
   `NumericParameterSchemaTransformer.NumericParamsByPath` (per #194's registration requirement — easy to
   miss, as #195 and #184 both found for their own endpoints).
6. Add `ApiMessages.CharacterNotFound` (`"ErrorCharacterNotFound"`) with lockstep translations in all
   three `i18ntext/UI.*.json` files.
7. No entity-specific filters yet (e.g. `?sourceId=`) — deferred per #196's filter convention.
8. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| New: `Quotinator.Api.Tests` | `GetAllCharacters_ReturnsPaginatedResults` | ❌ |
| New: `Quotinator.Api.Tests` | `GetAllCharacters_IncludesSourceReferencesForEachCharacter` | ❌ |
| New: `Quotinator.Api.Tests` | `GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences` | ❌ |
| New: `Quotinator.Api.Tests` | `GetCharacterById_MultipleSourceLinks_ReturnsAllOfThemWithNames` | ❌ |
| New: `Quotinator.Api.Tests` | `GetCharacterById_SourceSoftDeleted_ExcludedFromSources` | ❌ |
| New: `Quotinator.Api.Tests` | `GetCharacterById_UnknownId_Returns404` | ❌ |

Plus the full eight-case pagination matrix CLAUDE.md's "Standard pagination contract" mandates for every
new paginated GET endpoint (page=0, malformed page/pageSize, negative pageSize, pageSize>500, pageSize=0,
pageSize omitted, page beyond last), plus a malformed-`{id}`-route-segment case and a case-insensitive-id
case mirroring #184's own `SourceEndpoints` tests.

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
