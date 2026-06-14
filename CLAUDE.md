# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

It is the primary context document for AI assistants working in this repository. Read this before doing anything else.

---

## Commands

```bash
# Build (must be 0 warnings, 0 errors)
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release --verbosity normal

# Run a single test by name filter
dotnet test --configuration Release --filter "FullyQualifiedName~GetRandom_NoN_ReturnsSingleQuote"

# Run only one test project
dotnet test tests/Quotinator.Core.Tests --configuration Release

# Run the API locally (data path defaults to data/quotes.json relative to output)
dotnet run --project src/Quotinator.Api

# Re-seed data/quotes.json from the configured sources (run from repo root)
dotnet-script scripts/seed.csx
dotnet-script scripts/seed.csx -- --dry-run    # preview stats without writing
dotnet-script scripts/seed.csx -- --no-fetch   # use scripts/cache/ instead of downloading

# Build the Docker image locally (required before tagging a release)
docker build -f docker/Dockerfile -t quotinator:local .
```

The Scalar API reference is at `/scalar/v1` and the OpenAPI spec at `/openapi/v1.json` — available in all environments including production.

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

### Localisation — two concerns, one string store

All translated UI strings and API error messages live in a single set of JSON files:

```
src/Quotinator.Api/i18ntext/UI.en-GB.json   ← English baseline (source of truth)
src/Quotinator.Api/i18ntext/UI.de.json
src/Quotinator.Api/i18ntext/UI.nl.json
```

**Rule:** every key that exists in `UI.en-GB.json` must exist (non-empty) in every other file. The test `TranslationCompletenessTests` enforces this.

**When adding a new UI string — checklist (all in the same commit):**
1. Add the key to `UI.en-GB.json`
2. Add translations to `UI.de.json`, `UI.nl.json`, and `UI.en-GB.json`
3. Reference it in the Razor component as `@Text.KeyName` — **never hardcode English (or any language) directly in `.razor` markup**

`TranslationCompletenessTests` catches missing or empty keys but does NOT detect hardcoded strings in markup. That is a code review gate.

**How each consumer uses these files:**

- **Blazor UI** (`Toolbelt.Blazor.I18nText`) — injects `II18nText` and calls `GetTextTableAsync<UI>(this)` in Razor components. Language is resolved from the browser/session context.
- **API error messages** (`IApiLocalizer`) — reads the same JSON files at startup into a dictionary. The `IApiLocalizer` indexer (`localizer[ApiMessages.SomeKey]`) resolves to `CultureInfo.CurrentUICulture`, which `RequestLocalizationMiddleware` sets from the `Accept-Language` request header. Inject `IApiLocalizer` into endpoint handlers via DI.
- `ApiMessages.cs` contains only the string constants (keys) used to look up messages via `IApiLocalizer`. It has no dictionary or translation logic.

**Why `IApiLocalizer` does not use the generated `UI` class:**

`Toolbelt.Blazor.I18nText` generates a `Quotinator.Api.I18nText.UI` class at build time (compiled directly into the assembly — there is no `.cs` source file). It is populated via `await I18nText.GetTextTableAsync<UI>(this)`, where `this` is a Blazor `IComponent`. This makes it unsuitable for REST endpoint use for three reasons:
1. It is async — minimal API handlers are synchronous at the point of localisation.
2. It requires a Blazor component owner (`this`) for re-render signalling.
3. It resolves language from the **Blazor circuit context** (browser session), not from `CultureInfo.CurrentUICulture` — so it would ignore the `Accept-Language` header on REST calls.

`IApiLocalizer` solves all three: it reads the JSON files once at startup, and at call time resolves via `CultureInfo.CurrentUICulture` which the middleware has already set correctly. Do not replace it with `II18nText`.

**The `?lang=` query parameter is a separate concern.** It tells `IQuoteService` which language to use when returning *quote content* (translations in `quotes.json`). It does not affect UI strings or error messages — those always follow `Accept-Language`. Do not conflate the two.

### Language selector — UI culture override

