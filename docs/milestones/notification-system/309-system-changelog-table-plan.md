# #309 — Move changelog content to database-backed System_Changelog table

**Status:** In progress (step 5)
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
schema-drift parity tests, and ADR 009 verification as the main database, reusing
`Quotinator.Data.Database.DatabaseInitializer`'s existing generic pattern rather than inventing a
parallel one — pointed at the changelog's own keyed connection factory instead of the main database's.
**Operational default stays simple in practice**: since the in-memory storage mode always starts
genuinely empty, `DatabaseInitializer`'s own existing "completely empty database → apply the
one-step baseline" rule (already how the main database bootstraps a fresh install) applies naturally
here too, every single boot — the incremental-migration path exists, is tested, and is ready for
whenever the schema actually needs to change (during development, or once a persistent-file variant
means real on-disk state can carry across restarts), without ever needing to be exercised by the
default in-memory mode's own steady-state behaviour.

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
    Id               TEXT NOT NULL PRIMARY KEY,
    Language         TEXT NOT NULL,
    Version          TEXT,                 -- NULL = that language's 'unreleased' entry
    Date             TEXT,                 -- NULL for 'unreleased'
    QuoteText        TEXT,
    QuoteAttribution TEXT,
    DateCreated      TEXT NOT NULL,
    DateModified     TEXT,
    DateDeleted      TEXT,
    IsDeleted        INTEGER NOT NULL DEFAULT 0
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
database gets its own instance of `Quotinator.Data.Database.DatabaseInitializer`'s generic
baseline+incremental machinery (own migration list starting at version 1, own baseline SQL, own
schema-version tracking table, own schema-drift parity test), pointed at the keyed changelog connection
factory instead of the main database's. `DatabaseInitializer`'s own existing "completely empty database
→ apply the one-step baseline" rule means the in-memory storage mode's steady-state behaviour is still
just "run the baseline, every boot" in practice (an in-memory database is always empty at startup by
construction) — the incremental path is exercised only when the schema actually needs to change (during
development) or a future persistent-file variant carries real on-disk state across restarts, but it
exists and is tested either way, not retrofitted later.

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

### 5. `ChangelogDatabaseInitializer` (own `DatabaseInitializer` instance, migration 1 + baseline SQL + schema-drift parity test), `ChangelogEntity`/`ChangelogLineEntity`, `ChangelogRepository : AggregateRepository<Changelog, ChangelogLine>`
**Status:** Not started

### 6. `ChangelogSystemContentImporter`
**Status:** Not started

### 7. `ChangelogLineRow` + `IJoinStrategy` + `IChangelogReader`/`ChangelogReader` — DB-first, JSON-fallback
**Status:** Not started

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
| 1 | ❌ | The keep-alive connection keeps the shared-cache in-memory database alive across multiple separately-opened connections | Unit test | `ChangelogInMemoryConnectionFactoryTests.MultipleConnections_ShareSameInMemoryDatabase` |
| 2 | ❌ | `ChangelogDatabaseInitializer`'s baseline and incremental replay produce identical schema (parity, matching the main database's own schema-drift test pattern) | Unit test | `ChangelogDatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalSchema` |
| 3 | ❌ | A genuinely empty (fresh) changelog database takes the one-step baseline path, matching `DatabaseInitializer`'s existing rule | Unit test | `ChangelogDatabaseInitializerTests.EmptyDatabase_AppliesBaseline` |
| 4 | ❌ | Importer writes one `Changelog` row per release + one per language's `unreleased`, with correctly-ordered `ChangelogLine` children | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_WritesReleasesAndOrderedLines` |
| 5 | ❌ | Re-running the importer overwrites (not duplicates) existing rows | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_RunTwice_OverwritesNotDuplicates` |
| 6 | ❌ | `IChangelogReader.GetDocumentAsync` returns DB-backed content, correctly reassembled (including `AudienceHighlights["notification"]`), when the database is populated | Unit test | `ChangelogReaderTests.GetDocumentAsync_DatabasePopulated_ReturnsReassembledContent` |
| 7 | ❌ | `IChangelogReader.GetDocumentAsync` falls back to `IChangelogService` (not an exception) when the tables don't exist | Unit test | `ChangelogReaderTests.GetDocumentAsync_TablesMissing_FallsBackToFileService` |
| 8 | ❌ | The fallback logs a warning explaining the condition, not silently | Unit test | `ChangelogReaderTests.GetDocumentAsync_TablesMissing_LogsWarning` |
| 9 | ❌ | A genuinely different SQL error (not "table missing") still propagates, not swallowed | Unit test | `ChangelogReaderTests.GetDocumentAsync_UnrelatedSqlError_Propagates` |
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
