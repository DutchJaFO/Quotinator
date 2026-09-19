# #409 — A quote held for review over a case-only text change shows nothing to decide, and is decided without asking

**Status:** Planning
**GitHub issue:** #409
**Tiers required:** T1, T2
**Depends on:** #370

---

## Next action

Approve this plan. Then step 1: add the `QuoteFieldMerge` signatures.

---

## Description

#374 made `quoteText` and `character` case-sensitive when a quote is staged
(`QuoteFieldMerge.CaseSensitiveContentFields`), so a quote whose text differs only in letter case is held
for review. Every later step compares the same fields case-insensitively, because it calls
`FieldMergeResolver` without that set:

| Where | What goes wrong |
|---|---|
| `SqliteImportActionService.ComputeAmbiguousFields` | The listing and the review page show nothing to decide |
| `SqliteImportActionService.DecideAsync` (Quote branch) | An empty decide succeeds and keeps the stored text, chosen by nobody |
| `SqliteImportActionService.DecideBatchAsync` | The pending-review notification's *Keep existing* / *Take incoming* builds no decision for the row, so it stays `Pending` |
| `SqliteQuoteImportService.BuildConflictEntries` | Under `merge-theirs` the `POST /import` response reports `quoteText` as kept (`ours`), while the planner took the incoming text |

## Scope changes

Agreed 2026-09-19 while cross-checking the issue against the code:

- **The notification's batch decide** (`DecideBatchAsync`) is in scope — same root cause, same fix.
- **`CLAUDE.md`** still says every field compares case-insensitively and rejects a per-field exemption
  list; #374 introduced exactly that exemption. The paragraph is corrected here.
- **The `POST /import` response** (`BuildConflictEntries`) is the fourth caller of the same kind,
  found by the fix-the-class rule (`docs/testing-policy.md`, *Bug fixes*).

## Decisions

- **One entry point for a quote comparison.** `QuoteFieldMerge` gains `Resolve`, `ResolveWithDecisions`
  and `ValuesEqual`, which call `FieldMergeResolver` with `CaseSensitiveContentFields`. Every quote
  comparison goes through them — the planner's and the four above — so a caller cannot forget the set.
  Four callers forgetting it is how this issue arose.
- Other entity types keep calling `FieldMergeResolver` directly: none of them has a case-sensitive field.
- **Fixtures store the existing quote by importing and applying it**, not by inserting a row:
  `SqliteImportActionServiceTests.SeedExistingQuoteAsync` stores no character, so it cannot set up a
  case-only character change.

---

## Steps

### 1. Add the `QuoteFieldMerge` signatures

**Status:** ⬜ Not started

`QuoteFieldMerge.Resolve`, `QuoteFieldMerge.ResolveWithDecisions` and `QuoteFieldMerge.ValuesEqual`,
each calling `FieldMergeResolver` **without** the set — today's behaviour, so the tests in step 2
compile and fail on their assertions. No caller changes yet.

### 2. Write every test and the automated document, and run each red

**Status:** ⬜ Not started

The unit tests in the Verification checklist, rows 1–10. Rows 7 and 10 are controls and start green.
The automated document is run against an image of this branch after step 1 — the canary — and fails
at its listing step.

### 3. Pass the set, and route every quote comparison through `QuoteFieldMerge`

**Status:** ⬜ Not started

The three methods pass `CaseSensitiveContentFields`. Replace the direct `FieldMergeResolver` calls on
quote fields: `ImportActionPlanner` (`Resolve`, two `ResolveWithDecisions`, four `ValuesEqual`),
`SqliteImportActionService` (`DecideAsync`'s Quote branch, `ComputeAmbiguousFields`' Quote case) and
`SqliteQuoteImportService.BuildConflictEntries`. `DecideBatchAsync` is fixed through
`ComputeAmbiguousFields`.

### 4. Correct `CLAUDE.md`

**Status:** ⬜ Not started

Rewrite the paragraph on `FieldMergeResolver.ValuesEqual` in *GUID/enum/id/Name/Title comparisons are
case-insensitive by default*: case-insensitive by default, except a quote's `quoteText` and `character`
(#374); every step that compares a quote — staging, listing, deciding — goes through `QuoteFieldMerge`.

### 5. Build and run the full suite

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 6. T2 pass

**Status:** ⬜ Not started

Against a fresh build of the branch: the smoke set, the new document, and the documents that list or
decide staged actions — `import-and-staged-actions/01`, `/13`, `/20`, `/28`. Each container's log is
read for `[Runtime - Exception]` lines before it is removed.

### 7. T1 pass

**Status:** ⬜ Not started

The developer starts the application in Visual Studio.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The listing reports `quoteText` for a case-only text change | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_QuoteTextDiffersOnlyByCase_ReportsQuoteTextAsAmbiguous` |
| 2 | ❌ | The listing reports `character` for a case-only character change | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_CharacterDiffersOnlyByCase_ReportsCharacterAsAmbiguous` |
| 3 | ❌ | An empty decide on a case-only change is refused, naming the field | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteTextDiffersOnlyByCase_NoDecision_IsRefused` |
| 4 | ❌ | Taking incoming on a case-only change decides the incoming casing | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteTextDiffersOnlyByCase_Replace_TakesIncomingText` |
| 5 | ❌ | The notification's batch decide settles a case-only change | Unit test | `SqliteImportActionServiceTests.DecideBatchAsync_QuoteTextDiffersOnlyByCase_DecidesIt` |
| 6 | ❌ | Under `merge-theirs`, the `POST /import` response reports a case-only `quoteText` as taken from the incoming side | Unit test | `QuoteImportServiceTests.ImportAsync_MergeTheirs_QuoteTextDiffersOnlyByCase_ReportsQuoteTextFromIncoming` |
| 7 | ✅ | A quote field outside the set still resolves a case-only difference on its own — control | Unit test | `QuoteFieldMergeTests.ResolveWithDecisions_SourceDiffersOnlyByCase_NeedsNoDecision` (new class) |
| 8 | ❌ | A case-only `quoteText` difference is unresolved without a decision | Unit test | `QuoteFieldMergeTests.ResolveWithDecisions_QuoteTextDiffersOnlyByCase_IsUnresolved` |
| 9 | ❌ | `CLAUDE.md` names the case-sensitive quote fields and the single entry point | Unit test | `QuoteFieldMergeTests.CaseSensitiveContentFields_AreDocumentedInClaudeMd` |
| 10 | ✅ | A genuine text change is listed exactly as before — control | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_PendingModifyConflicts_ReportsAmbiguousFieldsWithoutThrowing` (existing) |
| 11 | ❌ | In a running container, a case-only change is listed with `quoteText`, an empty decide is refused, and taking incoming decides the incoming casing | Live (T2) | `automated-testing/import-and-staged-actions/29-a-case-only-change-is-shown-for-review.md` passes on this branch's build and fails on the canary at its listing step |
| 12 | ❌ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
