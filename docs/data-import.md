# Data import architecture

This document describes how quote data enters a running Quotinator instance — from the bundled source files baked into the image, through startup seeding, to planned user imports.

---

## Overview

Quotinator separates **source files** (the canonical data on disk) from the **SQLite database** (the live data store the API queries). At startup the seeder reads source files and populates the database. After the first seed, the database is the source of truth; re-seeding is an explicit admin action.

```
data/sources/          ←  committed to git, baked into the Docker image
      ↓  startup seeder
quotinatordata.db      ←  live data store; persisted on Docker volume at /data (never /app/data — see docs/docker.md)
      ↓  REST API
consumers (MagicMirror², HA automations, etc.)
```

---

## Source file layout

```
data/sources/
├── manifest.json                        ← import order
├── quotinator-curated.json              ← manually verified entries (extended format)
├── vilaboim_movie-quotes.json           ← external seed source (flat format)
└── NikhilNamal17_popular-movie-quotes.json
```

### Two formats

| Format | Schema | Used by |
|---|---|---|
| **Flat** — top-level JSON array | `schemas/source-flat.schema.json` | External seed sources produced by a converter plugin (see `scripts/SOURCES.md`) |
| **Extended** — top-level object with `quotes`, `stageDirections`, `soundCues`, `conversations` | `schemas/source-extended.schema.json` | Curated file and future user imports |

Both formats share the same canonical quote object schema. The extended format is a superset.

### Manifest

`data/sources/manifest.json` lists files in the preferred import order. Files not listed are appended alphabetically after those that are.

```json
{
  "files": [
    { "file": "quotinator-curated.json",                 "name": "quotinator/curated" },
    { "file": "vilaboim_movie-quotes.json",              "name": "vilaboim/movie-quotes" },
    { "file": "NikhilNamal17_popular-movie-quotes.json", "name": "NikhilNamal17/popular-movie-quotes" }
  ]
}
```

The manifest is committed to git and baked into the Docker image alongside the source files.

---

## Stable IDs

Quote IDs are deterministically derived from the quote text and source name using SHA-256. The same quote from the same source always receives the same UUID, so re-seeding does not create duplicates.

> **Note on UUID byte layout:** `new Guid(byte[])` interprets bytes 6–7 little-endian, placing the version nibble at position 3 of the third UUID group rather than position 1. The IDs are therefore not strict RFC 4122 v4 UUIDs, but they are stable and unique within the dataset. The JSON schemas use a relaxed UUID pattern that accepts any well-formed UUID string.

---

## Conflict resolution

When the same stable ID appears in multiple source files (same text from the same source), the seeder skips the duplicate — first listed in the manifest wins.

When the same quote text appears with different sources or IDs (e.g. the same line appears in both the vilaboim and NikhilNamal17 datasets under different films), both entries are imported. Deduplication across sources is not applied automatically; a review UI in the import milestone will surface these for manual resolution.

---

## Re-seeding

Two admin endpoints trigger re-seeding:

| Endpoint | Behaviour |
|---|---|
| `POST /api/v1/admin/database/reseed` | Clears quote data, reimports all source files. Schema history (migrations) is preserved. |
| `POST /api/v1/admin/database/reset` | Full reset: clears data **and** schema history, reapplies migrations, reimports. Use to recover from a corrupted migration state. |

Both endpoints read from the `data/sources/` directory baked into the image. They do not re-download external sources.

---

## User imports (planned — import milestone)

Future versions will allow users to supply their own source files. The planned design:

- A configurable `imports/` directory (separate from the bundled `sources/` directory) where users drop JSON files in flat or extended format
- The same manifest mechanism and stable-ID logic applies
- An import UI (Blazor management page) for reviewing conflicts and approving new entries before they reach the database
- A "reset to defaults" option that clears user-imported data and re-seeds from bundled sources only

See the [Data Import & Sources milestone](https://github.com/DutchJaFO/Quotinator/milestone/5) for the full scope.

---

## Adding data

### External datasets

Write an `IQuoteSourceConverter` plugin to add a new external source. See [`scripts/SOURCES.md`](../scripts/SOURCES.md) for the full workflow.

### Curated entries

Add manually to `data/sources/quotinator-curated.json` using the extended format. Assign a UUID manually, verify attribution, and run `dotnet test` to confirm schema validity before committing.
