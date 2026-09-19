# #409 — A quote held for review over a case-only text change shows nothing to decide, and is decided without asking

**Status:** Waiting for release
**GitHub issue:** #409
**Tiers required:** T1, T2
**Depends on:** #370

---

## Next action

Wait for the release; then the *Released* checklist and the closing comment.

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

**Status:** ✅ Done — `1e4a27be`. The build is clean. Adding `SqliteQuoteImportService.cs` and
`QuoteImportServiceTests.cs` to the scoped IDE0090 list surfaced their existing `new Type(...)`
declarations, converted in the same commit.

`QuoteFieldMerge.Resolve`, `QuoteFieldMerge.ResolveWithDecisions` and `QuoteFieldMerge.ValuesEqual`,
each calling `FieldMergeResolver` **without** the set — today's behaviour, so the tests in step 2
compile and fail on their assertions. No caller changes yet.

### 2. Write every test and the automated document, and run each red

**Status:** ✅ Done — seven red, each on its own assertion with its precondition holding; rows 4, 7 and
10 green.

Row 4 was planned red and is not: an explicit decision always wins in `FieldMergeResolver`, so taking
incoming already stored the incoming casing. It stays as the positive half of row 3.

| Row | Red on |
|---|---|
| 1, 2 | The listing reports no ambiguous field |
| 3 | The empty decide returns `Decided` |
| 5 | The batch decide decides 0 actions |
| 6 | The response reports `ours` while the stored text is the incoming `ORIGINAL.` |
| 8 | `QuoteFieldMerge.ResolveWithDecisions` reports nothing unresolved |
| 9 | `CLAUDE.md` does not contain the text |

*A case-only change is shown for review, and needs a decision*, against a canary image of `871061d8`:
step 3 printed `caseOnly= control=quoteText`, and step 4's empty decide answered `204`. Container and
image removed.

### 3. Pass the set, and route every quote comparison through `QuoteFieldMerge`

**Status:** ✅ Done — `19ea138b`. Every #409 test green except row 9, which waits on step 4. Every
direct `FieldMergeResolver` call left in `Quotinator.Core` is for another entity type.

The three methods pass `CaseSensitiveContentFields`. Replace the direct `FieldMergeResolver` calls on
quote fields: `ImportActionPlanner` (`Resolve`, two `ResolveWithDecisions`, four `ValuesEqual`),
`SqliteImportActionService` (`DecideAsync`'s Quote branch, `ComputeAmbiguousFields`' Quote case) and
`SqliteQuoteImportService.BuildConflictEntries`. `DecideBatchAsync` is fixed through
`ComputeAmbiguousFields`.

### 4. Correct `CLAUDE.md`

**Status:** ✅ Done — `0f184b63`. Row 9 green.

