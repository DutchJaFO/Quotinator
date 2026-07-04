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
`IQuoteSourceConverter` plugin. When the auto-update mechanism (`ISourceCacheUpdater`) downloads that
source, it looks up the named converter and runs it before caching the result: **download to a temp
file → convert (if a converter is named) → validate the result is canonical-schema → move into the
persistent cache**. A source with no `converter` is expected to already be canonical-schema at its
`downloadUrl` — validation still runs either way, so non-canonical content is rejected and the
existing cached/local copy is kept, rather than corrupting it.

IDs are deterministically derived from quote text and source name via SHA-256
(`Quotinator.Core.Import.QuoteIdentity.StableId`) so they are stable across every re-conversion —
this must never change once a source ships, or existing rows would be silently duplicated/orphaned.

`data/sources/manifest.json` lists files in the preferred import order and is not auto-created for
the bundled sources directory (it always exists — see `Quotinator__CreateMissingManifest` for the
user-imports directory's own auto-create behaviour).

---

## Adding a new external source

### 1. Check the license

Only add sources with a permissive license (MIT, CC0, CC-BY, or equivalent). Note the SPDX identifier
— you will need it for `SOURCES.md`.

### 2. Write a converter plugin

Create a new project under `src/`, e.g. `src/Quotinator.Converters.<Name>/`, referencing
`Quotinator.Core` (for `SourceQuote`) and `Quotinator.Data` (for `IQuoteSourceConverter`). Implement:

```csharp
public sealed class MySourceConverter : IQuoteSourceConverter
{
    public string Name => "my-source";

    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        // Parse the raw format at inputPath, build a List<SourceQuote> using
        // Quotinator.Core.Import.QuoteIdentity/YearParsing/QuoteTypeNormalisation where applicable,
        // and write it (JsonSerializer.Serialize) to outputPath. Throw SourceConversionException on
        // unrecoverable input (top-level parse failure, zero entries converted) — never write a
        // near-empty output file silently.
    }
}
```

See `src/Quotinator.Converters.Vilaboim/` (a bare-string-array format, regex-parsed) and
`src/Quotinator.Converters.NikhilNamal17/` (a JSON object-array format, field-mapped) for the two
existing patterns. Write a corresponding test project with, at minimum, an **ID-stability regression
test**: read a known quote/source pair from the source's already-committed `data/sources/*.json` (if
any prior version exists) or from your own fixture, and assert the converter reproduces the exact
same id.

### 3. Register the plugin

Add the new converter instance to the dictionary built in `src/Quotinator.Api/Program.cs` (search for
`quoteSourceConverters`), and add a `ProjectReference` from `Quotinator.Api.csproj` to the new plugin
project. Add the project to `Quotinator.slnx` and `docker/Dockerfile`'s restore-layer `COPY` block.

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
  "converter": "my-source"
}
```

Use `url`/`downloadUrl` directly instead of `github` for a non-GitHub-hosted source. Omit `converter`
entirely if the source already serves canonical-schema JSON at its `downloadUrl`.

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
