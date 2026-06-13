# Quotinator — Home Assistant Add-on

A self-hosted movie quote REST API with Blazor management UI.

## Installation

1. In Home Assistant, go to **Settings → Add-ons → Add-on Store**
2. Click the three-dot menu (⋮) in the top right → **Repositories**
3. Add `https://github.com/DutchJaFO/Quotinator` and click **Add**
4. Find **Quotinator** in the store and click **Install**

## Configuration

### Port

By default, Quotinator listens on port `8080` for direct access. If port `8080` is already in use on your system, change it in the add-on configuration:

```yaml
ports:
  8080/tcp: 8081   # change the left-hand value to any available port
```

### Ingress

Ingress is enabled by default. The Quotinator UI will appear in your Home Assistant sidebar without any port configuration required.

## Data

Quotes are stored in `/data/quotes.json` inside the add-on data directory. This directory persists across add-on updates and restarts.

## Access

| Method | URL |
|---|---|
| Ingress (sidebar) | Via Home Assistant UI |
| Direct access | `http://<ha-host>:8080` |
| Health check | `http://<ha-host>:8080/api/v1/health` |
