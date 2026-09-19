# #410 — Deciding a Quote Add action answers 500 instead of an outcome

**Status:** Waiting for release
**GitHub issue:** #410
**Tiers required:** T1, T2
**Depends on:** #370

---

## Next action

Wait for the release; then the *Released* checklist and the closing comment.

---

## Description

`SqliteImportActionService.DecideAsync` treats every Quote action as a Modify and deserializes its
`ExistingValue`. A Quote Add has none — or, when Blocked, only a `{ conflictingQuoteId }` marker — so
deciding one throws and the endpoint answers `500`. Every other entity type's Add reaches
`NotDecidable`, including one already applied, which should read as already resolved.

## Scope changes

Agreed 2026-09-19, recorded on the issue:

- Every Add, of every entity type and in every state, answers a defined outcome and never throws.
- A held Quote Add — Pending, Stale or Blocked — answers `422`, saying it is resolved by correcting the
  imported file or adding a rule. Only a Quote Add can be held: every other type's Add is staged
  `Decided`.
- Recording *why* an item was held, and resolving it from the interface by creating the rule, is #416.

## Decisions

- **One new outcome, `HeldForReview`.** A held Add is not "not decidable" in the sense of an
  unsupported kind: it is waiting on the file or a rule, and the message says so.
- **The resolved-state check moves to the top of `DecideAsync`**, before any branch deserializes
  anything, so an applied or discarded action of any kind answers `AlreadyResolved`. For a Modify the
  outcome is unchanged — the coordinator already returned it, later.
- **`NotDecidable` names the action kind as well as the entity type.** Its text says the entity type
  "does not currently support a Modify decision", which is false for a Quote Add, whose type does. It
  becomes *Import action '…' is a 'Quote' Add action and cannot be manually decided.* — true for an Add
  of any type and for a Season Modify alike.
- **Every way a Quote Add is held gets its own decide test and its own live check.** Staging each is
  already unit-tested — a second date (`ResolveSourceAsync_ReviewPolicy_SecondDateForAKnownTitle_StagesPending`),
  a Keep/Replace rule matching nothing stored
  (`Seed_WithAKeepRuleMatchingABrandNewQuote_StagesPendingForReviewRatherThanApplyingOrIgnoring`), a stale
  alias (`PlanAsync_SourceAliasStale_CanonicalSourceRenamedAway_AddPathStagesQuoteAsStale`) and a duplicate of
  a stored quote (`GetPagedAsync_BlockedCollisionAgainstAnExistingQuote_DoesNotCrash`) — except a duplicate
  within the same file, which gains one. Deciding none of them is tested, and no automated-test document
  covers a held Add at all.
