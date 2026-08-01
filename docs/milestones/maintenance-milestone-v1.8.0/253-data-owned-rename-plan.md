# #253 — Rename Quotinator.Data-owned tables and entities

**Status:** Planning
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

Per the last shipped tag `v1.8.2` (2026-07-31): versions 1–2 are frozen; versions 3–4 have never
shipped and are safe to rewrite (#155's precedent). **Plan:** add a new file,
`DomainPrefixRenameMigrations.cs`, containing every `ALTER TABLE ... RENAME TO` /
`CREATE TABLE ... AS SELECT` needed for the six renames above plus `ImportBatches`' reclassification,
and fold today's versions 3+4 into it — replacing both, not appending after them. Net: 3 Data
migrations total (versions 1, 2, 3), one fewer than today's 4.

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

### 1. Write the squashed migration

**Status:** ⬜ Not started

Create `DomainPrefixRenameMigrations.cs` combining `ImportConflictMigrations.AddAppliedPolicyCheckConstraint`
+ `ImportActionMigrations.AddAppliedPolicyCheckConstraint` (today's versions 3+4, unreleased) with the
six `ALTER TABLE ... RENAME TO` statements and `ImportBatches`' full reclassification (table creation
moved from Core's baseline into this migration). Update `DataOwnedMigrations` to reference it as the
new version 3, replacing the old versions 3 and 4 entries.

### 2. Update DataBaselineSql

**Status:** ⬜ Not started

Update the fresh-install baseline fragment in `DatabaseInitializer.cs` to create every table under its
final name directly (`Audit_Entry`, `Audit_Change`, `Import_Conflict`, `Import_Action`,
`Import_SourceFileOverride`, `Import_Batch`), per CLAUDE.md's baseline-drift rule. `ImportBatches`'
`CREATE TABLE` moves here from `Quotinator.Core`'s own baseline (coordinate with #254 — whichever PR
merges first should leave a visible TODO/cross-reference for the other, since both projects' baselines
must agree on where `Import_Batch` is created).

### 3. Rename entity classes and files

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

Update every hand-written query referencing the six renamed tables — `Sql.SystemChangeLog`'s nested
class (line 301 onward) and its sibling nested classes for the other five tables. The generic
repository layer (`RepositorySql`/`EntityColumnMetadata`) needs **no manual changes** — it reads table
names reflectively from each entity's `[Table]` attribute, so step 3 alone covers every
`SqliteRestorableRepository<T>`/`SqliteRepositoryBase<T>` call site.

### 5. Update GetUserTables Reset-exclusion pattern

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

Audit `SqlIdCaseGuard`, `SqlSelectPresentationGuard`, `SqlTextCaseGuard`, and any other reflection-based
guard test under `Quotinator.Data.Tests`/`Quotinator.Data.Testing.Tests` for a hardcoded old table or
class name that would silently stop being scanned once the rename lands (a guard that references
`"System_ChangeLog"` by string literal, for instance, would silently pass on a renamed-but-still-broken
query).

### 7. Full solution build, test, and Docker verification

**Status:** ⬜ Not started

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Full test suite green,
including the migration verification described in the checklist below. T1 (developer starts app in
Visual Studio) + T2 (Docker smoke test).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `DataBaselineSql` and the incremental migration path produce identical schemas | Unit test | The existing `DataOwnedBaseline_And_IncrementalReplay_ProduceIdentical*Schema`/`...AcceptSame*CheckConstraintValues` tests in `DatabaseInitializerOwnershipTests`, renamed to match the new table names (not new tests — see the GitHub issue's Expected tests note) |
| 2 | ❌ | Every entity class renamed, `[Table]` attributes updated | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |
| 3 | ❌ | `Quotinator.Data/Queries/Sql.cs` updated, no hand-written query references an old table name | Live | `rg "System_AuditEntries|System_ChangeLog|System_ImportConflicts|System_ImportActions|System_SourceFileOverrides|ImportBatches\b" src/Quotinator.Data/Queries/Sql.cs` returns nothing |
| 4 | ❌ | `GetUserTables` excludes `Import_`/`Audit_` alongside `System_`, Reset still preserves all six renamed tables | Unit test | `DatabaseInitializerTests.GetUserTables_ImportPrefixedTable_IsExcluded`, `DatabaseInitializerTests.GetUserTables_AuditPrefixedTable_IsExcluded` |
| 5 | ❌ | No guard test silently stopped scanning a renamed table/class | Unit test | `SqlQueryGuardTests`/`RepositorySqlGuardTests`-equivalent suite in `Quotinator.Data.Tests` passes with the renamed identifiers present in its own `DynamicData` enumeration |
| 6 | ❌ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false` both 0 warnings, 0 errors, all green |
| 7 | ❌ | T1 verified | Live | Developer starts app in Visual Studio, confirms no startup error |
| 8 | ❌ | T2 verified | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeds; smoke-test commands touching audit/import-action/import-conflict/source-file-override/import-batch data return expected output |

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
