# #191 — Populate Sources.Date from the resolving quote

**Status:** Planning
**GitHub issue:** #191
**Tiers required:** T1, T2
**Depends on:** none (isolated fix inside `ImportActionPlanner.ResolveSourceAsync`)

---

## Spec requirements

1. `ResolveSourceAsync`'s staged Add payload for a newly-discovered Source carries `q.Date`, not the
   record's `null` default.
2. First-quote-wins: once a Source's natural key (`Title|Type`) is resolved — either from an existing
   DB row or from a same-batch Add — every later quote referencing that key must not alter the already-
   staged/stored `Date`. This is not new logic; it is `ResolveSourceAsync`'s existing `index`
   short-circuit, and Date must ride along on it unchanged.
3. No backfill for already-seeded deployments (decided with the developer, 2026-07-19) — the fix applies
   to newly-imported/newly-seeded Sources only. An operator running an existing deployment with
   null-dated Sources must run a full Reset to pick up dates for already-seeded rows.
4. No conflict-resolution logic for disagreeing quote dates (decided with the developer, 2026-07-19) —
   whichever quote is encountered first in file/import order wins arbitrarily, exactly as `Title`/`Type`
   already do today. The 16 known-disagreeing source keys (issue #191's own table) are not specially
   handled.

---

## Background — why this issue exists

`ImportActionPlanner.ResolveSourceAsync` (`ImportActionPlanner.cs` line 187) constructs its Add payload
as `new SourceActionPayload(q.Source, typeStr)` (line 219) — only the first two of
`SourceActionPayload`'s four positional parameters (`Title`, `Type`, `Date = null`, `SeriesId = null`).
`q.Date` (`SourceQuote.Date`, `SourceQuote.cs` line 30) is available on the exact same `SourceQuote`
already in scope and is simply never read. Confirmed live during #180's T2 Docker pass (2026-07-16): all
479 seeded Sources have `Date IS NULL` despite 741/841 bundled quotes carrying a `date`.

**Verified before starting** (per this project's standing rule):

- **Confirmed as claimed**: `SourceActionPayload` is `internal sealed record SourceActionPayload(string
  Title, string Type, string? Date = null, string? SeriesId = null)` (`ImportActionPlanner.cs` line
  1036) — `Date` genuinely defaults to `null` when omitted positionally, exactly as the issue states.
- **Confirmed as claimed**: `ResolveSourceAsync`'s only call site for the Add branch is line 219; the
  method's first branch (`index.TryGetValue(key, out var existing)`, line 193) and its DB-lookup branch
  (`Sql.Sources.SelectIdByTitleAndType`, line 202-208) both return early without constructing a payload
  at all — `Date` is only ever written on a genuine first-time Add, confirming Spec requirement 2 (first-
  quote-wins) already falls out of the existing control flow with zero new logic needed; the fix is
  purely "read one more field into the payload already being built there."
- **Confirmed this is the single call site for Source discovery-via-quote**: `ResolveSourceAsync` is
  called once, from `PlanAsync`'s quote loop (`ImportActionPlanner.cs` line 77). `PlanSourcesAsync` (the
  separate `sources[]`-file-entry path used by #162/explicit Source authoring) is untouched by this
  issue — it already threads `s.Date` through its own payload construction correctly; this bug is
  specific to a Source that is *never itself named in a file* and is only inferred from a quote.
- **Confirmed the seed path and the live-import path share this exact code**: `QuotinatorDatabaseInitializer`'s
  startup seeding and `POST /api/v1/import`/`/admin/sources/refresh` all stage through the same
  `ImportActionPlanner.PlanAsync` → `ResolveSourceAsync` call — one fix point covers every ingestion
  route, matching this milestone's existing "staging is already consistently two-stage end to end"
  finding (see #181's own verification note in `overview.md`).

---

## Approach

One-line change at the existing Add-payload construction (`ImportActionPlanner.cs` line 219):

```csharp
IncomingValue = JsonSerializer.Serialize(new SourceActionPayload(q.Source, typeStr, q.Date)),
```

`SeriesId` stays omitted (defaults to `null`) — this issue is scoped to `Date` only; Series/Universe
population is #180's own already-shipped concern via a different path (the curated overlay file), not
something a bare quote can express.

No change to `PlanSourcesAsync`, `Sql.cs`, `SqliteImportActionService.cs`, or any Modify/decide path —
this is an Add-payload-only fix. A Source discovered this way is staged as `Decided` immediately (see
`ResolveSourceAsync`'s `Status = ImportActionStatus.Decided`, unaffected by this change), so the new
`Date` value flows to the database the same way `Title`/`Type` already do, with no new review step.

---

## Steps

### 1. Write the two failing tests (red)

**Status:** Not started.

- `Quotinator.Core.Tests.Database.ImportActionPlannerTests.ResolveSourceAsync_QuoteWithDate_StagesSourceAddCarryingThatDate`
  — plan a single brand-new quote carrying `Date = "1993"`; assert the staged Source Add action's
  deserialized `SourceActionPayload.Date` equals `"1993"`. Follow the existing
  `PlanAsync_BrandNewQuote_StagesAddActionsForQuoteSourceCharacterPerson` test's setup shape
  (`BuildQuote` helper needs a `date` parameter added).
- Second case in the same test class: `ResolveSourceAsync_TwoQuotesSameSourceDifferentDates_FirstQuotesDateWins`
  — plan two same-batch quotes sharing a `Title|Type` key with different `Date` values; assert the
  staged (single) Source Add action's `Date` matches the *first* quote's date, not the second's — proves
  Spec requirement 2 (first-quote-wins) directly, using the same mechanism
  `PlanAsync_TwoQuotesInSameBatchReferencingSameNewSource_StagesOnlyOneSourceAddAction` already proves
  for action-count.
- `Quotinator.Core.Tests.Database.DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_SeedsSourceDatesFromQuotes`
  — initialise a real database from a small fixture source file with a dated quote; assert the resulting
  `Sources.Date` column is populated after seeding, proving the fix reaches the actual startup path, not
  only the planner in isolation.

### 2. Implement the fix

**Status:** Not started. The one-line change in the Approach section above.

### 3. Verify

**Status:** Not started. `dotnet build --configuration Release` (0 warnings/errors), `dotnet test
--configuration Release --verbosity normal` (full suite green, including the 3 new tests going red→green),
T1, T2 (Docker: reseed a fresh container from the bundled files, confirm via
`Quotinator.Tools.DbInspector` that `Sources.Date` is populated for a known-dated title such as
`Jurassic Park`, and cross-check the aggregate `have_date` count from the issue's own reproduction steps
is now nonzero and roughly matches the ~453 distinct dated source keys measured against the bundled
files — not necessarily exactly, since first-quote-wins plus multiple source files' import order
determines the final count, not a 1:1 mapping).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | A brand-new Source discovered via a quote carries that quote's `Date` in its staged Add payload | Unit test | `ImportActionPlannerTests.ResolveSourceAsync_QuoteWithDate_StagesSourceAddCarryingThatDate` |
| 2 | ⬜ | Two same-batch quotes disagreeing on `Date` for the same Source: the first-encountered quote's date wins, no new conflict logic | Unit test | `ImportActionPlannerTests.ResolveSourceAsync_TwoQuotesSameSourceDifferentDates_FirstQuotesDateWins` |
| 3 | ⬜ | A real startup seed populates `Sources.Date` end to end | Unit test | `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_SeedsSourceDatesFromQuotes` |
| 4 | ⬜ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` |
| 5 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 6 | ⬜ | T2 — a fresh seeded container has populated Source dates | Live (T2) | Docker: `Quotinator.Tools.DbInspector` query against a known-dated title, plus the aggregate `have_date` reproduction query from the issue |

---

## Notes

None yet — this is a planning-only pass; implementation has not started.
