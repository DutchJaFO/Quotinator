# CLAUDE.md — Quotinator Project Context

This file is the primary context document for AI assistants (Claude Code, etc.) working in this repository. Read this before doing anything else.

---

## What is Quotinator?

Quotinator is a self-hosted quote REST API with MCP support, built in C# / ASP.NET Core and deployable as a Docker container.

**Primary use case:** Supply real, verified quotes to self-hosted display and automation tools, replacing approaches that use LLMs to generate quotes (which are often inaccurate).

Quotes come from **films, television, books, and famous people**. All quotes are stored in their original language (most are American English) with optional curated translations.

**Planned integrations:**
- MCP tool for AI assistants
- Home Assistant Docker add-on
- MagicMirror² compliments module

---

## Developer Context

- Language: **C# (.NET 10)**
- UI framework: **Blazor Server**
- Deployment: **Docker** (linux/amd64 + linux/arm64)
- The developer works professionally with C# and Blazor — keep patterns familiar and idiomatic

---

## Project Priorities (in order)

1. **Correctness** — quotes must be real and accurately attributed; never generate or invent quotes
2. **Simplicity** — homelab project; avoid over-engineering
3. **Maintainability** — maintained solo; keep dependencies minimal
4. **Portability** — Docker-first, multi-arch
5. **Extensibility** — MCP, Home Assistant, and management UI are planned but not v1

---

## Current Development Phase

**Phase: v1 — COMPLETE (tagged 1.0.0)**

v1 phase gates — all done:
- [x] `data/quotes.json` seeded and deduplicated from both source datasets (780 quotes)
- [x] REST read endpoints working (`/random`, `/random?n=`, `/`, `/{id}`, `/search`)
- [x] `/api/v1/health` endpoint
- [x] Docker image builds and runs correctly on amd64 and arm64

**Phase: v2 — SQLite backend (next)**

Focus: replace flat-file JSON with a SQLite database. Keep the REST API surface unchanged. No auth, no Blazor UI, no write endpoints yet — just the persistence layer swap.

Phase gates (must be done before moving to v2 write endpoints):
- [ ] SQLite database created at startup with the correct schema (EF Core forbidden — use Dapper or raw ADO.NET)
- [ ] Migration from `data/quotes.json` → SQLite on first run (or a seeder that imports the JSON)
- [ ] `IQuoteService` implementation backed by SQLite replacing `QuoteService` (flat-file)
- [ ] All v1 read endpoints behave identically to v1 flat-file (existing tests pass unchanged)
- [ ] Docker volume at `/app/data` persists the `.db` file across restarts
- [ ] `.gitignore` excludes `data/*.db`

---

## Architecture Decisions

### Flat-file JSON for v1, SQLite for v2
`data/quotes.json` is loaded into memory at startup. No database in v1. SQLite migration is planned for v2 when write endpoints and user management are added.

**SQL injection policy (mandatory for v2):** All database access must use parameterised queries or a query builder that parameterises automatically. Never build SQL strings by concatenating user input. This applies to every parameter that originates from an HTTP request — `id`, `q`, `type`, `genre`, `lang`, `page`, `pageSize`. The same inputs that reach the in-memory service in v1 will reach the database in v2; the v1 input validation layer is the first defence, parameterised queries are the second.

### Project structure
```
src/Quotinator.Api/      # ASP.NET Core — REST endpoints + Blazor Server UI (combined)
src/Quotinator.Core/     # Shared — models, interfaces, services
data/quotes.json         # The quote dataset
scripts/seed.csx         # Seed/merge/dedup script (dotnet-script)
docker/Dockerfile        # Multi-stage build, targets linux/amd64 + linux/arm64
addon/                   # Home Assistant add-on manifest and assets
```

### Why Quotinator.Api hosts the Blazor UI

The Web and API were merged into a single project so that Quotinator ships as one container. This is required for the Home Assistant add-on (the HA supervisor runs single-container add-ons) and simplifies all deployment scenarios. The Blazor UI and REST endpoints share one process, one port, and one image.

