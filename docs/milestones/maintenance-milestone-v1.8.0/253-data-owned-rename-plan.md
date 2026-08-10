# #253 — Rename Quotinator.Data-owned tables and entities

**Status:** Waiting for release
**GitHub issue:** #253
**Tiers required:** T1, T2
**Depends on:** Nothing

---

## Background

Implements [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md) (table domain
prefixes) and [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md)
(class suffixes) for every table `Quotinator.Data` owns. Split from #227 (2026-08-01) into a
per-project sub-issue alongside #254 (the `Quotinator.Core` sibling) — a table rename and its matching
`[Table]` attribute change must ship in the same deployment, so "table rename" and "class rename"
cannot be split from each other, but splitting by *project* is safe since each project's tables,
entities, and hand-written SQL are self-contained. Full research (migration-squash boundary, mapping,
`REFERENCES` inventory) already done in
[#227's reference doc](227-domain-prefixed-naming-implementation-plan.md) — this plan reuses it rather
than re-deriving it, and adds the concrete current file paths confirmed by reading the actual code on
2026-08-01.

## Table/class mapping

| Table today | Table after | Class today | Class after | Current entity file |
|---|---|---|---|---|
| `System_AuditEntries` | `Audit_Entry` | `SystemAuditEntry` | `AuditEntryEntity` | `src/Quotinator.Data/Entities/SystemAuditEntry.cs` |
| `System_ChangeLog` | `Audit_Change` | `SystemChangeLog` | `ChangeEntity` | `src/Quotinator.Data/Entities/SystemChangeLog.cs` |
| `System_ImportConflicts` | `Import_Conflict` | *(none formal today)* | `ImportConflictEntity` (new) | *(none — read via ad hoc query models today)* |
| `System_ImportActions` | `Import_Action` | `SystemImportAction` | `ImportActionEntity` | `src/Quotinator.Data/Entities/SystemImportAction.cs` |
| `System_SourceFileOverrides` | `Import_SourceFileOverride` | `SourceFileOverride` | `SourceFileOverrideEntity` | `src/Quotinator.Data/Entities/SourceFileOverride.cs` |
| `ImportBatches` | `Import_Batch` | `ImportBatch` | `ImportBatchEntity` | `src/Quotinator.Data/Entities/ImportBatch.cs` |
| `System_SchemaVersion` | *(unchanged — residual)* | — | — | — |
| `System_ConsumerSchemaVersion` | *(unchanged — residual)* | — | — | — |

**`ImportBatches` is reclassified as fully `Quotinator.Data`-owned as part of this issue.** Today it is
created in `Quotinator.Core`'s `QuotinatorMigrations.cs` (`src/Quotinator.Core/Database/QuotinatorMigrations.cs:151`
and again at its baseline, line 669) — table creation moves into `Quotinator.Data`'s
`DataOwnedMigrations`/`DataBaselineSql`, and version tracking moves from `System_ConsumerSchemaVersion`
to `System_SchemaVersion`, per ADR 015. `ImportBatch`'s repository (`SqliteImportBatchRepository`,
`IImportBatchRepository`) already lives in `Quotinator.Data` — only the schema-creation SQL and
migration-list entry are moving, not any C# service code.

## Current migration structure (confirmed against the actual code, 2026-08-01)

`DatabaseInitializer.cs` (`src/Quotinator.Data/Database/DatabaseInitializer.cs`) holds the
`DataOwnedMigrations` list and `DataBaselineSql` inline, but the individual migration SQL itself lives
in separate per-concern files, referenced by name:

```csharp
private static readonly IReadOnlyList<SchemaMigration> DataOwnedMigrations =
[
    new SchemaMigration { Version = 1, Sql = AuditMigrations.CreateAuditEntriesTable },
    new SchemaMigration { Version = 2, Sql = DataConsolidatedMigrations.SinceV172 },
    new SchemaMigration { Version = 3, Sql = ImportConflictMigrations.AddAppliedPolicyCheckConstraint },
    new SchemaMigration { Version = 4, Sql = ImportActionMigrations.AddAppliedPolicyCheckConstraint },
];
```

