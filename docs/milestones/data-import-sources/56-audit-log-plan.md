# #56 — System_ChangeLog (originally "Audit log")

**Status:** Waiting for release

**Tiers required:** T1, T2

**GitHub issue:** #56

**Depends on:** #58

**Connects to:** #45, #55, #59, #16, #19, #15

---

## Spec requirements (reconciled — see Scope changes)

The original issue proposed a table named `AuditLog` with `ActorType`/`ActorId` columns and a
`created`/`updated`/`enriched`/`completed`/`verified_absent`/`imported` action vocabulary. An earlier
version of this plan doc had already drifted from that text with no documented reason (different column
name, different index, a different actor vocabulary, three actions the issue never mentions). Both the
issue and that earlier draft were superseded by a planning session with the user before any
implementation — see Scope changes for the full reasoning behind every change. Final shape:

1. `System_ChangeLog` table (Quotinator.Data-owned, `System_`-prefixed, survives Reset) —
   `Id` (UUID), `EntityType` (TEXT, free-text), `EntityId` (TEXT), `InitiatedByType` (TEXT, `CHECK`
   against the `InitiatorType` enum), `InitiatedById` (TEXT, nullable), `Action` (TEXT, `CHECK` against
   the `ChangeAction` enum), `Field`/`OldValue`/`NewValue` (TEXT, nullable), `OccurredAt` (TEXT ISO 8601 UTC)
2. Index `IX_System_ChangeLog_Entity` on `(EntityType, EntityId, OccurredAt DESC)`
3. `ChangeAction` enum (fixed, closed): `Created`, `Modified`, `SoftDelete`, `HardDelete` — describes
   only the kind of database operation, not who/what caused it or which business feature triggered it
4. `InitiatorType` enum (fixed, closed): `Seed`, `Import`, `WriteEndpoint`, `Enrichment` — the mechanism
   that wrote the row; the specific identifying detail (which seed batch, which import batch UUID, which
   HTTP route, which provider name) lives in `InitiatedById`
5. `ISystemChangeLogWriter.LogAsync(SystemChangeLog, ...)` / `ISystemChangeLogReader.GetHistoryAsync(entityType, entityId)`
6. `IInitiatorContext : ICallerContext` adds `InitiatedByType`/`InitiatedById` — built, registered, but
   not yet consumed ambiently by this issue's own wiring (see Scope changes)
7. `GET /api/v1/quotes/{id}/history` and equivalents — **deferred, see Scope changes**
8. Blazor UI history panel — deferred to v3 (unchanged from the original issue)

---

## Scope changes

Reconciled 2026-07-05 across several rounds with the user, before implementation — pending a comment on
#56 recording the same:

- **Renamed from `AuditLog`/`ActorType` to `System_ChangeLog`/`InitiatedByType` throughout.**
  `ActorType`/`ActorId` (the issue's original names) would collide with a plausible future `Actor` table
  in Quotinator's own domain (the performer who played a `Character`, distinct from `Person`, which today
  means "the real person who said/wrote a quote"). Renamed to `InitiatedByType`/`InitiatedById` to avoid
  that collision. Separately, `AuditLog` as a name would sit confusingly close to the already-shipped
  `System_AuditEntries` once both are Data-owned siblings — renamed to `System_ChangeLog` throughout
  (entity `SystemChangeLog`, interfaces `ISystemChangeLogWriter`/`ISystemChangeLogReader`, implementations
  `SystemChangeLogWriter`/`SystemChangeLogReader`).
