# Changelog

All notable changes to Quotinator are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.2.1] - 2026-06-16

### Highlights
- The database file is now named `quotinatordata.db` — on first startup after upgrading, the old `quotes.db` is renamed automatically with no data loss
- A backup of the database is created automatically before any schema migration
- Container log output is now single-line and easier to read; the startup banner shows all data paths at a glance

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

## [1.2.0] - 2026-06-16

### Highlights
- Admin endpoints (reseed, reset) are now protected by an API key — they return 401 by default and only accept requests with the correct `Authorization: Bearer <key>` header

### Added
- `AdminApiKeyFilter` endpoint filter guards `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` with a static API key
- `Quotinator:AdminApiKey` config key (env var `Quotinator__AdminApiKey`) — must be set explicitly to enable the endpoints; no bundled default
- Key comparison uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks
- HA add-on: optional `admin_api_key` config option with translations in English, Dutch, and German

## [1.1.0] - 2026-06-15

### Highlights
- You can now filter quotes by more than one genre or type at once — for example, get only sci-fi comedies or drama films
- Sci-fi and non-fiction quotes were missing from search and random results; both genres now work correctly
- Two new admin endpoints let you reseed or fully reset the quote database without restarting the container

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

## [1.0.15] - 2026-06-15

### Highlights
- Fixed a session issue in Home Assistant where the interface lost its state after the container restarted

### Fixed
- DataProtection keys written to ephemeral container filesystem when `Quotinator__DataPath` env var is absent in HA (e.g. due to supervisor config cache) — `Program.cs` now falls back to `/data` (the HA persistent volume mount point) before the `/app/data` default, so keys are always on a persistent volume and antiforgery tokens survive container restarts
- OpenAPI UI and spec links in `Home.razor` had `target="_blank"`, which the HA companion app forwarded to the system browser (no HA session) — removing `target="_blank"` keeps navigation within the companion app's webview where the session is active

## [1.0.14] - 2026-06-15

### Highlights
- Internal improvement — no user-facing changes

### Changed
- `DatabaseInitializer` now implements `IDatabaseInitializer`; `Program.cs` registers and resolves via the interface
- Endpoint tests register `NoOpDatabaseInitializer` alongside `FakeQuoteService` — no database is created or seeded during tests that have no intent to exercise the database layer

## [1.0.13] - 2026-06-15

### Highlights
- Bug fix — no user-facing changes

### Fixed
- Race condition in `DatabaseInitializer.SeedIfEmptyAsync`: parallel `WebApplicationFactory` instances (parallel MSTest runs with `ExecutionScope.MethodLevel`) could both observe an empty database and attempt concurrent seeding, causing a `UNIQUE constraint failed: Sources.Title, Sources.Type` error — fixed with a static `SemaphoreSlim` that serialises seed attempts within the same process

## [1.0.12] - 2026-06-15

