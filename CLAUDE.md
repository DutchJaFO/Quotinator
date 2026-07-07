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

# Regenerate a data/sources/ file locally (run the app, then force-refresh via the admin endpoint —
# see scripts/SOURCES.md for the full converter-plugin workflow)
dotnet run --project src/Quotinator.Api
curl -X POST -H "X-Api-Key: <your admin key>" "http://localhost:5000/api/v1/admin/sources/refresh?force=true"

# Build the Docker image locally (required before tagging a release)
docker build -f docker/Dockerfile -t quotinator:local .

# Install git hooks (run once per clone — prevents accidental GitHub issue auto-close via commit message)
cp scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/commit-msg
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

Active milestones, open issues, and development priorities are tracked in GitHub — not here. This section is intentionally brief to avoid going stale.

- **Milestones:** https://github.com/DutchJaFO/Quotinator/milestones
- **Issues:** https://github.com/DutchJaFO/Quotinator/issues

---

## Authoritative sources

**Code is never authoritative on its own — only evidence of what was actually done, which may itself be wrong.** Before making a design or scope decision, check sources in this order:

1. **Official documentation** for the language, framework, or library involved (e.g. SQLite's own docs for what `ALTER TABLE` can and can't do).
2. **This project's own documentation** — `docs/architecture-decisions/` (ADRs — formal, numbered, permanent decisions) first, then `docs/decisions/` (informal/in-progress notes), then the relevant milestone plan doc, then this file.
3. **If neither has an answer, ask the user.** Never silently pick an option and proceed as if it were settled.

**Existing code that looks like a pattern is not the same as a validated decision.** Copying what an earlier entity/class/table already does is not a substitute for checking whether that earlier code actually complied with a governing ADR — it may itself be the mistake propagating. This is exactly how `SystemAuditEntry` (#73) shipped without `RecordBase` despite ADR 002 mandating it "without exception": the ADR existed a week before the implementation, nobody checked it, and the next two entities (`SystemImportConflict`, then `ChangeLogEntry`) each copied the previous one's shape instead of checking the ADR independently, compounding the same deviation three times before it was caught (see ADR 002 for the full incident).

**Always check `docs/architecture-decisions/` before designing a new entity, table, or repository pattern** — not just the milestone's own plan docs. An ADR can govern a decision the current GitHub issue never mentions.

---

## Architecture Decisions

### Flat-file JSON for v1, SQLite for v2
`data/quotes.json` is loaded into memory at startup. No database in v1. SQLite migration is planned for v2 when write endpoints and user management are added.

**SQL injection policy (mandatory for v2):** All database access must use parameterised queries or a query builder that parameterises automatically. Never build SQL strings by concatenating user input. This applies to every parameter that originates from an HTTP request — `id`, `q`, `type`, `genre`, `lang`, `page`, `pageSize`. The same inputs that reach the in-memory service in v1 will reach the database in v2; the v1 input validation layer is the first defence, parameterised queries are the second.

**Schema migration policy:** Migrations are numbered, append-only sequences in `DatabaseInitializer.Migrations`. Rules that must be followed for every migration:

- **Never reorder or edit an existing migration** — once applied to a real database, a migration is frozen. Changing it silently corrupts installations that already ran it.
- **Every DDL statement must be idempotent where SQLite allows it.** Use `CREATE TABLE IF NOT EXISTS` and `DROP TABLE IF EXISTS`. **SQLite has no `IF EXISTS`/`IF NOT EXISTS` form for `ALTER TABLE ... RENAME TO` or `ALTER TABLE ... ADD COLUMN`** (verified against sqlite.org — neither statement's grammar supports it, at any version). A non-idempotent migration that fails partway through leaves the database in a state where the version is not recorded but the schema change was partially applied — causing a never-ending startup crash loop on every subsequent restart. See "No exception-based migration recovery" below for how this project handles statements that can't be made idempotent.
- **One schema change per migration where possible.** Multi-statement migrations are harder to make fully idempotent and harder to reason about when partially applied.
- All migration SQL stays inside `DatabaseInitializer` as `private const string Migration00N_...` — not in `Sql.cs`. Migration text is frozen at migration time and must not be discoverable or modifiable via the `Sql` class.

**Migration ownership split (Data vs. consumer):** `Quotinator.Data` owns migrations for its own tables (currently `System_AuditEntries`; any future `System_`-prefixed table Quotinator.Data itself defines) via a fixed internal list (`DatabaseInitializer.DataOwnedMigrations`) — never passed through the constructor, and never controlled by the consuming project. These always apply first, before any consumer-supplied migration, and are tracked in their own `System_SchemaVersion` table. A consuming project's own domain migrations (e.g. `Quotinator.Engine`'s `QuotinatorMigrations.All`) are tracked independently in `System_ConsumerSchemaVersion`, so "version N" always means the same specific migration for whichever side owns it, unaffected by the other side's migration count changing over time. `IDatabaseInitializer.SchemaVersion` reports the consumer's own version (what operators track release-over-release); `DataSchemaVersion` reports Quotinator.Data's own version separately.

**Baseline schema for fresh databases:** A completely empty database (zero tables of any kind, detected via `Sql.Schema.AnyTableExists`) skips replaying migration history entirely and instead applies a one-step consolidated baseline: `DatabaseInitializer`'s own `DataBaselineSql` (Quotinator.Data's tables) followed by the consumer's `SchemaBaseline.Sql` (e.g. `QuotinatorMigrations.Baseline`, Quotinator.Engine's domain tables). A database with *any* pre-existing table — even just an empty version table — always takes the full incremental path instead; the two paths never cross. **Whenever a new migration is added to either `DataOwnedMigrations` or a consumer's migration list, the corresponding baseline must be updated to match its final result in the same commit** — this is enforced by dedicated schema-drift tests (`DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema` in `Quotinator.Data.Tests`, `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` in `Quotinator.Engine.Tests`) that compare the baseline-created schema against the incrementally-replayed schema and fail on any drift, including in CHECK constraint behaviour (which `PRAGMA table_info` doesn't capture structurally).

