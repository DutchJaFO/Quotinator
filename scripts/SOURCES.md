# Adding a new quote source

This document explains how to add a new external dataset to Quotinator's seed pipeline.

---

## How the pipeline works

`scripts/seed.csx` reads `scripts/sources.json`, downloads each source (or uses a local cache), normalises every entry to the [canonical quote schema](../CLAUDE.md#quote-schema-canonical), and writes one JSON file per source to `data/sources/`. IDs are deterministically derived from quote text and source name via SHA-256 so they are stable across re-seeds.

There is no merge step and no deduplication. Each source file is independent. Conflict resolution (same quote appearing in multiple sources) is handled at import time by the startup seeder, and eventually by a review UI in the import milestone.

`data/sources/manifest.json` lists files in the preferred import order. The manifest is created automatically if it does not exist; if it already exists, `seed.csx` does not overwrite it.

---

## Running the seed script

**Prerequisites:** `dotnet-script` global tool

```bash
dotnet tool install -g dotnet-script
```

**Seed (downloads fresh copies of all sources):**

```bash
dotnet-script scripts/seed.csx
```

**Dry run (prints stats, does not write any files):**

```bash
dotnet-script scripts/seed.csx -- --dry-run
```

**Use local cache (skips download, uses `scripts/cache/`):**

```bash
dotnet-script scripts/seed.csx -- --no-fetch
```

---

## Adding a new external source

### 1. Check the license

Only add sources with a permissive license (MIT, CC0, CC-BY, or equivalent). Note the SPDX identifier — you will need it for `sources.json`.

### 2. Identify the data format

The seed script supports two built-in formats. Check which one matches your source:

| Format | Description | Example |
|---|---|---|
| `quoted-string` | JSON array of `"\"Quote text.\" Source Title"` strings | vilaboim/movie-quotes |
| `object-array` | JSON array of objects with named fields | NikhilNamal17/popular-movie-quotes |

If your source uses a different layout, add a new adapter function in `seed.csx` (see **Custom adapters** below).

### 3. Add an entry to `sources.json`

**For `quoted-string` sources:**

```json
{
  "name": "author/repo-name",
  "url": "https://raw.githubusercontent.com/author/repo/main/quotes.json",
  "format": "quoted-string",
  "defaultType": "movie",
  "license": "MIT",
  "attribution": "https://github.com/author/repo-name"
}
```

**For `object-array` sources:**

```json
{
  "name": "author/repo-name",
  "url": "https://raw.githubusercontent.com/author/repo/main/data.json",
  "format": "object-array",
  "defaultType": "movie",
  "fieldMap": {
    "quote":  "quote",
    "source": "movie",
    "type":   "type",
    "year":   "year"
  },
  "license": "MIT",
  "attribution": "https://github.com/author/repo-name"
}
```

`fieldMap` maps Quotinator's internal field names to the keys used in the source JSON:

| Quotinator field | Description |
|---|---|
| `quote` | The verbatim quote text |
| `source` | Film/show/book title or speech occasion |
| `type` | `movie`, `tv`, `anime`, `book`, or `person` — unknown values fall back to `defaultType` |
| `year` | Release or publication year (integer); values outside 1900–2100 are discarded |

### 4. Run the seed script

```bash
dotnet-script scripts/seed.csx -- --dry-run   # verify counts
dotnet-script scripts/seed.csx                # write data/sources/<name>.json
```

The output file is named after the `name` field with `/` replaced by `_` (e.g. `author/repo-name` → `author_repo-name.json`).

### 5. Add attribution to `SOURCES.md`

Add an entry to the **Quote datasets** section in [`SOURCES.md`](../SOURCES.md) at the repo root. Include the file path, schema reference, repository URL, author, license, and contents.

### 6. Update `manifest.json`

Add the new file to `data/sources/manifest.json` in the desired import position. External sources should come after `quotinator-curated.json`.

---

## Custom adapters

If your source uses a format that doesn't fit `quoted-string` or `object-array`, add a new adapter in `seed.csx`:

1. Add a new `ParseXxx` static function that returns `List<(string Quote, string Source, string? Date, string Type)>`
2. Add a case to the `format switch` block in the main loop
3. Use a new `"format"` value in `sources.json`

Keep adapters pure functions — they receive a `JsonNode` and return a list. Network access and file I/O belong in the main loop.

---

## Manually curated entries

Entries that are not in any external dataset (specific book quotes, speeches, conversations, etc.) belong in `data/sources/quotinator-curated.json`. This file uses the **extended format** (`schemas/source-extended.schema.json`) which supports `quotes`, `stageDirections`, `soundCues`, and `conversations` sections.

Do **not** run `seed.csx` for curated entries — add them directly to `quotinator-curated.json` with a manually assigned UUID and verify them before committing. The `SourceDataIntegrityTests` will validate the file against the schema automatically on the next build.
