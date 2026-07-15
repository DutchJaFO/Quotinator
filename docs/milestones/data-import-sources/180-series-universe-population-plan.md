# #180 — Populate Series/Universe data via curated overlay file (review-only, staged)

**Status:** Planning
**GitHub issue:** #180
**Tiers required:** T1, T2
**Depends on:** #179 (Series/Universe schema)

---

## Spec requirements (from the GitHub issue)

1. Create the curated overlay file, once #179's schema exists.
2. Add it to `data/sources/manifest.json` with `duplicateResolution: { default: "review" }`
   explicitly set — must not inherit the bundled default (`skip`).
3. Confirm a conflicting re-import stages a `Pending` action rather than silently resolving.
4. Confirm a non-conflicting import applies cleanly with no staged action requiring a decision.
5. `README.md`/`SOURCES.md` updated if this introduces a new bundled data file.

---

## Background — why this issue exists

Filed while verifying (2026-07-15) that Quotinator's import pipeline stays two-stage end to end as
the milestone heads toward data enrichment work. That verification confirmed the pipeline itself is
already sound: bundled/startup seeding, `POST /api/v1/import`, and `Quotinator__AutoUpdateSources`'s
eventual database write all resolve through the same `ImportActionPlanner`/`SqliteImportActionService`
staging machinery (#154) and the same `ManifestPolicy` cascade — no bespoke direct-write path exists
anywhere in the codebase.

What the verification also surfaced: the bundled-seeding default policy is `skip`
(`data/sources/manifest.json`), which — while it does go through the staging pipeline mechanically —
picks a side without ever surfacing the disagreement to a human. That is an acceptable, deliberate
default for third-party upstream data (favouring existing rows over a re-import), but it is the wrong
default for **this issue's own curated overlay file**: if a Source already has a curator-set
`SeriesId` and this file's value disagrees, that is a genuine conflict between two pieces of
first-party, hand-authored data, and a human should decide it — not have it silently discarded or
silently overwritten. This issue's manifest entry must therefore explicitly override the file's own
`duplicateResolution` to `review`.

Any *recurring* instance of the same unresolved conflict (re-staged on every startup until a human
decides it) is #153's concern once it exists (declarative conflict-resolution rules), not something
this issue builds a bespoke mechanism for. Until #153 lands, a human re-decides an unresolved
conflict each time it's re-staged — acceptable since curated enrichment is an infrequent, deliberate
update, not continuous traffic, unlike a third-party source refreshed on a timer.

---

## Steps

**Scope correction (2026-07-15):** starting Step 1 surfaced that #179 shipped the database schema
only and deliberately left the JSON import path unwired (confirmed by reading
`schemas/source-extended.schema.json`'s `source` def — only `id`/`title`/`type`/`date`, no Series
field at all — and `SourceEntry.cs`/`SourceActionPayload`/`PlanSourcesAsync`, none of which mention
`SeriesId`). Steps 1-2 below replace the originally-planned single "design the file" step with the
actual wiring work, sized the same as #171/#172/#173's shape. **Confirmed with the developer:** the
overlay file references a Series by its **name** (a `seriesName` string field), never a raw id — the
database itself links `Sources.SeriesId` by id, but ids throughout this project's import files are
always generated from one or more identifying properties (the existing pattern for `quote.source`
resolving to a Source id, `quote.author` resolving to a Person id, and the already-confirmed
name-only matching for the `series[]`/`universe[]` sections themselves); the importer resolves
`seriesName` to the matching Series' id at import time, the same way it already resolves a quote's
`source` title to a Source id.

### 1. Schema, entry classes, and stable-id derivation

**Status:** Not started.

- Add `series[]` and `universe[]` sections to `schemas/source-extended.schema.json` — each entry has
  only a `name` (no explicit `id`, matching the earlier confirmed name-only-matching decision).
- Add a nullable `seriesName` string field to the `source` def in the same schema.
- Add `SeriesEntry.cs`/`UniverseEntry.cs` under `src/Quotinator.Core/Import/` (mirrors
  `PersonEntry.cs`'s shape: a plain DTO with `[JsonPropertyName]`-mapped properties).
- Add `SeriesName` to `SourceEntry.cs`.
- Add `EntityIdentity.SeriesId(string name)` / `EntityIdentity.UniverseId(string name)`, mirroring
  `EntityIdentity.PersonId(string name)`'s single-part `StableId("series"|"universe", name)` shape.

### 2. Reader wiring, Sql query sets, and planner logic

**Status:** Not started.

- `ParsedSourceFile`/`SourceQuoteFileReader` wiring for the two new top-level sections.
- New `Sql.Series`/`Sql.Universe` query sets (select-existing-by-id, select-id-by-name natural key,
  insert) — mirrors the existing `Sql.People`/`Sql.Sources` shape.
- `PlanSeriesAsync`/`PlanUniverseAsync` in `ImportActionPlanner`, wired into `PlanAsync` — **Add-only**,
  no Modify/decidability: Series/Universe have only a `Name`, and renaming one is not part of this
  issue's spec.
- Extend `SourceActionPayload` with a `SeriesName` field alongside `Title`/`Type`/`Date`; wire it into
  `ToFieldMap`, the changed-fields diff, and `CompletenessGuard.ShouldBlock` in `PlanSourcesAsync` —
  same treatment `dateOfBirth` got for `PersonActionPayload` in #173. A `seriesName` that doesn't
  resolve to any known Series (misspelled, or the `series[]` section omits it) is itself an Add for
  that Series (staged via step 2's `PlanSeriesAsync` in the same batch) rather than an error — the
  Series is created if it doesn't exist yet, consistent with a quote's own `source`/`author` fields
  never requiring the referenced Source/Person to pre-exist.
- `SqliteImportActionService`: Add-apply for Series/Universe rows; extend the existing Source
  Modify-apply path to write `SeriesId` (resolved from `seriesName`); add a `SourceSeriesName`
  decidability property to `ConflictDecisionRequest`, mirroring `PersonDateOfBirth`.

### 3. Add the manifest entry with an explicit `review` policy

**Status:** Not started.

`data/sources/manifest.json`'s per-file `duplicateResolution` override (see `ManifestPolicy.Resolve`,
`src/Quotinator.Data/Import/ManifestPolicy.cs:39-48`) is set to `review` for this file specifically —
confirmed via a test that this file's resolved policy is `review`, not the bundled-seeding default of
`skip`, independent of whether the top-level manifest default ever changes. This issue's overlay data
is a new standalone file (e.g. `data/sources/quotinator-series-universe.json`) with `quotes: []` and
populated `sources`/`series`/`universe` sections — kept separate from `quotinator-curated.json` so
that file's own existing `duplicateResolution` policy (governing its quote-curation purpose) is never
touched or reinterpreted for a second, unrelated purpose.

### 4. Populate initial Series/Universe data

**Status:** Reviewed and confirmed with the developer (2026-07-15); not yet implemented — depends on
#179 having shipped `Universe`/`Series`/`Source.SeriesId` (done) and this issue's own Steps 1-2.

Content confirmed for the initial pass, identified from all 476 distinct Source titles across the
three bundled files:

- **Star Wars** (Universe) → Series "Star Wars": all 10 Star Wars-titled Sources.
- **Middle Earth** (Universe) → Series "The Lord of the Rings" (4 Sources — see the typo note below
  for one of them) and Series "The Hobbit" (3 Sources, including one whose title is missing its
  subtitle but is confirmed to be the movie, not the book).
- **Wizarding World** (Universe) → Series "Harry Potter": all 8 Harry Potter-titled Sources.
- **Marvel Cinematic Universe** (Universe) → Series "The Avengers" (6), "Captain America" (2), "Iron
  Man" (3), plus trivial single-entry Series for "Spider-Man: Homecoming", "Captain Marvel", "Black
  Panther", "Guardians of the Galaxy" — each becomes a genuine multi-entry Series automatically once
  a sequel's Source is imported; the trivial Series exists only because `Source` can currently reach
  `Universe` solely through `Series` (see Notes below).
- **Batman** (Universe) → Series "Batman (1989)" (2: *Batman*, *Batman & Robin*), Series "The Dark
  Knight Trilogy" (3: *Batman begins*, *The Dark Knight*, *Dark Knight Rises*), plus trivial
  single-entry Series for "Batman v Superman: Dawn of Justice" and "The Dark Knight Returns".
- **Standalone franchise Series** (no broader Universe): Terminator (2), John Wick (3), Jurassic Park
  (2), Back to the Future (2), Fast & Furious (4), Frozen (2), The Godfather (4), Naruto (2),
  Rocky/Creed (4), X-Men (2), Deadpool (2).
- **Deferred, not populated in this pass**: non-MCU Spider-Man (*Spider Man*/*Spiderman* are very
  likely the same Raimi film duplicated — needs #182's resolution before a Series can be staged
  confidently; *Spider-Man: Into the Spider-Verse* is deliberately multiversal by premise, forcing it
  into a single "Spider-Man" universe would be an unwarranted editorial call). Pirates of the
  Caribbean (only one distinct film present in the bundled data despite being a real multi-film
  series — will resolve automatically once a sequel is imported, nothing to populate yet).

**Known duplicate to populate around, not fix here**: `Lord Of The Ring - The Fellowships Of The
Ring` (a confirmed typo of `The Lord of the Rings: The Fellowship of the Ring`, both 2001, both
`type: movie`) computes to a different `Sources` id than the correctly-titled row and cannot be
merged with it today (see #182). Both rows get `SeriesId = "The Lord of the Rings"` so Series
membership is accurate despite the underlying duplicate — the Series will show two "Fellowship of
the Ring" entries until #182 eventually resolves the duplicate itself.

### 5. Verify staged-conflict behaviour

**Status:** Not started.

- Non-conflicting case: a Source with no existing `SeriesId` imports this file cleanly, no staged
  action requiring a decision — `SeedSeriesUniverseOverlay_NoExistingSeriesId_AppliesCleanly`.
- Conflicting case: a Source with an existing, different `SeriesId` (e.g. seeded by a prior run of
  this same file with an since-edited value) stages a `Pending` action instead of silently
  overwriting — `SeedSeriesUniverseOverlay_ConflictingExistingSeriesId_StagesPendingNotAutoResolved`.
- Policy-resolution case: confirm this file's resolved policy is genuinely `review`, not inherited
  from the bundled default — `SeedSeriesUniverseOverlay_ManifestEntry_ResolvesReviewPolicyNotBundledDefaultSkip`.

### 6. Documentation

**Status:** Not started. `README.md`/`SOURCES.md` updated for the new bundled data file (step 3's
new standalone file), per this project's existing convention for documenting bundled sources.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `series[]`/`universe[]` sections and Source's `seriesName` field parse per schema | Unit test | `Quotinator.Core.Tests.SourceQuoteFileReader_SeriesAndUniverseSections_ParseIntoEntries` — starts red |
| 2 | ❌ | A `series[]`/`universe[]` entry with no matching existing row stages an `Add` | Unit test | `Quotinator.Engine.Tests.ImportActionPlanner_NewSeriesName_StagesAddAction` / `..._NewUniverseName_StagesAddAction` — starts red |
| 3 | ❌ | A Source's `seriesName` resolves to the matching Series and is treated as a Modify field with the same decidability/CompletenessGuard treatment as Title/Type/Date | Unit test | `Quotinator.Engine.Tests.ImportActionPlanner_SourceSeriesNameChanged_StagesModifyAction` / `..._CompleteSourceSeriesNameChanged_StagesBlocked` — starts red |
| 4 | ❌ | A non-conflicting overlay import applies cleanly | Unit test | `Quotinator.Engine.Tests.SeedSeriesUniverseOverlay_NoExistingSeriesId_AppliesCleanly` — starts red |
| 5 | ❌ | A conflicting overlay import stages `Pending`, never silently resolves | Unit test | `Quotinator.Engine.Tests.SeedSeriesUniverseOverlay_ConflictingExistingSeriesId_StagesPendingNotAutoResolved` — starts red |
| 6 | ❌ | This file's manifest entry resolves to `review`, not the bundled default of `skip` | Unit test | `Quotinator.Engine.Tests.SeedSeriesUniverseOverlay_ManifestEntry_ResolvesReviewPolicyNotBundledDefaultSkip` — starts red |
| 7 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 8 | ❌ | T1 — app starts in Visual Studio, overlay file seeds without error, `Series`/`Universe` visible via `Quotinator.Tools.DbInspector` | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 9 | ❌ | T2 — Docker smoke test: seed with the overlay file present, confirm `SeriesId` set on the expected Sources; edit the file to introduce a conflict, restart, confirm a `Pending` action is staged (not silently applied) | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `Quotinator.Tools.DbInspector`/`GET /import/actions?status=pending` |

---

## Notes

T1 and T2 are both required — this issue adds/modifies a bundled data file and its manifest entry,
which affects startup seeding behaviour (per this project's blanket T1/T2 rule).

This issue depends on #179 landing first (needs `Universe`/`Series`/`Source.SeriesId` to exist). It
does not depend on #174 (the Character merge algorithm) — Series/Universe data on Sources is useful
input to #174's eventual merge algorithm, but this issue only needs #179's schema to do its own job.

Any recurring-conflict automation is explicitly out of scope — see Background. Do not build a
bespoke auto-resolution mechanism here; #153 is the tracked issue for that, whenever it lands.

**Findings from the franchise-identification review pass (2026-07-15), not captured by a Step above:**

- **`Source` can only reach `Universe` through `Series` — there is no direct `Source.UniverseId`.**
  This is fine for anything in a named sub-series, but a Source that's only loosely/thematically part
  of a Universe (no named sub-series of its own — e.g. `BATMAN V SUPERMAN: DAWN OF JUSTICE`) needs a
  trivial, single-Source Series invented purely to carry the link. Confirmed with the developer as
  the accepted workaround for this pass rather than a schema change on top of #179 (already shipped,
  T1+T2 verified) — a genuine `Source.UniverseId` would be real new scope, not something to fold in
  here. Revisit only if the trivial-Series pattern becomes unwieldy at a larger scale.
- **A `Source` can only ever belong to one `Universe`** (the chain is single-valued at both levels:
  one `Series` per `Source`, one `Universe` per `Series`). No genuine case in the bundled data
  currently needs a Source in two Universes at once — noted as a known limitation to revisit if one
  is found, not filed as an issue yet.
- **Source aliases and a title/subtitle concept are a real, separate gap** — `Source.Title` is the
  only identifier today, with no mechanism for "this is the same Source under a different valid
  title" (e.g. `Dr. Strangelove` vs. its own full subtitle) or for franchises that distinguish
  installments by subtitle rather than numeral (`The Hobbit: The Desolation of Smaug`, no "Part 2").
  Confirmed this isn't a hypothetical: `Lord Of The Ring - The Fellowships Of The Ring` is a live
  instance in our own bundled data (see Step 3). Filed as **#182** ("Merge/consolidate entities whose
  computed id was affected by a data mistake"), in the Data Enrichment milestone (not Data Import &
  Sources — the triggering cause is upstream data quality, matching #147's own milestone placement).
  #180 does not wait on #182 or attempt to solve it — see Step 3 for how the known duplicate is
  populated around instead of fixed.
