# CI/CD

Quotinator uses GitHub Actions for continuous integration and delivery.

---

## Workflows

### CI — `.github/workflows/ci.yml`

Triggers on every push to `main` and every pull request targeting `main`.

Steps:
1. Restore NuGet packages
2. Build (Release configuration)
3. Run all MSTest tests

**Branch protection (configure in GitHub → Settings → Branches):**
- Require this workflow to pass before merging into `main`
- Require pull request reviews (optional, solo project)

---

### Release — `.github/workflows/release.yml`

Triggers when a tag matching `v*.*.*` is pushed.

Steps:
1. Build multi-arch Docker image (`linux/amd64` + `linux/arm64`)
2. Push to GitHub Container Registry (`ghcr.io`)

**Image published:** `ghcr.io/<owner>/quotinator`

Tags produced vary by release channel:

| Tag pushed | Docker tags produced |
|---|---|
| `v1.2.3` (stable) | `1.2.3`, `1.2`, `1`, `latest` |
| `v1.2.3-beta.1` (beta) | `1.2.3-beta.1` only |

Pre-release tags (anything containing `-`) never receive `latest`, `major`, or `major.minor` aliases. This is the standard behaviour of `docker/metadata-action` with semver tagging.

**Prerequisites before the first release:**
- The repo must be public, or the package visibility set to match in `ghcr.io` settings

---

## Release process

### Before tagging

Every release requires three file updates — commit these before creating the tag:

1. **`src/Quotinator.Api/Quotinator.Api.csproj`** — set `<Version>` to match the tag (without the `v` prefix):
   ```xml
   <Version>1.0.0-beta.1</Version>
   ```
   This version is read at runtime and exposed via `GET /api/v1/version` and the Blazor UI.
2. **`addon/config.yaml`** — set `version` to match the tag (without the `v` prefix):
   ```yaml
   version: "1.0.0-beta.1"
   ```
3. **`addon/CHANGELOG.md`** — move items from `[Unreleased]` to a new section with the version and today's date.

### Beta release

```bash
git tag v1.0.0-beta.1
git push origin v1.0.0-beta.1
```

Produces image tag `1.0.0-beta.1` only. Safe to share for testing without affecting `latest`.

### Stable release

```bash
git tag v1.0.0
git push origin v1.0.0
```

Produces `1.0.0`, `1.0`, `1`, and `latest`.

### Subsequent betas

Increment the beta number: `v1.0.0-beta.2`, `v1.0.0-beta.3`, etc.

---

## Secrets

No additional secrets are needed. The release workflow uses `GITHUB_TOKEN`, which GitHub provides automatically with `packages: write` permission.

---

## Versioning

Follow [Semantic Versioning](https://semver.org/):

| Increment | When |
|---|---|
| `MAJOR` | Breaking API changes |
| `MINOR` | New features, backwards-compatible |
| `PATCH` | Bug fixes |

Pre-v1 releases may use `v0.x.x`.
