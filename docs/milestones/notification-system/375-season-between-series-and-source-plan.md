# #375 — A quote from a multi-season TV series cannot say which season it is from

**Status:** In progress (verification checklist)
**GitHub issue:** #375
**Tiers required:** T1, T2
**Depends on:** nothing

---

## Description

A quote attaches to a Source, and for a TV series that Source is the whole show — so a quote from a
later season has nowhere to say so. `Quotinator_Quote` has no `Date` of its own;
`Sql.Quotes.SelectRawById` reads a quote's `source` and `date` from `s.Title AS Source, s.Date`, and a
`tv` Source's `Date` is the series' start year. The year an import file carries per quote is discarded.

Four bundled `tv` titles carry quotes claiming more than one year — `Arrow` 2015/2017,
`Game of Thrones` 2011/2012, `Mr. Robot` 2015/2017, `The Good Place` 2018/2019 (measured 2026-09-03).
Those quotes do come from different seasons, but **the years are not what says so**: every one that
resolved to an episode aired in a different year than the file claims (2012 → 2016, 2017 → 2016, 2017 →
2015). The year is bad data. The quote text is the only thing that identifies the episode, and the
episode is what establishes the season — see step 7, where that lookup is already done.

**The hierarchy gains a level: `Universe → Series → (Season) → Source`** (developer, 2026-09-03), with
Season optional and deliberately neutral — "tv-series are the first that have the 'season' concept, but
we should be sure to keep the concept neutral as that allows us to apply it to other material (like
magazines and podcasts) that group episodes."

**A quote attaches to the nearest Source we can find** (developer, 2026-09-03). An episode where the
episode is known, the show otherwise. "If we can find the precise series or episode that is a bonus,
but not critical at this stage… we do not expect all quotes to be perfectly attributed." That is what
keeps `Quote.SourceId` non-nullable without inventing placeholder rows: the show-level Source every
`tv` quote already points at stays exactly where it is, and episode Sources are added beside it for the
quotes that can reach one.

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

**2026-09-03 — a Source is the nearest work we can identify, not always the episode.** An earlier
answer in this thread was "a Source is always the episode." The data gathered for step 7 then showed
three of seven quotes cannot be tied to an episode from a Tier 1 source at all, which under that rule
would leave them with nothing to attach to — the nullable-parent gap arriving from the data side.
Resolved (developer): "we attach quotes to the nearest source we can find… we do not expect all quotes
to be perfectly attributed. We may encounter more as we evaluate the data we have. This simply gives us
examples that need more research in the data enhancement milestone."

So the granularity of a Source varies by what is known, deliberately. Nothing migrates: an unattributed
`tv` quote keeps the show-level Source it already has, and gaining an episode later is an improvement to
that row, not a repair of a broken one. **Imperfect attribution is an expected steady state here, not a
defect**, and the residue is material for the data enhancement milestone rather than a blocker for this
issue.

