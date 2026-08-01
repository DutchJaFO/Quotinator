# #254 — Rename Quotinator.Core-owned tables and entities

**Status:** Planning
**GitHub issue:** #254
**Tiers required:** T1, T2
**Depends on:** Nothing

---

## Background

Implements [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md) (table domain
prefixes) and [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md)
(class suffixes) for every table `Quotinator.Core` owns. Split from #227 (2026-08-01) into a
per-project sub-issue alongside #253 (the `Quotinator.Data` sibling — see that issue's plan doc for why
the split is by project, not by table-rename-vs-class-rename). Full research already done in
[#227's reference doc](227-domain-prefixed-naming-implementation-plan.md).

**`ImportBatches` is out of scope here** — reclassified as `Quotinator.Data`-owned (`Import_Batch`) by
#253, not renamed by this issue. This issue's own migration/baseline only needs to update its
`REFERENCES ImportBatches(Id)` clauses to `REFERENCES Import_Batch(Id)` once #253's rename lands.

## Table/class mapping

| Table today | Table after | Class today | Class after |
|---|---|---|---|
| `Quotes` | `Quotinator_Quote` | `QuoteEntity` | *(unchanged)* |
| `Sources` | `Quotinator_Source` | `Source` | `SourceEntity` |
| `Characters` | `Quotinator_Character` | `Character` | `CharacterEntity` |
| `People` | `Quotinator_Person` | `Person` | `PersonEntity` |
| `Series` | `Quotinator_Series` | `Series` | `SeriesEntity` |
| `Universe` | `Quotinator_Universe` | `Universe` | `UniverseEntity` |
| `Conversations` | `Quotinator_Conversation` | `ConversationEntity` | *(unchanged)* |
| `StageDirections` | `Quotinator_StageDirection` | `StageDirectionEntity` | *(unchanged)* |
| `SoundCues` | `Quotinator_SoundCue` | `SoundCueEntity` | *(unchanged)* |
| `ConversationLines` | `Quotinator_ConversationLine` | `ConversationLineEntity` | *(unchanged)* |
| `CharacterSources` | `Quotinator_CharacterSource` | `CharacterSourceEntity` | *(unchanged)* |
| `QuoteTranslations` | `Quotinator_QuoteTranslation` | `QuoteTranslationEntity` | *(unchanged)* |
| `SourceTranslations` | `Quotinator_SourceTranslation` | `SourceTranslation` | `SourceTranslationEntity` |
| `CharacterTranslations` | `Quotinator_CharacterTranslation` | `CharacterTranslation` | `CharacterTranslationEntity` |
| `StageDirectionTranslations` | `Quotinator_StageDirectionTranslation` | `StageDirectionTranslationEntity` | *(unchanged)* |
| `SoundCueTranslations` | `Quotinator_SoundCueTranslation` | `SoundCueTranslationEntity` | *(unchanged)* |
| `QuoteGenres` | `Quotinator_QuoteGenre` | `QuoteGenreEntity` | *(unchanged)* |

17 tables, all already have (or gain) an `Entity`-suffixed class.

## Current migration structure (confirmed against the actual code, 2026-08-01)

All migration SQL for `Quotinator.Core` lives inline in
`src/Quotinator.Core/Database/QuotinatorMigrations.cs` as `QuotinatorMigrations.All` (the incremental
list) and `QuotinatorMigrations.Baseline` (the fresh-install path). Per the last shipped tag `v1.8.2`
(2026-07-31): migrations 1–4 are frozen; migration 5 (#150's CHECK constraint) is unreleased and safe
to rewrite. **Plan:** squash migration 5 together with all 17 renames into one new migration 5,
replacing it — not appended after it. Net: 5 Consumer migrations total, unchanged count.

`ImportBatches(Id)` is referenced by `Quotes`, `Sources`, `Characters`, and `People` (confirmed via
grep — 19 occurrences of `REFERENCES ImportBatches(Id)` across the incremental migrations and the
baseline in `QuotinatorMigrations.cs`, e.g. lines 165–168, 299, 313, 340, 393, 407, 445, 691, 705, 725,
752, 792, 809, 844, 858, 885). Every one becomes `REFERENCES Import_Batch(Id)` once #253 lands.

---

## Steps

### 1. Write the squashed migration

**Status:** ⬜ Not started

Rewrite `QuotinatorMigrations.cs`'s migration 5 (today's #150 CHECK constraint, unreleased) to also
include every `ALTER TABLE ... RENAME TO` for the 17 tables above, and update every
`REFERENCES ImportBatches(Id)` clause it introduces or touches to `REFERENCES Import_Batch(Id)`.

### 2. Update QuotinatorMigrations.Baseline

**Status:** ⬜ Not started

Update the fresh-install baseline to create every table under its final `Quotinator_`-prefixed name
directly, with every `REFERENCES Import_Batch(Id)` clause already correct (matching #253's baseline).
Remove `ImportBatches`' own `CREATE TABLE` from this baseline entirely — it moves to
`Quotinator.Data`'s `DataBaselineSql` per #253. Coordinate merge order with #253 so neither baseline is
left creating `ImportBatches` twice or not at all during the transition.

### 3. Rename entity classes and files

**Status:** ⬜ Not started

- `Source` → `SourceEntity`, `Character` → `CharacterEntity`, `Person` → `PersonEntity`,
  `Series` → `SeriesEntity`, `Universe` → `UniverseEntity`
- `SourceTranslation` → `SourceTranslationEntity`, `CharacterTranslation` → `CharacterTranslationEntity`
- Update every `[Table("...")]` attribute on all 17 entities (including the ones whose C# class name
  doesn't change, e.g. `QuoteEntity`, `ConversationEntity` — only their attribute value changes)

Compiler-verified (CS0246) — no logic change. Generic type arguments
(`SqliteRestorableRepository<Character>` etc.) and every `new Character { ... }` construction site need
the new class name; DI registrations for renamed types need updating.

### 4. Update Quotinator.Core/Queries/Sql.cs

**Status:** ⬜ Not started

The largest mechanical surface in this issue: ~321 references to the 17 table names across ~886 lines
(`FROM`/`JOIN`/column-qualifier prefixes). The generic repository layer
(`RepositorySql`/`EntityColumnMetadata`) needs **no manual changes** — it reads table names reflectively
from each entity's `[Table]` attribute, so step 3 alone covers it; this step is scoped entirely to
hand-written SQL in `Sql.cs`.

### 5. Update Core-side guard tests

**Status:** ⬜ Not started

Audit `SqlQueryGuardTests` (and any sibling reflection-based guard test in `Quotinator.Core.Tests`) for
a hardcoded old table/class name that would silently stop being scanned — same risk class as #253's
step 6.

### 6. Full solution build, test, and Docker verification

**Status:** ⬜ Not started

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green,
including migration verification per the checklist below. T1 (developer starts app in Visual Studio) +
T2 (Docker smoke test).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `QuotinatorMigrations.Baseline` and the incremental migration path produce identical schemas, including CHECK constraint behaviour | Unit test | The existing `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` and `...AcceptSameCheckConstraintValues` tests (not new — they compare generically and don't need renaming) |
| 2 | ❌ | Every `REFERENCES ImportBatches(Id)` clause updated to `REFERENCES Import_Batch(Id)` | Live | `rg "REFERENCES ImportBatches" src/Quotinator.Core/Database/QuotinatorMigrations.cs` returns nothing |
| 3 | ❌ | Every entity class renamed, `[Table]` attributes updated | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |
| 4 | ❌ | `Quotinator.Core/Queries/Sql.cs` updated, no hand-written query references an old table name | Live | `rg "FROM Quotes|FROM Sources|FROM Characters|FROM People|FROM Series|FROM Universe|FROM Conversations|FROM StageDirections|FROM SoundCues" src/Quotinator.Core/Queries/Sql.cs` (unqualified old names) returns nothing |
| 5 | ❌ | No guard test silently stopped scanning a renamed table/class | Unit test | `SqlQueryGuardTests` (and siblings) pass with the renamed identifiers present in their own `DynamicData`/reflection enumeration |
| 6 | ❌ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false` both 0 warnings, 0 errors, all green |
| 7 | ❌ | T1 verified | Live | Developer starts app in Visual Studio, confirms no startup error |
| 8 | ❌ | T2 verified | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeds; smoke-test commands touching quotes/sources/characters/people data return expected output |

---

## Scope changes

**`ImportBatches` out-of-scope note**: already understood as #253's responsibility from #227's
original planning pass — reflected here as an explicit out-of-scope note, not a new deferral.

**Full incremental migration-path verification against the last published release's schema (ADR 009)
is deliberately not this issue's own verification row.** Same reasoning as #253 (its sibling issue) —
deferred to a dedicated tracked issue at milestone close (per `docs/workflow/process.md`, see #155 for
the worked example), not built per issue, since this milestone's migrations are expected to be further
consolidated before it closes. An earlier draft of this plan doc and its GitHub issue incorrectly
included `v1.8.2`-specific tests in this issue's own scope; corrected 2026-08-01.