**No exception-based migration recovery.** A migration must never rely on catching its own failure to detect an already-applied state — a genuinely different failure with the same error message would be silently misclassified and swallowed, leaving no way to know whether the correct migrations actually applied. Two rules follow from this:

- **Fix the root cause instead of adding a check.** `Reset` (`DropAndRebuildAsync`) never wipes or replays `Quotinator.Data`'s own migration history (`System_SchemaVersion`), regardless of `preserveSchemaVersion` — because Data's migrations only ever concern `System_`-prefixed tables, which a Reset never drops in the first place (see `Sql.Schema.GetUserTables`). Only the consumer's own domain tables and `System_ConsumerSchemaVersion` are actually dropped and replayed. This is what makes the previously-unavoidable rename collision on every Reset simply never happen, with no check of any kind. Structural metadata checks (`sqlite_master`, `pragma_table_info`) are reserved for the single existing whole-database-empty check (`Sql.Schema.AnyTableExists`) — do not add a new one anywhere else as a substitute for catching an exception.
- **A database whose recorded schema version doesn't match its actual on-disk schema is a hard failure, not a self-heal.** If a migration throws for any reason, it is never inspected or interpreted — `ApplyMigrationsAsync` and `DropAndRebuildAsync` back up the database before any destructive step, and on any exception restore that backup and rethrow, leaving the database exactly as it was before the attempt. The operator must run an explicit Reset to resolve a genuine mismatch. `ApplyMigrationPhaseAsync` itself has no `try`/`catch` at all — a failing migration's own transaction rolls back automatically via `using`, and the exception propagates untouched.

