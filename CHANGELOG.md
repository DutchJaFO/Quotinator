# Changelog

All notable changes to Quotinator are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.9] - 2026-06-14

### Added
- `log_level` add-on option — controls verbosity of the HA supervisor log; valid values: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`; default `info`
- UTC timestamps on all log lines (`yyyy-MM-dd HH:mm:ss`)
- Startup banner logs version, data path, quote count, and keys directory on every start
- Shutdown message logged when the container stops

### Changed
- `Console.WriteLine` startup output replaced by the structured logger — all output now respects the configured log level and timestamp format

## [1.0.8] - 2026-06-14

### Fixed
- Scalar UI button, OpenAPI spec link, and health check link on the home page were broken under the Home Assistant ingress — absolute paths (e.g. `/scalar/v1`) ignored `<base href>` and resolved against HA's server root; changed to relative paths so they resolve correctly through the ingress proxy in all deployment scenarios (closes [#8](https://github.com/DutchJaFO/Quotinator/issues/8))

## [1.0.7] - 2026-06-14

### Added
- First-run seeding: if `Quotinator__DataPath` points to an empty directory (e.g. a fresh HA add-on data volume), the bundled `quotes.json` is automatically copied there on startup so the add-on works immediately without manual setup

### Fixed
- Blazor assets (CSS, `blazor.web.js`) failed to load through the Home Assistant ingress panel — the hardcoded `<base href="/" />` caused relative URLs to resolve against HA's own server root instead of the ingress proxy path; fixed by reading `X-Ingress-Path` from the HA supervisor and setting it as the ASP.NET Core `PathBase`, which `<base href>` now reflects dynamically
- DataProtection keys were not persisted across HA add-on restarts — `Quotinator__DataPath=/data/quotes.json` points the app at the supervisor's persistent volume (`map: data:rw`) so keys survive restarts and updates; antiforgery decryption failures and Blazor circuit descriptor mismatches are resolved
- Kestrel double-bind warning: `ASPNETCORE_HTTP_PORTS: "8099"` in `addon/config.yaml` clashed with the Kestrel code that also binds port 8099; removed the environment block so port binding is owned entirely by the application
- DataProtection keys are now written to a `.keys/` subdirectory within the data directory
- `/Culture/Set` (language cookie endpoint) no longer appears in the Scalar API reference — it is a Blazor UI helper, not a REST API endpoint

## [1.0.6] - 2026-06-14

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

### Changed
- `Microsoft.AspNetCore.OpenApi` updated from 10.0.7 to 10.0.9
- `actions/checkout` updated from v5 to v6 (CI only)
- `Microsoft.AspNetCore.Mvc.Testing` updated from 10.0.0 to 10.0.9 (test only)
- `MSTest` updated from 4.0.2 to 4.2.3 (test only)
- `.gitattributes` normalised to LF for all text files — prevents CRLF/LF mismatches from Dependabot merges

## [1.0.4] - 2026-06-14

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

### Fixed
- Documentation corrections: CI/CD steps updated to reflect publish smoke test and GitHub Release creation; Home Assistant docs updated with GHCR visibility requirement; Docker docs corrected for `data/` build context inclusion
- Add-on store listing and documentation corrected — Blazor management UI is planned for v2, not present in v1
- Add-on `config.yaml` version kept in sync with API version going forward

## [1.0.2] - 2026-06-14

### Fixed
- GitHub Releases are now created automatically when a version tag is pushed

## [1.0.1] - 2026-06-13

### Fixed
- Docker image build now succeeds: `data/quotes.json` was excluded from the build context via `.dockerignore`, causing `dotnet publish` to fail
- Updated all GitHub Actions to Node.js 24-compatible major versions; `actions/checkout@v4` still produces a Node 20 deprecation warning (upstream issue, tracked in session notes)

## [1.0.0] - 2026-06-13

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

[1.0.8]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/DutchJaFO/Quotinator/releases/tag/v1.0.0
