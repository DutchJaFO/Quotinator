# #309 — Move changelog content to database-backed System_Changelog table

**Status:** In progress
**GitHub issue:** #309 (open)
**Depends on:** #80 (done, released — Changelog handling milestone)

> **Next action: smoke test 44 at T2 (row 34) — the only outstanding row.** Every live check on this issue has
> found a further defect underneath the last — steps 14, 16, 17 and 18. The database did not survive
> process uptime; verification rested on the absence of a message and so proved nothing; the JSON
> fallback was silently serving the startup read on *every* boot; the refresh was not atomic, so a read
> could be served a half-rebuilt changelog; and a read could be served a previous run's content, which on
> an upgrade is the wrong changelog entirely. All are fixed and unit-tested. The last three share one
> root: the database is populated asynchronously, and each mechanism reading it assumed something
> different about what an unexpected result meant. Two live runs on 2026-08-19 confirmed the current
> shape — a fresh database (row 32) and an already-populated one on the upgrade path (row 33), the
> latter being the exact profile that failed at 22:27. **One row remains: 34, smoke test 44 at T2.**

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

**Revision (2026-08-16) — one ADR added to scope; nothing built here is wrong.** This issue made
Quotinator the first application in the project with more than one database. ADR 015's table-naming
rule assumes one: it defines `Import_`/`Audit_`/`System_` for `Quotinator.Data`'s own tables and one
prefix (`Quotinator_`) for the consuming application's domain tables, all inside a single namespace,
because its whole rationale is SQLite's lack of schema qualification *within* that namespace. It says
nothing about a second database.

The gap surfaced while planning #319, when a reader took `Changelog`/`ChangelogLine`/
`ChangelogSchemaVersion` for ADR 015 drift and filed it as a defect. It is not one — ADR 018's
"Database placement" section decides the separation, and `ChangelogContentMigrations`' class doc states
the naming consequence. But the rule that makes those names correct is not written down anywhere a
reader would look first, which is why the misreading happened at all.

Two clarifications from the developer (2026-08-16) frame what the ADR has to settle: **the changelog
database is effectively a second user-domain database**, not a system one, so no existing domain prefix
describes it; and **ADR 005 is not wrong** — its `System_Changelog` naming was correct under the
one-database assumption in force when it was written, and it simply did not account for an application
having more than one database.

The ADR is the final step below.

**Outcome (2026-08-16): a revision to [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md),
not a new ADR.** A prefix names a domain, never a database, and an application defines a domain per
distinct concern it owns — superseding that ADR's one-prefix-per-consumer rule. `Changelog_` is
Quotinator's second domain, so this issue's three tables are renamed: `Changelog` → `Changelog_Entry`,
`ChangelogLine` → `Changelog_Line`, `ChangelogSchemaVersion` → `Changelog_SchemaVersion`. Applied by
editing this database's migration and baseline in place, since it is in-memory only and no persistent
copy exists anywhere.

Every previously-verified row stays ✅ — the rename changes table names, not behaviour, and the existing
tests cover it.

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
**Status:** ✅ Done

`About.razor.cs` now injects `IChangelogReader` instead of `IChangelogService` and awaits
`GetDocumentAsync(...)` inside the existing `OnInitializedAsync` (already async, no shape change to the
method needed). `About.razor`'s own markup was untouched — it only ever consumed `_document`, never
`IChangelogService` directly.

**Live-verified in Docker (T2), not just unit-tested** — built the image, ran the container, and
confirmed via the container's own startup logs that `[Changelog - Import] refreshed 126 entries across 3
language(s)` completed before `/about` was requested. Fetched `/about` after full startup completed (the
first fetch, made too early, correctly returned the `StartupWaitMiddleware` "starting up" page instead —
itself a confirmation that #280's existing wait-page behaviour still works correctly with the new
background changelog task in flight). The second fetch returned real rendered content: 43
`changelog-entry` elements, correct `data-version` attributes (`1.0.0`, `1.0.0-beta.1`, `1.0.1`, …), and
real highlight text — proving the DB-backed path actually served the page, not a silent fallback (no
`[Changelog - Read]`/`[Database - Init]` warning appeared anywhere in the container's logs for the whole
run). Container logs showed zero warnings or errors end to end.

Verified: full solution `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Full solution
`dotnet test --configuration Release` — all projects green.

### 9. Tests
**Status:** ✅ Done

Already satisfied incrementally by Steps 6 and 7's own test coverage, written alongside each class
rather than deferred to a separate pass — `ChangelogSystemContentImporterTests` (real-SQLite,
shared-cache in-memory, matching the production storage mode) and `ChangelogReaderTests`, including the
#293-pattern reproduction this step specifically called for
(`GetDocumentAsync_TablesMissing_FallsBackToFileService`/`_LogsWarning`). No further test-writing work
remained by the time this step was reached; recorded as its own step in the verification table below
rather than folded silently into 6/7's own entries, since it names a distinct requirement.