This follows from the second of the three principles governing both issues (developer, 2026-09-03,
recorded in full in #374's plan): seeding has no pending results when done; seeding does not guarantee
the data is 100% complete and accurate; the rules exist to enhance incoming results for sources we do
not control. The first two are separate guarantees, and this issue only owes the first.

**Not this issue:** #374's own key change and date corrections. This issue only has to make seasons
exist and be attachable before that lands.

---

## Cross-check against authoritative sources, 2026-09-03

Per `docs/workflow/process.md`'s Planning step 3.

1. **ADR 011 owns the hierarchy this changes, and is revised in place rather than superseded.** Its
   Decision 1 fixes Universe → Series → Source as one-to-many at both levels, with Simplicity ranked
   above Extensibility. Inserting Season modifies that rule inside that ADR's own subject, which
   `docs/architecture-decisions/README.md` settles: "When a decision is refined, edit the affected
   section in place so the ADR reads as one current statement. Do not append a `## Revision — issue #N`
   section" — and no new ADR, since stripped of narrative a new one would restate ADR 011 almost
   entirely. No new index row follows; only ADR 011's own title changes.

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

### 1. Decide where a Season lives, and record it in ADR 011

**Status:** ✅ Done, 2026-09-03

**Revised ADR 011 in place; no ADR 021.** The plan originally said the ADR "is added to
`README.md`'s index", which presumed a new document. `docs/architecture-decisions/README.md` settles it
the other way: a refinement is edited into the affected section so the ADR reads as one current
statement, with no `## Revision` section and no second document to assemble the rule from. Inserting
Season modifies ADR 011's Decision 1 — the hierarchy — inside ADR 011's own subject, so a new ADR
stripped of narrative would have restated it almost entirely. Only the ADR's own title changed in the
index.

What the revision records: the hierarchy as `Universe → Series → Season → Source`, one-to-many at every
level with every parent FK nullable; that a Season is an ordered grouping of Sources within a Series and
is **not** television-specific, with nothing keyed off `Source.Type`; that a Source's granularity
follows what can be established, so a quote always has one to point at and `Quote.SourceId` stays
non-nullable; and that `Season` is keyed `UNIQUE (SeriesId, Number)` rather than on a globally unique
name. Decision 3 extends to Season, including that it takes the generic repository and the standard
masterdata route rather than anything bespoke.

Two consequences were added: that a Season is what lets one serialised work hold quotes from more than
one part without collapsing them or splitting the work, and that a consumer must not infer granularity
from a Source's presence — the accepted cost of never leaving a quote without a Source.

### 2. Write every test first, and run them red

**Status:** 🚧 In progress

Per `docs/testing-policy.md`, whose "Red first means signatures first" section this step's own failure
prompted: a test referencing a type that does not exist yet breaks the build rather than going red, so
each piece is **signature → test → watch it fail → implement**, and the signature carries no behaviour.
The plan originally said only "write every test first", which is unachievable as stated in a compiled
language and is where `EntityIdentity.SeasonId` was implemented before its own tests existed.

Covers the migration's drift assertions as much as the entity's behaviour. Note that a drift test alone
is not red here: with no `Quotinator_Season` in either the baseline or the replay the two agree
trivially, so the red assertion is that the table exists with its expected columns and constraint, not
that the two paths match.

Progress:

| Signature | Tests | State |
|---|---|---|
| `SeasonEntity` | — | created |
| `EntityIdentity.SeasonId` | 4 in `EntityIdentityTests` | green, **mutation-verified rather than observed red** — the slip that prompted the policy section |
| — (schema only) | 3 in `DatabaseInitializerTests` | 2 red on the missing table and column; `Quote_StillRequiresASource` green as its control |
| `SeasonDisplay.Format` | 5 in `SeasonDisplayTests` | red on `NotImplementedException` |
| `SeasonEntryDto`, `SourceEntryDto.SeasonNumber` | 4 import-linking in `DatabaseInitializerTests` | red on the missing table and column |
| `SeasonResponse`, `ISeasonSeriesReferenceReader`, 2 fakes | 17 in `SeasonEndpointsTests` | 15 red — no endpoint is mapped yet |

**Two of the seventeen pass for a weak reason, and are known to.** `GetSeasonById_UnknownId_Returns404`
and `..._MalformedId_Returns404NotBadRequest` are satisfied by the route not existing at all. They are
kept because they become meaningful once the endpoint exists, and every positive case beside them is
red — but on their own they would prove nothing, which is the "only-failures" shape the testing policy
warns about.

Remaining: `SeasonReaderTests` (the repository-level `pageSize = 0` case, which needs the real reader),
the `SourceEndpointsTests` Season-reference and N+1 cases, `OpenApiSpecEndpointTests`, and the
`source-verification.md` text assertion.

### 3. The Season entity and its migration

**Status:** ✅ Done, 2026-09-03

`Quotinator_Season` mirroring `Quotinator_Series` — `Id`, nullable `SeriesId` FK, `ImportBatchId`,
`CompletenessStatus` with its copied CHECK, `NoValueKnown`, RecordBase — plus `Number` (required) and
optional `Title` and `Subtitle`. `UNIQUE (SeriesId, Number)`, not a global `Name`.
`EntityIdentity.SeasonId(seriesId, number)` takes the parent id, as `CharacterId` already takes
`sourceId`. `CREATE TABLE IF NOT EXISTS` is idempotent, so this needs no rebuild; the baseline gains the
same table in the same commit and both drift tests are extended.

Registration is one line against the existing generic — see cross-check 5.

### 4. Source gains its Season link

**Status:** ✅ Done, 2026-09-03

`Quotinator_Source.SeasonId`, nullable, `REFERENCES Quotinator_Season(Id)` — an `ALTER TABLE … ADD
COLUMN`, which SQLite supports and which carries no CHECK, so no rebuild. `SourceEntity` gains the
matching `Guid?`. A Source keeps its existing `SeriesId`: a film in a trilogy has a Series and no
Season, an episode has both.

### 5. The import shape: `seasons[]` and `seasonNumber`

**Status:** ✅ Done, 2026-09-03

A `seasons[]` declaration array in `schemas/source-extended.schema.json` and its DTO, mirroring
`series[]`/`universe[]`, carrying number, title, subtitle and the series it belongs to. `SourceEntryDto`
gains `seasonNumber` as `Optional<int>` — the number identifies the season within the series the entry
already names, so no name matching is involved.

Delivered as the full staged-import path, not a shortcut: `PlanSeasonsAsync` (Add, natural-key match,
Modify with the same conflict-rule and CompletenessGuard handling its siblings have), an apply branch
with an idempotent `EnsureSeasonExistsAsync`, and `Sql.Season`. Season is planned after Series so a
`seriesName` resolves against an already-built index, and before Sources so a `seasonNumber` resolves
against an already-built season index.

**Two defects found while wiring it, neither of which a compiler or an existing test would have caught:**

1. **A silently cleared Season link.** `Sql.Sources.UpdateFieldsById` writes every field it names, and
   the quote-driven date backfill builds its payload from `SelectExistingByTitleAndType` — which did not
   return `SeasonId`. Any later quote touching a season-linked Source would have written `NULL` over the
   link. Fixed by returning `SeasonId` from that query and threading it through every Source payload;
   `AQuoteBackfillingASourcesDate_KeepsItsSeasonLink` is the regression guard, mutation-verified.
2. **Positional Dapper tuples.** Adding a column mid-`SELECT` shifts every later column into the wrong
   tuple slot, with no compile error — `CompletenessStatus` would have received `SeasonId`'s value. Both
   readers of each changed query were updated in step with the SQL.

The `/import` endpoint's own call site needed `seasons` passed explicitly; it takes optional arguments
positionally, so the build stayed green while that path silently created no seasons.

### 6. Curate the Avatar reference data

**Status:** ✅ Done, 2026-09-03

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

**The quote, picked arbitrarily from that episode's IMDb quotes page** — the point of a random pick
being that whatever comes up must fit the tables, which proves more than a curated choice would:

> **Sokka** — "Giant light beams, flying bison, Airbenders, I think I've got Midnight Sun Madness. I'm
> going home to where stuff makes sense."

It exercises the whole chain at once: a Character, an episode-level Source, and a Season carrying number,
title and subtitle together — the only bundled content that does.

Delivered as `data/sources/quotinator-seasons.json` — `universe`/`series`/`seasons`/`sources` plus that
one quote — wired into `manifest.json` between the series-universe overlay and the bulk files, so the
reference graph exists before any bulk quote is read.
`InitialiseAsync_BundledSeasonsFile_SeedsTheWholeChain` asserts the chain against the real file rather
than a fixture, since arriving-ahead-of-the-bulk-import is the property under test.

**`schemas/source-extended.schema.json` had to be extended first, and would otherwise have rejected the
file outright.** Both the `source` definition and the new `season` one carry
`additionalProperties: false`, so `seasonNumber` was not merely undocumented — it was invalid. Step 5
added the DTO without the schema; that is the same gap CLAUDE.md's JSON-parsing policy names, seen from
the other direction.

**The quote's id was hand-authored in the first draft, and is not any more** (developer, 2026-09-03: "we
never hand author Id's; it's why we have the various helper methods"). An invented UUID imports once and
then diverges from whatever `QuoteIdentity.StableId` computes for the same quote on any later
re-conversion, duplicating the row rather than matching it.
`BundledSeasonsFile_QuoteIdsAreDerived_NotHandAuthored` asserts every quote id in the file equals the
derived value, and is what produced the correct one.

**Case-insensitivity is asserted, not assumed, for everything keyed on an id here.** Every new query
wraps through `IdClauses`, but wrapping is not evidence:
`EntityIdentityTests.SeasonId_SeriesIdCasingDiffers_ProducesSameId` covers the derivation and
`SeasonNaturalKeyLookup_IsCaseInsensitiveOnSeriesId` covers the natural-key lookup against a real
database, mutation-verified by dropping the wrap and watching it fail. Both fixtures use an id
containing hex letters and assert that up front — a digits-only GUID is identical in either case and
would make the assertion vacuous.

### 7. Attach the resolvable quotes to their episodes

**Status:** ✅ Done, 2026-09-03

The seven quotes are the ones step 1 measured; the lookups were done on 2026-09-03 rather than left as
a step, so this step writes rules against known values and nothing here is open-ended.

| Quote | Character | Episode | Season |
|---|---|---|---|
| "That's what I do: I drink and I know things." (Game of Thrones) | Tyrion Lannister | *Home* | S6.E2, aired 2016-05-01 |
| "I have burrowed underneath your brain…" (Mr. Robot) | Mr. Robot | `eps2.1_k3rnel-pan1c.ksd` | S2.E1, 2016 |
| "Unfortunately, we're all human. Except me, of course." (Mr. Robot) | Tyrell Wellick | `eps1.4_3xpl0its.wmv` | S1.E4, 2015 |
| "Time means nothing. Jeremy Bearimy, baby…" (The Good Place) | Chidi | *Pandemonium* | S4, 2019 |
| "People walk around… That's power." (Mr. Robot) | **Fernando Vera** | — listed only at series and character level | — |
| "When we lose our principles, we invite chaos." (Mr. Robot) | — | — | — |
| "Love's the most powerful emotion…" (Arrow) | — | — | — |

Four get a per-quote `Custom` rule setting `source` and `date` together, resolving each to its episode's
Source. The fifth gets its Character and nothing more. **The last two keep the show-level Source they
already have** — nearest-Source, per Scope changes — and are recorded as data enhancement candidates.

Two things this lookup established that the plan depends on:

- **The file's years are wrong, not season markers.** Every resolved episode aired in a different year
  than the file claims. Any rule written from the year rather than from the episode would be wrong.
- **A missing IMDb quote entry is not a missing quote.** "When we lose our principles, we invite chaos"
  returns nothing on IMDb and is nonetheless real — the developer supplied a screen capture of the line
  as broadcast. IMDb's quote pages are user-contributed and incomplete, so absence there is evidence of
  nothing at all, and must never be read as a quote being unverifiable (which would wrongly implicate
  [#219](https://github.com/DutchJaFO/Quotinator/issues/219)).

One incidental find worth carrying into the data enhancement milestone: the bundled Tyrell Wellick quote
is a trimmed fragment of a longer line — IMDb has "…And unfortunately, we're all human. Except me, of
course."

Delivered as five rules appended to `nikhilnamal17-conflict-rules.json`, matching the existing
Anakin/Galadriel Custom-value pattern exactly — no new mechanism. All five verified against the real
bundled file: `InitialiseAsync_NikhilNamal17WithRealRuleFile_ResolvedQuotesLandOnTheirEpisodeSource`
covers the four source+date corrections, `..._VeraQuoteGetsCharacterButKeepsShowLevelSource` the
character-only one, and `..._UnattributedQuotesKeepShowLevelSource` is the control proving the two
unresolved quotes are untouched. All three were confirmed red before the rules existed (temporarily
reverting the rule file) and green after — the equivalent of red-first for a data change rather than
code.

**Two real mistakes, caught before landing rather than after:**

- **The rule's `existingRecord`/`incomingRecord` snapshot has to be the raw, pre-correction value on
  *both* sides — not the corrected one, and not what a human would call "current".** This project's own
  Anakin/Galadriel rules already establish the pattern; verified against them directly rather than
  inferred from `ConflictRuleLookup`'s doc comments alone.
- **Punctuation has to match exactly, not just case.** Three of the five quotes use a curly apostrophe
  (`’`) in the bundled JSON, not a straight one — `ValuesEqual` does case-insensitive comparison
  but no punctuation normalisation, so a straight apostrophe in the rule's `quoteText` would have left
  `TryResolve` permanently reporting `false` with no error and no obvious symptom short of the rule
  silently never firing. Caught by checking the raw bytes of each source line individually rather than
  retyping the quote text by hand.

### 8. The masterdata endpoints

**Status:** ✅ Done, 2026-09-03

`SeasonEndpoints.cs` mirrors `SeriesEndpoints.cs` exactly — `GET /api/v1/masterdata/seasons` and
`/{id}`, tagged `ApiTags.MasterData`, `.WithName`/`.WithSummary` per the List/GetById convention with
each name a `private const string` shared with its logging tag, both handlers built on the shared
`PagedListing.GetAllAsync`/`EntityLookup.TryFindByIdAsync` helpers so the full eight-case pagination
contract and the 404 behaviour come for free rather than being reimplemented. `ApiMessages.SeasonNotFound`
added with its three locale strings. `page`/`pageSize` registered in
`NumericParameterSchemaTransformer.NumericParamsByPath` under `api/v1/masterdata/seasons`, and a live
`OpenApiSpecEndpointTests` `DataRow` added for it — the generic pagination row (17 tests) proves the
contract against a fake; this one proves the transformer is actually wired into `AddOpenApi`.

`SeasonSeriesReferenceReader` implements `ISeasonSeriesReferenceReader` via
`JoinQueryRepository`/`IJoinStrategy` per ADR 017, mirroring `SeriesUniverseReferenceReader` exactly —
`SeasonSeriesReferenceStrategy`/`...BatchStrategy` wrap the SQL step 5 already wrote. All 17
`SeasonEndpointsTests` from step 2 now pass, plus `SeasonSeriesReferenceReaderTests` (real SQLite, no
fake, mirroring `SeriesUniverseReferenceReaderTests`) and `SeasonRepositoryTests` — see below for why
the latter exists despite step 3's "no repository is written" decision standing unchanged.

`docs/api-endpoints.md` gained the two routes in the same commit.

**Three mistakes, all caught before landing:**

- **A file-clobbering `mv`.** Creating the new single-row DTO by `mv`-ing an *existing* file
  (`SeriesReferenceRow.cs`, already in use by `SourceSeriesReferenceReader`) onto the new name silently
  destroyed it — the build broke immediately with two `CS0246`s naming the file I had just erased.
  Recovered with `git show HEAD:<path>`; the two types now live in separate files as they should have
  from the start.
- **`Assert.AreEqual(JsonValueKind.Null, ...)` is the wrong test for a null `MasterDataReference`.**
  This project's JSON options omit a null property rather than emit it, so `GetProperty("series")`
  threw `KeyNotFoundException` instead of failing the assertion. `SeriesEndpointsTests`,
  `SourceEndpointsTests` and `PersonEndpointsTests` each already carry a private
  `AssertPropertyIsNullOrAbsent` helper for exactly this; `SeasonEndpointsTests` now carries the fourth
  copy, matching the established (if unconsolidated) convention rather than inventing a fifth shape.
- **"No repository is written" does not mean "no test is needed."** `SqliteRepositoryTests` already
  proves the shared generic's `pageSize = 0` contract — against a synthetic `Widget` table, never
  against `Quotinator_Season`. Neither Series nor Universe has ever had this gap closed either, so
  `SeasonRepositoryTests` is new ground for the whole masterdata family, not a Season-specific
  requirement: it is the one thing generic coverage cannot prove — that Season's real schema
  (`[Table("Quotinator_Season")]`, its columns) round-trips through the shared machinery at all.

**Two regressions from adding a tenth `ImportActionEntityTypes` member, both hardcoded inventories
working exactly as designed:** `EnumParameterSchemaTransformerTests.EntityType_OnImportActions_PatchedToEnum`
asserted the OpenAPI enum's nine values by name (fixed — `Season` added); grepped the test suite for
other `"Series", "Universe"]`-shaped literals and found no sibling.

**One pre-existing intermittent found, not fixed here.**
`DatabaseInitializerTests.Reseed_EntityTypeThatArrived_IsPresentEvenWhenNothingChanged` (from #373,
commit `ad4f3fd3` — this issue never touches its logic or fixture) failed once during a full-solution
`-m:1` run and did not reproduce across five follow-up attempts, including two full runs of
`Quotinator.Core.Tests` alone. Flagged as its own task rather than guessed at, per
`docs/testing-policy.md`'s bug-fix rules — a real fix needs a red-before-fix reproduction, which
non-reproducing does not provide.

### 9. A Source's response carries its Season

**Status:** ✅ Done, 2026-09-03

Season's own response already carries its Series (step 8). This step is the other direction:
`SourceResponse.Season`, a `MasterDataReference?` alongside its existing `Series`, resolved by
`ISourceSeasonReferenceReader`/`SourceSeasonReferenceReader` — `Sql.Sources.SelectSeasonReferenceForSource`/
`SelectSeasonReferencesForSources` mirror the existing Source→Series pair exactly, and the reader mirrors
`SourceSeriesReferenceReader`. `SourceEndpoints.GetAll`/`GetById` both call it, batched at `GetAll`
(one query per page, never one per row) exactly as the Series reference already is.

**The reference's `Name` is the season's rendered display name** (`SeasonDisplay.Format`), not its raw
`Title` — a number-only season has no `Title` at all, and "Book One" alone would be wrong when the
season also has a `Subtitle`. The row DTOs carry `Number`/`Title`/`Subtitle` for exactly this; the
formatting happens once, at the API layer, matching where `MasterDataReference` construction already
happens for every other reference in this codebase.

Six new `SourceEndpointsTests` (the reference shape, its null/soft-deleted cases, and the batch case)
were mutation-verified in both directions — one mutation on `GetById`'s resolution, a separate one on
`GetAll`'s, each caught by its own test and left the other's tests passing, confirming they check
different code paths rather than one incidentally covering the other.

**One real bug, self-inflicted and caught by the zero-warnings gate rather than a failing test.** The
first draft of `GetSourceById_SourceHasSeason_ReturnsSeasonReferenceWithRenderedDisplayName` built a
`repo` fixture and then never passed it to `CreateFactory` — the test ran against an empty repository,
so the id it queried 404'd, and asserting into a 404 body's `season` property would itself have thrown
rather than asserted anything meaningful. Caught by `dotnet build` flagging the now-unused `repo`
variable (`IDE0059`) before the test was ever run — the zero-warnings policy finding a test bug before
the test had a chance to lie by passing for the wrong reason.

**Registering `ISourceSeasonReferenceReader` in `Program.cs` immediately broke six existing
`SourceEndpointsTests`.** The real DI registration is live for every test using the real
`QuotinatorWebApplicationFactory`, and none of the existing Source tests supplied a fake for the new
interface — so they hit the real SQLite-backed reader against a database with no `Quotinator_Season`
table. Fixed by adding `FakeSourceSeasonReferenceReader` (mirroring `FakeSourceSeriesReferenceReader`)
and wiring it into `SourceEndpointsTests.CreateFactory`'s default. The general shape: adding any new
reader to `Program.cs`'s real DI graph is a breaking change for every endpoint test that shares that
entity's factory, whether or not that test's own scenario cares about the new reference.

### 10. Document how a quote's text and episode are verified

**Status:** ✅ Done, 2026-09-03

`source-verification.md` gained a new section, "Verifying a quote's text, speaker, and episode" — the
case its tiers didn't cover. IMDb is already Tier 1 and publishes per-title, per-episode and
per-character quote pages; the procedure states that these are the source for a quote's text, its
speaker and its episode, and that the episode's own air date is cross-checked against the season's
range — which caught nothing wrong here but would catch an episode attributed to the wrong season.
Also extended the "When this applies" list, since a conflict rule attributing a quote to an episode is
exactly the class of claim that section scopes.

Two rules step 7 learned the hard way, and the reason this step is not merely administrative:

- **A quote absent from IMDb is not an unverified quote.** Those pages are user-contributed and
  incomplete. Absence is evidence of nothing, and must not be read as grounds for #219.
- **Attribution is expected to be partial.** The procedure says what to do when the episode cannot be
  found: attach to the nearest Source that can be, record the row as a data enhancement candidate, and
  move on. Without that written down, the next reader treats a gap as a failure and stalls.

**Verified by `SourceVerificationDocTests`, not by eye** — three assertions over the doc's own text,
mirroring #307's "a documentation-confirmation row is not a step anyone schedules" fix rather than
leaving this as a box a human is trusted to remember to re-check. Confirmed genuinely red by
temporarily reverting the doc and watching all three fail, then restoring.

**One fabricated citation, caught before it shipped.** The first draft of the "attribution is partial"
paragraph pointed at "CLAUDE.md's 'GUID/enum/id/Name/Title comparisons' section's sibling rule on
nearest-match defaults" — a rule that section does not contain; it governs case-insensitivity, nothing
about matching granularity. Re-read the actual section before citing it and found no such rule exists.
Corrected to cite only what is real: #375's own plan doc, and the reasoning already recorded in it.

### 11. Docs, vocabulary, and the boyscout pass

**Status:** ✅ Done, 2026-09-03

`docs/api-endpoints.md` and the endpoints' `[Description]` attributes landed with steps 8 and 9, in
their own commits, as the plan required. `docs/vocabulary.md` gains `Season`, alphabetically between
`SafeValue<T>` and `SeedBatch`.

**The `.editorconfig` scoped `IDE0008` list was not kept current step by step, despite the plan saying
so.** Every new file this issue created did use explicit types from the start, but the list itself —
the record CLAUDE.md says exists specifically so the boyscout rule can't be "silently missed... before
the developer caught it by eye" — was only updated here, at the end, rather than at each file's first
touch. Recovered by diffing this issue's full touched-file set (`git log --name-only` across every
#375 commit) against the list already there: 42 files were missing. Appended at the end of the
existing glob, preserving its order rather than re-sorting the whole thing, since a reordered diff
would obscure which 42 were actually new. The seven files this issue touched that were already present
— `Program.cs`, `ApiMessages.cs`, `QuotinatorDatabaseInitializer.cs`,
`SqliteImportActionService.cs`, `OpenApiSpecEndpointTests.cs`, `DatabaseInitializerTests.cs`,
`ImportBatchesTests.cs` — needed no addition, having joined the list during earlier notification-system
work.

**`dotnet format style`'s `--include` flag silently did nothing on the first invocation with several
space-separated paths, then worked identically on a retry.** No error, no partial result — the run
reported "0 of N files" formatted for files independently confirmed (via a single-file rerun) to have
fixable violations. Root cause not identified; worked around by treating every "0 formatted" result as
suspect and re-running rather than trusting it, which is what caught it. Recorded here as a reason to
distrust a clean `dotnet format` run that follows a change to the file set, not just a reason to
distrust the tool once.

**`dotnet format --diagnostics IDE0028 IDE0305 IDE0300` was first run without `--include`, scanning the
whole `Quotinator.Core` project rather than only this issue's 43 files.** Only two files changed, both
already in the touched set, so the mistake happened to be harmless — but that was luck, not the rule
being followed, and every subsequent pass was re-run scoped. CLAUDE.md's own text anticipates exactly
this follow-on ("Giving a declaration an explicit type routinely exposes IDE0028/IDE0305... fix them in
the same pass"), and 6 more files needed a further pass once the `var` conversions above landed.

Verified with a manual sweep afterward for the three shapes the automated fixer cannot reach
(`foreach (var x in …)`, `using var x = …`, `var (a, b) = …` deconstructions) — none present in any of
the 43 files — and a full solution rebuild plus test run (0 warnings, every project green), since the
conversion touched live logic files (`ImportActionPlanner.cs`, `SqliteQuoteImportService.cs`) rather
than only test code.

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
| 22 | ❌ | The four resolvable quotes land on the episodes step 7 names | Unit test | `DatabaseInitializerTests.TheFourResolvedTvQuotes_LandOnTheirNamedEpisodes` — the values are fixed in step 7, so this asserts them rather than discovering them |
| 23 | ❌ | The three unresolved quotes keep their show-level Source | Unit test | `DatabaseInitializerTests.AnUnattributedTvQuote_KeepsItsShowLevelSource` — nearest-Source asserted, so partial attribution stays a supported state rather than a silent gap |
| 24 | ❌ | The verification procedure states how a quote's text, speaker and episode are established, and what to do when the episode is not found | Unit test | assertion over `source-verification.md`'s own text, covering both rules step 10 names |
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
