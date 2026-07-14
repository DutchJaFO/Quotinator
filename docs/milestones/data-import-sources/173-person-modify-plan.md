# #173 — Person: explicit id, Modify/decidability, wire up dateOfBirth/dateOfDeath

**Status:** In progress
**GitHub issue:** #173
**Tiers required:** T1, T2
**Depends on:** #162, #165, #168 (shipped patterns this builds on)

---

## Spec requirements (from the GitHub issue)

1. `schemas/source-extended.schema.json` gains a `people` array section + `person` `$def`: `id`
   (required, UUID v4 pattern, same regex as `source`/`stageDirection`/etc.), `name` (required),
   `dateOfBirth` (optional, imprecise ISO 8601 string, same convention as `SourceEntry.Date`),
   `dateOfDeath` (optional, same convention). Purely additive — a file without a `people` section
   parses identically to today.
2. New `PersonEntry.cs` record in `Quotinator.Core.Import`, doc-commented like `SourceEntry`.
   `ParsedSourceFile` gains `People` (defaults `[]`). `SourceQuoteFileReader.TryParseExtended` gains
   the new root-key parse.
3. `Sql.People` (`src/Quotinator.Engine/Queries/Sql.cs`) gains `SelectExistingById` (returns `Name`,
   `DateOfBirth`, `DateOfDeath`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById`.
4. New `PlanPeopleAsync` (`src/Quotinator.Engine/Database/ImportActionPlanner.cs`), run before the
   quote loop, mirroring `PlanSourcesAsync`'s shape: id-match lookup → field-map diff (`name`,
   `dateOfBirth`, `dateOfDeath`) → unchanged-check (silent reuse) → policy-based resolution →
   `CompletenessGuard.ShouldBlock` evaluated against the policy-**resolved** value → stage `Blocked`
   or `Modify`. Falls back to the existing natural-key lookup (`Sql.People.SelectIdByName`) when no
   id match — a not-yet-declared-by-id row found this way stays Add-only/natural-key-matched, same
   scope boundary as Source's own natural-key fallback. A person discovered only implicitly through a
   Quote's `author` string (no explicit `people[]` entry) stays Add-only forever, same rule.
5. `ApplyResolvedActionAsync`'s Person case splits on `ActionType`: `Add` unchanged; `Modify` calls
   the new `Sql.People.UpdateFieldsById` against `MergedFields` — the first write path that ever
   populates `DateOfBirth`/`DateOfDeath`.
6. `DecideAsync` gains an `EntityType == Person && ActionType == Modify` branch, mirroring Source's
   branch shape.
7. `ComputeAmbiguousFields` gains a `Person` case.
8. `ReverseAppliedActionsAsync`'s Person case splits on `ActionType`: `Add` keeps today's
   soft-delete-if-unreferenced; `Modify` restores `Name`/`DateOfBirth`/`DateOfDeath` via
   `UpdateFieldsById` from `ExistingValue`.
9. `ClearStaleAddTargetsAsync`'s Person cleanup branch currently uses the Guid-typed repository path
   (`_personRepository.HardDeleteAsync(Guid.Parse(...))`), correct today only because every Person id
   is `EntityIdentity`-derived (always uppercase). This issue **must** switch it to the raw-SQL,
   case-preserving pattern (`RepositorySql.HardDelete("People")`), the same fix #162 made for Source
   — an explicit `people[]` id is file-authored and not guaranteed uppercase. Non-optional part of
   this issue's scope, not a nice-to-have.
10. `ConflictDecisionRequest` gains `PersonName`, `PersonDateOfBirth`, `PersonDateOfDeath` (nullable
    `FieldDecision?`).

---

## Implementation notes from reading the current code (2026-07-12)

- **`Sql.People` today has no update query at all** — confirmed: `CountActive`, `DeleteAll`,
  `SelectIdByName`, `CountActiveReferences`, `InsertIfNotExists` only (`Sql.cs:219-233`). Matches
  spec item 3 exactly; nothing to preserve/rename, purely additive.
- **`DateOfBirth`/`DateOfDeath` are confirmed dead today** — `grep -rn "DateOfBirth|DateOfDeath"`
  across `src/`/`tests/` hits only the entity property (`Person.cs:15,18`), the two migration/baseline
  `CREATE TABLE` blocks (`QuotinatorMigrations.cs:90-91,469-470`), and `InsertIfNotExists`, which
  hardcodes both to `NULL` on every Add (`Sql.cs:232`). No read or write path touches them anywhere
  else. `PersonActionPayload` today is `internal sealed record PersonActionPayload(string Name);` —
  single field, used by `ResolvePersonAsync` (`ImportActionPlanner.cs:274`), `ApplyResolvedActionAsync`'s
  Person case (`SqliteImportActionService.cs:531`), and `ToFieldMap(PersonActionPayload)`
  (`SqliteImportActionService.cs:830`).
- **Field type precedent for the two date columns**: `Person.DateOfBirth`/`DateOfDeath` are
  `SafeValue<DateTime?>` on the entity, same as `Source.Date` (`Source.cs:19`) — yet
  `SourceActionPayload.Date` is a plain `string?` (`ImportActionPlanner.cs:498`), and
  `Sql.Sources.SelectExistingById` reads `Date` as a raw `string?` via the Dapper tuple projection
  (`ImportActionPlanner.cs:300`), never round-tripping through `DateTime`/`SafeValue`. `PersonActionPayload`
  and `Sql.People.SelectExistingById` must follow the identical convention: `DateOfBirth`/`DateOfDeath`
  as plain nullable strings throughout the payload/planner/apply path, exactly like `Source.Date`.
- **`ResolvePersonAsync` (`ImportActionPlanner.cs:248-280`) is pure natural-key lookup today** — no id
  branch, no `CompletenessGuard` call, unconditionally stages `Add`. `PlanPeopleAsync` is a **new**
  method, not a rewrite of an existing one — structurally this issue is closer to #162's original
  "`PlanSourcesAsync` is a from-scratch addition" shape than #171/#172's "rewrite an existing Add-only
  planner" shape. `ResolvePersonAsync` itself is not deleted — it remains the no-`people[]`-entry
  fallback path exactly as `ResolveSourceAsync` remains Source's, called after `PlanPeopleAsync` has
  had a chance to populate `personIndex` for anything explicitly declared.
- **`personIndex` threading risk — same class of gap #162 found via its own test 7a**
  (`PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId`, see
  `162-source-explicit-id-plan.md` row 7a). `ResolvePersonAsync` already checks
  `index.TryGetValue(q.Author, ...)` before touching the database (`ImportActionPlanner.cs:254`) —
  `PlanPeopleAsync` **must** populate `personIndex[s.Name] = s.Id` in both its id-matched and
  natural-key-fallback branches (mirroring `PlanSourcesAsync`'s `sourceIndex[$"{s.Title}|{typeStr}"] =
  s.Id` at lines 312 and 383), or a same-batch quote whose `author` matches a `people[]` entry will
  independently derive a second, wrong `EntityIdentity.PersonId` instead of resolving to the declared
  row. The issue's own "Expected tests" table does not list a dedicated regression test for this (10
  tests only) — treat verifying this specific interaction as part of implementing step 5 correctly,
  not a separate checklist line; add a test for it only if implementation surfaces the same gap #162
  hit.
- **`ClearStaleAddTargetsAsync`'s current Person branch** (`SqliteImportActionService.cs:211-212`):
  `foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Person)) await
  _personRepository.HardDeleteAsync(Guid.Parse(action.EntityId));` — confirmed still on the Guid-typed
  path, exactly as spec item 9 describes. The Source branch immediately above it (lines 204-209) is
  the already-fixed template: `await quoteConn.ExecuteAsync(RepositorySql.HardDelete("Sources"), new {
  id = action.EntityId });`. Swap the Person branch to the identical shape against `"People"`.
- **`ReverseAppliedActionsAsync`'s current Person case** (`SqliteImportActionService.cs:357-362`)
  unconditionally does the active-reference check + soft-delete regardless of `ActionType` — needs the
  same `Add`/`Modify` split Source already has at lines 331-356 (the `Modify` branch there is the exact
  template: deserialize `ExistingValue`, `UpdateFieldsById`, log, `break` before the reference-count
  check).
- **`DecideAsync`'s gating** (`SqliteImportActionService.cs:90-111`): Source's Modify branch is
  checked first, then anything that isn't `Quote` is rejected. The new Person branch is added the same
  way — before the `!= Quote` rejection, alongside the Source check.
- **`ComputeAmbiguousFields`'s switch** (`SqliteImportActionService.cs:850-868`) only has `Quote` and
  `Source` cases today; every other entity type — including `Person` currently — falls through to the
  `default: return [];`. A `Person` case needs adding, following the existing `Source` case's shape
  exactly (deserialize both sides as `PersonActionPayload`, `ToFieldMap` each).
- **`ConflictDecisionRequest`** (`ConflictDecisionRequest.cs:37-44`) has `SourceTitle`/`SourceType`/
  `SourceDate` today; `PersonName`/`PersonDateOfBirth`/`PersonDateOfDeath` go immediately after, same
  nullable-`FieldDecision?`-with-doc-comment convention, referencing `#173`.
