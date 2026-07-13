# #172 — SoundCue: Modify/decidability

**Status:** Waiting for release
**GitHub issue:** #172
**Tiers required:** T1, T2
**Depends on:** #162, #165, #168 (shipped patterns this builds on); benefits from #171 landing first (shared sub-problem)

---

## Spec requirements (from the GitHub issue)

SoundCue already has an explicit file-carried id (#67/#68) but is Add-only everywhere: `PlanSoundCuesAsync`
does a bare id-existence check with no field diff, apply is insert-if-not-exists, `DecideAsync` rejects
deciding it, and `ReverseAppliedActionsAsync` always soft-deletes unconditionally. This issue extends the
same Modify/decidability capability #171 gives StageDirection to SoundCue — sequenced right after it so the
one genuinely new sub-problem both entities share (updating a translations-adjacent single-row entity in
place) is solved once and directly copied here, not re-derived. Concretely:

1. `Sql.SoundCues` (`src/Quotinator.Engine/Queries/Sql.cs`) gains `SelectExistingById` (returns `Text`,
   `SoundFileUrl`, `ImageUrl`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById`.
2. `PlanSoundCuesAsync` (`src/Quotinator.Engine/Database/ImportActionPlanner.cs`) rewritten to mirror
   `PlanSourcesAsync`'s shape (and #171's `PlanStageDirectionsAsync`) exactly: id-match lookup → compute
   existing/incoming field maps (`text`, `soundFileUrl`, `imageUrl`) → unchanged-check (silent reuse) →
   policy-based resolution → `CompletenessGuard.ShouldBlock` evaluated against the policy-**resolved**
   value → stage `Blocked` or `Modify`. Existing "no id match → Add" path is unchanged.
3. `ApplyResolvedActionAsync`'s SoundCue case (`src/Quotinator.Engine/Services/SqliteImportActionService.cs`)
   splits on `ActionType`: `Add` unchanged; `Modify` calls the new `Sql.SoundCues.UpdateFieldsById` against
   `MergedFields`, applies completeness via `ApplyCompletenessAsync`.
4. `DecideAsync` gains an `EntityType == SoundCue && ActionType == Modify` branch, mirroring Source's/
   #171's branch shape.
5. `ComputeAmbiguousFields` gains a `SoundCue` case.
6. `ReverseAppliedActionsAsync`'s SoundCue case splits on `ActionType`: `Add` keeps today's
   soft-delete-if-unreferenced; `Modify` restores `Text`/`SoundFileUrl`/`ImageUrl` via `UpdateFieldsById`
   from `ExistingValue`.
7. `ConflictDecisionRequest` gains `SoundCueText`, `SoundCueSoundFileUrl`, `SoundCueImageUrl` (nullable
   `FieldDecision?`).
8. Translations (`SoundCueTranslations`) stay explicitly out of scope, same reasoning as #171 — only
   `text`/`soundFileUrl`/`imageUrl` on the parent row become correctable.

---

## Steps

### 1. Add `Sql.SoundCues` query set

**Status:** ✅ Done. Implemented together with #171 (shared sub-problem, per the developer's direction
to do both at once). Confirmed at implementation time: `SoundCues` already had `CompletenessStatus`/
`NoValueKnown` columns with correct defaults in both the baseline and incremental migration (checked
both copies for drift — identical) — no pre-existing migration gap, nothing extra to fix.

`src/Quotinator.Engine/Queries/Sql.cs`'s existing `SoundCues` class (currently `DeleteAll`, `SelectIdById`,
`CountActiveReferences`, `InsertIfNotExists`, `SelectByIdWithTranslation` — no update query at all) gains
four new members, mirroring `Sources`' analogous four exactly:

- `SelectExistingById` — `SELECT Text, SoundFileUrl, ImageUrl, CompletenessStatus FROM SoundCues WHERE Id = @id AND IsDeleted = 0;`
  (mirrors `Sources.SelectExistingById`'s shape).
- `SelectCompletenessById` — `SELECT CompletenessStatus, NoValueKnown FROM SoundCues WHERE Id = @id;`
  (mirrors `Sources.SelectCompletenessById` verbatim, table name swapped).
- `UpdateCompletenessById` — `UPDATE SoundCues SET CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;`
  (mirrors `Sources.UpdateCompletenessById` verbatim).
- `UpdateFieldsById` — `UPDATE SoundCues SET Text = @text, SoundFileUrl = @soundFileUrl, ImageUrl = @imageUrl, DateModified = @dateModified WHERE Id = @id;`
  (mirrors `Sources.UpdateFieldsById`'s shape — never touches `CompletenessStatus`/`NoValueKnown`, same
  separation of concerns `Sources.UpdateFieldsById`'s doc comment calls out).

Note: `SoundCues` currently has no `CompletenessStatus`/`NoValueKnown` columns read anywhere — confirm at
implementation time that the table actually has these columns (it should, per #165's blanket
`CompletenessGuard` rollout across every entity table; if a migration gap is found, that is a separate,
pre-existing bug to flag, not something this issue's scope should silently absorb).

### 2. Rewrite `PlanSoundCuesAsync` to add id-match Modify/Blocked detection

**Status:** ✅ Done — mirrors `PlanSourcesAsync`'s id-match branch field for field, including local
`ToFieldMap(SoundCueActionPayload)` overload and the #168-correct resolved-before-block ordering.

`ImportActionPlanner.cs`'s `PlanSoundCuesAsync` (currently lines ~425-445: a bare
`Sql.SoundCues.SelectIdById` existence check, `continue` on any match, stage `Add` otherwise) is rewritten
to mirror `PlanSourcesAsync`'s current shape (`ImportActionPlanner.cs:292-402`) field-for-field:

1. Query `Sql.SoundCues.SelectExistingById` instead of `SelectIdById`.
2. On a match: build `existingPayload`/`incomingPayload` as `SoundCueActionPayload(Text, SoundFileUrl,
   ImageUrl, Translations)` — reuse the existing record, `Translations` empty/unused on the existing side
   since translations aren't compared (see step 8's scope note) — and `existingFields`/`incomingFields` via
   a local `ToFieldMap` over `text`/`soundFileUrl`/`imageUrl` only.
3. Compute `changedFields` using **`FieldMergeResolver.ValuesEqual`, not `!=`/`Equals`, from the start** —
   this is the one lesson #168 already paid for (a naive equality check over a list-typed field silently
   over-blocked Quote's `genres`; SoundCue's fields are all plain nullable strings today, so the bug class
   doesn't currently apply, but using `ValuesEqual` uniformly avoids re-deriving this the next time a
   list-typed field is added to any entity, and keeps the four id-match planners — Quote, Source,
   StageDirection, SoundCue — textually consistent). `changedFields.Count == 0` → `continue` (silent
   reuse), matching `PlanSourcesAsync`'s line 316.
4. Compute `isMerge`/`mergeResult`/`resolved` from the batch's `DuplicateResolutionPolicy`, exactly as
   `PlanSourcesAsync` does — `resolved` must be computed **before** the blocking decision (this is the
   #168 fix already baked into `PlanSourcesAsync`; a fresh `PlanSoundCuesAsync` written from scratch must
   not reintroduce the pre-168 raw-incoming-vs-existing gate order).
5. Compute a second, resolved-vs-existing `effectiveChangedFields` diff (via `ValuesEqual` again) purely
   for the `CompletenessGuard.ShouldBlock` argument, matching `PlanSourcesAsync:332-337`.
6. `ShouldBlock(currentStatus, effectiveChangedFields)` → stage `Blocked` (no `MergedFields`,
   `ExistingValue`/`IncomingValue` set) if true; otherwise stage `Modify` with `MergedFields = resolved`
   when not `Pending`, `AppliedPolicy` set, same `isPending = policy == Review` / `Decided` vs `Pending`
   status split as `PlanSourcesAsync:353-354`.
7. `PlanSoundCuesAsync`'s signature needs `DuplicateResolutionPolicy policy` added (currently doesn't take
   one, since Add-only staging never needed it) — update its call site at `PlanAsync`'s existing
   `await PlanSoundCuesAsync(connection, soundCues ?? [], batchIdStr, actions, now, transaction);` (line
   166) to pass `policy`, matching how `PlanSourcesAsync` is already called on line 58.
8. The "no id match → Add" path is unchanged (existing `SoundCueActionPayload` construction, `Decided`
   status) — `PlanSourcesAsync`'s natural-key fallback (§ no-id-match branch) has no SoundCue analogue,
   since SoundCue has no natural key to fall back to; a no-match SoundCue always stages a plain `Add`,
   exactly as today.

### 3. Split `ApplyResolvedActionAsync`'s SoundCue case on `ActionType`

**Status:** ✅ Done.

The current case (`SqliteImportActionService.cs:626-631`) unconditionally calls
`EnsureSoundCueExistsAsync` regardless of `ActionType`. Mirrors Source's Apply-side split
(`SqliteImportActionService.cs:492-517`) exactly:

- `Add` (`action.ActionType.Parsed == ImportActionKind.Add`): unchanged — deserialize `IncomingValue` as
  `SoundCueActionPayload`, call `EnsureSoundCueExistsAsync` as today.
- `Modify`: deserialize `action.MergedFields` (never `null` here — `ApplyResolvedActionAsync` only runs
  against `Decided` actions, and step 2 guarantees `MergedFields` is set on every `Decided` Modify) as
  `SoundCueActionPayload`, call the new `Sql.SoundCues.UpdateFieldsById` with `text`/`soundFileUrl`/
  `imageUrl`/`dateModified`/`id`, then `QuoteSeedWriter.LogChangeAsync(changeLog, "soundCue",
  action.EntityId, ChangeAction.Modified, oldValue: action.ExistingValue, newValue: payload, ...)`.
- Both branches end by calling `ApplyCompletenessAsync(sqliteConnection, sqliteTransaction,
  Sql.SoundCues.SelectCompletenessById, Sql.SoundCues.UpdateCompletenessById, action.EntityId,
  action.MarkCompletenessAs.Parsed, now)` — mirrors Source's `ApplyCompletenessAsync` call at line 516,
  which today runs for both Add and Modify uniformly.

### 4. `DecideAsync` gains a SoundCue Modify branch

**Status:** ✅ Done.

Add a branch before the existing `if (action.EntityType != ImportActionEntityTypes.Quote) throw ...`
check (`SqliteImportActionService.cs:110-111`), structured exactly like the existing Source branch
(lines 90-108):

```csharp
if (action.EntityType == ImportActionEntityTypes.SoundCue && action.ActionType.Parsed == ImportActionKind.Modify)
{
    var existingPayload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.ExistingValue!)!;
    var incomingPayload = JsonSerializer.Deserialize<SoundCueActionPayload>(action.IncomingValue!)!;

    var existingFields = ToFieldMap(existingPayload);
    var incomingFields = ToFieldMap(incomingPayload);
    var decisions       = ToSoundCueDecisionMap(request);

    var result = FieldMergeResolver.ResolveWithDecisions(existingFields, incomingFields, decisions);

    var resolvedPayload = new SoundCueActionPayload(
        (string)result.MergedFields["text"]!,
        (string?)result.MergedFields["soundFileUrl"],
        (string?)result.MergedFields["imageUrl"],
        existingPayload.Translations);

    await _coordinator.DecideAsync(actionId, JsonSerializer.Serialize(resolvedPayload), request.MarkCompletenessAs);
    return;
}
```

New private helper `ToSoundCueDecisionMap(ConflictDecisionRequest request)`, mirroring
`ToSourceDecisionMap` (lines 929-944) exactly: `Add("text", request.SoundCueText)`,
`Add("soundFileUrl", request.SoundCueSoundFileUrl)`, `Add("imageUrl", request.SoundCueImageUrl)`.

Note `SoundCueActionPayload`'s constructor requires a non-nullable `Translations` argument
(`IReadOnlyDictionary<string, SourceSoundCueTranslation>`) — pass `existingPayload.Translations` through
unchanged (translations are out of scope for correction per requirement 8, but the record shape still
needs a value; reusing the existing row's translations preserves them across the Modify, matching
"never touched" rather than "silently cleared").

### 5. `ComputeAmbiguousFields` gains a SoundCue case

**Status:** ✅ Done — confirmed live via T2: a Pending Modify with a genuinely ambiguous `text` field
correctly reported `"ambiguousFields":["text"]`.

Add a `case ImportActionEntityTypes.SoundCue:` arm to the `switch` at
`SqliteImportActionService.cs:850-868`, alongside the existing `Quote`/`Source` cases:

```csharp
case ImportActionEntityTypes.SoundCue:
{
    existing = ToFieldMap(JsonSerializer.Deserialize<SoundCueActionPayload>(action.ExistingValue!)!);
    incoming = ToFieldMap(JsonSerializer.Deserialize<SoundCueActionPayload>(action.IncomingValue!)!);
    break;
}
```

The existing `ToFieldMap(SoundCueActionPayload payload)` overload (line 836-837) already exists (built for
`BuildFields`'s `"SoundCue"` case) — reused as-is, no new overload needed.

### 6. Split `ReverseAppliedActionsAsync`'s SoundCue case on `ActionType`

**Status:** ✅ Done — confirmed live via T2 (single-shot apply/reverse cycle restored pre-correction
text).

The current case (`SqliteImportActionService.cs:379-384`) unconditionally does
`HasActiveReferencesAsync` + soft-delete regardless of `ActionType`. Mirrors the Source Reverse split
(lines 331-356) exactly:

- `Modify` (`action.ActionType.Parsed == ImportActionKind.Modify`): deserialize `action.ExistingValue` as
  `SoundCueActionPayload`, call `Sql.SoundCues.UpdateFieldsById` restoring `Text`/`SoundFileUrl`/
  `ImageUrl`, log `ChangeAction.Modified` with `oldValue: null, newValue: existingSoundCuePayload` (matches
  Source's own reversal logging convention — the "old" value from the reversal's own perspective is the
  restored state, not tracked further back). No active-reference check needed, same reasoning as Source's
  Modify reversal: a Modify reversal never deletes anything.
- `Add` (else branch): unchanged — today's `HasActiveReferencesAsync` + `RepositorySql.SoftDelete
  ("SoundCues")` + `ChangeAction.SoftDelete` logging, exactly as it stands today.

### 7. `ConflictDecisionRequest` gains three new properties

**Status:** ✅ Done — added alongside #171's StageDirection properties in the same commit.

`src/Quotinator.Engine/Models/ConflictDecisionRequest.cs` gains, immediately after the existing
`SourceDate` property (line 44), three new nullable `FieldDecision?` properties following the same
XML-doc convention (`/// <summary>Decision for a SoundCue action's ... (#172).</summary>`):

- `SoundCueText`
- `SoundCueSoundFileUrl`
- `SoundCueImageUrl`

### 8. Translations stay out of scope (documentation-only, no code change)

**Status:** ✅ Done — `ReverseBatchAsync_SoundCueModify_RestoresExistingValue` seeds a
`SoundCueTranslations` row before the Modify+reversal cycle and asserts it survives untouched.

No code change for this step — a deliberate scope boundary, same reasoning #171 uses for StageDirection.
`SoundCueTranslations` rows are never read, diffed, or written by any of steps 1-7; only `Text`/
`SoundFileUrl`/`ImageUrl` on the parent `SoundCues` row become correctable via Modify. Worth a one-line
note in the closing comment / plan doc "Notes" section so a future reader doesn't assume translation
correction was silently included.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An id-match with a changed `text`/`soundFileUrl`/`imageUrl` field stages a `Modify` action | Unit test | `Quotinator.Engine.Tests.PlanSoundCuesAsync_IdMatchFound_TextDiffers_StagesModifyAction` |
| 2 | ✅ | An id-match with nothing changed stages no action (silent reuse) | Unit test | `Quotinator.Engine.Tests.PlanSoundCuesAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 3 | ✅ | A `Complete`-status id-matched row with a policy-resolved change stages `Blocked`, not `Modify` | Unit test | `Quotinator.Engine.Tests.PlanSoundCuesAsync_CompleteStatus_StagesBlockedNotModify` |
| 4 | ✅ | A `Complete`-status row under `Skip` policy never blocks (Skip's resolved value always equals the existing value) | Unit test | `Quotinator.Engine.Tests.PlanSoundCuesAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 5 | ✅ | The decide endpoint accepts SoundCue `text`/`soundFileUrl`/`imageUrl` field decisions for a `Modify` action | Unit test | `Quotinator.Engine.Tests.DecideAsync_SoundCueModify_ResolvesFieldDecisions` |
| 6 | ✅ | Reversing an applied SoundCue `Modify` restores `ExistingValue`'s fields, not a soft-delete | Unit test | `Quotinator.Engine.Tests.ReverseBatchAsync_SoundCueModify_RestoresExistingValue` |
| 7 | ✅ | Reversing an applied SoundCue `Add` still soft-deletes if unreferenced (regression) | Unit test | Existing SoundCue Add-reversal coverage re-run — no gap found, split behaves identically for the `Add` branch |
| 8 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,183/1,183 passing, 0 warnings, 0 errors |
| 9 | ✅ | Build clean | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s) |
| 10 | ✅ | Live: a `sources`-style correction to a SoundCue's `text`/`soundFileUrl`/`imageUrl` via `soundCues[]` stages/decides/applies a `Modify`, and a `Complete` SoundCue's field cannot be silently overwritten | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .`: imported a `soundCues[]` entry, decided a Pending Modify with `markCompletenessAs: Complete` (`ambiguousFields` correctly reported `["text"]` beforehand), re-imported a changed `text` under `review` policy — confirmed the resulting action was `Blocked` (`GET /import/actions?status=Blocked`) and the on-disk `Text` was unchanged. Separately, single-shot corrected a non-`Complete` SoundCue's `text`, confirmed the write landed via `Quotinator.Tools.DbInspector`, then reversed it (preview + real) and confirmed the pre-correction `text` was restored. Note: the two-phase decide→`/import/actions/apply` path never marks `ImportBatches.Status = Applied` — a pre-existing, entity-agnostic gap in the shared coordinator (unrelated to this issue's own logic), so this row's reverse check used the single-shot direct-apply path instead. Flagged separately as a follow-up task, not fixed here. |
| 11 | ✅ | App still opens and builds in Visual Studio | Live (T1) | Developer confirmed clean startup in Visual Studio (schema v8/data v10, multiple `GET /api/v1/quotes/random` → 200, no errors) |

---

## Notes

T1 and T2 are both required per this project's blanket rule (see #168's own "Notes" section — no
Razor/migration-surface exemption applies to a genuine C# logic change). Translations
(`SoundCueTranslations`) are explicitly out of scope for this issue — only the parent row's `text`/
`soundFileUrl`/`imageUrl` become correctable; see step 8. If #171 (StageDirection) lands first, its
`PlanStageDirectionsAsync`/`ReverseAppliedActionsAsync`/`DecideAsync` shapes should be used as an
additional cross-check alongside `PlanSourcesAsync` before implementing this issue's steps, since the two
entities share the same "single-row, translations-adjacent" sub-problem by design.

**Implemented together with #171**, per the developer's direction — #171 landed in the same pass, so
its shapes were the direct cross-check for this issue's steps rather than a hypothetical. See #171's own
plan doc for the parallel implementation notes.

**Found during T2 verification, not part of this issue's own scope:** the two-phase
`decide` → `POST /import/actions/apply` flow never marks the owning `ImportBatches.Status` as
`Applied` (only the single-shot direct-apply path in `SqliteQuoteImportService.cs` does), which
silently breaks `POST /import/actions/reverse` for any batch staged under `review`. This is
entity-agnostic — affects Quote/Source/StageDirection/SoundCue alike — and pre-existing (the test
suite's own `MarkImportBatchAppliedAsync` helper has been working around it via raw SQL). Flagged as a
separate follow-up task, not fixed as part of #171/#172.
