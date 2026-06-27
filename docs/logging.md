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

### What is captured

Every request produces one log line:

```
[Api - Request] GET /api/v1/quotes/search?q=back&genre=comedy → 200 in 11ms
[Api - Request] POST /api/v1/admin/database/reseed → 200 in 340ms
[Api - Request] GET /api/v1/version → 200 in 2ms
```

Captured: HTTP method, full URL (path + query string), response status code, elapsed time in milliseconds.

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
| `[Config]` | Config / env-var diagnostic lines |
| `[SSL]` | TLS cert load, Kestrel HTTPS bind |
| `[DataProtection]` | Key persistence setup |
| `[RateLimit]` | Rate limiter configuration |
| `[Server]` | Kestrel bind addresses, application lifetime events |
| `[Api - Request]` | Request log middleware — one line per HTTP request |
| `[Api - Random]` | Entry to GET /api/v1/quotes/random |
| `[Api - Search]` | Entry to GET /api/v1/quotes/search |
| `[Api - GetById]` | Entry to GET /api/v1/quotes/{id} |
| `[Api - GetAll]` | Entry to GET /api/v1/quotes/ |
| `[Api - Admin]` | Admin endpoint handlers (reseed, reset, seed preview) |
| `[Audit]` | Audit trail write operations (AuditWriter) |

New subsystems must register a prefix in this table before their log lines land in a PR.

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

```
[Api - Request] GET /api/v1/quotes/random → 200 in 8ms
[Api - Request] GET /api/v1/quotes/search?q=love&lang=nl → 200 in 14ms
[Api - Request] POST /api/v1/admin/database/reseed → 200 in 340ms
[Api - Request] GET /api/v1/version → 200 in 2ms
```

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
