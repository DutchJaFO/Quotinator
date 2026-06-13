# Data import strategies

This document describes the three ways quote data can reach a running Quotinator instance. The current implementation uses **Option A**. Options B and C are documented here as candidates for future versions.

---

## Option A — Committed file (current)

The seed script is run manually by the developer. The resulting `data/quotes.json` is committed to the repository and baked into the Docker image at build time.

```
developer runs seed.csx
        ↓
data/quotes.json committed to repo
        ↓
docker build copies file into image
        ↓
QuoteService loads it at startup
```

**Pros**
- Fully reproducible — the exact dataset is pinned in git history
- Auditable — every change to the dataset is a commit with a diff
- No network access needed at build or runtime
- Simple to reason about; works identically locally and in Docker

**Cons**
- Developer must remember to re-seed and commit when upstream sources are updated
- The image embeds the data; a data-only update requires a new image build and deploy

**When to use:** homelab, personal use, any case where you want to know exactly what data is in production.

---

## Option B — Build-time seeding

The seed script runs during `docker build`, so the image always contains freshly fetched data at the moment it was built. `data/quotes.json` is not committed to the repository.

```
docker build
        ↓
Dockerfile runs seed.csx (requires network access)
        ↓
data/quotes.json written into the image layer
        ↓
QuoteService loads it at startup
```

**Pros**
- Data is always current as of the build date without a manual commit step
- No large JSON file in the git repository

**Cons**
- Build requires outbound internet access — breaks air-gapped or offline builds
- Builds are not reproducible: the same Dockerfile may produce different images on different days
- Upstream source changes can break the build unexpectedly
- CI/CD pipelines must handle build failures due to network issues

**Implementation notes**
- The `dotnet-script` global tool would need to be installed in the build stage
- The Dockerfile would need a `RUN dotnet-script scripts/seed.csx` step before the publish stage
- Consider pinning source file content via a hash check if reproducibility matters

**When to use:** scheduled nightly builds where current data matters more than reproducibility.

---

## Option C — Startup seeding

The seed script (or equivalent logic) runs when the container starts for the first time. `data/quotes.json` is written to the mounted data volume, not into the image.

```
container starts
        ↓
startup check: does /app/data/quotes.json exist?
    no  → run seed, write to volume, continue
    yes → skip seed, continue
        ↓
QuoteService loads from volume
```

**Pros**
- Image stays small — no data embedded
- Data can be refreshed without rebuilding the image (delete the file, restart)
- Naturally handles the case where the data volume is empty on first run

**Cons**
- Container requires outbound internet access on first start
- First startup is slower
- Seed failures on startup prevent the service from starting
- Behaviour differs between first run (slow, network-dependent) and subsequent runs (fast)
- More complex orchestration: the health check must account for seed time

**Implementation notes**
- An entrypoint script (shell or C#) would check for the file and conditionally run the seed
- A `QUOTINATOR_SEED_ON_START=true` environment variable could control this behaviour
- The mounted volume path must be writable by the container user

**When to use:** deployments where the data volume is ephemeral or where you want the data to be refreshed on each new deployment without rebuilding the image.

---

## Comparison

| | A — Committed | B — Build-time | C — Startup |
|---|---|---|---|
| Data in git | Yes | No | No |
| Reproducible builds | Yes | No | No |
| Network at build | No | Yes | No |
| Network at runtime | No | No | Yes (first run) |
| Manual update step | Yes | No | No |
| Data auditable in git | Yes | No | No |
| Works air-gapped | Yes | No | No |
