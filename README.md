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
│   ├── Quotinator.Core/         # Domain models, interfaces, and in-memory service implementations
│   ├── Quotinator.Data/         # Generic, reusable SQLite/Dapper infrastructure (domain-agnostic)
│   ├── Quotinator.Data.Testing/ # Test helper library — stubs, fakes, and disposable SQLite DB
│   └── Quotinator.Engine/       # SQLite-backed Quotinator domain implementation (bridges Core + Data)
├── tests/
│   ├── Quotinator.Api.Tests/         # Endpoint integration tests (WebApplicationFactory)
│   ├── Quotinator.Changelog.Tests/   # Changelog schema and generation tests
│   ├── Quotinator.Constants.Tests/   # Tests for route and constant definitions
│   ├── Quotinator.Core.Tests/        # Unit tests for domain logic and in-memory service
│   ├── Quotinator.Data.Example/      # Concrete example implementations of Data patterns (not a test runner)
│   ├── Quotinator.Data.Testing.Tests/ # Tests for the Data.Testing helper library
│   ├── Quotinator.Data.Tests/        # Integration tests for Data infrastructure (real SQLite, no fakes)
│   └── Quotinator.Engine.Tests/      # Integration tests for Engine (SqliteQuoteService, migrations)
├── addon/                       # Home Assistant add-on manifest, config, and translations
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

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/quotes/random` | Random quote(s) — returns a `FilteredQuoteResult` envelope. When a returned quote belongs to a conversation, one is chosen at random and its full line list embedded on that item's `embeddedConversation`; every other quote in that conversation is excluded from the rest of the result |
| GET | `/api/v1/quotes/random?n=10` | N random quotes (1–100) |
| GET | `/api/v1/quotes/random?type=movie&type=book` | Random quote from movies or books (multi-value OR logic) |
| GET | `/api/v1/quotes/random?genre=sci-fi&genre=drama` | Random quote matching either genre |
| GET | `/api/v1/quotes/random?character=Gandalf` | Random quote by or featuring Gandalf |
| GET | `/api/v1/quotes/random?author=Tolkien&source=Fellowship` | Combine filters with AND logic |
| GET | `/api/v1/quotes/random?decade=1980` | Random quote from the 1980s (expands to yearFrom=1980&yearTo=1989) |
| GET | `/api/v1/quotes/random?yearFrom=1970&yearTo=1989` | Random quote from an explicit year range |
| GET | `/api/v1/quotes` | All quotes, paginated (`page`, `pageSize`, `type`, `genre`, `yearFrom`, `yearTo`, `year`, `decade` — all optional) |
| GET | `/api/v1/quotes/{id}` | Quote by UUID |
| GET | `/api/v1/quotes/search?q=term` | Search quotes; returns a result envelope (`status`, `items`, `totalMatching`, `message`). Add `&type=movie&type=book` and/or `&field=quote\|source\|character\|author` |
| GET | `/api/v1/conversations` | Paginated list of Conversations — summaries only (`id`, `description`, `completenessStatus`, `lineCount`), never the full line list (`page`, `pageSize`) |
| GET | `/api/v1/conversations/{id}` | A conversation's full ordered line list — quotes, stage directions, and sound cues |
| GET | `/api/v1/masterdata/sources` | Paginated list of Sources — the films, television series, books, and other works quotes are drawn from (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/sources/{id}` | Source by UUID. Includes a `series` reference (`{id, name}`, or `null` if the source has no series) |
| GET | `/api/v1/masterdata/characters` | Paginated list of Characters — fictional characters who deliver quotes (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/characters/{id}` | Character by UUID. Includes a `sources` array of `{id, name}` references for every Source the character appears in (#179) |
| GET | `/api/v1/masterdata/people` | Paginated list of People — real individuals who said or wrote a quote (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/people/{id}` | Person by UUID |
| GET | `/api/v1/masterdata/series` | Paginated list of Series — direct continuities of Sources within a Universe (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/series/{id}` | Series by UUID. Includes a `universe` reference (`{id, name}`, or `null` if the series has no universe) |
| GET | `/api/v1/masterdata/universes` | Paginated list of Universes — fictional worlds or franchises spanning one or more Series (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/universes/{id}` | Universe by UUID |
| GET | `/api/v1/masterdata/stagedirections` | Paginated list of StageDirections — reusable scene-setting or action descriptions that can appear in a conversation (`page`, `pageSize`) |
| GET | `/api/v1/masterdata/stagedirections/{id}` | StageDirection by UUID |
| GET | `/api/v1/health` | Health check |
| GET | `/api/v1/version` | Running version and environment |
| POST | `/api/v1/import` | Import one source file (JSON or, via `converter: "csv"` in `settings`, CSV) — same duplicate-detection engine as startup seeding. Multipart fields: `file`, `settings` (optional JSON: `converter`, `duplicateResolution`, `enrich`) — or pass `batchId` (query string) instead of `file` to apply a batch already staged by a prior `/import`/`/import/preview` call. Stages then attempts to apply — `200` when everything applied, `202` when any row needs a decision, `422` if neither `file` nor `batchId` is given. Returns a summary/conflicts/errors envelope (requires `X-Api-Key`) |
| POST | `/api/v1/import/preview` | Same as `/import` but never applies — a real, inspectable batch is staged (review via `GET /import/actions?batchId=`), nothing is written to quote data. `200` when the batch would apply cleanly as-is, `202` when any row needs a decision (requires `X-Api-Key`) |
| GET | `/api/v1/import/actions` | List staged import actions (Quote/Source/Character/Person), paginated. Filter by `status` (`Pending`, `Decided`, `Applied`, `Discarded`, `Blocked`), `batchId`, and/or `entityType`. Each item includes `relatedActionIds` and `ambiguousFields` |
| POST | `/api/v1/import/actions/{id}/decide` | Stage a per-field keep/replace/custom decision for one staged Quote or Source action — git-merge-style, nothing is written yet. Any decision may also set `markCompletenessAs` to directly set the target record's completeness status once applied (requires `X-Api-Key`) |
| POST | `/api/v1/import/actions/{id}/undo` | Revert a staged action's decision back to pending (requires `X-Api-Key`) |
| POST | `/api/v1/import/actions/apply?batchId=` | Apply every action in a batch atomically, once every one of them has a decision — refuses with the still-pending ids otherwise (requires `X-Api-Key`) |
| POST | `/api/v1/import/actions/discard?batchId=` | Discard every staged action in a batch — never touches domain tables (requires `X-Api-Key`) |
| POST | `/api/v1/import/actions/reverse?batchId=` | Undo an applied import batch — Add actions are soft-deleted, Modify actions are restored to their pre-change values. Only the most recently applied batch still live may be reversed (strict LIFO stack); the batch's own record is itself soft-deleted on success. Pass `?preview=true` to check whether it would succeed without changing anything (requires `X-Api-Key`) |
| GET | `/api/v1/admin/database/seed/preview` | Preview what a reseed would import — no data is changed. Reflects any already-downloaded source cache, but never triggers a network call itself. Each file includes `isValidJson` (whether it parsed at all) and, when it has a `downloadUrl`, `refreshOutcome`/`lastRefreshedAtUtc` (requires `X-Api-Key`) |
| POST | `/api/v1/admin/database/reseed` | Clear all data and reimport from `data/sources/` — schema history preserved. Pass `?forceSourceRefresh=true` to bypass the download cache's freshness check for this call (requires `X-Api-Key`) |
| POST | `/api/v1/admin/database/reset` | Full reset: clear data, reapply migrations, reimport (requires `X-Api-Key`). Audit log always survives. Schema version history is cleared and replayed by default; pass `?preserveSchemaVersion=true` to keep it. Pass `?forceSourceRefresh=true` to bypass the download cache's freshness check for this call |
| POST | `/api/v1/admin/sources/refresh` | Refresh the download cache for any source with a `downloadUrl`/`github` manifest entry, without touching the database. Pass `?force=true` to bypass the freshness check. Each result includes `lastRefreshedAtUtc` — the cached copy's own last-write time, so an `uptodate` outcome still shows how old the data actually is (requires `X-Api-Key`) |

Admin endpoints require the `X-Api-Key: <key>` request header matching the `admin_api_key` set in the add-on configuration. Requests without the header, or with an incorrect key, receive `401 Unauthorized`. The endpoints return `401` if no key is configured.

Sources declaring a `downloadUrl` or `github` manifest entry are automatically refreshed from the network before seeding — controlled by `Quotinator__AutoUpdateSources` (default `true`; set `false` for fully offline/air-gapped installs) and `Quotinator__SourceUpdateIntervalHours` (default `24`, overridable per-entry via the manifest's `refreshIntervalHours` field). A network failure never blocks startup, reseed, or reset — the app falls back to whatever copy is already on disk.

**`/random` and `/search` filter parameters:** `type` and `genre` are repeatable (OR logic within each, AND between them). `yearFrom` / `yearTo` are inclusive year bounds; `year` is shorthand for a single year; `decade` (must be divisible by 10) is shorthand for a 10-year range. All filter combinations are ANDed. Both endpoints return a result envelope with `status` (`Ok`, `NoResults`, `InvalidType`, `InvalidGenre`, `InputTooLong`, `InvalidInput`), `items`, `totalMatching`, and an optional `message` (set when status is not `Ok`). `/random` additionally supports `character`, `author`, and `source` as case-insensitive contains filters on the random pool, and includes `requestedCount`/`returnedCount` (`null` on `/search`) — `returnedCount` can be lower than `requestedCount` when the pool is smaller than requested or conversation-aware deduplication excluded quotes sharing a conversation with an already-selected one; `/search` uses `field` (`quote`, `source`, `character`, `author`) to restrict which field the search term is matched against.

Every quote response includes a `conversations` field — `null` unless the quote belongs to one or more conversations, in which case it lists `{ conversationId, position, totalLines }` for each (fetch the full line list via `GET /api/v1/conversations/{id}`).

All endpoints return [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) on structural errors (invalid `lang`, out-of-range `n`, etc.), with localised `detail` messages driven by the `Accept-Language` request header. The API applies a sliding-window rate limit of 100 requests per minute per IP.

The interactive API reference (Scalar) is available at `/scalar/v1` and the raw OpenAPI spec at `/openapi/v1.json`.

The web UI includes a language selector in the navbar. It overrides the browser's automatic language detection (English, Deutsch, Nederlands) and persists the choice as a cookie for one year. Selecting "Auto-detect" clears the override and returns to browser language detection.

---

## Home Assistant Add-on

Quotinator can be installed directly as a Home Assistant add-on. Click the button below to open your Home Assistant instance's app store with this repository pre-filled:

[![Open your Home Assistant instance and show the app store with this repository pre-filled.](https://my.home-assistant.io/badges/supervisor_store.svg)](https://my.home-assistant.io/redirect/supervisor_store/?repository_url=https%3A%2F%2Fgithub.com%2FDutchJaFO%2FQuotinator)

Then find **Quotinator** in the store and click **Install**. See [`addon/DOCS.md`](addon/DOCS.md) for configuration options once installed.

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
