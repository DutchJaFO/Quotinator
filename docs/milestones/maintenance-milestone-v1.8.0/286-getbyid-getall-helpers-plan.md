# #286 — Extract GetById/GetAll generic helpers for the 7 plain-repository-pattern masterdata endpoints

**Status:** Planning
**GitHub issue:** #286
**Tiers required:** T1, T2
**Depends on:** #283 (done — Person is one of the 7 covered entities, needed its consistency fix first)

---

## Spec requirements

1. Add `EntityLookup.TryFindByIdAsync<TEntity, TResponse>(string id, IApiLocalizer localizer,
   IRepository<TEntity> repository, string notFoundMessageKey, Func<TEntity, Task<TResponse>>
   mapAsync)` in `Quotinator.Api.Endpoints.Shared`.
2. Add `PagedListing.GetAllAsync<TEntity, TResponse>(string? page, string? pageSize, IApiLocalizer
   localizer, IListableRepository<TEntity> repository, Func<IReadOnlyList<TEntity>,
   Task<IReadOnlyList<TResponse>>> mapItemsAsync)` in the same namespace.
3. Apply both to `CharacterEndpoints`, `PersonEndpoints`, `SeriesEndpoints`, `SoundCueEndpoints`,
   `SourceEndpoints`, `StageDirectionEndpoints`, `UniverseEndpoints`. No behaviour change.
4. `ConversationEndpoints` stays out of scope (ADR 017/#285's own pattern).

---

## Background — why this issue exists

Direct actionable conclusion of #281's research. See #281's closing comment for the full duplication
catalog and the Minimal-API-framework justification for a helper-method approach over a base class.

**Verified before starting** (re-confirmed current state of all 7 files, since #283 changed
`PersonEndpoints` since #281's original investigation):

- 3 of 7 entities (`Character`, `Series`, `Source`) need an async reference-reader call before
  mapping (batched for `GetAll`, single for `GetById`) — `CharacterSourceLinkReader`,
  `ISeriesUniverseReferenceReader`, `ISourceSeriesReferenceReader` respectively. The other 4
  (`Person`, `SoundCue`, `StageDirection`, `Universe`) map directly with no extra dependency — their
  `mapAsync`/`mapItemsAsync` delegate wraps a sync `ToResponse` call in `Task.FromResult(...)`.
- `PersonEndpoints.GetAll`'s beyond-last-page check now runs before mapping (fixed by #283) — all 7
  files share the same statement order today, so the helper's own fixed order (parse → query →
  validate-beyond-last → map → wrap) matches every one of them; no entity needs special-casing.
- `PersonEndpoints`'s `GetById` uses a nested-ternary code shape; the other 6 use various
  early-return/ternary shapes (per #281's catalog). All 7 have identical *intent*
  (`Guid.TryParse → repo.GetByIdAsync → null-or-mapped → 404-or-200`) — the helper collapses all of
  them to the same call, resolving the style inconsistency as a side effect, not a separate fix.
- Confirmed `IRepository<T>.GetByIdAsync`/`IListableRepository<T>.GetPageAsync` signatures
  (`Quotinator.Data.Repositories`) — both already async, no mismatch with the new helpers.

---

## Approach

**`EntityLookup.TryFindByIdAsync<TEntity, TResponse>`** — `Guid.TryParse(id, ...)` fails → `404` via
`NotFoundResult.OkOrNotFound<TResponse>(null, ...)`; else `await repository.GetByIdAsync(parsedId)`;
`null` → `404`; else `await mapAsync(entity)` → `200` via `NotFoundResult.OkOrNotFound`.

**`PagedListing.GetAllAsync<TEntity, TResponse>`** — `PaginationParsing.TryParse` fails → its own
`422`; else `await repository.GetPageAsync(page, pageSize)`; `PaginationParsing.ValidatePageBeyondLast`
fails → its own `422`; else `await mapItemsAsync(result.Items)`, wrap in
`new PagedItems<TResponse>(items, result.Page, result.PageSize, result.TotalCount)` → `200`.

**Per-entity handler shrinks to roughly:**

```csharp
private static Task<IResult> GetById(string id, IApiLocalizer localizer, ILogger<Log> logger,
    IRepository<CharacterEntity> repository, ICharacterSourceLinkReader linkReader)
{
    logger.LogIdQuery($"[Api - {GetCharacterByIdName}]", id);
    return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.CharacterNotFound,
        async c => ToResponse(c, await linkReader.GetSourceReferencesAsync(c.Id)));
}
```

for the 3 entities with a reference reader, and for the other 4 (e.g. `Universe`):

```csharp
private static Task<IResult> GetById(string id, IApiLocalizer localizer, ILogger<Log> logger,
    IRepository<UniverseEntity> repository)
{
    logger.LogIdQuery($"[Api - {GetUniverseByIdName}]", id);
    return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.UniverseNotFound,
        e => Task.FromResult(ToResponse(e)));
}
```

`GetAll` follows the same shape via `PagedListing.GetAllAsync`, passing the entity's existing
`ToResponse` (batched where a reference reader exists) as `mapItemsAsync`.

Each entity's own `ToResponse` method, route registration, `WithName`/`WithSummary`/`WithDescription`,
and not-found message key are all unchanged.

---

## Files touched

- `src/Quotinator.Api/Endpoints/Shared/EntityLookup.cs` — new
- `src/Quotinator.Api/Endpoints/Shared/PagedListing.cs` — new
- `src/Quotinator.Api/Endpoints/CharacterEndpoints.cs`
- `src/Quotinator.Api/Endpoints/PersonEndpoints.cs`
- `src/Quotinator.Api/Endpoints/SeriesEndpoints.cs`
- `src/Quotinator.Api/Endpoints/SoundCueEndpoints.cs`
- `src/Quotinator.Api/Endpoints/SourceEndpoints.cs`
- `src/Quotinator.Api/Endpoints/StageDirectionEndpoints.cs`
- `src/Quotinator.Api/Endpoints/UniverseEndpoints.cs`
- No test changes expected — see "Expected tests" in the GitHub issue for why.

---

## Steps

### 1. Add the two shared helpers
**Status:** ⬜ Not started

### 2. Apply to the 4 entities with no reference reader (Person, SoundCue, StageDirection, Universe)
**Status:** ⬜ Not started

### 3. Apply to the 3 entities with a batched reference reader (Character, Series, Source)
**Status:** ⬜ Not started

### 4. Verify
**Status:** ⬜ Not started

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Both helpers added, matching the issue's exact specified signatures | Code review | `EntityLookup.cs`/`PagedListing.cs` |
| 2 | ⬜ | All 7 endpoint files use the helpers, same public behaviour | Unit test | Existing endpoint test suites pass unmodified |
| 3 | ⬜ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — all pass |
| 4 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 5 | ⬜ | T2 — live container's 7 masterdata list/get-by-id endpoints still work correctly | Live (T2) | Docker smoke test |

---

## Notes

None yet.