### Project structure
```
src/
  Quotinator.Constants/        # Route strings, tag names, error message keys — no dependencies
  Quotinator.Core/             # Domain models, interfaces, and in-memory service implementations
  Quotinator.Data/             # Generic, reusable SQLite/Dapper infrastructure — domain-agnostic
  Quotinator.Data.Testing/     # Test helper library — stubs, fakes, disposable SQLite DB (reference from test projects only)
  Quotinator.Engine/           # SQLite-backed Quotinator domain implementation — bridges Core + Data
  Quotinator.Changelog/        # Changelog schema, models, and generator logic
  Quotinator.Converters.Vilaboim/      # IQuoteSourceConverter plugin: vilaboim/movie-quotes raw format
  Quotinator.Converters.NikhilNamal17/ # IQuoteSourceConverter plugin: NikhilNamal17/popular-movie-quotes raw format
  Quotinator.Api/              # ASP.NET Core — REST endpoints + Blazor Server UI (combined)
tests/
  Quotinator.Api.Tests/             # Endpoint integration tests (WebApplicationFactory)
  Quotinator.Changelog.Tests/       # Changelog schema and generation tests
  Quotinator.Constants.Tests/       # Tests for route and constant definitions
  Quotinator.Converters.Vilaboim.Tests/      # Tests for the Vilaboim converter plugin
  Quotinator.Converters.NikhilNamal17.Tests/ # Tests for the NikhilNamal17 converter plugin
  Quotinator.Core.Tests/            # Unit tests for domain logic and in-memory service
  Quotinator.Data.Example/          # Concrete example implementations of Data patterns (not a test runner)
  Quotinator.Data.Testing.Tests/    # Tests for the Data.Testing helper library
  Quotinator.Data.Tests/            # Integration tests for Data infrastructure (real SQLite, no fakes)
  Quotinator.Engine.Tests/          # Integration tests for Engine (SqliteQuoteService, migrations)
  Quotinator.Tools.DbInspector.Tests/  # Unit tests for the DbInspector dev tool
tools/
  Quotinator.Tools.DbInspector/     # Dev-only CLI: run arbitrary SQL against a Quotinator SQLite file. Never shipped.
data/sources/             # Bundled source files (one JSON per dataset) + manifest
docs/                     # Workflow guides, testing policy, CVE docs, milestone plans
scripts/
  changelog.csx           # Changelog markdown generator
docker/Dockerfile         # Multi-stage build, targets linux/amd64 + linux/arm64
addon/                    # Home Assistant add-on manifest and assets
```

Dependency direction: `Quotinator.Api` → `Quotinator.Engine` → `Quotinator.Core`; `Quotinator.Engine` → `Quotinator.Data`; `Quotinator.Api` → `Quotinator.Constants`. Core and Data have no dependencies on each other or on Engine. `Quotinator.Data.Testing` → `Quotinator.Data` only.

`tools/` holds standalone developer utilities that are never referenced by any `src/` project and never built into the Docker image — they exist purely to support local development/debugging. See `tools/Quotinator.Tools.DbInspector/README.md` for the current example.

### File placement rule

Files at a project root must be kept to a minimum. The only permitted root-level file is `Program.cs` in `Quotinator.Api` (the ASP.NET Core entry point). All other source files must live in a subfolder whose name corresponds to the namespace segment it adds.

**Rules:**
- Folder name = namespace segment after the project root namespace. A file in `Quotinator.Constants/Routes/` must have namespace `Quotinator.Constants.Routes`.
- Namespace must always match folder path — never place a file in a subfolder to organise it while keeping the parent namespace.
- Single-file folders are acceptable when a concept is clearly distinct (e.g. `RateLimiting/RateLimitPolicies.cs`).
- Avoid redundant folder names. A `Data/` subfolder inside `Quotinator.Data` would produce `Quotinator.Data.Data` — rename the folder to something descriptive (e.g. `Connections/`).

**Current layout of `Quotinator.Constants`:**
```
Api/           → Quotinator.Constants.Api        (ApiMessages, ApiTags)
RateLimiting/  → Quotinator.Constants.RateLimiting (RateLimitPolicies)
Routes/        → Quotinator.Constants.Routes     (ApiRoutes, RouteExtensions)
```

**Razor caveat:** `.razor` files are not always caught by the build when a namespace or component reference changes. A `dotnet build` may report 0 errors while a `.razor` file still references the old namespace at runtime. After any namespace refactor, manually check every `.razor` and `_Imports.razor` file that references the changed namespace and run the app to confirm the Blazor UI loads correctly.

### Dependency injection policy

**Default: always use DI registration.** Services, repositories, and infrastructure types must be registered with the DI container and received via constructor injection. Using `new` to instantiate a dependency is a code smell — it bypasses DI, makes testing harder, and prevents lifetime management by the container.

**The only permitted exception:** `new` may be used when the DI container itself cannot supply a required parameter at registration time (e.g. a computed path, a runtime config value, or a factory-constructed primitive). In that case, use the service-provider factory overload (`builder.Services.AddSingleton<T>(sp => new T(sp.GetRequiredService<IDep>(), computedValue))`) rather than a bare `new` call at the call site.

Any use of bare `new` for a type that could reasonably be registered must have a comment explaining why DI was not used.

### JSON parsing policy