- **`System_ChangeLog` is Quotinator.Data-owned and `System_`-prefixed, not Engine-owned as first
  planned.** I initially argued Engine-owned/wiped-on-Reset; the user corrected this. The reasoning that
  won: `System_AuditEntries.TableName` and `System_ImportConflicts.EntityType` are already free-text
  strings with no hardcoded enum of Quotinator's specific tables — that is exactly what makes them
  reusable for any current or future entity type without a schema change, and it is already how those two
  Data-owned tables work. `System_ChangeLog.EntityType`/`Action` follow the identical shape. In this
  codebase, "Data owns the migration" and "the table is `System_`-prefixed and survives Reset" are the
  same fact, not two independent choices — `System_ImportConflicts` already sets the precedent of rows
  surviving Reset even though the quotes they reference are wiped and reseeded with new IDs (see
  `ResetAsync_PreservesExistingImportConflictRows`'s own comment). `System_ChangeLog` follows the same
  rule for consistency, matching the shipped behaviour of its two siblings rather than inventing a new,
  inconsistent exception for only the newest table.
- **Filed as a separate follow-up issue, #151** (v1.8.0 maintenance milestone): should `System_AuditEntries`/
  `System_ImportConflicts`/`System_ChangeLog` actually purge rows that reference an entity a Reset just
  wiped, rather than blanket-surviving as all three do today? This is a critique of already-shipped
  behaviour on two existing tables, not something to silently resolve by making `System_ChangeLog` behave
  differently from its siblings — a cross-cutting architecture question spanning tables from multiple
  milestones, not scoped to Data Import & Sources specifically.
- **`Action` and `InitiatedByType` are real C# enums (`ChangeAction`, `InitiatorType`), not bare
  strings.** Reminder from the user: this codebase already stores enums as constrained `TEXT` — `Sources.Type`
  and `QuoteGenres.Genre` both have `CHECK` constraints listing their values, and `SafeValue<TEnum?>`/
  `SafeEnumHandler<TEnum>` is the established Dapper pattern for it. Both enums live in
  `Quotinator.Data/Models/` and are registered directly in the base `DatabaseConfiguration.Configure()`
  (not deferred to `QuotinatorDapperConfiguration.RegisterDomainHandlers()`), since "Created"/"Seed" are
  domain-agnostic vocabularies Data can define itself, unlike `EntityType`'s values. `EntityType` stays a
  plain `string` — its values (`quote`/`character`/`source`/`person`) genuinely are Quotinator-specific,
  and `Quotinator.Data` cannot depend on `Quotinator.Engine` to borrow an entity-type enum (backwards from
  the established dependency direction) — this matches `SystemImportConflict.EntityType`, already a plain
  string today for the identical reason.
- **Both enum columns get a `CHECK` constraint enumerating their values**, matching `Sources.Type`/
  `QuoteGenres.Genre`'s existing precedent. I initially said no `CHECK` constraint should exist at all;
  that was wrong — this codebase's actual convention is the opposite. Adding a fifth member later needs a
  migration to widen the constraint, same as `Migration004_ImportBatchTypeUserSeed` did for
  `ImportBatches.Type`. All four `InitiatorType` members are included in the constraint now (not just
  `Seed`/`Import`), even though `WriteEndpoint`/`Enrichment` have no writer yet, since the full vocabulary
  was decided in this issue and a near-immediate follow-up migration once #16/#19 land would be wasted
  churn.
- **`Action`/`InitiatedByType` describe only the kind of database operation, not the business feature
  that caused it.** The issue's original vocabulary (`created`/`updated`/`enriched`/`completed`/
  `verified_absent`/`imported`) mixed CRUD shape with business semantics. Final `ChangeAction` vocabulary
  is closed and generic: `Created`, `Modified`, `SoftDelete`, `HardDelete`. What the issue called
  `enriched` is just `Modified` + `InitiatedByType = Enrichment`; `completed`/`verified_absent` (#55) will
  be `Modified` + `InitiatedByType = WriteEndpoint` once #55's management UI exists — no dedicated action
  value needed for either.
- **Granularity: one row per entity per operation, not one row per changed field.** `Field` stays
  available on the schema (nullable) for a future genuinely single-field action, but a `Modified` row
  from conflict resolution logs `Field = null` with `OldValue`/`NewValue` as whole-record JSON snapshots
  — reusing `existingFields`/`incomingFields`/`resolved`, already computed for `System_ImportConflicts`
  logging. No new field-diffing logic was needed.
- **Every `UPDATE` that actually executes gets logged — a mistake I caught and corrected, not a scope
  choice.** I originally planned to skip logging for `newest-wins`/`merge-ours`/`merge-theirs` rewrites
  ("no write endpoint yet"). The user caught this: `Sql.Quotes.UpdateOnNewestWins` always executes for
  those three policies — the code never checks "would this change anything" first, so if it runs, a real
  change happened and it must be logged. `skip`/`review` correctly log nothing, because no `UPDATE`
  executes for them at all — "no log entry" only ever corresponds to "no write occurred," never to "the
  write doesn't count."
- **This issue wires real logging into the write paths that exist today** — startup seeding
  (`InitiatorType.Seed`) and the live import endpoint #45 (`InitiatorType.Import`) — rather than shipping
  the interface as inert, uncalled infrastructure. `IInitiatorContext`/`InitiatorContext` are built and
  registered in DI per the design, but this issue's actual seeding/import wiring passes
  `InitiatorType`/`InitiatedById` as explicit parameters through a small `QuoteSeedWriter.ChangeLogContext`
  (matching how `ISystemImportConflictWriter` is already threaded explicitly through this same file),
  rather than reading ambient state from `IInitiatorContext` — both call sites already know their own
  initiator identity directly. `IInitiatorContext` remains real, tested infrastructure for a future
  ambient-context use case (e.g. #16's write endpoints, mirroring how `ICallerContext.Agent` bridges the
  HTTP layer down to `System_AuditEntries` today), not dead code — it just isn't the mechanism this
  issue's own two call sites needed.
- **`GET /api/v1/quotes/{id}/history` (and the character/source/person equivalents) is deferred out of
  this issue.** Nothing in this milestone consumes it (the Blazor history panel is v3; #59's soft-reset
  doesn't need it to function) — unlike #45's import endpoint, which was the deliverable itself. Revisit
  when #59 or the v3 Blazor UI milestone actually needs to read this data back through the API.
- **Project placement**, following `SystemImportConflict`'s exact existing precedent (entity in
  `Quotinator.Data.Entities`, writer/reader in `Quotinator.Data.Repositories`, no separate Core DTO layer):
  - `ChangeAction`, `InitiatorType` → `Quotinator.Data.Models`
  - `SystemChangeLog` → `Quotinator.Data.Entities`
  - `ISystemChangeLogWriter`, `SystemChangeLogWriter`, `ISystemChangeLogReader`, `SystemChangeLogReader`,
    `IInitiatorContext`, `InitiatorContext` → `Quotinator.Data.Repositories`
  - `Sql.SystemChangeLog.SelectByEntity` → `Quotinator.Data/Queries/Sql.cs` (hand-written SELECT needs
    centralising per the String centralisation policy; the INSERT needs no entry since Dapper.Contrib
    generates it, same as `SystemImportConflict`'s own INSERT)
- **`SystemChangeLog` inherits `RecordBase`, and so do `SystemAuditEntry` and `SystemImportConflict`
  (retrofitted in the same session, not filed as a separate issue).** `docs/architecture-decisions/002-recordbase-on-all-tables.md`
  ("RecordBase applies to all tables without exception") predates `SystemAuditEntry`'s original
  implementation (#73) by a week but was never applied to it — nobody checked the ADR at the time.
  `SystemImportConflict` (#64) then copied `SystemAuditEntry`'s non-`RecordBase` shape without checking
  the ADR either, and this issue's own first draft did the same by copying `SystemImportConflict`. The
  user caught this by naming the ADR directly and required fixing all three entities in this issue, not
  just the new one. Consequences:
  - `SystemChangeLog`/`SystemImportConflict` (both unreleased) had their `Id` changed from an
    auto-increment `long` (`[Key]`) to a `RecordBase` `Guid` (`[ExplicitKey]`) by editing their
    `CREATE TABLE` migration constants directly — safe, since no real database has ever run them.
  - `SystemAuditEntry` already shipped in v1.7.2 with an `INTEGER AUTOINCREMENT` primary key. SQLite has
    no `ALTER TABLE` form for changing a column's type or PK behaviour, so this needed a genuinely new
    migration (`AuditMigrations.MigrateToRecordBase`, `DataOwnedMigrations` version 5): rebuild the table
    under a temporary name (same technique as `Migration004_ImportBatchTypeUserSeed`), generate a
    synthetic `Guid` per existing row (SQLite has no native UUID function, so one is assembled from
    `randomblob`/`hex`), backfill `DateCreated` from `PerformedAt`, drop the old table, rename, recreate
    indexes.
  - All three writers (`SystemAuditWriter`, `SystemImportConflictWriter`, `SystemChangeLogWriter`) keep
    extending `SqliteRepositoryBase<T>` directly rather than `SqliteRepository<T>` — that separation
    (avoiding infinite audit-of-audit recursion) is independent of whether `T` inherits `RecordBase`, and
    stays exactly as it was.
  - `RecordBase`'s `DateModified`/`DateDeleted`/`IsDeleted` are never meaningfully used on any of the
    three tables (rows are never modified or soft-deleted after being written), and `DateCreated`
    duplicates each table's own domain timestamp (`PerformedAt`/`DetectedAt`/`OccurredAt`). This
    redundancy is the ADR's own accepted trade-off — full `IRepository<T>`/`IRestorableRepository<T>`
    citizenship for every table, no exceptions, rather than optimising away a few unused columns on
    three specific tables.
  - Several existing tests hardcoded the old `long`/auto-increment shape (raw `CREATE TABLE
    System_AuditEntries` schemas inline in five `Quotinator.Data.Tests` repository test files; raw
    `INSERT`/`ORDER BY Id` statements assuming sequential integer IDs) and needed updating to the new
    `RecordBase` shape — see Step 9.
  - This incident is also the reason `CLAUDE.md` gained a new "Authoritative sources" section and
    `docs/workflow/process.md`'s cross-check step now lists `docs/architecture-decisions/` first, ahead
    of JSON schemas — so a governing ADR is checked before copying an existing entity's shape, not after.

---

## Steps

### 1. Enums
**Status:** ✅ Done

`ChangeAction` (`Created`, `Modified`, `SoftDelete`, `HardDelete`) and `InitiatorType` (`Seed`, `Import`,
`WriteEndpoint`, `Enrichment`) in `Quotinator.Data/Models/`, registered via
`RegisterEnumHandler<ChangeAction>()`/`RegisterEnumHandler<InitiatorType>()` directly in
`DatabaseConfiguration.Configure()`.

### 2. Schema migration
**Status:** ✅ Done

`ChangeLogMigrations.CreateChangeLogTable` (`Quotinator.Data/Database/ChangeLogMigrations.cs`), added to
`DatabaseInitializer.DataOwnedMigrations` as version 4 (after #64's `ImportConflictMigrations.CreateImportConflictsTable`).
`DataBaselineSql` updated in the same commit. Verified via
`DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangeLogSchema` and
`...AcceptSameChangeLogCheckConstraintValues` (the latter needed since `PRAGMA table_info` doesn't
capture `CHECK` constraint text, mirroring `Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues`'s
existing rationale for `ImportBatches.Type`).

### 3. Entity, writer, reader
**Status:** ✅ Done

`SystemChangeLog` (`Quotinator.Data/Entities/SystemChangeLog.cs`), `ISystemChangeLogWriter`/`SystemChangeLogWriter`
and `ISystemChangeLogReader`/`SystemChangeLogReader` (`Quotinator.Data/Repositories/`), `Sql.SystemChangeLog.SelectByEntity`
(`Quotinator.Data/Queries/Sql.cs`) — all following `SystemImportConflict`'s exact precedent.

### 4. Initiator context
**Status:** ✅ Done

`IInitiatorContext : ICallerContext` / `InitiatorContext` (`Quotinator.Data/Repositories/`), registered in
`Program.cs` as the single instance behind both `ICallerContext` and `IInitiatorContext` — existing
`SqliteRepository<T>`/`System_AuditEntries` consumers reading `ICallerContext.Agent` are unaffected.

### 5. Wire into startup seeding
**Status:** ✅ Done

`QuoteSeedWriter.ChangeLogContext` (a small record bundling `ISystemChangeLogWriter`/`InitiatorType`/
`InitiatedById`) threaded through `GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/
`GetOrCreatePersonAsync` (log `Created` on a genuinely new row) and through
`QuotinatorDatabaseInitializer.SeedIfEmptyInternalAsync`'s two Quote branches (`Created` on fresh insert,
`Modified` on a cross-file `newest-wins`/`merge-ours`/`merge-theirs` rewrite — `skip`/`review` write
nothing, since no `UPDATE` executes for them). `InitiatorType.Seed`, `InitiatedById = importBatch.Id`.

### 6. Wire into the live import endpoint
**Status:** ✅ Done

Same `ChangeLogContext` pattern in `SqliteQuoteImportService.ImportAsync`: `Created` on a new row,
`Modified` on a `newest-wins`/`merge-ours`/`merge-theirs` rewrite, nothing on `skip`/`review`.
`InitiatorType.Import`, `InitiatedById = batch.Id`.

### 7. Future wiring (explicitly deferred, not part of this issue)
**Status:** N/A — deferred

- `InitiatedByType = WriteEndpoint` — no consumer until #16 (write endpoints) exists
- `InitiatedByType = Enrichment` — no consumer until #19 (enrichment scaffold) exists
- `Action = SoftDelete`/`HardDelete` — no delete path exists anywhere yet (#16 unstarted); the enum
  members exist in the fixed vocabulary but nothing writes them in this issue
- History read endpoint(s) — deferred, no consumer in this milestone yet
- Blazor UI history panel — deferred to v3

### 8. Tests
**Status:** ✅ Done

- Schema/`CHECK`-constraint drift (`Quotinator.Data.Tests/Database/DatabaseInitializerOwnershipTests.cs`):
  `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangeLogSchema`,
  `...AcceptSameChangeLogCheckConstraintValues`
- `IInitiatorContext`/`InitiatorContext` `AsyncLocal` isolation
  (`Quotinator.Data.Tests/Repositories/InitiatorContextTests.cs`)
- Startup seeding: `ConflictResolutionTests.Seed_FreshQuote_WritesCreatedChangeLogRowsWithSeedInitiator`,
  `NewestWins_CrossFileDuplicate_WritesModifiedChangeLogRowForQuote`,
  `SkipOrReview_CrossFileDuplicate_WritesNoModifiedChangeLogRow`, `ResetAsync_PreservesExistingChangeLogRows`
- Live import: `QuoteImportServiceTests.ImportAsync_FreshDatabase_WritesCreatedChangeLogRowWithImportInitiator`,
  `ImportAsync_NewestWins_WritesModifiedChangeLogRowWithSameImportBatchId`,
  `ImportAsync_Skip_WritesNoModifiedChangeLogRow`, `ImportAsync_PreviewWithNewRow_NoChangeLogRowPersisted`

### 9. RecordBase retrofit (ADR 002 compliance — `SystemAuditEntry`, `SystemImportConflict`, `SystemChangeLog`)
**Status:** ✅ Done

- `SystemAuditEntry`/`SystemImportConflict`/`SystemChangeLog` all now extend `RecordBase`.
- `AuditMigrations.MigrateToRecordBase` (`DataOwnedMigrations` version 5) rebuilds the already-shipped
  `System_AuditEntries` table, preserving existing rows with a synthetic `Guid` per row and
  `DateCreated` backfilled from `PerformedAt`.
- `ChangeLogMigrations.CreateChangeLogTable` edited in place (never applied to any real database yet)
  to use a `Guid` `TEXT` primary key from the start.
- `ImportConflictMigrations.CreateImportConflictsTable` was **initially** edited in place the same way,
  on the mistaken belief that "unreleased" (no tagged version) meant "safe to edit." This was wrong —
  this migration had already applied to real local development databases (from earlier `#64` work,
  well before this issue started) — and T1 testing caught the resulting corruption live: `POST
  /api/v1/admin/database/reseed` threw `SqliteException: table System_ImportConflicts has no column
  named DateCreated`, because the in-place edit never actually re-ran against a database where
  migration 3 had already recorded as applied. Reverted to its original shipped shape; the retrofit is
  now its own separate migration 6 (`ImportConflictMigrations.MigrateToRecordBase`), using the exact
  same rebuild-and-rename technique as `AuditMigrations.MigrateToRecordBase`. See the T1 row below for
  how this was caught and re-verified.
- `DataBaselineSql` updated for all three tables to their final `RecordBase`-compliant shape.
- Five `Quotinator.Data.Tests` repository tests (`AggregateRepositoryTests`, `InsertManyAsyncTests`,
  `LinkRepositoryTests`, `OneToOneRepositoryTests`, `SystemAuditWriterTests`) had their inline
  `CREATE TABLE System_AuditEntries` fixtures updated to the new shape; one assertion
  (`SystemAuditWriterTests.ClearAsync_WithTable_...`) that relied on `ORDER BY Id` for insertion order
  was rewritten to look up rows by `TableName` instead, since `Id` is no longer a sequential integer.
- `DowngradeToLegacyNamesAsync()` (`Quotinator.Engine.Tests/Database/DatabaseInitializerTests.cs`) was
  rebuilding a "legacy" `AuditEntries` table via a bare rename, which — after this retrofit — would
  silently carry over migration 5's new columns instead of reproducing a genuine pre-migration-2 legacy
  table. Rewritten to fully rebuild the table under the original migration-1 shape before renaming.
- Verified via `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema` (proves
  the incremental replay of migrations 1→2→3→4→5 produces the same final schema as the baseline path) and
  `InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved` (proves a
  genuinely legacy-shaped row survives the full migration chain with its data intact — the direct proof
  that the shipped-table migration is safe for real upgrading installations).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `System_ChangeLog` table + index created, baseline matches incremental replay, `CHECK` constraints enforced identically on both paths | Unit test | `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangeLogSchema`, `...AcceptSameChangeLogCheckConstraintValues` |
| 2 | ✅ | `Created` entries logged during startup seeding with correct initiator | Unit test | `ConflictResolutionTests.Seed_FreshQuote_WritesCreatedChangeLogRowsWithSeedInitiator` |
| 3 | ✅ | `Modified` entries logged on cross-file `newest-wins`/`merge-ours`/`merge-theirs` during seeding; nothing on `skip`/`review` | Unit test | `NewestWins_CrossFileDuplicate_WritesModifiedChangeLogRowForQuote`, `SkipOrReview_CrossFileDuplicate_WritesNoModifiedChangeLogRow` |
| 4 | ✅ | `Created`/`Modified` entries logged during live import with correct initiator (batch UUID); nothing on `skip`/`review`; nothing persisted on preview | Unit test | `QuoteImportServiceTests.ImportAsync_FreshDatabase_WritesCreatedChangeLogRowWithImportInitiator`, `...NewestWins_WritesModifiedChangeLogRowWithSameImportBatchId`, `...Skip_WritesNoModifiedChangeLogRow`, `...PreviewWithNewRow_NoChangeLogRowPersisted` |
| 5 | ✅ | `System_ChangeLog` survives a full Reset (Data-owned, `System_`-prefixed) | Unit test | `ResetAsync_PreservesExistingChangeLogRows` |
| 6 | ✅ | `IInitiatorContext`/`InitiatorContext` isolate per async context | Unit test | `InitiatorContextTests.ConcurrentAsyncFlows_DoNotSeeEachOthersInitiatorValues` |
| 7 | N/A | `InitiatedByType = WriteEndpoint` actually written | N/A | Deferred to #16 |
| 8 | N/A | `InitiatedByType = Enrichment` actually written | N/A | Deferred to #19 |
| 9 | N/A | `SoftDelete`/`HardDelete` actions actually written | N/A | Deferred to #16 |
| 10 | N/A | History read endpoint(s) | N/A | Deferred — no consumer in this milestone yet |
| 11 | N/A | Blazor UI history panel | N/A | Deferred to v3 |
| 12 | ✅ | `SystemAuditEntry`/`SystemImportConflict`/`SystemChangeLog` all inherit `RecordBase`, per ADR 002 | Unit test | `DataOwnedBaseline_And_IncrementalReplay_ProduceIdentical{SystemAuditEntries,SystemImportConflicts,SystemChangeLog}Schema` |
| 13 | ✅ | The already-shipped `System_AuditEntries` table migrates safely to the new shape, preserving existing rows | Unit test | `InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved` |
| 14 | ✅ | The already-applied `System_ImportConflicts` table (migration 3, applied to real local dev databases before this issue) migrates safely to the new shape via a separate migration 6, rather than an in-place edit | Unit test + Live | `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema`; live-reproduced against a copy of the real dev database that had migration 3 already applied in its original shape — `POST /api/v1/admin/database/reseed` (which writes `System_ImportConflicts` rows on duplicate detection) returned 200 with 45 conflicts logged, no exception, both via direct CLI run and via Docker (container log showed the true incremental path: `migration applied: Data v5 → v6`, matching the real bug scenario, not the fresh-baseline path) |
| 15 | ✅ | T1 — app starts in VS without error; migration applies cleanly | Live | First attempt caught the `System_ImportConflicts` bug above (see item 14) via an unhandled-exception dialog that made Visual Studio appear frozen. Re-verified live in Visual Studio against the real dev database after the fix: log shows `applying 1 pending Data migration(s) (version 5 → 6)...` → `schema updated (data v6, app v6)`, followed by real usage — `GET /admin/database/seed/preview`, `GET /admin/audit`, `POST /quotes/import/preview` — all returning 200 with no errors |
| 16 | ✅ | T2 — Docker smoke test | Live | `docker build` succeeded. Run against a bind-mounted copy of the real (pre-fix-state) dev database from **PowerShell**, per `docs/docker.md`'s documented Git-Bash-mangles-`/data`-paths caveat (an initial attempt from Git Bash silently substituted a different path and produced a misleadingly "clean" fresh-baseline run instead of exercising the real migration — corrected by rerunning from PowerShell). Confirmed via `docker exec ls /data` that the container saw the actual host file (byte-identical), then `migration applied: Data v5 → v6` in the log, then `/api/v1/health` and `POST /admin/database/reseed` both returned 200 (45 conflicts logged) with zero errors in container logs. `docker cp`'d the resulting DB out and confirmed 101 `System_ImportConflicts` rows with correct `Id`/`DateCreated`/`IsDeleted` values via `Quotinator.Tools.DbInspector` |
