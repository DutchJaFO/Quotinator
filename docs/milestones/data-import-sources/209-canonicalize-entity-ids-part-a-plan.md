# #209 — Canonicalize explicit ids at capture: Source, Person, StageDirection, SoundCue, Conversation

**Status:** Planning
**GitHub issue:** #209
**Tiers required:** T1, T2
**Depends on:** none (ADR 012 already committed; built against current `Quotinator.Core`/`Quotinator.Data`; parent tracking issue #207)

---

## Spec requirements

1. Add `Quotinator.Data.Helpers.EntityIdCanonicalizer` — a single reusable helper that turns a raw
   externally-supplied id string into this project's canonical uppercase form, with both a throwing and
   a non-throwing variant (a capture site needs to fall back gracefully on malformed input, not crash an
   entire import over one bad id).
2. Canonicalize `SourceEntry.Id`/`PersonEntry.Id` through it at the single earliest point each is
   captured in `ImportActionPlanner` — both the Add path and the correction-match path (which today
   uses the file's own casing for `sourceIndex`/`personIndex`, not the matched row's actual stored id).
3. Canonicalize `SourceStageDirection.Id`/`SourceSoundCue.Id`/`SourceConversation.Id` the same way, at
   the single earliest point each is captured in `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/
   `PlanConversationsAsync`.
4. No code change needed for Character — verify and document the finding.
5. Audit every `Ensure*ExistsAsync` helper and every `Sql.*.Insert`/`Update` binding an entity id as a
   raw `string`, to confirm nothing downstream of (2)/(3) can still introduce non-canonical casing.
6. Verify against the Quote→Source join specifically — not only the masterdata `GetById` endpoint that
   originally surfaced this.
7. Build the cross-entity regression guard proving the invariant holds for every entity this issue fixes
   (Source, Person, StageDirection, SoundCue, Conversation).

---

## Background — why this issue exists

See ADR 012 (`docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md`) for the full
incident writeup. In short: `Sources.Id`, `Quotes.SourceId`, and `CharacterSources.SourceId` are all
written from the same in-memory `ImportActionPlanner.sourceIndex` value, bound as plain `string` Dapper
parameters (never `Guid`-typed, so `GuidHandler`'s uppercase normalization never runs). A file-authored
lowercase explicit id therefore reaches storage exactly as typed. This is accidentally self-consistent
(the Quote→Source join still matches, since both sides carry the same wrong casing) until a `Guid`-typed
lookup — which `GuidHandler` force-uppercases — silently fails to find the non-canonical row. That's how
`GET /api/v1/masterdata/sources/{id}` was found 404ing for a Source that resolved correctly via
`GET /api/v1/quotes/{id}`.

**Verified before starting** (per this project's standing rule):

- **Confirmed as claimed**: `ImportActionPlanner.PlanSourcesAsync`'s correction-match branch sets
  `sourceIndex[$"{s.Title}|{typeStr}"] = matchedId;` where `matchedId = s.Id!` (`ImportActionPlanner.cs`
  line 354/366) — the file's own casing, not a canonicalized form. The Add-path fallback,
  `var addId = s.Id ?? EntityIdentity.SourceId(s.Title, typeStr);` (line 517), has the identical
  exposure. `PlanPeopleAsync` mirrors this exactly at lines 570 (`personIndex[p.Name] = p.Id;`, matched
  branch) and 638 (same, Add branch) — `PersonEntry.Id` is `required` (never optional), so every Person
  Add or correction goes through this path.
- **Confirmed as claimed**: `Sql.Sources.SelectExistingById`/`Sql.People.SelectExistingById` (and their
  sibling `UpdateFieldsById`/`SelectCompletenessById`/`UpdateCompletenessById`/`CountActiveReferences`
  queries) are already `UPPER(Id) = UPPER(@id)`-wrapped (#180's own fix) — so the *existence lookup*
  already correctly finds a row regardless of the file's casing. The bug is specifically that `matchedId`
  then uses the file's raw casing instead of asking the row what its own actual id is, poisoning
  `sourceIndex`/`personIndex` for any same-batch quote that resolves against it.
- **Confirmed as claimed**: `Sql.Quotes.Insert` (`SqliteImportActionService.cs` line 804-814) binds
  `SourceId = payload.SourceId` as a plain string, sourced from `QuoteActionPayload.SourceId`, which
  traces back to `ResolveSourceAsync`'s return value — itself either `sourceIndex`'s value (if resolved
  same-batch) or a direct DB-read existing id (already canonical, since it comes from a `SELECT`, not a
  file). This confirms the join-consistency mechanism described in ADR 012 is real, not theoretical.
- **Character does NOT share this gap, verified not assumed**: `EntityIdentity.CharacterId(sourceId,
  name)` calls the shared `StableId` helper (`Quotinator.Core.Import.EntityIdentity.cs`), which in turn
  calls `QuoteIdentity.Normalise` on every input piece before hashing — and `Normalise` is
  `s.Trim().ToLowerInvariant()` with whitespace collapsed (`QuoteIdentity.cs` line 17-18). Because the
  hash input is *always* lowercased before `SHA256.HashData` runs, a Character's derived stable id is
  **invariant to the casing of the `sourceId` string passed in**. Character also has no file-authored
  explicit id path at all (no `characters[]` section exists in the schema) — the only id computation is
  `EntityIdentity.CharacterId`, whose final line is `new Guid(hash[..16]).ToString("D").ToUpperInvariant()`
  — always canonical-uppercase by construction. No fix needed for Character; this finding is the answer
  to Spec requirement 4, not a placeholder for later work.
- **Scope widened during the parent issue's split into sub-issues, verified by direct code read**:
  `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync` (`ImportActionPlanner.cs`)
  each use their entry's raw `.Id` directly for every `EntityId`, Add, and Modify write — `sd.Id`/
  `sc.Id`/`c.Id` — with no canonicalization anywhere in the method. Unlike Source/Person, these three
  match purely by id (no natural-key index), so the correction-branch index-poisoning risk described
  above doesn't apply to them, but their own `EntityId`/write-parameter casing is equally
  uncanonicalized. Folded into this sub-issue's scope rather than filed separately, since it is the
  identical bug class discovered mid-investigation.
- **Correction to ADR 012's own sketch, found while designing the guard test**: the ADR describes the
  cross-entity guard as `[DynamicData]`-driven "asserting the full, real invariant end to end" through
  the import pipeline for all five entities. Attempting this concretely, the five entity DTOs
  (`SourceEntry`/`PersonEntry`/`SourceStageDirection`/`SourceSoundCue`/`SourceConversation`) have no
  common shape a single generic pipeline-level test method can drive without heavy indirection (delegates
  captured across `[DynamicData]`'s static-method boundary, which runs before per-test instance state like
  `_dbPath` exists). Redesigned as a **storage-layer** invariant guard instead — canonicalize a lowercase
  id, insert it directly into each of the five tables via minimal raw SQL, then assert a `Guid`-typed
  `SELECT` finds it — genuinely `[DynamicData]`-driven, one method, still proves the real invariant
  (`GuidHandler`-typed lookups find canonically-written rows), just below the full pipeline rather than
  through it. The full pipeline is still exercised, but by *separate*, entity-specific tests for every
  entity whose planner logic actually changes (Source, Person, StageDirection, SoundCue, Conversation) —
  see Step 7.

---

## Approach

### `EntityIdCanonicalizer` (`Quotinator.Data.Helpers`, alongside `GuidHandler`/`SafeValue<T>`)

The uppercase forms this sub-issue needs (the sibling sub-issue, #210, adds the lowercase forms to the
same class — whichever lands first creates the file with its own half):

```csharp
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException">rawId is not a valid Guid.</exception>
    public static string CanonicalizeUppercase(string rawId) => Guid.Parse(rawId).ToString("D").ToUpperInvariant();

    public static bool TryCanonicalizeUppercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed)) { canonical = parsed.ToString("D").ToUpperInvariant(); return true; }
        canonical = null;
        return false;
    }
}
```

The non-throwing `Try*` form is needed because `ImportActionPlanner` must not let one malformed id throw
and abort an entire batch's planning.

### Capture-point fix: canonicalize once per entry, use everywhere

**`PlanSourcesAsync`** — `SourceEntry.Id` is `string?` (optional; enrichment-shaped entries omit it).
Canonicalize once at the top of the loop body:

```csharp
var canonicalId = s.Id is { } rawId && EntityIdCanonicalizer.TryCanonicalizeUppercase(rawId, out var canonical)
    ? canonical
    : s.Id; // malformed or absent: pass through unchanged — not this issue's job to add new validation