Rewrite the paragraph on `FieldMergeResolver.ValuesEqual` in *GUID/enum/id/Name/Title comparisons are
case-insensitive by default*: case-insensitive by default, except a quote's `quoteText` and `character`
(#374); every step that compares a quote — staging, listing, deciding — goes through `QuoteFieldMerge`.

### 5. Build and run the full suite

**Status:** ✅ Done — 4,143 tests passed across 11 projects, 0 warnings, 0 errors.

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 6. T2 pass

**Status:** ✅ Done — 2026-09-19, against an image built from `72e5d5e8`.

| Document | Result |
|---|---|
| *A case-only change is shown for review, and needs a decision* | Pass — `caseOnly=quoteText control=quoteText`, empty decide `422` naming `quoteText`, taking incoming `status=Decided quoteText=SURELY YOU CANNOT BE SERIOUS.`; 0 exceptions |
| *Baseline — health, version, random and search* | Pass, 0 exceptions |
| *The pagination contract holds live on every paginated endpoint* | Pass, 0 exceptions |
| *Reset wipes the entire database and does not reseed* | Pass |
| *Kestrel serves a wait page during initialisation* | Pass, 0 exceptions |
| *Notifications list, dismiss, render, and drive their action* | Pass, every step, driven in a browser |
| *The changelog is served from its own on-disk database* | Pass — step 5 read after the import line appears; see below |
| *The staged review → decide → apply workflow* | Pass, 0 exceptions |
| *Every seed and import surface reports per-file counts* | Pass, 0 exceptions |
| *Reviewing conflicted import actions throws nothing* | Pass, 0 exceptions |
| *A fresh seed resolves every bundled file with nothing left pending* | Steps 1–4 and 6 pass; step 5 cannot run until #400, as the document states |
| *A file left awaiting review raises an alert, and resolving it retires the alert* | Steps 1–5, 7, 9 and 8's *Done* half pass; step 9 settled through the notification's *Take incoming* — `DecideBatchAsync` — reaching `Applied` |
| *Bulk-deciding a staged batch via file export and re-import* | Not run past step 2 — see below |

**Every exception line is accounted for:** two `SocketException` per stop or restart (the listening
ports); the browser's WebSocket `OperationCanceledException` when *Notifications list, dismiss, render,
and drive their action* stopped its container with the page open; a stale-cookie
`CryptographicException` and `AntiforgeryValidationException` in the pending-review alert document's
browser steps; and that document's step 7 read-only-mount `IOException`, `SqliteException` and
`CryptographicException`, which the step documents as pre-existing.

**Found in documents this issue does not own:**

- *Bulk-deciding a staged batch via file export and re-import*, step 2, imports the curated file under
  `review` and expects `202`; it answers `200`, because since #373 an already-stored file stages
  nothing — the reason *The staged review → decide → apply workflow* moved to the conflict fixture. The
  document never reaches its own subject.
- *The changelog is served from its own on-disk database*, step 5, reads the log as soon as health
  answers, but after a restart the changelog import runs after that: its `refreshed` line appeared
  716 ms later.
- *Reset wipes the entire database and does not reseed* leaves the `-wal`/`-shm` files DbInspector
  creates beside `smoke156-before.db`.

Against a fresh build of the branch: the smoke set, the new document, and the documents that list or
decide staged actions — *The staged review → decide → apply workflow*, *Bulk-deciding a staged batch via
file export and re-import*, *A file left awaiting review raises an alert, and resolving it retires the
alert*, and *Reviewing conflicted import actions throws nothing*. Each container's log is read for
`[Runtime - Exception]` lines before it is removed.

### 7. T1 pass

**Status:** ✅ Done — 2026-09-19, the developer's Visual Studio run: the application started and reached
ready, upgrading a Data v3 / App v5 database to Data v22 / App v9.

The same run logged one `ObjectDisposedException` on a `NetworkStream`, 75 s after startup, with no
outgoing request made — the second occurrence, recorded under #370's T1 as unidentified. Not this
issue's; still unidentified.

---

## Process gap check

**Rules that existed and were not followed** — behavioural, no document change:

- `sed`, `grep`, `head` and `tail` in a shell, and a PowerShell regex rewrite of this plan — ADR 010.
- The first plan was not checked against the code before it was presented: a control that could not
  be set up, tests written before their signatures, and a missing fixture — `process.md`, *Every row must
  name a step someone can actually execute*, and `docs/testing-policy.md`, *Red first means signatures
  first*.
- Automated-test documents referred to by number — now a standing rule in memory.

**Genuine gaps, resolved in documents during this issue:**

- `process.md` listed the commit types `feat`, `fix`, `docs`, `chore` and `refactor`; the test commits
  of #370 and #409 used `test`, which it did not list. `test` is now defined there (developer
  decision, 2026-09-19).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The listing reports `quoteText` for a case-only text change | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_QuoteTextDiffersOnlyByCase_ReportsQuoteTextAsAmbiguous` |
| 2 | ✅ | The listing reports `character` for a case-only character change | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_CharacterDiffersOnlyByCase_ReportsCharacterAsAmbiguous` |
| 3 | ✅ | An empty decide on a case-only change is refused, naming the field | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteTextDiffersOnlyByCase_NoDecision_IsRefused` |
| 4 | ✅ | Taking incoming on a case-only change stores the incoming casing — positive half of row 3 | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteTextDiffersOnlyByCase_Replace_TakesIncomingText` |
| 5 | ✅ | The notification's batch decide settles a case-only change | Unit test | `SqliteImportActionServiceTests.DecideBatchAsync_QuoteTextDiffersOnlyByCase_DecidesIt` |
| 6 | ✅ | Under `merge-theirs`, the `POST /import` response reports a case-only `quoteText` as taken from the incoming side | Unit test | `QuoteImportServiceTests.ImportAsync_MergeTheirs_QuoteTextDiffersOnlyByCase_ReportsQuoteTextFromIncoming` |
| 7 | ✅ | A quote field outside the set still resolves a case-only difference on its own — control | Unit test | `QuoteFieldMergeTests.ResolveWithDecisions_SourceDiffersOnlyByCase_NeedsNoDecision` (new class) |
| 8 | ✅ | A case-only `quoteText` difference is unresolved without a decision | Unit test | `QuoteFieldMergeTests.ResolveWithDecisions_QuoteTextDiffersOnlyByCase_IsUnresolved` |
| 9 | ✅ | `CLAUDE.md` names the case-sensitive quote fields and the single entry point | Unit test | `QuoteFieldMergeTests.CaseSensitiveContentFields_AreDocumentedInClaudeMd` |
| 10 | ✅ | A genuine text change is listed exactly as before — control | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_PendingModifyConflicts_ReportsAmbiguousFieldsWithoutThrowing` (existing) |
| 11 | ✅ | In a running container, a case-only change is listed with `quoteText`, an empty decide is refused, and taking incoming decides the incoming casing | Live (T2) | [*A case-only change is shown for review, and needs a decision*](../../automated-testing/import-and-staged-actions/29-a-case-only-change-is-shown-for-review.md) passes on this branch's build and fails on the canary at its listing step |
| 12 | ✅ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
