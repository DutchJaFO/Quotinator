# #64 — Conflict resolution policy

**Status:** In progress (step 1)
**Tiers required:** T1, T2
**GitHub issue:** #64
**Depends on:** #63 (manifest field — done), #45 (per-import override — not started), #58 (batch recording — waiting for release)

---

## Spec requirements

1. Five policies: `skip`, `newest-wins`, `merge-ours`, `merge-theirs`, `review`. `review` behaves identically to `skip` today — reserved for #45's future human-review workflow, added now so no further schema/enum change is needed when #45 lands.
2. Default policy: `newest-wins`.
3. Global config key: `Quotinator__DefaultConflictPolicy` (flat); per-entity-type overrides (`Quotes`/`Sources`/`Characters`/`People`/`Translations`) stay nested under `Quotinator:DuplicateResolution:*`.
4. Manifest-level `duplicateResolution` override (per-source, per-type) — accepted scope expansion beyond #64's original text, built for #57/#61, documented via GitHub comment (see Scope changes below).
5. `merge-ours`/`merge-theirs` behaviour: per field, auto-fill from whichever side is non-empty when the other is empty/null. When both sides have differing non-empty values (a true conflict), `merge-ours` keeps the existing value and `merge-theirs` takes the incoming value. Applies identically to scalar and array/list fields — no union/combine behavior.
6. `ImportBatches.ConflictPolicy` — new column, modeled as `SafeValue<DuplicateResolutionPolicy?>` (enum, via the existing `SafeEnumHandler<TEnum>` infrastructure), recording the batch's representative applied policy (quotes' effective policy, since a batch may span multiple entity types but the issue asks for one column).
7. New `System_ImportConflicts` table, owned by `Quotinator.Data` (own migration list, `System_SchemaVersion`, excluded from Reset via the existing `System_%` pattern match) — logs **every** detected conflict, not only pending ones, with a `Status` (`resolved`/`pending`). `ExistingValue`/`IncomingValue` are opaque JSON blobs (Data never parses them). `AppliedPolicy` is the same enum/`SafeValue` pattern as #6. `MergedFields` is an optional opaque JSON blob populated only for `merge-ours`/`merge-theirs` resolutions, documenting per-field which side won.
8. Per-import-run override via query parameter — blocked on #45 (not started); left out of scope, no speculative parameter added.
9. Known limitation, accepted for now: the single generic conflict table assumes one import "domain" (quotes and related entities). Revisit if Quotinator ever adds a second, structurally distinct import domain.

---

## Steps

### 1. Rename `Overwrite` → `NewestWins`; complete the five-value vocabulary
**Status:** ⬜ Not started — the underlying `Skip`/`Overwrite` dual-policy mechanism already works in production code today; this step is the rename to the full five-value vocabulary, not new behaviour for the two existing values.

`DuplicateResolutionPolicy` enum gains all five members: `Skip`, `NewestWins`, `MergeOurs`, `MergeTheirs`, `Review`. Wire strings (JSON and config): `"skip"`, `"newest-wins"`, `"merge-ours"`, `"merge-theirs"`, `"review"` — via `JsonNamingPolicy.KebabCaseLower` (must be asserted for the multi-word values, not assumed, e.g. `MergeOurs` → `"merge-ours"`). `ManifestSeedPlannerTests`' `Overwrite` reference updates to `NewestWins`.

### 2. Flip defaults to `NewestWins`
**Status:** ⬜ Not started

`ManifestPolicy.HardcodedDefault` and `ManifestPolicyDto.Default`'s omitted-key default both become `NewestWins` (currently `Skip`). `data/sources/manifest.json`'s explicit `"skip"` stays untouched — it's a deliberate per-source override, unaffected by the global default changing.

### 3. Update `schemas/manifest.schema.json`'s enum values
**Status:** ⬜ Not started

`duplicateResolution` enum values become `["skip", "newest-wins", "merge-ours", "merge-theirs", "review"]`.

### 4. Restructure config keys
**Status:** ⬜ Not started

`Program.cs`'s `ParseResolutionPolicy`/`ParseNullableResolutionPolicy` read a new flat `Quotinator:DefaultConflictPolicy` key (env `Quotinator__DefaultConflictPolicy`, default `newest-wins` when absent), replacing `Quotinator:DuplicateResolution:Default`. The 5 nested per-type keys (`Quotinator:DuplicateResolution:{Quotes,Sources,Characters,People,Translations}`) keep their paths, minus the now-redundant `Default` sibling. String matching covers all five wire values.

### 5. Fix JSON kebab-case enum serialization
**Status:** ⬜ Not started

