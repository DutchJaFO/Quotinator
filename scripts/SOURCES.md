# Adding a new quote source

This document explains how to add a new external dataset to Quotinator's seed pipeline.

---

## How the pipeline works

`scripts/seed.csx` reads `scripts/sources.json`, downloads each source (or uses a local cache), normalises every entry to the [canonical quote schema](../CLAUDE.md#quote-schema-canonical), deduplicates across all sources, and writes `data/quotes.json`.

All deduplication is done on normalised quote text (lowercased, whitespace-collapsed). When the same quote appears in multiple sources, the first source listed in `sources.json` wins — put richer sources (those with year/type/character data) before simpler ones.

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

**Dry run (prints stats, does not write `data/quotes.json`):**

```bash
dotnet-script scripts/seed.csx --dry-run
```

**Use local cache (skips download, uses `scripts/cache/`):**

```bash
dotnet-script scripts/seed.csx --no-fetch
```

---

## Adding a new source

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

### 4. Add attribution to `SOURCES.md`

Add a row to the attribution table in [`SOURCES.md`](../SOURCES.md) at the repo root.

### 5. Run the seed script and review the output

```bash
dotnet-script scripts/seed.csx --dry-run   # verify counts
dotnet-script scripts/seed.csx             # write data/quotes.json
```

Spot-check a sample of new entries in `data/quotes.json` before committing.

---

## Custom adapters

If your source uses a format that doesn't fit `quoted-string` or `object-array`, add a new adapter in `seed.csx`:

1. Add a new `ParseXxx` static function that returns `List<(string Quote, string Source, string? Date, string Type)>`
2. Add a case to the `format switch` block in the main loop
3. Use a new `"format"` value in `sources.json`

Keep adapters pure functions — they receive a `JsonNode` and return a list. Network access and file I/O belong in the main loop.

---

## Manually curated entries

Entries that are not in any external dataset (specific book quotes, speeches, etc.) should be added **directly to `data/quotes.json`** rather than through the seed pipeline. Use a proper UUID v4 for the `id` field and follow the canonical schema. These entries will survive re-seeding because deduplication is quote-text-based, not ID-based.