The navbar `LanguageSelector` control (`Components/Controls/LanguageSelector.razor`) lets users override the browser's `Accept-Language` preference. It submits a GET form to `/Culture/Set?culture={code}&redirectUri={path}`, which sets the `.AspNetCore.Culture` cookie (`c={code}|uic={code}`) and redirects back using `TypedResults.LocalRedirect` (prevents open-redirect attacks). The cookie is read by `CookieRequestCultureProvider` (one of the default providers in `RequestLocalizationOptions`) on every subsequent request.

**Cookie options:** `MaxAge = 365 days`, `IsEssential = true` (no cookie consent banner needed — language preference is functional), `SameSite = Lax` (blocks CSRF cross-site POSTs while allowing top-level navigations), `Secure = true` (HTTPS only — Quotinator is always served behind TLS in production, either via HA ingress or a reverse proxy). Do not remove these flags without an explicit team decision.

**`<html lang>` must be dynamic.** `App.razor` derives `lang` from `CultureInfo.CurrentUICulture.Name` via `App.razor.cs`. This satisfies WCAG SC 3.1.1 (Language of Page — Level A). Never hardcode `lang="en"` — screen readers use this attribute to select the correct pronunciation engine.

**Do not use** `NavigationManager.NavigateTo(..., forceLoad: true)` for this — that requires `InteractiveServer` render mode. The plain HTML form approach works in static SSR and requires no Blazor circuit.

**`@code` is a Razor reserved keyword.** Never use `code` as a loop variable name in `.razor` files — `@code` will be parsed as the `@code` directive. Use `cultureCode`, `langCode`, or similar instead.

### Endpoint test pattern

Endpoint tests use `WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) and replace `IQuoteService` with `FakeQuoteService` via `WithWebHostBuilder`. See `tests/Quotinator.Api.Tests/Endpoints/QuoteEndpointsTests.cs` for the canonical pattern. The `public partial class Program { }` line at the bottom of `Program.cs` is required to expose the entry point to the test project.

### Route registration order

`/search` is registered before `/{id}` in `QuoteEndpoints.cs` so the literal segment takes priority over the catch-all parameter. Preserve this order.

### Blazor code style

These rules apply to all Blazor components and pages:

1. **Folder layout** — controls go in `Components/Controls/`, pages in `Components/Pages/`, layout components in `Components/Layout/`. No components at the `Components/` root level.
2. **Always use code-behind files** — every `.razor` file has a paired `.razor.cs` partial class, even if it contains only the namespace and class declaration. No inline `@code { }` blocks, no `@inject` directives. Move `@inject` to `[Inject]` properties and `@using System.*` to the `.razor.cs` using list. The only exception is if the Blazor framework itself does not support a code-behind partial for that file type. Any other potential exception must be raised explicitly and decided by the team — never assumed or decided unilaterally.
3. **Member sort order** — public first, then protected, then private. Within each group: constructors, methods, properties, fields (standard C# convention).
4. **Regions** — use `#region Protected` / `#region Private` (etc.) whenever a class has members from more than one access-modifier group. Omit regions when all members share one modifier level.
5. **Namespace for generated `UI` class** — `Toolbelt.Blazor.I18nText` is both a namespace and a type. In `.razor.cs` files, alias the service: `using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;` and declare the property as `[Inject] private I18nTextService I18nText { get; set; } = default!;`.
6. **`[Inject]` requires `using Microsoft.AspNetCore.Components;`** — `.razor.cs` files do not inherit `_Imports.razor` usings; always add this using explicitly.

### Keeping API documentation in sync

When adding, removing, or changing any endpoint, parameter, or behaviour, update **all three** of these in the same commit:

1. `README.md` — the REST API Endpoints table and any parameter descriptions
2. `addon/DOCS.md` — the API Endpoints table (HA add-on users read this)
3. `src/Quotinator.Api/Endpoints/QuoteEndpoints.cs` — the `[Description]` attributes on the endpoint and its parameters (these feed the OpenAPI/Scalar UI)

