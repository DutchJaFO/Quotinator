# A season-attached quote is served correctly through a real container

**Smoke:** no
**Environment:** Fresh
**Traces to:** #375

## Preconditions

`quotinator-seasons.json` seeds the Avatar: The Last Airbender universe/series, three curated seasons
(Book One: Water / Book Two: Earth / Book Three: Fire), one episode Source ("The Boy in the Iceberg",
linked to Book One), and one quote resolved onto that episode Source. No unit test can prove this end
to end — the fake-backed `SourceEndpointsTests`/`SeasonEndpointsTests` stand in for the read path, but
nothing in the unit suite runs the actual bundled `quotinator-seasons.json` through the real seed
pipeline and back out through a live HTTP response.

> ### Building this document found a real bug the unit suite never could
>
> The unit tests (`SourceEndpointsTests`, `SeasonEndpointsTests`) all passed green against
> `FakeSourceSeasonReferenceReader`. The first live request against a real container — `GET
> /api/v1/masterdata/sources/{id}` for the episode Source — returned a `500`. Container logs named the
> cause exactly: `SourceSeasonReferenceRow`/`SourceSeasonReferencesBatchRow` declared their `Number`
> column as `int`, but SQLite's `INTEGER` affinity always reads back as `long` through
> Microsoft.Data.Sqlite, and Dapper's record-constructor materialization (used by
> `JoinQueryRepository<TResult>`, per ADR 017) requires an exact type match — unlike the generic
> repository's property-setter mapping, which narrows a `long` into an `int` implicitly. A fake
> substitutes the reader entirely, so it never exercises Dapper's own materialization; only a real
> `SqliteConnection` reading a real `INTEGER` column can surface this. Fixed by declaring `Number` as
> `long` on both records and narrowing to `int` at the one point `SourceSeasonReferenceReader` builds
> its own `int`-typed tuple contract — see `SourceSeasonReferenceRow.cs`'s own remarks for the full
> account, and `SourceSeasonReferenceReaderTests.cs` (real SQLite, mutation-verified) for the
> regression guard. This is the same class of gap `SourceSeriesReferenceReaderTests`' own remarks
> already name for a sibling reader: a fake-backed test cannot reach Dapper's materialization at all,
> only a real one can.
>
> This is Cause 1 from this suite's own three-cause framework above — the feature was broken, not the
> expectation — found by running the real steps below rather than trusting the unit suite's green run.

## Determinism

- **Copy the `-wal` and `-shm` sidecars** alongside `quotinatordata.db` before reading it with the DB
  inspector, for the same reason `import-and-staged-actions/10`'s Determinism section gives — the app
  runs in WAL mode and a bare `.db` copy can silently omit recently committed rows. The container is
  stopped first so the file is not being written mid-copy.
- **The season/episode identity is fixed content, not measured data.** Unlike `10`'s Source-date ratio,
  every value asserted here — the three season display names, the episode title, the quote text, the
  linked season number — comes from `data/sources/quotinator-seasons.json` verbatim, so the expected
  values are exact, not a range.
- **Ids are read from the running container, not hand-typed.** The episode Source's id is queried by
  title before it is used in a `GET /{id}` call — ids are derived at seed time (`EntityIdentity`) and
  are not fixed across a schema change the way the season numbers and titles are.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-22 --port 18622
$base = "http://localhost:18622/api/v1"
```

**Expected:** the app reports healthy — the bundled seed, including `quotinator-seasons.json`, has
finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. The three curated seasons are served with rendered display names

```powershell
$seasons = (Invoke-RestMethod "$base/masterdata/seasons?pageSize=0").items | Sort-Object number
$seasons | Select-Object number, title, subtitle, displayName, @{n='series';e={$_.series.name}}
```

**Expected:** three rows — `1 / Book One / Water / "Book One: Water"`, `2 / Book Two / Earth / "Book
Two: Earth"`, `3 / Book Three / Fire / "Book Three: Fire"` — each with `series.name` = "Avatar: The
Last Airbender".

**Observed (2026-09-03):**

```
number title      subtitle displayName      series
------ -----      -------- ------------      ------
     1 Book One   Water    Book One: Water   Avatar: The Last Airbender
     2 Book Two   Earth    Book Two: Earth   Avatar: The Last Airbender
     3 Book Three Fire     Book Three: Fire  Avatar: The Last Airbender
```

### 3. The episode Source carries its Season reference

```powershell
$episode = (Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items |
    Where-Object { $_.title -eq "The Boy in the Iceberg" }
$episode | Select-Object id, title, date, @{n='series';e={$_.series.name}}, @{n='season';e={$_.season.name}}
```