- **Schema template**: `schemas/source-extended.schema.json`'s `source` `$def` (lines 78-88) — `id`
  (UUID v4 pattern), `title`/`type` required, `date` optional nullable string — is the direct template
  for a new `person` `$def`: `id` (same pattern), `name` (required, `minLength: 1`), `dateOfBirth`
  (optional, `["string", "null"]`), `dateOfDeath` (same). A new top-level `people` array property is
  added alongside the existing `sources`/`stageDirections`/`soundCues`/`conversations` properties
  (lines 15-34), same `"items": { "$ref": "#/$defs/person" }` shape.
- **Reader wiring**: `SourceQuoteFileReader.TryParseExtended` (`SourceQuoteFileReader.cs:75-83`) has
  one `root?["X"]?.Deserialize<List<T>>(Options) ?? []` line per section — add
  `People = root?["people"]?.Deserialize<List<PersonEntry>>(Options) ?? []` alongside the existing
  four. `ParsedSourceFile` (`ParsedSourceFile.cs`) gains a `People` property with the same
  `IReadOnlyList<PersonEntry> People { get; init; } = [];` shape as `Sources`.

---

## Steps

### 1. Write the red tests

**Status:** ✅ Done.

Add the ten tests from the issue's "Expected tests" table across `Quotinator.Core.Tests`
(`SourceQuoteFileReaderTests.cs` for the parsing test) and `Quotinator.Engine.Tests`
(`ImportActionPlannerTests.cs` for the four planning tests, `SqliteImportActionServiceTests.cs` for
the apply/decide/reverse/stale-cleanup tests — matching where #162's/#171's equivalent Source/
StageDirection tests live). Confirm all ten fail against current (pre-implementation) code, per this
project's red-before-green policy.

