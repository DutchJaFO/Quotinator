# #309 — Move changelog content to database-backed System_Changelog table

**Status:** In progress (step 8)
**GitHub issue:** #309 (open)
**Depends on:** #80 (done, released — Changelog handling milestone)

---

## Background

Implements [ADR 005](../architecture-decisions/005-quotinator-changelog-project-scope.md)'s revision
and [ADR 018](../architecture-decisions/018-system-content-in-quotinator-data.md)'s file-authored
system content pattern. Changelog content currently lives entirely in
`src/Quotinator.Api/resources/changelog.*.json`, compiled into the publish output — updating it
requires a code commit and a redeploy. This issue moves changelog content to a database-backed,
queryable store, refreshed at startup from the same JSON files relocated to a runtime-accessible
location, while keeping the existing JSON-based read path alive as a fallback.

**Revision (2026-08-13) — master/detail, not a JSON blob.** The original design in this section stored
each release as one row with its rich content (`highlights`, `added`, `changed`, `fixed`, `removed`,
`audienceHighlights`, `issues`, `cves`) serialized into a single JSON blob column. That was picked
without checking whether this project's own master/detail infrastructure (`AggregateRepository<TParent,
TChild>`, built by #75 specifically for "parent table with a child collection written atomically") fit
better — it does. A JSON blob makes every update a full-row rewrite and is opaque to SQL; a child table
makes individual entries directly insertable/updatable/removable and directly queryable (e.g. #81's
"notification" highlights become a real `WHERE Kind = 'AudienceHighlight' AND AudienceKey =
'notification'`, not app-level filtering after deserializing a blob). See the Design section below.

**Revision (2026-08-13) — separate database, not a table in `quotinatordata.db`.** Per ADR 018's own
revision: system-level content defaults to the main database *unless* it has no transactional coupling
and no relational link to domain data — changelog content has neither (never joined against
quote/source/character tables, never written as part of an import/seeding transaction), so it moves to
its own, separate SQLite database instead of a `System_`-prefixed table in the main one. This keeps the
main database free of content outside its own domain and lets the two be updated independently. Since
changelog content is 100% regenerated from the JSON files at every startup — nothing ever writes to it
directly, no user content, no admin endpoints — the default *storage mode* is an **in-memory** SQLite
database, recreated fresh every boot. A persistent-file variant may be worth adding later if
re-importing on every startup turns out to be measurably costly — the storage mode is a configuration
point (connection string), not hardcoded into the schema, importer, or reader, so switching later is a
wiring change, not a redesign. Not built now (YAGNI).

**Revision (2026-08-13) — full migration capability, not none.** The original design in this section
skipped migration/versioning infrastructure entirely, reasoning that a database recreated fresh every
boot has nothing to migrate *from*. Per ADR 018's own correction: every database gets the same migration
capability without exception — a content type having no *current* reason to change its schema doesn't
mean it never will, and "separate database" is a placement decision, never a reason to skip building the
ability to evolve safely. This issue's database gets the same baseline+incremental migration machinery,
schema-drift parity tests, and ADR 009 verification as the main database, following
`Quotinator.Data.Database.DatabaseInitializer`'s existing baseline-vs-incremental *pattern* — pointed at
the changelog's own keyed connection factory instead of the main database's.
**Operational default stays simple in practice**: since the in-memory storage mode always starts
genuinely empty, the same "completely empty database → apply the one-step baseline" rule (already how
the main database bootstraps a fresh install) applies naturally here too, every single boot — the
incremental-migration path exists, is tested, and is ready for whenever the schema actually needs to
change (during development, or once a persistent-file variant means real on-disk state can carry across
restarts), without ever needing to be exercised by the default in-memory mode's own steady-state
behaviour.

**Correction (2026-08-14, found during Step 5 implementation) — a new, independent
`ChangelogDatabaseInitializer` class, not a second instance of `DatabaseInitializer`.** The paragraph
above (and the "Separate, in-memory database" design subsection below) originally assumed
`DatabaseInitializer` itself could be instantiated a second time against the changelog's keyed
connection factory. Reading `DatabaseInitializer.cs` in full during implementation showed this doesn't
work: the main database's own migration list (`DataOwnedMigrations`) and baseline SQL are hardcoded
`private static` fields, not constructor parameters, and the class carries main-database-specific
concerns (legacy filename migration, backup budget, the full `IDatabaseInitializer` interface surface).
A second instance would attempt to create `Import_Batch`/`Audit_Entry`/`System_Notification`/etc. in the
changelog database too. `ChangelogDatabaseInitializer` (`Quotinator.Data.Database`) is instead a small,
independent class that follows the identical baseline-vs-incremental *pattern* (empty database → one-step
baseline; otherwise replay pending migrations against its own `ChangelogSchemaVersion` table) without
subclassing or reusing `DatabaseInitializer` itself.

**Scope addition (2026-08-13) — About-page fallback.** `/about` is already on
`DatabaseHealthGateMiddleware`'s exempt-path list — expected to stay reachable during a degraded main
database. #293 already fixed the identical failure mode for `System_Notification` (a missing table
crashing a page that was supposed to degrade gracefully). This issue's changelog database is separate
from the one #293's fix concerns, but the same failure shape applies if the changelog import itself
fails (malformed JSON, disk read error) and its tables never get created. The fallback reuses #293's own
narrow-exception-catch idiom and logs a warning when it triggers, but deliberately does not build a
user-visible warning UI — that's #305's job (general DB integrity detection, v1.9.0), not duplicated
here.

## Authoritative-source cross-check

- **ADR 018** (including its 2026-08-13 revision) — file-authored system content is `Quotinator.Data`-
  owned; lives in a separate database when it has no domain coupling (this issue's case); the generic
  importer abstraction is this issue's own to design, built concretely first, not over-generalized
  against a single consumer.
- **ADR 005's revision** — the JSON files stay the authored source; they relocate to a
  runtime-accessible location; `Quotinator.Changelog` itself keeps zero direct database access — a
  separate `Quotinator.Data`-owned component does the reading/writing.
- **`docs/data-access.md`** — `AggregateRepository<TParent, TChild>` (#75) is the existing, tested
  parent/child-atomic-write pattern; `JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` (ADR 017)
  is mandatory for any multi-table read, "even without an immediate capability gain."
- **ADR 015** — `System_` domain prefix (operational/system content).
- **ADR 002 / ADR 008** — `RecordBase` without exception; the `Kind` discriminator column on the child
  table is a closed enum, so it gets a matching `CHECK` constraint from creation.
- **`data/sources/` precedent** — bundled quote sources are read from
  `AppContext.BaseDirectory/data/sources/` (`Program.cs`), loose files copied into the publish output,
  not compiled resources. Mirrored for the relocated changelog JSON files.
- **#293's fallback precedent** — `NotificationReader.GetActiveNotificationsAsync`/`GetPagedAsync` wrap
  their query in `try/catch (SqliteException ex) when (IsMissingTableError(ex))`, matching both
  `SqliteErrorCode == 1` and the specific table name — narrow enough that a genuinely different SQL
  error still propagates. This issue's reader follows the identical idiom.
- **`IDbConnectionFactory`** (`Quotinator.Data.Connections`) is currently registered as one unkeyed
  singleton (`Program.cs`). A second, keyed registration for the changelog database is this project's
  first use of .NET's keyed-DI services (`AddKeyedSingleton`/`[FromKeyedServices]`) — no existing
  precedent to follow, confirmed by reading the current registration.

No conflict found — proceeding with the design below.

---

## Design

### Schema — master/detail, `Kind`-discriminated child table

One parent row per `(Language, Version)` — `Version IS NULL` represents that language's `unreleased`
entry. `AggregateRepository<TParent, TChild>` is generic over exactly one child type, so every
list-shaped field (`highlights`, `added`, `changed`, `fixed`, `removed`, `issues`, `cves`,
`audienceHighlights`) is modeled as rows in **one** child table, discriminated by `Kind`:

```
CREATE TABLE IF NOT EXISTS Changelog (
    Id                TEXT NOT NULL PRIMARY KEY,
    Language          TEXT NOT NULL,
    Version           TEXT,                 -- NULL = that language's 'unreleased' entry
    Date              TEXT,                 -- NULL for 'unreleased'
    MachineTranslated INTEGER NOT NULL DEFAULT 0,  -- repeated per row for one Language; added during Step 6, see its notes
    QuoteText         TEXT,
    QuoteAttribution  TEXT,
    DateCreated       TEXT NOT NULL,
    DateModified      TEXT,
    DateDeleted       TEXT,
    IsDeleted         INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Changelog_Language_Version
    ON Changelog (Language, Version) WHERE Version IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UX_Changelog_Language_Unreleased
    ON Changelog (Language) WHERE Version IS NULL;

CREATE TABLE IF NOT EXISTS ChangelogLine (
    Id           TEXT NOT NULL PRIMARY KEY,
    ChangelogId  TEXT NOT NULL REFERENCES Changelog(Id),
    Kind         TEXT NOT NULL
                 CHECK (Kind IN ('Highlight','Added','Changed','Fixed','Removed','Issue','Cve','AudienceHighlight')),
    AudienceKey  TEXT,                     -- only non-null when Kind = 'AudienceHighlight'
    Value        TEXT NOT NULL,
    SortOrder    INTEGER NOT NULL,
    DateCreated  TEXT NOT NULL,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_ChangelogLine_ChangelogId ON ChangelogLine (ChangelogId);
```

No `System_` prefix on these two tables — ADR 015's domain-prefix convention exists to disambiguate
tables sharing one database (SQLite has no schema qualification); a dedicated, single-purpose database
has nothing to disambiguate from, so the prefix would be pure noise. `Issues` (ints) and `Cves`/
everything else (strings) both store as `Value TEXT` — a minor, acceptable modeling compromise, parsed
back to `int` only for the `Issue` kind when reassembling a `ChangelogUnreleased`/`ChangelogRelease`.

`SortOrder` preserves each list's original order, since child rows otherwise have none. `AudienceKey`
is exactly where #307's `ChangelogReservedAudience`/`"notification"` convention plugs in — a
`ChangelogLine` row with `Kind = AudienceHighlight, AudienceKey = "notification"` is what
`GetHighlightsFor(ChangelogReservedAudience.Notification)` ultimately reads once reassembled.

### Separate, in-memory database

New `Quotinator.Data.Connections.SqliteInMemoryConnectionFactory` (or similar), using SQLite's
shared-cache in-memory mode (`Data Source=file:quotinatorchangelog?mode=memory&cache=shared`) so that
multiple connections created over the app's lifetime all see the same in-memory database — a bare
`:memory:` connection string is *not* shared across separate connections, so this specific mode is
required, not optional. A dedicated singleton **keep-alive connection**, opened at startup and disposed
at shutdown, holds the shared-cache database alive for the app's lifetime (a shared-cache in-memory
database is destroyed the moment its last open connection closes).

Registered via keyed DI: `builder.Services.AddKeyedSingleton<IDbConnectionFactory>("changelog", ...)` —
this project's first use of keyed services, since every existing consumer uses the single unkeyed
`IDbConnectionFactory` registration for the main database. The changelog repository/reader classes take
this keyed factory via `[FromKeyedServices("changelog")]` constructor injection.

**Full migration capability, per ADR 018's correction — not a bare `CREATE TABLE IF NOT EXISTS`.** This
database gets its own baseline+incremental migration machinery (own migration list starting at version
1, own baseline SQL, own schema-version tracking table, own schema-drift parity test), via a small,
independent `ChangelogDatabaseInitializer` class following `DatabaseInitializer`'s baseline-vs-incremental
*pattern* rather than a second instance of that class itself (see the "not a second instance of
`DatabaseInitializer`" correction above) — pointed at the keyed changelog connection factory instead of
the main database's. The same "completely empty database → apply the one-step baseline" rule means the
in-memory storage mode's steady-state behaviour is still just "run the baseline, every boot" in practice
(an in-memory database is always empty at startup by construction) — the incremental path is exercised
only when the schema actually needs to change (during development) or a future persistent-file variant
carries real on-disk state across restarts, but it exists and is tested either way, not retrofitted
later.

**Config point for a future persistent variant (not built now):** the connection string is read from
one place (e.g. `Quotinator:ChangelogStorageConnectionString`, defaulting to the shared-cache in-memory
string above) — swapping in a real file path later is a configuration change, not a schema/importer/
reader/migration redesign, addressing the "re-importing every startup might be wasteful" concern without
building unused code now.

### Changelog system-content importer (concrete, not over-generalized)

A single `Quotinator.Data.Import.ChangelogSystemContentImporter` reads every `changelog.*.json` file via
the existing `IChangelogService`, flattens each release's/`unreleased`'s list fields into `ChangelogLine`
rows (tagged by `Kind`, `AudienceKey` where applicable, `SortOrder` = original list index), and writes
via `AggregateRepository<Changelog, ChangelogLine>.InsertAsync`. Run once at startup, after the
keep-alive connection opens and the tables are created. Per ADR 018, this is deliberately *not* built as
a generic "system content importer" interface yet — this issue is the pattern's first real consumer; a
shared abstraction is extracted only once a second consumer (Genre) actually needs one.

### `IChangelogReader` — DB-first, JSON-fallback, one place owns both

New `Quotinator.Data.Repositories.IChangelogReader`/`ChangelogReader`, constructor-injected with the
existing `IChangelogService` (as its fallback) plus a `JoinQueryRepository<ChangelogLineRow>` (a flat
LEFT JOIN of `Changelog`/`ChangelogLine`, per ADR 017 — never a hand-rolled query) built against the
keyed changelog connection factory:

```
public async Task<ChangelogDocument?> GetDocumentAsync(string? language)
{
    try
    {
        var rows = await _joinRepo.QueryAsync(new { language });
        return AssembleDocument(rows);   // groups flat rows by ChangelogId/Kind/AudienceKey back into
                                          // ChangelogRelease/ChangelogUnreleased objects
    }
    catch (SqliteException ex) when (IsMissingChangelogTableError(ex))
    {
        _logger.LogChangelogTableMissingFallingBackToFile(ex);   // WARNING, not silent
        return _changelogService.GetForCulture(language);
    }
}
```

Both `About.razor` and #81's future producer read through `IChangelogReader`, not `IChangelogService`
directly. This is a change to #81's plan doc (update after this issue lands, not now).

**Explicitly out of scope:** a user-visible warning banner/notification when the fallback triggers —
#305's job, not duplicated here.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

### 2. `DataPaths.ChangelogFolder`, relocate JSON files, update `Program.cs`/`.csproj`
**Status:** ✅ Done

`changelog.{en,nl,de}.json` moved to `data/changelog/` (git-tracked rename, not copy). New
`DataPaths.ChangelogFolder` constant; `Quotinator.Api.csproj` gets a `data/changelog/**/*` content-copy
rule mirroring `data/sources/`'s own; `Program.cs`'s `IChangelogService` registration points at
`AppContext.BaseDirectory/data/changelog`. Updated everything that referenced the old
`src/Quotinator.Api/resources/changelog.*.json` path: `RepositoryStructureTests` (fixed + extended with
the same disk↔slnx bidirectional check `data/sources/` already has), `ChangelogSchemaTests`'
file-discovery helper, `CLAUDE.md`, `docs/workflow/release.md`, `docs/ci-cd.md`,
`docs/workflow/issue-closure.md`, `scripts/changelog.csx`'s usage comment, and `.github/workflows/
_build-test.yml`'s publish-output assertion (added a `data/changelog/` check mirroring the existing
`data/sources/` one). Left `scripts/README.md` and the historical `changelog-upgrade.csx`-related
mentions untouched — those describe the unrelated, already-dead `changelog.json` migration artifact
that stays in `src/Quotinator.Api/resources/`, never matched by `ChangelogService`'s own
`changelog.*.json` glob.

Verified: `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s).
`Quotinator.Changelog.Tests` (41), `Quotinator.Api.Tests` (673, including the 3 new/updated
`RepositoryStructureTests`) — all green.

### 3. `Quotinator.Data` → `Quotinator.Changelog` project reference
**Status:** ✅ Done

Added to `Quotinator.Data.csproj`. Confirmed no circular dependency — `Quotinator.Changelog` itself
only references `Quotinator.Logging`, never `Quotinator.Data` (per ADR 005's dependency-isolation
invariant, unchanged). Build clean, 0 warnings/0 errors.

### 4. Separate in-memory database: keep-alive connection, keyed `IDbConnectionFactory`
**Status:** ✅ Done

New `Quotinator.Data.Connections.DatabaseConnectionKeys.Changelog` (the keyed-DI service key, not a
bare string literal at call sites) and `ChangelogConnectionKeepAlive` (opens and holds one connection
for the app's lifetime, disposed at shutdown). `Program.cs` registers `SqliteConnectionFactory` under
this key with the shared-cache in-memory connection string
(`file:quotinatorchangelog?mode=memory&cache=shared`, `useMemoryTempStore: true` for the same #294
reason as the main database), and eagerly resolves `ChangelogConnectionKeepAlive` right after
`app.StartAsync()`, wrapped in its own non-fatal try/catch separate from the main database's own —
a changelog-database failure must never affect the main database's health status.

**Found while testing:** `Microsoft.Data.Sqlite` pools connections by default — disposing the
keep-alive connection alone did *not* immediately destroy the shared-cache database in a first test
attempt, because the connection pool kept a dormant native connection open underneath. Confirmed via
`SqliteConnection.ClearAllPools()` that the keep-alive is still genuinely load-bearing once pooling is
actually bypassed (matching real conditions — pooled connections do get reclaimed under memory
pressure) — not merely defensive against a scenario pooling already prevented on its own. Both
directions are now covered by `ChangelogConnectionKeepAliveTests` (`Quotinator.Data.Tests`).

Verified: build 0 warnings/0 errors; full solution test suite green (Data.Tests 1081, Core.Tests 1462,
Api.Tests 673, Changelog.Tests 41).

### 5. `ChangelogDatabaseInitializer` (own class, migration 1 + baseline SQL + schema-drift parity test), `ChangelogEntity`/`ChangelogLineEntity`, `ChangelogRepository : AggregateRepository<Changelog, ChangelogLine>`
**Status:** ✅ Done

`ChangelogLineKind` (`Quotinator.Data.Enums`, 8 members: `Highlight`, `Added`, `Changed`, `Fixed`,
`Removed`, `Issue`, `Cve`, `AudienceHighlight`), `ChangelogEntity`/`ChangelogLineEntity`
(`Quotinator.Data.Entities`, both `RecordBase`-derived per ADR 002) built as designed above.
`DatabaseConfiguration.Configure()` registers `RegisterEnumHandler<ChangelogLineKind>()` alongside the
other closed-set enum handlers.

`ChangelogDatabaseInitializer` (see the Design section's correction above) is a small, independent class
in `Quotinator.Data.Database` — not a second `DatabaseInitializer` instance — following the same
baseline-vs-incremental pattern against its own `ChangelogSchemaVersion` table. `NullAuditEntryWriter`
(`Quotinator.Data.Repositories`) is a production null-object `IAuditEntryWriter` for `ChangelogRepository`
— the changelog database structurally has no `Audit_Entry` table, and `SqliteRepository<T>` writes its
audit entry using the *same* connection/transaction it was given, so the real writer would attempt
`INSERT INTO Audit_Entry` against a database with no such table. `ChangelogRepository :
AggregateRepository<ChangelogEntity, ChangelogLineEntity>` follows #75's existing master/detail pattern
exactly (`InsertWithLinesAsync`, `GetChildren`/`ChildRepository` overrides).

Wired into `Program.cs`: `ChangelogDatabaseInitializer`/`ChangelogRepository` registered as DI singletons;
`ChangelogDatabaseInitializer.InitialiseAsync()` is called right after `ChangelogConnectionKeepAlive` is
eagerly resolved (post-`app.StartAsync()`), in the same non-fatal try/catch — a changelog-database
initialisation failure logs a warning and lets startup continue, matching ADR 018's fallback requirement
(the future `IChangelogReader` falls back to `IChangelogService` regardless of *why* the changelog
database is unavailable).

**Two naming collisions found and fixed during this step, both now documented in `<remarks>` on the
classes involved:**
- **Filesystem case-insensitivity:** `ChangelogMigrations.cs` was first written as the migration-SQL
  class's filename, which — on this Windows (case-insensitive) filesystem — resolved to the *same file*
  as the pre-existing, unrelated `ChangeLogMigrations.cs` (`System_ChangeLog`, the audit-trail
  field-change-tracking table, #56) and silently overwrote it. Caught by a build error
  (`CS0103: 'ChangeLogMigrations' does not exist`). Recovered via `git restore`, then rewritten under the
  genuinely distinct name `ChangelogContentMigrations.cs` / class `ChangelogContentMigrations`.
- **Namespace ambiguity, not filesystem:** `NoOpAuditEntryWriter.cs` (this step's original filename)
  shared its simple class name with the pre-existing, test-only
  `Quotinator.Data.Testing.NoOps.NoOpAuditEntryWriter`, producing 30+ `CS0104` ambiguous-reference errors
  across test files that import both namespaces. Renamed to `NullAuditEntryWriter.cs` / class
  `NullAuditEntryWriter` before ever being committed.

**SQL string-centralisation policy applied, not bypassed.** `ChangelogDatabaseInitializer`'s own
version-bookkeeping SQL (empty-database detection, current-version lookup, version-row insert) was
initially written as inline string literals in the class itself. `SqlSourceScanTests
.AllSqlStringLiterals_AreInCentralisedFiles` (`Quotinator.Core.Tests`) caught this — every DML literal
outside `Sql.cs`/`RepositorySql.cs`/`QuotinatorMigrations.cs` is a violation. Moved to a new
`Sql.ChangelogSchema` nested class in `Quotinator.Data.Queries.Sql` (mirroring `Sql.Schema`'s existing
version-bookkeeping constants for the main database), and `SqlQueryGuardTests
.AggregateQueries_MatchDocumentedInventory` / `SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries`
were updated to document the two new aggregate-function constants (`GetCurrentVersion`'s
`COALESCE(MAX(...))`, `AnyTableExists`'s `COUNT(*)`) and the new `ChangelogSchema` nested-class name —
both guard tests exist specifically to catch this class of gap, and did.

`ChangelogDatabaseInitializerTests` (`Quotinator.Data.Tests`) — `Baseline_And_IncrementalReplay_
ProduceIdenticalSchema` (parity for both `Changelog`/`ChangelogLine`), `Baseline_And_IncrementalReplay_
AcceptSameKindCheckConstraintValues` (CHECK-constraint behavioural round-trip, matching the main
database's own precedent for constraint values `PRAGMA table_info` can't capture structurally), and
`EmptyDatabase_AppliesBaseline`. Each test opens its own `ChangelogConnectionKeepAlive` against a unique
GUID-named shared-cache connection string (mirroring `ChangelogConnectionKeepAliveTests`'s own pattern) —
required because the initializer opens-and-disposes its own connection per call, which would destroy an
unheld shared-cache database between the initialise call and the later schema-inspection connection.

Verified: full solution `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Full solution
`dotnet test --configuration Release` — all projects green (Data.Tests 1100, Core.Tests 1462, Api.Tests
673, Changelog.Tests 41, plus the smaller supporting test projects).

### 6. `ChangelogSystemContentImporter`
**Status:** ✅ Done

`ChangelogSystemContentImporter` (`Quotinator.Data.Import`) reads every loaded language via
`IChangelogService.AvailableLanguages`/`GetForCulture`, clears existing `ChangelogLine`/`Changelog`
content first (child before parent — FK enforcement is off by default on this project's connections, so
deleting the parent first would silently orphan children rather than fail loudly), then writes one
`ChangelogEntity` per release plus, where present, one per language's `unreleased` entry, each via
`ChangelogRepository.InsertWithLinesAsync`. `SortOrder` restarts at 0 for every `(Kind, AudienceKey)`
list, preserving each source list's own original order rather than a single global write-order counter.
`Issues` (`List<int>`) are stored as their string form, parsed back to `int` only when the reader (Step
7) reassembles a `ChangelogUnreleased`/`ChangelogRelease`.

**Schema gap found and fixed in the same step:** the Step 5 schema omitted `ChangelogDocument
.MachineTranslated` — actively rendered by `About.razor` (`@if (_document.MachineTranslated)`) to show a
disclaimer, unlike `SectionHeaders`, which is populated by `ChangelogService` but read by no consumer
anywhere in the codebase and was deliberately left out (YAGNI, add later per real need). Added
`MachineTranslated INTEGER NOT NULL DEFAULT 0` to `ChangelogContentMigrations.CreateChangelogTables` and
a matching property to `ChangelogEntity` — edited migration 1 directly rather than adding migration 2,
since migration 1 has never shipped to any real database (feature branch, unreleased issue), matching
this project's own precedent for a pre-release schema gap (see `System_ChangeLog`'s own history in
`ChangeLogMigrations.cs`). Since every row for one language repeats the same document-level
`MachineTranslated` value (this schema has no separate one-row-per-language document table), storing it
on `Changelog` is a deliberate denormalisation, the same one already applied to `Language` itself.

The importer's own bulk-clear SQL (`DELETE FROM ChangelogLine;`/`DELETE FROM Changelog;`) was moved into
a new `Sql.ChangelogContent` nested class (`Quotinator.Data.Queries.Sql`) from the start, per the SQL
string-centralisation policy already applied in Step 5 — `SqlBoundaryTests
.Sql_ContainsOnlyGenericInfrastructureQueries` was updated to include the new nested class name.

**Startup-latency regression found and fixed before commit:** wiring `ChangelogSystemContentImporter
.RefreshAsync()` into `Program.cs` as a third `await` inside the same try/catch as
`ChangelogDatabaseInitializer.InitialiseAsync()` (right after `app.StartAsync()`) pushed
`StartupPhaseState.MarkComplete()` — which runs sequentially after all startup work, including this
block — meaningfully later, which broke `ProgramNotificationSeedingRegressionTests
.Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable` (`Quotinator.Api.Tests`):
the test's request now arrived before `MarkComplete()` ran, so `/api/v1/health` reported `"starting"`
(503) instead of the expected `"healthy"` (200) — a real, deterministic regression (reproduced in
isolation), not test flakiness. Fixed by detaching the content-refresh call into its own
`Task.Run(...)`-backed background task (its own internal try/catch, same warning-log-and-continue
behaviour), leaving only `ChangelogConnectionKeepAlive`/`ChangelogDatabaseInitializer.InitialiseAsync()`
(fast — one connection, a couple of DDL statements) on the synchronous startup path. This is consistent
with, not a workaround around, ADR 018's fallback design: `IChangelogReader` (Step 7) already tolerates
the changelog database not being ready by falling back to the JSON-backed `IChangelogService` — the same
path it uses for a genuine failure, so a request racing the background import simply exercises that
fallback once instead of blocking on it.

`ChangelogSystemContentImporterTests` (`Quotinator.Data.Tests`) —
`RefreshAsync_WritesReleasesAndOrderedLines` (one release + one unreleased entry from a hand-built fake
`IChangelogService`; asserts row counts, `SortOrder` preserves list order, `AudienceHighlights` land with
the correct `AudienceKey`, `Issues` store as strings, quote/`MachineTranslated` round-trip) and
`RefreshAsync_RunTwice_OverwritesNotDuplicates` (calling `RefreshAsync` twice leaves row counts
unchanged, proving the clear-then-reimport step, not the `(Language, Version)` unique constraint,
is what makes re-running safe).

Verified: full solution `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Full solution
`dotnet test --configuration Release` — all projects green (run twice to confirm the startup-latency fix
is not merely flaky-passing), including the previously-broken
`Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable`.

### 7. `ChangelogLineRow` + `IJoinStrategy` + `IChangelogReader`/`ChangelogReader` — DB-first, JSON-fallback
**Status:** ✅ Done

`ChangelogLineRow` (`Quotinator.Data.Queries`) is the flat read model — every `Changelog` column plus
`ChangelogLine`'s `Kind`/`AudienceKey`/`Value`/`SortOrder`, all nullable on the line side so a
`Changelog` row with zero lines still appears once via the LEFT JOIN. `ChangelogWithLinesStrategy :
IJoinStrategy<ChangelogLineRow>` wraps a new `Sql.ChangelogContent.SelectAllWithLines()` factory method
— hand-written `LEFT JOIN ... ON {IdClauses.Join(...)} AND [l].[IsDeleted] = 0` matching
`Sql.Quotes.SelectBase`'s own idiom (not the generic `Sql.Joins.Left` helper, which doesn't
case-insensitively wrap its join condition), per ADR 017 (never a hand-rolled query outside this
pattern) and the case-insensitive-by-default rule (`Changelog.Id`'s own SELECT-list appearance also
goes through `IdClauses.SelectColumn`).

**Deviation from this section's original design snippet, documented here rather than silently
diverging:** the sketch above showed `_joinRepo.QueryAsync(new { language })` — a SQL-side language
filter. `ChangelogReader` instead queries with no filter at all and resolves the language (including the
same normalise-then-fallback-to-`en` logic `IChangelogService.GetForCulture`/`ChangelogService.Normalise`
already implement) in C#, after grouping the flat rows by `Language` then by `ChangelogId`. Reasoning:
the whole changelog dataset (bounded by release count × line count — a handful of KB in practice) is
cheap to pull into memory once, and duplicating the normalise/fallback rule as a second, SQL-side
implementation would be a second place for that logic to drift from the JSON-backed service's own rule.
`Issues` lines parse back from their stored string form to `int` here (`int.Parse`); `Releases` re-sort
by `Date` descending (ISO 8601 strings sort correctly lexically — the same assumption this project
already relies on for `RecordBase`'s own string-stored timestamps) since `Changelog` itself has no
explicit ordering column, unlike `ChangelogLine.SortOrder`.

The missing-table fallback (`IsMissingChangelogTable`, checking `SqliteErrorCode == 1` and the message
containing `"no such table: Changelog"`) is #293's `NotificationReader.IsMissingNotificationTable` idiom
applied verbatim — checking for `Changelog` alone (the query's driving table) covers the realistic
failure mode, since `ChangelogLine` is created in the same migration/baseline. An empty result set (zero
rows — e.g. a request racing the background import from Step 6, which #309's own `Program.cs` wiring
deliberately doesn't block startup on) is treated the same as a missing table: both fall back to
`IChangelogService` rather than returning an empty/null document, which is the same degraded case the
missing-table path already had to handle.

`ChangelogReaderTests` (`Quotinator.Data.Tests`) —
`GetDocumentAsync_DatabasePopulated_ReturnsReassembledContent` (imports a document via
`ChangelogSystemContentImporter`, then reads it back through `ChangelogReader` and asserts every
field — including `GetHighlightsFor(ChangelogReservedAudience.Notification)` round-tripping through the
DB, closing the loop #307 opened), `GetDocumentAsync_TablesMissing_FallsBackToFileService`,
`GetDocumentAsync_TablesMissing_LogsWarning` (a hand-rolled `RecordingLogger : ILogger<ChangelogReader>`,
matching `SourceCacheUpdaterTests`' own existing pattern — no test-logging package was added), and
`GetDocumentAsync_UnrelatedSqlError_Propagates` (a deliberately broken `IJoinStrategy` producing a SQL
syntax error, proving the narrow exception filter doesn't over-match).

Verified: full solution `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Full solution
`dotnet test --configuration Release` — all projects green (run twice), including
`Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable` (Step 6's fix still holds).

### 8. Wire `About.razor` to `IChangelogReader`
**Status:** Not started

### 9. Tests
**Status:** Not started

Real-SQLite tests (in-memory, matching the production storage mode) for the importer and reader; a
reproduction of #293's own pattern — build a database with no `Changelog`/`ChangelogLine` tables,
confirm `IChangelogReader` falls back to the JSON path instead of throwing.

### 10. Full verification (T1, T2)
**Status:** Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The keep-alive connection keeps the shared-cache in-memory database alive across multiple separately-opened connections | Unit test | `ChangelogConnectionKeepAliveTests.MultipleConnections_WithKeepAliveOpen_ShareSameInMemoryDatabase` |
| 2 | ✅ | `ChangelogDatabaseInitializer`'s baseline and incremental replay produce identical schema (parity, matching the main database's own schema-drift test pattern) | Unit test | `ChangelogDatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalSchema` |
| 3 | ✅ | A genuinely empty (fresh) changelog database takes the one-step baseline path, matching `DatabaseInitializer`'s existing rule | Unit test | `ChangelogDatabaseInitializerTests.EmptyDatabase_AppliesBaseline` |
| 4 | ✅ | Importer writes one `Changelog` row per release + one per language's `unreleased`, with correctly-ordered `ChangelogLine` children | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_WritesReleasesAndOrderedLines` |
| 5 | ✅ | Re-running the importer overwrites (not duplicates) existing rows | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_RunTwice_OverwritesNotDuplicates` |
| 6 | ✅ | `IChangelogReader.GetDocumentAsync` returns DB-backed content, correctly reassembled (including `AudienceHighlights["notification"]`), when the database is populated | Unit test | `ChangelogReaderTests.GetDocumentAsync_DatabasePopulated_ReturnsReassembledContent` |
| 7 | ✅ | `IChangelogReader.GetDocumentAsync` falls back to `IChangelogService` (not an exception) when the tables don't exist | Unit test | `ChangelogReaderTests.GetDocumentAsync_TablesMissing_FallsBackToFileService` |
| 8 | ✅ | The fallback logs a warning explaining the condition, not silently | Unit test | `ChangelogReaderTests.GetDocumentAsync_TablesMissing_LogsWarning` |
| 9 | ✅ | A genuinely different SQL error (not "table missing") still propagates, not swallowed | Unit test | `ChangelogReaderTests.GetDocumentAsync_UnrelatedSqlError_Propagates` |
| 10 | ❌ | `About.razor` renders correctly via `IChangelogReader` | Live (T1) | Developer confirms in Visual Studio |
| 11 | ❌ | `About.razor` still renders (fallback path) when the changelog import fails, matching #293's degraded-state precedent | Live (T2) | Docker: force the changelog import to fail, confirm `/about` still returns `200` with content, not a crash |
| 12 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 13 | ❌ | Full test suite green | Build | `dotnet test --configuration Release` |

---

## Relationship to existing issues

- **#80** — the changelog system this issue moves storage for.
- **#75** — the `AggregateRepository<TParent, TChild>` master/detail pattern this issue's schema is
  built on.
- **#293** — the bug-class (missing-table crash on an exempt degraded-state path) this issue must not
  reintroduce; its narrow-exception-catch idiom is reused directly.
- **#305** — owns general DB-integrity detection and user-visible warnings; this issue's fallback stays
  structurally compatible with it but does not duplicate it.
- **#307** — already shipped; `ChangelogReservedAudience`/`GetHighlightsFor(...)` work unchanged
  against DB-sourced or JSON-fallback-sourced `ChangelogRelease`/`ChangelogUnreleased` objects alike —
  `AudienceKey` on `ChangelogLine` is where the `"notification"` convention lives in this schema.
- **#81** — hard dependency on this issue; its plan doc needs updating (after this issue lands) to read
  via `IChangelogReader` instead of `IChangelogService` directly.
