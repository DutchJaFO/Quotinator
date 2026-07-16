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

**Status:** Done. `source.id` was additionally made **optional** (see the design correction below
Step 2) — `required` is now `["title", "type"]`, and `SourceEntry.Id` is `string?`.

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

**Design correction (2026-07-16), from developer review of the first overlay-file draft.** Two rules
the draft violated, both stated directly:

> import files should not provide values for properties it does not intent to set. Setting Date to
> 'null' implies you want to reset it.
>
> given that Id's for sources are generated we should not ask users to add them to files if we can
> avoid it

The draft carried a computed `id` and an explicit `date` on all 75 entries — the first is friction
this project can generate for itself, and the second actively means "reset the date," which is not
what an enrichment file intends. Neither was fixable in the file alone:

- `SourceEntry.Id` was `required`, and `PlanSourcesAsync`'s natural-key fallback staged **nothing**
  (an unconditional `continue` — #162's documented scope boundary), so simply dropping the ids would
  have silently discarded every `seriesName`.
- `SourceEntry.Date` is `string?`, so an absent `date` and an explicit `"date": null` both deserialize
  to `null` — the file genuinely could not express "don't touch this" (see Notes; this is a
  pre-existing, cross-cutting gap, filed separately).

**Resolution:** `sources[]` now has two shapes, distinguished by whether `id` is present —
*correction* (#162, explicit id, Title/Type/Date all correctable, unchanged) and *enrichment* (#180,
id omitted, matched by natural key). The enrichment path stages a Modify that diffs **`seriesId`
only**: Title/Type are the lookup key on that path so they cannot be corrections by construction
(that is precisely what #162's explicit id exists for), and `Date` is read from the existing row and
carried through on *both* sides of the diff — which encodes "don't touch it" without needing the
absent-vs-null distinction at all. A no-match entry still stages an Add, using
`EntityIdentity.SourceId(title, type)` when the file omits an id — the same value `ResolveSourceAsync`
independently computes for a quote referencing that title/type, so both resolve to one row.

New: `Sql.Sources.SelectExistingByTitleAndType` (returns the matched row's real id + Date + SeriesId +
CompletenessStatus). `CompletenessGuard` applies on this path too — a `Complete` row is never silently
enriched.

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
entries, 26 `series[]` entries, 5 `universe[]` entries. Every entry is in the *enrichment* shape per
Step 2's design correction: `{ title, type, seriesName }` only — no generated id to hand-author, and
no property the file does not intend to set (no `date`; no `universeName` on a standalone series).
Titles were extracted programmatically from the three bundled files rather than hand-typed, to avoid
a Unicode-mismatch class of bug found live during this step — see Notes.

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
| 8 | ✅ | Deciding and applying a Source SeriesId Modify actually writes the resolved value to the `Sources` row, not just to the staged action | Unit test | `Quotinator.Engine.Tests.Services.SqliteImportActionServiceTests.ApplyBatchAsync_SourceSeriesIdDecided_WritesResolvedSeriesId` — see Notes for the two live bugs this test was added to guard |
| 9 | ✅ | An entry omitting its id is matched by natural key and stages a `seriesId`-only Modify — never a duplicate `Add`, and never a silent skip | Unit test | `PlanSourcesAsync_NoExplicitId_NaturalKeyMatch_SeriesNameSet_StagesModify` / `..._NoSeriesName_NoActionStaged` / `..._AlreadyTagged_NoActionStaged` / `..._NoMatchAtAll_StagesAddWithComputedId` / `..._CompleteStatus_SeriesNameSet_StagesBlocked` |
| 10 | ✅ | An entry omitting `date` never resets the existing row's date | Unit test | `PlanSourcesAsync_NoExplicitId_OmittedDate_PreservesExistingDate` |
| 11 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 12 | ⬜ | T1 — app starts in Visual Studio, overlay file seeds without error, `Series`/`Universe` visible via `Quotinator.Tools.DbInspector` | Live (T1) | Developer to confirm in Visual Studio |
| 13 | ✅ | T2 — Docker smoke test: seed with the overlay file present, confirm all 75 Source actions stage `Pending` `Modify` under Review with only `seriesId` differing (not silently applied, not `Add`), decide+apply all, confirm `SeriesId` set on exactly 75 rows via a checkpointed DB copy | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + full decide/apply cycle + `Quotinator.Tools.DbInspector` — 479 sources / 75 tagged confirmed; see Notes for the three bugs this pass caught |

---

## Notes

T1 and T2 are both required — this issue adds/modifies a bundled data file and its manifest entry,
which affects startup seeding behaviour (per this project's blanket T1/T2 rule).

This issue depends on #179 landing first (needs `Universe`/`Series`/`Source.SeriesId` to exist). It
does not depend on #174 (the Character merge algorithm) — Series/Universe data on Sources is useful
input to #174's eventual merge algorithm, but this issue only needs #179's schema to do its own job.

Any recurring-conflict automation is explicitly out of scope — see Background. Do not build a
bespoke auto-resolution mechanism here; #153 is the tracked issue for that, whenever it lands.

**Deferred gaps found during implementation, not fixed here:**

- **An import file cannot express "leave this property alone."** Every optional field on every entry
  DTO is a plain nullable (`SourceEntry.Date`, `PersonEntry.DateOfBirth`/`DateOfDeath`,
  `SourceStageDirection.ImageUrl`, `SourceSoundCue.SoundFileUrl`/`ImageUrl`,
  `SourceConversation.Description`), so an absent property and an explicit `null` both deserialize to
  `null` — indistinguishable. Per the developer's rule ("import files should not provide values for
  properties it does not intent to set; setting Date to 'null' implies you want to reset it"), those
  two must mean different things: absent = don't touch, null = reset. This is **pre-existing and
  cross-cutting** — it predates #180 and affects five entities across #162/#171/#172/#173/#176; #180
  is simply the first file to hit it. Confirmed with the developer that #180 sidesteps it (the
  enrichment path carries the existing Date through on both sides of the diff, so no entry on that
  path can reset anything) rather than widening scope to change five shipped entities' Modify
  behaviour. Filed as **#190**, which also owns removing this issue's now-redundant
  carry-the-Date-through workaround once the general mechanism lands.
- **`Source.Date` is never populated from a quote's own `date`.** `ResolveSourceAsync`
  (`ImportActionPlanner.cs`) constructs `new SourceActionPayload(q.Source, typeStr)` with only two
  arguments, so a Source discovered implicitly through a quote always gets `Date = null` even when
  the quote itself carries a year (NikhilNamal17's converter maps `year` → `date` on every quote).
  Verified live during T2: all 479 seeded Sources have `Date IS NULL`, while 741 of 841 quotes across
  the three bundled files do carry a date (453 distinct source keys; 16 of them with quotes that
  disagree on the value). Pre-existing, unrelated to #180's own changes, and not fixed here — filed
  as **#191**. Its 16 conflicting cases are expected to get an authoritative answer during the Data
  Enrichment milestone rather than being tie-broken from upstream values.

**Findings from the T2 Docker pass (2026-07-16), all fixed, not captured by a Step above:**

- **`manifest.json` file order mattered and was originally wrong.** `quotinator-series-universe.json`
  was listed right after `quotinator-curated.json`, before `NikhilNamal17_popular-movie-quotes.json`/
  `vilaboim_movie-quotes.json` — the two files that actually establish most of the 75 target Sources
  via natural-key resolution. With that ordering, most of the overlay's `sources[]` entries staged as
  a fresh `Add` (nothing existed yet) rather than a `Modify`. Combined with this project's
  batch-apply-atomicity contract (a batch with any `Pending` action stays wholly unapplied until every
  action in it is decided), the safe `Add`/`Series`/`Universe` actions sat staged-but-unapplied while
  the batch waited on its one genuinely `Pending` Source. By the time that one action was later
  decided and the batch applied, the *other* 74 Sources had already been independently created by
  NikhilNamal17/vilaboim's own later-processed natural-key resolution — so the overlay's own deferred
  `Add` attempts hit `INSERT OR IGNORE` against an already-existing row and silently no-opped, losing
  the SeriesId assignment entirely for all 74. **Fixed by moving `quotinator-series-universe.json` to
  the end of `manifest.json`'s file list** — every target Source now already exists by the time the
  overlay runs, so all 75 entries take the well-behaved `Modify` path (which has a real `UPDATE`, not
  an ignore-on-conflict `INSERT`) instead of a racy `Add`. Confirmed live: all 75 now stage `Pending`
  under Review (matching the design correction below), and after deciding and applying all of them,
  `Sources.SeriesId` is set on all 75, verified via a gracefully-stopped (WAL-checkpointed) database
  copy — a plain live copy while the container was still running under WAL mode showed 0, which was
  correctly *not* trusted as ground truth without checking a checkpointed copy first (per this
  project's "never assume WAL lag, verify causally" rule).
- **Two duplicated payload-reconstruction sites silently dropped `SeriesId` to null**, found only by
  actually deciding and applying a Source SeriesId action end-to-end (no prior unit test exercised
  that full path — the planner-level tests only checked staging, and the DB-integration tests only
  checked Pending status, never decide→apply→verify-on-disk):
  - `SqliteImportActionService.DecideAsync`'s Source branch reconstructed `SourceActionPayload` with
    only 3 of 4 positional constructor args (`Title`, `Type`, `Date`), silently defaulting `SeriesId`
    to `null` even though `FieldMergeResolver` had already resolved the correct value.
  - `SqliteImportActionService`'s own private `ToFieldMap(SourceActionPayload)` overload — a second,
    independent copy of `ImportActionPlanner`'s own `ToFieldMap`, each carrying an explicit doc-comment
    warning that they "must stay in sync" — was never updated when `SeriesId` was added, so
    `FieldMergeResolver.ResolveWithDecisions` never even saw a `seriesId` key to resolve.
  Both fixed directly. New regression test:
  `SqliteImportActionServiceTests.ApplyBatchAsync_SourceSeriesIdDecided_WritesResolvedSeriesId` —
  the one test in the suite that exercises the full plan → stage → decide → apply → verify-on-disk
  pipeline for `SeriesId`, closing a real coverage gap (every other `SeriesId` test stopped at either
  "staged correctly" or "Pending status correct," never reaching the actual write).

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
