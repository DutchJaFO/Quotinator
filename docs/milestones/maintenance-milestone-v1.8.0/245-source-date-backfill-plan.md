# #245 — Sources.Date stays NULL when a Source's only sources[] entry omits date

**Status:** Waiting for release
**GitHub issue:** #245
**Tiers required:** T1, T2
**Depends on:** none (isolated fix inside `ImportActionPlanner.ResolveSourceAsync`)

---

## Spec requirements

1. When `ResolveSourceAsync` (the quote-driven Source resolution path) finds a Source that already
   exists with `Date IS NULL`, and the resolving quote (`q.Date`) carries a non-null date, that gap
   must be backfilled — not left `NULL` forever the way it is today.
2. First-found-wins: no conflict-resolution logic for disagreeing dates. If the existing row already
   has a non-null `Date`, a later quote with a different date must never overwrite it — mirrors #191's
   own already-established scoping for this exact concern.
3. The backfill goes through `CompletenessGuard.ShouldBlock` like every other Source field correction
   in this codebase — no special-cased bypass. A `Complete` row with a `NULL` `Date` stages a visible
   `Blocked` action instead of a silent write.
4. `SeriesId` is untouched by this change — scoped to `Date` only, exactly like #191.
5. No backfill for already-seeded/deployed databases — an operator with existing null-dated Sources
   needs a Reset to pick up dates, same limitation #191 accepted for its own fix.

---

## Background — why this issue exists

#191 (`docs/milestones/data-import-sources/191-source-date-population-plan.md`) fixed `Sources.Date`
staying `NULL` for a Source *discovered purely via a quote* (no `sources[]` entry names it) — it
populates `Date` at the moment `ResolveSourceAsync` stages a brand-new `Add` action, reading `q.Date`
into the payload it was already building.

#191 explicitly did not touch `PlanSourcesAsync` (the separate `sources[]`-entry path, #162), because
every Source that path creates was assumed to either carry its own `date` or never need one filled in
from elsewhere. That assumption doesn't hold: `quotinator-series-universe.json` declares 61
`sources[]` entries whose only purpose is linking a Source to a Series (`title`/`type`/`seriesName`
only, no `date`). Any of these that's also referenced by a dated quote in a later-seeded file
permanently keeps `Date = NULL`, because:

1. `PlanSourcesAsync`'s Add branch creates the row with `Date = NULL` (the entry supplies none).
2. `ResolveSourceAsync` (the quote-driven path) later finds the row already exists by natural key
   (`Sql.Sources.SelectIdByTitleAndType` — id only) and returns immediately. It has no update/backfill
   logic for an already-existing row at all — #191's Add-only fix never runs for a row that already
   exists.

Confirmed live on the bundled dataset: `461 total, 98 null_dates`. `Frozen`/`Jurassic Park` are both
`NULL` despite each having a dated quote elsewhere in the bundled data — this issue's own repro, and
the exact gap #191's own T2 row 6 already flagged (`have_date` was `439/479` right after that fix
shipped, not `479/479`) without anyone investigating the remaining 40 at the time.

