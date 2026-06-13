# OpenAPI & Scalar

## Overview

Quotinator uses `Microsoft.AspNetCore.OpenApi` (built into .NET 10) to generate the OpenAPI spec, and `Scalar.AspNetCore` as the interactive API UI. Both are available in development only.

| Resource | URL |
|---|---|
| Scalar UI | `https://localhost:7028/scalar/v1` |
| Raw OpenAPI spec | `https://localhost:7028/openapi/v1.json` |

The browser opens automatically to the Scalar UI when the API is started via the `https` profile in Visual Studio.

---

## Package versions

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.x | OpenAPI spec generation (built-in to .NET 10) |
| `Scalar.AspNetCore` | 2.x | Interactive API UI (replaces Swagger UI / Swashbuckle) |

> Swashbuckle is not used. Microsoft replaced it with `Microsoft.AspNetCore.OpenApi` + Scalar for .NET 9+.

---

## Document-level metadata

API title, description, version, and contact are set via a document transformer in `Program.cs`:

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new() { ... };
        document.Tags = new HashSet<OpenApiTag> { ... };
        return Task.CompletedTask;
    });
});
```

Use `document.Tags` to add descriptions to tag groups. The `using Microsoft.OpenApi;` directive is required for `OpenApiTag`.

---

## Documenting endpoints

Minimal API endpoints use fluent methods — not XML comments:

```csharp
app.MapGet("/api/v1/quotes/random", ...)
   .WithName("GetRandomQuote")       // operationId in the spec
   .WithTags("Quotes")               // groups the endpoint in the UI
   .WithSummary("Random quote")      // short label shown in the endpoint list
   .WithDescription("Returns one random quote from the dataset.");
```

### Documenting parameters

Use `[System.ComponentModel.Description]` on handler parameters. ASP.NET Core 10 picks this up natively — the deprecated `WithOpenApi(op => ...)` overload is not used.

```csharp
private static IResult GetRandom(
    IQuoteService service,
    [Description("Number of quotes to return (1–100). Omit for a single random quote.")] string? n = null,
    [Description("ISO 639-1 language code (e.g. `nl`, `de`).")] string? lang = null)
{ ... }
```

- **`WithTags`** — must match a tag registered in `document.Tags` in the transformer, otherwise it renders without a description
- **`WithSummary`** — one line, shown collapsed in the UI
- **`WithDescription`** — full sentence(s), shown expanded; supports markdown

---

## Documenting models

XML documentation on model classes and properties is picked up automatically by the OpenAPI generator. `GenerateDocumentationFile` is enabled in both `Quotinator.Core` and `Quotinator.Api`.

```csharp
/// <summary>Represents a single quote entry in the Quotinator dataset.</summary>
public class Quote
{
    /// <summary>Unique identifier (UUID v4). Assigned at seed time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The verbatim quote text in the original language.</summary>
    [JsonPropertyName("quote")]
    public required string QuoteText { get; init; }

    // ... see Quote.cs for the full model
}
```

Use `[JsonPropertyName]` when the JSON key differs from the C# property name (e.g. `"quote"` → `QuoteText`). The OpenAPI generator respects these attributes.

---

## Adding a new tag group

1. Add the tag and its description to `document.Tags` in the transformer in `Program.cs`
2. Use `.WithTags("TagName")` on every endpoint that belongs to that group

```csharp
document.Tags = new HashSet<OpenApiTag>
{
    new() { Name = ApiTags.System,  Description = "Endpoints for monitoring and verifying the health of the API." },
    new() { Name = ApiTags.Quotes,  Description = "Endpoints for fetching and searching quotes." },
};
```

Tag name constants live in `src/Quotinator.Api/ApiTags.cs`. Always use the constants — never hardcode the string — so that `.WithTags(...)` calls and the document transformer stay in sync.
