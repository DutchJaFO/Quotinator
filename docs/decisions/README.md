# decisions/

This folder holds lightweight design notes that do not rise to the level of a full Architecture Decision Record.

Suitable content:
- Spike writeups — short explorations of a technology or approach, with conclusions
- Open questions — unresolved design questions being tracked until a decision is made
- Option comparisons — side-by-side notes when evaluating two or more approaches before committing

## Distinction from `architecture-decisions/`

| `decisions/` | `architecture-decisions/` |
|---|---|
| Informal, in-progress, or exploratory | Formal, numbered, permanent record |
| May be superseded and deleted | Never deleted — only marked Superseded or Deprecated |
| No fixed format | Follows the ADR format (Context / Decision / Consequences) |

When a question in `decisions/` is resolved, the conclusion either becomes an ADR in `architecture-decisions/` or is recorded in the relevant issue and this file is removed.
