# #186 — Masterdata: GET /api/v1/masterdata/people list + get-by-id

**Status:** Planning
**GitHub issue:** #186
**Tiers required:** T1, T2
**Depends on:** #193, #195, #196

---

## Spec requirements (corrected during planning review 2026-07-18)

1. `GET /api/v1/masterdata/people` — paginated list, using `IListableRepository<Person>` (#193, already
   DI-registered) + `Quotinator.Data.Models.PagedItems<T>` (#195 — **not** `PageResponse<T>`, which does
   not exist) + the shared `PaginationParsing.TryParse`/`ValidatePageBeyondLast` helper (#195 — this
   **rejects** an out-of-range `pageSize` with 422; it does not clamp). Response items are a new
   `PersonResponse` DTO with `Id`, `Name`, `DateOfBirth`, `DateOfDeath`, `CompletenessStatus` — flattened
   from `Person`'s `SafeValue<T>`-wrapped fields, never the raw entity (see Background).
2. `GET /api/v1/masterdata/people/{id}` — single Person by id, using the shared `NotFoundResult
   .OkOrNotFound` helper (#195). `{id}` matches case-insensitively (see Background for why no extra code
   is needed for this beyond a plain `Guid.TryParse`).
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, route prefix
   `/api/v1/masterdata/people` (#196's masterdata routing convention), and `[Description]` attributes.
4. No entity-specific filters yet — deferred per #196's entity-scoped filter-parameter convention (no
   consuming endpoint exists yet to justify building `EntityFilterParsing.ResolveAsync` wiring here).
5. Update `README.md` and `addon/DOCS.md`'s endpoint tables.
6. **Not in the original issue text, found during planning verification:** `api/v1/masterdata/people` must
   be registered in `NumericParameterSchemaTransformer.NumericParamsByPath` (`page`/`pageSize`) — without
   it, the published OpenAPI type regresses from `integer|string` to bare `string`, the exact gap #194
   exists to prevent and #195 already fixed for the other three paginated endpoints.
7. **Not in the original issue text, found during planning verification:** CLAUDE.md's "Standard
   pagination contract" section mandates a full eight-case test matrix for every new paginated GET
   endpoint. The issue's own "Expected tests" table lists only 4 tests total and does not cover this
   matrix — expanded in this plan's Steps/Verification checklist.

---

## Background — why this issue exists

Sub-issue of #183 (the parent tracking issue, itself split into #193–#196 — carries no implementation of
its own). Per `overview.md`'s dependency map, #186 concretely depends on #193 (repository capability),
#195 (pagination contract + helpers), and #196 (masterdata routing/tag convention) — all three "Waiting
for release". The issue's own text said "Depends on #183"; corrected here since #183 itself ships nothing.

`Person` (`src/Quotinator.Engine/Entities/Person.cs`) has no read endpoint today —
`IRestorableRepository<Person>` exists but is used only by #59's batch-undo reversal path.

**Verified before starting** (per this project's standing rule — every issue in this milestone's
`184`–`196` range has had at least one factual error caught this way):

- `Person` entity fields confirmed current: `Name` (`string`), `DateOfBirth`/`DateOfDeath`
  (`SafeValue<DateTime?>`), `ImportBatchId` (`Guid?`), `CompletenessStatus`
  (`SafeValue<CompletenessStatus?>`), `NoValueKnown` (`IReadOnlyList<string>`), plus `RecordBase`'s
  `Id`/`DateCreated`/`DateModified`/`DateDeleted`/`IsDeleted`. Matches the issue's own field list for the
  response (`Id`, `Name`, `DateOfBirth`, `DateOfDeath`, `CompletenessStatus`).
- DI registration confirmed exactly as described: `Program.cs:311` —
  `builder.Services.AddSingleton<IListableRepository<Person>>(sp => (IListableRepository<Person>)
  sp.GetRequiredService<IRestorableRepository<Person>>());`. No new DI registration needed for the
  repository itself.
- `ApiTags.MasterData` (`ApiTags.cs:11`) and `RateLimitPolicies.Api` (`RateLimitPolicies.cs:6`) both exist
  exactly as the issue states.
