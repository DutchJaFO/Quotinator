# Home Assistant Add-on

Quotinator ships as a Home Assistant add-on. Users add the GitHub repository as a custom add-on source and install it directly from the HA add-on store.

---

## Repository structure

HA discovers add-ons by searching recursively for `config.yaml` files. No separate repository is needed — the main Quotinator repo serves as the add-on repository.

| File | Purpose |
|---|---|
| `repository.yaml` | Repository metadata (name, URL, maintainer) |
| `addon/config.yaml` | Add-on manifest — name, version, arch, ports, ingress |
| `addon/DOCS.md` | User-facing documentation shown in the HA UI |
| `addon/icon.png` | Add-on icon (128×128 PNG, not yet created) |
| `addon/logo.png` | Add-on logo (250×100 PNG, not yet created) |
| `addon/README.md` | Store listing — short description shown in the add-on store |
| `addon/DOCS.md` | Full documentation shown on the add-on detail page |
| `addon/CHANGELOG.md` | Version history (Keep a Changelog format) |

---

## Adding to Home Assistant

Users install the add-on by adding the repository URL in HA:

1. **Settings → Add-ons → Add-on Store → ⋮ → Repositories**
2. Add `https://github.com/DutchJaFO/Quotinator`
3. Find **Quotinator** and click **Install**

---

## Ports

The container listens on two ports:

| Port | Purpose |
|---|---|
| `8080` | Direct access — user can override the host-side port in HA if `8080` is taken |
| `8099` | Home Assistant ingress — dedicated port for the HA sidebar integration |

Both ports are configured via `ASPNETCORE_HTTP_PORTS=8080;8099` in the container.

---

## Ingress

Ingress embeds the Blazor UI directly into the HA sidebar. The HA supervisor handles URL routing and authentication automatically — no port forwarding or reverse proxy configuration is needed.

The ingress port (`8099`) is separate from the direct access port (`8080`) so both can be used simultaneously.

---

## Data persistence

Quotes are stored in `/data/quotes.json` inside the add-on data directory, which HA maps to a persistent volume. The data survives add-on updates and restarts.

---

## Releasing a new version

The `version` field in `addon/config.yaml` must match the Docker image tag published to `ghcr.io`. When releasing:

1. Update `version` in `addon/config.yaml` to match the new tag (e.g. `1.0.0`)
2. Tag and push: `git tag v1.0.0 && git push origin v1.0.0`
3. The release workflow builds and pushes `ghcr.io/dutchjafo/quotinator:1.0.0`
4. HA users will see the update available in the add-on store

---

## Pending

- `addon/icon.png` — 128×128 PNG, currently a placeholder (GitHub avatar); replace with final artwork before publishing to the HA store
- `addon/logo.png` — 250×100 PNG, currently a placeholder (GitHub avatar); replace with final artwork before publishing to the HA store