**Verified before starting** (per this project's standing rule):

- **Confirmed as claimed**: `ResolveSourceAsync`'s existing-row branch
  (`ImportActionPlanner.cs:415-421`) queries only `Sql.Sources.SelectIdByTitleAndType` (id-only) and
  returns immediately with no comparison against `q.Date` at all.
- **A sibling query already exists with exactly the shape needed**: `Sql.Sources.SelectExistingByTitleAndType`
  (`Sql.cs:429`) returns `Id, Date, SeriesId, CompletenessStatus` — already used by `PlanSourcesAsync`'s
  own natural-key branch for the identical lookup shape. No new SQL is needed for this fix.
- **Confirmed each seed file's batch fully applies before the next file's planning begins**:
  `QuotinatorDatabaseInitializer.cs` (~line 281) shows `PlanAsync` followed immediately by
  `ApplyBatchAsync`, per file, in sequence. A later file's `ResolveSourceAsync` call therefore always
  sees the previous file's already-applied `Date` (if backfilled) via a fresh DB query — no same-run
  staleness to guard against, and no cross-file coordination logic is needed beyond "query the DB".
- **Confirmed `CompletenessGuard.ShouldBlock` is a pure `status == Complete && changedFields.Count > 0`
  check** (`CompletenessGuard.cs`) — a `NeedsReview`/`Incomplete` row (the default for any freshly
  seeded/discovered Source) is always freely correctable; the guard only matters for a row a human has
  explicitly marked `Complete`.

---

## Approach

In `ResolveSourceAsync`'s existing-row branch (`ImportActionPlanner.cs:415-421`):

1. Query via `Sql.Sources.SelectExistingByTitleAndType` instead of `SelectIdByTitleAndType`.
2. If the row's `Date` is `null` and `q.Date` is not `null`:
   - Build `existingPayload`/`incomingPayload` as `SourceActionPayload(q.Source, typeStr, row.Date /
     q.Date, row.SeriesId)` — `SeriesId` carried through unchanged on both sides.
   - `changedFields = { "date" }`.
   - `CompletenessGuard.ShouldBlock(row.CompletenessStatus.Parsed ?? Incomplete, changedFields)`:
     - **Blocked** → stage a `Modify`/`Blocked` `ImportActionEntity` (same shape as
       `PlanSourcesAsync`'s own Blocked-staging code, `ImportActionPlanner.cs:639-651`).
     - **Not blocked** → stage a `Modify`/`Decided` `ImportActionEntity` with `MergedFields` = the
       incoming payload (auto-applies immediately, no review step — mirrors #191's own precedent of
       staging its Add as `Decided` for exactly this "background field population, not a user
       decision" reasoning).
3. If the row's `Date` is already non-null, or `q.Date` is null, or nothing changed: behave exactly as
   today (return the id, no action staged) — this is what keeps "first-found-wins" correct with zero
   new tie-break logic.
4. `index[key] = foundId` still happens on every path (unchanged) — the in-memory per-batch cache
   means only the *first* quote in a given file/batch ever reaches this lookup for a given key; every
   later same-batch quote referencing the same Source short-circuits before this logic runs at all,
   which is exactly "first quote encountered wins," requiring no additional bookkeeping.

`SeriesId` is deliberately not synced by this same code path — that stays #180's own explicit-entry
concern; widening scope here would blur what this issue is actually fixing.

---

## Files touched

- `src/Quotinator.Core/Database/ImportActionPlanner.cs` — `ResolveSourceAsync`, per Approach above.
- No `Sql.cs` change — reuses `Sql.Sources.SelectExistingByTitleAndType`, already correctly shaped.
- `data/sources/vilaboim-conflict-rules.json` — 3 stale-snapshot corrections, found during T2 (see Notes).

---

## Steps

### 1. Write the four failing tests (red)
**Status:** ✅ Done — all four confirmed red against pre-fix code (3 assertion failures on the missing
backfill/blocked behaviour; the 4th, `..._NoActionStaged`, already passed pre-fix since it documents
existing correct behaviour, not a bug — it's a regression guard, not a repro).

In `Quotinator.Core.Tests.Database.ImportActionPlannerTests`:
- `ResolveSourceAsync_ExistingNullDatedSource_QuoteWithDate_StagesDecidedModifyBackfillingDate`
- `ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_NoActionStaged`
- `ResolveSourceAsync_ExistingCompleteNullDatedSource_QuoteWithDate_StagesBlockedNotBackfill`

In `Quotinator.Core.Tests.Database.DatabaseInitializerTests`:
- `InitialiseAsync_DatelessSourcesEntryThenDatedQuoteInLaterFile_BackfillsSourceDate` — reproduces the
  real-world shape directly: file A seeds a `sources[]` entry with no `date`; file B (seeded after)
  contains a quote for that same `Title|Type` carrying a `date`; assert the final `Sources.Date` is
  populated after full initialisation.

### 2. Implement the fix
**Status:** ✅ Done — `ResolveSourceAsync`'s existing-row branch switched to `SelectExistingByTitleAndType`,
per Approach above. Neither `ExistingBatchId` nor `AppliedPolicy` is set on the staged Modify actions —
confirmed against `PlanSourcesAsync`'s own established Source-Modify precedent (neither field is ever
set there either) and confirmed safe (`AppliedPolicy.Parsed` only gates on `== Skip`, never on `null`).

### 3. Verify
**Status:** ✅ Done — all four tests green. Canary mutation performed on
`..._NoActionStaged` per `docs/testing-policy.md`'s negative-assertion rule (temporarily removed the
`row.Date is null` guard, confirmed the test fails with a clear message, reverted). Full solution
build (0 warnings/0 errors) and test suite (3173 tests, 0 failures, up from 3169 — exactly the 4 new
tests) both green. T2 found and required a real, unanticipated fix — see Notes below.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A NULL-dated existing Source resolved via a dated quote stages a Decided Modify backfilling Date | Unit test | `ImportActionPlannerTests.ResolveSourceAsync_ExistingNullDatedSource_QuoteWithDate_StagesDecidedModifyBackfillingDate` |
| 2 | ✅ | A dated existing Source resolved via a differently-dated quote stages nothing (first-found-wins) | Unit test | `ImportActionPlannerTests.ResolveSourceAsync_ExistingDatedSource_QuoteWithDifferentDate_NoActionStaged` |
| 3 | ✅ | A Complete, NULL-dated Source resolved via a dated quote stages Blocked, not a silent backfill | Unit test | `ImportActionPlannerTests.ResolveSourceAsync_ExistingCompleteNullDatedSource_QuoteWithDate_StagesBlockedNotBackfill` |
| 4 | ✅ | A real startup seed backfills a dateless sources[]-entry Source once a later file's quote supplies a date | Unit test | `DatabaseInitializerTests.InitialiseAsync_DatelessSourcesEntryThenDatedQuoteInLaterFile_BackfillsSourceDate` |
| 5 | ✅ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — 3173/3173 |
| 6 | ✅ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed (2026-08-08): clean Reset → fresh reseed cycle, `799 quotes / 461 sources`, zero pending/stale actions, matching T2 exactly |
| 7 | ✅ | T2 — a fresh seeded container backfills real bundled Sources | Live (T2) | `Frozen` → `2013`, `Jurassic Park` → `1993` via `Quotinator.Tools.DbInspector`; `null_dates` dropped `98` → `38`; fresh-seed `status=pending`/`status=stale` both `totalCount: 0` |

---

## Notes

**T2 surfaced a real, unanticipated downstream consequence, found and fixed in the same pass
(2026-08-08).** A fresh seed with the code fix alone showed `quotes: 736` / `sources: 423` (not the
expected `799`/`461`) and left 3 quotes from `vilaboim_movie-quotes.json` (Star Wars IV, The
Terminator, Terminator 2) staged `Stale`, breaking the "fresh seed = zero pending actions" invariant.

Root cause: these 3 quotes are cross-file duplicates also present in
`NikhilNamal17_popular-movie-quotes.json` (which carries real release years). `vilaboim`'s own schema
has no `date` field at all, so its re-import of the same quotes always sends `date: null`. Before this
fix, these Sources' `Date` was always `null` too (nothing ever backfilled them), so the quote-level
field comparison always saw existing=null vs incoming=null — no conflict.  Once this fix correctly
backfills the Source's `Date`, the comparison now sees a genuine mismatch (existing=a real year,
incoming=null), which correctly trips `vilaboim-conflict-rules.json`'s existing rules for these 3
quotes into `Stale` — their recorded snapshot no longer matches reality, exactly the drift-detection
#153 was built to catch. **This is #153 working correctly, not a bug in this fix** — but shipping this
fix as-is would have left those 3 rules permanently stale on every fresh seed until manually corrected.

Developer decision (2026-08-08): update the 3 affected rules' `existingRecord.date` snapshots in
`vilaboim-conflict-rules.json` to match the new post-backfill reality (`null` → the real year), keeping
the existing `Keep` resolution unchanged — same fix shape #153's own Zootopia-apostrophe staleness case
already used. `existingRecord.source`'s own stale title text (e.g. `"Star Wars"` vs. the Source's
actual current title `"Star Wars: Episode IV - A New Hope"`) was deliberately left untouched — `source`
is not among that rule's own governed `fields`, so it plays no part in staleness evaluation and
correcting it is unrelated, pre-existing drift outside this issue's scope.

Confirmed fixed: a fresh seed with both changes shows `quotes: 799` / `sources: 461` (the expected
baseline) and `status=pending`/`status=stale` both `totalCount: 0`.
