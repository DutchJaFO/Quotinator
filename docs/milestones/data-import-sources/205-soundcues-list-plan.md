# #205 — Masterdata: GET /api/v1/masterdata/soundcues list + get-by-id

**Status:** Waiting for release
**GitHub issue:** #205
**Tiers required:** T1, T2
**Depends on:** #195, #196

---

## Spec requirements (corrected during planning review 2026-07-19)

1. Register `IListableRepository<SoundCueEntity>` in `Program.cs`, resolving to the existing
   `IRestorableRepository<SoundCueEntity>` singleton (`Program.cs:301`) — a second interface binding, not
   a second object. This entity was left out of #193's original six-entity scope (Source, Character,
   Person, Series, Universe, Conversation), so this issue adds the missing binding itself rather than
   depending on #193 to have done it — the exact same gap #204 (StageDirection) closed for its own
   entity.
2. `GET /api/v1/masterdata/soundcues` — paginated list, using the newly-registered
   `IListableRepository<SoundCueEntity>.GetPageAsync` + `Quotinator.Data.Models.PagedItems<T>` +
   `PaginationParsing.TryParse`/`ValidatePageBeyondLast` (rejects out-of-range input with 422; does not
   clamp). Response items are a new `SoundCueResponse` DTO with `Id`, `Text`, `SoundFileUrl`, `ImageUrl`,
   `CompletenessStatus` — no `MasterDataReference` field, since `SoundCueEntity` has no FK to another
   masterdata entity (same shape as #204's `StageDirectionResponse`, plus one extra optional field).
3. `GET /api/v1/masterdata/soundcues/{id}` — single SoundCue by id, using `NotFoundResult.OkOrNotFound`.
   `{id}` matches case-insensitively — for free, via `SqliteRepository<T>.GetByIdAsync`'s existing
   uppercase-normalisation, not new logic this issue has to write.
4. Both endpoints use `RateLimitPolicies.Api`, tag `ApiTags.MasterData`, and `[Description]` attributes.
5. No entity-specific filters yet — deferred per #196's entity-scoped filter-parameter convention.
6. `page`/`pageSize` on `api/v1/masterdata/soundcues` must be registered in
   `NumericParameterSchemaTransformer.NumericParamsByPath` — the same gap every issue in the #184–#189/
   #204 batch independently found for their own paths.
7. Per CLAUDE.md's "Standard pagination contract", the endpoint must ship with the full eight-case
   pagination test matrix, plus a live `OpenApiSpecEndpointTests` pipeline case (not just the
   transformer's own unit tests) — #204 added this on top of what #188 shipped; #205 follows #204's
   fuller precedent.
8. Update `README.md` and `addon/DOCS.md`'s endpoint tables, and register the new
   `[Api - GetAllSoundCues]`/`[Api - GetSoundCueById]` log prefixes in `docs/logging.md`.
9. Route naming: `soundcues` (plain lowercase-concatenated, **not** kebab-case `sound-cues`) — same
   explicit developer decision (2026-07-19) already applied to #204's `stagedirections`, matching the
   existing single-word style every other masterdata route uses.

---

## Background — why this issue exists

`SoundCueEntity` (`src/Quotinator.Engine/Entities/SoundCueEntity.cs`, #67/#68) — "a reusable audio
element... that can appear in a conversation" — has `IRestorableRepository<SoundCueEntity>` registered
(`Program.cs:301`, added for #67/#68's stale-Add-target/batch-reversal machinery) but no
`IListableRepository<SoundCueEntity>` binding and no read endpoint of any kind today. #193 explicitly
scoped itself to Source, Character, Person, Series, Universe, and Conversation — SoundCue was never
included, the same gap #204 closed for StageDirection. #204 and #205 were filed together (2026-07-19) as
a pair to close this gap for both remaining entities.

**Verified before starting** (per this project's standing rule — every issue in this milestone's
`184`–`205` range has had at least one factual error caught this way; #204's own just-shipped code — the
closest possible structural sibling, filed and implemented in the same session for the identically-shaped
gap — was read directly and used as the template):

- **`SoundCueEntity` fields confirmed directly** (`src/Quotinator.Engine/Entities/SoundCueEntity.cs`):
  `Text` (`string`, defaults to `string.Empty`), `SoundFileUrl` (`string?`), `ImageUrl` (`string?`),
  `ImportBatchId` (`Guid?`), `CompletenessStatus` (`SafeValue<CompletenessStatus?>`), `NoValueKnown`
  (`IReadOnlyList<string>`), plus `RecordBase`'s `Id`/`DateCreated`/`DateModified`/`DateDeleted`/
  `IsDeleted`. **No FK to another masterdata entity** — confirmed by reading the full file — so no
  `MasterDataReference`-typed field is needed anywhere on `SoundCueResponse`. The only structural
  difference from `StageDirectionEntity` is the extra `SoundFileUrl` field.
- **`IRestorableRepository<SoundCueEntity>` confirmed registered, `IListableRepository<T>` confirmed NOT
  registered** — read `Program.cs` directly (`Program.cs:301` for the former; grepped the file for
  `IListableRepository<SoundCueEntity>` and found no match). This issue's Step 1 adds exactly the one
  missing line, mirroring #204's just-shipped `IListableRepository<StageDirectionEntity>` binding
  (`Program.cs:318`) and the earlier `IListableRepository<Source>`/`<Character>`/`<Person>`/
  `<ConversationEntity>` bindings — a second interface binding onto the same object, not a second
  instance.
- **`RepositorySql.SelectPage`/generic count already work for any `[Table]`-attributed entity with no
  further infrastructure work** — confirmed by reading #193's shipped code, same as #204: `SelectPage
  (tableName)` and the generic active-row count are driven purely by the `[Table]` attribute
  (`[Table("SoundCues")]`, confirmed on the entity), not by an entity-specific allowlist. No schema
  change, no new `RepositorySql` method, no new `SqlQueryGuardTests`/`RepositorySqlGuardTests` case is
  needed for this issue.
- **`PagedItems<T>`, `PaginationParsing`, `NotFoundResult`, `ApiTags.MasterData`, `RateLimitPolicies.Api`,
  `NumericParameterSchemaTransformer.NumericParamsByPath`, and the full eight-case pagination test matrix
  requirement** are all re-confirmed exactly as every #184–#189/#204 plan doc already established — not
  re-verified line-by-line again here; see #184's plan doc for the original, most detailed verification
  of each.
- **Response DTO decision, mirroring #204/#186/#188 exactly**: `SoundCueEntity`'s
  `SafeValue<CompletenessStatus?>` field has no `System.Text.Json` converter anywhere in the codebase.
  `SoundCueResponse.CompletenessStatus` is typed as the plain enum, populated via
  `entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete`.
- **`ApiMessages.SoundCueNotFound` does not exist yet**, and no `ErrorSoundCueNotFound` key exists in any
  of the three `i18ntext/UI.*.json` files — confirmed via grep. Both must be added, mirroring
  `StageDirectionNotFound`/`ErrorStageDirectionNotFound`.
- **Malformed-id-as-404 precedent re-confirmed**: `GetSoundCueById` follows the same precedent every
  prior masterdata `GetById` endpoint uses — a malformed (non-`Guid`) `{id}` and a well-formed-but-unknown
  `{id}` both produce the same 404 via `NotFoundResult.OkOrNotFound`.
- **Route naming already decided, re-applied here**: `soundcues` (plain lowercase-concatenated), matching
  #204's `stagedirections` decision — this is the second and last multi-word masterdata resource name in
  this milestone, so the same decision applies without needing to be re-litigated.
- **Soft-deleted visibility, already correct, confirmed structural**: `RepositorySql.SelectPage`/
  `SelectById` already filter `IsDeleted = 0` unconditionally — a soft-deleted SoundCue is already
  invisible via this endpoint for free, per CLAUDE.md's "Soft-deleted rows are invisible by default,
  everywhere" convention.
- **Fake repository and endpoint shape confirmed against #204's actual shipped code** (`StageDirectionEndpoints.cs`,
  `StageDirectionResponse.cs`, `FakeStageDirectionRepository.cs`) — the exact same "canned page / canned
  single entity" fake shape, the exact same `GetAll`/`GetById`/`ToResponse` endpoint structure, adapted
  only for the entity name and the extra `SoundFileUrl` field.

Conventions and infrastructure only — no schema change, no new `Sql.cs`/`RepositorySql` method, only the
one new DI binding this issue itself adds.

---

## Steps

### 1. Register `IListableRepository<SoundCueEntity>` in `Program.cs`

**Status:** Done.

Add immediately after #204's `IListableRepository<StageDirectionEntity>` binding (`Program.cs:318`):

```csharp
builder.Services.AddSingleton<IListableRepository<SoundCueEntity>>(sp =>
    (IListableRepository<SoundCueEntity>)sp.GetRequiredService<IRestorableRepository<SoundCueEntity>>());
```

### 2. `SoundCueResponse` DTO

**Status:** Done.

New file `src/Quotinator.Api/Models/SoundCueResponse.cs`, namespace `Quotinator.Api.Models`:

```csharp
namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single SoundCue.</summary>
public sealed class SoundCueResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The sound cue text in its original language.</summary>
    public required string Text { get; init; }

    /// <summary>Optional audio file for the cue. <c>null</c> when unset.</summary>
    public string? SoundFileUrl { get; init; }

    /// <summary>Optional image illustrating the cue. <c>null</c> when unset.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required Quotinator.Data.Entities.CompletenessStatus CompletenessStatus { get; init; }
}
```

### 3. `ApiMessages.SoundCueNotFound` + i18n lockstep

**Status:** Done.

Add to `src/Quotinator.Constants/Api/ApiMessages.cs`:
```csharp
public const string SoundCueNotFound = "ErrorSoundCueNotFound";
```

Add `"ErrorSoundCueNotFound"` to all three `i18ntext/UI.*.json` files in the same commit:
- `UI.en-GB.json`: `"No sound cue with the requested ID was found."`
- `UI.nl.json`: `"Er is geen geluidssignaal gevonden met het opgegeven ID."`
- `UI.de.json`: `"Es wurde kein Soundeffekt mit der angegebenen ID gefunden."`

`TranslationCompletenessTests` must stay green.

### 4. `SoundCueEndpoints.cs`

**Status:** Done.

New file `src/Quotinator.Api/Endpoints/SoundCueEndpoints.cs`, static class `SoundCueEndpoints`,
mirroring #204's `StageDirectionEndpoints.cs` exactly:

```csharp
namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/soundcues</c> endpoints.</summary>
internal static class SoundCueEndpoints
{
    private sealed class Log { }

    internal static void MapSoundCueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/soundcues")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSoundCues")
             .WithSummary("List sound cues")
             .WithDescription(
                 "Returns a paginated list of sound cues. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every sound cue as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSoundCueById")
             .WithSummary("Sound cue by ID")
             .WithDescription("Returns a single sound cue by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every sound cue as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllSoundCues] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLastError is not null)
            return beyondLastError;

        var mapped = new PagedItems<SoundCueResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.Page, result.PageSize, result.TotalCount);

        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the sound cue.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository)
    {
        logger.LogInformation("[Api - GetSoundCueById] id={Id}", id);

        SoundCueEntity? entity = Guid.TryParse(id, out var soundCueId)
            ? await repository.GetByIdAsync(soundCueId)
            : null;

        var response = entity is null ? null : ToResponse(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.SoundCueNotFound);
    }

    private static SoundCueResponse ToResponse(SoundCueEntity entity) => new()
    {
        Id                 = entity.Id.ToString("D").ToUpperInvariant(),
        Text               = entity.Text,
        SoundFileUrl       = entity.SoundFileUrl,
        ImageUrl           = entity.ImageUrl,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}
```

Register the call in `Program.cs` alongside the other `Map*Endpoints()` calls:
```csharp
app.MapSoundCueEndpoints();
```

### 5. Register the OpenAPI numeric-param transformer path

**Status:** Done.

Add to `NumericParameterSchemaTransformer.NumericParamsByPath`:
```csharp
["api/v1/masterdata/soundcues"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
{
    ["page"]     = QueryParamDefaults.Page,
    ["pageSize"] = QueryParamDefaults.PageSize,
},
```

### 6. `FakeSoundCueRepository`

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Fakes/FakeSoundCueRepository.cs`, implementing
`IListableRepository<SoundCueEntity>`, mirroring #204's `FakeStageDirectionRepository.cs` exactly (canned
`ReturnPage`/`ReturnById`, `Last*Requested` recording, write methods throw `NotImplementedException`).

### 7. Endpoint tests

**Status:** Done.

New file `tests/Quotinator.Api.Tests/Endpoints/SoundCueEndpointsTests.cs`, mirroring #204's
`StageDirectionEndpointsTests.cs` exactly (`CreateFactory(FakeSoundCueRepository? repository = null)`, a
`NewSoundCue(...)` fixture builder). 14 tests:

- `GetAllSoundCues_ReturnsPaginatedResults`
- `GetAllSoundCues_PageZero_Returns422`
- `GetAllSoundCues_PageMalformed_Returns422`
- `GetAllSoundCues_PageSizeMalformed_Returns422`
- `GetAllSoundCues_PageSizeNegative_Returns422`
- `GetAllSoundCues_PageSizeAbove500_Returns422NotSilentClamp`
- `GetAllSoundCues_PageSizeZero_ReturnsAllRowsAsOnePage`
- `GetAllSoundCues_PageSizeOmitted_DefaultsTo20`
- `GetAllSoundCues_PageBeyondLast_Returns422DistinctDetail`
- `GetSoundCueById_ExistingId_ReturnsSoundCue` — must additionally assert `completenessStatus`
  serializes as a plain JSON string value, never `{"raw":...,"parsed":...}`
- `GetSoundCueById_UnknownId_Returns404`
- `GetSoundCueById_MalformedId_Returns404NotBadRequest`
- `GetSoundCueById_LowercaseId_MatchesCaseInsensitively`
- `SoundCueEndpoints_OnLiveSpec_TaggedMasterData`

**Extend existing shared-infrastructure test files** (per CLAUDE.md's rule that a transformer path
registration needs a live-pipeline test, not just the transformer's own unit tests — #204 added this on
top of what #188 originally shipped without it):
- `tests/Quotinator.Api.Tests/OpenApi/NumericParameterSchemaTransformerTests.cs` — new case(s) for
  `api/v1/masterdata/soundcues`.
- `tests/Quotinator.Api.Tests/OpenApi/OpenApiSpecEndpointTests.cs` — two new `[DataRow]` entries:
  `("/api/v1/masterdata/soundcues", "page")`, `("/api/v1/masterdata/soundcues", "pageSize")`.

### 8. Documentation

**Status:** Done.

Update `README.md`'s and `addon/DOCS.md`'s REST API Endpoints tables — add rows for
`GET /api/v1/masterdata/soundcues` and `GET /api/v1/masterdata/soundcues/{id}`. Add
`[Api - GetAllSoundCues]`/`[Api - GetSoundCueById]` to `docs/logging.md`'s "Defined prefixes" table,
placed near the other `[Api - Get*ById]` rows.

### 9. Solution file

**Status:** Done. No changes needed — confirmed no `<Compile Remove>`/`<Compile Include>` restriction in
either `Quotinator.Api.csproj` or `Quotinator.Api.Tests.csproj`; all new `.cs` files are picked up by the
existing SDK-style project globs (same outcome #204 confirmed for its own files).

Add `src/Quotinator.Api/Models/SoundCueResponse.cs`, `src/Quotinator.Api/Endpoints/SoundCueEndpoints.cs`,
and `tests/Quotinator.Api.Tests/Fakes/FakeSoundCueRepository.cs`/`tests/Quotinator.Api.Tests/Endpoints/
SoundCueEndpointsTests.cs` to `Quotinator.slnx` if not automatically picked up by the existing project
globs — has not been needed for any `.cs` file in this batch so far (confirmed by #204's own check of
both `.csproj` files for `<Compile Remove>`/`<Compile Include>` restrictions — none found), but verify
regardless.

### 10. Verify

**Status:** Done (unit-test tier only; T1/T2 live verification still pending — see header status).

`dotnet build --configuration Release` → 0 Warning(s), 0 Error(s). `dotnet test --configuration Release
--verbosity normal` → all 10 test projects report "Test Run Successful", 487 tests passed in
`Quotinator.Api.Tests` (including all 14 new `SoundCueEndpointsTests`, 4 new
`NumericParameterSchemaTransformerTests` cases, 2 new `OpenApiSpecEndpointTests` DataRow cases, and both
`TranslationCompletenessTests`), 0 warnings/0 errors overall. Confirmed red-before-green: temporarily
commented out `app.MapSoundCueEndpoints();` in `Program.cs` and reran `SoundCueEndpointsTests` — 12 of 14
failed (the 2 that "passed" were the two tests that already expect 404, which a nonexistent route also
produces) — then restored the line and reran the full suite green.

T2 (Docker) and T1 (Visual Studio) have not been run — deferred to a single combined pass covering #204 and
#205 together, per this session's instructions.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full suite green across all 10 test projects, 0 warnings, 0 errors — check every
project's own summary line, not just the last one printed. Confirm all listed expected tests started red
before implementation (e.g. by temporarily reverting the new `app.MapSoundCueEndpoints();` line).

T2 (Docker): `docker build` + `docker run`, then:
```bash
curl -s "http://localhost:8080/api/v1/masterdata/soundcues"
curl -s "http://localhost:8080/api/v1/masterdata/soundcues?pageSize=0"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/soundcues?pageSize=999"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/soundcues/00000000-0000-0000-0000-000000000000"
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"masterdata/soundcues[^"]*"'
```
Confirm: default list returns 200 with `items`/`page`/`pageSize`/`totalCount`/`totalPages`; `pageSize=0`
returns every SoundCue as one page; `pageSize=999` returns 422; an unknown id returns 404 with
`ErrorSoundCueNotFound`'s message; the OpenAPI spec publishes `page`/`pageSize` as `integer|null` on
`api/v1/masterdata/soundcues`. Also fetch a real SoundCue id via `Quotinator.Tools.DbInspector`
(`SELECT Id, Text FROM SoundCues WHERE IsDeleted = 0 LIMIT 1;`) and confirm
`GET /api/v1/masterdata/soundcues/{that id, lowercased}` returns 200 — live proof of case-insensitive
matching, not just the unit test.

This project always runs T2 regardless of a documented trigger — this issue's own change to `Program.cs`
(the new DI registration and `MapSoundCueEndpoints()` call) also independently satisfies
`docs/release-verification.md`'s "touches Program.cs startup" trigger.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `IListableRepository<SoundCueEntity>` is registered in DI | Unit test | App starts under `WebApplicationFactory` (implicit in every endpoint test) |
| 2 | ✅ | `GET /api/v1/masterdata/soundcues` returns a paginated list | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_ReturnsPaginatedResults` |
| 3 | ✅ | `page=0` returns 422 | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageZero_Returns422` |
| 4 | ✅ | Malformed `page`/`pageSize` returns 422 | Unit test | `_PageMalformed_Returns422`, `_PageSizeMalformed_Returns422` |
| 5 | ✅ | Negative `pageSize` returns 422 | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageSizeNegative_Returns422` |
| 6 | ✅ | `pageSize > 500` returns 422, never clamped | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageSizeAbove500_Returns422NotSilentClamp` |
| 7 | ✅ | `pageSize = 0` returns every row with the effective count reported | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 8 | ✅ | Omitted `pageSize` defaults to 20 | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageSizeOmitted_DefaultsTo20` |
| 9 | ✅ | A page beyond the last returns 422, distinct from case 3 | Unit test | `SoundCueEndpointsTests.GetAllSoundCues_PageBeyondLast_Returns422DistinctDetail` |
| 10 | ✅ | `GET /api/v1/masterdata/soundcues/{id}` returns the matching SoundCue | Unit test | `SoundCueEndpointsTests.GetSoundCueById_ExistingId_ReturnsSoundCue` |
| 11 | ✅ | `completenessStatus` serializes as a plain JSON value, never `{raw, parsed}` | Unit test | Same test (shape assertion) |
| 12 | ✅ | An unknown id returns 404 | Unit test | `SoundCueEndpointsTests.GetSoundCueById_UnknownId_Returns404` |
| 13 | ✅ | A malformed `{id}` route segment returns 404, not an unhandled exception or bare 400 | Unit test | `SoundCueEndpointsTests.GetSoundCueById_MalformedId_Returns404NotBadRequest` |
| 14 | ✅ | A lowercase id matches an uppercase-stored id | Unit test | `SoundCueEndpointsTests.GetSoundCueById_LowercaseId_MatchesCaseInsensitively` |
| 15 | ✅ | `page`/`pageSize` publish as `integer` in the OpenAPI spec, proven via the live pipeline | Unit test | `NumericParameterSchemaTransformerTests` (new cases) + `OpenApiSpecEndpointTests` (new `[DataRow]`s) |
| 16 | ✅ | Both endpoints tagged `ApiTags.MasterData` and rate-limited `RateLimitPolicies.Api`, proven live | Unit test | `SoundCueEndpoints_OnLiveSpec_TaggedMasterData` |
| 17 | ✅ | `ApiMessages.SoundCueNotFound` exists and all three locale files carry `ErrorSoundCueNotFound` | Unit test | `TranslationCompletenessTests` |
| 18 | ✅ | `README.md`/`addon/DOCS.md`/`docs/logging.md` document both new endpoints | Doc review | Files updated |
| 19 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 20 | ✅ | T1 — app starts in Visual Studio; both endpoints reachable | Live (T1) | Developer confirmed 2026-07-19 — clean startup, GetAll/GetById/pagination contract all exercised live |
| 21 | ✅ | T2 — the live contract holds against the built image | Live (T2) | `docker build`/`docker run` matrix — see Step 10. Run 2026-07-19 as a combined pass across #184–#189/#204/#205 — confirmed live with real seeded SoundCue rows |

---

## Notes

This issue closes the last remaining gap in #193's original six-entity scope — with #204 (StageDirection)
and #205 (SoundCue) both landed, every entity that had an `IRestorableRepository<T>` before this milestone
now also has an `IListableRepository<T>` and a masterdata list/get-by-id endpoint pair.

No `MasterDataReference` work is needed here (see Background) — this is the fifth masterdata entity in
the milestone with no FK of its own, after Person (#186), Universe (#188), and StageDirection (#204).

Route naming (`soundcues`, not `sound-cues`) reuses the same explicit developer decision #204 already
established — not re-litigated here.

---

## Corrected issue text (for a future `gh issue edit`)

The issue as filed already reflects this plan doc's findings (it was drafted in this same planning pass
as #204, not corrected after the fact) — no `gh issue edit` is needed unless implementation surfaces
something new.
