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

### External tools

| Tool | License | Purpose | Source |
|---|---|---|---|
| `dotnet-script` | MIT | Run `.csx` C# scripts without a project file (seed script) | [NuGet](https://www.nuget.org/packages/dotnet-script) · [GitHub](https://github.com/dotnet-script/dotnet-script) |
| DB Browser for SQLite | GPL v3 / LGPL v2.1 | GUI for inspecting and querying the `quotes.db` file directly | [sqlitebrowser.org](https://sqlitebrowser.org) · [GitHub](https://github.com/sqlitebrowser/sqlitebrowser) |

---

## Quote datasets

The `data/quotes.json` dataset is seeded from the following open-source projects and extended with manually curated entries. All seed sources are MIT licensed. Data has been normalised to the Quotinator quote schema and deduplicated.

---

## vilaboim/movie-quotes

- **Repository:** https://github.com/vilaboim/movie-quotes
- **Author:** Lucas Vilaboim
- **License:** MIT © Lucas Vilaboim
- **Contents:** AFI Top 100 movie quotes
- **npm:** https://www.npmjs.com/package/movie-quotes

---

## NikhilNamal17/popular-movie-quotes

- **Repository:** https://github.com/NikhilNamal17/popular-movie-quotes
- **Author:** Nikhil N Namal
- **License:** MIT
- **Contents:** Popular movie, TV, and anime quotes (~732 entries)
- **npm:** https://www.npmjs.com/package/popular-movie-quotes

---

## Manually curated entries

Entries not sourced from the above datasets are added manually. These include:

- Quotes from books — attributed to the author and work
- Quotes from famous people — attributed to the person and occasion where known

All manually added entries must be accurately attributed. Do not add quotes of uncertain origin or attribution.

---

## Note on copyright

Quotes from films, television, and books are copyrighted by their respective studios, publishers, or estates. Use of individual quotes in this project constitutes fair use for personal, non-commercial purposes. Quotinator is not intended for commercial use or bulk redistribution of quote content.

Quotes attributed to real people are in the public domain where the speaker is deceased and no separate copyright claim applies. Exercise judgment when adding quotes from living persons.