- `PagedItems<T>` confirmed as `public record PagedItems<T>(IReadOnlyList<T> Items, int Page, int
  PageSize, int TotalCount)` (not sealed, computed `TotalPages`) in `src/Quotinator.Data/Models
  /PagedItems.cs`. `IListableRepository<T>.GetPageAsync` returns `Task<PagedItems<T>>` directly — the
  issue's `PageResponse<T>` does not exist anywhere in the codebase.
- `PaginationParsing.TryParse`/`ValidatePageBeyondLast` and `NotFoundResult.OkOrNotFound<T>` both confirmed
  to exist with the signatures documented in CLAUDE.md's "Standard pagination contract" section.

**New discrepancies found beyond the corrections supplied for this planning pass:**

1. **`NumericParameterSchemaTransformer.NumericParamsByPath` registration is a real, un-flagged
   requirement.** Neither the original issue text nor the corrections supplied before this planning pass
   mention it. CLAUDE.md's "Numeric query parameter binding pattern" section states the rule generally
   ("Add both the endpoint path and the parameter name... to `NumericParameterSchemaTransformer
   .NumericParamsByPath`") and #194/#195 already had to apply it to `api/v1/quotes`, `api/v1/admin/audit`,
   and `api/v1/import/actions`. `api/v1/masterdata/people` is a fourth paginated path and needs the same
   treatment — added as Step 4 below, with both a `NumericParameterSchemaTransformerTests` addition and a
   new `[DataRow]` on `OpenApiSpecEndpointTests.PageParam_OnLiveSpec_PublishesIntegerType`.
2. **CLAUDE.md's eight-case pagination test matrix is not covered by the issue's own "Expected tests"
   table.** The issue lists 4 tests total (`GetAllPeople_ReturnsPaginatedResults`,
   `GetPersonById_ExistingId_ReturnsPerson`, `GetPersonById_UnknownId_Returns404`,
   `GetPersonById_LowercaseId_MatchesCaseInsensitively`) — none of which individually cover `page=0`,
   malformed `page`/`pageSize`, negative `pageSize`, `pageSize` above 500, `pageSize=0` effective-size
   reporting, or page-beyond-last. This is the same gap #195's own Notes section warns new paginated
   endpoints not to repeat ("Coverage of these eight cases was missing piecemeal across `/quotes`,
   `/admin/audit`, and `/import/actions` themselves and only closed after the fact"). Expanded in Step 7
   below; the corrected issue text reflects this too.
3. **`GetPersonById_LowercaseId_MatchesCaseInsensitively` needs no bespoke case-insensitive-matching code
   in this endpoint.** Traced the full path: the route `{id}` is parsed via `Guid.TryParse` (Guid parsing
   is inherently case-insensitive — a `Guid` struct carries no casing). `SqliteRepository<T>.GetByIdAsync`
   force-uppercases its parameter (`id.ToString("D").ToUpperInvariant()`) before querying
   `RepositorySql.SelectById`'s plain `WHERE Id = @id` (a case-sensitive SQLite TEXT comparison — no
   `UPPER()`/`COLLATE NOCASE`). This is only safe because every `Guid`-typed write goes through
   `GuidHandler` (`src/Quotinator.Data/Helpers/GuidHandler.cs`), which also force-uppercases on write —
   so a normally-inserted `Person` (via `InsertAsync`, `entity.Id` is `Guid`-typed) is always stored
   uppercase, and read/write agree. The test therefore passes once the endpoint correctly parses the route
   value into a `Guid` — no extra `UPPER()` SQL or manual case-folding needed here, unlike what CLAUDE.md's
   general "GUID/enum/id comparisons are case-insensitive by default" rule might suggest at first glance
   for a *new* id-matching query. Worth stating explicitly so a future reader doesn't reasonably assume
   this endpoint skipped that rule.
4. **Malformed-GUID `{id}` is not addressed by the issue text.** Decided: `Guid.TryParse` failure is
   treated identically to "no such row" → 404 via `NotFoundResult.OkOrNotFound`, not a 422. This mirrors
   the existing precedent at `SqliteQuoteService.GetById` (`src/Quotinator.Engine/Services
   /SqliteQuoteService.cs:50-62`), which passes the raw, unvalidated `id` string straight into a
   parameterised query — a malformed value simply matches no row and returns `null` → 404, never 422. A
   422 is reserved for filter/pagination *parameters* that constrain a list (#196's `EntityFilterParsing`
   convention), not for a primary resource-identifying path segment. No new test is added for this beyond
   the issue's own four, since it is not in the issue's "Expected tests" list and behaves identically to
   the already-covered `GetPersonById_UnknownId_Returns404` case from the caller's point of view.
5. **Out-of-scope finding, not fixed by this issue:** `AdminEndpoints.GetAuditLog` returns
   `PagedItems<SystemAuditEntry>` directly via `Results.Ok(result)`. `SystemAuditEntry : RecordBase`, and
   `RecordBase.DateCreated`/`DateModified`/`DateDeleted` are `SafeValue<DateTime?>` with no
   `System.Text.Json` converter registered anywhere in `Program.cs`. This means `/admin/audit`'s live JSON
   response leaks `{"raw":"...","parsed":"..."}` for those three fields today, untested
   (`AdminAuditEndpointTests.cs` has no assertion on `DateCreated`'s shape). This is exactly the class of
   leak the developer's `PersonResponse` DTO decision (see below) exists to avoid — cited here as
   confirming evidence for that decision, not as something #186 fixes (different entity, different
   endpoint, no test currently claims otherwise). Worth a separate issue if the developer wants it tracked.

**Response DTO decision** (given, not re-litigated): `Person`'s raw entity cannot be serialized directly —
its `SafeValue<T>`-wrapped fields would leak the `{raw, parsed}` shape shown above. Built `PersonResponse`
(`src/Quotinator.Api/Models/PersonResponse.cs`, namespace `Quotinator.Api.Models` — new folder, per
CLAUDE.md's file placement rule; no service layer exists for this endpoint so the DTO does not belong in
`Quotinator.Core`), mirroring `QuoteResponse`'s flattening style:

- `Id` — `string`, `person.Id.ToString("D").ToUpperInvariant()` (matches the codebase-wide uppercase Guid
  convention; `Guid.ToString("D")` defaults to lowercase).
- `Name` — `string`, direct passthrough.
- `DateOfBirth` / `DateOfDeath` — `string?`, taken from `SafeValue<DateTime?>.Raw` (not `.Parsed`), null
  when `Raw` is empty. `Person.DateOfBirth`'s own doc comment says "Imprecise ISO 8601... e.g. `"1955"` or
  `"1955-02-24"`" — parsing to `DateTime` would silently normalise `"1955"` to `1955-01-01`, losing the
  precision the field is documented to preserve. This mirrors how the canonical `Quote.date` field is
  exposed as a plain string on the wire, not a parsed `DateTime`.
- `CompletenessStatus` — `Quotinator.Data.Entities.CompletenessStatus?`, taken from
  `SafeValue<CompletenessStatus?>.Parsed` directly. No manual string conversion needed: the enum already
  carries `[JsonConverter(typeof(JsonStringEnumConverter))]` at its own declaration
  (`CompletenessStatus.cs:10`), so `System.Text.Json` serializes it as a string automatically — this is
  exactly the pattern CLAUDE.md's "JSON parsing policy" section prescribes for enum-valued fields.

---

## Steps

### 1. `ApiMessages.PersonNotFound` + i18n lockstep

**Status:** Not started.

Add `public const string PersonNotFound = "ErrorPersonNotFound";` to
`src/Quotinator.Constants/Api/ApiMessages.cs`, placed after `EntityFilterNoMatch` (the current last
error-message constant — matches the file's chronological-append ordering, not alphabetical).

Add `"ErrorPersonNotFound"` to all three i18n files, placed after `"InfoEntityFilterNoMatch"` and before
`"AdminEndpointsHeading"` (same chronological-append point in each file):

- `UI.en-GB.json`: `"No person with the requested ID was found."`
- `UI.nl.json`: `"Er is geen persoon gevonden met het opgegeven ID."`
- `UI.de.json`: `"Es wurde keine Person mit der angegebenen ID gefunden."`

Wording mirrors the existing `ErrorConversationNotFound`/`ErrorQuoteNotFound` phrasing pattern exactly (only
the noun changes) in all three languages.

### 2. `PersonResponse` DTO

**Status:** Not started.

New file `src/Quotinator.Api/Models/PersonResponse.cs`, namespace `Quotinator.Api.Models`:

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Person.</summary>
public sealed class PersonResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The person's full name.</summary>
    public required string Name { get; init; }

    /// <summary>Imprecise ISO 8601 birth date (e.g. "1955" or "1955-02-24"). Null when unknown.</summary>
    public string? DateOfBirth { get; init; }

    /// <summary>Imprecise ISO 8601 death date. Null when still living or unknown.</summary>
    public string? DateOfDeath { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed. Null when not yet assessed.</summary>
    public Quotinator.Data.Entities.CompletenessStatus? CompletenessStatus { get; init; }
}
```

No mapping logic on the DTO itself — kept a pure data shape, matching `QuoteResponse`. Mapping lives in
`PersonEndpoints.cs` (Step 3), matching how `SqliteQuoteService.ToResponse` keeps mapping logic in the
service/endpoint layer rather than on the DTO.

### 3. `PersonEndpoints.cs` + `Program.cs` wiring

**Status:** Not started.

New file `src/Quotinator.Api/Endpoints/PersonEndpoints.cs`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Api.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/people</c> endpoints.</summary>
internal static class PersonEndpoints
{
    private sealed class Log { }

    internal static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/people")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllPeople")
             .WithSummary("All people (paginated)")
             .WithDescription(
                 "Returns a paginated list of people (real individuals who said or wrote a quote). " +
                 "No entity-specific filters yet.");

        group.MapGet("/{id}", GetById)
             .WithName("GetPersonById")
             .WithSummary("Person by ID")
             .WithDescription(
                 "Returns a single person by UUID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null,
        IListableRepository<Person> repository = null!)
    {
        logger.LogInformation("[Api - GetAllPeople] page={Page:l} pageSize={PageSize:l}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);
        var response = new PagedItems<PersonResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);

        return PaginationParsing.ValidatePageBeyondLast(pageValue, response.TotalPages, localizer)
            ?? Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the person.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Person> repository)
    {
        logger.LogInformation("[Api - GetPersonById] id={Id:l}", id);

        PersonResponse? response = Guid.TryParse(id, out var personId)
            ? await repository.GetByIdAsync(personId) is { } person ? ToResponse(person) : null
            : null;

        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.PersonNotFound);
    }

    private static PersonResponse ToResponse(Person person) => new()
    {
        Id                 = person.Id.ToString("D").ToUpperInvariant(),
        Name               = person.Name,
        DateOfBirth        = string.IsNullOrEmpty(person.DateOfBirth.Raw) ? null : person.DateOfBirth.Raw,
        DateOfDeath        = string.IsNullOrEmpty(person.DateOfDeath.Raw) ? null : person.DateOfDeath.Raw,
        CompletenessStatus = person.CompletenessStatus.Parsed
    };
}
```

Notes on this shape:
- `GetAll`'s parameter order (`localizer`, `logger`, then the two `string?` params with defaults, then
  `repository = null!` last) matches `AdminEndpoints.GetAuditLog`'s exact precedent — C# requires optional
  parameters after required ones, and DI-resolved parameters that need to appear after an optional one get
  a `= null!` default purely to satisfy that ordering (always supplied by DI in practice).
- `[Description]`/`[DefaultValue]` wording for `page`/`pageSize` is copied verbatim from
  `AdminEndpoints.GetAuditLog` — the standard pagination contract's established wording, not a new phrasing.
- `GetById`'s malformed-id handling (Background finding 4) folds into the same `NotFoundResult
  .OkOrNotFound` call as a genuine 404, not a separate branch.

`Program.cs`: add `app.MapPersonEndpoints();` alongside the existing `Map*Endpoints()` calls
(`Program.cs:539-542`), immediately after `app.MapConversationEndpoints();`.

### 4. Register the OpenAPI transformer path

**Status:** Not started.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath`
(`src/Quotinator.Api/OpenApi/NumericParameterSchemaTransformer.cs`), following the exact shape of the
existing `api/v1/admin/audit`/`api/v1/import/actions` entries:

