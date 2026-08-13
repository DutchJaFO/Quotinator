# #309 — Move changelog content to database-backed System_Changelog table

**Status:** Planning
**GitHub issue:** #309 (open)
**Depends on:** #80 (done, released — Changelog handling milestone)

---

## Background

Implements [ADR 005](../architecture-decisions/005-quotinator-changelog-project-scope.md)'s revision
and [ADR 018](../architecture-decisions/018-system-content-in-quotinator-data.md)'s file-authored
system content pattern. Changelog content currently lives entirely in
`src/Quotinator.Api/resources/changelog.*.json`, compiled into the publish output — updating it
requires a code commit and a redeploy. This issue moves changelog content to a database-backed
`System_Changelog` table, refreshed at startup from the same JSON files relocated to a
runtime-accessible location, while keeping the existing JSON-based read path alive as a fallback.

**Scope addition (2026-08-13):** the About page is already on `DatabaseHealthGateMiddleware`'s
exempt-path list (`/about`) — expected to stay reachable during a degraded/unmigrated database. #293
already fixed the identical failure mode for `System_Notification` (a missing table crashing a page
that was supposed to degrade gracefully instead). Without an equivalent fix here, #309 would
reintroduce that exact bug for `System_Changelog`. The developer separately flagged that #293's own
fallback (silently show "no notifications") is *itself* too silent — but the general "detect and warn
about a broken database" mechanism is #305's job (already filed, v1.9.0 milestone), not something #309
should duplicate. This issue's own fallback logs clearly (matching #293's later-improved
`NotificationSeeding` message quality bar) and stays structurally compatible with whatever #305
eventually builds, without inventing a second, changelog-specific warning UI.

## Authoritative-source cross-check

- **ADR 018** — file-authored system content is `Quotinator.Data`-owned; the generic importer
  abstraction is this issue's own to design (deliberately deferred there, since designing it against
  one consumer risks getting it wrong before a second consumer — Genre — exists). `Quotinator.Data` may
  depend on `Quotinator.Changelog` (already dependency-isolated, no Quotinator-domain types).
- **ADR 005's revision** — the JSON files stay the authored source; they relocate to a
  runtime-accessible location; `Quotinator.Changelog` itself keeps zero direct database access — a
  separate `Quotinator.Data`-owned component does the reading/writing.
- **ADR 015** — `System_` domain prefix for this table (operational/system content, not quote-domain,
  not audit-trail, not import content).
- **ADR 002 / ADR 008** — `RecordBase` on the new table without exception; any enum-backed column gets
  a matching `CHECK` constraint from creation.