**Expected:** one row, `series` = "Avatar: The Last Airbender", `season` = "Book One: Water" — the
rendered display name, not the raw number.

**Observed (2026-09-03):**

```
id                                   title                   date       series                      season
--                                   -----                   ----       ------                      ------
b6619e6b-6105-344c-a513-6cf190788ca4 The Boy in the Iceberg  2005-02-21 Avatar: The Last Airbender  Book One: Water
```

This is the request that returned `500` before the fix in Preconditions — `GetAllSources_...` above and
`GetById` below both drive the same reader.

### 4. `GET /{id}` for the episode Source, individually

```powershell
Invoke-RestMethod "$base/masterdata/sources/$($episode.id)" | ConvertTo-Json -Depth 5
```

**Expected:** the same `season` reference as step 3, from the single-Source read path
(`SourceSeasonReferenceRow`) rather than the batched one (`SourceSeasonReferencesBatchRow`) — both were
broken the same way before the fix, so both are exercised here.

**Observed (2026-09-03):**

```json
{
  "id": "b6619e6b-6105-344c-a513-6cf190788ca4",
  "title": "The Boy in the Iceberg",
  "type": "tv",
  "date": "2005-02-21",
  "series": { "id": "90de0820-bc3c-a347-b554-316c33ce7c3d", "name": "Avatar: The Last Airbender" },
  "season": { "id": "d6a15410-0de6-3848-9537-388684a68b60", "name": "Book One: Water" },
  "completenessStatus": "NeedsReview"
}
```

### 5. The quote itself resolves through search to the episode Source

```powershell
$hit = (Invoke-RestMethod "$base/quotes/search?q=Midnight+Sun+Madness&field=quote").items
$hit | Select-Object source, character, date, @{n='series';e={$_.series.name}}, @{n='universe';e={$_.universe.name}}
```

**Expected:** one match, `source` = "The Boy in the Iceberg" (the episode, not the show), `character` =
"Sokka", `series`/`universe` populated — the curated-overlay-then-resolved-quote path end to end.

**Observed (2026-09-03):**

```
source                  character date       series                      universe
------                  --------- ----       ------                      --------
The Boy in the Iceberg  Sokka     2005-02-21 Avatar: The Last Airbender  Avatar
```

`QuoteResponse` itself carries no `season` field — the season is reached through the quote's `source`
(step 3/4), consistent with `MasterDataReference`'s "enough to display, fetch the rest from that
entity's own endpoint" precedent (CLAUDE.md's masterdata reference shape).

### 6. Cross-check the link at the database level

```powershell
docker stop -t 15 qt-import-22
docker cp qt-import-22:/data/quotinatordata.db .claude/temp/inspect-22.db
docker cp qt-import-22:/data/quotinatordata.db-wal .claude/temp/inspect-22.db-wal 2>$null
docker cp qt-import-22:/data/quotinatordata.db-shm .claude/temp/inspect-22.db-shm 2>$null
docker start qt-import-22
dotnet script scripts/testing/http.csx -- --url "$base/health" --wait-for 200 --status

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-22.db" `
  --sql "SELECT s.Title AS Source, se.Number, se.Title AS SeasonTitle, se.Subtitle FROM Quotinator_Source s JOIN Quotinator_Season se ON se.Id = s.SeasonId WHERE s.IsDeleted = 0"
```

**Expected:** one row — the API responses in steps 3–5 are not reporting something the schema itself
does not actually contain.

**Observed (2026-09-03):**

```
Source                  Number  SeasonTitle  Subtitle
The Boy in the Iceberg  1       Book One     Water
```

## Observed effect

The full chain — curated `seasons[]`/`seriesName`/`seasonNumber` declarations, the staged-import
`PlanSeasonsAsync` path, the `Quotinator_Season` table, `Quotinator_Source.SeasonId`, both
`ISourceSeasonReferenceReader` read paths, and `SeasonDisplay.Format` — is exercised end to end by a
real container built from the current image, and returns the expected episode/season/series/universe
values with no divergence from what `quotinator-seasons.json` declares. The one genuine defect this
surfaced (Preconditions) was fixed before this document's own steps were captured as passing; the
`500` above is preserved in the document because it is the reason this test exists, not despite it.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-22
Remove-Item .claude/temp/inspect-22.db, .claude/temp/inspect-22.db-wal, `
            .claude/temp/inspect-22.db-shm -ErrorAction SilentlyContinue
```