### Quote schema (canonical)
All quotes must conform to this schema in `quotes.json`:
```json
{
  "id": "uuid-v4",
  "quote": "The actual quote text.",
  "originalLanguage": "en",
  "source": "Film / Book / Show title or speech occasion",
  "date": "1994",
  "character": "Character Name",
  "author": "Book author or person who said it",
  "type": "movie",
  "genres": ["drama"],
  "translations": {
    "nl": { "quote": "...", "source": "..." }
  }
}
```

Field notes:
- `id`: UUID v4, generated at seed time, never changes
- `originalLanguage`: ISO 639-1 code; defaults to `"en"` for the vast majority of entries
- `source`: film title, TV series, book title, or speech occasion — replaces the old `movie` field
- `date`: ISO 8601, as precise as the source allows — `"1994"`, `"1940-06"`, or `"1940-06-04"`
- `character`: optional; fictional character for movie/tv/anime/book fiction entries
- `author`: optional; book's author or the real person (for `person` type)
- `type`: `movie`, `tv`, `anime`, `book`, or `person`
- `genres`: array of genre tags; standard values below
- `translations`: manually curated only — never auto-generated

**Standard genre tags:** `action`, `adventure`, `animation`, `comedy`, `drama`, `fantasy`, `fiction`, `horror`, `mystery`, `non-fiction`, `romance`, `sci-fi`, `thriller`

### API response language
All read endpoints accept an optional `lang` query parameter (ISO 639-1). If the requested language has no translation, the response falls back to `originalLanguage` transparently. The response always includes:
- `language` — the language actually returned
- `originalLanguage` — the source language
- `isTranslated` — `true` when `language != originalLanguage`

### API versioning
All endpoints are prefixed `/api/v1/`. Always version from the start.

### Configuration
Sensitive or environment-specific config (API keys, ports, data paths) goes in environment variables or `appsettings.local.json`, which is gitignored. Never hardcode these values and never commit them.

### MCP (v3)
Expose at `/mcp` using the official MCP .NET SDK when available. Do not implement in v1.

---

## Data Sources

The `quotes.json` dataset is seeded from two MIT-licensed sources:

| Source | License | Schema |
|---|---|---|
| [vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes) | MIT | `{ quote, movie }` |
| [NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes) | MIT | `{ quote, movie, type, year }` |

Both are attributed in `SOURCES.md`. The seed/merge/dedup script lives at `scripts/seed.csx`.

Additional curated entries (books, famous people) are added manually and must be accurately attributed.

---

## Testing Policy

See [`docs/testing-policy.md`](docs/testing-policy.md).

---

## What NOT to do

- Do not use Entity Framework in v1 — flat-file JSON only
- Do not add authentication in v1 — API is read-only in this phase
- Do not implement the Blazor UI until v1 REST API phase gates are complete
- Do not add NuGet packages without a clear reason — keep the dependency footprint small
- Do not build SQL strings by concatenating user input in v2 — always use parameterised queries
- Do not change the quote schema without updating this file and `README.md`
- Do not generate or invent quotes — all quotes must come from the seeded dataset or be manually added
- Do not auto-translate quotes — translations must be manually curated
- Do not commit secrets, local IPs, or environment-specific configuration

---

## Key Files

| File | Purpose |
|---|---|
| `README.md` | Public-facing project documentation and roadmap |
| `CLAUDE.md` | This file — AI assistant context |
| `SOURCES.md` | Attribution for seed data |
| `data/quotes.json` | The quote dataset |
| `scripts/seed.csx` | Seed/merge/dedup script |
| `src/Quotinator.Api/Program.cs` | API entry point |
| `src/Quotinator.Core/Models/Quote.cs` | Canonical Quote model |
| `src/Quotinator.Core/Models/QuoteTranslation.cs` | Translation entry model |
| `src/Quotinator.Core/Models/QuoteResponse.cs` | API response DTO |
| `docker/Dockerfile` | Container build |
| `.gitignore` | Must exclude `appsettings.local.json`, `.env`, and `data/*.db` |

---

## MagicMirror Integration (example consumer)

