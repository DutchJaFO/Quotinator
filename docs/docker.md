# Docker

## Overview

Quotinator ships as a **single container** hosting both the REST API and the Blazor Server frontend. This is required for Home Assistant add-on compatibility (the HA supervisor runs single-container add-ons).

- **Image:** `ghcr.io/dutchjafo/quotinator`
- **Base image:** `mcr.microsoft.com/dotnet/aspnet:10.0`
- **Platforms:** `linux/amd64`, `linux/arm64`
- **Ports:** `8080` (direct access — HTTP or HTTPS), `8099` (Home Assistant ingress — always HTTP)
- **Data:** `quotes.json` persisted via Docker volume at `/app/data`

---

## Prerequisites

### Installing Docker Desktop (Windows)

Docker Desktop is required to build and run the container locally.

1. Install via winget (recommended):

```powershell
winget install Docker.DockerDesktop
```

2. Restart your machine after installation.

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
| `.dockerignore` | Excludes `bin/`, `obj/`, docs, scripts from the build context. `data/` is intentionally included so `dotnet publish` can copy `quotes.json` into the image. |

---

## Building the image

### Via Docker Compose (recommended)

Docker Compose builds the image automatically when you run `up --build`. No separate build step needed.

### Manually

Build the image from the repo root:

```powershell
docker build -f docker/Dockerfile -t quotinator:local .
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

### Using Docker directly

```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run -d -p 8080:8080 -v ./data:/app/data quotinator:local
```

---

## Production / homelab deployment

```bash
docker run -d \
  -p 8080:8080 \
  -v /path/to/data:/app/data \
  ghcr.io/dutchjafo/quotinator:latest
```

### With SSL

```bash
docker run -d \
  -p 8080:8080 \
  -v /path/to/data:/app/data \
  -v /path/to/certs:/ssl:ro \
  -e Quotinator__Ssl=true \
  -e Quotinator__SslCertFile=/ssl/fullchain.pem \
  -e Quotinator__SslKeyFile=/ssl/privkey.pem \
  ghcr.io/dutchjafo/quotinator:latest
```

See [`home-assistant.md`](home-assistant.md) for the Home Assistant add-on deployment.

---

## Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Controls environment name shown in `/api/v1/version` |
| `ASPNETCORE_HTTP_PORTS` | _(empty)_ | Cleared in the Dockerfile — port binding is owned by Kestrel configuration in `Program.cs` |
| `Quotinator__DataPath` | `/app/data/quotes.json` | Path to the quote dataset |
| `Quotinator__Ssl` | `false` | Enable HTTPS on port 8080 |
| `Quotinator__SslCertFile` | _(empty)_ | Path to PEM certificate file |
| `Quotinator__SslKeyFile` | _(empty)_ | Path to PEM private key file |

> The Scalar API reference (`/scalar/v1`) and raw OpenAPI spec (`/openapi/v1.json`) are available in **all** environments including Production.

---

## Image naming

Images are published to GitHub Container Registry on every release tag as `ghcr.io/dutchjafo/quotinator`.

| Tag pushed | Docker tags produced |
|---|---|
| `v1.2.3` (stable) | `1.2.3`, `1.2`, `1`, `latest` |
| `v1.2.3-beta.1` (beta) | `1.2.3-beta.1` only |

See [`ci-cd.md`](ci-cd.md) for the full release process.

---

## Build notes

### Layer caching

The Dockerfile uses a two-step pattern to cache NuGet package downloads separately from source compilation:

```dockerfile
# Step 1 — restore only (cached unless a .csproj changes)
COPY src/**/*.csproj ...
RUN dotnet restore

# Step 2 — copy full source and publish
COPY src/ ...
RUN dotnet publish --configuration Release --output /app/publish
```

This keeps incremental rebuilds fast: if only source files change, Docker skips the package download layer and goes straight to compile.

### Why `--no-restore` is NOT used on `dotnet publish`

The standard Docker optimisation for .NET apps adds `--no-restore` to `dotnet publish` so that the second restore pass is skipped entirely. **This does not work for Blazor projects.**

The Blazor static web assets pipeline generates `_framework/blazor.web.js` during the build phase, and that generation requires `.razor` source files to be present. In the two-step Docker pattern, `dotnet restore` runs before any source is copied — so when restore runs, there are no `.razor` files. The resulting static web asset manifest is incomplete. With `--no-restore`, `dotnet publish` reuses that incomplete manifest and `_framework/blazor.web.js` is never written to the output. The result: Blazor interactivity (button clicks, component events) silently does not work at runtime — the browser fails to load `blazor.web.js` and the Blazor circuit never connects.

Without `--no-restore`, `dotnet publish` regenerates the manifest from scratch with the full source present. NuGet packages are still cached from the first restore layer, so there is no meaningful overhead.

This diverges from the standard `--no-restore` optimisation documented for generic ASP.NET Core apps, which do not have this static web assets dependency. All Blazor-specific deployment guides omit `--no-restore` for exactly this reason.

**References:**
- [Solved: How to Run Blazor on Docker Containers](https://sqlpey.com/dotnet/solved-how-to-run-blazor-on-docker-containers/) — Blazor-specific Dockerfile, no `--no-restore`
- [GitHub issue dotnet/aspnetcore #64366](https://github.com/dotnet/aspnetcore/issues/64366) — framework issue tracking Blazor static web asset problems in Docker
- [Docker deployment for ASP.NET Core API / Blazor apps — C# Corner](https://www.c-sharpcorner.com/article/docker-deployment-for-asp-net-core-api-blazor-apps/) — Blazor Dockerfile pattern, no `--no-restore`
- [Containerize a .NET app — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container?tabs=windows&pivots=dotnet-10-0) — generic .NET pattern (console app); does not apply to Blazor static web assets

### Multi-arch

Multi-arch publishing (`linux/amd64` + `linux/arm64`) is handled by `docker buildx` in the GitHub Actions release workflow.

### `ASPNETCORE_HTTP_PORTS`

The .NET base Docker image sets `ASPNETCORE_HTTP_PORTS=8080` by default. The Dockerfile clears this (`ENV ASPNETCORE_HTTP_PORTS=""`), giving Kestrel code in `Program.cs` sole control over port binding. This allows conditional HTTPS on port 8080 and always-HTTP on port 8099 (HA ingress) without conflicting environment variable overrides.
