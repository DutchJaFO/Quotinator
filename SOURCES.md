# Sources & Attribution

Attribution for all external resources used in this project: quote datasets and NuGet packages.

---

## NuGet packages

### Production

| Package | Version | License | Purpose | Source |
|---|---|---|---|---|
| `Dapper` | 2.1.79 | Apache 2.0 | Lightweight SQL micro-ORM for parameterised queries | [NuGet](https://www.nuget.org/packages/Dapper) · [GitHub](https://github.com/DapperLib/Dapper) |
| `Dapper.Contrib` | 2.0.78 | Apache 2.0 | CRUD extension methods for Dapper (`Insert`, `Update`, `Get`) | [NuGet](https://www.nuget.org/packages/Dapper.Contrib) · [GitHub](https://github.com/DapperLib/Dapper) |
| `Microsoft.Data.Sqlite` | 10.0.9 | MIT | SQLite ADO.NET provider | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite) · [GitHub](https://github.com/dotnet/efcore) |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 | MIT | `ILogger<T>` interface — used in Core without pulling in the full logging stack | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions) · [GitHub](https://github.com/dotnet/runtime) |
| `Microsoft.AspNetCore.OpenApi` | 10.0.9 | MIT | OpenAPI spec generation (built-in to .NET 10) | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi) · [GitHub](https://github.com/dotnet/aspnetcore) |
| `Scalar.AspNetCore` | 2.16.3 | MIT | Interactive OpenAPI UI (replaces Swagger UI) | [NuGet](https://www.nuget.org/packages/Scalar.AspNetCore) · [GitHub](https://github.com/scalar/scalar) |
| `Toolbelt.Blazor.I18nText` | 14.1.1 | MIT | Blazor UI localisation (en, en-GB, de, nl) | [NuGet](https://www.nuget.org/packages/Toolbelt.Blazor.I18nText) · [GitHub](https://github.com/jsakamoto/Toolbelt.Blazor.I18nText) |

### Development / tooling

| Package | Version | License | Purpose | Source |
|---|---|---|---|---|
| `MSTest` | 4.2.3 | MIT | Unit and integration test framework | [NuGet](https://www.nuget.org/packages/MSTest) · [GitHub](https://github.com/microsoft/testfx) |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.9 | MIT | `WebApplicationFactory` for endpoint integration tests | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing) · [GitHub](https://github.com/dotnet/aspnetcore) |
| `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` | 1.23.0 | MIT | Visual Studio Docker tooling integration | [NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.Azure.Containers.Tools.Targets) |
| `JsonSchema.Net` | 9.2.2 | MIT | JSON Schema Draft 2020-12 validation — used in `SourceDataIntegrityTests` to validate source files and the manifest against their schemas | [NuGet](https://www.nuget.org/packages/JsonSchema.Net) · [GitHub](https://github.com/gregsdennis/json-everything) |

### External tools

| Tool | License | Purpose | Source |
|---|---|---|---|
| `dotnet-script` | MIT | Run `.csx` C# scripts without a project file (seed script) | [NuGet](https://www.nuget.org/packages/dotnet-script) · [GitHub](https://github.com/dotnet-script/dotnet-script) |
| DB Browser for SQLite | GPL v3 / LGPL v2.1 | GUI for inspecting and querying the `quotes.db` file directly | [sqlitebrowser.org](https://sqlitebrowser.org) · [GitHub](https://github.com/sqlitebrowser/sqlitebrowser) |

---

## JSON Schemas

Three schemas in `schemas/` define and validate the import file formats. They are used by `SourceDataIntegrityTests` at build time and by editors (VS Code, JetBrains) for IntelliSense when editing source files.

| Schema file | Validates |
|---|---|
| `schemas/manifest.schema.json` | `data/sources/manifest.json` and `data/imports/manifest.json` — ordered list of source files |
| `schemas/source-flat.schema.json` | External source files in `data/sources/` — flat JSON array of canonical quote objects |
| `schemas/source-extended.schema.json` | `data/sources/quotinator-curated.json` and user import files — object with `quotes`, `stageDirections`, `soundCues`, `conversations` |
| `schemas/seed-sources.schema.json` | `scripts/sources.json` — seed pipeline configuration (external source URLs, formats, field mappings) |
| `schemas/upstream-quoted-string.schema.json` | `scripts/cache/vilaboim_*.json` — raw upstream format used by [vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes) |
| `schemas/upstream-object-array.schema.json` | `scripts/cache/NikhilNamal17_*.json` — raw upstream format used by [NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes) |

The schemas implement [JSON Schema Draft 2020-12](https://json-schema.org/draft/2020-12/schema).

The two upstream schemas (`upstream-quoted-string`, `upstream-object-array`) were derived from the respective packages' own test suites — neither repo ships a schema file. They are candidates for upstream PRs.

---

## Quote datasets

The `data/sources/` directory contains one JSON file per dataset, normalised to the Quotinator canonical schema. All seed sources are MIT licensed.

---

## vilaboim/movie-quotes

- **File:** `data/sources/vilaboim_movie-quotes.json`
- **Schema:** `schemas/source-flat.schema.json`
- **Repository:** https://github.com/vilaboim/movie-quotes
- **Author:** Lucas Vilaboim
- **License:** MIT © Lucas Vilaboim
- **Contents:** AFI Top 100 movie quotes (~99 entries)
- **npm:** https://www.npmjs.com/package/movie-quotes

---

## NikhilNamal17/popular-movie-quotes

- **File:** `data/sources/NikhilNamal17_popular-movie-quotes.json`
- **Schema:** `schemas/source-flat.schema.json`
- **Repository:** https://github.com/NikhilNamal17/popular-movie-quotes
- **Author:** Nikhil N Namal
- **License:** MIT
- **Contents:** Popular movie, TV, and anime quotes (~732 entries)
- **npm:** https://www.npmjs.com/package/popular-movie-quotes

---

## quotinator/curated

- **File:** `data/sources/quotinator-curated.json`
- **Schema:** `schemas/source-extended.schema.json`
- **Contents:** Manually verified entries not sourced from the datasets above. Includes quotes with enriched metadata (character names, genres, conversation groupings), books, and famous people.

All entries must be accurately attributed and verified before adding. Do not add quotes of uncertain origin or attribution.

---

## Note on copyright

Quotes from films, television, and books are copyrighted by their respective studios, publishers, or estates. Use of individual quotes in this project constitutes fair use for personal, non-commercial purposes. Quotinator is not intended for commercial use or bulk redistribution of quote content.

Quotes attributed to real people are in the public domain where the speaker is deceased and no separate copyright claim applies. Exercise judgment when adding quotes from living persons.
