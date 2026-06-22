# Logging Standards

This file is the authoritative reference for how Quotinator structures its log output.
Apply these standards whenever you touch a file that emits log lines — boyscout style.

---

## Startup framing banners

Two banners wrap the entire startup sequence.

**Opening banner** — printed immediately via `Console.WriteLine`, before any startup work begins.
Name and status only; no data collected yet:

```
######################
#  Quotinator starting  #
######################
```

**Closing banner** — printed after all startup work is complete (DB init, config read, addresses bound).
This is the single place a reader confirms the server is up and correctly configured:

```
######################
#  Quotinator ready     #
######################
Version:        1.x.x
...
######################
```

Everything between the two banners is diagnostic or informational.

**Why `Console.WriteLine` for banners?**
The systemd / HA supervisor log formatter collapses `logger.LogInformation` multi-line strings
to a single line. `Console.WriteLine` bypasses the formatter and preserves newlines.
Individual single-line structured messages must still use `logger.LogInformation`.

---

## Structured log prefix

Every log line must carry a `[Subsystem - Phase]` prefix so readers and `grep` can isolate
a subsystem without knowing message text.

Format: `[Subsystem - Phase] message text`

### Defined prefixes

| Prefix | When to use |
|---|---|
| `[Database - Init]` | Schema creation, migration, filename migration |
| `[Database - Seed]` | Quote import, genre seed, duplicate handling |
| `[Database - Stats]` | Final quote / source / character / people counts |
| `[Database - Backup]` | Backup operations |
| `[Config]` | Config / env-var diagnostic lines |
| `[SSL]` | TLS cert load, Kestrel HTTPS bind |
| `[DataProtection]` | Key persistence setup |
| `[RateLimit]` | Rate limiter configuration |
| `[Server]` | Kestrel bind addresses, application lifetime events |

New subsystems must register a prefix in this table before their log lines land in a PR.

### Example output between the banners

```
[Database - Init] initializing
[Database - Init] schema: none found — creating fresh
[Database - Init] schema v1 created
[Database - Seed] importing 410 quotes from vilaboim_movie-quotes.json (Bundled)...
[Database - Seed] seeding complete — 780 unique quotes from 792 total (12 duplicates)
[Database - Stats] 780 quotes  3 sources  42 characters  12 people
[Server] listening on http://0.0.0.0:8080
```

---

## Security rule

Never log a secret value. API keys and any future credentials must appear as `set` or `not set` only.
This applies everywhere — banners, structured log lines, and diagnostic dumps.

---

## Boyscout rule

When you edit a file that emits log lines without the `[Subsystem - Phase]` prefix, add the prefix
in the same commit. Do not defer it to a separate cleanup PR.