- **The stale Add is not run live.** Reaching it needs a stored source renamed away from the title its
  generated id was hashed from; a live test would have to know that id before the seed runs, and a later
  file cannot be imported into a running container (#368, #390). Its unit test covers it.
- **The existing message test keeps its purpose.** `Describe_EachOutcome_MatchesTheMessageItReplaces`
  proves the #370 outcomes kept the exception texts; `NotDecidable` leaves it deliberately, asserted by
  its own test instead.

---

## Steps

### 1. Add the signatures

**Status:** ✅ Done — `c98522c8`. The build is clean; `ApiMessages.cs` joined the scoped IDE0090 list
with nothing to convert.

`ImportActionDecideOutcome.HeldForReview`; `ImportActionDecideResult.HeldForReview(actionId, status)`;
`ImportActionDecideResult.NotDecidable` gains an `actionType` parameter, its one caller passing
`action.ActionType.Raw`. `Describe` and the endpoint are left as they are, so the new outcome falls
through to their existing arms. The `ErrorImportActionHeldForReview` message in English, Dutch and
German, and its `ApiMessages` constant, so `TranslationCompletenessTests` stays green.

### 2. Write every test and the automated document, and run each red

**Status:** ✅ Done — every planned red test fails, each after its precondition assert passed; rows 9,
10 and 11 green.

| Row | Red on |
|---|---|
| 1, 2, 3, 5, 6, 7, 12 | `DecideAsync` throws `ArgumentNullException` deserializing the Add's absent `ExistingValue` |
| 4 | `DecideAsync` throws `JsonException` deserializing the blocked Add's `{ conflictingQuoteId }` marker as a quote |
| 8 | `NotDecidable` instead of `AlreadyResolved`, for both `Source` and `Character` |
| 13, 14 | `Describe` returns empty for `HeldForReview`, and the old `NotDecidable` text |
| 15 | The endpoint's unmatched arm answers with the ambiguous-fields message |
| 16 | The `422` names no action kind |

*Deciding an import Add answers an outcome, never a server error*, against a canary image of `af7f9498`:
all five cases staged as intended — `applied=Add/Applied`, `secondDate` and `ruleOnNothing` `Pending`,
`storedDuplicate` and `fileDuplicate` `Blocked` — and every decide answered `500`, with 71 exception
lines logged. Container, bind folder and image removed.

The tests in the Verification checklist. Rows 9, 10 and 11 are controls or coverage of existing
behaviour and start green. Fixtures, each following the staging test already named in *Decisions*:

- **Pending, second date** — two new quotes from one TV source title under two dates, under
  `newest-wins`, as `GetPagedAsync_PendingAdd_AmbiguousFieldsIsEmpty` does.
- **Pending, rule matching nothing stored** — a new quote with a `keep` conflict rule for its id.
- **Stale** — a source stored under a title renamed away from its alias's canonical one, as the stale
  staging test does.
- **Blocked, duplicate of a stored quote** — a quote imported and applied, then a second id with the same
  text and source.
- **Blocked, duplicate within the same file** — two ids with the same text and source in one file.
- **Applied** — `StageAndApplyAsync`.

The automated document seeds a fresh container of its own from a user-imports manifest — a base file,
then a file carrying every live variant under `review`, with its own rule file — and decides each. It
declares `Quotinator__IncludeDefaultSources=false`, so the database holds only its own content, and
`Quotinator__AutoPurgeUserImportActions=false`: the setting defaults to `true`, which would delete the
applied base batch's actions and leave no applied Add to decide. It is run against an
image of this branch after step 1, the canary, and fails at its first decide.

### 3. Answer every Add with an outcome

**Status:** ✅ Done — `b5bcb93b`. Every #410 test green, and the build clean.

`HeldForReview` is returned for a held Add of **any** entity type, not only a Quote: only Quote Adds
are held today, and the developer's rule — resolved by correcting the file or adding a rule — applies
to every importable type, so a type that starts holding Adds needs no second change here.

`DecideAsync`: after the not-found check, `Applied`/`Discarded` returns `AlreadyResolved`; an Add in
`Pending`, `Stale` or `Blocked` returns `HeldForReview`; any other Add returns `NotDecidable`.
`Describe` gains the `HeldForReview` text and the new `NotDecidable` text. The decide endpoint maps
`HeldForReview` to `422` with its message, and passes the action kind to `NotDecidable`'s. The
`NotDecidable` message changes in all three languages. `IImportActionService`'s outcome list, the
endpoint's `WithDescription` and `docs/api-endpoints.md` name the new `422`.

### 4. Build and run the full suite

**Status:** ✅ Done — 4,157 tests passed across 11 projects, 0 warnings, 0 errors.

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 5. T2 pass

**Status:** ✅ Done — 2026-09-19, against an image built from `dec09367`.

| Document | Result |
|---|---|
| *Deciding an import Add answers an outcome, never a server error* | Pass — `applied`, `secondDate`, `ruleOnNothing`, `storedDuplicate`, `fileDuplicate` staged as intended; each decide `422`, the applied one "already resolved" and the rest "held for review"; `before=0 after=0` |
| *Baseline — health, version, random and search* | Pass, 0 exceptions |
| *The pagination contract holds live on every paginated endpoint* | Pass, 0 exceptions |
| *Kestrel serves a wait page during initialisation* | Pass, 0 exceptions |
| *Reset wipes the entire database and does not reseed* | Pass |
| *The changelog is served from its own on-disk database* | Pass — step 5 read after the import line appears (#411) |
| *The staged review → decide → apply workflow* | Pass, 0 exceptions |
| *A fresh seed resolves every bundled file with nothing left pending* | Steps 1–4 and 6 pass; step 5 waits on #400, as the document states |
| *Every seed and import surface reports per-file counts* | Pass, 0 exceptions |
| *Reviewing conflicted import actions throws nothing* | Pass, `before=0 after=0` |
| *A case-only change is shown for review, and needs a decision* | Pass, 0 exceptions |
| *Notifications list, dismiss, render, and drive their action* | Pass, every step, driven in a browser |
| *A file left awaiting review raises an alert, and resolving it retires the alert* | Steps 1–5, 7, 9 and 8's *Done* half pass; 6 and 8's other half as #411 records |

**Every exception line is accounted for:** two `SocketException` per stop or restart (the listening
ports); the WebSocket `OperationCanceledException` of a page open during a stop; a stale-cookie
`CryptographicException` and `AntiforgeryValidationException` in the browser-driven documents; and the
read-only-mount `IOException`, `SqliteException` and `CryptographicException` the pending-review alert
document's step 7 records as pre-existing.

Against a fresh build of the branch: the smoke set, the new document, and the documents that decide
staged actions — *The staged review → decide → apply workflow*, *A file left awaiting review raises an
alert, and resolving it retires the alert*, *Reviewing conflicted import actions throws nothing* and *A
case-only change is shown for review, and needs a decision*. Each container's log is read for
`[Runtime - Exception]` lines before it is removed.

### 6. T1 pass

**Status:** ✅ Done — 2026-09-19, the developer's Visual Studio run: the application started and reached
ready, upgrading a Data v3 / App v5 database to Data v22 / App v9. The reseeds and the reset that
followed logged no exception.

---

## Process gap check

**Rules that existed and were not followed** — behavioural, no document change:

- The first plan was presented before the existing coverage was checked; the developer asked for every
  held variant to have unit and automated tests, which the Steps and Verification table now carry —
  `process.md`, *Every row must name a step someone can actually execute*, and `docs/testing-policy.md`'s
  fix-the-class rule.
- DbInspector was run with `--no-build` where its documents run it without, and the step had to be
  repeated.

**Genuine gaps, each owned by an issue:**

- A problem in incoming content is resolved by correcting the content or by a rule, and a resolution
  chosen in the interface creates that rule (developer decision, 2026-09-19). Nothing documents it yet;
  its ADR is #416's first requirement.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Deciding a Quote Add held over a second date answers held-for-review and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteAddHeldOverASecondDate_ReturnsHeldForReviewWithoutThrowing` |
| 2 | ✅ | Deciding a Quote Add held by a rule matching nothing stored answers held-for-review and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteAddHeldByARuleMatchingNothing_ReturnsHeldForReviewWithoutThrowing` |
| 3 | ✅ | Deciding a stale Quote Add answers held-for-review and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_StaleQuoteAdd_ReturnsHeldForReviewWithoutThrowing` |
| 4 | ✅ | Deciding a Quote Add blocked as a duplicate of a stored quote answers held-for-review and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteAddBlockedAsADuplicateOfAStoredQuote_ReturnsHeldForReviewWithoutThrowing` |
| 5 | ✅ | Deciding a Quote Add blocked as a duplicate within the same file answers held-for-review and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_QuoteAddBlockedAsADuplicateWithinItsFile_ReturnsHeldForReviewWithoutThrowing` |
| 6 | ✅ | Deciding an applied Quote Add answers already-resolved and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_AppliedQuoteAdd_ReturnsAlreadyResolvedWithoutThrowing` — the issue's `DecideAsync_QuoteAddAction_ReturnsAnOutcomeWithoutThrowing`, named for its case |
| 7 | ✅ | Deciding a decided, unapplied Quote Add answers not-decidable and throws nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_DecidedQuoteAdd_ReturnsNotDecidableWithoutThrowing` |
| 8 | ✅ | Deciding an applied Add of another type answers already-resolved | Unit test | `SqliteImportActionServiceTests.DecideAsync_AppliedNonQuoteAdd_ReturnsAlreadyResolved` (`Source`, `Character`) |
| 9 | ✅ | Deciding a decided Add of another type still answers not-decidable — control | Unit test | `SqliteImportActionServiceTests.DecideAsync_NonQuoteAction_ReturnsNotDecidableWithoutThrowing` (existing) |
| 10 | ✅ | A Quote Modify still decides — control | Unit test | `SqliteImportActionServiceTests.DecideAsync_AllFieldsDecided_ReturnsDecided` (existing) |
| 11 | ✅ | Two quotes with the same text and source in one file stage the second as Blocked — coverage of existing behaviour | Unit test | `ImportActionPlannerTests.PlanAsync_TwoQuotesWithTheSameTextAndSourceInOneFile_StagesTheSecondBlocked` |
| 12 | ✅ | A held Add is reported as a bulk-decide row error, not thrown | Unit test | `SqliteImportActionServiceTests.BulkDecideAsync_HeldQuoteAdd_ReportedAsRowErrorWithoutThrowing` |
| 13 | ✅ | The held-for-review text says the item is resolved by correcting the file or adding a rule | Unit test | `ImportActionDecideResultTests.Describe_HeldForReview_NamesBothWaysToResolveIt` |
| 14 | ✅ | The not-decidable text names the action kind and makes no claim about Modify support | Unit test | `ImportActionDecideResultTests.Describe_NotDecidable_NamesTheActionKind`; `Describe_EachOutcome_MatchesTheMessageItReplaces` loses its `NotDecidable` line |
| 15 | ✅ | The decide endpoint answers a held Add with `422` and its message | Unit test | `ImportActionEndpointsTests.DecideAction_HeldForReview_Returns422WithItsMessage` — the issue's `DecideAction_QuoteAdd_Returns422` |
| 16 | ✅ | The decide endpoint's not-decidable `422` names the action kind | Unit test | `ImportActionEndpointsTests.DecideAction_NotDecidable_Returns422` (existing, updated) |
| 17 | ✅ | In a running container, deciding an applied Add and a Quote Add held in each live-reachable way — a second date, a rule matching nothing stored, a duplicate of a stored quote, a duplicate within its file — answers `422` each, with nothing thrown | Live (T2) | *Deciding an import Add answers an outcome, never a server error* (`automated-testing/import-and-staged-actions/30-deciding-an-add-answers-an-outcome.md`) passes on this branch's build and fails on the canary at its first decide |
| 18 | ✅ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
