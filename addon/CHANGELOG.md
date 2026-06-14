# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Fixed
- Blazor assets (CSS, `blazor.web.js`) failed to load in the Home Assistant ingress panel — the sidebar page was broken with an "unhandled error" and the "New quote" button did not work

## [1.0.6] - 2026-06-14

### Added
- SSL / HTTPS support on the direct-access port — set `ssl: true` and supply cert/key filenames (relative to `/ssl/`); defaults to disabled
- Ingress now correctly detects the browser's HTTPS context via `X-Forwarded-Proto` — language selection and session cookies work properly through the HA ingress

### Fixed
- Blazor interactive components (e.g. the "New quote" button on the home page) did not work in Docker or the HA add-on

### Changed
- Language selection cookie is no longer blocked in plain-HTTP direct-access deployments

## [1.0.5] - 2026-06-14

### Changed
- `Microsoft.AspNetCore.OpenApi` updated from 10.0.7 to 10.0.9

## [1.0.4] - 2026-06-14

### Added
- Language selector in the UI navbar — overrides browser language, persists as a cookie for one year
- AppArmor profile (`apparmor.txt`) — restricts container filesystem and network access; improves add-on quality score

### Fixed
- Home Assistant ingress now connects correctly: binds to both port 8080 (direct) and 8099 (ingress) via `ASPNETCORE_HTTP_PORTS` environment variable
- Language cookie now uses `SameSite=Lax` and `Secure=true`
- UI language now reflects the selected language in all components (Interactive Server components previously showed English regardless of selection)
- `<html lang>` now reflects the active UI culture, satisfying WCAG SC 3.1.1

### Changed
- Direct access port disabled by default (`null`); enable in add-on configuration if needed for direct LAN or tool access

## [1.0.3] - 2026-06-14

### Fixed
- Store listing and documentation updated to accurately reflect v1 scope (REST API only; Blazor management UI is planned for v2)
- GHCR package visibility requirement documented

## [1.0.2] - 2026-06-14

### Fixed
- Docker image tag corrected — add-on version now matches published image tag on GHCR

## [1.0.0-beta.1] - 2026-06-13

### Added
- Project scaffold: ASP.NET Core Minimal API + Blazor Server (combined container)
- Health check endpoint (`/api/v1/health`)
- Blazor UI placeholder with links to the health check and OpenAPI explorer
- Quote data model — supports films, TV, anime, books, and famous people
- Localisation infrastructure: UI strings in English (US/GB), German, and Dutch
- Multi-arch Docker image (`linux/amd64` + `linux/aarch64`)
- Home Assistant ingress on port 8099; direct access on port 8080

### Not yet in this release
- Quote dataset (`data/quotes.json` not yet seeded)
- REST read endpoints (`/random`, `/`, `/{id}`, `/search`)