```

Every later reference to `s.Id`/`explicitId` for lookup, `matchedId`, and `addId` uses `canonicalId`
instead. A well-formed lowercase id is now canonical everywhere it's used in this iteration — the lookup
(already tolerant, unaffected), `sourceIndex`, and the staged `EntityId`. A malformed id behaves exactly
as it does today (passes through unchanged) — deliberately unchanged, since general id-format validation
is out of this issue's scope.

**`PlanPeopleAsync`** — `PersonEntry.Id` is `required string`, so the same pattern applies without the
`s.Id is { }` null-check:

```csharp
var canonicalId = EntityIdCanonicalizer.TryCanonicalizeUppercase(p.Id, out var canonical) ? canonical! : p.Id;
```

used everywhere `p.Id` currently appears (`SelectExistingById`'s parameter, `personIndex[p.Name] =`,
every staged `EntityId =`).

**`PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync`** — `SourceStageDirection.Id`/
`SourceSoundCue.Id`/`SourceConversation.Id` are all `required string` (matched purely by id, no
natural-key fallback). Same pattern as `PlanPeopleAsync`, applied at the top of each method's loop body:

```csharp
var canonicalId = EntityIdCanonicalizer.TryCanonicalizeUppercase(sd.Id, out var canonical) ? canonical! : sd.Id;
```

used everywhere `sd.Id`/`sc.Id`/`c.Id` currently appears (`SelectExistingById`'s parameter, every staged
`EntityId =`). No natural-key index exists for these three, so there is no correction-branch poisoning
risk to guard against — only the write-side casing itself needs fixing.

### `SqliteImportActionService.cs` — no change needed

Because canonicalization happens once, upstream, at the point `ImportActionPlanner` first captures the
id, `action.EntityId`/`payload.SourceId`/every `Ensure*ExistsAsync` call site downstream already receives
the canonical value with zero code changes on their part — this is the entire point of ADR 012's
"single earliest point of capture" principle. Step 5 (audit) is therefore primarily verification, not
new code: confirm no *other* capture point exists that bypasses `ImportActionPlanner` (grepped — none
found; masterdata endpoints are read-only, `DecideAsync`'s conflict-resolution path only resolves
*ambiguous fields* on an already-staged action, never introduces a new id).

---

## Steps

### 1. `EntityIdCanonicalizer` + its tests

**Status:** Not started.

New file `src/Quotinator.Data/Helpers/EntityIdCanonicalizer.cs`. New test file
`tests/Quotinator.Data.Tests/Helpers/EntityIdCanonicalizerTests.cs`:
`CanonicalizeUppercase_LowercaseGuid_ReturnsUppercaseD`, `CanonicalizeUppercase_AlreadyCanonical_IsIdempotent`,
`CanonicalizeUppercase_Malformed_Throws`, `TryCanonicalizeUppercase_ValidGuid_ReturnsTrueWithCanonicalForm`,
`TryCanonicalizeUppercase_Malformed_ReturnsFalse`.

### 2. `PlanSourcesAsync` capture-point fix

**Status:** Not started. Per the Approach section above — one `canonicalId` computed at the top of the
loop, used at every `s.Id`/`explicitId`/`matchedId`/`addId` reference.

### 3. `PlanPeopleAsync` capture-point fix

**Status:** Not started. Same pattern, adjusted for `PersonEntry.Id` being required rather than optional.

### 4. `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync` capture-point fix

**Status:** Not started. Same pattern as Step 3, applied to all three methods independently — each has
its own loop and its own `.Id` reference, no shared index to coordinate across them.

### 5. Character — no fix, finding documented

**Status:** Done during planning (see Background's verified finding) — `EntityIdentity.CharacterId`'s
hash input is always lowercased before hashing, so its output is casing-invariant regardless of
`sourceId`'s casing. No code change.

### 6. Audit `Ensure*ExistsAsync`/`Sql.*.Insert`/`Update` sites

**Status:** Not started. Confirm (by inspection, not by changing code) that every remaining raw-`string`
id parameter downstream of Steps 2-4 now receives an already-canonical value — `EnsureSourceExistsAsync`,
`EnsurePersonExistsAsync`, `EnsureCharacterExistsAsync`, and the direct insert/update bindings inside
`PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync`. Record the grep evidence in this
doc's Notes once done.

### 7. Tests

**Status:** Not started.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Helpers.EntityIdCanonicalizerTests` | (5 cases, Step 1) |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_LowercaseExplicitId_AddPath_ResolvedIdIsCanonicalUppercase` |
| " | `PlanSourcesAsync_LowercaseExplicitId_CorrectionMatch_IndexedIdIsCanonicalUppercase` |
| " | `PlanSourcesAsync_QuoteReferencesLowercaseExplicitSource_ResolvedSourceIdIsCanonical` (the join-safety case, at planner level) |
| " | `PlanPeopleAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| " | `PlanStageDirectionsAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| " | `PlanSoundCuesAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| " | `PlanConversationsAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `[DynamicData]`-driven, one method covering Sources/People/StageDirections/SoundCues/Conversations: canonicalize a lowercase id, insert directly, assert a `Guid`-typed `SELECT` finds it (the storage-layer guard — see Background's correction to ADR 012's original sketch) |
| " | `ApplyBatchAsync_LowercaseExplicitSourceId_QuoteJoinStillResolves` (full pipeline: import Source + same-batch Quote with lowercase explicit Source id, apply, read the quote back via the real join query, confirm source title/date resolve) |
| " | `ApplyBatchAsync_LowercaseExplicitSourceId_MasterdataRepositoryLookupResolves` (apply, then `SqliteRepository<Source>.GetByIdAsync(Guid)` — the exact call the masterdata endpoint makes — finds the row) |

