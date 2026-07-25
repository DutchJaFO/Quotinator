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

# Install git hooks (run once per clone — prevents accidental GitHub issue auto-close via commit
# message, enforces the draft-then-review commit rule below, and auto-deletes the reviewed draft
# after a successful commit)
cp scripts/hooks/commit-msg .git/hooks/commit-msg
cp scripts/hooks/post-commit .git/hooks/post-commit
chmod +x .git/hooks/commit-msg .git/hooks/post-commit
```

The Scalar API reference is at `/scalar/v1` and the OpenAPI spec at `/openapi/v1.json` — available in all environments including production.

**An AI assistant must never run `dotnet run`/`dotnet watch` directly for its own verification.** `dotnet run --project src/Quotinator.Api` above is listed for a human developer — running it as the assistant risks a port/process conflict with a developer's own Visual Studio instance (this has already caused a real IIS Express outage requiring a reboot). For the assistant's own live/smoke verification, use Docker (`docker build` + `docker run` — see `docs/release-verification.md`'s T2 tier). T1 (Visual Studio) is exclusively the developer's own action to perform and confirm — never something the assistant replicates locally.

**Draft, review, then act — for every `git commit` and every GitHub issue create/edit, no exceptions.** Write the full intended text (commit message, or issue title + body) to a file, **and paste that same full text directly into the chat response** — not a summary, not a diff-only excerpt, and not a `Read` tool call whose output happens to render the file, which is not the same thing as the assistant's own words containing the text. Only run the actual command after explicit approval. See `docs/workflow/process.md`'s "Commit message format and content" for the exact mechanics (`.claude/temp/commit-draft.md`, `git commit -F`). The `commit-msg` hook installed above enforces the commit side of this mechanically; `gh issue create`/`edit` have no equivalent hook, so that side relies on this rule being followed, not on tooling.

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
- **This repository is C#-only** ([ADR 010](docs/architecture-decisions/010-repository-is-csharp-only.md)). Any script worth keeping is a `dotnet-script` `.csx` file under `scripts/` (see `scripts/changelog.csx`) or a proper C# project under `tools/` — never Python, Perl, Node.js, or a Unix text-processing one-liner (`sed`, `awk`, etc.), including ad hoc during a development session. Direct invocation of already-installed CLI tools (`git`, `dotnet`, `docker`, `gh`) via the shell is unaffected — the rule governs what gets *written*, not which shell runs an existing command.

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

**Migration ownership split (Data vs. consumer):** `Quotinator.Data` owns migrations for its own tables (currently `System_AuditEntries`; any future `System_`-prefixed table Quotinator.Data itself defines) via a fixed internal list (`DatabaseInitializer.DataOwnedMigrations`) — never passed through the constructor, and never controlled by the consuming project. These always apply first, before any consumer-supplied migration, and are tracked in their own `System_SchemaVersion` table. A consuming project's own domain migrations (e.g. `Quotinator.Core`'s `QuotinatorMigrations.All`) are tracked independently in `System_ConsumerSchemaVersion`, so "version N" always means the same specific migration for whichever side owns it, unaffected by the other side's migration count changing over time. `IDatabaseInitializer.SchemaVersion` reports the consumer's own version (what operators track release-over-release); `DataSchemaVersion` reports Quotinator.Data's own version separately.

**Baseline schema for fresh databases:** A completely empty database (zero tables of any kind, detected via `Sql.Schema.AnyTableExists`) skips replaying migration history entirely and instead applies a one-step consolidated baseline: `DatabaseInitializer`'s own `DataBaselineSql` (Quotinator.Data's tables) followed by the consumer's `SchemaBaseline.Sql` (e.g. `QuotinatorMigrations.Baseline`, Quotinator.Core's domain tables). A database with *any* pre-existing table — even just an empty version table — always takes the full incremental path instead; the two paths never cross. **Whenever a new migration is added to either `DataOwnedMigrations` or a consumer's migration list, the corresponding baseline must be updated to match its final result in the same commit** — this is enforced by dedicated schema-drift tests (`DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema` in `Quotinator.Data.Tests`, `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`/`...AcceptSameCheckConstraintValues` in `Quotinator.Core.Tests`) that compare the baseline-created schema against the incrementally-replayed schema and fail on any drift, including in CHECK constraint behaviour (which `PRAGMA table_info` doesn't capture structurally).

**No exception-based migration recovery.** A migration must never rely on catching its own failure to detect an already-applied state — a genuinely different failure with the same error message would be silently misclassified and swallowed, leaving no way to know whether the correct migrations actually applied. Two rules follow from this:

- **Fix the root cause instead of adding a check.** `Reset` (`DropAndRebuildAsync`) never wipes or replays `Quotinator.Data`'s own migration history (`System_SchemaVersion`), regardless of `preserveSchemaVersion` — because Data's migrations only ever concern `System_`-prefixed tables, which a Reset never drops in the first place (see `Sql.Schema.GetUserTables`). Only the consumer's own domain tables and `System_ConsumerSchemaVersion` are actually dropped and replayed. This is what makes the previously-unavoidable rename collision on every Reset simply never happen, with no check of any kind. Structural metadata checks (`sqlite_master`, `pragma_table_info`) are reserved for the single existing whole-database-empty check (`Sql.Schema.AnyTableExists`) — do not add a new one anywhere else as a substitute for catching an exception.
- **A database whose recorded schema version doesn't match its actual on-disk schema is a hard failure, not a self-heal.** If a migration throws for any reason, it is never inspected or interpreted — `ApplyMigrationsAsync` and `DropAndRebuildAsync` back up the database before any destructive step, and on any exception restore that backup and rethrow, leaving the database exactly as it was before the attempt. The operator must run an explicit Reset to resolve a genuine mismatch. `ApplyMigrationPhaseAsync` itself has no `try`/`catch` at all — a failing migration's own transaction rolls back automatically via `using`, and the exception propagates untouched.

### Project structure
```
src/
  Quotinator.Constants/        # Route strings, tag names, error message keys — no dependencies
  Quotinator.Core/             # Domain models, interfaces, and the SQLite-backed service implementation — bridges domain contracts with Quotinator.Data's generic infrastructure
  Quotinator.Data/             # Generic, reusable SQLite/Dapper infrastructure — domain-agnostic
  Quotinator.Data.Testing/     # Test helper library — stubs, fakes, disposable SQLite DB (reference from test projects only)
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
  Quotinator.Core.Tests/            # Unit tests for domain logic, and integration tests for the SQLite-backed implementation (SqliteQuoteService, migrations)
  Quotinator.Data.Example/          # Concrete example implementations of Data patterns (not a test runner)
  Quotinator.Data.Testing.Tests/    # Tests for the Data.Testing helper library
  Quotinator.Data.Tests/            # Integration tests for Data infrastructure (real SQLite, no fakes)
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