**Always deserialize JSON into POCOs via `JsonSerializer.Deserialize<T>` — never walk a parsed document by hand (`JsonNode`/`JsonDocument` indexers, `["field"]`, `GetValue<T>()`) to extract data.** Define a DTO class per JSON shape (e.g. `SourceQuote` for quote files, `ChangelogRoot` for the changelog, `ManifestDto`/`ManifestFileEntryDto`/`ManifestGithubDto`/`ManifestPolicyDto` for `manifest.json`), with `[JsonPropertyName("...")]` on each property mapping the wire name to a PascalCase C# name. If a schema exists (`schemas/*.json`), every field it defines must be representable as a DTO property — a schema field with no corresponding POCO property is a policy violation. The same applies to writing JSON: build a DTO and call `JsonSerializer.Serialize`, never hand-assemble a `JsonObject`/`JsonArray`.

**Enum-valued string fields** (e.g. `"skip"`/`"overwrite"`, `"internal"`/`"external"`) should be typed directly as the C# enum on the DTO property with `[JsonConverter(typeof(JsonStringEnumConverter))]` — `System.Text.Json`'s built-in converter matches enum member names case-insensitively on read, so no manual string-switch mapping is needed for these.

**The only permitted exception:** sniffing which of several top-level shapes a document uses, when the shapes are different enough that a single DTO can't represent both (e.g. `LoadQuotesFromFile` in `QuotinatorDatabaseInitializer.cs` uses one `JsonNode.Parse` call only to check whether the root is a bare array or a `{ "quotes": [...] }` wrapper) — the actual field extraction for whichever shape is chosen must still go through `JsonSerializer.Deserialize<T>` into a POCO, not further manual node walking.

**Why:** manual node walking (`e!["field"]!.GetValue<string>()`) loses compile-time member names, gives worse error messages on type mismatches, and tends to accumulate ad hoc parsing logic (URL resolution, enum coercion, nullability handling) that a typed DTO expresses more clearly. It also invites silent divergence between the JSON schema and what the code actually reads, since nothing forces every schema field to have a corresponding read path. This was found and corrected in `ManifestSeedPlanner.cs`, which had grown into full manual `JsonNode` parsing while the rest of the codebase (`SourceQuote`, `ChangelogRoot`) already used POCOs — see the `Manifest*Dto` classes in `Quotinator.Data/Import/` for the corrected pattern.

### Serilog — programmatic configuration

Serilog is configured entirely in code via `builder.Host.UseSerilog((ctx, _, config) => { ... })` in `Program.cs`. **Do not switch to `ReadFrom.Configuration`** (which reads sink names from `appsettings.json` and uses `DllScanningAssemblyFinder` to locate the corresponding DLL in the app directory).

**Why:** The HA supervisor container sets the `/app` directory as read-only. `DllScanningAssemblyFinder` calls `Directory.GetFiles("/app", ...)`, which throws `UnauthorizedAccessException` and crashes the add-on before it starts. Programmatic configuration has no filesystem scan — sinks are referenced as compiled code, not discovered at runtime.

**Two templates, chosen in code:**
- Development: `{Timestamp:HH:mm:ss} {Level:u3}: ...` (time only, + Debug sink)
- Production: `{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}: ...` (full timestamp)

The `{Message}` token in the output template preserves embedded newlines, which is why the closing startup banner renders as a proper multi-line block in the HA supervisor log. This is the primary reason Quotinator uses Serilog rather than the default Microsoft console formatter.

**HA log level mapping** lives alongside the `UseSerilog` call in `Program.cs`. HA uses string level names (`trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`) that are mapped to `LogEventLevel` values before the logger is built. The mapping must stay in code — it cannot be driven from `appsettings.json` without reintroducing `ReadFrom.Configuration`.

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

### Year parameter binding pattern

`yearFrom`, `yearTo`, `year`, and `decade` are declared as `string?` in handler signatures rather than `int?`. This is deliberate: when declared as `int?`, ASP.NET Core's parameter binder throws `BadHttpRequestException` on invalid input (e.g. `yearFrom=1980x`) and the exception propagates unhandled through the entire middleware stack before being caught accidentally by `UseExceptionHandler`. Declaring them as `string?` lets `TryParseYear()` in `QuoteEndpoints.cs` catch the parse failure at the point of origin and return a 422 immediately.

The downside is that the OpenAPI generator infers `type: string` from the C# type, which is wrong. An operation transformer in `Program.cs` patches the schema back to `type: integer` for the three affected endpoints (`api/v1/quotes`, `api/v1/quotes/random`, `api/v1/quotes/search`). The transformer is scoped explicitly to those paths — do not add any endpoint to that set unless it also uses `TryParseYear`.

