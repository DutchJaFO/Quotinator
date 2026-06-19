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

# Run the API locally
dotnet run --project src/Quotinator.Api

# Re-seed data/sources/ from the configured sources (run from repo root)
dotnet-script scripts/seed.csx
dotnet-script scripts/seed.csx -- --dry-run    # preview what would be written without creating files
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

**Phase: v2 — SQLite backend — COMPLETE (tagged 1.0.12)**

v2 phase gates — all done:
- [x] SQLite database created at startup with the correct schema (Dapper + `Microsoft.Data.Sqlite`; EF Core not used)
- [x] Migration from `data/quotes.json` → SQLite on first run
- [x] `IQuoteService` implementation backed by SQLite (`SqliteQuoteService`)
- [x] All v1 read endpoints behave identically to v1 flat-file (all tests pass)
- [x] Docker volume at `/app/data` persists the `.db` file across restarts
- [x] `data/*.db` excluded from `.gitignore`
- [x] `Quotinator.Data` project extracted with reusable infrastructure (`RecordBase`, `SafeValue<T>`, type handlers, connection factory)
- [x] Database startup logging (schema, seeding, stats)

**Phase: v3 — Blazor management UI (next)**

Focus: a Blazor Server management interface for viewing and editing quotes. Requires write endpoints and authentication design first.

Phase gates:
- [ ] Auth design decided (local user accounts, API key, or HA token)
- [ ] Write endpoints (`POST /quotes`, `PUT /quotes/{id}`, `DELETE /quotes/{id}`)
- [ ] Blazor pages: quote list, quote detail/edit, add quote form
- [ ] Input validation and error display in UI

---

## Architecture Decisions

### Flat-file JSON for v1, SQLite for v2
`data/quotes.json` is loaded into memory at startup. No database in v1. SQLite migration is planned for v2 when write endpoints and user management are added.

**SQL injection policy (mandatory for v2):** All database access must use parameterised queries or a query builder that parameterises automatically. Never build SQL strings by concatenating user input. This applies to every parameter that originates from an HTTP request — `id`, `q`, `type`, `genre`, `lang`, `page`, `pageSize`. The same inputs that reach the in-memory service in v1 will reach the database in v2; the v1 input validation layer is the first defence, parameterised queries are the second.

### Project structure
```
src/Quotinator.Constants/ # Route strings, tag names, error message keys — no dependencies
src/Quotinator.Core/      # Models, interfaces, all service implementations
src/Quotinator.Api/       # ASP.NET Core — REST endpoints + Blazor Server UI (combined)
data/sources/             # Bundled source files (one JSON per dataset) + manifest
scripts/seed.csx          # Per-source seed script (dotnet-script)
docker/Dockerfile         # Multi-stage build, targets linux/amd64 + linux/arm64
addon/                    # Home Assistant add-on manifest and assets
```

Dependency direction: `Quotinator.Api` → `Quotinator.Core` → (no deps); `Quotinator.Api` → `Quotinator.Constants` (no deps). Core does not reference Constants.

### Why Quotinator.Api hosts the Blazor UI

The Web and API were merged into a single project so that Quotinator ships as one container. This is required for the Home Assistant add-on (the HA supervisor runs single-container add-ons) and simplifies all deployment scenarios. The Blazor UI and REST endpoints share one process, one port, and one image.

### Quote schema (canonical)
All quotes must conform to this schema (see `schemas/source-flat.schema.json` for the machine-readable version):
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

### Rate limiting

All quote endpoints (`/api/v1/quotes/**`) use a sliding-window rate limiter configured in `Program.cs`:
- **Limit:** 100 requests per minute per IP
- **Window:** 60 seconds, divided into 6 segments of 10 seconds each (`SegmentsPerWindow = 6`)
- **Queue:** none (`QueueLimit = 0`) — requests over the limit are rejected immediately with `429 Too Many Requests`

These values are intentionally generous for homelab use. Change them in `Program.cs` if a consumer (e.g. a bulk import script) legitimately needs a higher limit.

### SSL / HTTPS

Three access patterns exist and are handled differently:

| Access path | TLS handled by | What the app needs |
|---|---|---|
| HA ingress (sidebar) | HA supervisor (TLS termination) | `UseForwardedHeaders()` to read `X-Forwarded-Proto` |
| Direct port, behind reverse proxy | NGINX / Caddy / Traefik (user's proxy) | `UseForwardedHeaders()` only |
| Direct port, raw HTTPS | Kestrel | SSL cert configured in add-on options or env vars |

**ForwardedHeaders** (`UseForwardedHeaders()`) is always enabled. It reads `X-Forwarded-For` and `X-Forwarded-Proto` from any upstream proxy. `KnownNetworks` and `KnownProxies` are intentionally cleared — homelab deployments use trusted LAN proxies, so restricting by IP is unnecessary overhead. **This must be the first middleware in the pipeline** so that all downstream middleware (cookie Secure flags, rate limiting, antiforgery) sees the correct scheme and client IP.

**DataProtection keys** are persisted to a `keys/` subdirectory within the data directory via `PersistKeysToFileSystem`. This prevents antiforgery token decryption failures and Blazor circuit descriptor mismatches after container restarts. Never revert to `UseEphemeralDataProtectionProvider`.

**HA add-on data directory:** The HA supervisor mounts its persistent volume at `/data` inside the container (via `map: data:rw` in `addon/config.yaml`). The add-on env var `Quotinator__DataDir=/data` points the app there. The database (`quotinatordata.db`) and DataProtection keys (`keys/`) are written to this directory. Bundled source files are read directly from the Docker image (`/app/data/sources/`) — no file copy to the persistent volume is needed. User imports can be placed in `{dataDir}/imports/` and are imported after the bundled sources.

**Data directory fallback for HA:** The HA supervisor should apply `Quotinator__DataDir=/data` via `config.yaml` env_vars, but the supervisor may serve a cached config after an update (symptom: startup log shows `Data: /app/data` instead of `Data: /data`). To protect against this, `Program.cs` contains an `HaFallbackDir()` function that checks whether `/data` exists and, if so, uses it as the data directory before falling back to `/app/data`. This ensures the database and DataProtection keys always land on the persistent volume. The priority order is: (1) `Quotinator:DataDir` config value, (2) `/data` if it exists (HA persistent volume), (3) `{AppContext.BaseDirectory}/data` (standalone Docker default). Never remove this fallback.

**Cookie `Secure` flag** is derived from `context.Request.IsHttps` (set correctly by `UseForwardedHeaders()`). Do not hardcode `Secure = true` — it prevents cookies from being sent over plain HTTP in deployments where Quotinator itself is HTTP (behind a proxy or in development).

**Kestrel HTTPS** is configured when `Quotinator:Ssl=true` AND both cert/key files exist AND `DOTNET_RUNNING_IN_CONTAINER=true`. The container check prevents `ListenAnyIP` from conflicting with `launchSettings.json` in VS development. Port 8080 becomes HTTPS; port 8099 stays HTTP (ingress). `ASPNETCORE_HTTP_PORTS` is cleared in the Dockerfile (`ENV ASPNETCORE_HTTP_PORTS=""`); the HA add-on's `addon/config.yaml` sets it to `8099` for the ingress-only port.

**`UseHttpsRedirection` is intentionally absent.** When behind a proxy, redirects would target an unreachable internal port. When Kestrel terminates HTTPS on 8080 there is no HTTP on 8080 to redirect from.

SSL cert paths come from `Quotinator:SslCertFile` and `Quotinator:SslKeyFile`, set via `env_vars` in `addon/config.yaml`. HA's Let's Encrypt add-on writes to `/ssl/fullchain.pem` and `/ssl/privkey.pem` — these are the defaults.

**HA ingress base path (`X-Ingress-Path`)** — the HA supervisor proxies the add-on under a path prefix (e.g. `/api/hassio_ingress/TOKEN/`) and sets the `X-Ingress-Path` request header to that prefix. A custom middleware in `Program.cs` reads this header and applies it as `context.Request.PathBase`. `App.razor`'s `<base href>` is derived from `PathBase` at render time so all relative asset URLs (CSS, `blazor.web.js`, component JS) resolve correctly through the ingress proxy. Without this, all assets resolve against HA's own server root and the Blazor circuit never connects. This middleware runs immediately after `UseForwardedHeaders()`.

**Links in Blazor pages: `target="_blank"` rules differ by destination.** The HA companion app (iOS/Android) forwards `target="_blank"` links to the system browser, which has no HA session cookie. HA then blocks the ingress URL before it reaches Quotinator, producing a 404. Therefore:
- **Internal / HA ingress links** (anything routed through the HA supervisor, including the OpenAPI UI and spec links): must use plain `<a href="…">` without `target="_blank"`.
- **External links** (GitHub, external docs, etc.): must use `target="_blank" rel="noopener noreferrer"`. Without it, the external site loads inside the HA ingress frame and browsers block it via X-Frame-Options, showing an error instead of the page.

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

**When adding or renaming an HA add-on config option — checklist (all in the same commit):**
1. Add/update the option in `addon/config.yaml` (under `options:` and `schema:`)
2. Add/update the entry in `addon/translations/en.yaml` (English — baseline)
3. Add/update the entry in `addon/translations/nl.yaml` (Dutch)
4. Add/update the entry in `addon/translations/de.yaml` (German)

The translation files cover config option names/descriptions and port descriptions only. The `description` field in `config.yaml`, `addon/DOCS.md`, and `addon/README.md` have no HA translation mechanism and remain English-only. See `docs/home-assistant.md` for the full translation scope table.

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

**The `?lang=` query parameter is a separate concern.** It tells `IQuoteService` which language to use when returning *quote content* (translations stored in the source files under `data/sources/`). It does not affect UI strings or error messages — those always follow `Accept-Language`. Do not conflate the two.

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

### Vocabulary and abbreviations

`docs/vocabulary.md` is the authoritative reference for abbreviations and domain terms used in this project. Do not introduce a new abbreviation in code, comments, or documentation without adding it to that file in the same commit. Domain terms that carry a project-specific meaning (especially where a common word is used in a narrower sense) belong there too.

This policy does not affect XML `<summary>` tags — those follow standard C# documentation conventions and are a build requirement independent of the vocabulary.

### Code comments

Two separate rules:

1. **XML `<summary>` tags are required on all non-private types, methods, and properties** in `Quotinator.Core` and `Quotinator.Data`. The build enforces this (CS1591 is active; 0 warnings policy applies). Use `/// <inheritdoc/>` on interface implementations and method overrides rather than duplicating the parent summary. In `Quotinator.Api`, CS1591 is suppressed because the I18nText source-generated `UI` class cannot be annotated — add summaries manually to all Api source files without build enforcement.

2. **No inline `//` comments that explain *what* the code does** — well-named identifiers do that. Only add an inline comment when the *why* is non-obvious: a hidden constraint, a subtle invariant, a workaround for a specific quirk, or a configuration value whose purpose isn't clear from its name.

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

### OpenAPI and Scalar documentation language

The Scalar API reference (`/scalar/v1`) and the raw OpenAPI spec (`/openapi/v1.json`) are **English-only by deliberate decision** (verified 2026-06-14 against current specs):

- **OpenAPI 3.1 has no native localisation mechanism** for spec content (descriptions, summaries, titles). Providing translations requires maintaining separate spec files per language — non-standard and unsupported by any tooling in the ecosystem.
- **Scalar has no UI language configuration.** The Scalar interface chrome (buttons, navigation, labels) is English-only and cannot be configured by the API provider.
- **Developer tooling is English by convention globally.** Virtually all public REST APIs publish English-only API documentation regardless of the developer's country or language selection.
- **Parameter descriptions are compile-time constants** (`[Description]` attributes) and cannot be changed per-request, so full translation would not be achievable even in principle.

Do not attempt to translate OpenAPI spec content or Scalar UI text. Revisit this decision only if:
- The OpenAPI specification adds native localisation support, or
- Scalar adds a documented API for configuring the UI display language.

---

## Data Sources

Each source produces one file in `data/sources/`. Two MIT-licensed external sources are bundled:

| Source | Output file | License | Schema |
|---|---|---|---|
| [vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes) | `vilaboim_movie-quotes.json` | MIT | `{ quote, movie }` |
| [NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes) | `NikhilNamal17_popular-movie-quotes.json` | MIT | `{ quote, movie, type, year }` |

Both are attributed in `SOURCES.md`. The seed script lives at `scripts/seed.csx` — run it to regenerate the source files from upstream; it writes `manifest.json` only when it does not already exist.

Manually curated and verified entries live in `data/sources/quotinator-curated.json`. All entries must be accurately attributed and verified before adding.

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
| `data/sources/` | Bundled source files — one JSON per dataset + `manifest.json` |
| `data/sources/quotinator-curated.json` | Manually verified curated entries |
| `scripts/seed.csx` | Per-source seed script — writes one file per source, manifest only when missing |
| `src/Quotinator.Api/Program.cs` | API entry point |
| `src/Quotinator.Core/Models/Quote.cs` | Canonical Quote model |
| `src/Quotinator.Core/Models/QuoteTranslation.cs` | Translation entry model |
| `src/Quotinator.Core/Models/QuoteResponse.cs` | API response DTO |
| `docker/Dockerfile` | Container build |
| `docs/docker.md` | Docker build notes, Blazor static web assets caveat, port configuration |
| `.gitignore` | Must exclude `appsettings.local.json`, `.env`, and `data/*.db` |

---

## Visual Studio Solution (Quotinator.slnx)

The solution file is the source of truth for what is visible in Visual Studio. The rule is: **all files relevant to the project must be included as solution items, except generated binaries** (build output, `.db` files, etc.).

### Folder syntax

The `.slnx` format does **not** support nested `<Folder>` elements. Subfolders must be declared as flat top-level `<Folder>` elements with path-style names. Nesting a `<Folder>` inside another `<Folder>` causes the inner folder and its files to be invisible in Visual Studio Solution Explorer.

```xml
<!-- Wrong: nested Folder inside Folder -->
<Folder Name="/docs/">
  <Folder Name="/docs/workflow/">   ← invisible in VS
    <File Path="docs/workflow/process.md" />
  </Folder>
</Folder>

<!-- Correct: flat top-level elements with path-style names -->
<Folder Name="/docs/">
  <File Path="docs/README.md" />
</Folder>
<Folder Name="/docs/workflow/">
  <File Path="docs/workflow/process.md" />
</Folder>
```

Source: verified against [microsoft/vs-solutionpersistence](https://github.com/microsoft/vs-solutionpersistence) — their own `SolutionPersistence.slnx` uses this flat pattern.

Current folders and their contents:
- `/Solution Items/` — `CLAUDE.md`, `README.md`, `SOURCES.md`, `CHANGELOG.md`
- `/addon/` — all Home Assistant add-on files (`config.yaml`, `README.md`, `DOCS.md`, `CHANGELOG.md`, `icon.png`, `logo.png`)
- `/data/sources/` — `manifest.json`, `quotinator-curated.json`, `vilaboim_movie-quotes.json`, `NikhilNamal17_popular-movie-quotes.json`
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
3. **Changelog updated** — add entries to `CHANGELOG.md` under `[Unreleased]` as changes land. When tagging a release, promote the `[Unreleased]` block to a versioned heading (`## [x.y.z] - YYYY-MM-DD`) and **remove the `[Unreleased]` header entirely** — do not leave an empty section. Add the `[Unreleased]` header back only when the next change is ready to document. Every versioned section must have a `### Highlights` block in plain user-facing English — this is the only part shown in the Blazor UI. For purely internal releases use a short generic phrase (e.g. `Bug fix — no user-facing changes`). The `addon/CHANGELOG.md` uses a flat bullet list per version with no `### Added/Fixed/Changed` subsections (HA convention); update it alongside the root changelog.
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

> The CI pipeline runs `dotnet publish` and asserts `data/sources/` is present and non-empty in the output, but it does **not** build the Docker image. The release workflow builds the image on tag push — by that point a failure blocks the release. Always do step 5 locally before tagging.

## Tagging a release — separate push cycle

**Always tag in a separate commit/push cycle from feature work.** The reason: Dependabot may open PRs shortly after a push (NuGet and GitHub Actions updates run weekly). Merging those before tagging means the release includes up-to-date dependencies rather than shipping a version that is immediately out of date.

Workflow:
1. **At the start of a session** — check for open Dependabot PRs (`gh pr list --state open`) and merge any that are green before starting feature work. This avoids Dependabot reacting to your push mid-session.
2. Push all feature/fix commits to `main`
3. Wait for any remaining Dependabot PRs to finish CI
4. Review and merge passing Dependabot PRs
5. `git pull` to bring dependency bumps onto your local branch
6. Update `CHANGELOG.md` and `addon/CHANGELOG.md` with the dependency bump entries
7. Bump versions (`csproj`, `addon/config.yaml`, both changelogs) and commit
8. Run the full pre-push checklist above (including Docker build)
9. Push the version bump commit, then push the tag:
   ```bash
   git tag v1.0.x
   git push origin v1.0.x
   ```

> **Cloud/mobile Claude Code cannot push tags.** The Claude Code cloud and mobile environments receive a `403` when attempting `git push origin <tag>`. Tag pushes must always be done from a local terminal. This is a known platform limitation — do not attempt workarounds or assume the push failed for another reason.

---

## Issue and improvement tracking

Bugs, defects, and planned improvements are tracked as **GitHub Issues**. Do not maintain lists here. Only add a temporary note in this file if something is discovered mid-session and has not yet been filed as a GitHub Issue.

**Closing protocol:** See `CONTRIBUTING.md` — issues are either code-verified (closed automatically via `Fixes #N` in the commit) or deployment-verified (closed manually via `gh issue close` after confirming in the live HA add-on). Deployment-verified issues are tracked in `project_post_deploy_verification.md` in memory until confirmed.

**Deployment-only issues** — anything involving HA ingress routing, supervisor log output, add-on config panel, or container restart behaviour must be classified as deployment-verified and added to the memory checklist before the release.

---

## Next Milestone: v3 — Blazor Management UI

Starting point for the next development session.

- Auth design must come first — decide between local user accounts, API key, or HA token before writing any write endpoint
- Write endpoints (`POST`, `PUT`, `DELETE` on `/api/v1/quotes`) with full input validation and SQL injection protection (parameterised queries already required by architecture policy)
- Blazor pages: quote list, quote detail/edit, add quote form
- `IQuoteService` will need write methods — extend the interface when auth design is settled
- Existing read endpoints and tests must remain unchanged
