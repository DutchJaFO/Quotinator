# Logging Standards

This file is the authoritative reference for how Quotinator structures its log output.
Apply these standards whenever you touch a file that emits log lines — boyscout style.

---

## Observability overview

Quotinator has two distinct observability tracks. They serve different purposes and must not be confused:

| Track | Output | Covers | Purpose |
|---|---|---|---|
| **Request log** | Serilog → HA supervisor log (text) | Every HTTP request | Operational visibility — confirms endpoints were called; detects unexpected traffic or hammering |
| **Audit trail** | `AuditEntries` table in SQLite | Write operations + admin actions | Accountability — records who did what to which record, for long-term review |

**All endpoints are logged.** If an endpoint is being called, it must be visible in the log — including health checks, admin routes, and the version endpoint. If monitor poll noise becomes a problem in a specific deployment, the operator can disable request logging entirely via the `log_requests` config option.

**Read operations are not in the audit trail.** They appear in the request log. Auditing every read would produce unbounded write load with no accountability value.

---

## Request log

### Categories and log levels

Every request is categorised by path for its tag, but all three categories are logged at Debug
(#244) — normal operation logs only the bare minimum (e.g. the startup/shutdown banners); per-request
detail is opt-in verbosity for when an operator is actively debugging a problem, not something that
appears by default just from serving traffic.

| Tag | Level | Paths |
|---|---|---|
| `[Api - Request]` | Debug | `/api/**` — REST API endpoints |
| `[Web - Request]` | Debug | Blazor pages, culture routes, OpenAPI spec, Scalar UI |
| `[Web - Asset]` | Debug | Static files (`.js`, `.css`, `.svg`, etc.), `/_framework/**`, `/_content/**`, `/lib/**` |

At the default `info` log level, no request traffic is visible at all. Set `debug` to see it.

### What is captured

Every request produces two log lines: one on arrival, one on completion. Each request gets a short random correlation ID (8 lowercase hex chars) that appears on both lines, making start/end pairs unambiguous when long-running requests overlap with shorter ones.

```
{tag} {id} {METHOD} {url}
{tag} {id} {METHOD} {url} → {status} in {ms}ms
```

Example — overlapping requests at `debug` level (all traffic visible; at the default `info` level none of this appears at all):

```
11:00:00.000  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=love
11:00:00.000  [Web - Asset]   3f9c1a2b GET /app.khy4lop6wu.css
11:00:00.001  [Api - Request] e5f6a7b8 GET /api/v1/health
11:00:00.001  [Web - Request] 7d4e8b1c GET /about
11:00:00.001  [Web - Asset]   3f9c1a2b GET /app.khy4lop6wu.css → 200 in 62ms
11:00:00.002  [Api - Request] e5f6a7b8 GET /api/v1/health → 200 in 2ms
11:00:00.003  [Web - Request] 7d4e8b1c GET /about → 200 in 2ms
11:00:00.014  [Api - Request] a1b2c3d4 GET /api/v1/quotes/search?q=love → 200 in 14ms
```

Captured: tag, correlation ID, HTTP method, full URL (path + query string), response status code, elapsed time in milliseconds.

Useful greps:
```bash
grep "a1b2c3d4"      # find both lines for one request
grep "→ 500"         # all failures
grep "→ 429"         # rate-limited requests
grep "in [0-9]\{4\}" # requests taking 1 second or more
```

### What is never captured

| What | Why |
|---|---|
| `X-Api-Key` header value | Authentication credential — admin endpoints require it; must never appear in logs |
| `Authorization` header value | Future user authentication token |
| `Cookie` header value | May contain session data or auth state |
| `Set-Cookie` response header | May contain session tokens |
| Request body | May contain credentials, PII, or import data |
| Any other header value | Log only what is explicitly listed above as captured |

**The security rule is about what data is captured, not which routes are included.** `POST /api/v1/admin/database/reset → 200 in 5ms` is safe — the path is not a secret; the API key is in the header, which is never logged.

If a query parameter ever carries a secret (e.g. `?token=...`), strip that parameter from the URL before logging — do not exclude the entire route.

### Serilog quoting and the `{:l}` specifier

Serilog quotes string properties in rendered output by default: `{Url}` → `"/api/v1/health"`. Use the `l` (literal) format specifier on every string property in a logging call to suppress this:

```csharp
// Wrong — Serilog renders: [Api - Request] "a1b2c3d4" "GET" "/api/v1/health"
_logger.LogDebug("[Api - Request] {Id} {Method} {Url}", id, method, url);

// Correct — Serilog renders: [Api - Request] a1b2c3d4 GET /api/v1/health
_logger.LogDebug("[Api - Request] {Id:l} {Method:l} {Url:l}", id, method, url);
```

Scalar numerics (`int`, `long`) are not quoted by Serilog and need no specifier.

**Unit tests must use Serilog's actual rendering**, not the MEL `formatter` callback. A plain `ILogger` test double uses the MEL formatter, which does not add quotes — tests pass while Serilog still produces quoted output in production. Use a `CaptureSink : ILogEventSink` backed by a real `LoggerConfiguration` to test exactly what appears in the log.

### Configuration

Request logging is enabled by the `log_requests` add-on config option (default: `true`). When disabled, the middleware is not registered and no request lines are emitted. This option exists for homelab setups where the operator does not want the overhead.

---

## Audit trail

### What is captured

Two categories of operations are written to the `AuditEntries` table:

**1. Record-level write operations** — written automatically by the repository base class on every write:

| Operation | When |
|---|---|
| `Insert` | A single record is created |
| `Update` | A record is modified |
| `SoftDelete` | A record is marked deleted |
| `Restore` | A soft-deleted record is reinstated |
| `HardDelete` | A record is permanently removed |
| `Purge` | All soft-deleted records in a table are permanently removed |
| `Link` | A many-to-many join record is created |
| `Unlink` | A many-to-many join record is removed |
| `BulkInsert` | A batch of records is inserted (one summary entry per batch, not per row) |

**2. Admin actions** — written explicitly by admin endpoint handlers via `IAuditWriter.WriteAsync`:

| Operation | Endpoint | `TableName` |
|---|---|---|
| `Reseed` | `POST /api/v1/admin/database/reseed` | `"Database"` |
| `Reset` | `POST /api/v1/admin/database/reset` | `"Database"` |
| `Import` | Future import endpoint | `"Database"` |
| `Backup` | Future backup endpoint | `"Database"` |

Admin actions use `TableName = "Database"` and `RecordId = null` — they are database-level operations, not row-level.

### What is never captured

- Credentials of any kind — the API key is authenticated but never stored; only the `Agent` identity is recorded
- Read operations — those are in the request log
- Request body content

### Schema

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Auto-increment; immutable |
| `TableName` | TEXT NOT NULL | Table affected, or `"Database"` for admin actions |
| `RecordId` | TEXT | UUID of the affected row; null for bulk and admin entries |
| `Operation` | TEXT NOT NULL | One of the operation constants above |
| `Agent` | TEXT | `User-Agent` header value; `"ui"` for Blazor circuit requests; null if not provided |
| `PerformedAt` | TEXT NOT NULL | ISO 8601 UTC timestamp |

`UserId` (nullable TEXT) will be added in the auth milestone alongside `Agent` — no rework of this schema needed.

### How to query

```bash
# All audit entries, most recent first
sqlite3 /data/quotinatordata.db "SELECT * FROM AuditEntries ORDER BY PerformedAt DESC LIMIT 50;"

# Admin actions only
sqlite3 /data/quotinatordata.db "SELECT * FROM AuditEntries WHERE TableName = 'Database' ORDER BY PerformedAt DESC;"

# Activity for a specific record
sqlite3 /data/quotinatordata.db "SELECT * FROM AuditEntries WHERE RecordId = '<uuid>';"
```

---

## Startup framing banners

Two banners wrap the entire startup sequence.

**Opening banner** — printed immediately via `Console.WriteLine`, before any startup work begins.
Name and status only; no data collected yet:

```
######################
#  Quotinator starting  #
######################
```

**Closing banner** — printed after all startup work is complete (DB init, config read, addresses bound).
This is the single place a reader confirms the server is up and correctly configured:

```
######################
#  Quotinator ready     #
######################
Version:        1.x.x
...
######################
```

Everything between the two banners is diagnostic or informational.

**Why Serilog instead of the default Microsoft console formatter?**
Both banners are emitted by `StartupSummaryLogger` via `logger.LogInformation`. The default
Microsoft console formatter collapses multi-line `LogInformation` strings to a single line in
the HA supervisor log. Serilog's output template uses the `{Message}` token, which preserves
embedded newlines — so the full multi-line block appears correctly in the log.

`Console.WriteLine` is no longer used anywhere in the codebase. All output goes through
`logger.LogInformation` via Serilog.

See `CLAUDE.md → Serilog — programmatic configuration` for the configuration constraints.

Individual single-line structured messages must use `logger.LogInformation`.

---

## Structured log prefix

Every log line must carry a `[Subsystem - Phase]` prefix so readers and `grep` can isolate
a subsystem without knowing message text.

Format: `[Subsystem - Phase] message text`

### Defined prefixes

| Prefix | When to use |
|---|---|
| `[Database - Init]` | Schema creation, migration, filename migration |
| `[Database - Seed]` | Quote import, genre seed, duplicate handling |
| `[Database - Stats]` | Final quote / source / character / people counts |
| `[Database - Backup]` | Backup operations |
| `[Database - SourceRefresh]` | Auto-update source cache: download attempts, staleness/collision resolution |
| `[Config]` | Config / env-var diagnostic lines |
| `[SSL]` | TLS cert load, Kestrel HTTPS bind |
| `[DataProtection]` | Key persistence setup |
| `[RateLimit]` | Rate limiter configuration |
| `[Server]` | Kestrel bind addresses, application lifetime events |
| `[Api - Request]` | REST API endpoint calls (`/api/**`) — logged at Debug |
| `[Web - Request]` | Blazor pages, culture routes, OpenAPI/Scalar UI — logged at Debug |
| `[Web - Asset]` | Static files and Blazor framework assets — logged at Debug |
| `[Api - Random]` | Entry to GET /api/v1/quotes/random |
| `[Api - Search]` | Entry to GET /api/v1/quotes/search |
| `[Api - GetById]` | Entry to GET /api/v1/quotes/{id} |
| `[Api - GetAll]` | Entry to GET /api/v1/quotes/ |
| `[Api - Admin]` | Admin endpoint handlers (reseed, reset, seed preview) |
| `[Api - GetAllPeople]` | Entry to GET /api/v1/masterdata/people |
| `[Api - GetPersonById]` | Entry to GET /api/v1/masterdata/people/{id} |
| `[Api - GetAllSeries]` | Entry to GET /api/v1/masterdata/series |
| `[Api - GetSeriesById]` | Entry to GET /api/v1/masterdata/series/{id} |
| `[Api - GetAllUniverses]` | Entry to GET /api/v1/masterdata/universes |
| `[Api - GetUniverseById]` | Entry to GET /api/v1/masterdata/universes/{id} |
| `[Api - GetAllStageDirections]` | Entry to GET /api/v1/masterdata/stagedirections |
| `[Api - GetStageDirectionById]` | Entry to GET /api/v1/masterdata/stagedirections/{id} |
| `[Api - GetAllSoundCues]` | Entry to GET /api/v1/masterdata/soundcues |
| `[Api - GetSoundCueById]` | Entry to GET /api/v1/masterdata/soundcues/{id} |
| `[Api - GetAllConversations]` | Entry to GET /api/v1/conversations |
| `[Api - GetConversationById]` | Entry to GET /api/v1/conversations/{id} |
| `[Api - Import]` | Import endpoint handlers (`POST /import`, `/import/preview`, `/import/rules/*`) |
| `[Api - GetAllBackups]` | Entry to GET /api/v1/admin/backups — logged at Debug (a read) |
| `[Api - GetBackupStatus]` | Entry to GET /api/v1/admin/backups/status — logged at Debug (a read) |
| `[Api - GetBackupContent]` | Entry to GET /api/v1/admin/backups/{name}/content — logged at Debug (a read) |
| `[Api - CreateBackup]` | POST /api/v1/admin/backups/create — Information on success, Warning on refusal |
| `[Api - DeleteBackup]` | DELETE /api/v1/admin/backups/{name} — Information on success, Warning on refusal |
| `[Audit]` | Audit trail write operations (AuditWriter) |

**Backup endpoints split by level deliberately (#349, developer decision 2026-08-29): a read is Debug,
an action that creates or destroys a restore point is Information.** The status endpoint is designed to
be called on every render of the degraded UI, so logging reads at Information would bury the two lines
an operator actually needs — "a backup was created" and "a backup was removed" — under its own polling.
This is the same reasoning the request log applies above, applied one layer up.

New subsystems must register a prefix in this table before their log lines land in a PR.

### Knowledgebase codes in a log line

A log line describing a condition an operator might need to understand or act on also carries a
Knowledgebase code, after the prefix and before the message text.

**The code is a message-template property, never concatenated into the string.** Serilog already
does this job — a named property is captured structurally (queryable in a structured sink, not merely
greppable) *and* rendered into the text. Building the same string by hand throws the structured half
away and reinvents what the logger provides:

```csharp
// Wrong — the code is invisible to any structured sink, and re-typed at every call site
_logger.LogWarning($"[Database - Init] {KnowledgebaseCodes.DataDirectoryNotWritable}: …");

// Correct — captured as KbCode, rendered literally by the :l specifier
_logger.LogWarning("[Database - Init] {KbCode:l}: the data directory cannot be written …",
    KnowledgebaseCodes.DataDirectoryNotWritable);

// With a status code, same treatment
_logger.LogWarning("[Database - Init] {KbCode:l} {KbStatus:l}: the data directory cannot be written …",
    KnowledgebaseCodes.DataDirectoryNotWritable, KnowledgebaseStatus.Investigating);
```

Renders as:

```
[Database - Init] QTN-DB-014: the data directory cannot be written …
[Database - Init] QTN-DB-014 QTN-INV: the data directory cannot be written …
```

The `:l` specifier is required on both, per "Serilog quoting and the `{:l}` specifier" above — without
it Serilog renders `"QTN-DB-014"` with quotes. Test the rendering with a `CaptureSink` over a real
`LoggerConfiguration`, not the MEL formatter callback, for the same reason that section gives.

Code values are `const string` in one place and referenced from every surface that reports the
condition — the log line, the health response, the notification body, the degraded UI — never typed out
a second time. That is the same rule endpoint names follow (see `CLAUDE.md`'s endpoint naming
convention), and it is what makes one condition look up identically everywhere.

The `[Subsystem - Phase]` prefix is unaffected; codes are additive and `grep` on either keeps working.
Which conditions get a code is decided by triage, not by log level — the format, the area list and the
allocation rules live in [`knowledgebase.md`](knowledgebase.md). Routine Debug output (request, asset)
never carries one.

This is not the numeric `EventId` convention ruled out below: a code is user-facing text appearing
identically across every surface the condition reaches, not navigation metadata on a `[LoggerMessage]`
attribute. See `knowledgebase.md`'s closing section for the full distinction.

### Example output between the banners

```
[Database - Init] initializing
[Database - Init] schema: none found — creating fresh
[Database - Init] schema v1 created
[Database - Seed] importing 410 quotes from vilaboim_movie-quotes.json (Bundled)...
[Database - Seed] seeding complete — 780 unique quotes from 792 total (12 duplicates)
[Database - Stats] 780 quotes  3 sources  42 characters  12 people
[Server] listening on http://0.0.0.0:8080
```

### Example request log output

At `info` level — no request traffic is visible; request logging is Debug-only across all three
categories (#244).

At `debug` level — all traffic visible, grep by tag to isolate:

```
11:00:00.000  [Api - Request] a1b2c3d4 GET /api/v1/quotes/random
11:00:00.001  [Web - Asset]   3f9c1a2b GET /app.khy4lop6wu.css
11:00:00.001  [Web - Request] 7d4e8b1c GET /about
11:00:00.003  [Web - Asset]   3f9c1a2b GET /app.khy4lop6wu.css → 200 in 62ms
11:00:00.004  [Web - Request] 7d4e8b1c GET /about → 200 in 2ms
11:00:00.008  [Api - Request] a1b2c3d4 GET /api/v1/quotes/random → 200 in 8ms
```

---

## Logging call-site pattern

**Rule:** any `LogInformation`/`LogDebug`/`LogTrace`/`LogCritical` call that takes template arguments
must go through a `[LoggerMessage]`-decorated extension method — never call those four directly with
arguments. A bare, argument-free call (e.g. the opening startup banner) is exempt — there is nothing to
evaluate ahead of the level check.

**`LogWarning`/`LogError` are a deliberate exception, not an oversight.** Verified directly (2026-08-09,
#269): `CA1873` — despite its own documentation describing the rule as applying uniformly to any
logging call with an "expensive" argument — does not fire on `LogWarning`/`LogError` calls at all in
this SDK version, even when fed an identical, genuinely expensive argument (`string.Join(...)`) that
does trigger it on `LogInformation` in the same test. Since the goal here is an *enforced* rule backed
by the 0-warnings build policy, `LogWarning`/`LogError` calls are out of this rule's scope — converting
them would be unenforceable busywork with no analyzer to prevent regression. If `CA1873`'s coverage
changes in a future .NET SDK to include these levels, revisit this exception.

**Why:** the standard `ILogger` extension methods take a `params object?[]` — every call allocates and
boxes that array *before* checking whether the target log level is even enabled, regardless of how
trivial the arguments are (#269). `[LoggerMessage]` source-generated partial methods check `IsEnabled`
first, inside the generated method, eliminating that allocation entirely.

### Where a new method belongs

First check whether `Quotinator.Logging`'s shared `LogMessages` class already covers the new call
site's *shape* — same parameter types, same structural intent (e.g. "a subsystem tag plus a paginated
page/pageSize pair", or "a subsystem tag plus a bare id"). If so, reuse it with a different `tag`
argument rather than declaring a near-duplicate method. `Quotinator.Data`, `Quotinator.Core`,
`Quotinator.Api`, and `Quotinator.Changelog` all reference `Quotinator.Logging` for exactly this —
including the domain-agnostic data layer, so the same shape doesn't silently reappear as duplicated,
unconverted code the next time a paginated endpoint is added anywhere in the solution.

Only when the message text is genuinely specific to one subsystem does it belong in that project's own
`Logging/LogMessages.cs` instead (`src/Quotinator.Api/Logging/`, `src/Quotinator.Core/Logging/`,
`src/Quotinator.Data/Logging/`, `src/Quotinator.Changelog/Logging/`). Extension methods on `ILogger`,
not partial methods inside the calling class — no `partial` modifier changes needed on the caller, and
every call site is a plain one-line method call.

No explicit `EventId` is assigned on any `[LoggerMessage]` attribute — this project's log-navigation
convention is the `[Subsystem - Phase]` text prefix above, not numeric event IDs.

### A `[LoggerMessage]` conversion does not, by itself, defer an expensive argument

`[LoggerMessage]`'s `IsEnabled` check happens *inside* the generated method — but C# always evaluates
every argument expression at the call site *before* invoking any method. `logger.LogFileReport(fileName,
FormatReport(report))` still calls `FormatReport(report)` unconditionally, no matter what the generated
method's body does afterward. If an argument is a bare identifier or simple member-access read (`page`,
`quotes.Count`), this doesn't matter. If an argument is itself a non-trivial computation (`string.Join(...)`,
`Path.GetFileName(...)`, a formatting helper call), wrap the call site in an explicit
`logger.IsEnabled(LogLevel.X)` check as well:

```csharp
if (logger.IsEnabled(LogLevel.Information))
    logger.LogFileReport(fileName, FormatReport(report));
```

`CA1873` catches this too — it flags a `[LoggerMessage]`-wrapped call the same as a raw one when an
argument expression is expensive, since the underlying problem (unconditional evaluation) is identical
either way.

### Why this is enforced, not just documented

`CA1873` is escalated to `warning` in `.editorconfig` — the project's 0-warnings build policy means a
future direct `LogInformation(...)` call with arguments, or a `[LoggerMessage]` call fed an unguarded
expensive expression, fails the build immediately, the same way every other escalated analyzer rule
here is enforced. This section explains why and how to comply; the analyzer is what actually blocks a
regression. No separate guard test is needed on top of that — `CA1873` already covers this surface
project-wide.

---

## Verification log lines, and demoting them once verified

**Some behaviour is only verifiable if the log states it outright.** Where two code paths produce
identical observable output — a fallback that renders the same page as the real thing, a cache hit that
looks like a cache miss — nothing in the system reveals which one ran. Verification then has nothing to
stand on except the *absence* of an error message, and absence proves nothing: it is equally consistent
with "the healthy path ran" and "a silent path ran that nobody thought to log".

Found repeatedly in #309 (steps 16–18): the changelog's JSON fallback was serving reads on every boot,
then a partially-rebuilt table was served as though complete, then a previous run's content was served
as though current. All three rendered a plausible page, all three survived T1 and T2 passes that
declared the feature complete, and all three became visible the moment one line stated *which source
answered*.

**The rule.** When a path's correctness cannot be observed from its output, add a log line that states
what actually happened — positively, not by implication. Assert on that line in tests and smoke tests,
never on the absence of a failure message.

**Demote it once it has been verified.** A verification line earns Information level while the behaviour
is being established and confirmed live. Once it is confirmed and covered by a test that will catch a
regression, it becomes ordinary operational noise and should drop to Debug, matching the request log's
own reasoning above — normal operation logs the bare minimum, and detail is opt-in for an operator who
is actively investigating.

Three things that make a demotion safe rather than a silent loss of coverage:

- **A test must already cover the behaviour**, not just the log line. If the only thing proving the
  behaviour is a human reading a log, demoting it deletes the coverage.
- **Any smoke test grepping for that line must be updated in the same commit**, either to run the
  container at `debug` level or to assert something else. A smoke test silently grepping for a line
  that no longer appears at the default level is a guaranteed false failure — or worse, a false pass if
  it asserts absence.
- **Warnings are never demoted.** A line reporting a fallback, a degraded state, or a failure stays at
  Warning regardless of how well verified it is. Demotion applies only to the positive "this is what
  ran" statement, never to "this went wrong".

---

## Security rule

Never log a secret value. This applies everywhere — banners, structured log lines, diagnostic dumps, and the request log middleware:

- API keys and any future credentials appear as `set` or `not set` in diagnostic output
- Header values are never logged — the `X-Api-Key` value, `Authorization` token, `Cookie`, and `Set-Cookie` must not appear in any log line
- The `User-Agent` value is safe to log — it is identification, not authentication

---

## Boyscout rule

When you edit a file that emits log lines without the `[Subsystem - Phase]` prefix, add the prefix
in the same commit. Do not defer it to a separate cleanup PR.

This is one of two non-analyzer boyscout rules the project keeps — no compiler warning enforces it,
so it relies on the same discipline by hand. See `CLAUDE.md` → "Zero-warnings policy and boyscout
rules" for the full policy, the analyzer-backed rules (`var`/IDE0008, target-typed `new`/IDE0090),
and the other non-analyzer rule (SQL column names via `nameof`).
