# #192 — Expose series/universe on the quote read path — QuoteResponse fields and filters

**Status:** Waiting for release
**GitHub issue:** #192
**Tiers required:** T1, T2
**Depends on:** #180, #196, #206

---

## Spec requirements (corrected during planning review 2026-07-19)

1. Add `Series` and `Universe` to `QuoteResponse` (`Quotinator.Core.Models`), populated by joining
   `Sources.SeriesId` → `Series` → `Series.UniverseId` → `Universe`. Both nullable — a standalone
   Source has no Series, and a standalone Series has no Universe (both are the common case: only 75 of
   479 bundled Sources are in a Series at all). Both are typed as the minimal, read-only
   `MasterDataReference` (`{id, name}`) per CLAUDE.md's "Masterdata reference shape" convention — **not**
   a bare `SeriesId`/`UniverseId` string, and **not** the full `SeriesResponse`/`UniverseResponse` record.
2. **Architectural gap found during this review, resolved by a separate prerequisite issue, not by
   this one.** `QuoteResponse` lives in `Quotinator.Core.Models`, and `Quotinator.Core` had zero project
   references (confirmed directly against `Quotinator.Core.csproj` at the time of this review) — it
   couldn't reference anything in `Quotinator.Api`, where `MasterDataReference` and every masterdata
   response DTO lived. Two earlier drafts of this plan tried to solve that gap locally (first: move
   `MasterDataReference` to Core; then: add a second, duplicate Core-only `MasterDataReference`) — both
   were wrong, because the real blocker went deeper: every masterdata response DTO also needs
   `Quotinator.Data.Entities.CompletenessStatus`, which neither Core nor a Core/Api split could reach at
   once, only `Quotinator.Api` could. That's not a #192-scoped problem — it's a structural consequence of
   splitting `Quotinator.Core` and `Quotinator.Engine` into separate projects at all (ADR 004's Engine
   revision), and it will recur for any future Core-shaped type needing a Data-layer concept. **#206**
   merges `Quotinator.Engine` back into `Quotinator.Core`, after which `MasterDataReference` and every
   masterdata response DTO simply live in `Quotinator.Core.Models` already — #192 does not introduce,
   move, or duplicate `MasterDataReference` at all; it only consumes the type that already exists there
   once #206 lands. See `206-core-engine-merge-plan.md`.
3. Extend `Sql.Quotes.SelectBase`'s projection with two `LEFT JOIN`s (`Series`, then `Universe`) and four
   new columns. They must be `LEFT` — a Source with no Series (or a Series with no Universe) must still
   return its quote, not drop it from every result. This is the one query every read path shares
   (`GetById`, `GetAll`, `GetRandom`, `Search`), so one change here covers all four.
4. Add Series and Universe filters to `GET /api/v1/quotes` (`GetAll`), `GET /api/v1/quotes/random`
   (`GetRandom`), and `GET /api/v1/quotes/search` (`Search`), following #196's entity-scoped
   filter-parameter convention (`{entity}Id` / `{entity}`, mutually exclusive, resolved via
   `EntityFilterParsing.ResolveAsync` to a single `Guid` before it ever reaches the query) — **this issue
   is that convention's first real consumer**, not a re-decision of it. See Background for exactly how
   this coexists with `GetRandom`/`Search`'s existing `character`/`author`/`source` filters, which stay
   fuzzy and untouched.
5. A Universe filter must match quotes across *every* Series in that Universe, not only Sources directly
   in it — the join is Quote → Source → Series → Universe, so the filter applies two levels up from
   `Sources`. Expressed as a subquery (`s.SeriesId IN (SELECT Id FROM Series WHERE UniverseId = @id AND
   IsDeleted = 0)`), not a new `LEFT JOIN` in the `WHERE`-only (count) query shapes — see Step 4.
6. Update `README.md`, `addon/DOCS.md`, and `QuoteEndpoints.cs`'s `[Description]` attributes in the same
   commit, per CLAUDE.md's "Keeping API documentation in sync" rule.
7. Per CLAUDE.md's case-insensitivity convention, an id-valued `seriesId`/`universeId` filter matches
   case-insensitively — this is already `EntityFilterParsing.ResolveAsync`'s own behaviour (it parses to
   a real `Guid` via `Guid.TryParse`, which is inherently case-insensitive, then binds that `Guid` via
   the globally-registered `GuidHandler`), so no extra work is needed in this issue beyond using the
   shared helper as designed.
8. `Sql.Series.SelectIdByName`/`Sql.Universe.SelectIdByName` **already exist** (added for #180's import
   matching, `Sql.cs:392`/`Sql.cs:436`) — reused directly as the `resolveIdByName` delegates
   `EntityFilterParsing.ResolveAsync` needs, via two new small readers (`ISeriesNameResolver`/
   `IUniverseNameResolver`), not new SQL.

---

## Background — why this issue exists