```csharp
["api/v1/masterdata/people"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```

Add a matching `#region page/pageSize patched on masterdata/people (#186)` block to
`NumericParameterSchemaTransformerTests.cs` (unit, synthetic `OpenApiOperation`) and two new `[DataRow]`
entries — `("/api/v1/masterdata/people", "page")`, `("/api/v1/masterdata/people", "pageSize")` — to
`OpenApiSpecEndpointTests.PageParam_OnLiveSpec_PublishesIntegerType` (live pipeline, per CLAUDE.md's note
that the transformer's own unit tests don't prove it's actually registered via `AddOpenApi`).

### 5. Register new logging prefixes

**Status:** Not started.

`docs/logging.md`'s "Defined prefixes" table requires every new subsystem prefix to be registered before
its log lines land in a PR. Add two rows, following the existing `[Api - GetById]`/`[Api - GetAll]`
pattern:

| Prefix | When to use |
|---|---|
| `[Api - GetAllPeople]` | Entry to GET /api/v1/masterdata/people |
| `[Api - GetPersonById]` | Entry to GET /api/v1/masterdata/people/{id} |

### 6. `FakePersonRepository` test double

**Status:** Not started.

New file `tests/Quotinator.Api.Tests/Fakes/FakePersonRepository.cs`, namespace
`Quotinator.Api.Tests.Fakes`. No existing fake implements `IListableRepository<T>` to copy from — designed
here from `IListableRepository<T>`/`IRepository<T>`'s full member list, following
`FakeImportActionService`'s canned-result-plus-last-call-tracking style:

```csharp
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IListableRepository{T}"/> over <see cref="Person"/> — returns a canned
/// page or a canned single entity, recording the arguments it was called with. Write methods are not
/// needed by any Person endpoint today and throw if exercised, so a test that accidentally reaches one
/// fails loudly instead of silently succeeding.
/// </summary>
internal sealed class FakePersonRepository : IListableRepository<Person>
{
    public PagedItems<Person>? ReturnPage { get; set; }
    public Person? ReturnById { get; set; }

    public int? LastPageRequested { get; private set; }
    public int? LastPageSizeRequested { get; private set; }
    public Guid? LastIdRequested { get; private set; }

    public Task<PagedItems<Person>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        LastPageRequested     = page;
        LastPageSizeRequested = pageSize;
        return Task.FromResult(ReturnPage ?? new PagedItems<Person>([], page, pageSize, 0));
    }

    public Task<Person?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        LastIdRequested = id;
        return Task.FromResult(ReturnById is not null && ReturnById.Id == id ? ReturnById : null);
    }

    public Task InsertAsync(Person entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task InsertManyAsync(IEnumerable<Person> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
        => throw new NotImplementedException();

    public Task UpdateAsync(Person entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();
}
```

### 7. `PersonEndpointsTests.cs`

**Status:** Not started.

New file `tests/Quotinator.Api.Tests/Endpoints/PersonEndpointsTests.cs`, namespace
`Quotinator.Api.Tests.Endpoints`. `CreateFactory()` follows `AdminAuditEndpointTests.cs`'s pattern
(`FakeQuoteService` + `NoOpDatabaseInitializer` boilerplate, plus `IListableRepository<Person>` overridden
with `FakePersonRepository`).

**The issue's own four named tests:**
- `GetAllPeople_ReturnsPaginatedResults`
- `GetPersonById_ExistingId_ReturnsPerson`
- `GetPersonById_UnknownId_Returns404`
- `GetPersonById_LowercaseId_MatchesCaseInsensitively`

**Added to satisfy CLAUDE.md's eight-case pagination matrix** (Background finding 2), named with the
`GetAll{Entity}_*` prefix #184/#185/#187/#188 all use (not a `People_`/`Audit_`-style bare-entity
prefix — corrected during cross-plan review to keep the five masterdata issues consistent with each
other, not just internally consistent):
- `GetAllPeople_PageZero_Returns422`
- `GetAllPeople_PageMalformed_Returns422`
- `GetAllPeople_PageSizeMalformed_Returns422`
- `GetAllPeople_PageSizeNegative_Returns422`
- `GetAllPeople_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllPeople_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllPeople_PageSizeOmitted_DefaultsTo20`
- `GetAllPeople_PageBeyondLast_Returns422DistinctDetail`

