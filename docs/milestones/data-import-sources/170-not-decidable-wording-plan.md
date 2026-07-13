# #170 — ImportActionNotDecidableException's message and doc comment are stale — still says "only Quote actions"

**Status:** In progress
**GitHub issue:** #170
**Tiers required:** T1, T2

---

## Spec requirements (from the GitHub issue)

1. `ImportActionNotDecidableException`'s class doc comment (`src/Quotinator.Engine/Services/ImportActionNotDecidableException.cs`)
   no longer claims "Source/Character/Person actions are always staged already-Decided" — that has been
   false since #162 shipped Source's own Modify/decide path — and is reworded generically so it never
   needs editing again as more entities (Character, Person, Conversation, StageDirection, SoundCue)
   become decidable.
2. The exception's constructor message no longer names `'Quote'` specifically ("only 'Quote' actions
   support a decision") and instead states the rule generically (e.g. "this action's entity type does
   not currently support a Modify decision").
3. `UI.en-GB.json`'s `ErrorImportActionNotDecidable` key is reworded to match the same generic rule,
   dropping "only Quote actions support a decision".
4. `UI.de.json`'s `ErrorImportActionNotDecidable` key is updated in lockstep with the English wording
   change, translated, not left stale.
5. `UI.nl.json`'s `ErrorImportActionNotDecidable` key is updated in lockstep with the English wording
   change, translated, not left stale.
6. New test `ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType` exists, is red
   before the fix, and green after.

---

## Steps

### 1. Write the red test

**Status:** ✅ Done, strengthened after initial review. `ImportActionNotDecidableExceptionTests.cs`
created; test confirmed red against current code (`actual: true` — message does contain "Quote").
The original single-case version only asserted the message *didn't* contain "Quote" — that alone
doesn't prove `entityType` is actually interpolated into the message; a bug that silently dropped
the parameter entirely (e.g. a hardcoded generic string with no interpolation at all) would still
have passed it. Strengthened to a `[DataRow("Source")]`/`[DataRow("Character")]` parameterized test
that also asserts the message contains the actual `entityType`/`actionId` values passed in, and that
`ActionId`/`EntityType` properties round-trip correctly — proving the parameter is genuinely used,
not just absent from a static string. **Canary-verified per direct developer instruction**: temporarily
mutated the constructor to a fully hardcoded message with no interpolation at all (simulating exactly
the bug class this strengthened test exists to catch), confirmed both `DataRow` cases fail with a
clear assertion message ("expected substring: 'Character' / actual: 'Import action cannot be
manually decided...'"), then reverted via `git checkout` and reconfirmed green — proves the test is a
genuine, sensitive red/green gate, not just logically reasoned to be one.

Add `ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType` to
`tests/Quotinator.Engine.Tests/Services/` (new file `ImportActionNotDecidableExceptionTests.cs`, mirroring
the existing `Services/` test files in that project — `QuoteImportServiceTests.cs`,
`SqliteImportActionServiceTests.cs` — for namespace and layout). Construct the exception directly with
an arbitrary `entityType` (e.g. `"Source"`) and assert `Message` does not contain the literal substring
`"Quote"` anywhere (case-insensitive `Contains` check) — proving the message no longer names a specific
entity type, only the entity type actually passed in. Confirm this test is red against the current
constructor text (`"... cannot be manually decided — only 'Quote' actions support a decision."`) before
making any fix.

### 2. Reword the exception's class doc comment and constructor message

**Status:** ✅ Done. Summary and message both generic now — no entity type named as the exception,
`entityType`/`actionId` still interpolated.

In `src/Quotinator.Engine/Services/ImportActionNotDecidableException.cs`:
- Rewrite the `<summary>` to state the rule generically — the exception is thrown when the action's
  entity type does not currently support a Modify decision — without asserting which specific entity
  types do or don't (that fact lives in `SqliteImportActionService.DecideAsync`'s own branching, not in
  this doc comment, and will keep changing as more entities gain decidability).
- Rewrite the constructor message to the same generic phrasing, still interpolating the actual
  `entityType` and `actionId` values (both already captured as properties) so the message stays
  specific to the actual failure without hardcoding `'Quote'` as the one exception.

### 3. Update `UI.en-GB.json`'s `ErrorImportActionNotDecidable` key

**Status:** ✅ Done.

Reword the English baseline string to match step 2's generic phrasing (still using the existing `{0}`
placeholder for the entity type). This becomes the source-of-truth wording for the `de`/`nl` translations
in the next step.

### 4. Update `UI.de.json` and `UI.nl.json` in lockstep

**Status:** ✅ Done — both translated and updated in the same commit as step 3.

