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
- **[NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes)** — broader community dataset (~500 quotes)

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

All endpoints accept an optional `lang` query parameter (ISO 639-1) to request a specific language. See [`docs/localisation.md`](docs/localisation.md) for details.

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/quotes/random` | One random quote |
| GET | `/api/v1/quotes/random?n=10` | N random quotes |
| GET | `/api/v1/quotes` | All quotes (paginated) |
| GET | `/api/v1/quotes/{id}` | Quote by ID |
| GET | `/api/v1/quotes/search?q=` | Search quotes |
| GET | `/api/v1/health` | Health check |

---

## MCP Support

Quotinator exposes an MCP-compatible endpoint so AI assistants can use it as a tool. The MCP server is served at `/mcp` and provides the following tools:

- `get_random_quote` — fetch a random movie quote
- `search_quotes` — search by keyword, movie, or year range
- `get_quote_by_id` — fetch a specific quote by ID

---

## Web Frontend (Blazor)

The management UI is available at the root URL and provides:

- Quote browser with search and filters
- Add / edit / delete quotes
- API key management
- User management (admin only)
- Server health and stats dashboard
- MCP feature toggle

---

## Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
  -e QUOTINATOR__ApiKey=your-key-here \
  ghcr.io/dutchjafo/quotinator:latest
```

A `docker-compose.yml` example is included in the `docker/` directory.

---

## Development Setup

### Prerequisites
- .NET 10 SDK
- Docker Desktop (optional, for container testing)

### Run locally

```bash
git clone https://github.com/your-github-username/quotinator.git
cd quotinator
dotnet run --project src/Quotinator.Api
```

The API will be available at `https://localhost:7028`. See [`docs/running-locally.md`](docs/running-locally.md) for all available URLs.

---

## Roadmap

### v1 — Core (current focus)
- [x] Project structure
- [ ] Quote data seed + deduplication script
- [ ] REST API (read endpoints)
- [ ] Docker image (amd64 + arm64)

### v2 — Management
- [ ] Authenticated write endpoints
- [ ] Blazor management UI (CRUD)
- [ ] API key management
- [ ] User management
- [ ] SQLite backend (replace flat-file)

### v3 — Integrations
- [ ] MCP server endpoint
- [x] Home Assistant add-on manifest and ingress

---

## License

MIT. See [LICENSE](LICENSE) for details.
Quote data attribution: see [SOURCES.md](SOURCES.md).