### 10. Full verification (T1, T2)
**Status:** ✅ Done

**Row 12's original plan (a #293-style Docker fault injection) is not meaningfully constructible for
this database, and the verification method was changed rather than left unmet.** #293's own T2
procedure works by upgrading a *persistent* SQLite file across versions until a real migration failure
leaves genuinely missing tables on disk — that technique has no equivalent here: the changelog database
is always-fresh, in-memory, and rebuilt from nothing every boot, so there is no persistent state to
externally corrupt via Docker without editing the production code path itself (which would test the
edited code, not the shipped code). The two real ways this database's content can be unavailable to a
request — the `Changelog` table not existing, and the table existing with zero rows (the live race
window between Kestrel accepting requests and the Step 6 background import finishing) — are each
exercised directly against the real `ChangelogReader`/`ChangelogDatabaseInitializer` classes via
real-SQLite unit tests (rows 7, 8, 10), which is a strictly more precise reproduction of the actual
failure condition than a Docker-level fault would have been. The live Docker run in the `About.razor`
wiring step additionally confirms the happy path renders correctly with zero warnings/errors across a
full real startup.

**Row 11 (T1) confirmed by the developer in Visual Studio (2026-08-14).** A local `dotnet run` started
cleanly (`[Changelog - Import] refreshed 126 entries across 3 language(s)`, no changelog-related
warnings), and a screenshot of the live `/about` page (Dutch culture) shows the DB-backed content
rendering correctly — the new unreleased #309 entry appears translated under "Niet uitgebracht", and the
v1.8.3 release's full highlight list renders beneath it, matching the JSON source content exactly.

### 11. ADR: table naming when an application has more than one database

**Status:** ✅ Done

Delivered as **"Revision — issue #309" in
[ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)**, not as a new ADR. The
superseded paragraph carries a forward pointer to it.

Decided: a prefix names a **domain** — intent and ownership — never a database; an application defines
a domain per distinct concern it owns, superseding "exactly one"; and domains and databases are
independent axes, so `Changelog_` would be correct wherever that content lived.

A new ADR was drafted first and discarded. Stripped of narrative it held two things — the "exactly one"
change and a new domain row — both of which are modifications to ADR 015's own rule inside ADR 015's
own subject. A second document would have forced the two to be read together permanently.

Two positions were considered and rejected. Keeping the tables unprefixed is defensible on this ADR's
disambiguation rationale alone, but leaves a domain with no name. Making the prefix identify the
*database* was rejected as overloading: which database a table is in is expressed structurally (the
keyed connection factory, the initializer, the nested `Sql` class), and the revision records the
resulting limitation explicitly — a reader still cannot tell `Changelog_Entry` and `System_Notification`
apart by database, and the remedy would be splitting `Sql.cs`, not the table names.

### 12. Rename the changelog tables per ADR 015's revision

**Status:** ✅ Done

Applied by **editing this database's migration and baseline in place**, not by adding a rename
migration. That is only correct because the changelog database is in-memory only and no persistent copy
exists anywhere — ADR 015's revision records that this window closes the moment a persistent-file
variant ships.

#### What changes

| From | To |
|---|---|
| table `Changelog` | `Changelog_Entry` |
| table `ChangelogLine` | `Changelog_Line` |
| table `ChangelogSchemaVersion` | `Changelog_SchemaVersion` |
| index `UX_Changelog_Language_Version` | `UX_Changelog_Entry_Language_Version` |
| index `UX_Changelog_Language_Unreleased` | `UX_Changelog_Entry_Language_Unreleased` |
| index `IX_ChangelogLine_ChangelogId` | `IX_Changelog_Line_ChangelogEntryId` |
| column `ChangelogLine.ChangelogId` | `Changelog_Line.ChangelogEntryId` |
| class `ChangelogEntity` | `ChangelogEntryEntity` (file renamed to match) |

Index naming follows the main database's existing convention — `IX_`/`UX_` + the full prefixed table
name + columns, as in `IX_Audit_Entry_TableName_RecordId`. The class and column renames are developer
decisions (2026-08-16); ADR 015 governs table names only, and the existing class names are not
self-consistent (`Audit_Entry` → `AuditEntryEntity`, but `Audit_Change` → `ChangeEntity`), so precedent
could not settle them.

#### Files, from a full inventory (2026-08-16)

**`Quotinator.Data` — schema**
- `Database/ChangelogContentMigrations.cs` — both `CREATE TABLE`s, all three indexes, the FK
  `REFERENCES`, and the class doc, whose "No `System_`/domain prefix on either table" justification is
  now obsolete and is replaced by a pointer to ADR 015's revision.

**`Quotinator.Data` — queries**
- `Queries/Sql.cs`, `ChangelogSchema` — `CreateVersionTable`, `GetCurrentVersion`, `InsertVersion`, and
  the doc comment naming the version table.
