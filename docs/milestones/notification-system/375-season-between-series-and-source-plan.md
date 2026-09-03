# #375 — A quote from a multi-season TV series cannot say which season it is from

**Status:** Planning
**GitHub issue:** #375
**Tiers required:** T1, T2
**Depends on:** nothing

---

## Description

A quote attaches to a Source, and for a TV series that Source is the whole show — so a quote from a
later season has nowhere to say so. `Quotinator_Quote` has no `Date` of its own;
`Sql.Quotes.SelectRawById` reads a quote's `source` and `date` from `s.Title AS Source, s.Date`, and a
`tv` Source's `Date` is the series' start year. The year an import file carries per quote is discarded.

Four bundled `tv` titles already carry quotes from more than one year — `Arrow` 2015/2017,
`Game of Thrones` 2011/2012, `Mr. Robot` 2015/2017, `The Good Place` 2018/2019 (measured 2026-09-03).
They are neither competing release dates nor typos: they are quotes from different seasons of one show.

**The hierarchy gains a level: `Universe → Series → (Season) → Source`** (developer, 2026-09-03), with
Season optional and deliberately neutral — "tv-series are the first that have the 'season' concept, but
we should be sure to keep the concept neutral as that allows us to apply it to other material (like
magazines and podcasts) that group episodes." **A Source is always the episode.**

**#374 depends on this.** Its `UNIQUE (Title, Type, Date)` is table-wide, so without seasons it splits
one show into a Source row per year, and its date-correction step would then delete the season year to
tidy the duplicate away.

---

## Scope changes

**2026-09-03 — rejected: making a quote's parent nullable.** With Source defined as the episode and no
episode data in the bundled corpus, this plan first proposed making `Quote.SourceId` nullable and adding
`Quote.SeasonId`, so a quote could attach to whichever container was known. Overruled (developer): "we
solved this situation when we added the 'series' and 'universe' concepts… by ensuring we had those set
before we imported additional data." The missing episode is a *content* gap, not a schema one, and this
project already has the mechanism — `quotinator-series-universe.json` is `"quotes": []` plus reference
data, wired into `manifest.json` ahead of every bulk file. Seasons and episodes are curated the same
way, so a quote always has a real episode to attach to.

That rejection is what keeps this issue small. `Quote.SourceId` stays `NOT NULL`, every quote read path
keeps its `INNER JOIN Quotinator_Source`, and no response shape changes for non-episodic material.

**Not this issue:** #374's own key change and date corrections. This issue only has to make seasons
exist and be attachable before that lands.

---

## Cross-check against authoritative sources, 2026-09-03

Per `docs/workflow/process.md`'s Planning step 3.

1. **ADR 011 owns the hierarchy this changes, so an ADR is part of the work.** It fixes
   Universe → Series → Source as one-to-many at both levels, with Simplicity ranked above
   Extensibility, and says a Source belongs to at most one Series. Inserting Season is exactly the kind
   of decision it exists to record; the new ADR extends it rather than superseding it.

2. **ADR 002 gives the entity shape, and settles the natural key question.** Surrogate `Guid Id` plus a
   `UNIQUE` constraint carrying the natural key. Season's differs from its siblings: `Quotinator_Series`
   and `Quotinator_Universe` both use a globally unique `Name`, which cannot work for an ordinal that
   only means something within its parent — hence `UNIQUE (SeriesId, Number)`.

3. **ADR 008 does not apply.** Season adds no enum-backed column beyond the `CompletenessStatus` its
   siblings already carry, whose CHECK is copied unchanged from `Quotinator_Series`.

4. **ADR 017 governs the only new data access.** Resolving a Season's Series, and a Source's Season,
   into a `MasterDataReference` is a join, so it goes through `JoinQueryRepository`/`IJoinStrategy` and
   returns plain tuples — never a hand-rolled `connection.QueryAsync`.