### Highlights
- Quotes are now stored in a local database rather than a flat file — faster, more reliable, and ready for future write support ([#7](https://github.com/DutchJaFO/Quotinator/issues/7))
- The version endpoint now also reports the database schema version and record counts

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

## [1.0.11] - 2026-06-14

### Highlights
- Add-on configuration options now display translated names and descriptions in English, Dutch, and German

### Added
- HA add-on config panel now shows translated option names and descriptions in English, Dutch, and German (`addon/translations/`)
- GitHub Milestones created to track upcoming work; README roadmap section replaced with a link to the milestone list

### Changed
- Architecture: extracted `Quotinator.Constants` project (route strings, tag names, error message keys — no dependencies); moved `ApiLocalizer`, `VersionService`, and `InputValidation` to `Quotinator.Core`
- HA add-on documentation revised: access section now distinguishes ingress (default, no port config needed) from direct port (optional, for external tools); hardcoded `http://<ha-host>:8080/` URLs removed

### Fixed
- Stale `UI.en.json` references in `docs/localisation.md` and test comments corrected to `UI.en-GB.json` (the actual baseline file)

## [1.0.10] - 2026-06-14

### Highlights
- Startup log now prints as a single, readable block with a configuration summary

### Changed
- Startup banner now prints as a single log entry (one timestamp) instead of one line per log call
- Banner config summary added: `log_level`, `log_requests`, `ssl` state, and cert/key paths when SSL is enabled

## [1.0.9] - 2026-06-14

### Highlights
- New option: log one line per API request — useful for confirming calls arrive without enabling full debug logging
- New option: choose how much detail appears in the supervisor log
- All log lines now show a UTC timestamp

### Added
- `log_requests` add-on option — when enabled, logs one line per quote API request (`GET /api/v1/quotes/random?n=5 → 200 in 12ms`); default `false`; 429 responses are included, health/Blazor/static traffic is not
- `log_level` add-on option — controls verbosity of the HA supervisor log; valid values: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`; default `info`
- UTC timestamps on all log lines (`yyyy-MM-dd HH:mm:ss`)
- Startup banner logs version, data path, quote count, and keys directory on every start
- Shutdown message logged when the container stops

### Changed
- `Console.WriteLine` startup output replaced by the structured logger — all output now respects the configured log level and timestamp format

## [1.0.8] - 2026-06-14

### Highlights
- Fixed: the API Reference, OpenAPI spec, and health check links on the home page did not work through the Home Assistant ingress ([#8](https://github.com/DutchJaFO/Quotinator/issues/8))

### Fixed
- Scalar UI button, OpenAPI spec link, and health check link on the home page were broken under the Home Assistant ingress — absolute paths (e.g. `/scalar/v1`) ignored `<base href>` and resolved against HA's server root; changed to relative paths so they resolve correctly through the ingress proxy in all deployment scenarios (closes [#8](https://github.com/DutchJaFO/Quotinator/issues/8))

## [1.0.7] - 2026-06-14

### Highlights
- Quotes and session data now survive container restarts and add-on updates — no data loss on update
- Fixed: the Blazor page (including the "New quote" button) did not load correctly in the Home Assistant sidebar

### Added
- First-run seeding: if `Quotinator__DataPath` points to an empty directory (e.g. a fresh HA add-on data volume), the bundled `quotes.json` is automatically copied there on startup so the add-on works immediately without manual setup

### Fixed
- Blazor assets (CSS, `blazor.web.js`) failed to load through the Home Assistant ingress panel — the hardcoded `<base href="/" />` caused relative URLs to resolve against HA's own server root instead of the ingress proxy path; fixed by reading `X-Ingress-Path` from the HA supervisor and setting it as the ASP.NET Core `PathBase`, which `<base href>` now reflects dynamically
- DataProtection keys were not persisted across HA add-on restarts — `Quotinator__DataPath=/data/quotes.json` points the app at the supervisor's persistent volume (`map: data:rw`) so keys survive restarts and updates; antiforgery decryption failures and Blazor circuit descriptor mismatches are resolved
- Kestrel double-bind warning: `ASPNETCORE_HTTP_PORTS: "8099"` in `addon/config.yaml` clashed with the Kestrel code that also binds port 8099; removed the environment block so port binding is owned entirely by the application
- DataProtection keys are now written to a `.keys/` subdirectory within the data directory
- `/Culture/Set` (language cookie endpoint) no longer appears in the Scalar API reference — it is a Blazor UI helper, not a REST API endpoint

## [1.0.6] - 2026-06-14

### Highlights
- Optional HTTPS on the direct access port — enable ssl in the add-on configuration to use your Let's Encrypt certificate
- Fixed: interactive elements such as the "New quote" button did not work in Docker or the HA add-on

### Added
- Optional HTTPS on the direct-access port (8080) via Kestrel; configure with `Quotinator__Ssl=true`, `Quotinator__SslCertFile`, and `Quotinator__SslKeyFile` environment variables
- HA add-on SSL options (`ssl`, `certfile`, `keyfile`) — defaults to `ssl: false`; when enabled, uses certs from `/ssl/` (HA Let's Encrypt add-on writes there)
- `UseForwardedHeaders()` middleware reads `X-Forwarded-Proto` and `X-Forwarded-For` from upstream proxies so the app knows it is behind HTTPS even when running HTTP internally (required for HA ingress)

### Fixed
- Blazor interactive components (e.g. the "New quote" button) did not work in Docker — `_framework/blazor.web.js` was missing from the Docker publish output because `--no-restore` reused an incomplete Blazor static web assets manifest generated before source files were copied; fixed by removing `--no-restore` from `dotnet publish`

### Changed
- DataProtection keys are now persisted to the data directory (`/app/data`) instead of ephemeral in-memory; antiforgery tokens and Blazor circuit descriptors survive container restarts
- Culture cookie `Secure` flag is now derived from `Request.IsHttps` instead of hardcoded `true` — language selection persists in plain-HTTP deployments
- `UseHttpsRedirection` removed — redirect responsibility belongs to the consumer's reverse proxy, not the app
- Dockerfile clears `ASPNETCORE_HTTP_PORTS` set by the .NET base image; port binding is now owned entirely by the application's Kestrel configuration

## [1.0.5] - 2026-06-14

### Highlights
- Dependency updates — no user-facing changes

### Changed
- `Microsoft.AspNetCore.OpenApi` updated from 10.0.7 to 10.0.9
- `actions/checkout` updated from v5 to v6 (CI only)
- `Microsoft.AspNetCore.Mvc.Testing` updated from 10.0.0 to 10.0.9 (test only)
- `MSTest` updated from 4.0.2 to 4.2.3 (test only)
- `.gitattributes` normalised to LF for all text files — prevents CRLF/LF mismatches from Dependabot merges

## [1.0.4] - 2026-06-14

### Highlights
- Language selector in the navigation bar — override your browser's language preference; the choice is remembered for a year
- AppArmor security profile added to the Home Assistant add-on

### Added
- Release workflow now gates on CI passing — `build-and-push` depends on a `test` job so a broken build cannot produce a published image
- Dependabot configured for weekly NuGet and GitHub Actions updates (`.github/dependabot.yml`)
- Language selector in the navbar: overrides browser language preference, persists as a cookie for one year
- Open API reference button on the home page
- Translation support section on the home page
- AppArmor profile for the Home Assistant add-on (`addon/apparmor.txt`)

### Fixed
- WCAG SC 3.1.1: `<html lang>` is now dynamic and reflects the active UI culture (was hardcoded `"en"`)
- Language cookie now uses `SameSite=Lax` and `Secure=true`
- QuoteCard (Interactive Server component) now respects the language cookie; previously always rendered in English regardless of selected language
- Home Assistant ingress now connects correctly via `ASPNETCORE_HTTP_PORTS` environment variable

### Changed
- Home Assistant add-on direct access port disabled by default; enable in add-on configuration for direct LAN or tool access
- `UI.en-GB.json` is now the i18n baseline; `UI.en.json` removed
- Language selector offers: Auto-detect, English (en-GB), Deutsch, Nederlands

## [1.0.3] - 2026-06-14

### Highlights
- Documentation corrections — no user-facing changes

### Fixed
- Documentation corrections: CI/CD steps updated to reflect publish smoke test and GitHub Release creation; Home Assistant docs updated with GHCR visibility requirement; Docker docs corrected for `data/` build context inclusion
- Add-on store listing and documentation corrected — Blazor management UI is planned for v2, not present in v1
- Add-on `config.yaml` version kept in sync with API version going forward

## [1.0.2] - 2026-06-14

### Highlights
- Bug fix — no user-facing changes

### Fixed
- GitHub Releases are now created automatically when a version tag is pushed

## [1.0.1] - 2026-06-13

### Highlights
- Bug fix — no user-facing changes

### Fixed
- Docker image build now succeeds: `data/quotes.json` was excluded from the build context via `.dockerignore`, causing `dotnet publish` to fail
- Updated all GitHub Actions to Node.js 24-compatible major versions; `actions/checkout@v4` still produces a Node 20 deprecation warning (upstream issue, tracked in session notes)

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
[1.0.0]: https://github.com/DutchJaFO/Quotinator/releases/tag/v1.0.0
