# Quotinator — Home Assistant Add-on

A self-hosted quote REST API. Serves real, verified quotes from films, books, television, and famous people.

## Installation

1. In Home Assistant, go to **Settings → Add-ons → Add-on Store**
2. Click the three-dot menu (⋮) in the top right → **Repositories**
3. Add `https://github.com/DutchJaFO/Quotinator` and click **Add**
4. Find **Quotinator** in the store and click **Install**

## API Endpoints

The REST API is accessible in two ways:

- **Via HA ingress (default):** Quotinator appears in the Home Assistant sidebar. The API is reachable under the same ingress path — use this for automations and scripts running inside HA.
- **Via direct port (for external tools):** Enable the direct access port in the add-on configuration (see [Direct access port](#direct-access-port) below), then use `http://<ha-host>:<port>/api/v1/`.

| Endpoint | Description |
|---|---|
| `GET /api/v1/quotes/random` | Random quote(s) — returns a result envelope with `status`, `items`, `totalMatching` |
| `GET /api/v1/quotes/random?n=10` | N random quotes (1–100) |
| `GET /api/v1/quotes/random?type=movie&type=book` | Random quote from movies or books (repeatable, OR logic) |
| `GET /api/v1/quotes/random?genre=sci-fi&character=Gandalf` | Filter by genre and character (AND between params) |
| `GET /api/v1/quotes/random?decade=1980` | Random quote from the 1980s (`decade` must be divisible by 10) |
| `GET /api/v1/quotes/random?yearFrom=1970&yearTo=1989` | Random quote from an explicit year range |
| `GET /api/v1/quotes` | All quotes, paginated; `type`, `genre`, `yearFrom`, `yearTo`, `year`, `decade` all supported |
| `GET /api/v1/quotes/{id}` | Quote by UUID |
| `GET /api/v1/quotes/search?q=term` | Search quotes; add `&type=movie&type=book` and/or `&field=quote\|source\|character\|author` |
| `GET /api/v1/health` | Health check |
| `GET /api/v1/version` | Running version |
| `POST /api/v1/admin/database/reseed` | Clear all data and reimport from `quotes.json` |
| `POST /api/v1/admin/database/reset` | Full reset: clear data + schema history, reapply migrations, reimport |

All endpoints accept an optional `lang` query parameter (ISO 639-1 code, e.g. `nl`, `de`) to request a translated quote response. Falls back to the original language if no translation exists. Error message language is controlled separately by the `Accept-Language` request header.

**`/random` filter envelope status values:** `Ok` (results found), `NoResults` (valid filters, no matching data), `InvalidType`, `InvalidGenre`, `InputTooLong`, `InvalidInput`. Year filter errors (`decade` not divisible by 10, `yearFrom > yearTo`) also return `InvalidInput` in the envelope.

A sliding-window rate limit of **100 requests per minute per IP** applies to all quote endpoints. Excess requests receive `429 Too Many Requests`.

The interactive API reference (Scalar) is available at `/scalar/v1` under whichever access path you use.

## Configuration

### Ingress

Ingress is enabled by default. Quotinator appears in your Home Assistant sidebar and no port configuration is needed for normal use.

### Language

The UI adapts to the browser's language preference automatically. A language selector in the navbar lets you override this and choose between English, Deutsch, and Nederlands. Selecting "Auto-detect" clears the override and returns to browser language detection. The choice is saved as a cookie and persists across sessions.

### SSL / HTTPS

SSL is **disabled by default**. When disabled, the direct access port (8080) serves plain HTTP, and the HA ingress (sidebar) handles HTTPS via the HA supervisor.

To enable HTTPS on the direct access port, set `ssl: true` and supply the certificate filenames (relative to `/ssl/`). The HA **Let's Encrypt** add-on writes `fullchain.pem` and `privkey.pem` to `/ssl/` automatically:

```yaml
ssl: true
certfile: fullchain.pem
keyfile: privkey.pem
```

If you use a custom certificate, copy the files to `/ssl/` and reference them by filename.

> **Note:** When using the HA ingress (sidebar), you do not need SSL configured here — the HA supervisor handles TLS termination for ingress traffic.

### Request logging

Controls whether incoming requests to the quote API endpoints are logged. Disabled by default — enable it to confirm your calls are arriving without needing `log_level: debug`.

When enabled, each request to `/api/v1/quotes/*` produces one log line:

```
GET /api/v1/quotes/random?n=5&lang=nl → 200 in 12ms
```

Rate-limited requests (`429`) are also logged. Health checks, Blazor traffic, and static assets are not.

### Log level

Controls the verbosity of the add-on log. Use `debug` when reporting issues. Default: `info`.

Valid values: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`.

### Direct access port

The direct access port is **disabled by default**. Enable it in the add-on configuration if you need to reach the API from outside Home Assistant — for example from MagicMirror², a shell script, or curl:

```yaml
ports:
  8080/tcp: 8080   # or any available port on the host
```

## Data

The add-on data directory (`/data`) persists across updates and restarts. It contains:

| Path | Purpose | Safe to delete? |
|---|---|---|
| `quotes.json` | Quote dataset — seed source and custom additions | Only if you want to reset to bundled data |
| `quotinatordata.db` | SQLite database — the live data store | **No** — this is your data |
| `backups/` | Pre-migration database snapshots, named `quotinatordata_v{N}_{timestamp}Z.db` | Yes — old backups can be pruned freely |
| `keys/` | ASP.NET Core Data Protection keys — used to sign antiforgery tokens and Blazor session descriptors | **No** — deleting this invalidates all active browser sessions; the add-on recovers on restart but users will need to reload |

You can replace `quotes.json` with a custom dataset — the format is documented at `https://github.com/DutchJaFO/Quotinator`.

## Access

| Method | How to reach it |
|---|---|
| Ingress (default) | Home Assistant sidebar — no port configuration needed |
| Direct access (if port enabled) | `http://<ha-host>:<port>/` |
| Health check (direct) | `http://<ha-host>:<port>/api/v1/health` |
| Random quote (direct) | `http://<ha-host>:<port>/api/v1/quotes/random` |
| API reference (direct) | `http://<ha-host>:<port>/scalar/v1` |

Replace `<port>` with the host port you mapped to `8080/tcp` in the add-on configuration.
