# Quotinator 🎬

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
│   └── Quotinator.Core/         # Shared models, services, data access
├── data/
│   └── quotes.json              # Quote dataset (seed data + additions)
├── docker/
│   └── Dockerfile
├── scripts/
│   └── seed.csx                 # Seed/merge/dedup script
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
| Data | JSON flat-file (SQLite planned for v2) |
| Protocol | REST + MCP (Model Context Protocol) |
| Container | Docker (linux/amd64 + linux/arm64) |
| Auth | API key (v2), user management via UI (v2) |

---

## Quote Data

Quotinator uses a curated `quotes.json` dataset seeded from two MIT-licensed sources:

- **[vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes)** — AFI Top 100 movie quotes
- **[NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes)** — broader community dataset (~732 quotes)

Both sources are MIT licensed. See [SOURCES.md](SOURCES.md) for full attribution.

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

## REST API Endpoints (v1)

All endpoints accept an optional `lang` query parameter (ISO 639-1) to request a specific language. Responses always include `language`, `originalLanguage`, and `isTranslated` so consumers know whether they received a translation or the original. See [`docs/localisation.md`](docs/localisation.md) for details.

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/quotes/random` | One random quote |
| GET | `/api/v1/quotes/random?n=10` | N random quotes (1–100) |
| GET | `/api/v1/quotes` | All quotes, paginated (`page`, `pageSize`) |
| GET | `/api/v1/quotes/{id}` | Quote by UUID |
| GET | `/api/v1/quotes/search?q=term` | Search by text, source, character, or author |
| GET | `/api/v1/health` | Health check |
| GET | `/api/v1/version` | Running version and environment |

All list endpoints accept `type` and `genre` filters. All endpoints return [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) on error, with localised `detail` messages when `lang` is set. The API applies a sliding-window rate limit of 100 requests per minute per IP.

---

## MCP Support (v3 — planned)

MCP support is planned for v3. The endpoint will be served at `/mcp` and will expose tools for fetching and searching quotes. Not yet implemented.

---

## Web Frontend (v2 — planned)

The Blazor Server management UI is planned for v2. Not yet implemented.

---

## Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
  ghcr.io/dutchjafo/quotinator:latest
```

A `docker-compose.yml` example is included in the `docker/` directory.

> **Note:** Authentication is not implemented in v1. The API is read-only and requires no credentials.

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

### v1 — Core ✅ (1.0.0)
- [x] Project structure
- [x] Quote data seed + deduplication script (780 quotes)
- [x] REST API (read endpoints)
- [x] Docker image (amd64 + arm64)

### v2 — SQLite + Management (next)
- [ ] SQLite backend (replaces flat-file JSON)
- [ ] Authenticated write endpoints
- [ ] Blazor management UI (CRUD)
- [ ] API key management
- [ ] User management

### v3 — Integrations
- [ ] MCP server endpoint
- [x] Home Assistant add-on manifest and ingress

### Optional / unversioned
Features that may be added in any future version, or not at all. See [`docs/data-import.md`](docs/data-import.md) for detail on the import strategy variants.

- [ ] Docker build-time seeding (data baked in at image build, no committed file needed)
- [ ] Container startup seeding (data fetched fresh on first run)
- [ ] Scheduled or webhook-triggered seed refresh

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

---

## License

MIT. See [LICENSE](LICENSE) for details.
Quote data attribution: see [SOURCES.md](SOURCES.md).
