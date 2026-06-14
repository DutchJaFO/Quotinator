# Changelog

All notable changes to Quotinator are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Changed
- OpenAPI/Scalar documentation is English-only by deliberate decision — documented in `CLAUDE.md` and `docs/testing-policy.md`

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

[Unreleased]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.4...HEAD
[1.0.4]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/DutchJaFO/Quotinator/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/DutchJaFO/Quotinator/releases/tag/v1.0.0
