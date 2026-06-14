# Quotinator — Home Assistant Add-on

A self-hosted quote REST API. Serves real, verified quotes from films, books, television, and famous people.

## Installation

1. In Home Assistant, go to **Settings → Add-ons → Add-on Store**
2. Click the three-dot menu (⋮) in the top right → **Repositories**
3. Add `https://github.com/DutchJaFO/Quotinator` and click **Add**
4. Find **Quotinator** in the store and click **Install**

## API Endpoints

The REST API is available at `http://<ha-host>:8080/api/v1/`.

| Endpoint | Description |
|---|---|
| `GET /api/v1/quotes/random` | One random quote |
| `GET /api/v1/quotes/random?n=10` | N random quotes (1–100) |
| `GET /api/v1/quotes` | All quotes, paginated |
| `GET /api/v1/quotes/{id}` | Quote by UUID |
| `GET /api/v1/quotes/search?q=term` | Search quotes; add `&field=quote\|source\|character\|author` to restrict the field |
| `GET /api/v1/health` | Health check |
| `GET /api/v1/version` | Running version |

All endpoints accept an optional `lang` query parameter (ISO 639-1 code, e.g. `nl`, `de`) to request a translated quote response. Falls back to the original language if no translation exists. Error message language is controlled separately by the `Accept-Language` request header.

The interactive API reference is available at `http://<ha-host>:8080/scalar/v1`.

## Configuration

### Ingress

Ingress is enabled by default. Quotinator appears in your Home Assistant sidebar and no port configuration is needed for normal use.

### Language

The UI adapts to the browser's language preference automatically. A language selector in the navbar lets you override this and choose from the supported languages (English, English UK, Deutsch, Nederlands). The choice is saved as a cookie and persists across sessions.

### Direct access port

The direct access port is **disabled by default**. Enable it in the add-on configuration if you need to reach the API from outside Home Assistant — for example from MagicMirror², a shell script, or curl:

```yaml
ports:
  8080/tcp: 8080   # or any available port on the host
```

## Data

Quotes are stored in `/data/quotes.json` inside the add-on data directory. This directory persists across add-on updates and restarts. You can replace the file with a custom dataset — the format is documented at `https://github.com/DutchJaFO/Quotinator`.

## Access

| Method | URL |
|---|---|
| Ingress (default) | Home Assistant sidebar |
| Direct access (if enabled) | `http://<ha-host>:8080` |
| Health check (direct) | `http://<ha-host>:8080/api/v1/health` |
| Random quote (direct) | `http://<ha-host>:8080/api/v1/quotes/random` |