#179 added the `Universe` → `Series` → `Source` schema and #180 populated it (75 Sources across 26
Series and 5 Universes, from a curated overlay file). Nothing reads it — `QuoteResponse` has no `series`
or `universe` field, and no endpoint filters on either, so the data #180 populates has no read path from
a quote at all. This is the capability #169's research was originally motivated by — grouping related
Sources so a consumer can ask for "a random Star Wars quote" or display "from the Middle Earth universe"
without knowing which individual films exist. Today a consumer would have to fetch the quote, look up
its Source (#184), then that Source's Series (#187), then that Series' Universe (#188) — three extra
round-trips to answer a question the quote itself should be able to answer, and still no way to *filter*
by either. Found while reviewing #180's T1 (2026-07-16): the developer's first observation on seeing a
live quote response was that it carries neither field.

The masterdata list endpoints (#184/#187/#188) are a different concern — they expose Series/Universe as
first-class entities to enumerate. This issue is about enriching and filtering the *quote* read path,
which none of them touch.

**Verified before starting** (per this project's standing rule — every issue in this milestone's
`184`–`205` range has had at least one factual error caught this way):

- **`QuoteResponse` (`src/Quotinator.Core/Models/QuoteResponse.cs`) confirmed to have no `Series`/
  `Universe` field today** — read the full file directly. Its existing fields (`Id`, `Quote`, `Language`,
  `OriginalLanguage`, `Source`, `Date`, `Character`, `Author`, `Type`, `Genres`, `Conversations`,
  `EmbeddedConversation`) match the issue's own framing exactly.
- **`Sql.Quotes.SelectBase` confirmed to have no Series/Universe join** — read the full private constant
  directly (`Sql.cs:49-67`). It already joins `Sources s`, `Characters c` (`LEFT`), `People p` (`LEFT`),
  and three translation tables (`LEFT`) — Series/Universe are absent.
- **`MasterDataReference` project-boundary gap — the central finding of this review, resolved by filing
  #206 rather than by any workaround inside this plan.** `Quotinator.Core.csproj` had zero
  `<ProjectReference>` entries (confirmed by reading the file directly) — under the pre-#206 project
  graph (`Quotinator.Api` → `Quotinator.Engine` → `Quotinator.Core`; `Quotinator.Api` also referencing
  `Quotinator.Core` directly), Core couldn't reference anything in Api, where `MasterDataReference` and
  every masterdata response DTO lived (created by #184, reused by #185/#187). Two earlier drafts of this
  plan proposed local fixes — relocating the existing type, then adding a duplicate Core-only type — and
  both were wrong for the same underlying reason: every masterdata response DTO also needs
  `Quotinator.Data.Entities.CompletenessStatus` (confirmed genuine Data-layer plumbing — used by
  `Quotinator.Data.Import.CompletenessGuard`, backed by a SQL CHECK constraint per ADR 008), which
  neither Core nor Engine could see *together with* a Core-owned reference type, only `Quotinator.Api`
  could. That ruled out moving the response DTOs into Core too (the natural next idea) without also
  fixing the deeper problem: Core and Engine being split into separate projects at all. **#206** merges
  them, after which this entire tangle resolves for free — `MasterDataReference` and every masterdata
  response DTO already live in `Quotinator.Core.Models`, and `QuoteResponse` just references the type
  that's already there. This issue makes no `MasterDataReference`-related change of its own.
- **`GetAll` (`/api/v1/quotes`, the plain list endpoint) has no `character`/`author`/`source` filters
  today, unlike `GetRandom`/`Search`** — confirmed by reading `SqliteQuoteService.GetAll`
  (`SqliteQuoteService.cs:151`) directly: it calls `BuildFilterWhere(types, genres, lang, yearFrom,
  yearTo)`, the five-argument overload that has no `character`/`author`/`source` parameters at all
  (`SqliteQuoteService.cs:363-364`). Series/Universe are `GetAll`'s *first* entity-scoped filters of any
  kind — there is no existing fuzzy behaviour on this endpoint to reconcile against.
- **`GetRandom`/`Search` do have existing `character`/`author`/`source` filters, and CLAUDE.md already
  resolves how the new Series/Universe filters coexist with them** — confirmed by re-reading CLAUDE.md's
  "Entity-scoped filter-parameter convention" section directly: "Explicit exemption: `/quotes/search` and
  `/quotes/random`. Their existing `character`/`author`/`source` filters stay fuzzy, direct
  contains-matches — this convention is for *new* entity-scoped filters (#184–#189, #192), not a retrofit
  of Search/RandomQuote's existing behaviour." Read precisely: the exemption protects the *existing*
  `character`/`author`/`source` filters from being retrofitted to the strict id/name convention — it does
  **not** exempt this issue's own *new* Series/Universe filters from following it. #192 is explicitly
  named as a consumer of the convention in that same sentence. This resolves the issue's own hedge
  ("if #183's convention differs, the mismatch... is worth a deliberate note") definitively: there is no
  mismatch to note — Series/Universe on `/random`/`/search` use the strict convention; the pre-existing
  `character`/`author`/`source` filters on those same two endpoints are untouched.
- **`Sql.Series.SelectIdByName`/`Sql.Universe.SelectIdByName` already exist and are already used** —
  confirmed both queries exist (`Sql.cs:392`, `Sql.cs:436`) and are already called from
  `ImportActionPlanner.cs` (four call sites, via a raw `IDbConnection`/transaction for #180's import
  matching). Neither is currently exposed through a repository/reader class an endpoint could inject —
  this issue adds one small reader per entity (`ISeriesNameResolver`/`IUniverseNameResolver`), reusing
  the existing SQL rather than writing new queries.
- **`EntityFilterParsing.ResolveAsync` (`src/Quotinator.Api/Endpoints/Shared/EntityFilterParsing.cs`)
  confirmed to have zero real consumers today** — #196 built it and its three `ApiMessages` keys
  (`MutuallyExclusiveEntityFilter`, `InvalidEntityFilterId`, `EntityFilterNoMatch`, all confirmed present
  in `ApiMessages.cs`) but deliberately did not wire it to a real repository, since no consuming endpoint
  existed yet (#196's own Notes: "#192 is where a real `resolveIdByName` delegate first gets built").
  This issue is that first wiring.
- **`IQuoteService`'s three filtering methods (`GetAll`, `GetRandom`, `Search`) need new optional
  parameters** — confirmed by reading the interface directly (`IQuoteService.cs`): none currently accept
  a Series/Universe filter of any kind. New parameters are added at the end of each signature (optional,
  default `null`) to avoid a breaking change to the interface's existing shape.
- **`BuildFilterWhere`'s two overloads both need the new filter clauses** — confirmed by reading
  `SqliteQuoteService.cs:363-394` directly. The 5-arg overload (used by `GetAll`) and the 8-arg overload
  (used by `GetRandom`/`Search`) share one underlying implementation already — the new `seriesId`/
  `universeId` parameters are added to both, resolved to nullable `Guid` (not raw strings), matching how
  every other filter value already arrives pre-resolved/pre-validated at this layer.
- **No existing `SqlQueryGuardTests.AssembledQueryCases` entries cover a Series/Universe filter shape** —
  confirmed by reading `tests/Quotinator.Data.Tests/Repositories/RepositorySqlGuardTests.cs` and the
  Engine-side equivalent; this issue's new `BuildFilterWhere` clause combinations need new cases added to
  whichever guard test actually enumerates `Sql.Quotes`'s dynamic factory methods (the issue's own
  "Expected tests" table already lists this as `SqlQueryGuardTests.AssembledQuery_PassesAggregateGuard`
  — confirm the exact test/file name during implementation, since this project has more than one
  SQL-guard test class and the issue's own reference may be imprecise).
- **`FakeQuoteService` (`tests/Quotinator.Api.Tests/Fakes/FakeQuoteService.cs`) implements `IQuoteService`
  and will need updating for the new interface parameters** — every `QuoteEndpointsTests` test that
  constructs a `FakeQuoteService` continues to compile only if the fake's method signatures track the
  real interface; this is a mechanical consequence of Step 6's interface change, not a design decision,
  flagged here so it isn't missed as a "surprise" compile error during implementation.

Conventions consumed, not re-decided: #196's `EntityFilterParsing`/`ApiMessages` keys, CLAUDE.md's
"Masterdata reference shape" and case-insensitivity conventions, and #206's already-merged
`Quotinator.Core.Models.MasterDataReference`. This issue makes no `MasterDataReference` design decision
of its own — everything here follows an already-established pattern to its first real application on
the quote read path.

---

## Steps

### 1. Confirm #206 has landed before starting

**Status:** ✅ Done. #206 merged and committed (`2d79f79`) before this issue's implementation began.

Hard prerequisite, not a step this issue implements: confirm `Quotinator.Core.Models.MasterDataReference`
exists and `Quotinator.Core` has a project reference to `Quotinator.Data` (both land via #206). If #206
has not yet merged, this issue cannot proceed — every step below assumes `QuoteResponse` can reference
`MasterDataReference` directly, with no workaround of any kind.

### 2. Add `Series`/`Universe` to `QuoteResponse`

**Status:** ✅ Done, exactly as designed.

```csharp
/// <summary>The series this quote's source belongs to, if any (#179), as a minimal read-only reference.
/// <c>null</c> for a standalone source, and <c>null</c> if the linked series has been soft-deleted (per
/// CLAUDE.md's "Soft-deleted rows are invisible by default" convention).</summary>
public MasterDataReference? Series { get; init; }

/// <summary>The universe this quote's series belongs to, if any (#179), as a minimal read-only
/// reference. <c>null</c> when there is no series, when the series has no universe, or when the linked
/// universe has been soft-deleted.</summary>
public MasterDataReference? Universe { get; init; }
```

Placed after `Genres` and before `Conversations` — matches the field's conceptual position (still
describing the quote's own attribution, not its conversation membership).

### 3. Extend `Sql.Quotes.SelectBase` with Series/Universe joins

**Status:** ✅ Done, exactly as designed (in `Sql.cs`'s post-#206 location, `Quotinator.Core.Queries`).

```csharp
private const string SelectBase = """
    SELECT
        q.Id,
        COALESCE(qt.QuoteText,  q.QuoteText)  AS QuoteText,
        q.OriginalLanguage,
        COALESCE(st.Title,      s.Title)       AS Source,
        s.Date,
        s.Type                                 AS SourceType,
        COALESCE(ct.Name,       c.Name)        AS Character,
        p.Name                                 AS Author,
        CASE WHEN qt.QuoteText IS NOT NULL THEN @lang ELSE q.OriginalLanguage END AS EffectiveLanguage,
        ser.Id                                 AS SeriesId,
        ser.Name                               AS SeriesName,
        uni.Id                                 AS UniverseId,
        uni.Name                               AS UniverseName
    FROM   Quotes          q
    JOIN   Sources         s  ON  s.Id  = q.SourceId                                          AND s.IsDeleted  = 0
    LEFT JOIN Characters   c  ON  c.Id  = q.CharacterId                                       AND c.IsDeleted  = 0
    LEFT JOIN People       p  ON  p.Id  = q.PersonId                                          AND p.IsDeleted  = 0
    LEFT JOIN QuoteTranslations    qt ON qt.QuoteId     = q.Id AND qt.Language = @lang        AND qt.IsDeleted = 0
    LEFT JOIN SourceTranslations   st ON st.SourceId    = s.Id AND st.Language = @lang        AND st.IsDeleted = 0
    LEFT JOIN CharacterTranslations ct ON ct.CharacterId = c.Id AND ct.Language = @lang       AND ct.IsDeleted = 0
    LEFT JOIN Series       ser ON ser.Id = s.SeriesId                                         AND ser.IsDeleted = 0
    LEFT JOIN Universe     uni ON uni.Id = ser.UniverseId                                     AND uni.IsDeleted = 0
    """;
```

Both joins are `LEFT` (per Spec requirement 3) — `ser` resolves to no row when `s.SeriesId` is `NULL` or
points at a soft-deleted Series; `uni` resolves to no row when `ser` itself didn't match (no Series) or
`ser.UniverseId` is `NULL` or points at a soft-deleted Universe. No `WHERE`-clause change is needed —
absence of a match simply leaves those four columns `NULL` for that row, which is exactly the desired
"no series/universe" result.

`SelectRawById()` (the untranslated merge/conflict-resolution query) is deliberately **not** touched —
it exists for a different purpose (rebuilding a field map to compare against an incoming import record)
and has no `QuoteResponse` in its call chain.

### 4. Series/Universe filter clauses in `BuildFilterWhere`

**Status:** ✅ Done, exactly as designed. The flagged id-casing uncertainty was resolved by direct
inspection before implementing: `data/sources/quotinator-series-universe.json` has zero explicit `"id"`
fields anywhere in the file — every Series/Universe row is auto-generated through the normal
entity-write path (unlike #68's hand-authored Conversations), so ids land uppercase via the standard
`GuidHandler` normalisation. `s.SeriesId = @seriesId` needed no `UPPER()` wrapper, confirmed by the
passing `GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes` test.

Both overloads gain two new nullable `Guid?` parameters:

```csharp
internal static (string Sql, object Parameters) BuildFilterWhere(
    string[]? types, string[]? genres, string? lang, Guid? seriesId, Guid? universeId,
    int? yearFrom = null, int? yearTo = null)
    => BuildFilterWhere(types, genres, lang, null, null, null, seriesId, universeId, yearFrom, yearTo);

internal static (string Sql, DynamicParameters Parameters) BuildFilterWhere(
    string[]? types, string[]? genres, string? lang,
    string? character, string? author, string? source,
    Guid? seriesId, Guid? universeId,
    int? yearFrom = null, int? yearTo = null)
{
    // ... existing dbTypes/dbGenres/clauses setup unchanged ...

    if (seriesId   is not null) clauses.Add("s.SeriesId = @seriesId");
    if (universeId is not null) clauses.Add(
        "s.SeriesId IN (SELECT Id FROM Series WHERE UniverseId = @universeId AND IsDeleted = 0)");

    // ... existing character/author/source/yearFrom/yearTo clauses unchanged ...

    if (seriesId   is not null) p.Add("seriesId",   seriesId);
    if (universeId is not null) p.Add("universeId", universeId);

    // ... rest unchanged ...
}
```

`s.SeriesId = @seriesId` needs no new `JOIN` — `s` (Sources) is already joined in every base query that
calls `BuildFilterWhere` (`SelectBase`, `CountForRandomBase`, `CountForGetAllBase`), and `SeriesId` is a
plain column on `Sources`. The Universe filter's subquery (per Spec requirement 5) likewise needs no new
`JOIN` in `CountForRandomBase`/`CountForGetAllBase` — it's a self-contained correlated condition against
the already-available `s.SeriesId`. This means `CountForRandomBase`/`CountForGetAllBase` need **no
changes at all** — only `SelectBase` (Step 3) gains the display-purpose `LEFT JOIN`s; the count queries
only need the filter *condition*, which doesn't require them.

`Guid`-typed parameters bind correctly without any special handling — the globally-registered
`GuidHandler` (`Program.cs`'s `QuotinatorDapperConfiguration().Configure()`) already normalises them to
uppercase TEXT on write, matching every Series/Universe id stored via the normal insert path (#180's
curated overlay data was seeded through the standard entity-write path, unlike #68's curated
Conversations, which were hand-authored JSON with preserved-verbatim lowercase ids — **verify this
distinction directly against `data/sources/quotinator-series-universe.json` before assuming
case-insensitive matching is unnecessary here**; if that file also carries hand-authored lowercase ids,
`s.SeriesId = @seriesId` needs `UPPER(s.SeriesId) = UPPER(@seriesId)` instead, mirroring #189's
after-the-fact T2 fix rather than repeating that discovery process here).

### 5. `ISeriesNameResolver` / `IUniverseNameResolver`

**Status:** ✅ Done, exactly as designed — in `Quotinator.Core.Repositories`, registered in `Program.cs`
alongside `IConversationLineCountReader` and its siblings.

New files `src/Quotinator.Core/Repositories/ISeriesNameResolver.cs`/`SeriesNameResolver.cs` and
`IUniverseNameResolver.cs`/`UniverseNameResolver.cs`, namespace `Quotinator.Core.Repositories` — the
folder every other reader introduced this milestone lives in once #206's merge lands (pre-#206 this
would have been `Quotinator.Engine.Repositories`; #206 folds that segment into Core):

```csharp
namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Series name to its active (non-deleted) id — backs the name-valued form of the
/// #196 entity-scoped filter convention for #192's quote-read-path Series filter.</summary>
public interface ISeriesNameResolver
{
    /// <summary>The active Series id with this exact name, or <c>null</c> if none exists.</summary>
    Task<Guid?> ResolveIdByNameAsync(string name);
}
```

Implementation wraps the existing `Sql.Series.SelectIdByName` const (no new SQL — see Background) via
`IDbConnectionFactory`, mirroring `ImportActionPlanner`'s own call shape but through a small class an
endpoint can inject rather than a raw connection. `IUniverseNameResolver`/`UniverseNameResolver` mirror
this exactly against `Sql.Universe.SelectIdByName`.

Register both in `Program.cs`:
```csharp
builder.Services.AddSingleton<ISeriesNameResolver, SeriesNameResolver>();
builder.Services.AddSingleton<IUniverseNameResolver, UniverseNameResolver>();
```

### 6. `IQuoteService`/`SqliteQuoteService` — new filter parameters

**Status:** ✅ Done, with two deviations from the original sketch. `QuoteRow.SeriesId`/`UniverseId` are
typed `string?`, not `Guid?` — matching the row's existing convention (`Id`, `Source`, `Character` are
all plain strings read directly from Dapper, no constructor-matching pitfalls), simpler than the
originally-sketched `Guid?` + `.ToString("D").ToUpperInvariant()` conversion. Also found and fixed:
`Quotinator.Core.Services.QuoteService` — the dead v1 in-memory `IQuoteService` implementation flagged
as out-of-scope dead code in #206's own plan — still had to gain the two new parameters on its three
methods to keep compiling (interface conformance), even though it never uses them (flat-file
`SourceQuote` has no Series/Universe concept). Not anticipated by this plan; a compile-time consequence
of the interface change, not a design decision.

`IQuoteService.cs` — `GetAll`, `GetRandom`, `Search` each gain two new optional parameters at the end of
their signature:
```csharp
Guid? seriesId = null,
Guid? universeId = null
```

`SqliteQuoteService.cs` — each of the three methods forwards `seriesId`/`universeId` into its
`BuildFilterWhere` call (Step 4). `ToResponse` (the shared `QuoteRow` → `QuoteResponse` mapper) builds
the two new `MasterDataReference?` fields from `QuoteRow`'s four new nullable columns:

```csharp
Series   = row.SeriesId   is { } sid ? new MasterDataReference(sid.ToString("D").ToUpperInvariant(), row.SeriesName!)   : null,
Universe = row.UniverseId is { } uid ? new MasterDataReference(uid.ToString("D").ToUpperInvariant(), row.UniverseName!) : null,
```

`QuoteRow` (the private row DTO, `SqliteQuoteService.cs:407`) gains:
```csharp
public Guid?   SeriesId     { get; init; }
public string? SeriesName   { get; init; }
public Guid?   UniverseId   { get; init; }
public string? UniverseName { get; init; }
```

### 7. Wire `EntityFilterParsing` into `QuoteEndpoints.cs`

**Status:** ✅ Done. Added one private shared helper, `ResolveSeriesUniverseAsync`, since the
resolve-both-filters step is identical across `GetAll`/`GetRandom`/`Search` (not in the original plan
sketch, which showed the resolution inline per handler) — each caller still builds its own
Error/NotFound response using its own envelope shape (`PagedResult<T>` for `GetAll`,
`FilteredQuoteResult<T>` for `GetRandom`/`Search`), so the helper only removes the ~8 duplicated lines
of resolving both filters, not the response-shaping logic itself. `GetAll`/`GetRandom`/`Search` are now
`async Task<IResult>` handlers (were synchronous `IResult`) to support the `await` this introduces.

`GetAll`, `GetRandom`, `Search` each gain four new optional query parameters:
```csharp
[Description("Filter to quotes in this Series, by id.")] string? seriesId = null,
[Description("Filter to quotes in this Series, by exact name. Mutually exclusive with `seriesId`.")] string? series = null,
[Description("Filter to quotes in this Universe (spans every Series in it), by id.")] string? universeId = null,
[Description("Filter to quotes in this Universe (spans every Series in it), by exact name. Mutually exclusive with `universeId`.")] string? universe = null,
```

Each endpoint resolves both pairs via `EntityFilterParsing.ResolveAsync` before calling into
`IQuoteService`:

```csharp
var seriesResult = await EntityFilterParsing.ResolveAsync(
    seriesId, series, new EntityFilterNames("Series", "seriesId", "series"),
    seriesNameResolver.ResolveIdByNameAsync, localizer);
if (seriesResult.Outcome == EntityFilterOutcome.Error) return seriesResult.Error!;

var universeResult = await EntityFilterParsing.ResolveAsync(
    universeId, universe, new EntityFilterNames("Universe", "universeId", "universe"),
    universeNameResolver.ResolveIdByNameAsync, localizer);
if (universeResult.Outcome == EntityFilterOutcome.Error) return universeResult.Error!;
```

A `NotFound` outcome on either (a name that resolves to nothing) is a legitimate zero-results case, not
an error — matching the existing `FilteredResultStatus.NoResults` envelope shape `GetRandom`/`Search`
already use for "no quotes match filters" (`GetAll` needs the equivalent treatment via `PagedItems<T>`'s
own empty-result shape, since it has no `FilteredQuoteResult<T>` envelope to reuse). Both endpoints then
pass `seriesResult.Id`/`universeResult.Id` (each `Guid?`, `null` when `Outcome == NoFilter`) into
`service.GetAll(...)`/`GetRandom(...)`/`Search(...)`.

`ISeriesNameResolver seriesNameResolver` and `IUniverseNameResolver universeNameResolver` become new
injected parameters on `GetAll`, `GetRandom`, and `Search`'s handler methods.

### 8. `FakeQuoteService` — track the interface change

**Status:** ✅ Done. Gave `Tolkien` (the existing book-type fixture) a `Series` ("The Lord of the
Rings") and `Universe` ("Middle Earth") — fitting given #169's original "Middle Earth" motivating
example — via two new named fixtures (`FakeQuoteService.MiddleEarthSeries`/`MiddleEarthUniverse`).
Also added `FakeSeriesNameResolver`/`FakeUniverseNameResolver` (not explicitly listed here, but a
direct consequence of Step 7's endpoint-level DI dependency — `QuoteEndpointsTests`' test host needs
something to inject for `ISeriesNameResolver`/`IUniverseNameResolver`), modelled on the existing
`FakeSourceSeriesReferenceReader` pattern.

### 9. Tests

**Status:** ✅ Done. All 8 `SqliteQuoteServiceTests` cases and all 6 `QuoteEndpointsTests` cases below
pass, plus the `SqlQueryGuardTests` matrix extension. Test names below match what was actually written
(identical to the original list, since it held up unchanged through implementation).

Per the issue's own "Expected tests" table (all confirmed still applicable after this review, plus
additions this review's own findings call for):

**`Quotinator.Core.Tests.Services.SqliteQuoteServiceTests`** (real SQLite, not fakes — needed to prove
the actual `LEFT JOIN` chain and `BuildFilterWhere` SQL, matching this project's standing "DB integration
tests required" rule):
- `GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth`
- `GetById_SourceWithNoSeries_ReturnsQuoteWithNullSeriesAndUniverse`
- `GetById_SeriesWithNoUniverse_ReturnsSeriesWithNullUniverse` (Series present, Universe null)
- `GetById_SeriesSoftDeleted_ReturnsNullSeriesAndUniverse` — added this review; proves the `LEFT JOIN`'s
  own `IsDeleted = 0` condition, not just "no Series linked at all"
- `GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes`
- `GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse`
- `GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes`
- `GetRandom_SeriesFilter_ReturnsOnlyThatSeriesQuotes` — added this review; the issue's own table only
  exercises the Universe filter on `/random`, not the Series filter, on the same endpoint

**`Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests`** (fake-backed, proving the endpoint-layer wiring —
parameter parsing, `EntityFilterParsing` mutual-exclusivity/malformed-id/not-found outcomes — not the SQL
itself):
- `GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes`
- `GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse`
- `GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes`
- `GetAll_BothSeriesIdAndSeriesSupplied_Returns422` — added this review; proves #196's mutual-exclusivity
  rule actually fires on a real endpoint, not just in `EntityFilterParsingTests`' own isolated unit tests
- `GetAll_SeriesNameDoesNotResolve_ReturnsNoResultsNotError` — added this review; proves the
  `NotFound`-is-not-an-error distinction end to end
- `GetAll_MalformedSeriesId_Returns422` — added this review; proves the id-form's own validation path

**Shared infrastructure:**
- `SqlQueryGuardTests`'s `AssembledQueryCases` (or whichever guard test enumerates `Sql.Quotes`'s dynamic
  factory methods — confirm the exact class during implementation, see Background) — new cases covering
  every new `seriesId`/`universeId` clause combination the `BuildFilterWhere` rewrite introduces.

### 10. Documentation

**Status:** ✅ Done.

Update `README.md`'s and `addon/DOCS.md`'s existing `/api/v1/quotes`, `/api/v1/quotes/random`, and
`/api/v1/quotes/search` rows to mention the new `seriesId`/`series`/`universeId`/`universe` filters (an
edit to existing rows, not new rows — these endpoints already exist). `QuoteEndpoints.cs`'s
`[Description]` attributes on the three `MapGet` registrations (Step 7's inline `[Description]`s on the
parameters themselves cover the parameter-level docs; the endpoint-level `.WithDescription(...)` calls
also need a sentence added about the new filters, matching how `character`/`author`/`source` are already
mentioned there).

### 11. Solution file

**Status:** ✅ Done — no action needed. Confirmed: SDK-style `.csproj` auto-includes all `.cs` files
under a project's directory, and per this milestone's established pattern, files landing inside an
existing project folder need no explicit `Quotinator.slnx` entry. All new files (2 resolvers × 2 files
in `Quotinator.Core/Repositories/`, 2 fakes in `Quotinator.Api.Tests/Fakes/`) fall into this category.

### 12. Verify

**Status:** ✅ Done. `dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test
--configuration Release --verbosity normal` → full suite green (`Quotinator.Core.Tests` 576,
`Quotinator.Api.Tests` 493 — up from #206's post-merge baseline of 560/487 by exactly the 16/6 new
tests added), 0 warnings, 0 errors.

T2 (Docker): `docker build` + `docker run`, then verified the filter/resolution plumbing against the
live container — all four cases from this plan's original curl matrix (both `seriesId`+`series` → 422;
malformed `seriesId` → 422; well-formed non-matching `universeId` → 200 empty; unresolvable `series`
name → 200 no-results, not an error) passed exactly as designed, plus an equivalent `/random` no-match
case (200 `NoResults` envelope). **One deviation from the original curl matrix, found live, not a
regression:** the "a quote whose Source is in a known Series carries a resolved series/universe object
with real names" check could not be exercised this way — #180's curated Series/Universe overlay file
seeds as 75 `Pending` staged actions under review policy by design (confirmed via container startup
log: "quotinator-series-universe.json left staged awaiting review"), and deciding all 75 individually
is impractical for a single T2 pass. That specific case (real, non-null Series/Universe data flowing
through `ToResponse`) is instead proven by the real-SQLite `SqliteQuoteServiceTests` — most directly
`GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth` and the two `*FilterReturnsOnlyThat*Quotes`
tests — which insert genuinely applied Series/Universe/Source rows directly, bypassing the staged-review
pipeline entirely. T2 here instead confirms what only a live container can: the DI graph resolves
`ISeriesNameResolver`/`IUniverseNameResolver` correctly, and the full request pipeline (validation →
resolution → SQL → response) runs without error against real SQLite. Also confirmed via
`GET /openapi/v1.json` that all four new parameters (`seriesId`, `series`, `universeId`, `universe`)
publish correctly on `/api/v1/quotes`.

This project always runs T2 regardless of a documented trigger — this issue's own change to `Program.cs`
(the two new resolver DI registrations) also independently satisfies `docs/release-verification.md`'s
"touches Program.cs startup" trigger.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `QuoteResponse` references `Quotinator.Core.Models.MasterDataReference` (already merged there by #206) — no new type, move, or duplicate introduced by this issue | Unit test | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 2 | ✅ | `QuoteResponse.Series`/`Universe` populated when the Source is in a Series (with/without a Universe) | Unit test | `SqliteQuoteServiceTests.GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth` |
| 3 | ✅ | `QuoteResponse.Series`/`Universe` are `null` when the Source has no Series | Unit test | `SqliteQuoteServiceTests.GetById_SourceWithNoSeries_ReturnsQuoteWithNullSeriesAndUniverse` |
| 4 | ✅ | `QuoteResponse.Universe` is `null` when the Series itself has no Universe | Unit test | `SqliteQuoteServiceTests.GetById_SeriesWithNoUniverse_ReturnsSeriesWithNullUniverse` |
| 5 | ✅ | A soft-deleted Series/Universe never leaks a dangling reference | Unit test | `SqliteQuoteServiceTests.GetById_SeriesSoftDeleted_ReturnsNullSeriesAndUniverse` |
| 6 | ✅ | `GET /api/v1/quotes?seriesId=`/`?series=` returns only that Series' quotes | Unit test | `SqliteQuoteServiceTests.GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes`, `QuoteEndpointsTests.GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes` |
| 7 | ✅ | A Universe filter matches quotes across every Series in it | Unit test | `SqliteQuoteServiceTests.GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse`, `QuoteEndpointsTests.GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse` |
| 8 | ✅ | `/random` and `/search` support the same Series/Universe filters, id- and name-valued | Unit test | `SqliteQuoteServiceTests.GetRandom_SeriesFilter_ReturnsOnlyThatSeriesQuotes`, `GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes`, `QuoteEndpointsTests.GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes` |
| 9 | ✅ | Supplying both the id- and name-valued form of the same filter returns 422 | Unit test | `QuoteEndpointsTests.GetAll_BothSeriesIdAndSeriesSupplied_Returns422` |
| 10 | ✅ | A malformed id-valued filter returns 422 | Unit test | `QuoteEndpointsTests.GetAll_MalformedSeriesId_Returns422` |
| 11 | ✅ | A name-valued filter that resolves to nothing returns a no-results response, not an error | Unit test | `QuoteEndpointsTests.GetAll_SeriesNameDoesNotResolve_ReturnsNoResultsNotError` |
| 12 | ✅ | `character`/`author`/`source` on `/random`/`/search` remain unchanged fuzzy contains-matches | Unit test | Existing `QuoteEndpointsTests`/`SqliteQuoteServiceTests` cases for those filters still pass unmodified |
| 13 | ✅ | New `BuildFilterWhere` clause shapes pass the SQL aggregate/injection guard | Unit test | `SqlQueryGuardTests.AssembledQuery_PassesAggregateGuard` (new `seriesId`/`universeId` cases) |
| 14 | ✅ | `README.md`/`addon/DOCS.md`/`[Description]` attributes document the new filters | Doc review | Files updated |
| 15 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 16 | ✅ | T1 — app starts in Visual Studio; a live quote shows resolved Series/Universe | Live (T1) | Developer confirmed 2026-07-19 — clean startup (schema v9/data v10 recognized), `/quotes/random` with `series=`/`universe=` filters all 200, `/masterdata/universes` 200. Also exercised an edge case not in the original test matrix: `?universe=` (present, empty value) binds as `""`, not `null` — `EntityFilterParsing.ResolveAsync` treats it as a genuine name-valued filter, resolves it against no matching Universe, and returns the legitimate no-results envelope rather than erroring. Correct, not a bug, just outside the originally-designed test coverage. |
| 17 | ✅ | T2 — the live filter/resolution plumbing holds against the built image (real-data case covered by unit tests instead — see Step 12's note) | Live (T2) | `docker build`/`docker run` matrix — see Step 12 |

---

## Notes

This plan went through three designs for `MasterDataReference` before landing on the right one: move the
existing Api-layer type into Core (wrong — #184/#185/#187 proved its old location worked fine); add a
duplicate Core-only type (wrong — every masterdata response DTO also needs
`Quotinator.Data.Entities.CompletenessStatus`, which neither Core nor a Core/Api split could reach at
once); and finally, recognizing the real fix was one level up — merge `Quotinator.Engine` back into
`Quotinator.Core` entirely (#206), after which `MasterDataReference` and every masterdata response DTO
already live in `Quotinator.Core.Models` with no split to work around. This issue makes zero
`MasterDataReference`-related changes as a result — it is purely a consumer of what #206 already put in
place.

This issue does not touch `Quotinator.Core.Models.ConversationResponse`/`ConversationLineResponse`,
`ConversationSummaryResponse`, or any of #184–#189/#204/#205's own masterdata list endpoints — it only
adds Series/Universe *enrichment and filtering* to the pre-existing quote read path
(`GetById`/`GetAll`/`GetRandom`/`Search`), which is a distinct concern from those five/six issues'
"enumerate Series/Universe/Conversations/etc as first-class entities" scope.

The Background's flagged uncertainty about `data/sources/quotinator-series-universe.json`'s id casing
(hand-authored lowercase, like #68's curated Conversations, vs. normally-seeded uppercase) must be
resolved by direct inspection before Step 4 is implemented, not assumed either way — #189's T2 pass found
exactly this kind of assumption wrong once already this milestone, on a structurally similar join.

---

## Corrected issue text (for a future `gh issue edit`)

```
## Background

#179 added the `Universe` → `Series` → `Source` schema and #180 populated it (75 Sources across 26
Series and 5 Universes, from a curated overlay file). Nothing reads it. `QuoteResponse` has no `series`
or `universe` field, and no endpoint filters on either — so the data #180 populates currently has no
read path from a quote at all.

This is the capability #169's research was originally motivated by — grouping related Sources so a
consumer can ask for "a random Star Wars quote" or display "from the Middle Earth universe" without
knowing which individual films exist. Today a consumer would have to fetch the quote, look up its Source
(#184), then that Source's Series (#187), then that Series' Universe (#188) — three extra round-trips to
answer a question the quote itself should be able to answer, and still no way to *filter* by either.

The masterdata list endpoints (#184/#187/#188) are a different concern: they expose Series/Universe as
first-class entities to enumerate. This issue is about enriching and filtering the quote read path, which
none of them touch.

**Depends on #180** (the data to expose), **#196** (this project's entity-scoped filter-parameter
convention — `{entity}Id`/`{entity}`, mutually exclusive, resolved via `EntityFilterParsing.ResolveAsync`
— which this issue consumes as its first real caller, not re-decides), and **#206** (merges
`Quotinator.Engine` back into `Quotinator.Core` — a project-boundary gap found while planning this issue
itself: `QuoteResponse` needs `MasterDataReference`, which needs `Quotinator.Data.Entities.
CompletenessStatus` to be reachable from the same place every masterdata response DTO already lives in;
that's only true once #206 lands).

Found while reviewing #180's T1 (2026-07-16): the developer's first observation on seeing a live quote
response was that it carries neither field.

## What needs to be done

1. Add `Series` and `Universe` to `QuoteResponse`, both nullable `Quotinator.Core.Models.
   MasterDataReference` (already the canonical home for that type and every masterdata response DTO
   once #206 lands — this issue introduces no new type, move, or duplicate of its own).
2. Extend `Sql.Quotes.SelectBase`'s projection with two `LEFT JOIN`s (Series, then Universe) and four new
   columns. Must be `LEFT` — a Source with no Series must still return its quote.
3. Add Series and Universe filters to `GET /api/v1/quotes`, `GET /api/v1/quotes/random`, and
   `GET /api/v1/quotes/search`, following #196's convention exactly (id-valued and name-valued, mutually
   exclusive, resolved to a `Guid` before the query runs) via two new small readers
   (`ISeriesNameResolver`/`IUniverseNameResolver`) wrapping the already-existing
   `Sql.Series.SelectIdByName`/`Sql.Universe.SelectIdByName` queries. The existing
   `character`/`author`/`source` filters on `/random`/`/search` are untouched and stay fuzzy — only the
   new Series/Universe filters follow the strict convention, per CLAUDE.md's explicit exemption wording.
4. A Universe filter must match quotes across every Series in that Universe (Quote → Source → Series →
   Universe), expressed as a subquery against `Sources.SeriesId` — no new `JOIN` needed in the count
   queries for this.
5. Update `README.md`, `addon/DOCS.md`, and `QuoteEndpoints.cs`'s `[Description]` attributes in the same
   commit.
6. Id-valued filters match case-insensitively for free via the existing `Guid`/`GuidHandler` pipeline —
   verify directly whether `data/sources/quotinator-series-universe.json`'s ids need the same
   `UPPER()`-in-SQL treatment #189 needed for its hand-authored curated data, rather than assuming either
   way.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_SourceInSeriesWithUniverse_ResponseCarriesBoth` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_SourceWithNoSeries_ReturnsQuoteWithNullSeriesAndUniverse` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_SeriesWithNoUniverse_ReturnsSeriesWithNullUniverse` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetById_SeriesSoftDeleted_ReturnsNullSeriesAndUniverse` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetRandom_SeriesFilter_ReturnsOnlyThatSeriesQuotes` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteQuoteServiceTests` | `GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetAll_SeriesFilter_ReturnsOnlyThatSeriesQuotes` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetAll_UniverseFilter_ReturnsQuotesAcrossEverySeriesInThatUniverse` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetRandom_UniverseFilter_ReturnsOnlyThatUniverseQuotes` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetAll_BothSeriesIdAndSeriesSupplied_Returns422` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetAll_SeriesNameDoesNotResolve_ReturnsNoResultsNotError` | ❌ |
| `Quotinator.Api.Tests.Endpoints.QuoteEndpointsTests` | `GetAll_MalformedSeriesId_Returns422` | ❌ |
| SQL query guard (exact class TBD — see plan doc Background) | new `BuildFilterWhere` cases for `seriesId`/`universeId` | ✅ (existing guard, new cases) |

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
