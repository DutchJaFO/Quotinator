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
│   ├── Quotinator.Core/         # Models, interfaces, service implementations, SQLite services
│   ├── Quotinator.Data/         # SQLite infrastructure — connection factory, type handlers, base types
│   └── Quotinator.Constants/    # Route strings, tag names, error message keys (no dependencies)
├── tests/
│   ├── Quotinator.Core.Tests/   # Unit tests for core logic and input validation
│   └── Quotinator.Api.Tests/    # Endpoint integration tests (WebApplicationFactory)
├── addon/                       # Home Assistant add-on manifest, config, and translations
├── data/
│   └── quotes.json              # Quote dataset (seed data + additions)
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
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
| Data | SQLite (Dapper — no EF Core) |
| Protocol | REST (MCP planned) |
| Container | Docker (linux/amd64 + linux/arm64) |
| Auth | API key required for admin endpoints; quote endpoints are public |

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

## REST API Endpoints

All endpoints accept an optional `lang` query parameter (ISO 639-1) to request a specific language. Responses always include `language`, `originalLanguage`, and `isTranslated` so consumers know whether they received a translation or the original. See [`docs/localisation.md`](docs/localisation.md) for details.

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/quotes/random` | Random quote(s) — returns a `FilteredQuoteResult` envelope |
| GET | `/api/v1/quotes/random?n=10` | N random quotes (1–100) |
| GET | `/api/v1/quotes/random?type=movie&type=book` | Random quote from movies or books (multi-value OR logic) |
| GET | `/api/v1/quotes/random?genre=sci-fi&genre=drama` | Random quote matching either genre |
| GET | `/api/v1/quotes/random?character=Gandalf` | Random quote by or featuring Gandalf |
| GET | `/api/v1/quotes/random?author=Tolkien&source=Fellowship` | Combine filters with AND logic |
| GET | `/api/v1/quotes/random?decade=1980` | Random quote from the 1980s (expands to yearFrom=1980&yearTo=1989) |
| GET | `/api/v1/quotes/random?yearFrom=1970&yearTo=1989` | Random quote from an explicit year range |
| GET | `/api/v1/quotes` | All quotes, paginated (`page`, `pageSize`, `type`, `genre`, `yearFrom`, `yearTo`, `year`, `decade` — all optional) |
| GET | `/api/v1/quotes/{id}` | Quote by UUID |
| GET | `/api/v1/quotes/search?q=term` | Search quotes; add `&type=movie&type=book` and/or `&field=quote\|source\|character\|author` |
| GET | `/api/v1/health` | Health check |
| GET | `/api/v1/version` | Running version and environment |
| POST | `/api/v1/admin/database/reseed` | Clear all data and reimport from `quotes.json` (schema history preserved) |
| POST | `/api/v1/admin/database/reset` | Full reset: clear data + schema history, reapply migrations, reimport |

**`/random` filter parameters:** `type` and `genre` are repeatable (OR logic within each, AND between them). `character`, `author`, and `source` are case-insensitive contains matches. `yearFrom` / `yearTo` are inclusive year bounds; `year` is shorthand for a single year; `decade` (must be divisible by 10) is shorthand for a 10-year range. All filter combinations are ANDed. The response envelope always includes `status` (`Ok`, `NoResults`, `InvalidType`, `InvalidGenre`, `InputTooLong`, `InvalidInput`), `items`, and `totalMatching` (pool size before random selection).

All endpoints return [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) on structural errors (invalid `lang`, out-of-range `n`, etc.), with localised `detail` messages driven by the `Accept-Language` request header. The API applies a sliding-window rate limit of 100 requests per minute per IP.

The interactive API reference (Scalar) is available at `/scalar/v1` and the raw OpenAPI spec at `/openapi/v1.json`.

The web UI includes a language selector in the navbar. It overrides the browser's automatic language detection (English, Deutsch, Nederlands) and persists the choice as a cookie for one year. Selecting "Auto-detect" clears the override and returns to browser language detection.

---

## Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
  ghcr.io/dutchjafo/quotinator:latest
```

A `docker-compose.yml` example is included in the `docker/` directory.

### Data directory

The volume at `/app/data` contains everything Quotinator persists across restarts:

| Path | Purpose | Safe to delete? |
|---|---|---|
| `quotes.json` | Quote dataset — seed source and custom additions | Only if you want to reset to bundled data |
| `quotinatordata.db` | SQLite database — the live data store | **No** — this is your data |
| `backups/` | Pre-migration database snapshots, named `quotinatordata_v{N}_{timestamp}Z.db` | Yes — old backups can be pruned freely |
| `keys/` | ASP.NET Core Data Protection keys — used to sign antiforgery tokens and Blazor session descriptors | **No** — deleting this invalidates all active browser sessions; the app recovers on restart but users will need to reload |

> **Note:** Authentication is not yet implemented. The API is read-only and requires no credentials.

### HTTPS / SSL

SSL is disabled by default. To enable HTTPS on port 8080, mount a certificate and key and pass the paths via environment variables:

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
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
