# Quotinator 🎬

![CI](https://github.com/DutchJaFO/Quotinator/actions/workflows/ci.yml/badge.svg)
![CodeQL](https://github.com/DutchJaFO/Quotinator/actions/workflows/codeql.yml/badge.svg)
![License](https://img.shields.io/github/license/DutchJaFO/Quotinator)
![Release](https://img.shields.io/github/v/release/DutchJaFO/Quotinator)
![.NET](https://img.shields.io/badge/dotnet-10-512BD4)
![Supports amd64 Architecture](https://img.shields.io/badge/amd64-yes-green.svg)
![Supports aarch64 Architecture](https://img.shields.io/badge/aarch64-yes-green.svg)

> *"I'll be back... with a quote."*

A self-hosted quote REST API with MCP support, built in C# / ASP.NET Core, deployable as a Docker container. Designed for homelab and self-hosted environments — serves real, verified quotes from films, books, and famous people over a clean REST API, with a Blazor management frontend and MCP tool support for AI assistants.

---

## Project Goals

- Serve real, accurately attributed quotes via a clean REST API
- Source types: films, television, books, and famous people
- Quotes stored in their original language with optional curated translations
- Support the Model Context Protocol (MCP) so AI assistants can fetch quotes as a tool
- Ship as a Docker image (amd64 + arm64)
- Include a Blazor Server web frontend for managing quotes, users, and settings
- Stay maintainable by a single developer with standard .NET skills

---

## Architecture Overview

```
Quotinator/
├── src/
│   ├── Quotinator.Api/          # ASP.NET Core — REST endpoints + Blazor Server UI (combined)
│   ├── Quotinator.Changelog/    # Changelog library — models, schema validation, formatters
│   ├── Quotinator.Constants/    # Route strings, tag names, error message keys (no dependencies)
│   ├── Quotinator.Core/         # Domain models, interfaces, and the SQLite-backed service implementation
│   ├── Quotinator.Data/         # Generic, reusable SQLite/Dapper infrastructure (domain-agnostic)
│   └── Quotinator.Data.Testing/ # Test helper library — stubs, fakes, and disposable SQLite DB
├── tests/
│   ├── Quotinator.Api.Tests/         # Endpoint integration tests (WebApplicationFactory)
│   ├── Quotinator.Changelog.Tests/   # Changelog schema and generation tests
│   ├── Quotinator.Constants.Tests/   # Tests for route and constant definitions
│   ├── Quotinator.Core.Tests/        # Unit tests for domain logic, integration tests for the SQLite-backed implementation
│   ├── Quotinator.Data.Example/      # Concrete example implementations of Data patterns (not a test runner)
│   ├── Quotinator.Data.Testing.Tests/ # Tests for the Data.Testing helper library
│   └── Quotinator.Data.Tests/        # Integration tests for Data infrastructure (real SQLite, no fakes)
├── addon/                       # Home Assistant add-on manifest, config, and translations — stable channel
├── addon-beta/                  # Same as addon/, for the beta channel (same image, different slug/version)
├── data/
│   └── sources/                 # Bundled source files (one JSON per dataset) + manifest
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
├── docs/                        # Architecture decisions, workflow, security, and reference docs
├── schemas/                     # JSON Schema files for source file validation and editor IntelliSense
├── scripts/
│   ├── SOURCES.md                # Workflow for adding a new quote source via a converter plugin
│   ├── changelog.csx            # Changelog markdown generator (keepachangelog + HA add-on formats)
│   ├── changelog-import.csx     # Import tool for adding new changelog entries
│   └── changelog-upgrade.csx   # Schema upgrade tool for changelog format migrations
├── SOURCES.md                   # Attribution for seed data sources
├── CLAUDE.md                    # AI assistant context (read this first)
└── README.md
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# (.NET 10) |
| API | ASP.NET Core Minimal API |
| Frontend | Blazor Server |
| Data | SQLite (Dapper — no EF Core) |
| Logging | Serilog (programmatic configuration — HA container compatible) |
| Protocol | REST (MCP planned) |
| Container | Docker (linux/amd64 + linux/arm64) |
| Auth | API key required for admin endpoints; quote endpoints are public |

---

## Quote Data

Quotinator's quote data lives in `data/sources/` — one JSON file per dataset, normalised to the canonical schema. The bundled sources are:

- **`quotinator-curated.json`** — manually verified entries with enriched metadata (character names, genres, conversations)
- **`quotinator-series-universe.json`** — curated Series/Universe groupings for Sources already present in the other bundled files (e.g. linking Star Wars films into a "Star Wars" Series/Universe); carries no quotes of its own
- **[vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes)** — AFI Top 100 movie quotes (~99 entries)
- **[NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes)** — popular movie, TV, and anime quotes (~732 entries)

All external sources are MIT licensed. See [SOURCES.md](SOURCES.md) for full attribution and JSON Schema documentation.

The canonical quote schema is:

```json
{
  "id": "uuid-v4",
  "quote": "Here's looking at you, kid.",
  "originalLanguage": "en",
  "source": "Casablanca",
  "date": "1942",
  "character": "Rick Blaine",
  "author": null,
  "type": "movie",
  "genres": ["drama", "romance"],
  "translations": {
    "nl": { "quote": "Hier kijk ik naar je, kind.", "source": "Casablanca" }
  }
}
```

- `originalLanguage` — ISO 639-1 code; most entries are `"en"` (American English)
- `source` — film/show title, book title, or speech occasion
- `date` — ISO 8601, as precise as the source has it: `"1942"`, `"1940-06"`, or `"1940-06-04"`
- `character` — fictional character (movie/tv/anime/book fiction)
- `author` — book's author, or the real person for `person` type quotes
- `type` — `movie`, `tv`, `anime`, `book`, or `person`
- `genres` — filter tags; standard values: `action`, `adventure`, `animation`, `comedy`, `drama`, `fantasy`, `fiction`, `horror`, `mystery`, `non-fiction`, `romance`, `sci-fi`, `thriller`
- `translations` — manually curated only; never auto-generated

API responses include `language`, `originalLanguage`, and `isTranslated` so consumers always know whether they received a translation or the original.

---

## REST API Endpoints

All endpoints accept an optional `lang` query parameter (ISO 639-1) to request a specific language. Responses always include `language`, `originalLanguage`, and `isTranslated` so consumers know whether they received a translation or the original. See [`docs/localisation.md`](docs/localisation.md) for details.

See [`docs/api-endpoints.md`](docs/api-endpoints.md) for the full endpoint reference — every route, its query parameters, and a description of its behavior. For an interactive, always-current view of the same API on a running instance, use the Scalar reference at `/scalar/v1` or the raw OpenAPI spec at `/openapi/v1.json`.

The web UI includes a language selector in the navbar. It overrides the browser's automatic language detection (English, Deutsch, Nederlands) and persists the choice as a cookie for one year. Selecting "Auto-detect" clears the override and returns to browser language detection.

---

## Home Assistant Add-on

Quotinator can be installed directly as a Home Assistant add-on. Click the button below to open your Home Assistant instance's app store with this repository pre-filled:

[![Open your Home Assistant instance and show the app store with this repository pre-filled.](https://my.home-assistant.io/badges/supervisor_store.svg)](https://my.home-assistant.io/redirect/supervisor_store/?repository_url=https%3A%2F%2Fgithub.com%2FDutchJaFO%2FQuotinator)

Then find **Quotinator** (stable) or **Quotinator (BETA)** in the store and click **Install** — both are independently installable, published from the same repository and image. See [`addon/DOCS.md`](addon/DOCS.md) for configuration options once installed (identical for both channels).

## Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/data \
  -e Quotinator__DataDir=/data \
  ghcr.io/dutchjafo/quotinator:latest
```

A `docker-compose.yml` example is included in the `docker/` directory.

### Data directory

**Always mount the persistent volume at `/data` and set `Quotinator__DataDir=/data`** — never mount it at `/app/data`. Bundled quote sources are baked into the image at `/app/data/sources/`, and standalone Docker's data-directory default (when `Quotinator__DataDir` is unset) is that same `/app/data` path. Mounting a volume there hides the bundled sources under whatever is on the host (usually nothing on a first run), so the app starts with no quotes at all. See [`docs/docker.md`](docs/docker.md#data-directory-and-volume-mounts) for details.

The volume at `/data` contains everything Quotinator persists across restarts:

| Path | Purpose | Safe to delete? |
|---|---|---|
| `quotinatordata.db` | SQLite database — the live data store | **No** — this is your data |
| `backups/` | Pre-migration database snapshots, named `quotinatordata_v{N}_{timestamp}Z.db` | Yes — old backups can be pruned freely |
| `keys/` | ASP.NET Core Data Protection keys — used to sign antiforgery tokens and Blazor session descriptors | **No** — deleting this invalidates all active browser sessions; the app recovers on restart but users will need to reload |

> **Note:** Authentication is not yet implemented. The API is read-only and requires no credentials.

### HTTPS / SSL

SSL is disabled by default. To enable HTTPS on port 8080, mount a certificate and key and pass the paths via environment variables:

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/data \
  -e Quotinator__DataDir=/data \
  -v ./certs:/ssl:ro \
  -e Quotinator__Ssl=true \
  -e Quotinator__SslCertFile=/ssl/fullchain.pem \
  -e Quotinator__SslKeyFile=/ssl/privkey.pem \
  ghcr.io/dutchjafo/quotinator:latest
```

When running behind a reverse proxy (NGINX, Caddy, Traefik) that terminates TLS, leave `Quotinator__Ssl=false` — the app reads `X-Forwarded-Proto` and sets cookies correctly.

---

## Development Setup

### Prerequisites
- .NET 10 SDK
- Docker Desktop (optional, for container testing)

### Run locally

```bash
git clone https://github.com/DutchJaFO/Quotinator.git
cd quotinator
dotnet run --project src/Quotinator.Api
```

The API will be available at `https://localhost:7028`. See [`docs/running-locally.md`](docs/running-locally.md) for all available URLs.

---

## Roadmap

Upcoming work is tracked in [GitHub Milestones](https://github.com/DutchJaFO/Quotinator/milestones).

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

---

## License

MIT. See [LICENSE](LICENSE) for details.
Quote data attribution: see [SOURCES.md](SOURCES.md).