**Rules for adding new numeric query parameters:**
- Declare as `string?` and parse with `int.TryParse` (or a dedicated helper) — never `int?`
- Return 422 on parse failure via `Results.Problem`
- Add the endpoint path to the year-param schema transformer in `Program.cs`

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

### String centralisation policy

**Rule: no inline strings for any string that communicates with an external system or user-facing surface. Every such string must live in a named, discoverable location.**

The same principle applies across three domains in this project:

| Domain | Where strings live | Enforcement |
|---|---|---|
| **SQL** | `Quotinator.Core.Data.Sql` — fixed queries as `const` fields, dynamic queries as `static` factory methods | `SqlQueryGuardTests` reflects over `Sql.*` and drives all factory methods with a full filter matrix |
| **UI / error messages** | `src/Quotinator.Api/i18ntext/UI.*.json` — keyed by `ApiMessages` constants | `TranslationCompletenessTests` enforces every key in every locale |
| **OpenAPI descriptions** | `[Description]` attributes in `QuoteEndpoints.cs` | **Permitted exception** — C# requires attribute arguments to be compile-time constants; there is no mechanism to centralise them without losing the attribute. They are English-only by the decision above. |

**What "no inline strings" means in practice:**

- A SQL string typed anywhere outside `Sql.cs` is a violation. If the query is dynamic (WHERE clause appended at runtime), write a factory method in the appropriate `Sql.*` nested class and call it from the service. The method is then testable in isolation.
- A UI string or error message typed anywhere outside an `i18ntext/*.json` file is a violation — including inside `.razor` markup (see localisation checklist).
- When adding a new query or string, the corresponding test (`SqlQueryGuardTests`, `TranslationCompletenessTests`) must pass before the commit is pushed.

**How to audit:**

- SQL: `grep -rn '"SELECT\|"INSERT\|"UPDATE\|"DELETE' src/ --include="*.cs"` — any hit outside `Sql.cs` or migration constants is a violation.
- UI strings: run `dotnet test --filter TranslationCompleteness` — missing or empty keys fail the test.
- Factory method coverage: `SqlQueryGuardTests.AssembledQueryCases` must include a case for every call shape a factory method can produce.

---

## Data Sources

Each source produces one file in `data/sources/`. Two MIT-licensed external sources are bundled:

| Source | Output file | License | Schema |
|---|---|---|---|
| [vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes) | `vilaboim_movie-quotes.json` | MIT | `{ quote, movie }` |
| [NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes) | `NikhilNamal17_popular-movie-quotes.json` | MIT | `{ quote, movie, type, year }` |

Both are attributed in `SOURCES.md`. Each source's raw upstream format is converted to Quotinator's canonical schema by a first-party `IQuoteSourceConverter` plugin (`Quotinator.Converters.Vilaboim`, `Quotinator.Converters.NikhilNamal17`), invoked automatically by the live auto-update mechanism (`Quotinator__AutoUpdateSources`) and manually via `POST /api/v1/admin/sources/refresh` to regenerate a `data/sources/*.json` file locally. See `scripts/SOURCES.md` for the full workflow to add a new source.

Manually curated and verified entries live in `data/sources/quotinator-curated.json`. All entries must be accurately attributed and verified before adding.

---

## Testing Policy

See [`docs/testing-policy.md`](docs/testing-policy.md).

---

## Logging Standards

See [`docs/logging.md`](docs/logging.md).