The intended v1 consumer calls the random endpoint and maps the response to the format expected by the MagicMirror² compliments module:

```bash
curl -s "http://quotinator:8080/api/v1/quotes/random?n=20&lang=nl" \
  | jq '[.[] | {quote: .quote, author: ((.character // .author // "Unknown") + " — " + .source)}]' \
  > compliments.json
```

The actual host, port, and file path are configured in the consumer environment, not in this repo.

---

## Pre-Push Checklist

Run these checks before pushing any commit or tag. Tests alone do not cover all failure modes — the Docker build in particular is only verified here and in the release workflow.

1. **Build clean** — `dotnet build --configuration Release` must report `0 Warning(s)  0 Error(s)`
2. **Tests pass** — `dotnet test --configuration Release --verbosity normal` must report all tests passed with `0 Warning(s)  0 Error(s)`
3. **Changelog updated** — add an entry to `CHANGELOG.md` under `[Unreleased]` for any user-visible change; move entries to a versioned section when tagging a release
4. **Docker build succeeds** — run a local build to catch publish/container issues before they hit CI:
   ```bash
   docker build -f docker/Dockerfile -t quotinator:local .
   ```
   If you do not have Docker available, note this explicitly and let the reviewer know CI is the first Docker gate.
4. **Smoke-test the image** (optional but recommended for Dockerfile changes):
   ```bash
   docker run --rm -p 8080:8080 quotinator:local
   curl -s http://localhost:8080/api/v1/health
   curl -s http://localhost:8080/api/v1/quotes/random
   ```

> The CI pipeline runs `dotnet publish` and asserts `data/quotes.json` is present in the output, but it does **not** build the Docker image. The release workflow builds the image on tag push — by that point a failure blocks the release. Always do step 2 locally before tagging.

---

## Session Handoff Notes

Use this section to leave notes for the next session. Clear entries once the work they describe is complete.

**Background task not killed by PowerShell** — in this session, a `dotnet run` background task ("Run API and test random endpoint") persisted despite `Stop-Process` calls and had to be manually stopped from the background tasks panel. If this recurs, investigate whether the process is being relaunched by a watcher or VS, and whether there is a more reliable way to stop it.

**GHCR package must be public for HA add-on installs** — the `ghcr.io/dutchjafo/quotinator` package on GitHub must be set to **Public** (GitHub → Packages → quotinator → Package settings → Change visibility). The Home Assistant Supervisor pulls the image without credentials; a private package returns 401 and the add-on fails to install. This is a one-time manual step in GitHub settings — it is not controlled by code or CI.

**Dependabot not configured** — add `.github/dependabot.yml` to enable automated dependency updates for NuGet packages and GitHub Actions. Keeps dependencies current without manual tracking.

**Release workflow runs in parallel with CI** — the Release and CI workflows trigger independently on a tag push, with no guarantee CI passes before the Docker image is built and pushed. Consider adding a `workflow_run` trigger to the release workflow so it only starts after CI completes successfully. This prevents a broken build from producing a published release.

**`actions/checkout@v4` Node 20 warning** — after updating to `@v4`, GitHub Actions still reports this action targets Node.js 20. The error is a warning only (build succeeds). Investigate whether a specific patch version of `actions/checkout@v4` resolves this, or whether it requires waiting for an upstream release. See: https://github.blog/changelog/2025-09-19-deprecation-of-node-20-on-github-actions-runners/

**v2 SQLite backend** — next session starting point.

- Replace `QuoteService` (flat-file JSON) with a SQLite-backed implementation.
- EF Core is forbidden — use Dapper (preferred, minimal footprint) or raw `Microsoft.Data.Sqlite`.
- All parameterised query rules from the Architecture Decisions section apply from day one.
- `IQuoteService` contract stays the same — no API surface changes.
- Seeding strategy: on first run, if the DB is empty, import from `data/quotes.json` so existing deployments migrate automatically.
- The `.db` file lives in `/app/data/` (same Docker volume as the JSON file).
- Add `data/*.db` to `.gitignore` before the first run.
- Update the phase gates in this file and the roadmap in `README.md` as items are completed.