- `Queries/Sql.cs`, `ChangelogContent` — `ClearLines`, `ClearChangelogs`, and `SelectAllWithLines()`'s
  `FROM`/`LEFT JOIN`/join condition. Its `IdClauses.SelectColumn("[c].[Id]", "ChangelogId")` alias
  becomes `ChangelogEntryId` so the read model matches; the `LOWER()` wrap stays, keeping
  `SqlSelectPresentationGuard` satisfied under the new name.
- `Queries/ChangelogLineRow.cs` — property `ChangelogId` → `ChangelogEntryId`.
- `Queries/ChangelogWithLinesStrategy.cs` — doc comment only.

**`Quotinator.Data` — entities, repositories, import**
- `Entities/ChangelogEntity.cs` → `Entities/ChangelogEntryEntity.cs`, class renamed,
  `[Table("Changelog_Entry")]`.
- `Entities/ChangelogLineEntity.cs` — `[Table("Changelog_Line")]`, property `ChangelogId` →
  `ChangelogEntryId`, and its two `<see cref="ChangelogEntity"/>` references.
- `Repositories/ChangelogRepository.cs` — the `AggregateRepository<,>` type arguments,
  `InsertWithLinesAsync`, `GetChildren`, and two doc references.
- `Repositories/ChangelogReader.cs` — `GroupBy(r => r.ChangelogId)`.
- `Import/ChangelogSystemContentImporter.cs` — `new ChangelogEntity` and `ChangelogId = changelogId`.
- `Logging/LogMessages.cs` — the fallback warning text naming the `Changelog` table, and its summary.

**Tests**
- `Quotinator.Data.Tests/Database/ChangelogDatabaseInitializerTests.cs` — the table-name array and four
  raw SQL statements.
- `Quotinator.Data.Tests/Import/ChangelogSystemContentImporterTests.cs` — eight raw SQL statements.
- `Quotinator.Data.Tests/Repositories/ChangelogReaderTests.cs` — `BrokenSqlStrategy`'s deliberately
  malformed SQL, plus doc comments.

**Docs**
- `CLAUDE.md`'s table-naming line and `docs/database-conventions.md`'s "Table naming" rows gain
  `Changelog_`, plus the prefix-names-a-domain rule and a "don't drop the prefix in a single-purpose
  database" row.