Translate the reworded English string into German and Dutch, matching this project's changelog-lockstep
convention (CLAUDE.md's Localisation section: "every key that exists in `UI.en-GB.json` must exist
(non-empty) in every other file", enforced by `TranslationCompletenessTests`) — update both files in the
same commit as step 3, not deferred.

### 5. Verify no other call site or test still asserts the old wording

**Status:** ✅ Done, expanded beyond the original scope. The original three-exact-string grep
(`"only 'Quote'"`, `"only Quote actions"`, `"always staged already-Decided"`) returned no matches,
but a broader sweep (prompted by a direct question on whether "only the exception" was really the
full scope) found two more instances of the same stale claim in *different* wording that the narrow
grep missed:

- `IImportActionService.DecideAsync`'s own XML doc comment (the actual doc source —
  `SqliteImportActionService.DecideAsync`'s implementation only has `/// <inheritdoc/>`): "Throws
  `ImportActionNotDecidableException` for a Source/Character/Person action (always already-Decided;
  never a valid decide target)." Reworded to state the rule generically, matching the exception's
  own new wording.
- `ImportEndpoints.cs`'s `[Description]` attribute on the `POST /import/actions/{id}/decide` route —
  the text that actually feeds the OpenAPI spec and Scalar UI, arguably the most user-facing of all
  three locations found in this issue: "Only `Quote` actions can be decided — Source/Character/Person
  actions are always already-Decided... so targeting one returns `422`." Reworded generically. Also
  checked `README.md`/`addon/DOCS.md`'s own decide-endpoint rows per CLAUDE.md's "Keeping API
  documentation in sync" rule — both already said "Quote or Source action," already accurate, no
  change needed there.

A final broader grep (`only.{0,3}Quote|Quote.{0,10}only`) across all of `src/` confirmed no further
instances — the remaining hits are unrelated (`yearFrom`/`yearTo` "only quotes" filter descriptions,
an unrelated `GetOrCreateSourceAsync` method in `QuoteSeedWriter.cs`, changelog prose).

Grep the repo for the literal strings `"only 'Quote'"`, `"only Quote actions"`, and `"always staged
already-Decided"` to confirm no other file (test assertions, other doc comments) still depends on or
repeats the stale wording being replaced.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Exception message no longer names `'Quote'` as the only decidable entity type, and genuinely reports the actual `entityType`/`actionId` passed in (not a hardcoded string) | Unit test | `Quotinator.Engine.Tests.Services.ImportActionNotDecidableExceptionTests.ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType` (`[DataRow("Source")]`/`[DataRow("Character")]`) — confirmed red before the fix, green after |
| 2 | ✅ | Class doc comment no longer claims Source is "always staged already-Decided" | Live | Manual review of `src/Quotinator.Engine/Services/ImportActionNotDecidableException.cs`'s `<summary>` — Source-specific claim removed, replaced with generic wording |
| 3 | ✅ | `UI.en-GB.json`, `UI.de.json`, `UI.nl.json` all update the `ErrorImportActionNotDecidable` key in the same commit, none left stale | Unit test | `dotnet test --filter TranslationCompleteness` — 2/2 passing; manual diff review confirms all three wordings changed together |
| 4 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,162/1,162 passing (up from 1,161), 0 warnings, 0 errors, re-confirmed after the two additional fixes in step 5 |
| 5 | ✅ | No leftover reference to the stale wording anywhere in the repo, including differently-worded variants | Live | `grep -rn "only 'Quote'\|only Quote actions\|always staged already-Decided" src/ tests/` — no matches; broader sweep also caught and fixed `IImportActionService.DecideAsync`'s doc comment and `ImportEndpoints.cs`'s `[Description]` attribute, both worded differently from the three original strings; `README.md`/`addon/DOCS.md` already accurate |
| 6 | ✅ | Live: the reworded English error message is actually returned by the API for a real not-decidable action, and the smoke-test steps are themselves proven sensitive to the bug (canary-verified against a pre-fix build, per direct developer instruction) | Live (T2) | Docker smoke test: imported a fresh quote (stages a Source `Add`, already `Decided`), then `POST /api/v1/import/actions/{id}/decide` against that Add action's id — returned `422` with the new generic wording, no "Quote"-specific text. Canary: rebuilt the identical image from `a4ec145` (the commit immediately before the fix, via an isolated `git worktree`) and ran the *exact same* repro steps — confirmed the old `"only Quote actions support a decision"` text actually appears there, proving these smoke-test steps would have caught the original bug rather than happening to already avoid it. Canary container/image/worktree torn down afterward. |
| 7 | ❌ | App still opens and builds in Visual Studio | Live (T1) | Developer confirms the app starts cleanly in Visual Studio after the change |

---

## Notes

T1 and T2 are both required per this project's blanket rule (T1/T2 are never exempted except for pure
documentation-only changes — this is a real C# code change, not docs-only, even though it's small).
This issue is scoped as an isolated wording fix — it does not add or change any decidability behaviour
itself, only the message/doc comment describing that behaviour, so no changes to
`SqliteImportActionService.DecideAsync` or `ImportActionPlanner` are expected.
