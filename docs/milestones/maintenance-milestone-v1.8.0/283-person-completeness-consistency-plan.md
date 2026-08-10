# #283 — PersonResponse.CompletenessStatus is nullable with no fallback, unlike every other masterdata entity

**Status:** In progress
**GitHub issue:** #283
**Tiers required:** T1, T2
**Depends on:** none (isolated fix inside `PersonResponse`/`PersonEndpoints`)

---

## Spec requirements

1. `PersonResponse.CompletenessStatus` becomes `required CompletenessStatus` (non-nullable), matching
   every other masterdata response DTO.
2. `PersonEndpoints.ToResponse` falls back to `CompletenessStatus.Incomplete` when the entity's
   `CompletenessStatus.Parsed` is `null`, matching every sibling endpoint's `?? CompletenessStatus.
   Incomplete` mapping.
3. `PersonEndpoints.GetAll` checks `PaginationParsing.ValidatePageBeyondLast` and returns early
   *before* mapping `result.Items` into response DTOs, matching all 7 sibling `GetAll` handlers — a
   drive-by consistency fix, no behaviour change (the final returned 422 is identical either way).

---

## Background — why this issue exists

Discovered while investigating #281 (masterdata CRUD endpoint duplication). Every one of the 8
entity tables — including `People` — enforces `CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'`
via an identical CHECK constraint (`QuotinatorMigrations.cs`), and the enum's own doc comment confirms
`Incomplete` means "nothing known yet — the default for every newly created row." There is no DB-level
"not yet assessed" state that differs for Person. `PersonResponse.CompletenessStatus`'s nullability and
its doc comment ("Null when not yet assessed") describe a state that cannot occur under normal data —
the other 7 entities' `?? CompletenessStatus.Incomplete` fallback is the correct, established contract;
Person is the outlier.

**Verified before starting:**

- Confirmed via `QuotinatorMigrations.cs`: every `CompletenessStatus` column across all 8 tables is
  `TEXT NOT NULL DEFAULT 'Incomplete'` with the identical CHECK constraint — no exceptions.
- Confirmed via `PersonEntity.cs`: the entity-level property is `SafeValue<CompletenessStatus?>
  CompletenessStatus`, identical in type to all 7 sibling entities — the divergence is purely at the
  response DTO / mapping layer, not the entity layer.
- Confirmed no existing test exercises the `?? Incomplete` fallback for *any* of the 8 entities
  (`CharacterEndpointsTests.NewCharacter` always constructs a valid, parseable `CompletenessStatus` —
  never `SafeValue<CompletenessStatus?>.Empty`) — this fix adds the first test coverage for this
  fallback behaviour project-wide, using `PersonEndpointsTests`' existing `NewPerson`/
  `FakePersonRepository` fixture.

---

## Approach

1. `src/Quotinator.Core/Models/PersonResponse.cs`: change `public CompletenessStatus? CompletenessStatus { get; init; }` to `public required CompletenessStatus CompletenessStatus { get; init; }`, matching `CharacterResponse`/`SourceResponse`/etc.'s doc-comment style (no more "Null when not yet assessed").
2. `src/Quotinator.Api/Endpoints/PersonEndpoints.cs`, `ToResponse`: add `?? CompletenessStatus.Incomplete` to the `CompletenessStatus` assignment.
3. Same file, `GetAll`: move the `PaginationParsing.ValidatePageBeyondLast` check (and its early return) to run immediately after `GetPageAsync`, before mapping `result.Items` — matching `CharacterEndpoints.GetAll`'s structure.

---

## Files touched

- `src/Quotinator.Core/Models/PersonResponse.cs`
- `src/Quotinator.Api/Endpoints/PersonEndpoints.cs`
- `tests/Quotinator.Api.Tests/Endpoints/PersonEndpointsTests.cs` — one new test

---

## Steps

### 1. Write the failing test (red)
**Status:** ✅ Done — `PersonEndpointsTests.GetPersonById_CompletenessStatusUnparseable_ReturnsIncompleteNotNull`
confirmed red against pre-fix code (assertion failure — `completenessStatus` property absent from the
response).

### 2. Implement the fix
**Status:** ✅ Done — per Approach above: `PersonResponse.CompletenessStatus` is now `required
CompletenessStatus`, `PersonEndpoints.ToResponse` falls back to `Incomplete`, and `GetAll`'s
beyond-last-page check now runs before mapping, matching `CharacterEndpoints.GetAll`.

### 3. Verify
**Status:** ✅ Done — new test green. No canary needed (asserts a specific present value, not an
absence). Full solution build: 0 warnings/0 errors. Full solution test suite: 3284/3284 passed, 0
failed (up from 3283 — exactly the 1 new test), across all 10 test projects.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Unparseable/empty CompletenessStatus returns "Incomplete", not null/omitted | Unit test | `PersonEndpointsTests.GetPersonById_CompletenessStatusUnparseable_ReturnsIncompleteNotNull` |
| 2 | ✅ | `PersonEndpoints.GetAll` checks beyond-last-page before mapping items | Code review | `PersonEndpoints.cs` diff matches `CharacterEndpoints.cs`'s structure |
| 3 | ✅ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — 3284/3284 passed |
| 4 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 5 | ⬜ | T2 — live container serves a Person with Incomplete status correctly | Live (T2) | Docker smoke test |

---

## Notes

None yet.