5. **No repository is written.** `Program.cs:452-453` registers
   `IListableRepository<SeriesEntity>`/`<UniverseEntity>` against the generic
   `SqliteRepository<T>`; Season takes the same registration. Checked because "add an entity" reads
   like "add a repository", and here it is not (developer, 2026-09-03: "do not improvise repositories,
   check if our existing generics apply").

6. **The extended source schema already has the declaration arrays to mirror.**
   `schemas/source-extended.schema.json` defines `quotes`, `sources`, `series` and `universe`;
   `quotinator-series-universe.json` uses `series`/`universe`/`sources` with `"quotes": []`. A
   `seasons` array is the same shape, and `SourceEntryDto.SeriesName` is `Optional<string>` — the #190
   absent-vs-null carrier `seasonNumber` must mirror.

7. **The manifest already orders reference data before bulk content.**
   `quotinator-series-universe.json` sits at manifest position 2, `NikhilNamal17_popular-movie-quotes.json`
   at 3. Season reference data needs no new ordering mechanism, only an entry in the same place.

8. **Attaching a quote to an episode needs no new mechanism either.** A per-quote `Custom`
   `ConflictResolutionRule` keyed on the quote's own id already rewrites a quote's `source` — it is how
   the Anakin and Galadriel corrections work today. Setting `source` and `date` together resolves the
   quote to the episode's Source.

9. **`docs/workflow/source-verification.md` has a real gap this issue must close.** Its tiers govern
   titles, dates and which work an entry refers to — not whether a quote exists or which episode it is
   from. IMDb is already Tier 1 and publishes per-title quote pages; the procedure needs to say so
   before requirement 13's curated quotes can follow it.

10. **`Quotinator.Data` must stay domain-agnostic** (ADR 004). `SeasonEntity` and its endpoints are
    `Quotinator.Core`/`Quotinator.Api` concerns; nothing about a season goes into `Quotinator.Data`.

11. **Vocabulary.** `Season`, and whatever the rendered "Book One: Water" form is called, are new
    domain terms and go in `docs/vocabulary.md` in the same commit, per CLAUDE.md.

---

## Steps

### 1. Decide where a Season lives, and write the ADR

**Status:** ⬜ Not started

`Universe → Series → (Season) → Source` is settled; what the ADR records is *why* a fourth level rather
than a Source attribute, what keeps the concept neutral (no `Type` gate, no TV-specific column, an
ordered grouping of Sources within a Series), and that a Source is always the episode. It extends
ADR 011 rather than superseding it, and is added to `docs/architecture-decisions/README.md`'s index in
the same commit.

### 2. Write every test first, and run them red

**Status:** ⬜ Not started

Per `docs/testing-policy.md`. Covers the migration's drift tests as much as the entity's behaviour: the
baseline and the incremental replay must agree about a table that does not exist yet, which is a test
that can be written before the table is.

### 3. The Season entity and its migration

**Status:** ⬜ Not started

`Quotinator_Season` mirroring `Quotinator_Series` — `Id`, nullable `SeriesId` FK, `ImportBatchId`,
`CompletenessStatus` with its copied CHECK, `NoValueKnown`, RecordBase — plus `Number` (required) and
optional `Title` and `Subtitle`. `UNIQUE (SeriesId, Number)`, not a global `Name`.
`EntityIdentity.SeasonId(seriesId, number)` takes the parent id, as `CharacterId` already takes
`sourceId`. `CREATE TABLE IF NOT EXISTS` is idempotent, so this needs no rebuild; the baseline gains the
same table in the same commit and both drift tests are extended.

Registration is one line against the existing generic — see cross-check 5.

### 4. Source gains its Season link

**Status:** ⬜ Not started

`Quotinator_Source.SeasonId`, nullable, `REFERENCES Quotinator_Season(Id)` — an `ALTER TABLE … ADD
COLUMN`, which SQLite supports and which carries no CHECK, so no rebuild. `SourceEntity` gains the
matching `Guid?`. A Source keeps its existing `SeriesId`: a film in a trilogy has a Series and no
Season, an episode has both.

### 5. The import shape: `seasons[]` and `seasonNumber`

**Status:** ⬜ Not started

A `seasons[]` declaration array in `schemas/source-extended.schema.json` and its DTO, mirroring
`series[]`/`universe[]`, carrying number, title, subtitle and the series it belongs to. `SourceEntryDto`
gains `seasonNumber` as `Optional<int>` — the number identifies the season within the series the entry
already names, so no name matching is involved.

### 6. Curate the Avatar reference data

**Status:** ⬜ Not started

The worked example, and the only bundled content that exercises number + title + subtitle together.
Verified 2026-09-03 against two Tier 1 sources:

| | Verified |
|---|---|
| Series | Avatar: The Last Airbender, 2005 animated series. **A live-action series of the same title exists (2024)** — every entry must pin the 2005 one |
| Season 1 | `Number` 1, `Title` "Book One", `Subtitle` "Water" — 2005-02-21 to 2005-12-02, 20 episodes |
| Season 2 | `Number` 2, "Book Two" / "Earth" — 2006-03-17 to 2006-12-01, 20 episodes |
| Season 3 | `Number` 3, "Book Three" / "Fire" — 2007-09-21 to 2008-07-19, 21 episodes |
| First episode | "The Boy in the Iceberg", S1.E1, aired 2005-02-21 (IMDb `tt0801470`) — its air date matching Wikipedia's Book One start date is the two sources agreeing |

Sources: [season 1](https://en.wikipedia.org/wiki/Avatar:_The_Last_Airbender_season_1),
[season 2](https://en.wikipedia.org/wiki/Avatar:_The_Last_Airbender_season_2),
[season 3](https://en.wikipedia.org/wiki/Avatar:_The_Last_Airbender_season_3),
[the 2024 series](https://en.wikipedia.org/wiki/Avatar:_The_Last_Airbender_(2024_TV_series)),
[tt0801470](https://www.imdb.com/title/tt0801470/).

The quotes themselves come from IMDb's per-title quotes page for each episode, verbatim, with the
character named there. Step 10 is what makes that a documented source rather than a choice made here.

### 7. Attach the four bundled shows' quotes to their episodes

**Status:** ⬜ Not started

Per-quote `Custom` conflict rules setting `source` and `date` together, so each quote resolves to its
episode's Source. Which episode each quote is from is researched per `source-verification.md` as
extended by step 10. This is the step whose size is set by research rather than by code, and it is the
one that can be staged if it runs long — the model works without it; the four shows simply stay
attached at show level until it lands.

### 8. The masterdata endpoints

**Status:** ⬜ Not started

`GET /api/v1/masterdata/seasons` and `/{id}`, tagged `ApiTags.MasterData` (already declared, so ADR 020
needs no entry), `.WithName`/`.WithSummary` per the List/GetById convention with each name a
`private const string` shared with its logging tag. The full pagination contract including all eight
cases of the required matrix, and `page`/`pageSize` registered in
`NumericParameterSchemaTransformer.NumericParamsByPath` with the path.

### 9. A Source's response carries its Season

**Status:** ⬜ Not started

A `MasterDataReference?` alongside its existing Series reference, and a Season's own response carries
its Series the same way. Both resolved by a batched reader per ADR 017 — one query per page, never one
per row.

### 10. Document how a quote's text and episode are verified

**Status:** ⬜ Not started

`source-verification.md` gains the case its tiers do not cover. IMDb is already Tier 1 and publishes
per-title quote pages; the procedure states that this is the source for a quote's text and its episode,
and that the episode's own air date is cross-checked against the season's range as corroboration — which
is what caught nothing wrong here but would catch an episode attributed to the wrong season.

### 11. Docs, vocabulary, and the boyscout pass

**Status:** ⬜ Not started

`docs/api-endpoints.md` and the endpoints' `[Description]` attributes in the same commit as step 8.
`docs/vocabulary.md` gains Season. Every file this issue touches joins the scoped `IDE0008` list in
`.editorconfig` the moment it is first touched, per CLAUDE.md's "Variable declarations".

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A Source declaring a season number is linked to that season | Unit test | `DatabaseInitializerTests.ImportingASourceWithASeasonNumber_LinksItToThatSeason` |
| 2 | ❌ | A Source declaring no season is linked to none | Unit test | `DatabaseInitializerTests.ImportingASourceWithNoSeasonNumber_LinksItToNoSeason` — the control; the link is optional and this is what proves it |
| 3 | ❌ | Two series each with a season 1 are two distinct seasons | Unit test | `DatabaseInitializerTests.TwoSeriesEachWithSeasonOne_AreDistinctSeasons` — the per-parent natural key; a global key would collapse them |
| 4 | ❌ | Two seasons with the same number under different series get distinct ids | Unit test | `EntityIdentityTests.SeasonId_SameNumberUnderDifferentSeries_DiffersById` — row 3's failure mode one layer down, where the natural key admits both and the primary key rejects one |
| 5 | ❌ | A movie quote is unaffected | Unit test | `DatabaseInitializerTests.ImportingAMovieQuote_IsUnaffectedBySeasonSupport` |
| 6 | ❌ | `Quote.SourceId` is still `NOT NULL` and every quote still resolves a Source | Unit test | the schema drift test plus an assertion over `Sql.Quotes.SelectBase` keeping its inner join — the rejected design's failure mode, asserted so it cannot creep back |
| 7 | ❌ | A quote ruled to an episode resolves to that episode's Source | Unit test | `DatabaseInitializerTests.AQuoteRuledToAnEpisode_ResolvesToThatEpisodesSource` — the curated-overlay-then-bulk-import path end to end |
| 8 | ❌ | A season renders its number, title and subtitle together | Unit test | `DatabaseInitializerTests.AvatarSeasonRendersBookOneWater_FromNumberTitleAndSubtitle` |
| 9 | ❌ | A number-only season renders without them | Unit test | `DatabaseInitializerTests.ANumberOnlySeason_RendersWithoutTitleOrSubtitle` — the control for row 8 |
| 10 | ❌ | The migration and the baseline produce an identical `Quotinator_Season` schema | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`, extended |
| 11 | ❌ | The same holds for `Quotinator_Source` after its new column | Unit test | the same test — an `ADD COLUMN` is where a default or a reference silently differs between the two paths |
| 12 | ❌ | Both paths accept the same `CompletenessStatus` values on the new table | Unit test | the CHECK-constraint drift test, extended — `PRAGMA table_info` does not capture CHECK behaviour |
| 13 | ❌ | The seasons list and get-by-id return the expected payload, and an unknown id is a 404 | Unit test | `SeasonEndpointsTests` |
| 14 | ❌ | The list endpoint satisfies all eight pagination cases | Unit test | `SeasonEndpointsTests`, the standard matrix |
| 15 | ❌ | `pageSize = 0` returns every row at the repository level | Unit test | `SeasonReaderTests.GetPagedAsync_PageSizeZero_ReturnsEveryRow` — an endpoint test against a stub cannot catch a literal `LIMIT 0` |
| 16 | ❌ | A Source's Season is a `MasterDataReference`, resolved in one query per page | Unit test | `SourceEndpointsTests` — the reference shape and the N+1 guard together |
| 17 | ❌ | A Season's own Series is a `MasterDataReference` | Unit test | `SeasonEndpointsTests` |
| 18 | ❌ | A soft-deleted Season does not appear as a reference | Unit test | the join filters the referenced table, per CLAUDE.md's soft-delete rule — a reference must resolve to null, not to a deleted row |
| 19 | ❌ | The live spec publishes seasons' `page`/`pageSize` as integer with their defaults | Unit test | `OpenApiSpecEndpointTests` — the transformer's own unit tests pass even when it is never registered |
| 20 | ❌ | Every tag the seasons endpoints use is declared with a description | Unit test | `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` (existing) — expected to pass unchanged, since `MasterData` is already declared |
| 21 | ❌ | Every new test is red against the pre-change build | Test run | run at step 2 |
| 22 | ❌ | The Avatar seasons and first episode match their cited sources | Recorded rationale | step 6's table, each row citing its Wikipedia or IMDb URL |
| 23 | ❌ | Every curated Avatar quote cites the IMDb page it came from, verbatim | Recorded rationale | per `source-verification.md` as extended by step 10 |
| 24 | ❌ | The verification procedure states how a quote's text and episode are established | Unit test | assertion over `source-verification.md`'s own text, so the gap cannot silently reopen |
| 25 | ❌ | A real container serves an episode-attached quote through the API | Automated (T2) | a new `docs/automated-testing/` document — the seeded corpus end to end, which no unit test covers |
| 26 | ❌ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 27 | ❌ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 28 | ❌ | The behaviour is correct on the developer's own machine | Live (T1) | the seasons endpoints return the Avatar seasons, and a quote from an episode reports that episode |

**Row 6 is the rejected design asserted as a guard.** Making a quote's parent nullable was considered
and overruled; a test that fails the moment `SourceId` becomes nullable or the join becomes a `LEFT
JOIN` is what keeps that decision from being quietly reversed by a later change.

**Rows 2, 5 and 9 exist because "seasons work" is satisfied by a change that alters everything.** A
Season link applied unconditionally, or a renderer that always emits a title, would pass rows 1 and 8
and break every non-episodic row in the database.

**Row 22 is a recorded rationale rather than a test because the claim is about the world, not the
code** — the same treatment `source-verification.md` already prescribes for a title or date correction.
