# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

## [1.4.3] - 2026-06-20

- Internal improvements in preparation for upcoming data import features.

## [1.4.2] - 2026-06-20

- Fixed: the Docker image was incorrectly reporting version 1.0.0 — the actual version is now shown correctly.
- The REST API page now includes a direct link to the version endpoint.
- Internal improvements — no other user-facing changes.

## [1.4.1] - 2026-06-20

- Fixed: the changelog page now shows plain-English release summaries for all versions instead of technical details.
- Internal improvements in preparation for upcoming data import features.

## [1.4.0] - 2026-06-20

- Security: a database query vulnerability (CVE-2025-6965) was identified and mitigated; no user data was affected.

## [1.3.0] - 2026-06-17

- Quote data now loaded from multiple source files in `data/sources/` — one file per bundled dataset, plus optional user imports in the `imports/` subfolder of the data directory
- New `GET /api/v1/admin/database/seed/preview` endpoint shows what would be imported without writing to the database (requires `admin_api_key`)
- `Quotinator__DataDir` env var replaces `Quotinator__DataPath` — set to the data directory, not a file path

## [1.2.2] - 2026-06-16

- Fixed: the GitHub changelog link in the add-on UI opened inside the HA frame and was blocked — it now opens in a new tab correctly

## [1.2.1] - 2026-06-16

- Database file renamed from `quotes.db` to `quotinatordata.db` — the old file is renamed automatically on first startup after upgrade, no data loss
- A backup of the database is created before any schema migration and stored in the `backups/` subfolder of the data directory; old backups are safe to delete
- New optional `backup_path` config option to store backups in a custom location
- DataProtection keys folder renamed from `.keys` to `keys`
- Container log output now uses single-line format — easier to read in the HA log panel
- Startup banner shows all data paths at a glance

## [1.2.0] - 2026-06-16

- Admin endpoints (reseed, reset) now require `Authorization: Bearer <key>` — they return 401 by default until `admin_api_key` is set in the add-on configuration
- New `admin_api_key` option in the add-on configuration panel

## [1.1.0] - 2026-06-15

- Filter quotes by multiple genres or types at once (e.g. sci-fi comedies) using repeatable query parameters on `/random`, `/search`, and the paginated list
- Sci-fi and non-fiction genres are now correctly matched in all endpoints — they were silently missing from results before this fix
- `/random` now always returns a response envelope with status, items, total matching count, and an optional message; the shape of the response has changed
- New admin endpoints: `POST /api/v1/admin/database/reseed` and `POST /api/v1/admin/database/reset` for database maintenance without restarting the container

## [1.0.15] - 2026-06-15

- Fixed: antiforgery token errors after container restart — DataProtection keys are now reliably written to the persistent volume (`/data/.keys/`) even when the supervisor serves a cached config that omits `Quotinator__DataPath`
- Fixed: the OpenAPI UI link in the sidebar opened in the system browser when tapped in the HA companion app, losing the session and causing a 404; it now navigates within the companion app's webview

## [1.0.14] - 2026-06-15

- Internal: endpoint tests no longer create or touch a database — no impact on add-on behaviour

## [1.0.13] - 2026-06-15

- Internal: fixed a race condition in database seeding that caused test failures under parallel test execution — no impact on add-on behaviour

## [1.0.12] - 2026-06-15

- Quotes are now stored in a SQLite database (`quotes.db`) on the persistent volume — no action required; the database is created and seeded automatically on first run
- Startup log now shows database status: schema version, seeding progress, and a final count of quotes, sources, characters, and people
- Version endpoint (`GET /api/v1/version`) now returns the database schema version and record counts alongside the API version
- Fixed: startup banner and version endpoint incorrectly reported version `1.0.0` instead of the actual add-on version
- Fixed: Docker image build failed when `Quotinator.Data` was introduced — Dockerfile COPY layers corrected

## [1.0.11] - 2026-06-14

- Config panel options now show translated names and descriptions in English, Dutch, and German
- Documentation tab: access section now clearly distinguishes ingress (default, sidebar) from direct port (optional, for external tools); misleading hardcoded URL removed

## [1.0.10] - 2026-06-14

- Startup banner prints as a single block (one timestamp) with a config summary: log level, request logging state, SSL state, and cert paths when SSL is enabled

## [1.0.9] - 2026-06-14

- New `log_requests` option — logs one line per quote API request; useful for confirming calls arrive without enabling full debug logging (default: `false`)
- New `log_level` option — choose log verbosity from the add-on panel (default: `info`; use `debug` when reporting issues)
- UTC timestamps on all log lines
- Clear start and stop markers in the supervisor log (version, data path, quote count)

## [1.0.8] - 2026-06-14

- Fixed: links on the home page (Scalar UI, OpenAPI spec, health check) did not resolve correctly through the Home Assistant ingress — they now use relative paths

## [1.0.7] - 2026-06-14

- Quotes and DataProtection keys are now stored on the supervisor-mounted persistent volume (`/data`) and survive add-on restarts and updates — no manual data migration needed on first install
- Fixed: Blazor assets (CSS, `blazor.web.js`) failed to load in the Home Assistant ingress panel — the sidebar page was broken and the "New quote" button did not work
- Fixed: antiforgery decryption failures after add-on restart — DataProtection keys now persist across restarts
- Fixed: language cookie endpoint no longer appears in the Scalar API reference

## [1.0.6] - 2026-06-14

- SSL / HTTPS support on the direct-access port — set `ssl: true` and supply cert/key filenames (relative to `/ssl/`); defaults to disabled
- Ingress now correctly detects the browser's HTTPS context via `X-Forwarded-Proto`
- Fixed: Blazor interactive components (e.g. the "New quote" button) did not work in Docker or the HA add-on
- Language selection cookie now works in plain-HTTP direct-access deployments

## [1.0.5] - 2026-06-14

- Dependency updates: `Microsoft.AspNetCore.OpenApi`, `MSTest`, `Microsoft.AspNetCore.Mvc.Testing`, `actions/checkout` (CI only)

## [1.0.4] - 2026-06-14

- Language selector in the UI navbar — overrides browser language, persists as a cookie for one year
- AppArmor profile (`apparmor.txt`) — restricts container filesystem and network access; improves add-on quality score
- Fixed: Home Assistant ingress now connects correctly
- Direct access port disabled by default (`null`); enable in add-on configuration if needed for direct LAN or tool access

## [1.0.3] - 2026-06-14

- Store listing and documentation updated to accurately reflect v1 scope

## [1.0.2] - 2026-06-14

- Fixed: Docker image tag corrected — add-on version now matches the published image tag on GHCR

## [1.0.0-beta.1] - 2026-06-13

- Initial release: REST API, health check endpoint, Blazor UI placeholder
- 780 curated quotes from films, TV, books, and famous people
- Multi-arch Docker image (`linux/amd64` + `linux/aarch64`)
- Home Assistant ingress on port 8099; direct access on port 8080