Boyscout rule: when you edit any file that emits log lines without the `[Subsystem - Phase]` prefix, add the prefix in the same commit. Do not defer it to a cleanup PR.

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
| `CHANGELOG.md` | Generated changelog — do not edit directly |
| `Directory.Build.props` | Shared version number (`<Version>`) — only file to update when bumping |
| `Quotinator.slnx` | Visual Studio solution — all non-generated files must be listed here |
| `data/sources/` | Bundled source files — one JSON per dataset + `manifest.json` |
| `data/sources/quotinator-curated.json` | Manually verified curated entries |
| `schemas/source-flat.schema.json` | Machine-readable quote schema |
| `schemas/changelog.schema.json` | Machine-readable changelog schema — read before writing changelog entries |
| `scripts/SOURCES.md` | Workflow for adding a new quote source via a converter plugin |
| `scripts/changelog.csx` | Changelog markdown generator — run after editing `changelog.en.json` |
| `src/Quotinator.Data/Import/ISourceCacheUpdater.cs` | Live auto-update download/convert/validate pipeline for manifest-declared sources |
| `src/Quotinator.Data/Import/IQuoteSourceConverter.cs` | Converter plugin contract — implement one per raw upstream source format |
| `src/Quotinator.Api/Program.cs` | API entry point |
| `src/Quotinator.Api/resources/changelog.en.json` | Changelog source of truth — edit this, never the generated `.md` files |
| `src/Quotinator.Api/resources/changelog.nl.json` | Dutch changelog (lockstep with `en.json`) |
| `src/Quotinator.Api/resources/changelog.de.json` | German changelog (lockstep with `en.json`) |
| `src/Quotinator.Api/i18ntext/UI.en-GB.json` | English UI string baseline — source of truth for all UI keys |
| `src/Quotinator.Core/Models/Quote.cs` | Canonical Quote model |
| `src/Quotinator.Core/Models/QuoteTranslation.cs` | Translation entry model |
| `src/Quotinator.Core/Models/QuoteResponse.cs` | API response DTO |
| `src/Quotinator.Core/Data/Sql.cs` | All SQL query strings — never write SQL inline outside this file |
| `src/Quotinator.Data/Database/DatabaseInitializer.cs` | SQLite schema + numbered migrations |
| `addon/config.yaml` | HA add-on manifest — version, options, schema, port config |
| `addon/CHANGELOG.md` | Generated HA add-on changelog — do not edit directly |
| `docker/Dockerfile` | Container build |
| `docs/docker.md` | Docker build notes, Blazor static web assets caveat, port configuration |
| `docs/testing-policy.md` | Testing standards — test project pairing, CVE folder rule, parallel execution |
| `docs/workflow/process.md` | Milestone workflow — starting, executing, closing, living and maintenance milestones |
| `docs/workflow/checklist.md` | Issue filing, session-start, issue-closing, and milestone-close checklists |
| `docs/workflow/cve.md` | CVE handling workflow; template is at `docs/workflow/cve-template.md` |
| `docs/security/README.md` | Summary of all known CVEs and their current status across all projects |
| `docs/milestones/` | Per-milestone overview and per-issue plan docs |
| `.gitignore` | Must exclude `appsettings.local.json`, `.env`, and `data/*.db` |
| `src/[project]/CVE/` | Per-project CVE tracking — `CVE-YYYY-NNNNN.md` per alert; closed CVEs in `CVE/archived/` |
| `tools/Quotinator.Tools.DbInspector/` | Dev-only CLI — run arbitrary SQL against a Quotinator SQLite file; see its `README.md` |

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

**Do not add solution folders for files that are already part of a project.** Source files (`.cs`, `.razor`, `.razor.cs`) inside a project directory are visible in Solution Explorer through the project node — listing them again in a `<Folder>` creates a name collision between the folder path and the project's unique identifier and causes the "Solution Folder with the same unique identifier already exists" error. Only use `<Folder>` entries for files that live outside any project (docs, scripts, schemas, config).