`GetAllPeople_PageSizeZero_ReturnsAllRowsAsOnePage` only needs fake-repository coverage here (unlike
#195's real-SQLite regression requirement) — `IListableRepository<Person>.GetPageAsync`'s own
`pageSize == 0` effective-size behaviour is already covered by `SqliteRepositoryTests.cs`'s 13
`GetPageAsync` tests from #193/#195; this issue's test only needs to prove the endpoint passes the
fake's returned `PagedItems<Person>.PageSize` straight through to the wire, not re-prove the repository
contract.

**Also added during cross-plan review**: `GetPersonById_MalformedId_Returns404NotBadRequest` —
Background finding 4 designs this behaviour (a non-`Guid` `{id}` resolves to the same 404 path as "no
such row") but the original draft of this plan never added a test proving it, unlike #184/#185's
equivalent `MalformedId` test for their own `GetById` endpoints. Added here for parity.

**Also added during cross-plan review**: `PersonEndpoints_OnLiveSpec_TaggedMasterData`, mirroring
#187's `SeriesEndpoints_OnLiveSpec_TaggedMasterData` — extends `OpenApiSpecEndpointTests` (or a small
dedicated assertion against `/openapi/v1.json`'s `tags` array for both operations), proving requirement
3's tag/rate-limit wiring live rather than by code inspection only.

**Response shape assertions** (not separately named in the issue, but required to prove Step 2/3's design
choices actually landed):
- `GetPersonById_ExistingId_ReturnsPerson` must assert `dateOfBirth`/`dateOfDeath` are plain JSON string
  values (not `{"raw":...,"parsed":...}`) and `completenessStatus` serializes as a bare string (e.g.
  `"Complete"`), proving the `SafeValue<T>` leak Background finding 5 describes does not recur here.
- A `GetPersonById_UnknownDates_ReturnsNullNotEmptyString` case (a `Person` with `SafeValue<DateTime?>
  .Empty` for both dates) asserting `dateOfBirth`/`dateOfDeath` are JSON `null`, not `""`.

### 8. Documentation

**Status:** Not started.

`README.md`'s REST API Endpoints table (around line 145, immediately after the `/api/v1/conversations/{id}`
row): add two rows for `GET /api/v1/masterdata/people` and `GET /api/v1/masterdata/people/{id}`, matching
the existing table's terse one-line description style.

`addon/DOCS.md`'s API Endpoints table (around line 30, same insertion point relative to the
`/api/v1/conversations/{id}` row): same two rows, matching that table's style.