### 8. Verify

**Status:** Not started. `dotnet build --configuration Release` (0 warnings/errors), `dotnet test
--configuration Release --verbosity normal` (full suite green), T1, T2 (Docker smoke test — reproduce
the original live finding: import a Source with a lowercase explicit id, confirm
`GET /api/v1/masterdata/sources/{id}` now returns 200, and confirm `GET /api/v1/quotes/{id}` for a
same-batch quote referencing it still resolves the source title/date correctly).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `EntityIdCanonicalizer` canonicalizes, is idempotent, and rejects malformed input via both a throwing and non-throwing form | Unit test | `EntityIdCanonicalizerTests` (5 cases) |
| 2 | ⬜ | A lowercase explicit Source id (Add or correction-match) resolves to canonical uppercase everywhere it's used in the same batch | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_LowercaseExplicitId_*` (2 cases) |
| 3 | ⬜ | A same-batch quote referencing a lowercase-id'd Source resolves to the canonical id | Unit test | `PlanSourcesAsync_QuoteReferencesLowercaseExplicitSource_ResolvedSourceIdIsCanonical` |
| 4 | ⬜ | A lowercase explicit Person id resolves to canonical uppercase | Unit test | `PlanPeopleAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| 5 | ⬜ | A lowercase explicit StageDirection/SoundCue/Conversation id resolves to canonical uppercase | Unit test | `PlanStageDirectionsAsync_/PlanSoundCuesAsync_/PlanConversationsAsync_LowercaseExplicitId_ResolvedIdIsCanonicalUppercase` |
| 6 | ⬜ | Character ids are unaffected by Source-id casing (documented finding, not a fix) | Doc review | This plan doc's Background section |
| 7 | ⬜ | Every explicit-id-capable table's rows are findable via a `Guid`-typed lookup once canonicalized | Unit test | `SqliteImportActionServiceTests`' `[DynamicData]` storage guard (5 cases) |
| 8 | ⬜ | The Quote→Source join survives a lowercase explicit Source id through a full plan→apply cycle | Unit test | `ApplyBatchAsync_LowercaseExplicitSourceId_QuoteJoinStillResolves` |
| 9 | ⬜ | The masterdata Sources repository lookup (the exact query that originally 404'd) resolves | Unit test | `ApplyBatchAsync_LowercaseExplicitSourceId_MasterdataRepositoryLookupResolves` |
| 10 | ⬜ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` |
| 11 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 12 | ⬜ | T2 — the original live symptom is fixed end to end | Live (T2) | Docker: import a lowercase-explicit-id Source, `GET /api/v1/masterdata/sources/{id}` returns 200; a same-batch quote's `GET /api/v1/quotes/{id}` still resolves the source correctly |

---

## Notes

None yet — this is a planning-only pass; implementation has not started.