Dependency direction: `Quotinator.Api` → `Quotinator.Core`; `Quotinator.Core` → `Quotinator.Data`; `Quotinator.Api` → `Quotinator.Constants`. `Quotinator.Data` has no dependency on Core (must stay domain-agnostic — see ADR 004). `Quotinator.Data.Testing` → `Quotinator.Data` only. (Until #206, `Quotinator.Engine` sat between Api and Core as a separate project; it was merged into `Quotinator.Core` because Core's own "stay Dapper/SQLite-free" invariant — the only reason Engine existed as a *third* project rather than Core depending on Data directly — turned out not to be worth its cost. See ADR 004's `#206` revision for the full reasoning.)

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

Endpoint tests use `WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) and replace `IQuoteService` with `FakeQuoteService` via `WithWebHostBuilder`. **Also register `IDatabaseInitializer` with `NoOpDatabaseInitializer`** in the same `WithWebHostBuilder` call — without it, the test hits a real database at startup even though `FakeQuoteService` makes the endpoint logic itself DB-free. A test that intends real database contact registers a real (or in-memory-backed) initializer explicitly instead; the default for endpoint tests is no DB contact at all. See `tests/Quotinator.Api.Tests/Endpoints/QuoteEndpointsTests.cs` for the canonical pattern. The `public partial class Program { }` line at the bottom of `Program.cs` is required to expose the entry point to the test project.

### Route registration order

`/search` is registered before `/{id}` in `QuoteEndpoints.cs` so the literal segment takes priority over the catch-all parameter. Preserve this order.

### Masterdata routing convention

`/api/v1/masterdata/` is the route prefix for the five masterdata entities — Sources, Characters, People,
Series, and Universes (`GET /api/v1/masterdata/sources`, `GET /api/v1/masterdata/sources/{id}`, and so on
for each entity) — tagged `ApiTags.MasterData` in the OpenAPI/Scalar UI. This coexists with the flat
top-level plural pattern `/quotes` and `/import/actions` already use, deliberately: masterdata entities are
the shared reference data that quotes and conversations are built from, and grouping them under one prefix
makes that relationship legible in the API surface, rather than scattering five unrelated-looking
top-level routes.

**`/api/v1/conversations` deliberately keeps its own route and `ApiTags.Conversations` tag — it does not
move under `/masterdata/`.** Conversations is a *consumer* of masterdata (it embeds quotes, which
reference Sources/Characters), not a masterdata entity itself. Stated explicitly here so the next reader
doesn't reasonably assume the omission was an oversight.

### Masterdata reference shape

Any FK-valued field on a masterdata response DTO (e.g. a Source's link to its Series, a Character's links
to its Sources) is a minimal, read-only `MasterDataReference(string Id, string Name)` —
`src/Quotinator.Core/Models/MasterDataReference.cs` — **never** a bare id and never the full related record.
A single optional FK (`Source.SeriesId`) becomes a nullable `MasterDataReference?`; a many-to-many link
(Character↔Source) becomes a `IReadOnlyList<MasterDataReference>`. `MasterDataReference` originally lived
in `Quotinator.Api.Models` (introduced by #184); #206's merge of `Quotinator.Engine` into `Quotinator.Core`
relocated it to `Quotinator.Core.Models` alongside every masterdata response DTO and `QuoteResponse` —
one canonical location, not split across two projects.

**Why not a bare id:** a bare id forces the client into a second round-trip per reference just to show a
name. **Why not the full related record:** that would denormalise and bloat every response with data the
caller can already fetch via that entity's own masterdata endpoint if it needs more than a display label —
and `Quotinator.Core.Models.QuoteResponse` already establishes the precedent of embedding just enough to
render, not a nested full object. `MasterDataReference` is the middle ground, sized for "enough to display
without an extra call."

**Deliberately minimal, not permanently minimal.** `MasterDataReference` carries only `Id`/`Name` today
because nothing has needed more yet — richer detail (or the full related record) can be added to specific
response fields later, per concrete need, without redesigning the shape from scratch. Its properties are
read-only (`init`-only): these are display references embedded in another entity's response, not something
a client edits through this endpoint. Any future CRUD work targets the core response record itself (e.g.
`PUT /masterdata/sources/{id}`), never a nested reference field directly.

**A resolver, not the generic repository — resolving a FK to a reference always requires a join** the
generic `IListableRepository<T>`/`IRepository<T>` (single-table only, no join support) cannot express.
Each masterdata issue that needs this writes its own small reader in `Quotinator.Core.Repositories`
(e.g. `ISourceSeriesReferenceReader`, `ICharacterSourceLinkReader`) returning plain `(Guid Id, string
Name)` tuples, not `MasterDataReference` directly — this keeps the reader a data-shape concern,
independent of which response DTO an individual endpoint chooses to build from the tuple, rather than a
project-boundary workaround (both types are reachable from the same project since #206's merge; the
separation is a design choice, not a constraint). The consuming endpoint maps the resolver's result into
`MasterDataReference` at the API layer. A batched form (`GetXForManyAsync`, one query per page rather
than one per row) is required wherever the reference appears in a list response, matching #195's N+1
avoidance rule for pagination generally.

### Soft-deleted rows are invisible by default, everywhere

`IRepository<T>.GetByIdAsync`/`IListableRepository<T>.GetPageAsync` already exclude `IsDeleted = 1` rows
unconditionally — confirmed by reading `RepositorySql.SelectById`/`SelectPage`/`CountActive`
(`src/Quotinator.Data/Repositories/RepositorySql.cs`), none of which have a parameter or overload for
including deleted rows. No endpoint anywhere in this codebase exposes soft-deleted rows today, opt-in or
otherwise — `IRestorableRepository<T>.GetDeletedAsync`/`RestoreAsync` exist and are DI-registered, but are
never called from `Quotinator.Api`.

**The rule this establishes:** a soft-deleted row is never visible through a read endpoint by default, and
if a concrete need for admin-style "show me deleted rows too" visibility ever arises, it must be built as
an explicit opt-in query parameter (e.g. `includeDeleted=true`, defaulting to `false`) — never a default,
and never inferred from a caller's role or key. **Do not build this parameter speculatively** — add it to
a specific endpoint only when a real consumer needs it; until then, the existing unconditional exclusion
is the entire implementation, for free.

**This applies one level deeper than the primary record, too.** Any new reference-resolving join
(`MasterDataReference` above) must filter the *referenced* table to `IsDeleted = 0` in the `JOIN`/`ON`
clause, not just the driving table — the same idiom `Sql.Quotes.SelectBase`'s multi-table join and
`Sql.Characters.SelectIdBySourceAndName` already use (`JOIN Sources s ON s.Id = ... AND s.IsDeleted = 0`).
A soft-deleted target simply produces no matching row, so the reference resolves to `null` (or is absent
from a list) automatically — no separate "is this reference deleted" check is ever needed at the call site.

### Numeric query parameter binding pattern

`yearFrom`, `yearTo`, `year`, `decade`, `page`, `pageSize`, `n`, and `limit` are declared as `string?` in handler signatures rather than `int?`. This is deliberate: when declared as `int?`, ASP.NET Core's parameter binder throws `BadHttpRequestException` on invalid input (e.g. `yearFrom=1980x`) and the exception propagates unhandled through the entire middleware stack before being caught accidentally by `UseExceptionHandler`. Declaring them as `string?` lets `TryParseYear()` (or the equivalent inline `int.TryParse`) in `QuoteEndpoints.cs` catch the parse failure at the point of origin and return a 422 immediately.

The downside is that the OpenAPI generator infers `type: string` from the C# type, which is wrong, and drops any `[DefaultValue]` attribute along with it. `NumericParameterSchemaTransformer` (`src/Quotinator.Api/OpenApi/NumericParameterSchemaTransformer.cs`) patches both back — the schema to `type: integer` and, for parameters registered with one, the published `default` — via a registry keyed by **path and parameter name together**. Registering only the path patches nothing: this is the exact gap #194 found, where `api/v1/quotes` was registered for the year params but `page`/`pageSize` were never added alongside them.

**Rules for adding new numeric query parameter:**
- Declare as `string?` and parse with `int.TryParse` (or a dedicated helper) — never `int?`
- Return 422 on parse failure via `Results.Problem`
- If the parameter has a real default, add it to `Quotinator.Constants.Api.QueryParamDefaults` and use that constant in the `[DefaultValue(...)]` attribute and the handler's own fallback — one value, not three independently-drifting copies
- Add **both** the endpoint path and the parameter name (with its default, or `null` if it has none) to `NumericParameterSchemaTransformer.NumericParamsByPath`

### Standard pagination contract

Every paginated GET list endpoint (`/quotes`, `/admin/audit`, `/import/actions`) shares one contract
(#183/#195), implemented once and reused rather than reimplemented per endpoint:

- `page`/`pageSize` are `string?`-bound (see "Numeric query parameter binding pattern" above) and
  parsed via `PaginationParsing.TryParse` (`src/Quotinator.Api/Endpoints/Shared/PaginationParsing.cs`).
- `pageSize = 0` means "every matching row as a single page" — bypasses the max, and the response's
  `pageSize` reports the actual returned count, not the literal `0` requested (the "effective size"
  contract, built into `PagedItems<T>` wherever it's constructed).
- Maximum `pageSize` is `QueryParamDefaults.PageSizeMax` (500); default is `QueryParamDefaults.PageSize`
  (20). Both live in `Quotinator.Constants.Api.QueryParamDefaults` — never a second hardcoded copy.
- A page number past the last page is a distinct 422 (`PaginationParsing.ValidatePageBeyondLast`),
  checked *after* the query runs against the real `TotalPages`, never silently clamped or emptied.
- The response shape is `Quotinator.Data.Models.PagedItems<T>` (or, for `/quotes`, the pre-existing
  `Quotinator.Core.Models.PagedResult<T>`, which has an identical field shape — see #195's plan doc
  for why the two types coexist instead of unifying).
- `NotFoundResult.OkOrNotFound` (`src/Quotinator.Api/Endpoints/Shared/NotFoundResult.cs`) is the shared
  404 helper for the matching `GET /{id}` endpoint, when one exists.

**Whenever a new paginated GET endpoint is added, it must ship with the full test matrix below.**
Coverage of these eight cases was missing piecemeal across `/quotes`, `/admin/audit`, and
`/import/actions` themselves and only closed after the fact — do not let a new endpoint repeat that
gap.

| # | Case | Expected |
|---|---|---|
| 1 | `page=0` | 422 |
| 2 | `page` malformed (e.g. `page=abc`) | 422 |
| 3 | `pageSize` malformed | 422 |
| 4 | `pageSize` negative | 422 |
| 5 | `pageSize` above 500 | 422, never silently clamped |
| 6 | `pageSize = 0` | 200, `items` contains every row, `pageSize` in the response equals `totalCount` |
| 7 | `pageSize` omitted | defaults to 20 — assert the actual response field, not just a 200 |
| 8 | `page` beyond the last page (given a known `TotalPages`) | 422, distinct from case 1 |

See `tests/Quotinator.Api.Tests/Endpoints/QuoteEndpointsTests.cs`, `AdminAuditEndpointTests.cs`, and
`ImportActionEndpointsTests.cs` for the canonical implementations of all eight cases.

**Case 6 needs a second test at the repository/service level, not just the endpoint level.** The
endpoint-level test typically runs against a stub/fake reader that echoes its input back, which cannot
catch a reader translating `pageSize = 0` into a literal SQL `LIMIT 0` instead of `LIMIT -1` — exactly
the live bug #195's own T2 pass found in `SystemAuditReader`/`SystemImportActionReader` after their
*type* was retrofitted to `PagedItems<T>` but their SQL wasn't. Add a real-SQLite test asserting
`pageSize = 0` returns every row, not zero — see `SystemAuditReaderTests.cs`,
`SqliteQuoteServiceTests.cs`, and the `GetPagedAsync` region of `SystemImportActionWriterReaderTests.cs`
for the pattern.

**Registering a new path in `NumericParameterSchemaTransformer` also needs a live-pipeline test, not
only the transformer's own unit tests.** `NumericParameterSchemaTransformerTests.cs` exercises the
transformer class directly against a synthetic `OpenApiOperation` — it would keep passing even if the
transformer were never actually registered via `AddOpenApi` in `Program.cs`.
`OpenApiSpecEndpointTests.cs` closes that gap: a `WebApplicationFactory`-based test that fetches the
real `/openapi/v1.json` through the full pipeline and asserts the published type, replacing what would
otherwise be a manual `curl | grep` check of the live spec.

### GUID/enum/id/Name/Title comparisons are case-insensitive by default

Any GUID, enum, other identifier, or **Name/Title-valued natural-key** comparison is **case-insensitive by default** — never case-sensitive, and never behind a config toggle. This applies wherever two independently-cased copies of the same value can meet, not only at the REST route/query-parameter boundary: a curator-authored JSON file's own explicit id (e.g. a `sources[]`/`people[]` entry referencing an already-existing, `EntityIdentity`-derived row) is under no obligation to match the stored casing, and neither is a `series=`/`universe=` filter value or an import file's own Series/Universe/Person `name` field. The pattern is `LOWER(column) = LOWER(@param)` in the `Sql.cs` query, built via `Quotinator.Data.Queries.IdClauses` for id columns (see `Sql.Conversations.SelectForRead`, `Sql.SystemImportActions.SelectAllForBatch`'s `BuildWhere`, `Sql.Sources`/`Sql.People`'s `SelectExistingById`/`UpdateFieldsById`/`UpdateCompletenessById`/`CountActiveReferences`) or hand-written for Name/Title columns, matching `Sql.Sources.SelectIdByTitleAndType`'s own precedent (see `Sql.Series`/`Sql.Universe`/`Sql.People`'s own `SelectIdByName`, #216). This `LOWER()` wrapping is a pure comparison-mechanics concern, independent of and unrelated to which casing is canonical for storage/presentation — see ADR 012's "system-wide lowercase convention" revision. The canonical stored/presented form for every entity id is lowercase (`Guid.ToString("D")`'s own default), rendered via `Quotinator.Data.Helpers.GuidExtensions.ToCanonicalId()` — the single real choke point; never a bare `.ToString("D")`/`.ToUpperInvariant()` typed out inline, and never a raw `Guid`-typed value bound directly into an `IN`-list (Dapper's list-parameter expansion does not reliably invoke a registered type handler per element — pre-canonicalize to strings first). Name/Title columns have no equivalent canonical-casing concern (unlike ids, their stored casing is meaningful display text, e.g. "The Lord of the Rings") — only the comparison side needs wrapping, never the presentation side.

Found and fixed piecemeal across `status`/`entityType`/`batchId` (#154), a conversation `{id}` route (#69), Sources'/People's own id-first lookup used by an explicit `sources[]`/`people[]` entry (#180), and Series/Universe/People's own `SelectIdByName` natural-key lookups plus three further recurrences of the same class found in a systematic full-codebase audit — `?lang=` (reachable on nearly every read endpoint, and additionally normalized at the API boundary via `InputValidation.TryNormalizeLang`, the single choke point every `?lang=`-accepting endpoint calls before the value ever reaches a SQL comparison or an echoed `EffectiveLanguage`), `admin/audit`'s `?table=` (previously a silent no-op on `DELETE`, not just an empty `GET`), and `SystemChangeLog.EntityType` (#216) — before being recognised as a general rule that applies to every id- or natural-key-matching comparison in the codebase, not just route/query parameters. When adding any new GUID/enum/id/Name/Title-valued parameter or SQL comparison of this kind, apply case-insensitive matching from the start rather than waiting for it to be reported as a bug on that specific one — and when fixing an instance of this bug, grep the same file/module for sibling comparisons of the same kind and fix them together, since this class of bug has repeatedly turned out to affect more than the one reported case.

**Explicit, deliberate exception: `LIKE`-based free-text search (`/quotes/search`'s `q`, and the `character`/`author`/`source` fuzzy filters).** SQLite's `LIKE` only case-folds ASCII by default — accented Latin, Cyrillic, CJK, etc. remain case-sensitive unless the ICU extension is loaded (verified against [sqlite.org/lang_expr.html](https://www.sqlite.org/lang_expr.html) during #216). This is narrower than the blanket rule above and was deliberately left unfixed by #216 — accepted as a known limitation since no bundled translation currently exercises non-ASCII partial-match search — with the actual fix (native ICU extension vs. a managed `SqliteConnection.CreateCollation`/`CreateFunction` alternative) tracked separately as [#222](https://github.com/DutchJaFO/Quotinator/issues/222) in the v1.8.0 maintenance milestone. Do not silently "fix" this by wrapping a `LIKE` clause in `LOWER()` on both sides — that only helps ASCII and masks the real Unicode gap #222 exists to resolve properly.

**Comparison case-insensitivity is not the same guarantee as canonical presentation — a third mechanism, applied uniformly to every selected id column, is required.** A `SELECT` that isn't filtering or joining on a column runs neither write-side canonicalization nor `IdClauses`' comparison-side `LOWER()` wrapping. Every `*Id`-suffixed column in a SELECT list — primary key or foreign key — must go through `Quotinator.Data.Queries.IdClauses.SelectColumn(column, alias)`, which emits `LOWER(column) AS alias`. This applies unconditionally, not only to columns known to be `string`-typed on their C# side: a `Guid`-typed property happens to render lowercase for free today via `System.Text.Json`'s default formatting, but that's an accident of the serializer, not a guarantee — a column's downstream C# type can change without the query being touched (`Quotinator.Core.Models.MasterDataReference.Id` is `string`-typed for exactly this reason, despite backing what was originally a `Guid`-typed column). Wrap every selected id column the same way `IdClauses.Join` already wraps every JOIN condition unconditionally, regardless of whether it looks safe today.

**The one exemption**: `SystemChangeLog.InitiatedById` is `Id`-suffixed but not always an id — it holds an import batch UUID, an HTTP route, or an enrichment provider name — so forcing it lowercase would corrupt legitimate mixed-case content in the non-id cases. It is excluded by name in `SqlSelectPresentationGuard.ExemptColumnNames`, the only entry. A reader with no HTTP endpoint yet is still in scope for this rule — a DI-registered reader with a real `SELECT` query needs correct presentation for any consumer, not only a live one.

**Mechanical guard**: `Quotinator.Data.Diagnostics.SqlSelectPresentationGuard` mirrors `SqlIdCaseGuard`'s own strip-then-scan technique (not a maintained registry of "columns known to need it") — strip every already-`LOWER(...)`-wrapped column from a query's SELECT list, then flag any remaining `*Id`-suffixed reference. Wired into the same `SqlQueryGuardTests`/`RepositorySqlGuardTests` `DynamicData` enumeration `SqlIdCaseGuard` uses, so every SQL constant, factory method, and dynamically-assembled query is scanned on every test run — including `RepositorySql.cs`'s generic queries, which build an explicit column list via an `IEntityColumnMetadata` parameter rather than `SELECT *`, so they get the exact same wrap-every-id-column coverage as every hand-written query in `Sql.cs`. See ADR 012 for how `IEntityColumnMetadata`/`ReflectedColumnMetadata` work.

**The same convention extends beyond SQL to in-memory field-value comparison during import.**
`FieldMergeResolver.ValuesEqual` (`src/Quotinator.Data/Import/FieldMergeResolver.cs`) — the shared
comparison every entity's conflict/merge detection goes through (Quote, Source, Person, Character,
Series, Universe, StageDirection, SoundCue, Conversation) — compares string values (scalar or within a
list) case-insensitively, applied uniformly to every field including free-text content, not just
identity-like ones. Found while implementing #181: a plain `Equals(a, b)` meant an import file's own
casing variance (e.g. `"star wars"` vs `"Star Wars"`) was treated as a genuine field conflict, even
though `QuoteIdentity.StableId` already normalises casing away when generating the same quote's id —
an inconsistency between two adjacent mechanisms governing the same imported value. Deliberately applies
uniformly rather than only to source/character/author-style fields: a future import correcting only a
quote's own casing (e.g. an all-caps entry) is expected to be rare enough that requiring an accompanying
non-casing change (or an explicit `markCompletenessAs`) to register the correction is an acceptable
trade-off against the alternative of a growing per-field exemption list.

### Entity-scoped filter-parameter convention

Any endpoint that filters by a related masterdata entity (e.g. "quotes from this Source", "characters in
this Universe") exposes **two mutually-exclusive parameters**: an id-valued form (`{entity}Id`, e.g.
`sourceId`) and a name-valued form (`{entity}`, e.g. `source`). Supplying both is invalid. This is #196's
convention, implemented once as the shared `EntityFilterParsing.ResolveAsync`
(`src/Quotinator.Api/Endpoints/Shared/EntityFilterParsing.cs`) rather than reinvented per endpoint.

**The name-valued form is resolved to the entity's id first — it is not a direct SQL contains-match.**
`ResolveAsync` takes a caller-supplied `resolveIdByName` delegate (the consuming endpoint's own repository
lookup) and looks the name up *before* any list/filter query runs. If nothing matches, the caller already
knows there will be zero related results and returns that informatively — `EntityFilterOutcome.NotFound`
with a populated `Message` — rather than running a query that would also come back empty. This is
deliberately not a 422: a name that doesn't exist is a legitimate "no results" case, matching the existing
`FilteredResultStatus.NoResults` precedent (`QuoteEndpoints.cs:207-216`, 200 + empty items + an informative
message), not bad input.

**Validation**: supplying both parameters, or an id-valued one that isn't a well-formed GUID, both return
422 with a `detail`, never the framework binder's bare 400 — consistent with #183's pagination contract.
Once resolved to an id (whether supplied directly or found by name), matching is a case-insensitive exact
match (`LOWER(column) = LOWER(@id)`), per the case-insensitive-by-default rule above.

**Explicit exemption: `/quotes/search` and `/quotes/random`.** Their existing `character`/`author`/`source`
filters stay fuzzy, direct contains-matches — this convention is for *new* entity-scoped filters
(#184–#189, #192), not a retrofit of Search/RandomQuote's existing behaviour.

`EntityFilterParsing`'s three messages use `string.Format` on a localised template with `{0}`/`{1}`
placeholders (the same pattern as `ApiMessages.ImportActionAmbiguousFieldsUnresolved`,
`ImportEndpoints.cs:174`) — `IApiLocalizer` itself has no interpolation support (`this[string key]` is a
flat lookup), so the caller formats the resolved template with the specific parameter/entity names rather
than the message being generic.

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

### Verifying title/date corrections (`*-conflict-rules.json`, `*-source-aliases.json`)

A `ConflictResolutionRule` or `SourceAliasRule` entry encodes a factual claim about a real film, show,
or book — a canonical title, a release date, which real-world work two differently-spelled Source rows
both refer to. **Verify each such claim before adding the rule or alias — see
[`docs/workflow/source-verification.md`](docs/workflow/source-verification.md) for the required
procedure and source priority order.** Do not rely on unstated model/training knowledge, even for
well-known mainstream titles, and do not search sources in an arbitrary/inconsistent order — the linked
procedure defines which sources to check first and when to widen the search.

**Why this matters even for "obvious" facts**: correctness is this project's top priority (see Project
Priorities above) — quotes must be real and accurately attributed, and that guarantee is only as good
as the data feeding it. An uncited "I recognize this movie" claim is not reproducible or auditable the
way this project's other correctness work is (red-green tests, cited CVEs).

**Two known, deliberately unresolved exceptions**, left as future work rather than "fixed" here:
- A film with more than one legitimate official title (e.g. Harry Potter's Philosopher's/Sorcerer's
  Stone, UK vs US) has no way to record the alternate as anything but "corrected away" — see #218.
- A bundled quote that cannot be verified against any real source at all (not a title/date
  inconsistency, a genuine "does this quote exist" question) has no exclusion mechanism — see #219.

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
| `src/Quotinator.Data/Queries/Sql.cs` | All SQL query strings — never write SQL inline outside this file |
| `src/Quotinator.Data/Database/DatabaseInitializer.cs` | SQLite schema + numbered migrations |
| `addon/config.yaml` | HA add-on manifest — version, options, schema, port config |
| `addon/CHANGELOG.md` | Generated HA add-on changelog — do not edit directly |
| `docker/Dockerfile` | Container build |
| `docs/docker.md` | Docker build notes, Blazor static web assets caveat, port configuration |
| `docs/database-conventions.md` | Database do's and don'ts — RecordBase, enum/CHECK constraints, migrations, SQL safety, Data/Engine boundaries, DB testing conventions |
| `docs/data-access.md` | Repository/join-query usage patterns (how to use the infrastructure `database-conventions.md` governs) |
| `docs/testing-policy.md` | Testing standards — test project pairing, CVE folder rule, parallel execution |
| `docs/workflow/process.md` | Milestone workflow — starting, executing, closing, living and maintenance milestones |
| `docs/workflow/source-verification.md` | Procedure, source priority order, and escalation rules for verifying a title/date/attribution claim before a data correction |
| `docs/workflow/checklist.md` | Issue filing, session-start, issue-closing, and milestone-close checklists |
| `docs/workflow/cve.md` | CVE handling workflow; template is at `docs/workflow/cve-template.md` |
| `docs/security/README.md` | Summary of all known CVEs and their current status across all projects |
| `docs/milestones/` | Per-milestone overview and per-issue plan docs |
| `.gitignore` | Must exclude `appsettings.local.json`, `.env`, and `data/*.db` |
| `.claude/temp/` | Gitignored — the place for temporary/test-output files (one-off inspection output, scratch files generated purely to check something). Never write temporary output into a tracked folder such as `scripts/changelog-reference/`. |
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

**`main` must always be green.** A failing build or test is acceptable on a feature branch mid-development — it is never acceptable on `main`. This checklist exists specifically to guarantee that; do not skip steps because a deadline is close or the failure "looks unrelated."

1. **Build clean** — `dotnet build --configuration Release` must report `0 Warning(s)  0 Error(s)`
2. **Tests pass** — `dotnet test --configuration Release --verbosity normal` must report all tests passed with `0 Warning(s)  0 Error(s)`. The same 0-warnings policy that applies to `dotnet build` applies here — any compiler warning surfaced during test build is a blocking failure.
3. **Changelog updated** — `src/Quotinator.Api/resources/changelog.en.json` is the source of truth for all changelog content. **Never edit `CHANGELOG.md` or `addon/CHANGELOG.md` directly — they are generated files.**

   **Before writing any entries, read `schemas/changelog.schema.json`** — it is the authoritative definition of every field and which fields are required. Do not infer the format from prior entries or git history.

   **At the `Waiting for release` phase — as soon as an issue's verification is complete, not deferred until it's actually tagged or closed:** add entries to the `unreleased` section at the top of `changelog.en.json`. Include the issue number in `unreleased.issues`. This follows the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) `[Unreleased]` convention: entries accumulate as work completes so that promoting them at release time is a rename, not a writing exercise. Decide at the time of writing whether the change deserves a `highlights` entry (user-facing impact) or only `added`/`changed`/`fixed`/`removed` (technical). The issue itself stays open and uncommented — `gh issue close` is a separate, later action gated on the release actually shipping (see `docs/workflow/issue-closure.md`'s two-gate rule) — adding the changelog entry does not imply the issue is closed. See `docs/workflow/checklist.md` → "Before closing an issue" → "Waiting for release" for the full step list.

   **`changelog.nl.json` and `changelog.de.json` update in lockstep with `en.json`, in the same commit — every time, not only when filling an `issues[]` gap.** Every entry added to `en.json`'s `unreleased` section gets a matching translated entry in both other files before that commit is made; `TranslationCompletenessTests`-style drift between locales is a blocking failure, the same as a missing English entry.

   **Before tagging a release, audit `unreleased.issues[]` against every issue actually touched during the session(s) since the last release** — not just the most recent one. It is easy to add entries as you go and still miss an issue from earlier in a long session; a missed issue number breaks the "release linking" traceability the changelog exists to provide.

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
6. **Smoke-test the image** — required whenever a T2 verification pass is performed (see
   `docs/release-verification.md`'s T2 gate), not just for Dockerfile changes. **This is a living
   checklist**: whenever a T2 pass surfaces a new bug or edge case, add its verification command
   here in the same commit that fixes it — the list only grows, never shrinks. This is the single
   authoritative smoke test suite; `docs/release-verification.md`'s T2 section points here rather
   than keeping its own copy, to avoid the two drifting apart.

   **Baseline** — health/version/random/search:
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
   The search queries cover: default full-text (`love` should return results), `field=source` (`Casablanca` should return results), and `field=author` (`Churchill` should now return the curated Winston Churchill quote — see the curated `person`-type entries added below). `field=character` (`Rick`) and `type=person&q=love` may still return an empty `items` array with a `message`, since no bundled data currently matches either; that is expected behaviour, not a bug.

   **Import and staged-action review workflow** (#45, #149, #152, #154) — re-imports a bundled file with `review` policy forced, so the endpoint that would otherwise auto-resolve via the default policy instead produces a genuine pending action to exercise decide/undo/apply against. `/api/v1/import/actions/*` (#154's unified staging engine) is the live mechanism — every import and seed run stages through it now.
   ```bash
   curl -s "http://localhost:8080/api/v1/import/actions"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/conflicts"
   ```
   The first call should return `200` with an empty or existing `items` list — proves the endpoint is reachable with no setup. The second call **must return `404`** — `/import/conflicts` was removed entirely in #154 Phase B; if this ever returns anything else again, the legacy manual-review machinery has regressed back in.
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     -w "\n%{http_code}\n" \
     "http://localhost:8080/api/v1/import"
   curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
   ```
   The import itself should return `202` (not `200`) since the re-imported quotes are genuine duplicates left `Pending` under `review`. The `status=pending` filter is deliberately lowercase here, not `Pending` — proves the case-insensitive `status`/`entityType`/`batchId` query-filter fix (#154) is still in effect. After the import, this must show exactly the action(s) just created (with `ambiguousFields` populated only when the fields genuinely differ — re-importing the same file unmodified means they usually won't). From the response, copy one pending action's `id` and its `batchId`.
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
     -d '{"quoteText":{"choice":"keep"}}' \
     "http://localhost:8080/api/v1/import/actions/<id>/decide"
   curl -s "http://localhost:8080/api/v1/import/actions?status=Decided"
   curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/<id>/undo"
   curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
     -d '{"quoteText":{"choice":"keep"}}' \
     "http://localhost:8080/api/v1/import/actions/<id>/decide"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<lowercase the batchId here too>"
   ```
   After `decide`, `status=Decided` must show it; after `undo`, it must be back under `status=Pending`; after `decide` again, ready to apply. **If the curated file's re-import produces more than one pending action** (it currently produces two — both `Airplane!` quotes), `apply` at this point correctly returns `422` with a `pendingActionIds` array listing the ones still undecided — this is the batch-apply-atomicity contract working as designed, not a bug. Decide each remaining id the same way, then re-run `apply` until it returns `200` and the quote's field reflects the decision. Applying with a deliberately lowercased `batchId` here also re-confirms the case-insensitive fix.

   **Two-phase decide→apply reversal** (#177) — a batch applied entirely through the staged
   review→decide→apply flow (i.e. via `POST /import/actions/apply` directly, not `POST /import`'s own
   single-shot path) previously never had its own `ImportBatches.Status` set to `Applied`, so
   `POST /import/actions/reverse` always rejected it with a bare `422` even though the batch had
   genuinely applied. Re-import the curated file under `review` again and decide every pending action
   from the sequence above (repeat the `decide` call for each remaining `id` until none are left
   pending), then:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
   ```
   `apply` must return `200`; both `reverse` calls (preview and real) must also return `200` — never the
   `422` this issue reported. If this ever regresses, `SqliteImportActionService.ApplyBatchAsync`'s own
   `MarkImportBatchAppliedAsync` call (gated on `TryApplyBatchAsync` returning `null`) is the one place
   that sets `Status`/`AppliedAt` for every caller — check it wasn't bypassed by a new caller of
   `ApplyBatchAsync` or `TryApplyBatchAsync` added elsewhere.

   **`batchId`-mode alias** (#154) — `POST /import` can apply an already-staged batch directly, without re-uploading a file:
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"skip"}}' \
     "http://localhost:8080/api/v1/import/preview"
   ```
   Copy the `batchId` from the response, then:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import?batchId=<batchId>"
   ```
   Must return `200` (the `skip` policy leaves nothing pending) and apply the previewed batch — proves `batchId` mode is a genuine alias for `POST /import/actions/apply`, not a dead code path.

   **Discard** (#154):
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     "http://localhost:8080/api/v1/import/preview"
   ```
   Copy the `batchId`, then:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/discard?batchId=<batchId>"
   curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
   ```
   Discard must return `204`; every action in that batch must now show `"status":"Discarded"` — nothing was ever applied, since creation is deferred to apply time.

   **Reverse (undo)** (#59):
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
     -w "\n%{http_code}\n" \
     "http://localhost:8080/api/v1/import"
   ```
   Note the returned `batchId` — this must be `200` (a clean apply, nothing pending) so there is a
   genuinely `Applied` batch to reverse.
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
   curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
   ```
   `preview=true` must return `200` without changing anything; the real call must also return `200`.
   The actions listing must still show every action `"status":"Applied"` afterwards — reversal never
   introduces a new action status; the batch's own record being gone is the only signal it was undone
   (confirm via `GET /api/v1/admin/audit` or `Quotinator.Tools.DbInspector` against `ImportBatches`
   showing `IsDeleted=1` for this batch, since there is no `GET /import-batches` listing endpoint).
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
   ```
   Reversing the same batch again must now return `404` (already reversed, treated as absent).
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
     -w "\n%{http_code}\n" \
     "http://localhost:8080/api/v1/import"
   ```
   Re-importing the same file after reversal must succeed (`200`/`202`, never a silent no-op) and the
   curated quotes must be reachable again via `GET /api/v1/quotes/search?q=Airplane&field=source` —
   this is the resurrection fix (#59) proven live, not just by `ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`.
   Finally, with at least one other batch applied after the one just reversed (true for a normal
   database with seed + this import history), attempt to reverse an older batch out of order:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<an older batchId>"
   ```
   Must return `422` — the strict LIFO stack rule (#59): only the most recently applied batch still
   live may be reversed, regardless of whether it shares any entities with the older one.

   **Bodyless request validation** (#154) — a `POST /import` with no body, no `Content-Type`, and no `batchId` must be rejected with a clear, actionable message rather than a bare framework `400`:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import"
   ```
   Must return `422` with a `detail` field ("you must provide either a file... or a batchId", paraphrased per locale) — **not** a bare `400` with no `detail` at all. This distinction matters: `WebApplicationFactory`'s in-memory TestServer handles a bodyless request differently than real Kestrel does, so the unit test suite alone cannot prove this — only this live check can. If this ever regresses to a bare `400`, `POST /import`'s handler is binding `IFormFile`/`[FromForm]` parameters automatically again instead of reading `HttpRequest` manually (see `ImportEndpoints.cs`'s `HandleImportFromRequestAsync`).
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import?batchId=00000000-0000-0000-0000-000000000000"
   ```
   Must return `404` (unknown batch) even with zero body/`Content-Type` — proves `batchId` mode never attempts to read the request body at all.

   **StageDirection/SoundCue Modify/decidability** (#171/#172) — both entities were Add-only before
   these issues; this proves a `Complete` row blocks a silent overwrite, and a correctable row can be
   Modified/decided/reversed end to end. Create a small fixture (one quote is required — `POST /import`
   rejects a file with none):
   ```bash
   cat > .claude/temp/smoke-171-172.json <<'EOF'
   {
     "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out.","imageUrl":null,"translations":{}}],
     "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-171-172.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
   ```
   Must return `200` with both rows added (check via `Quotinator.Tools.DbInspector` — `SELECT Id, Text, CompletenessStatus FROM StageDirections WHERE Id = 'f0000002-...'`). Re-import the same ids with a
   changed `text` under `{"duplicateResolution":{"default":"review"}}` — must stage a `Pending` `Modify`
   action for each (`GET /import/actions?status=pending`) with `ambiguousFields: ["text"]`. Decide each
   with `{"stageDirectionText":{"choice":"replace"},"markCompletenessAs":"Complete"}` /
   `{"soundCueText":{"choice":"replace"},"markCompletenessAs":"Complete"}`, then
   `POST /import/actions/apply?batchId=...` — confirm the corrected text and `CompletenessStatus: Complete`
   via DbInspector. Re-import the same ids again with another changed `text` under `review` policy — must
   now stage `Blocked`, not `Pending` (`GET /import/actions?status=Blocked`), and the on-disk text must be
   unchanged — proves a `Complete` row can no longer be silently overwritten. Finally, exercise
   correct/apply/reverse on a still-correctable row: single-shot re-import a changed `text` under
   `newest-wins` (nothing pending, applies immediately, `ImportBatches.Status` set to `Applied` by this
   direct-apply path — the two-phase decide→apply path used above does **not** set it, a known
   pre-existing gap, see #171/#172's plan docs), confirm the write via DbInspector, then
   `POST /import/actions/reverse?batchId=...` (`preview=true` first, then for real) and confirm the
   pre-correction text is restored via DbInspector.

   **Person: explicit id, Modify/decidability, dateOfBirth/dateOfDeath** (#173) — Person was Add-only
   before this issue and never had a write path for `dateOfBirth`/`dateOfDeath`; this proves both a
   `Complete` Person blocks a silent overwrite and a correctable Person can be Modified/decided end to
   end, plus exercises the lowercase-explicit-id reversal fix found live during this issue's own T2
   pass. Create a small fixture (one quote is required):
   ```bash
   cat > .claude/temp/smoke-173.json <<'EOF'
   {
     "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
     "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1950-01-01","dateOfDeath":null}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
   ```
   Must return `200` with the Person added (check via `Quotinator.Tools.DbInspector` — `SELECT Id,
   Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM People WHERE Id = 'f0000005-...'` — note
   the id is deliberately lowercase, as a file-authored explicit id always is). Re-import the same id
   with a changed `dateOfBirth` under `{"duplicateResolution":{"default":"review"}}` — must stage a
   `Pending` `Modify` action (`GET /import/actions?status=pending`) with `ambiguousFields:
   ["dateOfBirth"]`. Decide with `{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":
   "Complete"}`, then `POST /import/actions/apply?batchId=...` — confirm the corrected `DateOfBirth`
   and `CompletenessStatus: Complete` via DbInspector. Re-import the same id again with another changed
   `dateOfBirth` under `review` policy — must now stage `Blocked`, not `Pending`
   (`GET /import/actions?status=Blocked`), and the on-disk value must be unchanged — proves a
   `Complete` Person can no longer be silently overwritten. Finally, exercise the lowercase-id
   reversal path: single-shot re-import a changed `dateOfBirth` under `newest-wins` (nothing pending,
   applies immediately), confirm the write via DbInspector, then `POST /import/actions/reverse?
   batchId=...` (`preview=true` first, then for real) — confirm via DbInspector that `IsDeleted` on
   the `People` row genuinely flips to `1` (this is the case-sensitivity regression found live during
   #173's own T2 pass: a Guid-typed repository call silently force-uppercases before comparing,
   matching zero rows against a lowercase-stored id, so the row would otherwise stay visibly present
   with `IsDeleted = 0` despite the endpoint reporting success). Re-import the exact same fixture one
   more time — must stage as a fresh `Add` (not `Modify`, which would mean the reversal silently
   no-op'd and the row was never truly gone), and `IsDeleted` must be back to `0` afterward.

   **Series/Universe schema, Character↔Source many-to-many identity** (#179) — Character no longer
   has a `SourceId` column; a Character's Source links live in `CharacterSources` instead, and today's
   matching remains per-Source in meaning (only the mechanism changed — reusing a Character across
   Sources is #174's job, not this one's). This proves both halves live: a brand-new Character on an
   existing Source creates exactly one new `CharacterSources` link, and the same Character *name*
   under a *different* Source still creates a separate row (no premature cross-Source reuse).
   ```bash
   cat > .claude/temp/smoke-179.json <<'EOF'
   {"quotes": [{"id":"a0000001-0000-4000-8000-000000000001","quote":"A #179 smoke test line.","originalLanguage":"en","source":"Airplane!","date":"1980","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   ```
   Must return `200`. Confirm via `Quotinator.Tools.DbInspector` — `SELECT COUNT(*) FROM
   CharacterSources;` must have increased by exactly 1, and `SELECT c.Name, s.Title FROM Characters c
   JOIN CharacterSources cs ON cs.CharacterId = c.Id JOIN Sources s ON s.Id = cs.SourceId WHERE
   c.Name = 'Striker (Smoke Test)';` must show one row linking to `Airplane!`. Then re-import the same
   character name under a different Source:
   ```bash
   cat > .claude/temp/smoke-179b.json <<'EOF'
   {"quotes": [{"id":"a0000002-0000-4000-8000-000000000002","quote":"A second #179 smoke test line, same character, different source.","originalLanguage":"en","source":"Monty Python and the Holy Grail","date":"1975","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179b.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   ```
   Must return `200`. `SELECT COUNT(*) FROM Characters WHERE Name = 'Striker (Smoke Test)';` must now
   be `2` — a *second*, separate Character row, each linked to its own Source via `CharacterSources`
   — proving today's per-Source matching genuinely survived the mechanism change unchanged, not
   silently reused across Sources.

   **Sources.Date populated from the resolving quote** (#191) — a Source discovered implicitly from a
   quote (no `sources[]` entry naming it) previously never carried a date, even when the resolving
   quote had one. Re-imports the curated file's own `Airplane!`/`1980` quote to confirm the fix reaches
   a real import, not only startup seeding (already proven by a fresh container's seed — see the
   aggregate query below).
   ```bash
   curl -s "http://localhost:8080/api/v1/quotes/search?q=Airplane&field=source"
   ```
   Check the `date` field on the returned item(s) is `"1980"`, not `null` — this Source already exists
   from seeding, so this call only confirms the read path surfaces the seeded date correctly; it does
   not by itself re-exercise `ResolveSourceAsync`. To confirm the fix on a *fresh* seed specifically
   (the actual code path this issue fixed), inspect the database directly, matching the issue's own
   reproduction steps:
   ```bash
   docker stop -t 15 <container>
   docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-191.db
   dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
     --sql "SELECT COUNT(*) AS sources, SUM(CASE WHEN Date IS NOT NULL THEN 1 ELSE 0 END) AS have_date FROM Sources WHERE IsDeleted = 0"
   ```
   `have_date` must be nonzero and a large majority of `sources` (roughly 400+ of 479 on the current
   bundled dataset) — before the fix this was always `0`. Cross-check one specific title:
   ```bash
   dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
     --sql "SELECT Title, Type, Date FROM Sources WHERE Title = 'Jurassic Park' AND IsDeleted = 0"
   ```
   Must return `Date = 1993`.

   **Canonicalize explicit ids at capture — Source/Person/StageDirection/SoundCue/Conversation** (#209)
   — a file-authored explicit id previously reached storage in whatever raw casing the file used,
   never canonicalized; a `Guid`-typed lookup (which force-uppercases) then silently failed to find a
   non-canonically-stored row, even though the same row resolved fine via a join. Also proves the
   ConversationLines FOREIGN KEY fix: the bundled curated file's own Conversations reference
   StageDirections/SoundCues by id, and #209's own fix would have broken that reference if left
   incomplete — a clean seed with no `SQLite Error 19` is itself part of this check, not just the
   import below.
   ```bash
   cat > .claude/temp/smoke-209.json <<'EOF'
   {
     "quotes": [{"id":"f6000001-0000-4000-8000-000000000001","quote":"A #209 smoke test line.","originalLanguage":"en","source":"209 Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "sources": [{"id":"f6000002-0000-4000-8000-000000000002","title":"209 Smoke Test Film","type":"movie"}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-209.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/f6000002-0000-4000-8000-000000000002"
   curl -s "http://localhost:8080/api/v1/quotes/f6000001-0000-4000-8000-000000000001"
   ```
   The import must return `200`. The masterdata lookup — using the file's own lowercase id in the URL,
   the exact scenario that originally 404'd — must also return `200`, with `id` shown canonicalized (as
   lowercase, ADR 012's system-wide convention) in the response. The quote lookup must resolve `source`
   to `"209 Smoke Test Film"` via the Quote→Source join, proving the fix didn't break the join to make
   the masterdata lookup work.

   **Pagination contract: pageSize=0, max 500, default 20, page-beyond-last** (#195) — `/quotes`,
   `/admin/audit`, and `/import/actions` share one pagination contract; this proves it holds live on
   all three, not just at the unit-test/stub level. The two audit/import readers were caught passing
   `pageSize=0` straight into `LIMIT @pageSize` instead of translating it to `LIMIT -1` during this
   issue's own T2 pass — no existing unit test could catch it, since the stub readers those tests use
   echo their input back rather than exercising real SQL. Run this section after the sections above so
   `/admin/audit` and `/import/actions` already have rows to page through.
   ```bash
   curl -s "http://localhost:8080/api/v1/quotes?pageSize=0"
   curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=0" -H "X-Api-Key: <your admin key>"
   curl -s "http://localhost:8080/api/v1/import/actions?pageSize=0"
   ```
   On all three: `items` must contain every row (not zero), and `pageSize` in the response must equal
   `totalCount` — the effective-size contract, not the literal `0` requested.
   ```bash
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=501"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=501" -H "X-Api-Key: <your admin key>"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=501"
   ```
   All three must return `422` — `pageSize` above 500 is rejected, never silently clamped.
   ```bash
   curl -s "http://localhost:8080/api/v1/admin/audit" -H "X-Api-Key: <your admin key>"
   curl -s "http://localhost:8080/api/v1/import/actions"
   ```
   `pageSize` in both responses must be `20`, not the endpoints' old default of `50` — since both
   tables already have rows from the sections above, this also confirms the default is genuinely
   applied, not just an artifact of an empty table.
   ```bash
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=500&page=99"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=1&page=999999" -H "X-Api-Key: <your admin key>"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=1&page=999999"
   ```
   All three must return `422` (page beyond the last page) — true for `/admin/audit` and
   `/import/actions` only because the sections above already populated at least one row in each; on a
   database with zero rows in a table, page 1 of nothing is not "beyond the last page" and this would
   return `200` instead (see `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull`).
   The remaining case — `page`/`pageSize` publishing as `integer`, not `string`, on the live spec for
   all three paths — is **not** part of this manual checklist. It was originally a `curl | grep`
   check here, but grepping a pretty-printed, multi-line JSON body for a nested field is fragile (the
   first version of that exact command was wrong — it assumed single-line JSON and never matched
   anything) and its pass/fail requires a human or AI to eyeball the output. It is now
   `OpenApiSpecEndpointTests.cs` — a `WebApplicationFactory`-based test that fetches the real
   `/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument`, so it runs
   deterministically in every `dotnet test` instead of requiring a live container. See "Standard
   pagination contract" earlier in this file for why this needs a dedicated live-pipeline test at all,
   given `NumericParameterSchemaTransformer` already has its own unit tests.

   **Quotes.Id case-insensitive lookup** (#210) — Quotes.Id canonicalizes to lowercase, the same
   convention every other entity uses (`EntityIdentity.StableId`, `GuidExtensions.ToCanonicalId`) —
   this project's single settled id format after two prior revisions (ADR 012's revision history).
   Before #210's first pass, `GET /quotes/{id}` had no case-insensitive read-side mitigation at all —
   the one fully-unmitigated gap of this kind found across the whole codebase.
   ```bash
   cat > .claude/temp/smoke-210.json <<'EOF'
   {"quotes": [{"id":"F0000210-0000-4000-8000-000000000210","quote":"A #210 smoke test quote with an uppercase explicit id.","originalLanguage":"en","source":"Smoke Test Film 210","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]}
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/f0000210-0000-4000-8000-000000000210"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/F0000210-0000-4000-8000-000000000210"
   ```
   Import must return `200`. Both `GET` calls (lowercase URL casing, and the file's own original
   uppercase casing) must return `200` with the same quote, and the response's own `id` field must be
   the canonical **lowercase** form (`f0000210-...`) regardless of the uppercase casing the file
   supplied — proving both the capture-time canonicalization and the case-insensitive read together.

   **ConversationLines.QuoteId FK safety** (#210's casing-unification revision) — a conversation line
   referencing a quote by an id whose casing doesn't match the quote's own now-canonical form must not
   violate `ConversationLines`' real `FOREIGN KEY` constraint to `Quotes(Id)` — the same bug class #209
   found for `StageDirectionId`/`SoundCueId`, now also covering `QuoteId`.
   ```bash
   cat > .claude/temp/smoke-210-conv.json <<'EOF'
   {
     "quotes": [{"id":"f0000210-0000-4000-8000-000000000211","quote":"A #210 conversation-line smoke test quote.","originalLanguage":"en","source":"Smoke Test Film 210b","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "conversations": [{"id":"f0000210-0000-4000-8000-000000000212","description":"A #210 smoke test conversation.","lines":[{"order":1,"type":"quote","quoteId":"F0000210-0000-4000-8000-000000000211"}]}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210-conv.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   ```
   The quote's own `id` is lowercase; the conversation line's `quoteId` deliberately uses the uppercase
   form of the same id. Must return `200`, not a `SQLite Error 19: FOREIGN KEY constraint failed`.

   **Systemic id-case guard** (#210's scope expansion) — `Quotinator.Data.Diagnostics.SqlIdCaseGuard`
   scans every SQL query in the codebase for an unwrapped id-comparison at build/test time
   (`SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard` in both `Quotinator.Core.Tests`
   and `Quotinator.Data.Tests`, `RepositorySqlFactory_PassesIdCaseGuard` in
   `Quotinator.Data.Tests.Repositories.RepositorySqlGuardTests`) — this is unit-test-tier coverage, not a
   live/T2 check, and needs no separate Docker verification beyond the Quotes.Id scenario above; listed
   here only so a future reader knows why `RepositorySql.cs`'s generic `SelectById`/`SoftDelete`/etc. are
   now `LOWER()`-wrapped (ADR 012's system-wide lowercase revision — see that ADR for why `LOWER()`, not
   `UPPER()`) even though no single T2 scenario exercises them directly.

   **Read-time presentation normalization for string-typed id-reference fields** (#210's third
   revision) — `batchId`/`entityId`/`existingBatchId`/`recordId` are `string`-typed (not `Guid`-typed),
   so unlike `id` fields they get no automatic lowercase rendering from `System.Text.Json`'s `Guid`
   serialization default; a `LOWER(...) AS ColumnName` wrap was added to `Sql.SystemImportActions
   .SelectColumns` and `Sql.SystemAudit.SelectPaged` so these fields render canonically regardless of
   what casing is actually stored. Confirms a real end-to-end HTTP round trip, not just the unit-test-tier
   `ExistingBatchId_RoundTripsCorrectly`:
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     "http://localhost:8080/api/v1/import"
   curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
   curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=1" -H "X-Api-Key: <your admin key>"
   ```
   The import response's own `batchId` and every `quoteId` under `pendingActionIds`, and the
   `/import/actions` response's `batchId`/`entityId`/`existingBatchId`, and the `/admin/audit` response's
   `recordId`, must all be lowercase — freshly generated `Guid`s render lowercase from
   `GuidExtensions.ToCanonicalId()` regardless, so this mainly confirms no regression; the actual
   read-time fix (rendering an *already-uppercase* stored value as lowercase) is proven at the SQLite
   integration-test tier by `ExistingBatchId_RoundTripsCorrectly`, which writes a deliberately mixed-case
   fixture directly (bypassing capture-time canonicalization) and reads it back through this exact query
   path — a live T2 run cannot easily manufacture pre-existing non-canonical data through the API alone,
   since every write path now canonicalizes at capture time.

   **Uniform SELECT-list wrapping via `IEntityColumnMetadata`** (#210's follow-on round) — `RepositorySql.cs`'s
   generic queries (`SelectById`, `SelectByIds`, `SelectDeleted`, `SelectByForeignKey`, `SelectJunctionRow`,
   `SelectPage`) build an explicit column list via a caller-supplied `IEntityColumnMetadata` instead of
   `SELECT *`, wrapping every id column the same way hand-written `Sql.cs` queries do. Confirms every
   generic-repository-backed masterdata endpoint still returns correct data and lowercase ids after the
   `SELECT *` removal:
   ```bash
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/people?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/series?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/universes?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/conversations?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/stagedirections?pageSize=2"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/soundcues?pageSize=2"
   ```
   All must return `200` with populated `items` and lowercase `id` fields — these endpoints all go
   through `SqliteRepository<T>`/`SqliteRestorableRepository<T>`'s generic `GetPageAsync`/`GetByIdAsync`,
   the only live paths that exercise `RepositorySql`'s rewritten queries end to end (Characters'
   `CharacterSources` many-to-many link also exercises `SqliteLinkRepository`). Also confirm
   `GetByIdAsync`'s case-insensitive lookup survived the rewrite — fetch one of the returned ids from
   `GET .../sources/{id}` with both its original casing and an uppercased version; both must return `200`
   with the same, lowercase-rendered `id`.

   **`batchId` validated explicitly on `/actions/apply`, `/actions/discard`, `/actions/reverse`; request
   logging reports the real final status code** — found live via manual Visual Studio testing (T1), not
   this checklist: all three endpoints declared `batchId` as a required, non-nullable minimal-API
   parameter, so an omitted `batchId` threw `BadHttpRequestException` at the binding layer before the
   handler ever ran. The global safety net (`BadRequestExceptionHandler`) caught it and returned `422` —
   but with a message hard-coded to numeric parameters ("Numeric parameters (yearFrom, yearTo, ...) must
   be whole numbers"), actively wrong for a missing `batchId`. Separately, the completion log line for
   that same request read `→ 200`, not the `422` the client actually received — `Program.cs` registered
   `UseExceptionHandler()` before `RequestLoggingMiddleware`, so the exception unwound through the
   logging middleware's `finally` block (which reads `context.Response.StatusCode`, still the untouched
   default at that point) before the exception handler further out ever set the real status. Fixed by
   declaring `batchId` as `string?` and validating it explicitly at the point of origin (mirroring the
   "Numeric query parameter binding pattern" convention), and by moving `RequestLoggingMiddleware`'s
   registration to before `UseExceptionHandler()` so it wraps the exception handler instead of being
   wrapped by it.
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/discard"
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse"
   ```
   All three must return `422` with `"detail":"You must provide a batchId."` — never the generic
   "Numeric parameters..." message. With `Quotinator__LogRequests=true`, `docker logs` for each of these
   requests must show `→ 422`, not `→ 200`. Re-run a normal `apply` with a real `batchId` (see the
   "Import and staged-action review workflow" section above) to confirm the fix didn't break the happy
   path — still `200`.

   **Character Modify/decidability via the widened `characters[]` schema, explicit-id-honoured-on-Add,
   case-insensitive Source natural-key matching** (#175) — before this issue, `characters[]` only ever
   supported Correction (`id` present, matched by id) or brand-new-via-natural-key; there was no way to
   correct an existing Character's `Name` through the staging/decide pipeline the way Source/Person/
   StageDirection/SoundCue already could. The widened schema adds `sourceTitle`/`sourceType` (required
   unconditionally, mirroring `source`'s own shape) so a no-id entry can resolve through ADR 013's real
   Type-anchored, Series-scoped matching algorithm rather than a bare Name lookup. Also proves a real
   T2-only bug this issue's own re-verification pass found and fixed: an explicit `characters[]` id that
   matches nothing was being silently discarded in favour of a freshly-computed `EntityIdentity`-derived
   id — unlike `PlanSourcesAsync`'s own established `canonicalId ?? EntityIdentity.SourceId(...)`
   precedent, which a unit-test-only pass would not have caught (the unit suite's own two tests for this
   were written and initially passed against the *bug*, since they never independently verified which id
   actually landed in the database — the T2 walkthrough below is what surfaces it).
   ```bash
   cat > .claude/temp/smoke-175-add.json <<'EOF'
   {
     "quotes": [{"id":"a1111175-0000-4000-8000-000000000001","quote":"A #175 smoke test creation quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "characters": [{"name":"Smoke Test New Character","sourceTitle":"Airplane!","sourceType":"movie"}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-add.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   ```
   Must return `200`. `GET /api/v1/masterdata/characters` must now include "Smoke Test New Character"
   linked to the existing `Airplane!` Source (no id supplied — resolved via ADR 013's algorithm finding
   no candidate, then a genuine Add). Next, correct an existing Character by id under `review`:
   ```bash
   cat > .claude/temp/smoke-175-modify.json <<'EOF'
   {
     "quotes": [{"id":"a1111175-0000-4000-8000-000000000002","quote":"A #175 smoke test modify-trigger quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "characters": [{"id":"<an existing Character id from the query above>","name":"Renamed Via Smoke Test","sourceTitle":"Airplane!","sourceType":"movie"}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-modify.json" -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   ```
   Must return `202` with one pending id, and `GET /api/v1/import/actions?status=pending` must show
   `"ambiguousFields":["name"]` only — not `sourceId`, confirming the Modify payload's unchanged
   `SourceId` doesn't spuriously trip `FieldMergeResolver`. Decide and apply:
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" -d '{"characterName":{"choice":"replace"},"markCompletenessAs":"Complete"}' "http://localhost:8080/api/v1/import/actions/<id>/decide"
   curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
   curl -s "http://localhost:8080/api/v1/masterdata/characters/<id>"
   ```
   `name` must read "Renamed Via Smoke Test" and `completenessStatus` must be `Complete`. Re-attempt
   another Modify against the same id under `review` — must now stage `Blocked`, not `Pending`
   (`GET /api/v1/import/actions?status=Blocked`), and the on-disk name must be unchanged, proving a
   `Complete` Character can no longer be silently overwritten (the same guarantee Source/Person/
   StageDirection/SoundCue already have). Next, the explicit-id-honoured-on-Add fix itself:
   ```bash
   cat > .claude/temp/smoke-175-explicit-add.json <<'EOF'
   {
     "quotes": [{"id":"a1111175-0000-4000-8000-000000000005","quote":"A #175 smoke test explicit-id-add quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "characters": [{"id":"F5111175-0000-4000-8000-000000000175","name":"Explicit Id Character","sourceTitle":"Airplane!","sourceType":"movie"}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-explicit-add.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters/f5111175-0000-4000-8000-000000000175"
   ```
   The import's own file uses the id in uppercase; the masterdata lookup uses the canonical lowercase
   form. Both must succeed, and the returned `id` must be the lowercase-canonicalized form of the file's
   own id — never an unrelated `EntityIdentity`-derived one. Finally, the case-insensitive Source
   natural-key fix (`Sql.Sources.SelectIdByTitleAndType`/`SelectExistingByTitleAndType`):
   ```bash
   cat > .claude/temp/smoke-175-source-casing.json <<'EOF'
   {
     "quotes": [{"id":"a1111175-0000-4000-8000-000000000006","quote":"A #175 smoke test source-casing quote.","originalLanguage":"en","source":"AIRPLANE!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
     "characters": [{"name":"Case Insensitive Source Character","sourceTitle":"AIRPLANE!","sourceType":"movie"}]
   }
   EOF
   curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-source-casing.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
   curl -s "http://localhost:8080/api/v1/masterdata/sources?pageSize=500"
   ```
   Despite `AIRPLANE!` appearing in both the quote's own `source` and the character's `sourceTitle`,
   there must still be exactly one `"title":"Airplane!"` row in the Sources list — proving the entry
   resolved to the pre-existing Source rather than creating a case-sensitive duplicate. Clean up:
   ```bash
   rm -f .claude/temp/smoke-175-*.json
   ```

   **Bulk-decide a staged batch via file export/import — CSV and JSON** (#163) — `GET
   /import/actions/export` flattens every decidable field of a batch's Pending/Decided/Blocked Modify
   actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export back
   and applies each row's decision. Proves the export→edit→bulk-decide→apply round trip works over the
   real wire format in both directions, plus two live-only bugs found and fixed during this issue's own
   T2 pass that no unit test could catch (see below).
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     "http://localhost:8080/api/v1/import"
   ```
   Note the returned `batchId` (must be `202`, with pending Quote Modify actions to export).
   ```bash
   curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<batchId>&format=json" -o /tmp/export.json
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
     -F "batchId=<batchId>" -F "file=@/tmp/export.json" \
     "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
   ```
   Must return `200` with `errors: []` and `actionsDecided` matching the batch's own pending-action
   count — submitting export's own unmodified JSON output back into bulk-decide must always round-trip
   cleanly with zero errors. **This is the exact scenario that caught the first live-only bug**: ASP.NET's
   app-wide camelCase JSON default (`ConfigureHttpJsonOptions` in `Program.cs`) means export's own output
   is genuinely camelCase, but `ParseJsonRows`'s `element.Deserialize<ImportActionFieldRow>()` call had no
   explicit `JsonSerializerOptions`, silently falling back to `System.Text.Json`'s case-sensitive,
   PascalCase-only library default — every row failed with "missing required properties" despite the data
   being present. Every unit-test-level round trip used bare `JsonSerializer` calls on both sides, which
   silently agreed on PascalCase and never exercised the app's real camelCase configuration — only a live
   HTTP round trip through the actual pipeline surfaces this class of bug. Fixed via a dedicated
   `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` passed explicitly to the `Deserialize`
   call. Apply the batch and repeat via CSV to confirm the second wire format too:
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
   ```
   ```bash
   curl -s -X POST -H "X-Api-Key: <your admin key>" \
     -F "file=@data/sources/quotinator-curated.json" \
     -F 'settings={"duplicateResolution":{"default":"review"}}' \
     "http://localhost:8080/api/v1/import"
   curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<new batchId>&format=csv" -o /tmp/export.csv
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
     -F "batchId=<new batchId>" -F "file=@/tmp/export.csv" -F "format=csv" \
     "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
   ```
   Must also return `200` with `errors: []`. Malformed-row resilience — edit one row of the CSV to an
   invalid `Decision` value, leave the rest untouched, resubmit:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
     -F "batchId=<new batchId>" -F "file=@/tmp/export-with-one-bad-row.csv" -F "format=csv" \
     "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
   ```
   Must return `200` (never `422` for the whole request) with exactly one entry in `errors[]` naming the
   bad row's `actionId`, and every other row's action still decided — "one bad row never aborts the
   rest of the file", matching `POST /import`'s own established contract. Unknown-format and missing-key
   checks:
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>&format=xml"
   curl -s -w "\n%{http_code}\n" -X POST -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
   ```
   Must return `422` (unknown `format`) and `401` (no `X-Api-Key`) respectively. **The second live-only
   bug**: a request with neither `batchId` nor a multipart body at all —
   ```bash
   curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/bulk-decide"
   ```
   Must return `422` with `"detail":"You must provide a batchId."` — before the fix this returned a bare,
   uninformative `400` with no `detail` at all, because the endpoint originally bound `IFormFile? file`
   directly as a minimal-API parameter, which requires a form content-type to even attempt binding; a
   request with no `Content-Type`/body fails that check at the framework's own routing/binding layer, not
   as a normal thrown exception, bypassing `BadRequestExceptionHandler` entirely — the exact same bug
   class `POST /import` fixed earlier (see "Bodyless request validation" (#154) above) but never retrofitted
   onto this newer endpoint. Fixed by switching the parameter to `HttpRequest request` and checking
   `batchId`, then `request.HasFormContentType`, manually before ever attempting to read the form — mirroring
   `HandleImportFromRequestAsync`'s existing pattern exactly.

   **Per-source conflict-resolution rule files and title-alias files (#181) — fresh 4-file seed
   produces zero pending actions.** Every bundled file (`quotinator-curated.json`,
   `quotinator-series-universe.json`, `NikhilNamal17_popular-movie-quotes.json`,
   `vilaboim_movie-quotes.json`) runs under `review` policy with its own `ruleFile`/`sourceAliasFile`.
   A `ConflictResolutionRule` auto-resolves a genuinely ambiguous field on an already-seen entity id
   (Modify path only); a `SourceAliasRule` corrects a misspelled/inconsistent raw `(title, type)` to
   the already-canonical Source *before* Source resolution ever runs, so it applies to both a
   first-seen Add and a re-seen Modify, and prevents a duplicate Source row from being created for
   the wrong spelling in the first place (a `ConflictResolutionRule` alone cannot do this — it only
   ever corrects what a Quote's own field *displays*, never which Source row it links to). Confirm a
   fresh container with a stock image:
   ```bash
   docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
   curl -s http://localhost:8080/api/v1/version
   curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?status=pending"
   ```
   `/version` must show `quotes: 799` (the current bundled-data total). `/import/actions?status=pending`
   must return `200` with an **empty** `items` array — no file should be left "staged awaiting review".
   If any are, `docker logs` will show `"<file>" left staged awaiting review — batch "<id>", N action(s)
   pending a decision`; inspect via `GET /import/actions?batchId=<id>` to see which entity/field lacks a
   rule or alias. Cross-check for duplicate Sources directly, using `Quotinator.Tools.DbInspector`
   against a copy of the running container's database (`docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-181.db`):
   ```bash
   dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
     --sql "SELECT Title, Type, COUNT(*) AS c FROM Sources WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING c > 1"
   ```
   Must return **no rows** — any row here is a genuine duplicate Source that slipped through both the
   rule and alias mechanisms.

   **Proves the lookup genuinely reads the rule file's live content, not a cached or hardcoded value —
   live-verified 2026-07-25.** Temporarily delete the Auntie Mame rule entirely from
   `nikhilnamal17-conflict-rules.json` (`entityId: 088603c0-...`), then rebuild and run a fresh
   container:
   ```bash
   docker build -f docker/Dockerfile -t quotinator:local .
   docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
   curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
   ```
   With the rule removed, that one quote's conflict must now stage `Pending` again (confirmed:
   `ambiguousFields: ["date"]`) — proving the mechanism actually consults the file's content on every
   seed, not a cached decision from an earlier run. Restore the rule, then change its `resolution` from
   `Keep` to `Replace` and reseed again — `GET /quotes/{id}` will **not** show the change (`date` is
   Source-derived, read via JOIN from `Sources.Date`, and the Source was already fixed at the film's
   correct year by whichever occurrence was seen first — a per-quote rule only ever affects that Quote's
   own `MergedFields` audit trail, never a Source-owned field's real stored value, the same limitation
   #181's own Step 10 addendum documents). Check via `Quotinator.Tools.DbInspector` instead:
   ```bash
   docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-181.db
   dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
     --sql "SELECT MergedFields FROM System_ImportActions WHERE EntityId='088603c0-b35a-1b48-977d-ca08489a0cbb' AND ActionType='Modify'"
   ```
   The row for the batch matching NikhilNamal17's own rule file must show `"date":"2005"` (the incoming
   value — Replace won), confirmed changed from `"date":"1958"` under the original `Keep` rule; a
   *second* row may appear for vilaboim's own separate cross-file duplicate of the same quote id,
   resolved by its own unmodified rule in `vilaboim-conflict-rules.json` — unaffected by this change,
   since each bundled file's rule file only governs that file's own batch. Revert both edits before
   committing — this is a temporary local mutation to prove the mechanism, not a real data change.

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

