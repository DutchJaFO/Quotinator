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
| `GET /api/v1/quotes/random` | Random quote(s) — returns a result envelope with `status`, `items`, `totalMatching`, `requestedCount`, `returnedCount`. When a returned quote belongs to a conversation, one is embedded on that item's `embeddedConversation` and every other quote sharing it is excluded from the rest of the result |
| `GET /api/v1/quotes/random?n=10` | N random quotes (1–100) |
| `GET /api/v1/quotes/random?type=movie&type=book` | Random quote from movies or books (repeatable, OR logic) |
| `GET /api/v1/quotes/random?genre=sci-fi&character=Gandalf` | Filter by genre and character (AND between params) |
| `GET /api/v1/quotes/random?decade=1980` | Random quote from the 1980s (`decade` must be divisible by 10) |
| `GET /api/v1/quotes/random?yearFrom=1970&yearTo=1989` | Random quote from an explicit year range |
| `GET /api/v1/quotes` | All quotes, paginated; `type`, `genre`, `yearFrom`, `yearTo`, `year`, `decade` all supported |
| `GET /api/v1/quotes/{id}` | Quote by UUID |
| `GET /api/v1/quotes/search?q=term` | Search quotes; returns a result envelope (`status`, `items`, `totalMatching`, `message`). Add `&type=movie&type=book` and/or `&field=quote\|source\|character\|author` |
| `GET /api/v1/conversations` | Paginated list of Conversations — summaries only (`id`, `description`, `completenessStatus`, `lineCount`), never the full line list (`page`, `pageSize`) |
| `GET /api/v1/conversations/{id}` | A conversation's full ordered line list — quotes, stage directions, and sound cues |
| `GET /api/v1/masterdata/sources` | Paginated list of Sources — the films, television series, books, and other works quotes are drawn from (`page`, `pageSize`) |
| `GET /api/v1/masterdata/sources/{id}` | Source by UUID. Includes a `series` reference (`{id, name}`, or `null` if the source has no series) |
| `GET /api/v1/masterdata/characters` | Paginated list of Characters — fictional characters who deliver quotes (`page`, `pageSize`) |
| `GET /api/v1/masterdata/characters/{id}` | Character by UUID. Includes a `sources` array of `{id, name}` references for every Source the character appears in (#179) |
| `GET /api/v1/masterdata/people` | Paginated list of People — real individuals who said or wrote a quote (`page`, `pageSize`) |
| `GET /api/v1/masterdata/people/{id}` | Person by UUID |
| `GET /api/v1/masterdata/series` | Paginated list of Series — direct continuities of Sources within a Universe (`page`, `pageSize`) |
| `GET /api/v1/masterdata/series/{id}` | Series by UUID. Includes a `universe` reference (`{id, name}`, or `null` if the series has no universe) |
| `GET /api/v1/masterdata/universes` | Paginated list of Universes — fictional worlds or franchises spanning one or more Series (`page`, `pageSize`) |
| `GET /api/v1/masterdata/universes/{id}` | Universe by UUID |
| `GET /api/v1/masterdata/stagedirections` | Paginated list of StageDirections — reusable scene-setting or action descriptions that can appear in a conversation (`page`, `pageSize`) |
| `GET /api/v1/masterdata/stagedirections/{id}` | StageDirection by UUID |
| `GET /api/v1/masterdata/soundcues` | Paginated list of SoundCues — reusable audio elements that can appear in a conversation (`page`, `pageSize`) |
| `GET /api/v1/masterdata/soundcues/{id}` | SoundCue by UUID |
| `GET /api/v1/health` | Health check |
| `GET /api/v1/version` | Running version |
| `POST /api/v1/import` | Import one source file (JSON or, via `converter: "csv"` in `settings`, CSV) — same duplicate-detection engine as startup seeding. Multipart fields: `file`, `settings` (optional JSON: `converter`, `duplicateResolution`, `enrich`) — or pass `batchId` (query string) instead of `file` to apply a batch already staged by a prior `/import`/`/import/preview` call. Stages then attempts to apply — `200` when everything applied, `202` when any row needs a decision, `422` if neither `file` nor `batchId` is given. Returns a summary/conflicts/errors envelope (requires `X-Api-Key`) |
| `POST /api/v1/import/preview` | Same as `/import` but never applies — a real, inspectable batch is staged (review via `GET /import/actions?batchId=`), nothing is written to quote data. `200` when the batch would apply cleanly as-is, `202` when any row needs a decision (requires `X-Api-Key`) |
| `GET /api/v1/import/actions` | List staged import actions (Quote/Source/Character/Person), paginated. Filter by `status` (`Pending`, `Decided`, `Applied`, `Discarded`, `Blocked`), `batchId`, and/or `entityType`. Each item includes `relatedActionIds` and `ambiguousFields` |
| `POST /api/v1/import/actions/{id}/decide` | Stage a per-field keep/replace/custom decision for one staged Quote or Source action — git-merge-style, nothing is written yet. Any decision may also set `markCompletenessAs` to directly set the target record's completeness status once applied (requires `X-Api-Key`) |
| `POST /api/v1/import/actions/{id}/undo` | Revert a staged action's decision back to pending (requires `X-Api-Key`) |
| `POST /api/v1/import/actions/apply?batchId=` | Apply every action in a batch atomically, once every one of them has a decision — refuses with the still-pending ids otherwise (requires `X-Api-Key`) |
| `POST /api/v1/import/actions/discard?batchId=` | Discard every staged action in a batch — never touches domain tables (requires `X-Api-Key`) |
| `POST /api/v1/import/actions/reverse?batchId=` | Undo an applied import batch — Add actions are soft-deleted, Modify actions are restored to their pre-change values. Only the most recently applied batch still live may be reversed (strict LIFO stack); the batch's own record is itself soft-deleted on success. Pass `?preview=true` to check whether it would succeed without changing anything (requires `X-Api-Key`) |
| `GET /api/v1/admin/database/seed/preview` | Preview what a reseed would import — no data is changed. Reflects any already-downloaded source cache, but never triggers a network call itself. Each file includes `isValidJson` (whether it parsed at all) and, when it has a `downloadUrl`, `refreshOutcome`/`lastRefreshedAtUtc` (requires `X-Api-Key`) |
| `POST /api/v1/admin/database/reseed` | Clear all data and reimport from the bundled source files. Pass `?forceSourceRefresh=true` to bypass the download cache's freshness check for this call (requires `X-Api-Key`) |
| `POST /api/v1/admin/database/reset` | Full reset: clear data, reapply migrations, reimport (requires `X-Api-Key`). Audit log always survives. Schema version history is cleared and replayed by default; pass `?preserveSchemaVersion=true` to keep it. Pass `?forceSourceRefresh=true` to bypass the download cache's freshness check for this call |
| `POST /api/v1/admin/sources/refresh` | Refresh the download cache for any source with a `downloadUrl`/`github` manifest entry, without touching the database. Pass `?force=true` to bypass the freshness check. Each result includes `lastRefreshedAtUtc` — the cached copy's own last-write time, so an `uptodate` outcome still shows how old the data actually is (requires `X-Api-Key`) |

Admin endpoints require the `X-Api-Key: <key>` request header matching the `admin_api_key` set in the add-on configuration. Requests without the header, or with an incorrect key, receive `401 Unauthorized`.

Sources declaring a `downloadUrl` or `github` manifest entry are automatically refreshed from the network before seeding — controlled by the **Auto-update sources** and **Source update interval (hours)** add-on options (see add-on configuration). A network failure never blocks startup, reseed, or reset — the app falls back to whatever copy is already on disk.

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

## Data

The add-on data directory (`/data`) persists across updates and restarts. It contains:

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
3. Restore the **Quotinator** add-on from that backup.

The add-on and its database will be restored to the state they were in when that backup was taken.

> If no suitable HA backup exists, or if it is older than you would like, continue to Option B or C.

#### Option B — Restore from a database backup (preferred if no HA backup, preserves quotes)

The add-on automatically creates a backup of the database before applying schema upgrades. If you installed Quotinator before the import-provenance feature was added (roughly v1.5.0), a valid backup will exist.

> **Important:** the failed Reset and any subsequent restart attempts also create a backup — but those backups capture the *broken* state and are not useful for recovery. You must use the **oldest** backup, not the most recent one.

1. **Stop the Quotinator add-on** from the Home Assistant add-on page.
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
5. **Start the Quotinator add-on.** It will detect schema version 2, apply the missing migration correctly on the original tables, and reseed any missing data.

> The valid backup contains the database state from before the import-provenance migration. Any quotes added after that original upgrade will need to be re-added.

#### Option C — Delete the database (clean slate, loses all data)

Use this if no HA backup or database backup exists, or if you do not need to preserve existing data. The add-on will reseed from the bundled source files on next start.

1. **Stop the Quotinator add-on.**
2. **Open a terminal on your HA host** (see Option B, step 2).
3. **Delete the database file:**
   ```bash
   rm /data/quotinatordata.db
   ```
4. **Start the Quotinator add-on.** It will create a fresh database and import all bundled quotes automatically.

> Quotes added via the import feature or manual edits are not part of the bundled source files and will be lost.
