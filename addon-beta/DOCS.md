# Quotinator (BETA) — Home Assistant Add-on

A self-hosted quote REST API. Serves real, verified quotes from films, books, television, and famous people.

This is the **beta channel** of Quotinator — the same software as the stable **Quotinator** add-on, published from the same repository and Docker image, but tracking pre-release versions. Install this alongside (or instead of) the stable add-on to try upcoming changes before they reach the stable channel. Everything below — API endpoints, configuration, data layout, troubleshooting — applies identically to both, since it's the same application.

## Installation

1. In Home Assistant, go to **Settings → Add-ons → Add-on Store**
2. Click the three-dot menu (⋮) in the top right → **Repositories**
3. Add `https://github.com/DutchJaFO/Quotinator` and click **Add**
4. Find **Quotinator (BETA)** in the store and click **Install**

## API Endpoints

The REST API is accessible in two ways:

- **Via HA ingress (default):** Quotinator (BETA) appears in the Home Assistant sidebar. The API is reachable under the same ingress path — use this for automations and scripts running inside HA.
- **Via direct port (for external tools):** Enable the direct access port in the add-on configuration (see [Direct access port](#direct-access-port) below), then use `http://<ha-host>:<port>/api/v1/`.

For the full, current endpoint list — every route, its parameters, and response shape — use the interactive Scalar reference at `/scalar/v1` (under whichever access path you use) or the raw OpenAPI spec at `/openapi/v1.json`. A plain-text version with the same content lives in the repository at [`docs/api-endpoints.md`](https://github.com/DutchJaFO/Quotinator/blob/main/docs/api-endpoints.md) — note that link tracks the latest `main` branch, which can run slightly ahead of whichever beta build you have installed, so prefer the live Scalar/OpenAPI reference above if the two ever disagree.

A few common examples to get started:

| Endpoint | Description |
|---|---|
| `GET /api/v1/quotes/random` | One random quote |
| `GET /api/v1/quotes/random?n=10&type=movie&genre=drama` | Multiple random quotes, filtered by type and genre |
| `GET /api/v1/quotes/search?q=term` | Search quotes by text |
| `GET /api/v1/masterdata/sources` | Paginated list of Sources (films, TV series, books, etc.) |
| `GET /api/v1/health` | Health check |
| `POST /api/v1/import` | Import a source file (requires `X-Api-Key`) |
| `POST /api/v1/admin/database/reseed` | Clear all data and reimport from the bundled source files (requires `X-Api-Key`) |

Admin endpoints require the `X-Api-Key: <key>` request header matching the `admin_api_key` set in the add-on configuration. Requests without the header, or with an incorrect key, receive `401 Unauthorized`.

All endpoints accept an optional `lang` query parameter (ISO 639-1 code, e.g. `nl`, `de`) to request a translated quote response. Falls back to the original language if no translation exists. Error message language is controlled separately by the `Accept-Language` request header.

A sliding-window rate limit of **100 requests per minute per IP** applies to all quote endpoints. Excess requests receive `429 Too Many Requests`.

## Configuration

### Ingress

Ingress is enabled by default. Quotinator (BETA) appears in your Home Assistant sidebar and no port configuration is needed for normal use.

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

Rate-limited requests (`429`) are also logged. Blazor pages and static assets are logged at debug level (`[Web - Request]` and `[Web - Asset]` tags) and are not visible at the default `info` log level.

### Log level

Controls the verbosity of the add-on log. Use `debug` when reporting issues. Default: `info`.

Valid values: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `fatal`.

### Direct access port

The direct access port is **disabled by default**. Enable it in the add-on configuration if you need to reach the API from outside Home Assistant — for example from MagicMirror², a shell script, or curl:

```yaml
ports:
  8080/tcp: 8080   # or any available port on the host
```

> If you also run the stable Quotinator add-on with its direct access port enabled, map this one to a **different** host port — two add-ons cannot share the same host port.

## Data

The add-on data directory (`/data`) persists across updates and restarts, and is **separate from the stable add-on's own data directory** — installing both keeps two independent databases. It contains:

| Path | Purpose | Safe to delete? |
|---|---|---|
| `quotinatordata.db` | SQLite database — the live data store | **No** — this is your data |
| `backups/` | Pre-migration database snapshots, named `quotinatordata_v{N}_{timestamp}Z.db` | Yes — old backups can be pruned freely |
| `keys/` | ASP.NET Core Data Protection keys — used to sign antiforgery tokens and Blazor session descriptors | **No** — deleting this invalidates all active browser sessions; the add-on recovers on restart but users will need to reload |

## Access

| Method | How to reach it |
|---|---|
| Ingress (default) | Home Assistant sidebar — no port configuration needed |
| Direct access (if port enabled) | `http://<ha-host>:<port>/` |
| Health check (direct) | `http://<ha-host>:<port>/api/v1/health` |
| Random quote (direct) | `http://<ha-host>:<port>/api/v1/quotes/random` |
| API reference (direct) | `http://<ha-host>:<port>/scalar/v1` |

Replace `<port>` with the host port you mapped to `8080/tcp` in the add-on configuration.

## Troubleshooting

### Add-on fails to start after using Reset Database

**Affected versions:** v1.5.x – v1.6.1
**Fixed in:** v1.6.2

Using the **Reset Database** admin action in v1.5.x – v1.6.1 can leave the database in a broken state where the add-on fails to start on every subsequent attempt. The error in the add-on log is:

```
SQLite Error 1: 'duplicate column name: ImportBatchId'
```

This happens because the reset clears the schema version history but does not drop the underlying tables. When the add-on tries to restart and re-apply its migrations, it attempts to add a column that already exists.

To recover, choose the option that applies to your situation.

#### Option A — Restore a Home Assistant backup (easiest, preserves everything)

If you have a recent Home Assistant backup taken before the Reset Database was triggered, this is the simplest recovery path. It restores both the add-on and its data in one step without any terminal access.

1. Go to **Settings → System → Backups** in Home Assistant.
2. Select a backup from before the problem occurred.
3. Restore the **Quotinator (BETA)** add-on from that backup.

The add-on and its database will be restored to the state they were in when that backup was taken.

> If no suitable HA backup exists, or if it is older than you would like, continue to Option B or C.

#### Option B — Restore from a database backup (preferred if no HA backup, preserves quotes)

The add-on automatically creates a backup of the database before applying schema upgrades. If you installed Quotinator before the import-provenance feature was added (roughly v1.5.0), a valid backup will exist.

> **Important:** the failed Reset and any subsequent restart attempts also create a backup — but those backups capture the *broken* state and are not useful for recovery. You must use the **oldest** backup, not the most recent one.

1. **Stop the Quotinator (BETA) add-on** from the Home Assistant add-on page.
2. **Open a terminal on your HA host.** Use the [Terminal & SSH add-on](https://github.com/home-assistant/addons/tree/master/ssh) or SSH directly into Home Assistant OS.
3. **List all backups, oldest first:**
   ```bash
   ls -lt /data/backups/ | tail -n +2 | tail -5
   ```
   You should see files named `quotinatordata_v{N}_{timestamp}Z.db`. If the only files there have today's date, they are the corrupted backups — use Option B instead.
4. **Restore the oldest backup** (the one with the earliest timestamp):
   ```bash
   cp /data/backups/quotinatordata_v2_<earliest-timestamp>Z.db /data/quotinatordata.db
   ```
   Replace `<earliest-timestamp>` with the actual filename from step 3.
5. **Start the Quotinator (BETA) add-on.** It will detect schema version 2, apply the missing migration correctly on the original tables, and reseed any missing data.

> The valid backup contains the database state from before the import-provenance migration. Any quotes added after that original upgrade will need to be re-added.

#### Option C — Delete the database (clean slate, loses all data)

Use this if no HA backup or database backup exists, or if you do not need to preserve existing data. The add-on will reseed from the bundled source files on next start.

1. **Stop the Quotinator (BETA) add-on.**
2. **Open a terminal on your HA host** (see Option B, step 2).
3. **Delete the database file:**
   ```bash
   rm /data/quotinatordata.db
   ```
4. **Start the Quotinator (BETA) add-on.** It will create a fresh database and import all bundled quotes automatically.

> Quotes added via the import feature or manual edits are not part of the bundled source files and will be lost.
