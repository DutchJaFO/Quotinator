# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

## [Unreleased]

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
