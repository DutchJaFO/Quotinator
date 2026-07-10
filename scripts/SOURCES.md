# Adding a new quote source

This document explains how to add a new external dataset to Quotinator via the converter plugin
mechanism. It replaces the historical `scripts/seed.csx`/`scripts/sources.json` workflow — that
standalone script downloaded and normalised sources offline, but its download step operated on the
same raw upstream URLs the live auto-update mechanism (`Quotinator__AutoUpdateSources`) also needs,
so their responsibilities have merged: conversion is now a first-party, compiled plugin the running
app itself can invoke, live or locally, via `POST /api/v1/admin/sources/refresh`.

---

## How the pipeline works

A manifest entry (`data/sources/manifest.json` or a user's `imports/manifest.json`) that declares a
`downloadUrl`/`github` may also declare a `converter` — the `Name` of a compiled
`IQuoteSourceConverter` plugin — and, alongside it, `converterOptions`, a converter-specific
configuration object. When the auto-update mechanism (`ISourceCacheUpdater`) downloads that source, it
looks up the named converter and runs it before caching the result: **download to a temp file →
convert (if a converter is named, passing `converterOptions` through) → validate the result is
canonical-schema → move into the persistent cache**. A source with no `converter` is expected to
already be canonical-schema at its `downloadUrl` — validation still runs either way, so non-canonical
content is rejected and the existing cached/local copy is kept, rather than corrupting it.

IDs are deterministically derived from quote text and source name via SHA-256
(`Quotinator.Core.Import.QuoteIdentity.StableId`) so they are stable across every re-conversion —
this must never change once a source ships, or existing rows would be silently duplicated/orphaned.

`data/sources/manifest.json` lists files in the preferred import order and is not auto-created for
the bundled sources directory (it always exists — see `Quotinator__CreateMissingManifest` for the
user-imports directory's own auto-create behaviour).

---

## Three generic converters — almost never write a new plugin

A new source almost always fits one of three shapes, each covered by an already-shipped, fully
configurable converter. **Only a genuinely novel raw shape needs a new plugin project** — check these
three first.

Every canonical quote field a converter can target: `id`, `quote`, `originalLanguage`, `source`,
`date`, `character`, `author`, `type`, `genres`. A canonical field left unmapped in any of the three
converters below falls back to a per-source default (if configured), then to the field's own built-in
default (`originalLanguage` → `en`, `type` → `movie`, everything else → absent). `quote`/`source` are
always required — a row/entry missing either is silently skipped, never defaulted.

### `csv` — a flat CSV file

One record per row, optional header line. Zero configuration needed when the header's column names
already match the canonical field names (case-insensitive) — this is the common case. For a header
with different labels, or a file with no header at all, supply `converterOptions`:

```json
{
  "converter": "csv",
  "converterOptions": {
    "hasHeader": true,
    "columnMapping": { "quote": 1, "source": 2, "date": 3 },
    "defaults": { "originalLanguage": "en", "type": "movie" }
  }
}
```

`columnMapping` is 1-based (column 1 is the first column). `hasHeader` (default `true`) says whether
row 1 is a label row to skip or the first row of data — only meaningful alongside `columnMapping`,
since the zero-config path always needs a header to match column names against. `genres` stays
semicolon-delimited within one cell, e.g. `drama;sci-fi`. Cannot express conversations.

### `basic-json-array` — a flat JSON array of objects

Zero configuration needed when the raw JSON's own property names already match the canonical field
names. For raw property names that differ (e.g. the source title is under `movie` rather than
`source`), supply `converterOptions`:

```json
{
  "converter": "basic-json-array",
  "converterOptions": {
    "propertyMapping": { "source": "movie", "date": "year" },
    "defaults": { "originalLanguage": "en" }
  }
}
```

`propertyMapping` keys are canonical field names, values are the raw JSON property name to read
instead. `genres` may be a JSON array of strings or a single JSON string — no delimiter needed, unlike
CSV, since JSON already expresses arrays natively. Cannot express conversations. This is what
`NikhilNamal17/popular-movie-quotes` (raw `quote`/`movie`/`type`/`year` fields) is configured as — see
`data/sources/manifest.json`.

### `regex-array` — a JSON array of bare strings

For raw data shaped as a JSON array of strings rather than objects (no property names to match
against at all). Requires both `pattern` (a regex applied to each entry) and `groupMapping` (which
1-based capture group maps to which canonical field) — there is no zero-config path, since a bare
capture group has no inherent name:

```json
{
  "converter": "regex-array",
  "converterOptions": {
    "pattern": "^\"(.+?)\"\\s+(.+)$",
    "groupMapping": { "quote": 1, "source": 2 }
  }
}
```

An entry the pattern doesn't match is skipped, not an error (unless *zero* entries match at all).
Cannot express conversations. This is what `vilaboim/movie-quotes` (raw shape `"Quote text." Source
Title`) is configured as — see `data/sources/manifest.json`.

---

## Adding a new external source

### 1. Check the license

Only add sources with a permissive license (MIT, CC0, CC-BY, or equivalent). Note the SPDX identifier
— you will need it for `SOURCES.md`.

### 2. Configure an existing converter, or write a new one only if truly needed

For the vast majority of new sources: pick whichever of `csv`/`basic-json-array`/`regex-array` matches
the raw shape (see above) and configure it via a manifest entry's `converterOptions` — no new code.

Only write a new converter plugin when the raw shape is genuinely novel — not a flat CSV, not a flat
JSON object array, not a regex-extractable string array (e.g. nested JSON structures, nested
conversations, nested per-line metadata). Create a new project under `src/`, e.g.
`src/Quotinator.Converters.<Name>/`, referencing `Quotinator.Core` (for `SourceQuote`,
`MappedSourceQuoteBuilder`, `IndexedFieldMapping`/`NamedFieldMapping`/`QuoteFieldDefaults` if
reusable) and `Quotinator.Data` (for `IQuoteSourceConverter`). Implement:

```csharp
public sealed class MySourceConverter : IQuoteSourceConverter
{
    public string Name => "my-source";

    public async Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default)
    {
        // Parse the raw format at inputPath, build a List<SourceQuote> using
        // Quotinator.Core.Import.QuoteIdentity/YearParsing/QuoteTypeNormalisation/MappedSourceQuoteBuilder
        // where applicable, and write it (JsonSerializer.Serialize) to outputPath. Deserialize `options`
        // into a converter-specific options type immediately (e.g. `options?.Deserialize<MySourceConverterOptions>()`)
        // — the interface itself carries no knowledge of any converter's options shape. Throw
        // SourceConversionException on unrecoverable input (top-level parse failure, zero entries
        // converted) — never write a near-empty output file silently.
    }
}
```

See `src/Quotinator.Converters.Csv/`, `src/Quotinator.Converters.BasicJsonArray/`, and
`src/Quotinator.Converters.RegexArray/` for the three existing patterns — each pairs its converter with
its own `<Name>ConverterOptions` class (`CsvConverterOptions`, `BasicJsonArrayConverterOptions`,
`RegexArrayConverterOptions`), never a raw `Dictionary<string,string>`, so the manifest author and any
future reader can see exactly what options exist. Write a corresponding test project with, at minimum,
an **ID-stability regression test**: read a known quote/source pair from the source's already-committed
`data/sources/*.json` (if any prior version exists) or from your own fixture, and assert the converter
reproduces the exact same id.

If a plugin should only ever be selectable from the bundled sources manifest (never a user-writable
`imports/manifest.json`), override `IsInternalOnly => true` — enforced automatically, fails closed
exactly like an unregistered converter name would.

### 3. Register a new plugin (skip if reusing an existing converter)

Add the new converter instance to the dictionary built in `src/Quotinator.Api/Program.cs` (search for
`quoteSourceConverters`), and add a `ProjectReference` from `Quotinator.Api.csproj` to the new plugin
project. Add the project (plus its test project and both their `CVE/` folders) to `Quotinator.slnx`
and `docker/Dockerfile`'s restore-layer `COPY` block.

### 4. Add a manifest entry

```json
{
  "file": "author_repo-name.json",
  "name": "author/repo-name",
  "github": {
    "owner": "author",
    "repo": "repo-name",
    "path": "quotes.json",
    "branch": "main"
  },
  "converter": "basic-json-array",
  "converterOptions": {
    "propertyMapping": { "source": "movie" }
  }
}
```

Use `url`/`downloadUrl` directly instead of `github` for a non-GitHub-hosted source. Omit `converter`
(and `converterOptions`) entirely if the source already serves canonical-schema JSON at its
`downloadUrl`.

### 5. Generate the initial file

Run the app locally (`dotnet run --project src/Quotinator.Api`) with
`Quotinator__AutoUpdateSources=true`, then call:

```bash
curl -X POST -H "X-Api-Key: <your admin key>" "http://localhost:5000/api/v1/admin/sources/refresh?force=true"
```

This downloads and converts every source with a `downloadUrl`, including the new one, into
`{dataDir}/sources/download/` (or `imports/download/` for a user-imports entry). Copy the resulting
file into `data/sources/`, verify it against `schemas/source-flat.schema.json`, and commit it.

### 6. Add attribution to `SOURCES.md`

Add an entry to the **Quote datasets** section in [`SOURCES.md`](../SOURCES.md) at the repo root.
Include the file path, schema reference, repository URL, author, license, and contents.

---

## Manually curated entries

Entries that are not in any external dataset (specific book quotes, speeches, conversations, etc.)
belong in `data/sources/quotinator-curated.json`. This file uses the **extended format**
(`schemas/source-extended.schema.json`) which supports `quotes`, `stageDirections`, `soundCues`, and
`conversations` sections.

Do **not** write a converter plugin for curated entries — add them directly to
`quotinator-curated.json` with a manually assigned UUID and verify them before committing.
`SourceDataIntegrityTests` validates the file against the schema automatically on the next build.