- `AuditMigrations.cs` — version 1 (frozen, `System_AuditEntries` creation)
- `DataConsolidatedMigrations.cs` — version 2 (frozen; itself composed of
  `ChangeLogMigrations.CreateChangeLogTable` + `SourceFileOverrideMigrations.CreateSourceFileOverridesTable`
  + more, per #155's consolidation)
- `ImportConflictMigrations.cs` — version 3 (unreleased, #150's CHECK constraint)
- `ImportActionMigrations.cs` — version 4 (unreleased, #150's CHECK constraint)

Per the last shipped tag `v1.8.2` (2026-07-31): versions 1–2 are frozen. **Original plan (wrong,
corrected 2026-08-02): versions 3–4 have never shipped in a tagged release, so fold them into a new
`DomainPrefixRenameMigrations.cs`, replacing both, not appending after them.** Found live during
#254's own T1 pass: this project's "never edit an existing migration" policy is not scoped to tagged
releases — it protects any database that has already run a migration, and this project's own local dev
database had already run versions 3 and 4 (the two `AddAppliedPolicyCheckConstraint` migrations) in an
earlier development session before this rename work was designed. Squashing them into a new version 3
left that database's already-recorded version 4 reading as "up to date" under the new 3-migration
count, silently skipping the entire rename and leaving `Import_Batch` never created — which then broke
`Quotinator.Core`'s own migration 6 (#254), which expects `Import_Batch` to already exist. **Corrected
plan:** versions 3 and 4 are restored to their exact original content; the rename is a new version 5,
appended after them, with its own rename step for `System_ImportConflicts`/`System_ImportActions`
simplified to a plain `ALTER TABLE ... RENAME TO` (no rebuild, no CHECK-constraint work) since versions
3/4 already handle that before version 5 ever runs. Net: 5 Data migrations total (versions 1–5), one
more than the original plan, not one fewer than today's 4.

`Sql.Schema.GetUserTables` (`src/Quotinator.Data/Queries/Sql.cs:91-93`) is the Reset-exclusion pattern:

```csharp
internal const string GetUserTables =
    "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
    "AND name NOT LIKE 'System\\_%' ESCAPE '\\';";
```

Must be extended to also exclude `Import_`/`Audit_` — otherwise a post-rename Reset would drop
`Import_Batch`/`Import_Action`/`Import_Conflict`/`Import_SourceFileOverride`/`Audit_Entry`/
`Audit_Change`, which is exactly the protection this pattern exists to provide.

---

## Steps

### 1. Write the rename migration

**Status:** ✅ Done (corrected 2026-08-02 — new version 5, not a rewrite of versions 3/4)

Create `DomainPrefixRenameMigrations.cs` with the six `ALTER TABLE ... RENAME TO` statements and
`ImportBatches`' full reclassification (table creation moved from Core's baseline into this
migration). `System_ImportConflicts`/`System_ImportActions` are plain renames here, not rebuilds — the
restored, untouched versions 3/4 (`ImportConflictMigrations.AddAppliedPolicyCheckConstraint`,
`ImportActionMigrations.AddAppliedPolicyCheckConstraint`) already rebuild them with the CHECK
constraint applied before this migration runs. `DataOwnedMigrations` appends this as a new version 5,
after the untouched versions 3 and 4 — see "Current migration structure" above for why.

### 2. Update DataBaselineSql

**Status:** ✅ Done

Update the fresh-install baseline fragment in `DatabaseInitializer.cs` to create every table under its
final name directly (`Audit_Entry`, `Audit_Change`, `Import_Conflict`, `Import_Action`,
`Import_SourceFileOverride`, `Import_Batch`), per CLAUDE.md's baseline-drift rule. `ImportBatches`'
`CREATE TABLE` moves here from `Quotinator.Core`'s own baseline (coordinate with #254 — whichever PR
merges first should leave a visible TODO/cross-reference for the other, since both projects' baselines
must agree on where `Import_Batch` is created).

### 3. Rename entity classes and files

**Status:** ✅ Done

- `SystemAuditEntry` → `AuditEntryEntity` (`src/Quotinator.Data/Entities/SystemAuditEntry.cs` → `AuditEntryEntity.cs`)
- `SystemChangeLog` → `ChangeEntity` (`src/Quotinator.Data/Entities/SystemChangeLog.cs` → `ChangeEntity.cs`)
- `SystemImportAction` → `ImportActionEntity` (`src/Quotinator.Data/Entities/SystemImportAction.cs` → `ImportActionEntity.cs`)
- `SourceFileOverride` → `SourceFileOverrideEntity` (`src/Quotinator.Data/Entities/SourceFileOverride.cs` → `SourceFileOverrideEntity.cs`)
- `ImportBatch` → `ImportBatchEntity` (`src/Quotinator.Data/Entities/ImportBatch.cs` → `ImportBatchEntity.cs`)
- New `ImportConflictEntity` for `Import_Conflict` — today read via ad hoc query models, not a
  `[Table]`-attributed class; decide during implementation whether a full `RecordBase`-derived entity
  is warranted or a lighter DTO-style read model suffices (no repository currently needs write access).

Update every `[Table("...")]` attribute to the new table name. Rename dependent types that carry the
old name in their own identifier: `SystemChangeLogWriter`/`SystemChangeLogReader` →
`ChangeWriter`/`ChangeReader` (and their interfaces `ISystemChangeLogWriter`/`ISystemChangeLogReader` →
`IChangeWriter`/`IChangeReader`), `NoOpSourceFileOverrideRegistry` stays (registry name doesn't carry
the table name). Confirm the exact rename list against actual DI registrations at implementation time —
the compiler (CS0246/CS0234) finds every miss.

### 4. Update Quotinator.Data/Queries/Sql.cs

**Status:** ✅ Done

Update every hand-written query referencing the six renamed tables — `Sql.SystemChangeLog`'s nested
class (line 301 onward) and its sibling nested classes for the other five tables. The generic
repository layer (`RepositorySql`/`EntityColumnMetadata`) needs **no manual changes** — it reads table
names reflectively from each entity's `[Table]` attribute, so step 3 alone covers every
`SqliteRestorableRepository<T>`/`SqliteRepositoryBase<T>` call site.

### 5. Update GetUserTables Reset-exclusion pattern

**Status:** ✅ Done

Extend `Sql.Schema.GetUserTables` to exclude `Import_`/`Audit_` alongside `System_`:

```sql
AND name NOT LIKE 'System\_%' ESCAPE '\\'
AND name NOT LIKE 'Import\_%' ESCAPE '\\'
AND name NOT LIKE 'Audit\_%' ESCAPE '\\'
```

The existing tests proving this pattern are `DatabaseInitializerTests.GetUserTables_SystemPrefixedTable_IsExcluded`
and `GetUserTables_SystemPrefixWithoutUnderscore_IsNotExcluded` (`Quotinator.Core.Tests`). Add the two
new sibling cases named in the GitHub issue's Expected tests table:
`GetUserTables_ImportPrefixedTable_IsExcluded`, `GetUserTables_AuditPrefixedTable_IsExcluded`.

### 6. Update Data-side guard tests

**Status:** ✅ Done

Audit `SqlIdCaseGuard`, `SqlSelectPresentationGuard`, `SqlTextCaseGuard`, and any other reflection-based
guard test under `Quotinator.Data.Tests`/`Quotinator.Data.Testing.Tests` for a hardcoded old table or
class name that would silently stop being scanned once the rename lands (a guard that references
`"System_ChangeLog"` by string literal, for instance, would silently pass on a renamed-but-still-broken
query).

### 7. Full solution build, test, and Docker verification

**Status:** ✅ Done

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green
(2870/2870 across all projects), including the migration verification described in the checklist
below. T1 verified by the developer 2026-08-02, after the version 3/4/5 correction: clean migration
replay from their real local dev database (`Data v4 → v5, App v5 → v6`), full seed, working requests —
the exact scenario this correction exists for.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `DataBaselineSql` and the incremental migration path produce identical schemas | Unit test | `DatabaseInitializerOwnershipTests`'s `DataOwnedBaseline_And_IncrementalReplay_ProduceIdentical*Schema`/`...AcceptSame*CheckConstraintValues` tests, updated to the new table names; `DatabaseInitializerTests`'s `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`/`...AcceptSameCheckConstraintValues` cover `Import_Batch` on the Core side (see Scope changes) |
| 2 | ✅ | Every entity class renamed, `[Table]` attributes updated | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |
| 3 | ✅ | `Quotinator.Data/Queries/Sql.cs` updated, no hand-written query references an old table name | Live | `rg "System_AuditEntries|System_ChangeLog|System_ImportConflicts|System_ImportActions|System_SourceFileOverrides" src/Quotinator.Data/Queries/Sql.cs` returns nothing outside the deliberately-kept nested class names (see Scope changes) |
| 4 | ✅ | `GetUserTables` excludes `Import_`/`Audit_` alongside `System_`, Reset never drops `Import_Batch` (or any of the six renamed tables) | Unit test | `DatabaseInitializerTests.GetUserTables_SystemPrefixedTable_IsExcluded`/`...SystemPrefixWithoutUnderscore_IsNotExcluded`, plus every Reset-round-trip test in `DatabaseInitializerTests`/`ConflictResolutionTests` that exercises the real exclusion list end to end |
| 5 | ✅ | No guard test silently stopped scanning a renamed table/class | Unit test | `SqlQueryGuardTests`, `SqlIdCaseGuardTests`, `SqlTextCaseGuardTests`, `SqlSelectPresentationGuardTests` (`Quotinator.Data.Tests`) all pass with the renamed identifiers present in their own `DynamicData` enumerations |
| 6 | ✅ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false`: 0 warnings, 0 errors, 2870/2870 tests passed (re-verified 2026-08-02 after the version 3/4/5 correction) |
| 7 | ✅ | T1 verified | Live | Developer confirmed 2026-08-02, after the version 3/4/5 correction: clean startup log — `applying 1 pending Data migration(s) (version 4 → 5)... applying 1 pending App migration(s) (version 5 → 6)... schema updated (data v5, app v6)`, full seed (799 quotes etc.), `/health`/`/version`/`/masterdata/sources` all `200` |
| 8 | ✅ | T2 verified | Live | Re-verified 2026-08-02 against the corrected version 3/4/5 split: `docker build` succeeded; container started cleanly (`schema v6 (data v5)` — both counts up by one from the flawed intermediate version's `v5 (data v3)`), fresh seed of 799 quotes/461 sources; `/health` returns `200 {"status":"healthy"}`. Earlier focused pass (decide→apply, reverse, `/admin/audit`, `pageSize=0`) predates this correction and the seeding-safety-net/degraded-mode work from #254 — worth a follow-up focused re-pass, not required to close this item since the schema-level fix (the thing that was actually broken) is what's being verified here |

---

## Scope changes

**Full incremental migration-path verification against the last published release's schema (ADR 009)
is deliberately not this issue's own verification row.** This milestone's migrations, including the
squash this issue performs, are expected to be further consolidated before the milestone closes, so a
test snapshotting today's `v1.8.2` baseline would need rework at that point anyway. Per
`docs/workflow/process.md`'s "Closing a milestone" step 2, that verification is its own tracked issue
filed once, at milestone close, against whatever the last published release's schema actually is by
then (see #155 for the worked example) — deferred there, not skipped. An earlier draft of this plan doc
and its GitHub issue incorrectly included a `v1.8.2`-specific test in this issue's own scope; corrected
2026-08-01.

**Sequencing note for #254 (not a hard dependency):** #254's own schema declares
`REFERENCES ImportBatches(Id)` on `Quotes`/`Sources`/`Characters`/`People`. Those must reference
`Import_Batch` (the name this issue establishes) once both ship. `Quotinator.Data`'s migrations always
apply before `Quotinator.Core`'s in the same release regardless of which PR merged first, so this is
safe to build in either order — but both must land in the same release, and #254's baseline/migration
must be updated to match this issue's final name before that release ships.

**`Import_Batch` is created empty by `Quotinator.Data`'s own migration/baseline (matching ADR 015's
stated intent exactly), but the data migration and the FK-declaration fixup live in
`Quotinator.Core`'s migration 5, not Data's.** `DatabaseInitializer.ApplyMigrationsAsync` always runs
Data's entire migration phase to completion before Consumer's begins, with no interleaving possible —
`ImportBatches` (the pre-#253 name) is created by Consumer's own migration 3, so a same-table rename
attempted from Data's migration list would run before the table exists on a genuinely fresh incremental
replay (confirmed by a real, red `no such table: Import_Batch` failure across ~130 tests). Creating
`Import_Batch` empty in Data's migration sidesteps that ordering problem entirely — it succeeds
identically whether `ImportBatches` already exists (a real upgrade) or doesn't yet (a from-scratch
incremental replay). `Quotinator.Core`'s migration 6 then copies `ImportBatches`' data into it, drops
`ImportBatches`, and rebuilds the nine tables that FK-reference it (`Quotes`, `Sources`, `Characters`,
`People`, `Conversations`, `StageDirections`, `SoundCues`, `Universe`, `Series`) so their
`REFERENCES` clause points at the new table — SQLite only auto-converts a FOREIGN KEY declaration in
another table when the table it references is *renamed* (`ALTER TABLE ... RENAME TO`), never when it's
dropped and a differently-named replacement created (confirmed against sqlite.org), so without the
rebuild every future `INSERT`/`UPDATE` on any of the nine with a non-null `ImportBatchId` would throw
`no such table: ImportBatches` once migrations finish and FK enforcement is back on — confirmed with a
direct, throwaway repro against a real SQLite connection before writing the fix. See
`QuotinatorMigrations.Migration006_DomainPrefixRename`'s own remarks for the full reasoning, including
why the nine rebuilds don't in turn disturb anything that references *them* (their own final table name
never changes, only what their `ImportBatchId` column points at). This is migration 6, not 5 — an
earlier version of this rename work rewrote migration 5's content in place, discovered live during
#254's T1 pass to be unsafe even though migration 5 had never shipped in a tagged release, since it had
already run against a real (local dev) database; see #254's own plan doc for the correction.

**This issue's own rename is version 5, not the original squashed version 3 — the same class of bug as
the one above, found on this issue's own migrations while diagnosing it, and the actual root cause of
the "no such table: Import_Batch" failure the #254 correction above describes.** The original plan
folded versions 3+4 (`ImportConflictMigrations.AddAppliedPolicyCheckConstraint`,
`ImportActionMigrations.AddAppliedPolicyCheckConstraint`) into a new version 3 alongside the rename,
reasoning "neither had ever shipped." This project's own local dev database had already run both in an
earlier session, so the squash left that database's already-recorded version 4 reading as "up to date"
under the new 3-migration count — Data's own migration phase (which always runs to completion before
Consumer's phase starts) silently skipped the entire rename, `Import_Batch` was never created, and
Consumer's migration 6 then failed trying to insert into it. Corrected: versions 3 and 4 restored to
their exact original content (both still present, unchanged, in `ImportConflictMigrations.cs`/
`ImportActionMigrations.cs` — never deleted, just briefly unreferenced from `DataOwnedMigrations`
during the flawed intermediate version); the rename is a new version 5, appended after them. See
"Current migration structure" above and `DatabaseInitializer.DataOwnedMigrations`'s own remarks for the
full incident.

**`GetUserTables`'s Reset-exclusion pattern stays a blanket `Import_`/`Audit_`/`System_` prefix match,
exactly as this plan originally proposed — `Import_Batch` is protected from Reset, not dropped and
replayed.** An earlier draft of this section argued the opposite (that `Import_Batch` is domain content
Reset must drop, so only `Import_Conflict`/`Import_Action`/`Import_SourceFileOverride` should be
protected by exact name) — that was wrong. ADR 014 already distinguishes "provenance" (where a row's
content came from — `Import_Batch`) from "domain content" as two separate concepts, and
`DropAndRebuildAsync` never replays `Quotinator.Data`'s own migrations regardless of what Reset drops —
so a Reset that dropped `Import_Batch` would leave it permanently missing, never recreated, which is
exactly the `no such table: Import_Batch` failure a live Reset-round-trip test caught. `Import_Batch`
tolerates dangling references after a Reset the same way the four ADR-014 audit-trail tables already
do.
