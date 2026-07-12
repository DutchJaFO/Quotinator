# #162 — Source: explicit file-carried id, decoupling matching from Title/Type/Date content

**Status:** Waiting for release
**GitHub issue:** #162
**Tiers required:** T1, T2
**Depends on:** #165, #149, #154, #67/#68 (identity-model precedent)

---

## Scope changes

This issue started scoped to `Date` only (Source's one field that isn't part of the natural-key
match). Verified against the actual code (`EntityIdentity.SourceId`, `ResolveSourceAsync`): Source is
matched by `WHERE Title=@title AND Type=@type`, so a corrected `Title`/`Type` can never be detected as
drift — it fails to match entirely and gets staged as a brand-new row. Scope grew through several
rounds of scrutiny:

1. Every one of Source's own content fields (`Title`, `Type`, `Date` — everything except FKs to other
   entities) belongs in scope, not just `Date`.
2. Fixing `Title`/`Type` requires decoupling matching from content — giving Source an explicit,
   file-carried id, the identity model `Conversation`/`StageDirection`/`SoundCue` already use (#67/#68),
   combined with Modify/merge support, which none of those three have (they're Add-only). A new
   combination, designed here for the first time.
3. The "never silently overwrite a reviewed record" mechanism this exposed turned out to be a genuine
   cross-entity concern, not Source-specific — split out into #165, which this issue now depends on
   and only consumes.

**Correction found via T2:** `ResolveSourceAsync`'s natural-key branch (unchanged by this issue —
pre-existing #154 code) reads a matched Source id through `Guid?`-typed Dapper mapping, then calls
`.ToString("D").ToUpperInvariant()` on it. That was always safe before this issue: every Source id was
either `Guid.NewGuid()`-based (written via the `GuidHandler`-typed repository path, always normalized
to uppercase on write) or `EntityIdentity`-derived (also always uppercase by construction), so
re-casing was a harmless no-op. A `sources[]` entry's explicit, file-authored id breaks that
assumption — `Guid` has no memory of original string casing, so `ToString("D")` always renders
lowercase *regardless of what's actually stored*, meaning a lowercase-authored id's natural-key match
got silently rewritten to a different string than the real row's id. The Quote's own defensive
`EnsureSourceExistsAsync` call then created a second, duplicate Source row using that wrong-cased id —
found live via T2 (a two-batch scenario: create via `sources[]`, correct via a second import), not by
any unit test, since every existing test exercised either a single-`PlanAsync`-call scenario or a
`Guid.NewGuid()`-seeded row (both cases where the bug is invisible). Fixed by reading the natural-key
match as a raw string (`ExecuteScalarAsync<string?>`), preserving whatever case is actually stored —
same "raw SQL, not the Guid-typed path" principle already applied to Conversation/StageDirection/
SoundCue's own explicit ids. Regression test:
`SqliteImportActionServiceTests.ApplyBatchAsync_LowercaseExplicitSourceId_SecondBatchCorrection_NeverCreatesDuplicateRow`
(confirmed red without the fix, green with it).

---

## Design

### A. Explicit Source ids, decoupling matching from content

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

`schemas/source-extended.schema.json` gains a `sources` array (same shape/precedent as
`stageDirections`/`soundCues`/`conversations`): `id` (required, UUID-v4 pattern, same regex as the
other three), `title` (required), `type` (required), `date` (optional). Purely additive — a file
without a `sources` section parses identically to today.

New `SourceEntry` record (`src/Quotinator.Core/Import/SourceEntry.cs`), doc-commented like
`SourceStageDirection`/`SourceSoundCue`'s "assigned at authoring time and never changes."
`ParsedSourceFile` gains `Sources` (defaults `[]`). `SourceQuoteFileReader.TryParseExtended` gains the
new root-key parse, matching the existing four-section pattern. `SourceQuote.Source` (the per-quote
title string) is **unchanged** — converter-driven bundled sources (`NikhilNamal17`, `vilaboim`) keep
working exactly as today, forever.

`ImportActionPlanner` gains `PlanSourcesAsync`, run before quotes resolve (mirrors the existing
conversations/stageDirections/soundCues ordering). For each declared `SourceEntry`:

1. **Id-based lookup first** (new `Sql.Sources.SelectExistingById`, mirrors `StageDirections.
   SelectIdById`'s shape): row already migrated to the explicit-id model — compare `Title`/`Type`/
   `Date` against it; call `CompletenessGuard.ShouldBlock` (#165) on the changed-field set; stage
   `Modify`, `Blocked`, or nothing accordingly.
2. **Falls back to natural-key lookup** (new `Sql.Sources.SelectExistingByTitleAndType`) only if no
   id-match: a not-yet-migrated row (created before an explicit id existed for it, or via a
   converter-driven flat file with no `sources` section). **Scope boundary, explicit:** this issue
   does not implement automatic re-keying of a pre-existing row onto a newly authored id — that needs
   cascading every FK (`Quotes.SourceId`, `Characters.SourceId`, `SourceTranslations.SourceId`)
   atomically, a distinct, higher-risk migration deserving its own future issue and review. A
   not-yet-migrated row found this way stays matched by natural key as today — `Title`/`Type` aren't
   correctable on it until it's re-created under the new scheme; `Date` is still correctable via the
   same Modify/`CompletenessGuard` path, since natural-key matching already finds it.
3. **No match at all**: stage an `Add`, `EntityId = SourceEntry.Id` (the file's own id, not
   `EntityIdentity.SourceId`). `EntityIdentity.SourceId` is **not removed** — it remains the
   id-assignment fallback for a Source discovered only implicitly through a quote (no `sources`
   section, or the section doesn't cover this title/type), preserving 100% backward compatibility for
   converter-driven sources.

### B. Sql / service / decide-path changes

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

- `Sql.Sources` gains `SelectExistingById`, `SelectExistingByTitleAndType`, `UpdateById` (updates
  `Title`/`Type`/`Date`/`DateModified`/`NoValueKnown`/`CompletenessStatus`; a `Complete` row never
  reaches this path — `CompletenessGuard.ShouldBlock` intercepts it earlier in the planner).
- `SourceActionPayload` gains `Date` (currently `Title`+`Type` only). `EnsureSourceExistsAsync` gains
  a `string? date` parameter (currently hardcodes `Date = null` even on Add — no path today ever
  persists a Source `Date` at all).
- `ApplyResolvedActionAsync`'s Source case splits on `ActionType`: `Add` → `EnsureSourceExistsAsync`
  (as today, now passing `Date`); `Modify` → deserialize `MergedFields`, call `Sql.Sources.UpdateById`,
  then apply #165's `MarkCompletenessAs`/`ComputeNextStatus` logic to persist the resulting
  `CompletenessStatus`.
- `ConflictDecisionRequest` gains `SourceTitle`/`SourceType`/`SourceDate` (nullable `FieldDecision?`
  each — no new endpoint/DTO needed, existing route unchanged). `DecideAsync` gains an `EntityType ==
  Source` branch before the existing `!= Quote` rejection.
- `ReverseAppliedActionsAsync`'s Source case currently always soft-deletes regardless of `ActionType`
  (harmless today — every Source action is an Add — but wrong once Modify exists). Branch on
  `ActionType.Parsed`: `Add` keeps today's soft-delete-if-unreferenced behaviour; `Modify` restores the
  full field set from `ExistingValue` via `Sql.Sources.UpdateById` (no reference-count check needed —
  a Modify reversal never deletes anything).

### C. Documentation

**Status:** ✅ Done — implemented and unit-tested; T1/T2 verification pending

`README.md`/`addon/DOCS.md` — no endpoint shape changes (same routes, same `ConflictDecisionRequest`
DTO gaining fields), but the `/import/actions/{id}/decide` description should note Source is now a
decidable entity type alongside Quote. `Quotinator.slnx` — add `SourceEntry.cs` and any new test files.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A file without a `sources` section parses identically to today | Unit test | `TryParseExtended_BareArray_YieldsQuotesAndEmptyExtendedSections`, `TryParseExtended_ObjectWithNoExtendedSections_YieldsEmptyLists` (both extended with a `Sources.Count == 0` assertion) |
| 2 | ✅ | A `sources` section parses all fields (`id`/`title`/`type`/`date`) | Unit test | `TryParseExtended_FullObject_ParsesAllFiveSections` |
| 3 | ✅ | An id-match compares `Title`/`Type`/`Date` against the existing row | Unit test | `PlanSourcesAsync_IdMatchFound_TitleDiffers_StagesModifyAction`, `PlanSourcesAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 4 | ✅ | No id-match falls back to natural-key lookup | Unit test | `PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged` |
| 5 | ✅ | No match at all stages an Add using the file's own id | Unit test | `PlanSourcesAsync_NoMatchAtAll_StagesAddWithFileId` |
| 6 | ✅ | Title/Type/Date drift on an id-matched row stages a Modify | Unit test | `PlanSourcesAsync_IdMatchFound_TitleDiffers_StagesModifyAction` |
| 7 | ✅ | A `Complete`-status id-matched row stages `Blocked`, not `Modify` | Unit test | `PlanSourcesAsync_CompleteStatus_StagesBlockedNotModify` |
| 7a | ✅ | A same-batch quote referencing an explicitly-declared Source resolves to that same id, not a second `EntityIdentity`-derived one | Unit test | `PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId` — caught a real gap during implementation: `PlanSourcesAsync` originally didn't populate the planner's `sourceIndex` cache, so `ResolveSourceAsync` would have independently derived a different id for the same quote |
| 8 | ✅ | Decide endpoint accepts Source Title/Type/Date decisions | Unit test | `DecideImportAction_SourceEntityType_AcceptsTitleTypeDateDecisions` |
| 9 | ✅ | Applying a Source Modify writes the resolved fields | Unit test | `ApplyBatchAsync_SourceModify_WritesResolvedFields` |
| 10 | ✅ | Reversing a Source Modify restores `ExistingValue`'s fields | Unit test | `ReverseAppliedActionsAsync_SourceModify_RestoresExistingValue` |
| 11 | ✅ | Reversing a Source Add still soft-deletes if unreferenced (regression) | Unit test | `ReverseAppliedActionsAsync_SourceAdd_StillSoftDeletesIfUnreferenced` — this test went red first and caught a real, separate bug: `ClearStaleAddTargetsAsync`'s and `ReverseAppliedActionsAsync`'s Source hard-delete/soft-delete paths used the Guid-typed repository API (which uppercases via `GuidHandler`), an assumption that only held while every Source Add id was `EntityIdentity`-derived (always uppercase). An explicit file-authored `sources[]` id is not guaranteed to be uppercase, so the WHERE clause silently matched zero rows. Fixed by switching both paths to the same raw-SQL approach already used for Conversation/StageDirection/SoundCue's own explicit ids |
| 12 | ✅ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); `dotnet test --configuration Release` → all 9 projects passing, 1150 tests total |
| 13 | ✅ | T1 — the migration upgrade path (shared with #165, see its own plan doc) | Live | Developer's own Visual Studio pass, confirmed clean upgrade |
| 14 | ✅ | T2 — a curated file with a corrected Source `Title` stages/decides/applies a Modify via `sources[]`; a `Complete` Source's field cannot be silently overwritten | Live | `docker build` + live curl cycle against `quotinator:local`: staged and applied an explicit-`sources[]` Title correction (confirmed via direct DB inspection: exactly one Source row, correctly updated); found and fixed the natural-key-matching case-sensitivity bug described in "Correction found via T2" above in the process. Then confirmed the `Complete`-status whole-batch-hold scenario (shared with #165's own T2 row) |

---

## Not in scope for this issue (deferred)

- Automatic re-keying of a pre-existing, natural-key-matched row onto a newly authored explicit id
  (§A.2's scope boundary) — a distinct, higher-risk FK-cascading migration for its own future issue.
- The generalized completeness/review model, `ImportActionStatus.Blocked`, and whole-batch hold — all
  built by #165, only consumed here.
- Extending explicit-id + decidability to Character/Person — each entity's own future issue, though
  this issue's design (id-first, natural-key-fallback) is the pattern they'd follow.
