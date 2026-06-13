# Quotinator

A self-hosted quote REST API. Serves real, verified quotes from films, books, television, and famous people — no AI-generated quotes.

## Features

- REST API with random, search, paginated, and lookup endpoints
- 780 curated quotes seeded from MIT-licensed sources
- Optional language parameter — returns translated quote when available, falls back to original
- Multi-arch image (amd64 + aarch64)

## Usage

After installation, the REST API is available at `http://<ha-host>:8080/api/v1/`.

See the **Documentation** tab for endpoints and configuration options.
