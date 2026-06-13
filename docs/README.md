# Quotinator — Documentation

## Structure

| File | Contents |
|---|---|
| `ci-cd.md` | CI/CD workflows, release process, versioning |
| `docker.md` | Docker image, local Docker run, environment variables, build notes |
| `home-assistant.md` | HA add-on setup, ingress, ports, releasing |
| `localisation.md` | UI string localisation and quote-level translation |
| `openapi.md` | OpenAPI and Scalar setup, how to document endpoints, tags, and models |
| `running-locally.md` | How to start and verify the application in Visual Studio |
| `testing-policy.md` | Test framework, project structure, what to test |

| Folder | Contents |
|---|---|
| `adr/` | Architecture Decision Records — numbered, one file per decision |
| `decisions/` | Lightweight design notes, spike writeups, open questions |

## ADR format

Files in `adr/` follow the naming convention `NNN-short-title.md` (e.g. `001-flat-file-json-for-v1.md`).

Each ADR contains:
- **Status** — Proposed / Accepted / Superseded / Deprecated
- **Context** — why the decision needed to be made
- **Decision** — what was decided
- **Consequences** — trade-offs and follow-on work