The Scalar API reference is at `/scalar/v1` and the raw spec at `/openapi/v1.json` — both are available in all environments including production. Do not gate them behind `IsDevelopment()`.

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
- Do not add translated strings outside the `i18ntext/UI.*.json` files — that is the single source of truth for all UI and error message translations
- Do not use `?lang=` to drive error message language — error messages use `Accept-Language` via `IApiLocalizer`; `?lang=` is only for quote content language

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

## Visual Studio Solution (Quotinator.slnx)

The solution file is the source of truth for what is visible in Visual Studio. The rule is: **all files relevant to the project must be included as solution items, except generated binaries** (build output, `.db` files, etc.).

Current folders and their contents:
- `/Solution Items/` — `CLAUDE.md`, `README.md`, `SOURCES.md`, `CHANGELOG.md`
- `/addon/` — all Home Assistant add-on files (`config.yaml`, `README.md`, `DOCS.md`, `CHANGELOG.md`, `icon.png`, `logo.png`)
- `/data/` — `quotes.json`
- `/docker/` — `Dockerfile`, `docker-compose.yml`
- `/scripts/` — `seed.csx`, `sources.json`, `SOURCES.md`
- `/src/` — C# projects
- `/tests/` — test projects

When adding new files to the repo, add them to the appropriate solution folder in `Quotinator.slnx` as well.

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

> **GitHub CLI auth:** if you see "GitHub CLI authentication expired", run `gh auth login` (choose GitHub.com → HTTPS → browser) before proceeding.

Run these checks before pushing any commit or tag. Tests alone do not cover all failure modes — the Docker build in particular is only verified here and in the release workflow.

1. **Build clean** — `dotnet build --configuration Release` must report `0 Warning(s)  0 Error(s)`
2. **Tests pass** — `dotnet test --configuration Release --verbosity normal` must report all tests passed with `0 Warning(s)  0 Error(s)`
3. **Changelog updated** — add an entry to `CHANGELOG.md` under `[Unreleased]` for any user-visible change; move entries to a versioned section when tagging a release
4. **Versions in sync** — when tagging a release, all three must match the tag (without the `v` prefix):
   - `src/Quotinator.Api/Quotinator.Api.csproj` → `<Version>`
   - `addon/config.yaml` → `version`
   - `CHANGELOG.md` and `addon/CHANGELOG.md` → versioned section heading
5. **Docker build succeeds** — run a local build to catch publish/container issues before they hit CI:
   ```bash
   docker build -f docker/Dockerfile -t quotinator:local .
   ```
   If you do not have Docker available, note this explicitly and let the reviewer know CI is the first Docker gate.
6. **Smoke-test the image** (optional but recommended for Dockerfile changes):
   ```bash
   docker run --rm -p 8080:8080 quotinator:local
   curl -s http://localhost:8080/api/v1/health
   curl -s http://localhost:8080/api/v1/quotes/random
   ```

> The CI pipeline runs `dotnet publish` and asserts `data/quotes.json` is present in the output, but it does **not** build the Docker image. The release workflow builds the image on tag push — by that point a failure blocks the release. Always do step 5 locally before tagging.

---

## Issue and improvement tracking

Bugs, defects, and planned improvements are tracked as **GitHub Issues**. Do not maintain lists here. Only add a temporary note in this file if something is discovered mid-session and has not yet been filed as a GitHub Issue.

---

## Next Milestone: v2 — SQLite Backend

Starting point for the next development session.

- Replace `QuoteService` (flat-file JSON) with a SQLite-backed implementation
- **User has an existing Dapper repository class with built-in schema versioning/migration support from a prior project — locate and reuse this before writing anything from scratch**
- EF Core is forbidden — use Dapper
- All parameterised query rules from the Architecture Decisions section apply from day one
- `IQuoteService` contract stays the same — no API surface changes
- Seeding strategy: on first run, if the DB is empty, import from `data/quotes.json` so existing deployments migrate automatically
- The `.db` file lives in `/app/data/` (same Docker volume as the JSON file)
- Add `data/*.db` to `.gitignore` before the first run
- Switch DataProtection from `UseEphemeralDataProtectionProvider()` to `PersistKeysToFileSystem(new DirectoryInfo("/app/data"))` so keys survive container restarts
- Update the phase gates in this file and the roadmap in `README.md` as items are completed