This is the **first** masterdata route to ship — neither table currently has any `/masterdata/` entry.

### 9. Verify

**Status:** Not started.

`dotnet build --configuration Release` → must report 0 warnings, 0 errors.
`dotnet test --configuration Release --verbosity normal` → full suite green, 0 warnings, 0 errors,
including `TranslationCompletenessTests` (Step 1's i18n lockstep) and the new
`NumericParameterSchemaTransformerTests`/`OpenApiSpecEndpointTests` cases (Step 4).

T1: developer confirms clean Visual Studio startup and exercises
`GET /api/v1/masterdata/people`/`GET /api/v1/masterdata/people/{id}` manually.

T2 (this project always runs T2 regardless of a specific documented trigger — see #196's own Notes):
`docker build` + `docker run`, then exercise the full pagination matrix live (mirroring #195's T2 pass) —
malformed → 422, `pageSize=999` → 422, `pageSize=500` → 200, `pageSize=0` → every row as one page with
effective `pageSize`, `page=0` → 422, page-beyond-last → 422 with a distinct detail, omitted `pageSize` →
20. Confirm `GET /openapi/v1.json` publishes `page`/`pageSize` as `["null","integer"]` with correct
defaults on `api/v1/masterdata/people`. Confirm a real seeded Person's `GET /api/v1/masterdata/people/{id}`
response has no `raw`/`parsed` leakage and a lowercase-formatted id in the URL still resolves.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `GET /api/v1/masterdata/people` returns a paginated `PagedItems<PersonResponse>` | Unit test | `GetAllPeople_ReturnsPaginatedResults` |
| 2 | ❌ | `GET /api/v1/masterdata/people/{id}` returns the matching person | Unit test | `GetPersonById_ExistingId_ReturnsPerson` |
| 3 | ❌ | `GET /api/v1/masterdata/people/{id}` returns 404 for an unknown id | Unit test | `GetPersonById_UnknownId_Returns404` |
| 4 | ❌ | `{id}` matches case-insensitively | Unit test | `GetPersonById_LowercaseId_MatchesCaseInsensitively` |
| 5 | ❌ | A malformed `{id}` route segment returns 404, not an unhandled exception or bare 400 | Unit test | `GetPersonById_MalformedId_Returns404NotBadRequest` |
| 6 | ❌ | `page=0` returns 422 | Unit test | `GetAllPeople_PageZero_Returns422` |
| 7 | ❌ | Malformed `page` returns 422 | Unit test | `GetAllPeople_PageMalformed_Returns422` |
| 8 | ❌ | Malformed `pageSize` returns 422 | Unit test | `GetAllPeople_PageSizeMalformed_Returns422` |
| 9 | ❌ | Negative `pageSize` returns 422 | Unit test | `GetAllPeople_PageSizeNegative_Returns422` |
| 10 | ❌ | `pageSize` above 500 returns 422, never silently clamped | Unit test | `GetAllPeople_PageSizeAbove500_Returns422NotSilentClamp` |
| 11 | ❌ | `pageSize=0` returns every row with the response's `pageSize` reporting the effective count | Unit test | `GetAllPeople_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 12 | ❌ | Omitted `pageSize` defaults to 20 | Unit test | `GetAllPeople_PageSizeOmitted_DefaultsTo20` |
| 13 | ❌ | A page beyond the last returns 422 with a detail distinct from case 6 | Unit test | `GetAllPeople_PageBeyondLast_Returns422DistinctDetail` |
| 14 | ❌ | `dateOfBirth`/`dateOfDeath`/`completenessStatus` serialize as plain JSON values, never `{raw, parsed}` | Unit test | `GetPersonById_ExistingId_ReturnsPerson` (shape assertions) |
| 15 | ❌ | Unknown `dateOfBirth`/`dateOfDeath` serialize as JSON `null`, not `""` | Unit test | `GetPersonById_UnknownDates_ReturnsNullNotEmptyString` |
| 16 | ❌ | `page`/`pageSize` on `api/v1/masterdata/people` publish as `integer` in the transformer's own logic | Unit test | `NumericParameterSchemaTransformerTests` (new region) |
| 17 | ❌ | `page`/`pageSize` on `api/v1/masterdata/people` publish as `integer` on the real, live OpenAPI spec | Unit test (live pipeline) | `OpenApiSpecEndpointTests.PageParam_OnLiveSpec_PublishesIntegerType` (new `[DataRow]` entries) |
| 18 | ❌ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api`, proven live | Unit test | `PersonEndpoints_OnLiveSpec_TaggedMasterData` |
| 19 | ❌ | `ErrorPersonNotFound` exists and is non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 20 | ❌ | `README.md`/`addon/DOCS.md` document both new endpoints | Doc review | Endpoint tables contain the two new rows |
| 21 | ❌ | `docs/logging.md` registers `[Api - GetAllPeople]`/`[Api - GetPersonById]` | Doc review | "Defined prefixes" table contains both rows |
| 22 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 23 | ❌ | T1 — app starts in Visual Studio; both endpoints behave as specified | Live (T1) | Developer confirms clean startup and manual exercise of both endpoints |
| 24 | ❌ | T2 — the live contract holds on the built image | Live (T2) | Full pagination matrix + response-shape checks against a real seeded Person, per Step 9 |

---

## Notes

This is the first masterdata entity endpoint to actually ship under `/api/v1/masterdata/` — #184 (Sources)
and #185 (Characters) are siblings still in "Planning" per `overview.md`, so there is no existing
`/masterdata/` implementation to diff against for consistency; #186 sets the concrete pattern the other
four (`#184`, `#185`, `#187`, `#188`) should follow, alongside #196's conventions documentation.

Steps 4 and 5 (transformer registration, logging prefixes) exist only because this planning pass traced
the full chain of *other* standing rules the issue text itself doesn't mention — CLAUDE.md's numeric
query-parameter rule and the logging doc's own registration requirement respectively. Both are easy to
miss because neither `184`'s nor `185`'s issue text (not yet planned) will likely mention them either;
worth flagging to the developer that every remaining masterdata list issue in this family carries the same
two silent requirements.

Step 7's extra tests (beyond the issue's own four) are additions to the issue's Definition of Done in
substance, not merely in this plan doc — the corrected issue text below reflects that; whether to actually
push that correction via `gh issue edit` is a separate, later, human-approved action.

---

## Corrected issue text (for a future `gh issue edit`)

**Title:** Masterdata: GET /api/v1/masterdata/people list + get-by-id (unchanged)

**Body:**

```markdown
## Background

Depends on #193, #195, #196 (concrete sub-issues of #183, the parent tracking issue which ships no
implementation of its own).

`Person` (`src/Quotinator.Engine/Entities/Person.cs`) has no read endpoint today —
`IRestorableRepository<Person>` exists but is only used for batch-undo reversal.

## What needs to be done

1. `GET /api/v1/masterdata/people` — paginated list, using #193's `IListableRepository<Person>` +
   `Quotinator.Data.Models.PagedItems<T>` + the shared `PaginationParsing` helper (#195). Response items
   include `Id`, `Name`, `DateOfBirth`, `DateOfDeath`, `CompletenessStatus`, flattened via a new
   `PersonResponse` DTO — the raw `Person` entity must never be serialized directly (its `SafeValue<T>`
   fields would leak `{"raw":...,"parsed":...}` onto the wire).
2. `GET /api/v1/masterdata/people/{id}` — single Person by id, using #195's shared not-found helper.
   `{id}` matches case-insensitively.
3. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes.
4. No entity-specific filters yet — deferred per #196's filter convention.
5. Register `api/v1/masterdata/people` in `NumericParameterSchemaTransformer.NumericParamsByPath`
   (`page`/`pageSize`) — without this the published OpenAPI type regresses from `integer|string` to bare
   `string`, per #194/#195's precedent.
6. Register `[Api - GetAllPeople]`/`[Api - GetPersonById]` in `docs/logging.md`'s prefix table.
7. Update `README.md` and `addon/DOCS.md`'s endpoint tables.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| New: `Quotinator.Api.Tests` | `GetAllPeople_ReturnsPaginatedResults` | ❌ |
| New: `Quotinator.Api.Tests` | `GetPersonById_ExistingId_ReturnsPerson` | ❌ |
| New: `Quotinator.Api.Tests` | `GetPersonById_UnknownId_Returns404` | ❌ |
| New: `Quotinator.Api.Tests` | `GetPersonById_LowercaseId_MatchesCaseInsensitively` | ❌ |
| New: `Quotinator.Api.Tests` | `GetPersonById_MalformedId_Returns404NotBadRequest` | ❌ |
| New: `Quotinator.Api.Tests` | Full 8-case pagination matrix per CLAUDE.md's "Standard pagination contract" (`GetAllPeople_PageZero_Returns422` and 7 siblings) | ❌ |
| New: `Quotinator.Api.Tests` | `NumericParameterSchemaTransformerTests`/`OpenApiSpecEndpointTests` additions for `api/v1/masterdata/people` | ❌ |

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