### 2. Add the `person` schema `$def` and `people` array section

**Status:** ✅ Done.

`schemas/source-extended.schema.json`: add a `people` property to the top-level `properties` block
(alongside `sources`, lines 15-19) with description text mirroring `sources`'s own; add a `person`
entry to `$defs` (alongside `source`, lines 78-88) with `id`/`name` required, `dateOfBirth`/
`dateOfDeath` optional nullable strings, `additionalProperties: false`.

### 3. Add `PersonEntry.cs` and wire it into `ParsedSourceFile`/`SourceQuoteFileReader`

**Status:** ✅ Done.

New `src/Quotinator.Core/Import/PersonEntry.cs`, doc-commented like `SourceEntry.cs` ("Assigned at
authoring time and never changes" for `Id`): `Id` (required `string`), `Name` (required `string`),
`DateOfBirth` (`string?`), `DateOfDeath` (`string?`), all with `[JsonPropertyName]` matching the
schema's camelCase wire names. `ParsedSourceFile` gains `People` (defaults `[]`).
`SourceQuoteFileReader.TryParseExtended` gains the `people` root-key parse line, same shape as the
existing four.

### 4. Add the `Sql.People` query set

**Status:** ✅ Done.

Add `SelectExistingById` (`SELECT Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM People
WHERE Id = @id AND IsDeleted = 0;`), `UpdateFieldsById` (updates `Name`, `DateOfBirth`, `DateOfDeath`,
`DateModified`), `SelectCompletenessById` (`SELECT CompletenessStatus, NoValueKnown FROM People WHERE
Id = @id;`), `UpdateCompletenessById` (`UPDATE People SET CompletenessStatus = @completenessStatus,
DateModified = @dateModified WHERE Id = @id;`) — same shapes as `Sql.Sources`'s equivalents
(`Sql.cs:244-257`), table name swapped.

### 5. Add `PlanPeopleAsync` and wire it into `PlanAsync`

**Status:** ✅ Done.

New method in `ImportActionPlanner.cs`, mirroring `PlanSourcesAsync`'s full shape
(`ImportActionPlanner.cs:292-396`) field-for-field, with `name`/`dateOfBirth`/`dateOfDeath` in place
of `title`/`type`/`date`: id-match lookup via `Sql.People.SelectExistingById` → existing/incoming
field maps → raw diff via `FieldMergeResolver.ValuesEqual` (unchanged → silent reuse, `continue`) →
policy-resolved value (`isMerge`/`mergeResult`/`resolved`, computed **before** the blocking check,
per #168's lesson) → second resolved-vs-existing diff → `CompletenessGuard.ShouldBlock` → stage
`Blocked` or `Modify`/`Pending`/`Decided` per policy. No id match falls back to
`Sql.People.SelectIdByName` (existing natural-key query) — a match there stages nothing (natural-key
row, not yet correctable); no match at all stages an `Add` using the file's own id. Populate
`personIndex[s.Name] = s.Id` in both the id-matched and natural-key-fallback branches (see
Implementation notes above — the #162-test-7a-shaped threading gap). Call `PlanPeopleAsync` from
`PlanAsync` before the quote loop (alongside the existing `PlanSourcesAsync` call at line 58), passing
a new `IReadOnlyList<PersonEntry>? people = null` parameter added to `PlanAsync`'s own signature and
`personIndex` (already declared at line 48, currently unused until `ResolvePersonAsync` runs per-quote
inside the loop).

### 6. Split `ApplyResolvedActionAsync`'s Person case on `ActionType`

**Status:** ✅ Done.

`Add` keeps calling `EnsurePersonExistsAsync` as today (`SqliteImportActionService.cs:531-533`).
`Modify` deserializes `action.MergedFields` as `PersonActionPayload`, calls the new
`Sql.People.UpdateFieldsById`, logs the change via `QuoteSeedWriter.LogChangeAsync`, then calls
`ApplyCompletenessAsync(..., Sql.People.SelectCompletenessById, Sql.People.UpdateCompletenessById,
action.EntityId, action.MarkCompletenessAs.Parsed, now)` — mirrors Source's case
(`SqliteImportActionService.cs:492-517`) exactly. `PersonActionPayload` gains `DateOfBirth`/
`DateOfDeath` (plain `string?`, see Implementation notes above) alongside the existing `Name`.

### 7. Add Person's `DecideAsync` branch

**Status:** ✅ Done.

Add an `EntityType == Person && ActionType == Modify` branch before the existing `!= Quote` rejection
(`SqliteImportActionService.cs:110`), alongside the Source branch already there. New
`ToPersonDecisionMap(ConflictDecisionRequest request)` helper mapping `name`/`dateOfBirth`/
`dateOfDeath` from `request.PersonName`/`request.PersonDateOfBirth`/`request.PersonDateOfDeath`,
mirroring `ToSourceDecisionMap` (`SqliteImportActionService.cs:929-944`).

### 8. Add Person to `ComputeAmbiguousFields`

**Status:** ✅ Done.

Add a `case ImportActionEntityTypes.Person:` alongside the existing `Source` case
(`SqliteImportActionService.cs:860-865`), deserializing `PersonActionPayload` on both sides and
building field maps via the existing `ToFieldMap(PersonActionPayload)` helper — currently only
`["name"]` (`SqliteImportActionService.cs:830-831`); extend it to also include `["dateOfBirth"]`/
`["dateOfDeath"]`, since `BuildFields` (line 816) already routes through this same overload for
`GET /import/actions` field display.

### 9. Split `ReverseAppliedActionsAsync`'s Person case on `ActionType`

**Status:** ✅ Done.

`Modify`: deserialize `action.ExistingValue` as `PersonActionPayload`, call
`Sql.People.UpdateFieldsById` to restore `Name`/`DateOfBirth`/`DateOfDeath`, log the change via
`QuoteSeedWriter.LogChangeAsync`, `break` before the reference-count check. `Add`: keep today's
`HasActiveReferencesAsync` + soft-delete behaviour unchanged (`SqliteImportActionService.cs:357-362`)
— mirrors Source's split (`SqliteImportActionService.cs:331-356`).

### 10. Fix `ClearStaleAddTargetsAsync`'s Person branch — required, not optional

**Status:** ✅ Done.

Replace `foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Person))
await _personRepository.HardDeleteAsync(Guid.Parse(action.EntityId));`
(`SqliteImportActionService.cs:211-212`) with the raw-SQL, case-preserving pattern already used for
Source immediately above it (lines 204-209): `foreach (var action in adds.Where(a => a.EntityType ==
ImportActionEntityTypes.Person)) await quoteConn.ExecuteAsync(RepositorySql.HardDelete("People"), new
{ id = action.EntityId });`. Update the surrounding comment block (currently describing only
Character/Person as safely uppercase, lines 197-200) to note Person is no longer in that safe category
once `people[]` supplies explicit, not-necessarily-uppercase ids — Character remains unaffected
(no explicit-id model exists for Character yet).

### 11. Add `ConflictDecisionRequest` properties

**Status:** ✅ Done.

Add `PersonName`, `PersonDateOfBirth`, `PersonDateOfDeath` (all nullable `FieldDecision?`), placed
after the existing `SourceDate` property (`ConflictDecisionRequest.cs:44`), each with a doc comment
following the existing `/// <summary>Decision for a Source action's ... (#162).</summary>` pattern but
referencing `#173`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A file's `people` section parses `id`/`name`/`dateOfBirth`/`dateOfDeath` correctly | Unit test | `Quotinator.Core.Tests.SourceQuoteFileReader_PeopleSection_ParsesCorrectly` |
| 2 | ✅ | An id-matched Person with a `name` diff stages a `Modify` action | Unit test | `Quotinator.Engine.Tests.PlanPeopleAsync_IdMatchFound_NameDiffers_StagesModifyAction` |
| 3 | ✅ | An id-matched Person with nothing changed stages nothing (silent reuse) | Unit test | `Quotinator.Engine.Tests.PlanPeopleAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 4 | ✅ | No id-match falls back to natural-key lookup, stages nothing for an already-existing row | Unit test | `Quotinator.Engine.Tests.PlanPeopleAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged` |
| 5 | ✅ | A `Complete`-status id-matched Person with a policy-resolved field change stages `Blocked`, not `Modify` | Unit test | `Quotinator.Engine.Tests.PlanPeopleAsync_CompleteStatus_StagesBlockedNotModify` |
| 6 | ✅ | A `Complete`-status Person under `Skip` policy never blocks (resolved value is always the existing value) | Unit test | `Quotinator.Engine.Tests.PlanPeopleAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 7 | ✅ | Applying a Person `Modify` writes `dateOfBirth`/`dateOfDeath` — the first path that ever populates them | Unit test | `Quotinator.Engine.Tests.ApplyBatchAsync_PersonModify_WritesDateOfBirthAndDateOfDeath` |
| 8 | ✅ | Decide endpoint accepts Person `name`/`dateOfBirth`/`dateOfDeath` field decisions | Unit test | `Quotinator.Engine.Tests.DecideAsync_PersonModify_ResolvesFieldDecisions` |
| 9 | ✅ | Reversing an applied Person `Modify` restores `ExistingValue`'s fields, not a soft-delete | Unit test | `Quotinator.Engine.Tests.ReverseBatchAsync_PersonModify_RestoresExistingValue` |
| 10 | ✅ | `ClearStaleAddTargetsAsync` correctly hard-deletes a stale Person Add whose explicit id is lowercase | Unit test | `Quotinator.Engine.Tests.ClearStaleAddTargetsAsync_PersonExplicitLowercaseId_HardDeletesCorrectly` — genuinely exercises the fix (see Notes: required a second fix beyond this issue's original scope) |
| 11 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,208/1,208 passing, 0 warnings, 0 errors |
| 12 | ✅ | Build clean | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s) |
| 13 | ✅ | Live: a `people[]` correction to a Person's `name`/`dateOfBirth`/`dateOfDeath` stages/decides/applies a `Modify`, and a `Complete` Person's field cannot be silently overwritten | Live (T2) | Docker smoke test: imported a `people[]` entry, decided a Pending Modify with `markCompletenessAs: Complete`, re-imported a changed field under `review` — confirmed the resulting action was `Blocked`. Separately, single-shot Add/reverse/re-Add cycle on a lowercase-id Person: first attempt (pre-reversal-fix image) staged the re-Add as `Modify` against a still-present row — proved the reversal's soft-delete was silently no-op'ing (case mismatch); after fixing the reversal path too (see Notes), the re-Add genuinely staged as `Add` and the row's `IsDeleted` flag was confirmed `1` before the re-Add, `0` after — a real red/green distinction, not an assumption |
| 14 | ❌ | App still opens and builds in Visual Studio | Live (T1) | Developer's own Visual Studio pass — confirms clean startup; this issue adds no new migration (both `DateOfBirth`/`DateOfDeath` columns already exist), only new query text, a new schema section, and new C# branches |

---

## Notes

T1 and T2 are both required per this project's blanket rule (see #168's own "Notes" section — no
Razor/migration-surface exemption applies to a genuine C# logic change; this issue has no migration
surface at all, since both date columns predate it).

This issue's "global entity, Name-keyed" Modify shape is deliberately meant to be the direct template
for issue #175 (Character), once #174 (Character's own per-Source-to-global identity redesign) lands
— Character currently has a `SourceId` scoping question Person does not, which is exactly why Person
(the simpler of the two remaining entities) goes first. Precision in this issue's design — especially
the id-first/natural-key-fallback shape and the `personIndex` same-batch-quote-resolution threading —
matters as a worked example for that follow-on issue, not only for its own sake.

**Two gaps found beyond the original 11 steps, both during implementation/T2, not anticipated by the
plan:**

1. **`PlanAsync`'s two production call sites were never updated to pass `parsed.People` through** —
   `SqliteQuoteImportService.cs` (the `POST /import` path) and `QuotinatorDatabaseInitializer.cs` (the
   startup seeder) both still called `ImportActionPlanner.PlanAsync(...)` without the new `people`
   parameter. Caught immediately by T2: importing a `people[]` entry staged zero Person actions at
   all, even though the same file worked correctly in unit tests (which call `PlanAsync` directly).
   Fixed by adding `parsed.People` as the final argument at both call sites.
2. **`ReverseAppliedActionsAsync`'s Person `Add` case had the identical case-sensitivity bug this
   issue's own step 10 fixed for `ClearStaleAddTargetsAsync`** — `_personRepository.SoftDeleteAsync
   (Guid.Parse(action.EntityId), uow)` force-uppercases via `GuidHandler` before comparing, silently
   matching zero rows against a lowercase-stored explicit id. The plan doc's step 9 said "keep today's
   soft-delete-if-unreferenced behaviour unchanged" — reasonable for a *rewrite* of an Add-only
   planner (matching #171/#172/#176's precedent), but wrong here because #173 is the issue that first
   makes a lowercase Person id possible at all, the same reason step 10 exists. Caught live via T2: a
   canary run against the pre-fix image staged a lowercase-id re-Add as a `Modify` (proving the prior
   reversal never actually happened, since the row was still visibly present), not the expected `Add`.
   Fixed with the identical raw-SQL pattern as step 10 (`RepositorySql.SoftDelete("People")`), and this
   also retroactively made `ClearStaleAddTargetsAsync_PersonExplicitLowercaseId_HardDeletesCorrectly`
   (which had been passing since the initial implementation) a genuine test of its own name — before
   this second fix, it was accidentally green because the row was never truly stale to begin with.
