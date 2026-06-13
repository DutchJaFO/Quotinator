# Docker

## Overview

Quotinator ships as a **single container** hosting both the REST API and the Blazor Server frontend. This is required for Home Assistant add-on compatibility (the HA supervisor runs single-container add-ons).

- **Image:** `ghcr.io/<owner>/quotinator`
- **Base image:** `mcr.microsoft.com/dotnet/aspnet:10.0`
- **Platforms:** `linux/amd64`, `linux/arm64`
- **Port:** `8080` (HTTP only — HTTPS is terminated at the reverse proxy)
- **Data:** `quotes.json` is mounted as a volume at `/app/data`

---

## Prerequisites

### Installing Docker Desktop (Windows)

Docker Desktop is required to build and run the container locally.

1. Install via winget (recommended):

```powershell
winget install Docker.DockerDesktop
```

2. Restart your machine after installation

3. WSL 2 is required on Windows — Docker Desktop will prompt if it is missing. If needed, install it manually in an **Administrator** PowerShell:

```powershell
wsl --install
```

Then restart again before starting Docker Desktop.

4. Verify Docker is running:

```powershell
docker info
```

You should see system information. If you see a connection error, Docker Desktop is not running — start it from the Start menu and wait for the whale icon in the system tray to stop animating.

---

## Files

| File | Purpose |
|---|---|
| `docker/Dockerfile` | Multi-stage build for the combined API + UI |
| `docker/docker-compose.yml` | Local development and testing |
| `.dockerignore` | Excludes `bin/`, `obj/`, `data/`, docs, scripts from the build context |

---

## Building the image

### Via Docker Compose (recommended)

Docker Compose builds the image automatically when you run `up --build`. No separate build step needed.

### Manually

Build the image from the repo root:

```powershell
docker build -f docker/Dockerfile -t quotinator .
```

This uses the multi-stage Dockerfile which:
1. Restores NuGet packages (cached layer — only re-runs when a `.csproj` changes)
2. Publishes the app in Release configuration
3. Copies the output into the lightweight ASP.NET runtime image

To verify the image was created:

```powershell
docker images quotinator
```

---

## Running locally with Docker

### Using Docker Compose (recommended)

```bash
docker compose -f docker/docker-compose.yml up --build
```

Once running, the application is available at `http://localhost:8080`.

> **Note:** Scalar and the OpenAPI spec are disabled in Production mode. To enable them in the container, add `ASPNETCORE_ENVIRONMENT=Development` to the compose file or run command. Do not do this in a production deployment.

### Using Docker directly

```bash
docker build -f docker/Dockerfile -t quotinator .
docker run -d -p 8080:8080 -v ./data:/app/data quotinator
```

---

## Production / homelab deployment

```bash
docker run -d \
  -p 8080:8080 \
  -v /path/to/data:/app/data \
  ghcr.io/<owner>/quotinator:latest
```

---

## Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Set to `Development` to enable Scalar UI and OpenAPI spec |
| `ASPNETCORE_HTTP_PORTS` | `8080` | HTTP port the app listens on |

---

## Image naming

Images are published to GitHub Container Registry on every release tag as `ghcr.io/<owner>/quotinator`.

| Tag pushed | Docker tags produced |
|---|---|
| `v1.2.3` (stable) | `1.2.3`, `1.2`, `1`, `latest` |
| `v1.2.3-beta.1` (beta) | `1.2.3-beta.1` only |

See [`ci-cd.md`](ci-cd.md) for the full release process.

---

## Build notes

### Layer caching

The Dockerfile copies project files and runs `dotnet restore` before copying source. NuGet restore is cached as a separate layer and only re-runs when a `.csproj` changes.

### Multi-arch

Multi-arch publishing (`linux/amd64` + `linux/arm64`) is handled by `docker buildx` in the GitHub Actions release workflow.
