# #254 — Rename Quotinator.Core-owned tables and entities

**Status:** Waiting for release
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

## Current migration structure (confirmed against the actual code, 2026-08-01; corrected 2026-08-02)

All migration SQL for `Quotinator.Core` lives inline in
`src/Quotinator.Core/Database/QuotinatorMigrations.cs` as `QuotinatorMigrations.All` (the incremental
list) and `QuotinatorMigrations.Baseline` (the fresh-install path). Per the last shipped tag `v1.8.2`
(2026-07-31): migrations 1–4 are frozen. **Original plan (wrong, corrected 2026-08-02): squash
migration 5 (#150's CHECK constraint) together with all 17 renames into one new migration 5, replacing
it in place, on the reasoning that migration 5 had never shipped in a tagged release.** Found live
during this issue's own T1 pass: this project's "never edit an existing migration" policy is not
scoped to tagged releases — it applies to any database that may already have run a migration, and this
project's own local dev database had already run migration 5's original (CHECK-constraint-only)
content before this rename work was designed, in an earlier development session. Rewriting migration
5's content in place left that database's already-recorded version 5 silently out of sync with the new
content, reproducing exactly the "recorded version doesn't match actual schema" failure mode this
project's migration policy exists to prevent. **Corrected plan:** migration 5 is restored to its
original, frozen, CHECK-constraint-only content; the rename is a new migration 6, appended after it —
matching this milestone's own standing rule that migrations are only consolidated at milestone close or
an intermediate release, never mid-milestone. Net: 6 Consumer migrations total (one more than
originally planned, not the same count).

`ImportBatches(Id)` is referenced by `Quotes`, `Sources`, `Characters`, and `People` (confirmed via
grep — 19 occurrences of `REFERENCES ImportBatches(Id)` across the incremental migrations and the
baseline in `QuotinatorMigrations.cs`, e.g. lines 165–168, 299, 313, 340, 393, 407, 445, 691, 705, 725,
752, 792, 809, 844, 858, 885). Every one becomes `REFERENCES Import_Batch(Id)` once #253 lands.

---

## Steps

### 1. Write the rename migration

**Status:** ✅ Done (corrected 2026-08-02 — new migration 6, not a rewrite of migration 5)

Add a new `Migration006_DomainPrefixRename` to `QuotinatorMigrations.cs`, appended after the
untouched, restored migration 5 (#150's CHECK constraint), including every `ALTER TABLE ... RENAME TO`
for the 17 tables above, and updating every `REFERENCES ImportBatches(Id)` clause it introduces or
touches to `REFERENCES Import_Batch(Id)`. See "Current migration structure" above for why this is a
new migration rather than an edit to migration 5.

### 2. Update QuotinatorMigrations.Baseline

**Status:** ✅ Done

Update the fresh-install baseline to create every table under its final `Quotinator_`-prefixed name
directly, with every `REFERENCES Import_Batch(Id)` clause already correct (matching #253's baseline).
Remove `ImportBatches`' own `CREATE TABLE` from this baseline entirely — it moves to
`Quotinator.Data`'s `DataBaselineSql` per #253. Coordinate merge order with #253 so neither baseline is
left creating `ImportBatches` twice or not at all during the transition.

### 3. Rename entity classes and files

**Status:** ✅ Done

- `Source` → `SourceEntity`, `Character` → `CharacterEntity`, `Person` → `PersonEntity`,
  `Series` → `SeriesEntity`, `Universe` → `UniverseEntity`
- `SourceTranslation` → `SourceTranslationEntity`, `CharacterTranslation` → `CharacterTranslationEntity`
- Update every `[Table("...")]` attribute on all 17 entities (including the ones whose C# class name
  doesn't change, e.g. `QuoteEntity`, `ConversationEntity` — only their attribute value changes)

Compiler-verified (CS0246) — no logic change. Generic type arguments
(`SqliteRestorableRepository<Character>` etc.) and every `new Character { ... }` construction site need
the new class name; DI registrations for renamed types need updating.

### 4. Update Quotinator.Core/Queries/Sql.cs

**Status:** ✅ Done

The largest mechanical surface in this issue: ~321 references to the 17 table names across ~886 lines
(`FROM`/`JOIN`/column-qualifier prefixes). The generic repository layer
(`RepositorySql`/`EntityColumnMetadata`) needs **no manual changes** — it reads table names reflectively
from each entity's `[Table]` attribute, so step 3 alone covers it; this step is scoped entirely to
hand-written SQL in `Sql.cs`.

### 5. Update Core-side guard tests

**Status:** ✅ Done

Audit `SqlQueryGuardTests` (and any sibling reflection-based guard test in `Quotinator.Core.Tests`) for
a hardcoded old table/class name that would silently stop being scanned — same risk class as #253's
step 6.

### 6. Full solution build, test, and Docker verification

**Status:** ✅ Done

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green
(2870/2870 across all projects, up from 2855 — new tests added alongside the seeding-safety-net and
degraded-mode work below). T2 re-verified against the corrected migration 5/6 numbering (data v5/app
v6, after the further Data-side version 3/4/5 correction). T1 verified by the developer 2026-08-02 —
clean migration replay from their real local dev database, the exact scenario this whole correction
exists for.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `QuotinatorMigrations.Baseline` and the incremental migration path produce identical schemas, including CHECK constraint behaviour | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` and `...AcceptSameCheckConstraintValues` pass |
| 2 | ✅ | Every `REFERENCES ImportBatches(Id)` clause introduced or touched by migration 6 (or the baseline) updated to `REFERENCES Import_Batch(Id)` | Live | `awk '/internal const string Migration006_DomainPrefixRename/,0' src/Quotinator.Core/Database/QuotinatorMigrations.cs \| grep -c "REFERENCES ImportBatches"` (from migration 6's own const declaration onward) returns `0`. The hits in frozen migrations 1–5 are correct and expected — those migrations ran when the table was genuinely still named `ImportBatches` and are never edited (CLAUDE.md's frozen-migration rule) |
| 3 | ✅ | Every entity class renamed, `[Table]` attributes updated | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |
| 4 | ✅ | `Quotinator.Core/Queries/Sql.cs` updated, no hand-written query references an old table name | Live | `rg "FROM Quotes\|FROM Sources\|FROM Characters\|FROM People\|FROM Series\|FROM Universe\|FROM Conversations\|FROM StageDirections\|FROM SoundCues" src/Quotinator.Core/Queries/Sql.cs` (unqualified old names) returns nothing |
| 5 | ✅ | No guard test silently stopped scanning a renamed table/class | Unit test | `SqlQueryGuardTests` (and siblings) pass with the renamed identifiers present in their own `DynamicData`/reflection enumeration |
| 6 | ✅ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false` both 0 warnings, 0 errors — 2855/2855 tests green across all projects |
| 7 | ✅ | T1 verified | Live | Developer confirmed 2026-08-02, after the migration 5/6 correction: clean startup log — `applying 1 pending Data migration(s) (version 4 → 5)... applying 1 pending App migration(s) (version 5 → 6)... schema updated (data v5, app v6)`, full seed (799 quotes etc.), `/health`/`/version`/`/masterdata/sources` all `200` |
| 8 | ✅ | T2 verified | Live | Re-verified 2026-08-02 against the corrected migration 5/6 split: `docker build` succeeded; fresh baseline schema created at data v3/app v6 (was v5 before the split), full bundled-source seed completed (799 quotes, 461 sources, etc.). Smoke-tested `/quotes/random`, `/masterdata/sources`, `/quotes?universeId=`, `/health` (now `200 {"status":"healthy"}`), and `/openapi/v1.json` — all returned expected data |

---

## Scope changes

**The rename is migration 6, not a rewrite of migration 5 — corrected 2026-08-02, found live during
this issue's own T1 pass.** See "Current migration structure" above for the full incident. Short
version: this project's "never edit an existing migration" policy protects any database that may have
already run a migration, not only databases running a tagged release — this project's own local dev
database had already run migration 5's original content in an earlier session, so rewriting it in
place (reasoning "it never shipped, so it's safe") broke that database's already-recorded version
tracking. Every reference to "migration 5" elsewhere in this plan doc describing the domain-prefix
rename itself should be read as "migration 6" after this correction; left as originally written below
where the surrounding text is otherwise still accurate, rather than rewriting the whole document.

**A genuinely unrelated, pre-existing bug was found (and fixed) while chasing the above: a stale local
test database and a broken test DI registration were compounding to hide each other.**
`ImportEndpointTests.cs` registered its `NoOpDatabaseInitializer` via
`services.AddSingleton(NoOpDatabaseInitializer.Instance)` — with no explicit `<IDatabaseInitializer>`
type argument, this registers the instance under its own concrete type, not the interface Program.cs
actually resolves, so the real `QuotinatorDatabaseInitializer` ran against a real database for every
test in that file, contrary to CLAUDE.md's documented "endpoint tests get zero DB contact by default"
pattern (see `QuoteEndpointsTests.cs` for the correct form: `AddSingleton<IDatabaseInitializer>(new
NoOpDatabaseInitializer())`). This had been silently masked for as long as that real database's schema
happened to stay compatible; it only surfaced as 24 failing `Import_*` tests once the seeding-safety-net
and degraded-mode work (also from this issue's T1 pass, see below) started correctly detecting and
reporting the stale local database's genuine schema/version mismatch. Fixed both: the DI registration
now specifies `<IDatabaseInitializer>` explicitly, and the stale `bin/Release/net10.0/data/` /
`bin/Debug/net10.0/data/` artifacts were deleted (build output, not source-controlled).

**`ImportBatches` out-of-scope note**: already understood as #253's responsibility from #227's
original planning pass — reflected here as an explicit out-of-scope note, not a new deferral.

**Full incremental migration-path verification against the last published release's schema (ADR 009)
is deliberately not this issue's own verification row.** Same reasoning as #253 (its sibling issue) —
deferred to a dedicated tracked issue at milestone close (per `docs/workflow/process.md`, see #155 for
the worked example), not built per issue, since this milestone's migrations are expected to be further
consolidated before it closes. An earlier draft of this plan doc and its GitHub issue incorrectly
included `v1.8.2`-specific tests in this issue's own scope; corrected 2026-08-01.

**Two pre-existing hand-inlined SQL fragments found in `SqliteQuoteService.cs`, outside `Sql.cs`
entirely** (the `?universeId=` filter's `FROM Series` subquery and a `FROM QuoteGenres` `EXISTS`
clause, both built as raw string literals directly in the service rather than via a `Sql.cs` factory
method). Updated to the new table names as part of this issue's own mechanical rename since leaving
them stale would have broken `?universeId=` filtering, but the underlying "String centralisation
policy" violation (CLAUDE.md) predates this issue and is out of this issue's scope to fix properly —
worth a follow-up issue to move both into `Sql.cs` as factory methods.

**Two classes of test fixture needed the opposite fix from the rest of the mechanical rename.** A
keyword-anchored rewrite (`FROM`/`JOIN`/`INTO`/`UPDATE`/`TABLE`/`REFERENCES` + old name → new name)
correctly covers test code that runs against the *final*, fully-migrated schema — but two fixture
families intentionally build a database at a schema version *before* migration 5 runs, and initially
got the same blind rewrite applied by mistake:
- `ImportBatchesTests.CreateV2DatabaseAsync` (simulates consumer schema v2, before migrations 3–5) —
  reverted its `CREATE TABLE`/`INSERT INTO` statements back to the pre-#254 names (`Sources`,
  `Characters`, `People`, `Quotes`, `QuoteGenres`), and added `SourceTranslations`/
  `CharacterTranslations`/`QuoteTranslations`/`QuoteGenres`'s RecordBase columns as stubs — migration
  5's rebuild pass now touches every Migration001-created table, not just the four `ImportBatchId`
  columns the old migration 5 (#150's CHECK constraint) ever touched, so the v2 stub's previously
  "good enough" subset of tables/columns stopped being sufficient.
- `DatabaseInitializerTests.SeedPreMergeCharactersAsync` and
  `Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow` (run the real frozen
  migrations 1–4 directly, then `CharacterGlobalIdentityMerge`, deliberately never migration 5) —
  reverted the same way, back to pre-#254 table names.

**A stale local `quotinatordata.db` under `tests/Quotinator.Api.Tests/bin/Release/net10.0/data/`
(and its `Debug` counterpart), left over from an earlier local test run, caused two `ImportEndpointTests`
failures that had nothing to do with this issue's own code.** Its recorded schema version predated
migration 5, so `DatabaseInitializer` considered it "up to date" and never replayed the rename,
producing a "no such table: Quotinator_Quote" failure indistinguishable at first glance from a genuine
migration bug. Deleted as a build artifact (regenerated automatically); not a source-controlled file.
Consistent with `docs/testing-policy.md`'s general caution against accumulated local database state —
worth remembering the next time an endpoint test fails with a "no such table" error that the source
changes don't obviously explain.