- **Migration numbering** — `DatabaseInitializer.DataOwnedMigrations` currently has versions 1–3
  (`AuditMigrations`, then #289's two squashed consolidations). This issue adds **version 4**.
- **`data/sources/` precedent** — bundled quote sources are read from
  `AppContext.BaseDirectory/data/sources/` (`Program.cs`), loose files copied into the publish output,
  not compiled resources. This is the exact shape to mirror for changelog content, not the mutable
  `{dataDir}` volume (which holds the *database*, not bundled read-only content) and not a fully
  external/mounted location — "no recompile needed" only requires escaping compiled resources, not
  escaping the image build.
- **#293's fallback precedent** — `NotificationReader.GetActiveNotificationsAsync`/`GetPagedAsync` wrap
  their query in `try/catch (SqliteException ex) when (IsMissingTableError(ex))`, matching both
  `SqliteErrorCode == 1` and the specific table name in the message — narrow enough that a genuinely
  different SQL error still propagates. This issue's reader follows the identical idiom.
- **`DatabaseHealthState`** (`Quotinator.Api.Startup`, `internal`) is unreachable from `Quotinator.Data`
  (wrong dependency direction, and internal to a different project) — the fallback cannot check it and
  must rely purely on the narrow exception-catch pattern above, same as #293.

No conflict found — proceeding with the design below.

---

## Design

### `System_Changelog` table

One row per `(Language, Version)` — `Version IS NULL` represents that language's `unreleased` entry
(at most one per language, refreshed/overwritten every startup rather than accumulated). Rich content
(`highlights`, `added`, `changed`, `fixed`, `removed`, `audienceHighlights`, `issues`, `cves`, `quote`)
is stored as one JSON blob column rather than normalized into child tables — this project's own
Simplicity priority doesn't justify a dozen join tables for read-mostly, low-volume release notes, and
storing it as `ChangelogRelease`/`ChangelogUnreleased`'s own JSON shape means deserializing a row
produces the exact same model type the JSON-fallback path already returns, so `#307`'s
`GetHighlightsFor(...)` works identically regardless of which path served the content.

```
CREATE TABLE IF NOT EXISTS System_Changelog (
    Id           TEXT NOT NULL PRIMARY KEY,
    Language     TEXT NOT NULL,
    Version      TEXT,                          -- NULL = that language's 'unreleased' entry
    Date         TEXT,                          -- NULL for 'unreleased'
    ContentJson  TEXT NOT NULL,                  -- serialized ChangelogRelease / ChangelogUnreleased
    DateCreated  TEXT NOT NULL,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_System_Changelog_Language_Version
    ON System_Changelog (Language, Version) WHERE Version IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UX_System_Changelog_Language_Unreleased
    ON System_Changelog (Language) WHERE Version IS NULL;
```

No enum-backed column here — `Language`/`Version` are open text, not a closed set — so ADR 008's
CHECK-constraint checklist doesn't apply to this table.

### File relocation

New `DataPaths.ChangelogFolder = "changelog"` constant (`Quotinator.Data`), alongside the existing
`SourcesFolder`/`ImportsFolder`. `changelog.*.json` files move from `src/Quotinator.Api/resources/` to
a new top-level `data/changelog/` folder (sibling to `data/sources/`), copied into the publish output
the same way `data/sources/` already is. `Program.cs`'s `IChangelogService` registration changes from
`Path.Combine(AppContext.BaseDirectory, "resources")` to
`Path.Combine(AppContext.BaseDirectory, "data", DataPaths.ChangelogFolder)`.

### Changelog system-content importer (concrete, not over-generalized)

A single `Quotinator.Data.Import.ChangelogSystemContentImporter` (or similar — exact name decided at
implementation) reads every `changelog.*.json` file via the existing `IChangelogService`, then
upserts one `System_Changelog` row per release (+ one per language's `unreleased` entry) into the
current connection. Run at startup, before/alongside the existing consumer-domain seeding step. Per
ADR 018, this is deliberately *not* built as a generic "system content importer" interface yet — this
issue is the pattern's first real consumer; a shared abstraction is extracted only once a second
consumer (Genre) actually needs one, avoiding a generic design validated against nothing.

### `IChangelogReader` — DB-first, JSON-fallback, one place owns both

New `Quotinator.Data.Repositories.IChangelogReader`/`ChangelogReader` (matching the
`INotificationReader`-style naming convention), constructor-injected with the existing
`IChangelogService` (as its fallback) plus the standard connection factory:

```
public async Task<ChangelogDocument?> GetDocumentAsync(string? language)
{
    try
    {
        // query System_Changelog for `language`, deserialize ContentJson rows into
        // ChangelogRelease/ChangelogUnreleased, assemble a ChangelogDocument
    }
    catch (SqliteException ex) when (IsMissingChangelogTableError(ex))
    {
        _logger.LogChangelogTableMissingFallingBackToFile(ex);   // WARNING, not silent — explains what
                                                                  // happened and that it doesn't mean
                                                                  // corruption, matching #293's own
                                                                  // improved message bar
        return _changelogService.GetForCulture(language);
    }
}
```

Both `About.razor` and #81's future producer read through `IChangelogReader`, not `IChangelogService`
directly — `IChangelogService` becomes purely the fallback's own implementation detail plus the
importer's own read-time source, no longer a page-facing dependency. This is a change to #81's plan doc
(update after this issue lands, not now).

**Explicitly out of scope:** a user-visible warning banner/notification when the fallback triggers.
That's #305's job (general DB integrity detection + warning, v1.9.0) — this issue only needs to not
crash and to log clearly enough that the condition is diagnosable, not to build a second, parallel
warning mechanism for changelog specifically.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

### 2. `DataPaths.ChangelogFolder`, relocate JSON files, update `Program.cs`/`.csproj`
**Status:** Not started

### 3. `Quotinator.Data` → `Quotinator.Changelog` project reference
**Status:** Not started

### 4. `System_Changelog` table: migration (version 4), baseline SQL, schema-drift test extension
**Status:** Not started

### 5. `ChangelogSystemContentImporter`
**Status:** Not started

### 6. `IChangelogReader`/`ChangelogReader` — DB-first, JSON-fallback
**Status:** Not started

### 7. Wire `About.razor` to `IChangelogReader`
**Status:** Not started

### 8. Tests
**Status:** Not started

Real-SQLite tests for the importer and reader (matching this project's DB-integration-test
requirement for seeder code); a reproduction of #293's own pattern — build a database with no
`System_Changelog` table, confirm `IChangelogReader` falls back to the JSON path instead of throwing.

### 9. Full verification (T1, T2)
**Status:** Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `System_Changelog` migration creates the table; baseline and incremental replay produce identical schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangelogSchema` |
| 2 | ❌ | Importer writes one row per release + one per language's `unreleased` entry | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_WritesOneRowPerReleaseAndUnreleased` |
| 3 | ❌ | Re-running the importer overwrites (not duplicates) existing rows | Unit test | `ChangelogSystemContentImporterTests.RefreshAsync_RunTwice_OverwritesNotDuplicates` |
| 4 | ❌ | `IChangelogReader.GetDocumentAsync` returns DB-backed content when `System_Changelog` exists and is populated | Unit test | `ChangelogReaderTests.GetDocumentAsync_TablePopulated_ReturnsDbContent` |
| 5 | ❌ | `IChangelogReader.GetDocumentAsync` falls back to `IChangelogService` (not an exception) when `System_Changelog` doesn't exist | Unit test | `ChangelogReaderTests.GetDocumentAsync_TableMissing_FallsBackToFileService` |
| 6 | ❌ | The fallback logs a warning explaining the condition, not silently | Unit test | `ChangelogReaderTests.GetDocumentAsync_TableMissing_LogsWarning` |
| 7 | ❌ | A genuinely different SQL error (not "table missing") still propagates, not swallowed | Unit test | `ChangelogReaderTests.GetDocumentAsync_UnrelatedSqlError_Propagates` |
| 8 | ❌ | `About.razor` renders correctly via `IChangelogReader` | Live (T1) | Developer confirms in Visual Studio |
| 9 | ❌ | `About.razor` still renders (fallback path) when `System_Changelog` is missing, matching #293's degraded-state precedent | Live (T2) | Docker: rename/drop `System_Changelog` on a running container, confirm `/about` still returns `200` with content, not a crash |
| 10 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 11 | ❌ | Full test suite green | Build | `dotnet test --configuration Release` |

---

## Relationship to existing issues

- **#80** — the changelog system this issue moves storage for.
- **#278** — precedent for the `System_*` table shape and `RecordBase`/migration conventions.
- **#293** — the exact bug-class (missing-table crash on an exempt degraded-state path) this issue must
  not reintroduce; its narrow-exception-catch idiom is reused directly.
- **#305** — owns general DB-integrity detection and user-visible warnings; this issue's fallback stays
  structurally compatible with it but does not duplicate it.
- **#307** — already shipped; `ChangelogReservedAudience`/`GetHighlightsFor(...)` work unchanged
  against DB-sourced or JSON-fallback-sourced `ChangelogRelease`/`ChangelogUnreleased` objects alike.
- **#81** — hard dependency on this issue; its plan doc needs updating (after this issue lands) to read
  via `IChangelogReader` instead of `IChangelogService` directly.
