# #180 — Populate Series/Universe data via curated overlay file (review-only, staged)

**Status:** In progress
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

**Status:** Done.

- Add `series[]` and `universe[]` sections to `schemas/source-extended.schema.json` — a `universe[]`
  entry has only a `name` (no explicit `id`, matching the earlier confirmed name-only-matching
  decision); a `series[]` entry has `name` plus a nullable `universeName` (same resolve-by-name
  treatment as `Source.seriesName` below — a Series needs to link up to its Universe the same way a
  Source needs to link up to its Series).
- Add a nullable `seriesName` string field to the `source` def in the same schema.
- Add `SeriesEntry.cs`/`UniverseEntry.cs` under `src/Quotinator.Core/Import/` (mirrors
  `PersonEntry.cs`'s shape: a plain DTO with `[JsonPropertyName]`-mapped properties).
- Add `SeriesName` to `SourceEntry.cs`.
- Add `EntityIdentity.SeriesId(string name)` / `EntityIdentity.UniverseId(string name)`, mirroring
  `EntityIdentity.PersonId(string name)`'s single-part `StableId("series"|"universe", name)` shape.

### 2. Reader wiring, Sql query sets, and planner logic

**Status:** Done. `SourceActionPayload.SeriesId` and `ConflictDecisionRequest.SourceSeriesId` are
resolved-id fields (not the file's own `seriesName` text), matching how `SourceActionPayload.Title`
etc. already carry resolved values, not raw file text.

- `ParsedSourceFile`/`SourceQuoteFileReader` wiring for the two new top-level sections.
- New `Sql.Series`/`Sql.Universe` query sets (select-existing-by-id, select-id-by-name natural key,
  insert) — mirrors the existing `Sql.People`/`Sql.Sources` shape.
- `PlanUniverseAsync`/`PlanSeriesAsync` in `ImportActionPlanner`, wired into `PlanAsync` in that order
  (Universe before Series before Source) — a `series[]` entry's `universeName` must resolve against
  an already-built universe index, the same way a `sources[]` entry's `seriesName` needs the series
  index built first. **Add-only, no Modify/decidability**: Series/Universe have only a `Name` (plus,
  for Series, its Universe link), and renaming one is not part of this issue's spec.
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

**Status:** Done.

`data/sources/manifest.json`'s per-file `duplicateResolution` override (see `ManifestPolicy.Resolve`,
`src/Quotinator.Data/Import/ManifestPolicy.cs:39-48`) is set to `review` for this file specifically —
confirmed via a test that this file's resolved policy is `review`, not the bundled-seeding default of
`skip`, independent of whether the top-level manifest default ever changes. This issue's overlay data
is a new standalone file (e.g. `data/sources/quotinator-series-universe.json`) with `quotes: []` and
populated `sources`/`series`/`universe` sections — kept separate from `quotinator-curated.json` so
that file's own existing `duplicateResolution` policy (governing its quote-curation purpose) is never
touched or reinterpreted for a second, unrelated purpose.

### 4. Populate initial Series/Universe data

**Status:** Done. Implemented as `data/sources/quotinator-series-universe.json` — 75 `sources[]`
entries (each with a computed `EntityIdentity.SourceId(title, type)`-matching `id`, extracted
programmatically from the three bundled files rather than hand-typed, to avoid a Unicode-mismatch
class of bug found live during this step — see Notes), 26 `series[]` entries, 5 `universe[]` entries.

Content, identified from all matching Source titles across the three bundled files (re-extracted and
re-verified while implementing, correcting two count errors from the original review pass — noted
below):

- **Star Wars** (Universe) → Series "Star Wars": all 10 Star Wars-titled Sources.
- **Middle Earth** (Universe) → Series "The Lord of the Rings" (4 Sources — see the duplicate note
  below for one of them) and Series "The Hobbit" (3 Sources, including one whose title is missing its
  subtitle but is confirmed to be the movie, not the book).
- **Wizarding World** (Universe) → Series "Harry Potter": all 8 Harry Potter-titled Sources.
- **Marvel Cinematic Universe** (Universe) → Series "The Avengers" (6 — includes a duplicate pair, see
  below), "Captain America" (2), plus trivial single-entry Series for "Iron Man", "Spider-Man:
  Homecoming", "Captain Marvel", "Black Panther", "Guardians of the Galaxy" — each becomes a genuine
  multi-entry Series automatically once a sequel's Source is imported; the trivial Series exists only
  because `Source` can currently reach `Universe` solely through `Series` (see Notes below).
  **Correction**: the original review pass counted "Iron Man (3)" — re-verified against the actual
  bundled data during implementation and found only 1 Iron Man Source exists today, so it is a
  trivial single-entry Series alongside the other MCU standalones, not a 3-entry one.
- **Batman** (Universe) → Series "Batman (1989)" (2: *Batman*, *Batman & Robin*), Series "The Dark
  Knight Trilogy" (4 — includes a duplicate pair, see below), plus trivial single-entry Series for
  "Batman v Superman: Dawn of Justice" and "The Dark Knight Returns" (a separate Frank Miller-based
  animated work, not part of the live-action trilogy despite sharing "Dark Knight" in its title).
- **Standalone franchise Series** (no broader Universe): Terminator (2), John Wick (3), Jurassic Park
  (1), Back to the Future (2), Fast & Furious (4), Frozen (2), The Godfather (4 — includes a duplicate
  pair, see below), Naruto (3 — spans both Anime and Movie `type` values for the same franchise, and
  a Series is allowed to span types unlike Character identity's own Type-anchor rule), Rocky (4 —
  includes a duplicate pair, see below), X-Men (2), Deadpool (2). **Correction**: the original review
  pass counted "Jurassic Park (2)" — only 1 distinct Source exists in the bundled data today.
- **Deferred, not populated in this pass**: non-MCU Spider-Man (*Spider Man*/*Spiderman* are very
  likely the same Raimi film duplicated — needs #182's resolution before a Series can be staged
  confidently; *Spider-Man: Into the Spider-Verse* is deliberately multiversal by premise, forcing it
  into a single "Spider-Man" universe would be an unwarranted editorial call). Pirates of the
  Caribbean (only one distinct film present in the bundled data despite being a real multi-film
  series — will resolve automatically once a sequel is imported, nothing to populate yet).

**Known duplicates to populate around, not fix here** (all confirmed live in the bundled data while
extracting exact titles programmatically for this step, not hand-typed — see Notes) — each pair
computes to a different `Sources` id and cannot be merged today (see #182); both rows in each pair get
the same `seriesName` so Series membership is accurate despite the underlying duplicate:
- `Lord Of The Ring - The Fellowships Of The Ring` vs `The Lord of the Rings: The Fellowship of the
  Ring` (typo) — both `SeriesId` → "The Lord of the Rings".
- `Avengers : Infinity War` vs `Avengers: Infinity War` (space before the colon) — both → "The
  Avengers".
- `Batman: The Dark Knight` vs `The Dark Knight` (missing "Batman:" prefix) — both → "The Dark Knight
  Trilogy".
- `The Godfather Part II` vs `The Godfather II` ("Part " omitted) — both → "The Godfather".
- `Adonis, Creed II` vs `Creed 2` (different naming convention entirely) — both → "Rocky".

### 5. Verify staged-conflict behaviour

**Status:** Done, with one design correction confirmed with the developer (2026-07-16): `PlanSourcesAsync`'s
"should this go Pending under Review" gate has no first-time-empty-fill exception — it is a raw
inequality check, unlike `FieldMergeResolver`'s own "empty-side wins, not a real conflict" philosophy
used elsewhere (decide-time auto-resolution, `MergeOurs`/`MergeTheirs`). This means a first-time
`null → value` SeriesId fill stages Pending exactly like a genuine disagreement does — so a fresh
install stages one Pending action per Source the overlay touches (~75 for the real file), each needing
a manual decide/apply call. Confirmed as accepted behaviour for #180 rather than widening scope to
change the shared Review gate (a cross-cutting change affecting Source/Person/StageDirection/SoundCue/
Conversation's existing Modify behaviour, not just this new field) — see Notes.

- `PlanSourcesAsync_ReviewPolicy_SeriesNameChanged_StagesPendingNotAutoResolved` (planner-level,
  `Quotinator.Engine.Tests`) — a genuine existing-vs-incoming SeriesId disagreement under Review stages
  Pending, `MergedFields` stays null.
- `SeedSeriesUniverseOverlay_NoExistingSeriesId_StagesPendingUnderReviewPolicy` (end-to-end through
  the real bundled-seed pipeline, `Quotinator.Engine.Tests.DatabaseInitializerTests`) — confirms the
  same behaviour holds for a first-time fill specifically, and that `Sources.SeriesId` stays null
  until the action is decided and applied (nothing silently written).
- `SeedSeriesUniverseOverlay_AlreadyTagged_NoActionStaged` — re-seeding identical, already-applied
  content is a true no-op (zero changed fields, nothing staged at all) — the one case that genuinely
  "applies cleanly" with no action of any kind.
- `PlanSeed_SeriesUniverseOverlayEntry_ResolvesReviewNotBundledDefaultSkip` (`Quotinator.Data.Tests.ManifestSeedPlannerTests`)
  — confirms this file's own manifest entry resolves to `Review`, not the bundled top-level default of
  `Skip`.

### 6. Documentation

**Status:** Done. `README.md`'s Quote Data section and `SOURCES.md` (a new `quotinator/series-universe`
entry plus the schema-table row) both updated.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `series[]`/`universe[]` sections and Source's `seriesName` field parse per schema | Unit test | `Quotinator.Core.Tests.SourceQuoteFileReaderTests.SourceQuoteFileReader_SeriesAndUniverseSections_ParseIntoEntries` |
| 2 | ✅ | A `series[]`/`universe[]` entry with no matching existing row stages an `Add` | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanUniverseAsync_NoMatchAtAll_StagesAddAction` / `PlanSeriesAsync_NoMatchAtAll_StagesAddAction` |
| 3 | ✅ | A Source's `seriesName` resolves to the matching Series and is treated as a Modify field with the same decidability/CompletenessGuard treatment as Title/Type/Date | Unit test | `PlanSourcesAsync_SeriesNameChanged_StagesModifyAction` / `PlanSourcesAsync_ReviewPolicy_SeriesNameChanged_StagesPendingNotAutoResolved` / `PlanSourcesAsync_CompleteStatus_SeriesNameChanged_StagesBlockedNotModify` |
| 4 | ✅ | A genuine SeriesId disagreement under Review stages `Pending`, never silently resolves — including a first-time null-to-value fill (see Step 5's design correction) | Unit test | `SeedSeriesUniverseOverlay_NoExistingSeriesId_StagesPendingUnderReviewPolicy` |
| 5 | ✅ | Re-seeding identical, already-applied overlay content is a true no-op | Unit test | `SeedSeriesUniverseOverlay_AlreadyTagged_NoActionStaged` |
| 6 | ✅ | This file's manifest entry resolves to `review`, not the bundled default of `skip` | Unit test | `Quotinator.Data.Tests.ManifestSeedPlannerTests.PlanSeed_SeriesUniverseOverlayEntry_ResolvesReviewNotBundledDefaultSkip` |
| 7 | ✅ | A lowercase file-authored Source id matches an uppercase (`EntityIdentity`-derived) existing row — case-insensitive by default (Sources'/People's id-matching queries fixed) | Unit test | `PlanSourcesAsync_LowercaseFileId_MatchesUppercaseStoredId_StagesModifyNotDuplicateAdd` |
| 8 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 9 | ⬜ | T1 — app starts in Visual Studio, overlay file seeds without error, `Series`/`Universe` visible via `Quotinator.Tools.DbInspector` | Live (T1) | Developer to confirm in Visual Studio |
| 10 | ⬜ | T2 — Docker smoke test: seed with the overlay file present, confirm ~75 `Pending` Source actions are staged (per Step 5's design correction — not silently applied), decide+apply one, confirm its `SeriesId` is set | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `Quotinator.Tools.DbInspector`/`GET /import/actions?status=pending` |

---

## Notes

T1 and T2 are both required — this issue adds/modifies a bundled data file and its manifest entry,
which affects startup seeding behaviour (per this project's blanket T1/T2 rule).

This issue depends on #179 landing first (needs `Universe`/`Series`/`Source.SeriesId` to exist). It
does not depend on #174 (the Character merge algorithm) — Series/Universe data on Sources is useful
input to #174's eventual merge algorithm, but this issue only needs #179's schema to do its own job.

Any recurring-conflict automation is explicitly out of scope — see Background. Do not build a
bespoke auto-resolution mechanism here; #153 is the tracked issue for that, whenever it lands.

**Findings from implementation (2026-07-16), not captured by a Step above:**

- **Sources'/People's id-matching queries were case-sensitive** (`SelectExistingById`,
  `SelectCompletenessById`, `UpdateCompletenessById`, `UpdateFieldsById`, `CountActiveReferences`),
  unlike this project's general "case-insensitive by default, unless a perfect match is genuinely
  required" identifier-comparison rule (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive by
  default" — the same class of bug this project has already hit and fixed for database GUID
  comparisons before). `EntityIdentity`-derived ids are always stored uppercase, so a file-authored `sources[]`/`people[]`
  id referencing an already-existing row silently matched nothing if the file used a different case —
  found live while authoring the overlay file's 75 `sources[]` entries. **Fixed directly** (not
  deferred): all five queries on both `Sql.Sources` and `Sql.People` now compare via
  `UPPER(column) = UPPER(@param)`, plus the two new `Sql.Series`/`Sql.Universe`
  `SelectCompletenessById`/`UpdateCompletenessById` queries introduced by this same issue, for
  consistency from the start. `schemas/source-extended.schema.json`'s `source.id` pattern was also
  relaxed to accept either case, since a file author's chosen casing is no longer functionally
  significant. The overlay file itself keeps uppercase ids (matching what `EntityIdentity` actually
  produces) — cosmetic only now that matching is case-insensitive either way.
- **`PlanSourcesAsync`'s Review-policy "stage Pending" gate has no first-time-empty-fill exception** —
  see Step 5's design correction for the full explanation and the developer's confirmed decision to
  accept this as-is rather than widen scope to change the shared gate.
- **Title extraction must never be hand-typed** — two Star Wars titles use an en-dash (`–`, U+2013)
  rather than a hyphen (`-`), caught only because a first hand-transcribed draft of the overlay data
  computed a different `Sources` id for both and the mismatch was traced back to the character
  difference. The final overlay file's `sources[]`/`series[]` content was generated by a `dotnet-script`
  extraction script reading the exact title strings out of the bundled JSON files (never retyped), per
  ADR 010's C#-only scripting rule.

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