Current folders and their contents:
- `/Solution Items/` — `CLAUDE.md`, `README.md`, `SOURCES.md`, `CHANGELOG.md`
- `/addon/` — all Home Assistant add-on files (`config.yaml`, `README.md`, `DOCS.md`, `CHANGELOG.md`, `icon.png`, `logo.png`)
- `/data/sources/` — `manifest.json`, `quotinator-curated.json`, `vilaboim_movie-quotes.json`, `NikhilNamal17_popular-movie-quotes.json`
- `/docker/` — `Dockerfile`, `docker-compose.yml`
- `/scripts/` — `SOURCES.md` and changelog scripts
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
2. **Tests pass** — `dotnet test --configuration Release --verbosity normal` must report all tests passed with `0 Warning(s)  0 Error(s)`. The same 0-warnings policy that applies to `dotnet build` applies here — any compiler warning surfaced during test build is a blocking failure.
3. **Changelog updated** — `src/Quotinator.Api/resources/changelog.en.json` is the source of truth for all changelog content. **Never edit `CHANGELOG.md` or `addon/CHANGELOG.md` directly — they are generated files.**

   **Before writing any entries, read `schemas/changelog.schema.json`** — it is the authoritative definition of every field and which fields are required. Do not infer the format from prior entries or git history.

   **During development — at issue close time** (not deferred to release): add entries to the `unreleased` section at the top of `changelog.en.json`. Include the issue number in `unreleased.issues`. This follows the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) `[Unreleased]` convention and keeps the changelog in sync without waiting for a release. Decide at the time of writing whether the change deserves a `highlights` entry (user-facing impact) or only `added`/`changed`/`fixed`/`removed` (technical). See `docs/workflow/checklist.md` → "Before closing an issue" for the full closing step.

   **Release issue-list rule:** every release entry whose work traces back to a specific issue must carry that issue's number in its `issues[]` array — including hotfix releases spawned by the same issue. Example: issue #100 spawned both v1.6.3 (primary Serilog change) and v1.6.4 (HA crash hotfix); both entries carry `"issues": [100]`. If a release is already tagged when the gap is noticed, add the number to the matching entry in `changelog.en.json` (+ `nl.json`, `de.json` lockstep) and regenerate.

   **When tagging a release**: promote the `unreleased` entries into a new release entry at the top of the `releases` array, set the `version` and `date` fields, and clear (or remove) the `unreleased` section. Then run the generator to regenerate both markdown files before committing.

   Rules for `highlights` in `changelog.en.json`:
   - **An array of plain-English strings** (one sentence per element) — the Blazor UI renders each element as a bullet
   - **Plain user-facing English only** — no CVE IDs, no API paths, no class names, no config key names, no technical implementation details
   - **For purely internal releases** use exactly: `["Internal improvements — no user-facing changes."]`
   - **Bad:** `["SQL queries centralised as mitigation for CVE-2025-6965"]` / `["New GET /api/v1/admin/... endpoint"]`
   - **Good:** `["Internal improvements — no user-facing changes."]` / `["Quotes can now be loaded from multiple data sources."]` / `["Security: a database query vulnerability (CVE-2025-6965) was identified and mitigated; no user data was affected."]`
   - **Security fixes** should always appear in highlights — include the CVE ID so users can verify, but keep the surrounding language non-technical
   - `ChangelogSchemaTests` validates structure (no null entries, CVE format) — run `dotnet test --filter ChangelogSchema` to verify before committing

   After editing `changelog.en.json`, regenerate the markdown files (run from repo root):
   ```bash
   dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
   dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
   ```
   Commit the regenerated files alongside the JSON change.
4. **Versions in sync** — when tagging a release, all three must match the tag (without the `v` prefix):
   - `Directory.Build.props` → `<Version>` (shared across all projects — **this is the only file to update**)
   - `addon/config.yaml` → `version`
   - `changelog.en.json` → new version entry at the top; regenerate `CHANGELOG.md` and `addon/CHANGELOG.md`

   `AssemblyVersion` and `FileVersion` are derived automatically as `$(Version).0` (e.g. `1.4.1` → `1.4.1.0`). Do not set them manually.
5. **Docker build succeeds** — run a local build to catch publish/container issues before they hit CI:
   ```bash
   docker build -f docker/Dockerfile -t quotinator:local .
   ```
   If you do not have Docker available, note this explicitly and let the reviewer know CI is the first Docker gate.
