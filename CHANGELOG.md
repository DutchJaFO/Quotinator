##### *GENERATED FILE [2026-06-22 18:11 UTC] — do not edit by hand.*

# Changelog

All notable changes to Quotinator are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Highlights
- The language selector has moved from the navbar into a new Settings section in the side menu, fixing an overlap with the hamburger button on mobile.

### Changed
- Language selector redesigned from a drop-down to individual nav items with a checkmark on the active language
- New Settings section added to the nav menu as the home for language selection and future settings
- Generated changelog files now show a minimal timestamp-only header by default; pass `--debug` to `changelog.csx` to include the input path and regenerate command

### Fixed
- On mobile, the hamburger toggle and language selector overlapped in the navbar (issue #103)

---

## [1.6.0] - 2026-06-22

### Highlights
- Changelog is now driven by a single JSON source — both markdown files are generated from it, and the Blazor changelog page reads it directly with no markdown parsing.
- Unreleased changes are now shown at the top of the changelog page.
- Each release now shows which GitHub issues and CVEs it addresses in a collapsible section.

### Added
- `Quotinator.Changelog` project: `IChangelogService` / `ChangelogService` — deserialises `changelog.json`; no markdown parser
- `ChangelogEntry` Blazor control: renders a single release entry (highlights or technical sections, GitHub release link, Issues & CVEs block)
- `ChangelogUnreleasedEntry` Blazor control: renders the unreleased block — always expanded, no version or date, no GitHub release link
- `ChangelogRoot`, `ChangelogUnreleased`, `ChangelogRelease`, `ChangelogReleaseTranslation`, `ChangelogTranslationItem`, `ChangelogSectionHeaders` models shaped directly by the JSON schema; `ChangelogUnreleased` is the base, `ChangelogRelease` inherits and adds `Version` and `Date`
- `ChangelogSchemaTests`: validates `changelog.json` structure (required fields, no null entries, CVE ID format)
- `schemas/changelog.schema.json`: machine-readable JSON schema for `changelog.json`
- `scripts/changelog.csx`: generates `keepachangelog` and `ha-addon` markdown from `changelog.json`; supports `--lang`, `--audience`, `--fallback`, `--fallback-message`
- `scripts/changelog-import.csx`: imports an existing `keepachangelog` or `ha-addon` markdown file into `changelog.json` format
- `scripts/changelog-upgrade.csx`: one-time migration tool used to assemble `changelog.json` from the two reference markdown files

### Changed
- `ChangelogService` now deserialises `changelog.json` directly into public models; private DTO mapping layer removed
- `ChangelogSection` model removed; Blazor components render the four change lists (`Added`, `Changed`, `Fixed`, `Removed`) directly
- Single-highlight releases now render as a paragraph instead of a bullet list
- `CHANGELOG.md` and `addon/CHANGELOG.md` are now generated files — edit `changelog.json`, then run `scripts/changelog.csx` to regenerate
- `Scalar.AspNetCore` updated from 2.16.3 to 2.16.4
- `actions/checkout` updated from v6 to v7 (CI only)
- `JsonSchema.Net` updated from 8.0.0 to 9.2.2 (test only)
- `MSTest` updated from 4.0.2 to 4.2.3 (test only)
- `Microsoft.Extensions.Logging.Abstractions` updated from 10.0.0 to 10.0.9

### Fixed
- `scripts/changelog-upgrade.csx`: versions present in root `CHANGELOG.md` but absent from `addon/CHANGELOG.md` now correctly use root highlights as generic highlights; v1.0.0 and v1.0.1 were affected
- `scripts/changelog.csx --fallback false`: standard highlights no longer leaked to absent-audience entries
- `changelog.json` v1.0.0 and v1.0.1 were missing `highlights` — both now show user-facing summaries instead of technical section badges

---

## [1.5.1] - 2026-06-20

### Highlights
- Fixed: startup banner was collapsed to a single line in the supervisor log.
- Fixed: admin API key and other options were not being read from the add-on config — the app now reads `/data/options.json` directly, which is the source the supervisor writes.

### Added
- Diagnostic log lines at startup showing which `Quotinator__*` environment variables are present (API key value is never logged — only whether it is set).

### Fixed
- Startup banner was collapsed to a single line in the HA supervisor log — restored `Console.WriteLine` so newlines are preserved.
- Admin API key and other add-on options were not being read from `/data/options.json`; the `env_vars` template mechanism is unreliable for optional options. The app now reads `/data/options.json` directly at startup.

---

## [1.5.0] - 2026-06-20

### Highlights
- Admin endpoints (reseed, reset) now require an API key via the `X-Api-Key` request header — set `admin_api_key` in the add-on options.
- The Scalar API reference shows an Authentication panel — enter your key once and it is sent automatically on all admin requests.
- The startup log now shows whether an admin API key is configured (`set` / `not set`).
- The REST API page in the UI shows the admin endpoints when a key is active.
- Fixed: admin endpoint documentation incorrectly stated `Authorization: Bearer` — the correct header is `X-Api-Key`.
- Fixed: `appsettings.local.json` was included in the Docker image, allowing a local developer override to silently take priority over the `admin_api_key` env var set by the HA supervisor.

### Added
- `X-Api-Key` OpenAPI security scheme: admin endpoints are tagged in the spec so Scalar shows an Authentication panel pre-filled with the header name.
- Admin API key status (`set` / `not set`) added to the startup banner.
- `appsettings.local.template.json` — committed template for local development overrides (copy to `appsettings.local.json`, gitignored).
- User Secrets support enabled on `Quotinator.Api` (`UserSecretsId: quotinator-api-dev`) — use VS "Manage User Secrets" as an alternative to the local settings file.
- `docs/running-locally.md` documents both local config approaches and how to test admin endpoints in Scalar.
- Startup banner reformatted: each config setting on its own line for easier reading in logs.

### Fixed
- Admin endpoints were documented as requiring `Authorization: Bearer <key>` but the correct header is `X-Api-Key: <key>`.
- The startup banner was not visible in Visual Studio — it now goes through the logger so it appears in the Output window alongside other log messages.
- `appsettings.local.json` was being included in the Docker image via `COPY . .`, allowing a local developer override to silently win over environment variables (including the HA `admin_api_key` env var). Added to `.dockerignore`.
- Intermittent test failure under parallel execution: three test classes each called `SqlMapper.AddTypeHandler` in `[ClassInitialize]`, causing a race on the global static handler dictionary. Registrations moved to `[AssemblyInitialize]` so they run once before any tests start.

---

## [1.4.3] - 2026-06-20

### Highlights
- Internal improvements in preparation for upcoming data import features.

### Added
- `ImportBatches` table tracks the origin of every seeded or imported record (`Id`, `Name`, `Type`, `Url`, `ImportedAt`, `ImportedBy`, `RecordCount`)
- Nullable `ImportBatchId` foreign key added to `Quotes`, `Sources`, `Characters`, and `People` — links each record to the import batch that introduced it
- `IImportBatchRepository` / `SqliteImportBatchRepository` in `Quotinator.Core` with `GetAllAsync`, `GetByTypeAsync`, `UpdateRecordCountAsync`
- Seeder creates one `ImportBatch` row per source file and writes `ImportBatchId` on all inserts for that file
- Migration003: upgrades existing databases with the new table and columns; inserts placeholder `Seed`-type rows for the two bundled external datasets (existing records keep `NULL` ImportBatchId — provenance not captured retroactively)

---

## [1.4.2] - 2026-06-20

### Highlights
- Fixed: the Docker image was incorrectly reporting version 1.0.0 — the actual version is now shown correctly.
- The REST API page now includes a direct link to the version endpoint.
- Internal improvements — no other user-facing changes.

### Added
- Version endpoint row (`api/v1/version`) on the REST API page; translated in English, Dutch, and German

### Changed
- `Quotinator.Constants` source files moved from project root into namespace-matched subfolders: `Api/` (`ApiMessages`, `ApiTags`), `Routes/` (`ApiRoutes`, `RouteExtensions`), `RateLimiting/` (`RateLimitPolicies`)
- `Quotinator.Data.Data` namespace renamed to `Quotinator.Data.Connections`

### Fixed
- Docker image embedded `1.0.0` as the API version — `Directory.Build.props` was not included in the build context; now copied before the restore layer so the correct version is embedded at publish time
- Docker build layer switched from per-project `COPY` statements to `COPY . .` to prevent files being silently omitted when new projects or root SDK files are added

---

## [1.4.1] - 2026-06-20

### Highlights
- Fixed: the changelog page now shows plain-English release summaries for all versions instead of technical details.
- Internal improvements in preparation for upcoming data import features.

### Added
- `IUnitOfWork` and `SqliteUnitOfWork` — transaction and shared-connection support across repository operations; required by the upcoming import endpoint (#45) for atomic batch inserts
- Repository methods (`IRepository<T>`, `IRestorableRepository<T>`) accept an optional `IUnitOfWork?` parameter; all existing callers require no changes

### Fixed
- `### Highlights` sections for 1.4.0 and 1.3.0 were not displaying in the Blazor changelog page — 1.4.0 used prose instead of a bullet list (silently ignored by the parser); 1.3.0 contained technical terms. Both rewritten in plain user-facing English.

---

## [1.4.0] - 2026-06-20

### Highlights
- Security: a database query vulnerability (CVE-2025-6965) was identified and mitigated; no user data was affected.

### Added
- `seed.csx --output-dir DIR` flag redirects seed output to a custom directory, making it possible to test seeding without overwriting `data/sources/`
- Generic repository infrastructure: `IRepository<T>` and `SqliteRepository<T>` (base CRUD), `IRestorableRepository<T>` and `SqliteRestorableRepository<T>` (opt-in soft-delete management — get deleted, restore, hard-delete, purge)
- `GuidHandler` Dapper type handler — forces `Guid` storage as uppercase TEXT to match Microsoft.Data.Sqlite's default, fixing a case-sensitivity mismatch that silently dropped results from parameterised queries
- ADR 002 documenting the RecordBase-everywhere decision — all tables carry audit columns; junction tables use a synthetic GUID primary key with a UNIQUE constraint on the natural key pair
- `README.md` in each reserved-but-empty folder (`docs/architecture-decisions/`, `docs/milestones/`, `docs/milestones/data-import-sources/`, `docs/workflow/`, `scripts/cache/`, `src/Quotinator.Core/Configuration/`) explaining the folder's purpose and expected contents
- `Directory.Build.props` suppressing NU1903 (CVE-2025-6965) globally with a rationale comment
- `Quotinator.Core.Data.Sql` static class centralising every SQL statement as named constants or factory methods; all inline SQL removed from `DatabaseInitializer` and `SqliteQuoteService`
- `SqlQueryGuardTests` — reflection-based test covering all `Sql.*` constants and all dynamic query factory methods with a full filter matrix; replaces the fragile file-scan approach
- `RepositorySql` class in `Quotinator.Data.Repositories` — centralises the six `SqliteRepository`/`SqliteRestorableRepository` template queries (previously inline); `SqlAggregateGuard.HasAggregateFunction` public helper exposes the aggregate-detection regex for use in tests
- `AggregateQueries_MatchDocumentedInventory` test — documents and locks the exact set of SQL constants that contain aggregate functions; fails when new aggregate queries are added without review
- `AllSqlStringLiterals_AreInCentralisedFiles` scan test — enforces that no C# source file outside `Sql.cs`, `RepositorySql.cs`, and `DatabaseInitializer.cs` contains DML string literals; closes the CVE-2025-6965 DoD item 1 gate
- String centralisation policy documented in `CLAUDE.md` — no inline strings for SQL, UI, or error messages; audit grep commands and test gates included

---

## [1.3.0] - 2026-06-17

### Highlights
- Quote data now loaded from multiple source files in `data/sources/` — one file per bundled dataset, plus optional user imports in the `imports/` subfolder of the data directory
- New `GET /api/v1/admin/database/seed/preview` endpoint shows what would be imported without writing to the database (requires `admin_api_key`)
- `Quotinator__DataDir` env var replaces `Quotinator__DataPath` — set to the data directory, not a file path

### Added
- `data/sources/` folder with one JSON file per dataset and a `manifest.json` controlling import order and duplicate-resolution policy
- `data/sources/quotinator-curated.json` — manually curated and verified quotes (Airplane! to start)
- Optional user imports: place `.json` files in `{dataDir}/imports/` to seed additional quotes on startup
- Duplicate-resolution policy per batch: `skip` (default) or `overwrite`; configurable in `manifest.json` or via `Quotinator:DuplicateResolution:*` env vars
- `GET /api/v1/admin/database/seed/preview` — dry-run scan returning file counts, quote counts, and cross-file duplicates (requires `AdminApiKey`)
- Reseed and reset responses now include a `duplicates` count
- Six JSON schemas in `schemas/` covering all source file formats and the manifest

### Changed
- `Quotinator__DataDir` env var replaces `Quotinator__DataPath` — value is the data directory, not a file path

### Fixed
- Seeder FK constraint failure on first startup caused by a Guid case mismatch between Dapper.Contrib (UPPERCASE) and raw SQL parameters (lowercase); SQLite text comparison is case-sensitive

---

## [1.2.2] - 2026-06-16

### Highlights
- Fixed: the GitHub changelog link in the add-on UI opened inside the HA frame and was blocked — it now opens in a new tab correctly

### Fixed
- GitHub release link on the About/Changelog page now opens in a new tab (`target="_blank"`) so it escapes the HA ingress frame; without this, GitHub's `X-Frame-Options` policy caused a browser error instead of the page loading

---

## [1.2.1] - 2026-06-16

### Highlights
- Database file renamed from `quotes.db` to `quotinatordata.db` — the old file is renamed automatically on first startup after upgrade, no data loss
- A backup of the database is created before any schema migration and stored in the `backups/` subfolder of the data directory; old backups are safe to delete
- New optional `backup_path` config option to store backups in a custom location
- DataProtection keys folder renamed from `.keys` to `keys`
- Container log output now uses single-line format — easier to read in the HA log panel
- Startup banner shows all data paths at a glance

### Added
- Pre-migration database backup using the SQLite backup API — written to `{dataDir}/backups/quotinatordata_v{N}_{timestamp}Z.db` before any schema version bump; old backups are safe to delete
- Configurable backup directory via `Quotinator:BackupPath` env var (defaults to `{dataDir}/backups`)
- HA add-on: optional `backup_path` config option with translations in English, Dutch, and German
- `DataPaths` constants class (`Quotinator.Core.Data`) for all file and folder names in the data directory — named and documented so future changes stay in one place
- Data directory structure documented in `README.md` and `addon/DOCS.md` with safe-to-delete guidance for each folder

### Changed
- Database file renamed from `quotes.db` to `quotinatordata.db`; the legacy file (including WAL and SHM journal files) is renamed automatically on first startup after upgrade
- DataProtection keys folder renamed from `.keys` to `keys` — visible name is easier to discover and document
- Console log formatter switched from `simple` (two lines per entry) to `systemd` (single line per entry) for cleaner container log output
- Startup banner rewritten with full readable labels (`Database:`, `Backups:`, `DataProtection:`, `Config:`) aligned to a single column; no abbreviations

---

## [1.2.0] - 2026-06-16

### Highlights
- Admin endpoints (reseed, reset) now require `Authorization: Bearer <key>` — they return 401 by default until `admin_api_key` is set in the add-on configuration
- New `admin_api_key` option in the add-on configuration panel

### Added
- `AdminApiKeyFilter` endpoint filter guards `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` with a static API key
- `Quotinator:AdminApiKey` config key (env var `Quotinator__AdminApiKey`) — must be set explicitly to enable the endpoints; no bundled default
- Key comparison uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks
- HA add-on: optional `admin_api_key` config option with translations in English, Dutch, and German

---

## [1.1.0] - 2026-06-15

### Highlights
- Filter quotes by multiple genres or types at once (e.g. sci-fi comedies) using repeatable query parameters on `/random`, `/search`, and the paginated list
- Sci-fi and non-fiction genres are now correctly matched in all endpoints — they were silently missing from results before this fix
- `/random` now always returns a response envelope with status, items, total matching count, and an optional message; the shape of the response has changed
- New admin endpoints: `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` for database maintenance without restarting the container

### Added
- `/random` now accepts multi-value `type` and `genre` filters (repeatable query params, OR logic within each, AND between them), plus `character`, `author`, and `source` text-contains filters (AND logic with other params)
- `/search` and `/` (paginated list) now accept multi-value `type` and `genre` filters with OR logic
- `FilteredQuoteResult<T>` response envelope on `/random` — always returned, includes `status` (enum), `items`, `totalMatching` (pool size before random selection), and `message` (non-null for non-Ok statuses)
- `FilteredResultStatus` enum: `Ok`, `NoResults`, `InvalidType`, `InvalidGenre`, `InputTooLong`, `InvalidInput`
- `ValidGenres` set added to `InputValidation`; `IsSuspiciousInput` regex detects common SQL injection patterns and surfaces them as `InvalidInput` status (parameterised queries already prevent actual injection)
- `ValidGenres` and `IsSuspiciousInput` tests added to `InputValidationTests`
- `POST /api/v1/admin/database/reseed` — clears all data and reimports from `quotes.json`; schema migration history is preserved
- `POST /api/v1/admin/database/reset` — clears data and schema version history, reapplies all migrations, then reimports from `quotes.json`; equivalent to a fresh database
- `GenreApiToDb` contract tests in `InputValidationTests`: every valid genre has a mapping, every mapped value is a valid enum name, hyphenated genres (`sci-fi`, `non-fiction`) map correctly

### Changed
- **Breaking:** `/random` response shape changed from a single quote object or array to the `FilteredQuoteResult` envelope in all cases
- Genre normalisation in `SqliteQuoteService` now correctly maps `sci-fi` → `SciFi` and `non-fiction` → `NonFiction` for DB queries, and reverse-maps on read (previously hyphenated genres were not matched correctly)
- Unknown `type`/`genre` values on `/random` now return a 200 `InvalidType`/`InvalidGenre` envelope instead of 400; on `/search` and `/` they silently match nothing

### Fixed
- `SqliteQuoteService.ToResponse` was serialising `Genre.SciFi` as `"scifi"` and `Genre.NonFiction` as `"nonfiction"`; both now return the correct API tag strings (`"sci-fi"`, `"non-fiction"`)
- `DatabaseInitializer` silently dropped `sci-fi` and `non-fiction` genres during seeding because `Enum.TryParse("sci-fi")` fails on hyphenated strings — `TryNormaliseGenre` now maps through `InputValidation.GenreApiToDb` before parsing; `Migration002` truncates the affected `QuoteGenres` rows so existing databases are automatically re-seeded on next startup; `GenreApiToDb` is moved from private in `SqliteQuoteService` to public in `InputValidation` so both consumers share one definition

---

## [1.0.15] - 2026-06-15

### Highlights
- Fixed: antiforgery token errors after container restart — DataProtection keys are now reliably written to the persistent volume (`/data/.keys/`) even when the supervisor serves a cached config that omits `Quotinator__DataPath`
- Fixed: the OpenAPI UI link in the sidebar opened in the system browser when tapped in the HA companion app, losing the session and causing a 404; it now navigates within the companion app's webview

### Fixed
- DataProtection keys written to ephemeral container filesystem when `Quotinator__DataPath` env var is absent in HA (e.g. due to supervisor config cache) — `Program.cs` now falls back to `/data` (the HA persistent volume mount point) before the `/app/data` default, so keys are always on a persistent volume and antiforgery tokens survive container restarts
- OpenAPI UI and spec links in `Home.razor` had `target="_blank"`, which the HA companion app forwarded to the system browser (no HA session) — removing `target="_blank"` keeps navigation within the companion app's webview where the session is active

---

## [1.0.14] - 2026-06-15

### Highlights
- Internal: endpoint tests no longer create or touch a database — no impact on add-on behaviour

### Changed
- `DatabaseInitializer` now implements `IDatabaseInitializer`; `Program.cs` registers and resolves via the interface
- Endpoint tests register `NoOpDatabaseInitializer` alongside `FakeQuoteService` — no database is created or seeded during tests that have no intent to exercise the database layer

---

## [1.0.13] - 2026-06-15

### Highlights
- Internal: fixed a race condition in database seeding that caused test failures under parallel test execution — no impact on add-on behaviour

### Fixed
- Race condition in `DatabaseInitializer.SeedIfEmptyAsync`: parallel `WebApplicationFactory` instances (parallel MSTest runs with `ExecutionScope.MethodLevel`) could both observe an empty database and attempt concurrent seeding, causing a `UNIQUE constraint failed: Sources.Title, Sources.Type` error — fixed with a static `SemaphoreSlim` that serialises seed attempts within the same process

---

## [1.0.12] - 2026-06-15

### Highlights
- Quotes are now stored in a SQLite database (`quotes.db`) on the persistent volume — no action required; the database is created and seeded automatically on first run
- Startup log now shows database status: schema version, seeding progress, and a final count of quotes, sources, characters, and people
- Version endpoint (`GET /api/v1/version`) now returns the database schema version and record counts alongside the API version
- Fixed: startup banner and version endpoint incorrectly reported version `1.0.0` instead of the actual add-on version
- Fixed: Docker image build failed when `Quotinator.Data` was introduced — Dockerfile COPY layers corrected

### Added
- SQLite backend (v2): replaces flat-file `QuoteService` with `SqliteQuoteService` backed by Dapper + `Microsoft.Data.Sqlite` (closes [#7](https://github.com/DutchJaFO/Quotinator/issues/7))
- Fully normalised schema: `Sources`, `SourceTranslations`, `Characters`, `CharacterTranslations`, `People`, `Quotes`, `QuoteTranslations`, `QuoteGenres` — all tables include `RecordBase` audit columns (`DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`) for soft-delete support
- `SchemaVersion` table with numbered migration support — pending migrations are applied automatically at startup; existing migrations are never edited
- First-run seeding: if the database is empty on startup, all 780 quotes are imported from the bundled `quotes.json` automatically; no manual migration step required
- `SafeValue<T>` — diagnostic wrapper that carries both `Raw` (original DB string) and `Parsed` (converted value); corrupt or unrecognised values never crash the application and the original string is preserved for diagnosis
- `SafeEnumHandler<T>` and `SafeDateHandler` — Dapper TypeHandlers; enum values stored as TEXT names (rename-safe), date fields support imprecise ISO 8601 (`"1994"`, `"1994-06"`, `"1994-06-04"`)
- `QuoteType` and `Genre` enums with `Unknown = 0` as a safe zero-value fallback
- `People` table: tracks real people (authors, public figures) with optional `DateOfBirth` / `DateOfDeath` in imprecise ISO 8601 format
- Characters scoped to their source — same character name from different franchises is stored as separate rows
- `Quotinator.Data` project: reusable data infrastructure (`RecordBase`, `SafeValue<T>`, `IDbConnectionFactory`, `SqliteConnectionFactory`, `SafeEnumHandler<T>`, `SafeDateHandler`) extracted into its own class library
- Database startup logging: log lines for schema creation/update, seeding progress, and a final summary (quote / source / character / people counts)
- XML `<summary>` documentation on all public types and members; CS1591 enforced in `Quotinator.Core` and `Quotinator.Data`
- Version endpoint (`GET /api/v1/version`) now returns `database.schemaVersion` and row counts (`quotes`, `sources`, `characters`, `people`)
- Startup banner now includes a `DB:` line with schema version and row counts
- `SOURCES.md`: added attribution for Dapper, Dapper.Contrib, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Logging.Abstractions`; added DB Browser for SQLite under external tools

### Fixed
- Version endpoint and startup banner reported `1.0.0` instead of the actual release version — `VersionService` was reading the `Quotinator.Core` assembly (no `<Version>` set) instead of the entry assembly; changed to `Assembly.GetEntryAssembly()`
- Docker build failed: `Quotinator.Data` project was missing from the Dockerfile COPY layers

---

## [1.0.11] - 2026-06-14

### Highlights
- Config panel options now show translated names and descriptions in English, Dutch, and German
- Documentation tab: access section now clearly distinguishes ingress (default, sidebar) from direct port (optional, for external tools); misleading hardcoded URL removed

### Added
- HA add-on config panel now shows translated option names and descriptions in English, Dutch, and German (`addon/translations/`)
- GitHub Milestones created to track upcoming work; README roadmap section replaced with a link to the milestone list

### Changed
- Architecture: extracted `Quotinator.Constants` project (route strings, tag names, error message keys — no dependencies); moved `ApiLocalizer`, `VersionService`, and `InputValidation` to `Quotinator.Core`
- HA add-on documentation revised: access section now distinguishes ingress (default, no port config needed) from direct port (optional, for external tools); hardcoded `http://<ha-host>:8080/` URLs removed

### Fixed
- Stale `UI.en.json` references in `docs/localisation.md` and test comments corrected to `UI.en-GB.json` (the actual baseline file)

---

## [1.0.10] - 2026-06-14

### Highlights
- Startup banner prints as a single block (one timestamp) with a config summary: log level, request logging state, SSL state, and cert paths when SSL is enabled

### Changed
- Startup banner now prints as a single log entry (one timestamp) instead of one line per log call
- Banner config summary added: `log_level`, `log_requests`, `ssl` state, and cert/key paths when SSL is enabled

---

## [1.0.9] - 2026-06-14

### Highlights
- New `log_requests` option — logs one line per quote API request; useful for confirming calls arrive without enabling full debug logging (default: `false`)
- New `log_level` option — choose log verbosity from the add-on panel (default: `info`; use `debug` when reporting issues)
- UTC timestamps on all log lines
- Clear start and stop markers in the supervisor log (version, data path, quote count)

### Added
- `log_requests` add-on option — when enabled, logs one line per quote API request (`GET /api/v1/quotes/random?n=5 → 200 in 12ms`); default `false`; 429 responses are included, health/Blazor/static traffic is not
- `log_level` add-on option — controls verbosity of the HA supervisor log; valid values: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`; default `info`
- UTC timestamps on all log lines (`yyyy-MM-dd HH:mm:ss`)
- Startup banner logs version, data path, quote count, and keys directory on every start
- Shutdown message logged when the container stops

### Changed
- `Console.WriteLine` startup output replaced by the structured logger — all output now respects the configured log level and timestamp format

---

## [1.0.8] - 2026-06-14

### Highlights
- Fixed: links on the home page (Scalar UI, OpenAPI spec, health check) did not resolve correctly through the Home Assistant ingress — they now use relative paths

### Fixed
- Scalar UI button, OpenAPI spec link, and health check link on the home page were broken under the Home Assistant ingress — absolute paths (e.g. `/scalar/v1`) ignored `<base href>` and resolved against HA's server root; changed to relative paths so they resolve correctly through the ingress proxy in all deployment scenarios (closes [#8](https://github.com/DutchJaFO/Quotinator/issues/8))

---

## [1.0.7] - 2026-06-14

### Highlights
- Quotes and DataProtection keys are now stored on the supervisor-mounted persistent volume (`/data`) and survive add-on restarts and updates — no manual data migration needed on first install
- Fixed: Blazor assets (CSS, `blazor.web.js`) failed to load in the Home Assistant ingress panel — the sidebar page was broken and the "New quote" button did not work
- Fixed: antiforgery decryption failures after add-on restart — DataProtection keys now persist across restarts
- Fixed: language cookie endpoint no longer appears in the Scalar API reference

### Added
- First-run seeding: if `Quotinator__DataPath` points to an empty directory (e.g. a fresh HA add-on data volume), the bundled `quotes.json` is automatically copied there on startup so the add-on works immediately without manual setup

### Fixed
- Blazor assets (CSS, `blazor.web.js`) failed to load through the Home Assistant ingress panel — the hardcoded `<base href="/" />` caused relative URLs to resolve against HA's own server root instead of the ingress proxy path; fixed by reading `X-Ingress-Path` from the HA supervisor and setting it as the ASP.NET Core `PathBase`, which `<base href>` now reflects dynamically
- DataProtection keys were not persisted across HA add-on restarts — `Quotinator__DataPath=/data/quotes.json` points the app at the supervisor's persistent volume (`map: data:rw`) so keys survive restarts and updates; antiforgery decryption failures and Blazor circuit descriptor mismatches are resolved
- Kestrel double-bind warning: `ASPNETCORE_HTTP_PORTS: "8099"` in `addon/config.yaml` clashed with the Kestrel code that also binds port 8099; removed the environment block so port binding is owned entirely by the application
- DataProtection keys are now written to a `.keys/` subdirectory within the data directory
- `/Culture/Set` (language cookie endpoint) no longer appears in the Scalar API reference — it is a Blazor UI helper, not a REST API endpoint

---

## [1.0.6] - 2026-06-14

### Highlights
- SSL / HTTPS support on the direct-access port — set `ssl: true` and supply cert/key filenames (relative to `/ssl/`); defaults to disabled
- Ingress now correctly detects the browser's HTTPS context via `X-Forwarded-Proto`
- Fixed: Blazor interactive components (e.g. the "New quote" button) did not work in Docker or the HA add-on
- Language selection cookie now works in plain-HTTP direct-access deployments

### Added
- Optional HTTPS on the direct-access port (8080) via Kestrel; configure with `Quotinator__Ssl=true`, `Quotinator__SslCertFile`, and `Quotinator__SslKeyFile` environment variables
- HA add-on SSL options (`ssl`, `certfile`, `keyfile`) — defaults to `ssl: false`; when enabled, uses certs from `/ssl/` (HA Let's Encrypt add-on writes there)
- `UseForwardedHeaders()` middleware reads `X-Forwarded-Proto` and `X-Forwarded-For` from upstream proxies so the app knows it is behind HTTPS even when running HTTP internally (required for HA ingress)

### Changed
- DataProtection keys are now persisted to the data directory (`/app/data`) instead of ephemeral in-memory; antiforgery tokens and Blazor circuit descriptors survive container restarts
- Culture cookie `Secure` flag is now derived from `Request.IsHttps` instead of hardcoded `true` — language selection persists in plain-HTTP deployments
- `UseHttpsRedirection` removed — redirect responsibility belongs to the consumer's reverse proxy, not the app
- Dockerfile clears `ASPNETCORE_HTTP_PORTS` set by the .NET base image; port binding is now owned entirely by the application's Kestrel configuration

### Fixed
- Blazor interactive components (e.g. the "New quote" button) did not work in Docker — `_framework/blazor.web.js` was missing from the Docker publish output because `--no-restore` reused an incomplete Blazor static web assets manifest generated before source files were copied; fixed by removing `--no-restore` from `dotnet publish`

---

## [1.0.5] - 2026-06-14

### Highlights
- Dependency updates: `Microsoft.AspNetCore.OpenApi`, `MSTest`, `Microsoft.AspNetCore.Mvc.Testing`, `actions/checkout` (CI only)

### Changed
- `Microsoft.AspNetCore.OpenApi` updated from 10.0.7 to 10.0.9
- `actions/checkout` updated from v5 to v6 (CI only)
- `Microsoft.AspNetCore.Mvc.Testing` updated from 10.0.0 to 10.0.9 (test only)
- `MSTest` updated from 4.0.2 to 4.2.3 (test only)
- `.gitattributes` normalised to LF for all text files — prevents CRLF/LF mismatches from Dependabot merges

---

## [1.0.4] - 2026-06-14

### Highlights
- Language selector in the UI navbar — overrides browser language, persists as a cookie for one year
- AppArmor profile (`apparmor.txt`) — restricts container filesystem and network access; improves add-on quality score
- Fixed: Home Assistant ingress now connects correctly
- Direct access port disabled by default (`null`); enable in add-on configuration if needed for direct LAN or tool access

### Added
- Release workflow now gates on CI passing — `build-and-push` depends on a `test` job so a broken build cannot produce a published image
- Dependabot configured for weekly NuGet and GitHub Actions updates (`.github/dependabot.yml`)
- Language selector in the navbar: overrides browser language preference, persists as a cookie for one year
- Open API reference button on the home page
- Translation support section on the home page
- AppArmor profile for the Home Assistant add-on (`addon/apparmor.txt`)

### Changed
- Home Assistant add-on direct access port disabled by default; enable in add-on configuration for direct LAN or tool access
- `UI.en-GB.json` is now the i18n baseline; `UI.en.json` removed
- Language selector offers: Auto-detect, English (en-GB), Deutsch, Nederlands

### Fixed
- WCAG SC 3.1.1: `<html lang>` is now dynamic and reflects the active UI culture (was hardcoded `"en"`)
- Language cookie now uses `SameSite=Lax` and `Secure=true`
- QuoteCard (Interactive Server component) now respects the language cookie; previously always rendered in English regardless of selected language
- Home Assistant ingress now connects correctly via `ASPNETCORE_HTTP_PORTS` environment variable

---

## [1.0.3] - 2026-06-14

### Highlights
- Store listing and documentation updated to accurately reflect v1 scope

### Fixed
- Documentation corrections: CI/CD steps updated to reflect publish smoke test and GitHub Release creation; Home Assistant docs updated with GHCR visibility requirement; Docker docs corrected for `data/` build context inclusion
- Add-on store listing and documentation corrected — Blazor management UI is planned for v2, not present in v1
- Add-on `config.yaml` version kept in sync with API version going forward

---

## [1.0.2] - 2026-06-14

### Highlights
- Fixed: Docker image tag corrected — add-on version now matches the published image tag on GHCR

### Fixed
- GitHub Releases are now created automatically when a version tag is pushed

---

## [1.0.1] - 2026-06-13

### Highlights
- Bug fix — no user-facing changes

### Fixed
- Docker image build now succeeds: `data/quotes.json` was excluded from the build context via `.dockerignore`, causing `dotnet publish` to fail
- Updated all GitHub Actions to Node.js 24-compatible major versions; `actions/checkout@v4` still produces a Node 20 deprecation warning (upstream issue, tracked in session notes)

---

## [1.0.0] - 2026-06-13

### Highlights
- Initial release: 780 curated quotes from films, TV, books, and famous people
- REST API with random, list, search, and detail endpoints; multi-language support; rate limiting
- OpenAPI documentation at /scalar/v1

### Added
- REST read endpoints: `GET /api/v1/quotes/random`, `/api/v1/quotes`, `/api/v1/quotes/{id}`, `/api/v1/quotes/search`
- Health check endpoint: `GET /api/v1/health`
- Version endpoint: `GET /api/v1/version`
- Quote dataset: 780 curated quotes seeded from two MIT-licensed sources
- Quote schema: supports films, TV, anime, books, and famous people; optional translations
- Language support: `lang` query parameter (ISO 639-1) with fallback to original language
- RFC 7807 ProblemDetails error responses with localised messages (en, en-GB, de, nl)
- Input validation: all query parameters validated; non-integer inputs return 400 instead of 500
- Sliding-window rate limiter: 100 requests per minute per IP
- OpenAPI documentation via Scalar UI (`/scalar/v1`) and raw spec (`/openapi/v1.json`), available in all environments including production
- Multi-arch Docker image (`linux/amd64` + `linux/arm64`)
- GitHub Actions CI pipeline: build, test, and publish smoke test
- GitHub Actions release pipeline: builds and pushes Docker image to GHCR on version tags

---

## [1.0.0-beta.1] - 2026-06-13

### Highlights
- Initial release: REST API, health check endpoint, Blazor UI placeholder
- 780 curated quotes from films, TV, books, and famous people
- Multi-arch Docker image (`linux/amd64` + `linux/aarch64`)
- Home Assistant ingress on port 8099; direct access on port 8080

[Unreleased]: https://github.com/DutchJaFO/Quotinator/compare/v1.6.0...HEAD
[1.6.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.5.1...v1.6.0
[1.5.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.5.0...v1.5.1
[1.5.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.4.3...v1.5.0
[1.4.3]: https://github.com/DutchJaFO/Quotinator/compare/v1.4.2...v1.4.3
[1.4.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.4.1...v1.4.2
[1.4.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.2.2...v1.3.0
[1.2.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.15...v1.1.0
[1.0.15]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.14...v1.0.15
[1.0.14]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.13...v1.0.14
[1.0.13]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.12...v1.0.13
[1.0.12]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.11...v1.0.12
[1.0.11]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.10...v1.0.11
[1.0.10]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.9...v1.0.10
[1.0.9]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.8...v1.0.9
[1.0.8]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.0-beta.1...v1.0.0
[1.0.0-beta.1]: https://github.com/DutchJaFO/Quotinator/releases/tag/v1.0.0-beta.1