A small `DuplicateResolutionPolicyJsonConverter : JsonStringEnumConverter<DuplicateResolutionPolicy>(JsonNamingPolicy.KebabCaseLower)` (parameterless-constructor subclass, since the attribute form can't pass constructor arguments directly), referenced on all `ManifestPolicyDto` properties. Verified via an actual round-trip unit test for all five values — a plain `JsonStringEnumConverter` would only case-insensitively match member names, not hyphenate `"newest-wins"`, so this must not be assumed correct.

### 6. Rename `Sql.Quotes.UpdateOnOverwrite` → `UpdateOnNewestWins`
**Status:** ⬜ Not started

Pure rename, no behaviour change. Update the matching `SqlQueryGuardTests` reference in the same commit.

### 7. Implement `merge-ours`/`merge-theirs` field-level resolution
**Status:** ⬜ Not started

A generic, reusable per-field comparison in `Quotinator.Data` (works against a field-name→value representation): auto-fill from whichever side is non-empty when the other is empty/null; when both sides have differing non-empty values, `merge-ours` keeps the existing value and `merge-theirs` takes the incoming value. Applies identically to scalar and array/list fields — no union/combine behaviour. Requires Engine-side code that converts a `Quote`/`SourceQuote` to/from that field-name→value representation so the shared helper can run without `Quotinator.Data` knowing the Quote schema.

### 8. Add `ImportBatches.ConflictPolicy` column
**Status:** ⬜ Not started

New `Migration005_ImportBatchConflictPolicy` in `QuotinatorMigrations.cs`: `ALTER TABLE ImportBatches ADD COLUMN ConflictPolicy TEXT NOT NULL DEFAULT 'skip'` (backfill value for pre-existing rows only — new rows get their real applied policy at insert time), following the established plain-`ALTER` pattern from Migration003 (no idempotency guard needed, per the existing transaction+backup/restore safety net in `ApplyMigrationsAsync`). `ImportBatch.cs` gains `public SafeValue<DuplicateResolutionPolicy?> ConflictPolicy { get; init; }` (not a plain string — `Quotinator.Data` already has the right generic infrastructure for enum-backed columns via `SafeEnumHandler<TEnum>`, already used for `QuoteType`/`Genre`). Requires `RegisterEnumHandler<DuplicateResolutionPolicy>()` added to `QuotinatorDapperConfiguration.RegisterDomainHandlers()`. Populated at `CreateImportBatchAsync` from `seedBatch.Policy.ForQuotes`.

### 9. Add `System_ImportConflicts` table (Data-owned)
**Status:** ⬜ Not started

New `src/Quotinator.Data/Database/ImportConflictMigrations.cs` (mirrors `AuditMigrations.cs`), added directly to `DatabaseInitializer.DataOwnedMigrations` (tracked via `System_SchemaVersion`) — no rename-migration needed since the table is introduced with its final `System_`-prefixed name from the start. Columns: `Id` (long, autoincrement, `[Key]`), `BatchId` (string, loose reference — no FK, since Data doesn't know Engine's table names), `EntityType` (string, free text, e.g. `"Quote"`), `EntityId` (string, nullable), `ExistingValue`/`IncomingValue` (string, nullable, opaque JSON — Data never parses them; Engine produces and later diffs that content), `AppliedPolicy` (`SafeValue<DuplicateResolutionPolicy?>`), `Status` (string: `"resolved"`/`"pending"`), `MergedFields` (string, nullable, opaque JSON, populated only when `AppliedPolicy.Parsed` is `MergeOurs`/`MergeTheirs`), `DetectedAt` (DateTime), `ResolvedAt` (DateTime, nullable). New entity `src/Quotinator.Data/Entities/SystemImportConflict.cs`, `[Table("System_ImportConflicts")]`. New `Sql.SystemImportConflicts` nested class in `src/Quotinator.Data/Queries/Sql.cs` (mirrors `Sql.SystemAudit`). New `ISystemImportConflictWriter`/`ISystemImportConflictReader` in `src/Quotinator.Data/Repositories/` (mirrors `ISystemAuditWriter`/`ISystemAuditReader`), plus their implementations. `QuotinatorDatabaseInitializer`'s duplicate-detection loop writes one row per detected conflict (all five policies, not just `review`), including a merge row's `MergedFields` blob.

### 10. Update baseline schema and schema-drift tests
**Status:** ⬜ Not started

Update `QuotinatorMigrations.Baseline` (`ImportBatches.ConflictPolicy`) and `DatabaseInitializer.DataBaselineSql` (`System_ImportConflicts`) in the same commit as their respective migrations, per CLAUDE.md's baseline policy. Re-run `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` and the equivalent Data-side schema-drift test for `System_ImportConflicts` — confirm no drift, don't assume.

### 11. Fix existing test fallout
**Status:** ⬜ Not started

`DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates` asserts `Skip` via `HardcodedDefault` directly; becomes `NewestWins`, with its misleading "(manifest default)" comment corrected. Four `SchemaVersion == 4` assertions bump to `5`; the `(3,4)` rollback-simulation set in `InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset` reviewed for whether it should become `(4,5)` to preserve the test's original intent.

### 12. Add new test coverage
**Status:** ⬜ Not started

Content-level `newest-wins` assertion (surviving row's fields actually match the later file). `merge-ours`/`merge-theirs`: incoming record with some empty fields → existing values retained for those fields; a field where both sides have differing non-empty values resolves per the ours/theirs tie-break; `MergedFields` blob correctly records the per-field source. Cover both scalar and array fields. `HardcodedDefault == NewestWins` regression guard. Config-parsing tests (absent/skip/newest-wins/merge-ours/merge-theirs/review/garbage input) — likely requires extracting `ParseResolutionPolicy`/`ParseNullableResolutionPolicy` out of `Program.cs`'s top-level statements into a small testable static class, since they're currently local functions. JSON kebab-case round-trip test for all five values. `ImportBatches.ConflictPolicy` persistence test. `System_ImportConflicts`: a row is written for every conflict regardless of policy; `Status` reflects `resolved` for skip/newest-wins/merge-ours/merge-theirs and `pending` for `review`; excluded from Reset like `System_AuditEntries`.

**Explicitly out of scope for all steps above:** #45's per-run query param and endpoint; the actual review/resolve workflow UI.

---

## Scope changes

Reconciled 2026-07-04 — see comment on #64 for the full record:

- **`merge-ours`/`merge-theirs`/`review` policies and the new `System_ImportConflicts` table** were not in #64's original text. `review` had been drafted as invented scope conflated from #45's own `conflictStrategy: skip|overwrite|review` design; the plan doc previously proposed a `ConflictQueue` table with no GitHub-issue basis. These are now deliberately expanded and owned by #64, since #64 defines the durable conflict-resolution data model the rest of the milestone (and #45 specifically) builds on. Modeled on Git's own merge-conflict vocabulary (whole-side `ours`/`theirs`, recursive auto-merge with `-X ours`/`-X theirs`, manual resolution as the fallback).
- **Manifest-level `duplicateResolution` override and per-entity-type granularity** (`Quotes`/`Sources`/`Characters`/`People`/`Translations`) were built for #57/#61's cross-source dedup needs but never posted as a scope-change comment on any issue. Retroactively accepted and documented here.
- **Bundled `data/sources/manifest.json` file order** was reviewed and corrected (see below) as a directly related fix, even though file ordering is nominally #63's concern.
- #45's own `conflictStrategy`/`review`/`/resolve` design predates this reconciliation and overlaps in terminology — flagged via a comment on #45, not resolved here.

### Bundled file order fix

Field-population counts measured across the full files:

| File | Total | `date` | `character` | `genres` |
|---|---|---|---|---|
| `quotinator-curated.json` | 2 | 100% | 100% | 100% |
| `NikhilNamal17_popular-movie-quotes.json` | 732 | 99.9% | 0% | 0% |
| `vilaboim_movie-quotes.json` | 99 | 0% | 0% | 0% |

The manifest's `files` order was `curated → vilaboim → NikhilNamal17`, with the bundled manifest's explicit `"duplicateResolution": {"default": "skip"}` override (first-seen wins). This meant any quote appearing in both automated sources kept `vilaboim`'s bare, dateless version and discarded `NikhilNamal17`'s dated one — backwards from #63's own stated purpose. Reordered to `curated → NikhilNamal17 → vilaboim`: curated stays protected first, and the richer automated source now wins ties against the barer one.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `skip` keeps existing record; `newest-wins` overwrites with incoming (content-level) | Unit test | Not yet implemented |
| 2 | ❌ | `merge-ours`/`merge-theirs` auto-fill blank fields from either side | Unit test | Not yet implemented |
| 3 | ❌ | `merge-ours`/`merge-theirs` resolve a true field conflict (both sides non-empty, differing) per the correct tie-break direction, for both scalar and array fields | Unit test | Not yet implemented |
| 4 | ❌ | `review` behaves identically to `skip` today | Unit test | Not yet implemented |
| 5 | ❌ | Default policy is `newest-wins` when nothing overrides it | Unit test | Not yet implemented |
| 6 | ❌ | Enum/config/schema/SQL vocabulary is consistent across all five values, no `overwrite` remaining | Unit test | Not yet implemented |
| 7 | ❌ | `Quotinator__DefaultConflictPolicy` read correctly; per-type keys still work nested | Unit test | Not yet implemented |
| 8 | ❌ | Manifest `duplicateResolution.default`, when key present but value omitted, resolves to `newest-wins` | Unit test | Not yet implemented |
| 9 | ❌ | All five wire strings round-trip correctly via the new JSON converter | Unit test | Not yet implemented |
| 10 | ❌ | `ImportBatches.ConflictPolicy` is enum-backed (`SafeValue<DuplicateResolutionPolicy?>`), backfills `'skip'` for pre-existing rows, and new batches persist their actual applied policy | Unit test | Not yet implemented |
| 11 | ❌ | `System_ImportConflicts` logs every conflict (not just pending), correct `Status`, `MergedFields` populated only for merge resolutions | Unit test | Not yet implemented |
| 12 | ❌ | `System_ImportConflicts` excluded from Reset, same as `System_AuditEntries` | Unit test | Not yet implemented |
| 13 | ❌ | Baseline and incremental-replay schemas match after both new migrations | Unit test | Not yet implemented |
| 14 | ❌ | Per-import override via `?conflictPolicy=` — blocked on #45 | N/A | Deferred, no seam added yet |
| 15 | ✅ | Bundled manifest file order lets richer sources win ties (`curated → NikhilNamal17 → vilaboim`) | Live | `data/sources/manifest.json` `files` order, verified against measured field-population counts above |
