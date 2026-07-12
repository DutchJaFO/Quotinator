# #170 — ImportActionNotDecidableException's message and doc comment are stale — still says "only Quote actions"

**Status:** Planning
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

**Status:** Not started.

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

**Status:** Not started.

In `src/Quotinator.Engine/Services/ImportActionNotDecidableException.cs`:
- Rewrite the `<summary>` to state the rule generically — the exception is thrown when the action's
  entity type does not currently support a Modify decision — without asserting which specific entity
  types do or don't (that fact lives in `SqliteImportActionService.DecideAsync`'s own branching, not in
  this doc comment, and will keep changing as more entities gain decidability).
- Rewrite the constructor message to the same generic phrasing, still interpolating the actual
  `entityType` and `actionId` values (both already captured as properties) so the message stays
  specific to the actual failure without hardcoding `'Quote'` as the one exception.

### 3. Update `UI.en-GB.json`'s `ErrorImportActionNotDecidable` key

**Status:** Not started.

Reword the English baseline string to match step 2's generic phrasing (still using the existing `{0}`
placeholder for the entity type). This becomes the source-of-truth wording for the `de`/`nl` translations
in the next step.

### 4. Update `UI.de.json` and `UI.nl.json` in lockstep

**Status:** Not started.

Translate the reworded English string into German and Dutch, matching this project's changelog-lockstep
convention (CLAUDE.md's Localisation section: "every key that exists in `UI.en-GB.json` must exist
(non-empty) in every other file", enforced by `TranslationCompletenessTests`) — update both files in the
same commit as step 3, not deferred.

### 5. Verify no other call site or test still asserts the old wording

**Status:** Not started.

Grep the repo for the literal strings `"only 'Quote'"`, `"only Quote actions"`, and `"always staged
already-Decided"` to confirm no other file (test assertions, other doc comments) still depends on or
repeats the stale wording being replaced.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Exception message no longer names `'Quote'` as the only decidable entity type | Unit test | `Quotinator.Engine.Tests.Services.ImportActionNotDecidableExceptionTests.ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType` — red before the fix, green after |
| 2 | ❌ | Class doc comment no longer claims Source is "always staged already-Decided" | Live | Manual review of `src/Quotinator.Engine/Services/ImportActionNotDecidableException.cs`'s `<summary>` — confirm the Source-specific claim is removed and replaced with generic wording |
| 3 | ❌ | `UI.en-GB.json`, `UI.de.json`, `UI.nl.json` all update the `ErrorImportActionNotDecidable` key in the same commit, none left stale | Unit test | `dotnet test --filter TranslationCompleteness` passes (all three locale files have a non-empty key); manual diff review confirms all three wordings were actually changed together, not just `en-GB.json` |
| 4 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — all tests passing, 0 warnings, 0 errors |
| 5 | ❌ | No leftover reference to the stale wording anywhere in the repo | Live | `grep -rn "only 'Quote'\|only Quote actions\|always staged already-Decided" src/ tests/` returns no matches |
| 6 | ❌ | Live: the reworded English error message is actually returned by the API for a real not-decidable action | Live (T2) | Docker smoke test: stage an already-`Decided` (or non-`Quote`, non-decidable) action and call `POST /api/v1/import/actions/<id>/decide` against it — response body's error `detail` shows the new generic wording, not the old `'Quote'`-specific text |
| 7 | ❌ | App still opens and builds in Visual Studio | Live (T1) | Developer confirms the app starts cleanly in Visual Studio after the change |

---

## Notes

T1 and T2 are both required per this project's blanket rule (T1/T2 are never exempted except for pure
documentation-only changes — this is a real C# code change, not docs-only, even though it's small).
This issue is scoped as an isolated wording fix — it does not add or change any decidability behaviour
itself, only the message/doc comment describing that behaviour, so no changes to
`SqliteImportActionService.DecideAsync` or `ImportActionPlanner` are expected.