6. **Smoke-test the image** (optional but recommended for Dockerfile changes):
   ```bash
   docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
   curl -s http://localhost:8080/api/v1/health
   curl -s http://localhost:8080/api/v1/version
   curl -s http://localhost:8080/api/v1/quotes/random
   curl -s "http://localhost:8080/api/v1/quotes/search?q=love"
   curl -s "http://localhost:8080/api/v1/quotes/search?q=Casablanca&field=source"
   curl -s "http://localhost:8080/api/v1/quotes/search?q=Churchill&field=author"
   curl -s "http://localhost:8080/api/v1/quotes/search?q=Rick&field=character"
   curl -s "http://localhost:8080/api/v1/quotes/search?q=love&type=person"
   ```
   Check that `/version` returns the expected version number — a missing `Directory.Build.props` in the build context silently produces `1.0.0` while `/health` still returns healthy.
   The search queries cover: default full-text (`love` should return results), `field=source` (`Casablanca` should return results), and `field=author`, `field=character`, `type=person` — these three may return an empty `items` array with a `message` when the bundled dataset has no matching data; that is expected behaviour, not a bug.

   **Import and manual conflict-review workflow** (#45, #149, #152) — re-imports a bundled file with `review` policy forced, so the endpoint that would otherwise auto-resolve via the default policy instead produces a genuine pending conflict to exercise decide/undo/apply against:
   ```bash
   curl -s "http://localhost:8080/api/v1/import/conflicts"
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     "http://localhost:8080/api/v1/import"
   curl -s "http://localhost:8080/api/v1/import/conflicts?status=pending"
   ```
   From the last response, copy one conflict's `id` and its `batchId` (already uppercase), then:
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
     -d '{"quoteText":{"choice":"keep"}}' \
     "http://localhost:8080/api/v1/import/conflicts/<id>/decide"
   curl -s "http://localhost:8080/api/v1/import/conflicts?status=decided"
   curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/conflicts/<id>/undo"
   curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
     -d '{"quoteText":{"choice":"keep"}}' \
     "http://localhost:8080/api/v1/import/conflicts/<id>/decide"
   curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/conflicts/apply?batchId=<batchId>"
   ```
   The first `GET /import/conflicts` (before any import) should return `200` with an empty or existing `items` list — proves the endpoint is reachable with no setup. After the import, `status=pending` must show exactly the conflict just created; after `decide`, `status=decided` must show it (and its `ambiguousFields` must be empty); after `undo`, it must be back under `status=pending`; after `apply`, the batch must return `200` and the quote's field should reflect the decision. A `422` from `apply` listing `pendingConflictIds` before every conflict in the batch is decided is expected, not a bug.

> The CI pipeline runs `dotnet publish` and asserts `data/sources/` is present and non-empty in the output, but it does **not** build the Docker image. The release workflow builds the image on tag push — by that point a failure blocks the release. Always do step 5 locally before tagging.

## Tagging a release — separate push cycle

**Always tag in a separate commit/push cycle from feature work.** The reason: Dependabot may open PRs shortly after a push (NuGet and GitHub Actions updates run weekly). Merging those before tagging means the release includes up-to-date dependencies rather than shipping a version that is immediately out of date.

Workflow:
1. **At the start of a session** — check for open Dependabot PRs (`gh pr list --state open`) and merge any that are green before starting feature work. This avoids Dependabot reacting to your push mid-session.
2. Push all feature/fix commits to `main`
3. Wait for any remaining Dependabot PRs to finish CI
4. Review and merge passing Dependabot PRs
5. `git pull` to bring dependency bumps onto your local branch
6. Add the dependency bump entry to `src/Quotinator.Api/resources/changelog.en.json`; regenerate both markdown files with `scripts/changelog.csx`
7. Bump versions (`Directory.Build.props` → `<Version>`, `addon/config.yaml`, `changelog.en.json` version entry) and commit
8. Run the full pre-push checklist above (including Docker build)
9. Push the version bump commit, then push the tag:
   ```bash
   git tag v1.0.x
   git push origin v1.0.x
   ```

> **Tag push environment note.** Claude Code Desktop can push tags directly. Claude Code cloud and mobile environments receive a `403` on tag pushes — if running in those environments, the tag must be pushed from a local terminal instead.

---

## Issue and improvement tracking

Bugs, defects, and planned improvements are tracked as **GitHub Issues**. Do not maintain lists here. Only add a temporary note in this file if something is discovered mid-session and has not yet been filed as a GitHub Issue.

**Closing protocol:** Issues are always closed explicitly via `gh issue close <N> --comment "..."` after the full closing checklist is complete. Never use `Fixes #N`, `Closes #N`, or any GitHub auto-close keyword in a **commit message or PR body** — these trigger auto-close on merge and bypass the verification comment requirement. The `commit-msg` hook guards commit messages; PR bodies must be checked manually. Deployment-verified issues are tracked in `project_post_deploy_verification.md` in memory until confirmed in the live HA add-on.

**Milestone workflow:** The full process for planning, executing, and closing milestones is in `docs/workflow/process.md`. The session-start and issue-close checklists are in `docs/workflow/checklist.md`. Always read these before starting a milestone session or closing an issue.

**Verification checklist format:** Every plan doc must include a verification table using exactly this format (from `docs/workflow/process.md`) — `Status` is always its own column, never embedded in `Verification`:

```
| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ / ❌ | Description | Unit test / Live | TestClass.MethodName or exact command + expected output |
```

The closing comment posted on the GitHub issue must reproduce this same table (not a custom format). See issue #61 for a canonical example.

**Deployment-only issues** — anything involving HA ingress routing, supervisor log output, add-on config panel, or container restart behaviour must be classified as deployment-verified and added to the memory checklist before the release.

