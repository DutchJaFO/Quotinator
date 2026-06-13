# Quotinator

A self-hosted movie quote REST API with a Blazor management UI. Serves real, verified movie quotes from a curated dataset — no AI-generated quotes.

## Features

- REST API with random, search, and lookup endpoints
- Blazor web UI accessible via Home Assistant ingress or direct access
- Quotes persisted in a local JSON dataset that survives updates
- Multi-arch image (amd64 + aarch64)

## Usage

After installation, open the add-on UI from the Home Assistant sidebar. The REST API is available at `http://<ha-host>:8080/api/v1/`.

See the **Documentation** tab for full configuration options.
