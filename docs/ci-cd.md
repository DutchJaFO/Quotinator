# CI/CD

Quotinator uses GitHub Actions for continuous integration and delivery.

---

## Workflows

### CI — `.github/workflows/ci.yml`

Triggers on every push to `main` and every pull request targeting `main`.

Steps:
1. Restore NuGet packages
2. Build (Release configuration) — must produce 0 warnings and 0 errors
3. Run all MSTest tests — must all pass with 0 warnings and 0 errors
4. Publish smoke test — runs `dotnet publish` and asserts `data/quotes.json` is present in the output

> CI does **not** build the Docker image. A broken Dockerfile will only be caught by the release workflow. Always do a local `docker build` before tagging — see the [Pre-Push Checklist](../CLAUDE.md#pre-push-checklist).

**Branch protection (configure in GitHub → Settings → Branches):**
- Require this workflow to pass before merging into `main`
- Require pull request reviews (optional, solo project)

---

### Release — `.github/workflows/release.yml`

Triggers when a tag matching `v*.*.*` is pushed.

Steps:
1. Create a GitHub Release with auto-generated notes from commits
2. Build multi-arch Docker image (`linux/amd64` + `linux/arm64`)
3. Push to GitHub Container Registry (`ghcr.io`)

> The release workflow runs in parallel with CI — there is no guarantee CI has passed when the Docker image is pushed. A future improvement is to gate the release on CI passing via a `workflow_run` trigger.

**Image published:** `ghcr.io/dutchjafo/quotinator`

Tags produced vary by release channel:

| Tag pushed | Docker tags produced |
|---|---|
| `v1.2.3` (stable) | `1.2.3`, `1.2`, `1`, `latest` |
| `v1.2.3-beta.1` (pre-release) | `1.2.3-beta.1` only |

Pre-release tags (anything containing `-`) never receive `latest`, `major`, or `major.minor` aliases. This is the standard behaviour of `docker/metadata-action` with semver tagging.

**Prerequisites:**
- The `ghcr.io/dutchjafo/quotinator` package must be set to **Public** in GitHub package settings. The Home Assistant Supervisor pulls the image without credentials — a private package returns 401 and the add-on fails to install.

---

## Release process

### Before tagging

Every release requires these file updates — commit them all before creating the tag:

1. **`src/Quotinator.Api/Quotinator.Api.csproj`** — set `<Version>` to match the tag (without the `v` prefix):
   ```xml
   <Version>1.0.3</Version>
   ```
   This version is read at runtime and exposed via `GET /api/v1/version`.

2. **`addon/config.yaml`** — set `version` to match the tag (without the `v` prefix):
   ```yaml
   version: "1.0.3"
   ```
   The HA Supervisor appends this value as the Docker image tag when pulling from GHCR. If it does not match a published tag the install will fail.

3. **`CHANGELOG.md`** (root) — move items from `[Unreleased]` to a new versioned section. Every versioned section must include a `### Highlights` block written in plain, user-facing English — this is what the Blazor frontend displays. For purely internal releases write a short generic phrase (e.g. `Bug fix — no user-facing changes`). Include GitHub issue links in highlight items where applicable.

4. **`addon/CHANGELOG.md`** — add a matching entry for the HA add-on release. Use a flat bullet list with no `### Added/Fixed/Changed` subsections (HA convention).

Then run the [Pre-Push Checklist](../CLAUDE.md#pre-push-checklist) before tagging.

### Stable release

```bash
git tag v1.0.3
git push origin v1.0.3
```

Produces `1.0.3`, `1.0`, `1`, and `latest`.

### Pre-release

```bash
git tag v2.0.0-beta.1
git push origin v2.0.0-beta.1
```

Produces image tag `2.0.0-beta.1` only. Safe to share for testing without affecting `latest`.

### Subsequent pre-releases

Increment the suffix: `v2.0.0-beta.2`, `v2.0.0-beta.3`, etc.

---

## Secrets

No additional secrets are needed. The release workflow uses `GITHUB_TOKEN`, which GitHub provides automatically with `contents: write` and `packages: write` permissions.

---

## Versioning

Follow [Semantic Versioning](https://semver.org/):

| Increment | When |
|---|---|
| `MAJOR` | Breaking API changes |
| `MINOR` | New features, backwards-compatible |
| `PATCH` | Bug fixes, documentation, CI changes |