- **No changelog entry** (decided during execution, deviating from this plan's own earlier line). #309
  is already in `unreleased.issues` with a `changed` entry describing the database-backed changelog;
  this rename changes no behaviour a user can observe. An "internal tables renamed" line is the
  technical detail the changelog's own rules exclude.

#### Verified as *not* affected

- `Quotinator.Data.Tests/Connections/ChangelogConnectionKeepAliveTests.cs` — uses its own scratch table
  `T`.
- `SqlBoundaryTests`/`SqlQueryGuardTests` — they enumerate the *nested class* names
  `ChangelogSchema`/`ChangelogContent`, which do not change.
- `DatabaseConnectionKeys.Changelog` (`"changelog"`) — a connection key, not a table name.
- `Quotinator.Changelog` the project, and every `IChangelogService`/`ChangelogRoot`/`ChangelogRelease`
  type — JSON-side, no table involved.

#### Order

Schema first, then queries, then entities/repositories/import, then tests, then docs. The build stays
red in between — this is one rename, not an incremental refactor, and splitting it into separately
green steps would mean writing compatibility shims for a change that has no consumers outside this
repository.

### 13. T2 verification of the rename

**Status:** ✅ Done (2026-08-17)

Ran against `quotinator:t2`, built from this branch.

| Check | Result |
|---|---|
| Baseline applies under the new names | `[Changelog - Init] schema created at baseline (v1)` |
| Importer writes to the renamed tables | `[Changelog - Import] refreshed 126 entries across 3 language(s)` |
| `/about` renders | HTTP 200, 42 `changelog-entry` elements |
| **Served from the database, not the fallback** | No `falling back` warning in the log — the decisive check, since the page renders identically either way |
| Whole-startup log clean | 0 `WRN`/`ERR`/`FTL` |
| Upgrade from a real v1.8.3 database | `data v3 → v11`, 0 exceptions, and `schema is up to date` (not a repeat) on restart |

Smoke-test sections run: 1 ✅, 33 ✅, 36 ✅, 37 ✅, 39a–39c ✅. **Not completed: 32, 38, 39d–39i** —
see the two findings below.

**Disposition of the three not completed**, recorded so none of them is left silently unaccounted for:

| Section | Where its coverage now lives |
|---|---|
| 32 (Reset is a full wipe, #156) | **Still not run.** Nothing in this issue touches Reset or the main database's wipe path, so it is not a gate on #309 — but no other issue has claimed it either, so it stays outstanding for whichever T2 pass next runs the checklist end to end |
| 38 (degraded pages survive a migration failure, #293) | Owned by **#327**, which rebuilds it around the never-crash contract — see step 15 |
| 39d–39i | Renumbered to sections 40–43 by step 15. Their scenarios were verified live under **#312**'s own T2 pass (its verification rows 23, 24, 25 and 27), against the same code this issue does not change |

### 14. Finding — the changelog database does not survive process uptime

**Status:** ✅ Done — fixed in this issue (developer decisions, 2026-08-18)

**Scope decision:** this lands here, not as its own issue. The three questions left open below were
answered by the developer on 2026-08-18:

- **Where the file lives:** `{dataDir}/quotinatorchangelog.db`, a sibling of `quotinatordata.db`, via a
  new `DataPaths.ChangelogDatabaseFile` constant. HA persistence comes free — `HaFallbackDir()` already
  resolves `/data`, so no `addon/config.yaml` change is needed.
- **Reset and backup:** neither touches it. Its contents are wholly derived from the changelog JSON
  shipped in the image and re-imported at every startup, so nothing user-authored is ever at risk and
  the file self-heals. Reset keeps its existing single responsibility (the main database only).
- **`ChangelogConnectionKeepAlive`:** retained in `Quotinator.Data` rather than deleted or relocated.
  It is no longer wired into `Program.cs`, but ~20 tests use in-memory SQLite deliberately for speed and
  isolation and still need it. Its XML doc now states plainly that production does not use it.

**Observed live during step 13's T2 run.** In a container started at 19:05:15, the baseline was created
and 126 entries imported. The first read, at **19:18:47**, failed:

```
[Changelog - Read] Changelog_Entry table missing — falling back to the JSON-backed changelog service
SqliteException: SQLite Error 1: 'no such table: Changelog_Entry'
```

Every subsequent read falls back too. No process restart occurred (one baseline log line). So the
in-memory database is gone permanently, and the database-backed read path — this issue's entire
purpose — is silently dead for the life of the process. Nothing is user-visible, because the JSON
fallback works exactly as designed; that is why it went unnoticed.

**Not caused by the rename.** The table existed and accepted 126 rows under its new name; a naming
error would have failed at import, not thirteen minutes later.

**Root cause (developer, 2026-08-17): a database that only ever exists in memory won't survive.**
`ChangelogConnectionKeepAlive` is a workaround for a storage mode whose existence depends on a
process-local handle staying open, not a fix for it. The remedy is the persistent-file variant this
issue deferred as YAGNI — `Program.cs:306`'s connection string is the single config point, exactly as
this plan predicted ("a wiring change, not a redesign"). The keep-alive class likely disappears with it.

**Sequencing note:** the rename landing *before* this is correct and lucky. ADR 015's revision states the
in-place migration edit is valid only while no persistent copy exists; going persistent first would have
made the rename cost a real migration against user databases.

**What changed.** `Program.cs` now builds the changelog `SqliteConnectionFactory` over
`Path.Combine(dataDir, DataPaths.ChangelogDatabaseFile)` instead of
`file:quotinatorchangelog?mode=memory&cache=shared`, and the eager keep-alive resolve at startup is
gone — a file needs no scaffolding to stay alive. Exactly the "wiring change, not a redesign" this
plan predicted.

**Red tests** (`ChangelogDatabaseWiringTests`, `Quotinator.Api.Tests`) assert the real registration
through the live DI container, not a stand-in factory: a test building its own factory would prove only
that SQLite persists files, not that this application asked it to. Both were red against the in-memory
wiring, reporting the actual connection string in their failure message.

**Consequence for future changelog schema changes.** ADR 015's revision permitted editing the changelog
migration in place *only while no persistent copy existed*. That is no longer true: from this release
onward a real user database exists on disk, so `ChangelogDatabaseInitializer.Migrations` is frozen under
the same append-only rule as `DatabaseInitializer.DataOwnedMigrations`, and its baseline must be kept in
step with it.

**Smoke test:** `docs/smoke-tests.md` section 44, added in the same commit as this fix per that
document's own living-checklist rule. It checks the file exists on disk, that no JSON-fallback line is
ever logged, and — the part that actually catches this regression — that the database-backed path is
still alive after more than fifteen minutes of uptime. No shorter check can see it.

### 15. Finding — `docs/smoke-tests.md` defects found while running it

**Status:** ✅ Done (2026-08-18) — all six fixed in `docs/smoke-tests.md`, plus two further defects the
fix pass surfaced. Two of the six were reframed rather than repaired, because the developer's own rules
made the original expectation invalid rather than merely stale:

| Section | Outcome |
|---|---|
| 33 | Fixed by removing the count entirely — asserts the announcement a *known cause* produces is present. Counts are now forbidden document-wide |
| 37 | Fixed by removing the version numbers, not by updating them — asserting a migration version is now forbidden document-wide |
| 38 | Not repairable; its coverage is now owned by **#327**, which rebuilds it around the never-crash contract instead of one historical incident |
| 39a | Fixed — every query now runs inside a container against the same mount, because `-v /tmp/…:/data` resolves in the Docker VM while `dotnet run` executes on Windows against a different `/tmp` |
| 39b | Fixed — reads the payload's fields for self-consistency instead of matching a transcribed literal that goes stale whenever the announcement's wording changes |
| 39 | Fixed — the five sub-tests sharing one container became named sub-parts of one section; the rest became independent sections 40–43, each with its own setup and cleanup. No lettered sub-tests remain |

**Two further defects found while fixing the above**, neither previously recorded:

- **The final sub-test could never run.** It read `/tmp/q312/data`, which an earlier sub-test's cleanup
  line deletes — guaranteed to fail in document order. Merged into the section that owns that volume.
- **The what's-new backfill sub-test was unrunnable as written** — no setup at all, a literal
  `docker start <container>` placeholder, and a `/tmp/qws` directory no section creates. Rewritten
  self-contained, with its migration rollback keyed off `MAX(Version)` rather than a hardcoded number.

Original defect list, for the record:

| Section | Defect |
|---|---|
| 33 | Stale — expects exactly one notification; two are correct now that the unreleased changelog carries a `notification` audience highlight |
| 37 | Stale — expects `Data v2 → v3`; actual is `v2 → v11` |
| 38 | **Guaranteed false pass** — its `--read-only` technique no longer forces a migration failure (#294 fixed that path), so its own "confirms we reached the failure state" check cannot hold. Sections 37 and 38 use an identical setup and assert opposite outcomes |
| 39a | `-v /tmp/...:/data` leaves the host directory empty on Docker Desktop for Windows, so the documented DbInspector path cannot resolve |
| 39b | Stale — expected `Metadata` predates migration 11's backfilled `releaseState`/`version`/`contentHash` |
| 39 | Lettered sub-tests (39a–39i) encode real dependencies: 39b/39c query the database 39a creates, 39d restarts its container by name, and 39a's cleanup line sits between 39d and 39e. Per process.md, sequences are plain integers; per the developer (2026-08-17), a smoke test must never depend on another |

**T2 re-confirmed against the final commit state (2026-08-14), not just Step 8's earlier snapshot.**
Step 8's own live Docker run happened before Step 9's added test and before the unreleased #309
changelog entry existed. Rebuilt the image and re-ran the full startup sequence from the actual final
state: zero warnings/errors/exceptions anywhere in the container's log for the whole run, `/about`
returned `200` with all 43 `changelog-entry` elements present, and the new unreleased entry's own text
(`"Changelog content (shown on the About page)…"`) appears exactly once in the rendered page — the live
app, not just the JSON source file, serves the change just added to `changelog.en.json`.

### 16. Finding — the JSON fallback was the normal path, and nothing said so

**Status:** ✅ Done (2026-08-19)

Found while assessing the T1 evidence for row 21, then corrected twice as the developer sharpened what
was actually wrong. Three layers, each exposed by fixing the one above it.

**Layer 1 — verification rested on the absence of a message.** Rows 17 and 19, and smoke test 44, all
treated the *absence* of a `falling back` warning as proof that the database served `/about`. It was not
proof: `GetDocumentAsync` had two fallback paths and only the missing-table one logged. The
empty-database path returned the JSON document silently, indistinguishable from a healthy read in both
the log and the rendered page — the same invisibility that let step 14's defect run for thirteen minutes.

Per developer direction: *"you need a log entry to show that the changelog was read from the database
instead of something else to prove it works."* Proving correctness by the absence of an error is
fragile, because any new silent path reopens the hole. Every read now states which source answered.

**Layer 2 — that log line immediately exposed a race on every boot.** The first run with it showed:

```
21:40:51 WRN [Changelog - Read] database has no entries — falling back to the JSON-backed changelog service
21:40:52 INF [Changelog - Import] refreshed 126 entries across 3 language(s)
21:40:57 INF [Changelog - Read] served 2292 row(s) from the database
```

`Program.cs` runs the changelog import (line ~798) and #81's what's-new producer (line ~1005) as two
independent detached tasks with no ordering between them. The producer won that race on **every single
boot**, so the startup read had always been served by the JSON fallback and the database-backed path
this issue exists to build was never the one that answered. Each task's comment independently reasoned
"the reader falls back on its own if the database isn't ready" — which is exactly what made the
exceptional path the normal one.

Per developer direction: *"of course you shouldn't try to read the json until you've established the
import into the changelog database could not work."* Emptiness during the startup window is not a
failure, it is an unfinished question. The reader now waits for the import to conclude before
interpreting it — which also fixes the producer for free, since the producer simply reads and the reader
does the waiting. No task reordering was needed.

**Layer 3 — an empty changelog is not a failure either.** Per developer direction: *"no changelog
entries does not mean the changelog failed to import. New applications may not have any entries yet."*
`Quotinator.Data` is meant to be reusable (ADR 003/004), and a consumer with no changelog at all is a
legitimate, permanent state — not a degraded one. So a successful import that wrote nothing is
authoritative: the reader returns the empty result rather than consulting the JSON files, and says so at
Information level rather than warning. `About.razor` already renders a null document by omitting the
changelog section, so nothing downstream needed changing.

**Resulting behaviour**, one branch per state the old code conflated into "empty → fall back":

| State | Behaviour | Log |
|---|---|---|
| Rows returned | Serve from the database | `served {RowCount} row(s) from the database` — Information |
| Empty, import still running | Wait for it, then re-query | none |
| Empty, import succeeded | Serve the empty result — a new application has no changelog | `the database holds no changelog entries` — Information |
| Empty, import failed | Fall back | `the changelog import failed — falling back…` — Warning |
| Empty, wait budget expired | Fall back | `timed out waiting for the changelog import…` — Warning, distinct from failure |
| Missing table | Fall back | unchanged Warning |

`IChangelogImportReadiness`/`ChangelogImportReadiness` (`Quotinator.Data.Import`) carries the outcome,
set by `Program.cs`'s import task on both its success and failure paths so a reader can never wait on a
signal nobody will raise. The wait is bounded — `DefaultWaitBudget`, 30 s — purely so a dead import task
cannot hang a page render; it is a backstop, not a tuned value. The budget belongs to the signal rather
than the caller, since no individual reader has a basis for choosing one.

**Red tests first** (`ChangelogReaderTests`). `GetDocumentAsync_DatabasePopulated_LogsThatTheDatabaseServedIt`
was red against the original implementation in the ordinary way. The four readiness tests could not be
red before compiling, since the interface they exercise did not exist — so they were verified by
mutation instead: the reader was temporarily reverted to its old fall-back-on-empty behaviour and all
four failed, confirming they are load-bearing rather than vacuous.

`GetDocumentAsync_DatabaseEmpty_FallsBackToFileService` was **removed, not renamed**: it asserted that an
empty database always falls back, which layers 2 and 3 establish is wrong. Three tests replace it, one
per state it conflated.

### 17. Finding — the refresh was not atomic, so readers could observe a half-rebuilt changelog

**Status:** ✅ Done (2026-08-19)

Found immediately by step 16's own log line, on the first run after it landed:

```
22:15:10 INF [Changelog - Read] served 615 row(s) from the database
22:15:11 INF [Changelog - Import] refreshed 126 entries across 3 language(s)
22:15:16 INF [Changelog - Read] served 2292 row(s) from the database
```

615 is not a complete changelog. `RefreshAsync` cleared both tables and then re-inserted entry by entry,
each insert its own transaction and the clears auto-committing on their own — nothing spanned the two.
For the duration of every refresh a concurrent reader could observe any intermediate state between empty
and complete, and **be served it as though it were the whole thing**. Here the startup read landed
mid-rebuild and #81's what's-new producer chose which release highlights to announce from a partial
changelog.

Step 16's readiness signal does not catch this and never could: `615 > 0`, so the reader takes the
"database has content" path without ever consulting the signal. Step 16 closed the empty case and left
the torn one open.

**The fix is atomicity, not more waiting.** The clear and every insert now share one transaction via
`TransactionScope.ExecuteAsync` — the project's existing idiom — with `InsertWithLinesAsync` joining the
caller's unit of work. A reader now sees either the previous complete content or the new complete
content, never a rebuild in progress. The two mechanisms are complementary and both are needed: the
transaction stops a *partial* read, the readiness signal handles a genuinely *empty* database on a fresh
install, where there is no previous content to serve.

**This defect predates step 16** — it has been present since step 5 built the importer, through the T1
and T2 passes that declared this issue complete. It was invisible for the same reason everything else in
steps 14–16 was: a partial changelog renders as a plausible page. The log line added in step 16 has now
paid for itself three times over.

**Red test** — `ChangelogSystemContentImporterTests.RefreshAsync_FailsPartway_LeavesThePreviousContentIntact`.
Deterministic rather than concurrent: a source that throws on the second language, asserting the database
still holds the first import's content afterwards. A timing-based test of the actual race would be flaky;
this tests the invariant the race violates.

The first red run was red for the *wrong* reason — a `UNIQUE constraint failed: Changelog_Entry.Language`
caused by the test's own setup mapping an `en`-tagged document to the `nl` key, since the importer writes
`document.Language` rather than the dictionary key. After fixing the fixture, red was re-established by
mutating the implementation back to its non-atomic shape, which failed on the assertion itself:
`expected: 4, actual: 2` — the clear committed, `en` written, `nl` threw. That is the defect, reproduced.

### 18. Finding — a read could be served the previous run's content, and the log line was unreadable

**Status:** ✅ Done (2026-08-19)

Two things, both surfaced by the 22:27 run that confirmed step 17.

**The count made no sense to a reader.** The developer's objection — *"you can't claim 126 entries and
2292 rows at the same time"* — was correct about the log even though the numbers were not actually
contradictory: `126` counts `Changelog_Entry` rows, `2292` counts the `LEFT JOIN Changelog_Line` result,
so roughly one per changelog *line*. Both true, measuring different things, and impossible to reconcile
from the log. Worse, "served 2292" describes neither what was asked for nor what was returned — the
reader hands back a single document. It now reports **entries**, the same unit the importer uses, so
`refreshed 126 entries` and `served 126 entries` are directly comparable and a mismatch is a signal.

**A read could return a previous run's content.** Step 17 made the refresh atomic, which is what a
reader observing a rebuild needs — but it also meant a read arriving before the commit is served the
*previous* run's complete content:

```
22:27:21 INF [Changelog - Read] served 2292 row(s) from the database   ← the previous run's copy
22:27:21 INF [Changelog - Import] refreshed 126 entries across 3 language(s)
```

Harmless on a normal boot, since both runs import identical JSON. Not harmless on an **upgrade**: the
new image ships a changelog the database does not yet contain, so #81's what's-new producer would
announce from the old copy and miss the new release's highlights — on the one boot where announcing them
is the entire point. And because it depends on which of two detached tasks wins, it is intermittent
rather than reliably wrong.

**This is a regression this issue introduced**, which is why it is fixed here rather than filed against
#81. Before #309 the producer read `IChangelogService` — the JSON files, directly, always current.
Moving it behind an asynchronously-rebuilt database is what created the staleness.

**The fix**: wait for readiness *before* reading, rather than only after finding the result empty. One
stateable invariant — a read reflects this process's own import — and the race disappears instead of
narrowing. `WaitAsync` returns immediately once an outcome is known, so only startup-window reads pay
anything. It also collapses the double-query the previous shape needed: one wait, one query, no re-read.

Three defects in three steps have now come from the same root: the database is populated asynchronously,
and every mechanism that read it made a different wrong assumption about what an unexpected result meant
— step 16 that empty means unavailable, step 17 that any non-empty result is complete, step 18 that any
complete result is current.

**Red test** — `ChangelogReaderTests.GetDocumentAsync_PreviousRunsContentStillPresent_ReturnsThisImportsContentInstead`.
A previous run's content committed, a read started, then this process's import replaces it. Red against
the previous shape, returning the stale `0.9.0` where `1.0.0` was expected.

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
| 10 | ✅ | An empty (zero-row) database — schema created, background import not finished yet — falls back the same way a missing table does | Unit test | `ChangelogReaderTests.GetDocumentAsync_DatabaseEmpty_FallsBackToFileService` |
| 11 | ✅ | `About.razor` renders correctly via `IChangelogReader` | Live (T1) | Developer confirmed in Visual Studio (2026-08-14) — screenshot of `/about` (Dutch culture) shows the DB-backed unreleased #309 entry and v1.8.3 release rendering correctly |
| 12 | ✅ | `About.razor` still renders (fallback path) when the changelog database is unavailable, matching #293's degraded-state precedent | Unit test (see the note under the full-verification step) | `ChangelogReaderTests.GetDocumentAsync_TablesMissing_FallsBackToFileService`/`_DatabaseEmpty_FallsBackToFileService`, plus the live Docker happy-path run confirming zero errors/warnings end to end |
| 13 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 14 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` (run repeatedly while implementing the reader and its tests, to rule out flakiness) |
| 15 | ✅ | The table-naming rule for an application with more than one database is stated in an ADR | Doc | ADR 015's "Revision — issue #309" section; `RepositoryStructureTests` passes (14/14, 2026-08-16) |
| 16 | ✅ | The changelog tables carry the `Changelog_` prefix per ADR 015's revision, in the migration, baseline, entities, `Sql.cs` and tests | Unit test | 14/14 changelog tests pass against the renamed schema (2026-08-16); full solution builds 0 Warning(s) 0 Error(s); full suite 3,445 passed |
| 17 | ✅ | The renamed schema works live: baseline applies, the importer writes, and `/about` reads from the database rather than the JSON fallback | Live (T2) | 2026-08-17 against `quotinator:t2` — see the T2 step above. Decisive evidence is the *absence* of the `falling back` warning, since the page renders either way |
| 18 | ✅ | Migration applies cleanly from the last released schema | Live (T2) | v1.8.3 → current: `data v3 → v11`, 0 exceptions, `schema is up to date` on restart (ADR 009) |
| 19 | ✅ | The changelog database survives process uptime | Live (T2) | `docs/smoke-tests.md` section 44. Container with a mapped data dir: `quotinatorchangelog.db` present on disk beside `quotinatordata.db`, `[Changelog - Import] refreshed 126 entries across 3 language(s)`, and after >15 min uptime the endpoint still serves content with a JSON-fallback line count of 0. Previously failed at +13 min with `no such table: Changelog_Entry` and a permanent fallback |
| 20 | ✅ | The changelog database is a file, not an in-memory instance | Unit test | `ChangelogDatabaseWiringTests.ChangelogDatabase_IsNotAnInMemoryDatabase` and `.ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase` — both red against the previous wiring |
| 21 | ✅ | T1 — `/about` renders correctly from the renamed tables, reading the on-disk changelog database, in Visual Studio | Live (T1) | 2026-08-19, `localhost:44368/about` under Dutch culture: the Wijzigingslog renders the unreleased section and the v1.8.3 release with its quote, and the machine-translation notice. Changelog schema created at baseline, `refreshed 126 entries across 3 language(s)`, `/about` loaded afterwards |
| 22 | ✅ | A database-backed read states positively that the database served it, rather than being inferred from the absence of a fallback warning | Unit test | `ChangelogReaderTests.GetDocumentAsync_DatabasePopulated_LogsThatTheDatabaseServedIt` — red before, since no such line existed |
| 23 | ✅ | A read arriving before the import has concluded waits for it, instead of reading emptiness as failure and falling back | Unit test | `ChangelogReaderTests.GetDocumentAsync_ImportStillRunning_WaitsForItRatherThanFallingBack` — verified by mutation (see step 16) |
| 24 | ✅ | An empty database after a *successful* import is authoritative — no fallback, no warning, since a new application legitimately has no changelog | Unit test | `ChangelogReaderTests.GetDocumentAsync_DatabaseEmptyAfterSuccessfulImport_DoesNotFallBack` |
| 25 | ✅ | A genuinely failed import falls back and warns | Unit test | `ChangelogReaderTests.GetDocumentAsync_ImportFailed_FallsBackAndWarns` |
| 26 | ✅ | Giving up waiting is reported as its own condition, never as an import failure | Unit test | `ChangelogReaderTests.GetDocumentAsync_WaitTimesOut_FallsBackWithItsOwnMessage` |
| 27 | ✅ | Live: the startup read is served by the database, not the fallback | Live (T1) | 2026-08-19 22:15 — no `falling back` line anywhere in the boot; `served 615 row(s)` at startup and `served 2292 row(s)` for `/about`. The race from 21:40:51 is closed |
| 28 | ✅ | A refresh is atomic — a failure partway leaves the previous content intact, so no reader can observe a half-rebuilt changelog | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_FailsPartway_LeavesThePreviousContentIntact` — red on the assertion (`expected: 4, actual: 2`) against the non-atomic shape |
| 29 | ✅ | Live: the startup read reports a complete count, not a partial one | Live (T1) | 2026-08-19 22:27 — `served 2292 row(s)` at startup and for every later read, against 615-vs-2292 at 22:15. Step 17's atomicity fix confirmed |
| 30 | ✅ | A read reflects this process's own import, never a previous run's content | Unit test | `ChangelogReaderTests.GetDocumentAsync_PreviousRunsContentStillPresent_ReturnsThisImportsContentInstead` — red against the previous shape, returning the stale `0.9.0` |
| 31 | ✅ | The read log reports entries, the same unit the importer reports, so the two lines are comparable | Unit test + Live | `LogChangelogServedFromDatabase` emits `served {EntryCount} entries`; smoke test 44 asserts the counts match |
| 32 | ✅ | Live: the startup read and the import report the same entry count, and the read is ordered after the import | Live (T1) | 2026-08-19 22:45, fresh database: `[Changelog - Init] schema created at baseline`, `refreshed 126 entries`, then `served 126 entries` on every read. No fallback line, no "holds no entries" line. Reversed from 22:27:21, where the read preceded the import |
| 33 | ✅ | Live: on the fast-startup path that previously got it wrong, the read is ordered after the import | Live (T1) | 2026-08-19 22:46, already-populated main database (`v3 → v11`, no seeding delay — the same profile as 22:27): `refreshed 126 entries` then `served 126 entries`, reversed from 22:27:21. **A log cannot distinguish "waited" from "did not need to wait"** — the read now awaits readiness before querying at all, so correct ordering is structurally guaranteed rather than observed. That distinction is unobservable live by construction; `GetDocumentAsync_PreviousRunsContentStillPresent_…` is the conclusive coverage. This row confirms the guarantee holds in the exact conditions that previously failed |
| 34 | ⬜ | Smoke test 44's rewritten assertion holds | Live (T2) | `docs/smoke-tests.md` section 44 — re-run needed, since the assertion it now makes did not exist when it was last run |

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
