# Running Locally

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 (v17.12 or later recommended for .NET 10 support)
- Docker Desktop — required for the Docker run profile; see [`docker.md`](docker.md) for installation instructions

---

## Opening the solution

Open `Quotinator.slnx` in Visual Studio. Two projects will load:

| Project | Type | Purpose |
|---|---|---|
| `Quotinator.Api` | ASP.NET Core (API + Blazor Server) | The application — REST API and web UI combined |
| `Quotinator.Core` | Class library | Shared models and services |

---

## Running the application (`Quotinator.Api`)

1. In the Visual Studio toolbar, select **`Quotinator.Api`** as the startup project
2. Select the **`https`** profile from the run dropdown
3. Press **F5** (debug) or **Ctrl+F5** (without debugger)

Visual Studio will open the browser automatically at the Blazor home page (`https://localhost:7028`). From there, click the **OpenAPI UI (Scalar)** link to open the API explorer.

### Verifying the application is running

The fastest check is the health endpoint. Either:

- Use the Scalar UI: expand `GET /api/v1/health` → click **Send**
- Or navigate directly in your browser:

```
https://localhost:7028/api/v1/health
```

Expected response:

```json
{ "status": "healthy" }
```

The Blazor frontend is available at the root:

```
https://localhost:7028
```

### Available URLs

| URL | Purpose |
|---|---|
| `https://localhost:7028` | Blazor frontend (HTTPS) |
| `http://localhost:5043` | Blazor frontend (HTTP) |
| `https://localhost:7028/scalar/v1` | OpenAPI UI (development only) |
| `https://localhost:7028/openapi/v1.json` | Raw OpenAPI spec (development only) |
| `https://localhost:7028/api/v1/health` | Health check endpoint |

---

## Running via Docker (Visual Studio)

1. In the Visual Studio toolbar, select the **`Docker`** profile from the run dropdown
2. Press **F5**

VS will build the image and start the container. The browser opens at `http://localhost:8080`.

> **Note:** Docker Desktop must be running before launching with this profile. Scalar and the OpenAPI spec are not available in the container — the app runs in Production mode. See [`docker.md`](docker.md) for details.

---

## Running from the command line

```bash
# .NET (development mode — includes Scalar)
dotnet run --project src/Quotinator.Api --launch-profile https

# Docker (production mode)
docker compose -f docker/docker-compose.yml up --build
```
