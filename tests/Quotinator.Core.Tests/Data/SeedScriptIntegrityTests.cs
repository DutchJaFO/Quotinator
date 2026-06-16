using System.Text.Json;
using Json.Schema;

namespace Quotinator.Core.Tests.Data;

[TestClass]
public class SeedScriptIntegrityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string ScriptsDir = Path.Combine(RepoRoot, "scripts");
    private static readonly string CacheDir   = Path.Combine(ScriptsDir, "cache");
    private static readonly string SchemasDir = Path.Combine(RepoRoot, "schemas");

    private static readonly JsonSchema SeedSourcesSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "seed-sources.schema.json"));

    private static readonly JsonSchema QuotedStringSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "upstream-quoted-string.schema.json"));

    private static readonly JsonSchema ObjectArraySchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "upstream-object-array.schema.json"));

    private static readonly EvaluationOptions StrictOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    // ── sources.json ──────────────────────────────────────────────────────────

    /// <summary>scripts/sources.json exists and is valid JSON.</summary>
    [TestMethod]
    public void SeedSources_IsValidJson()
    {
        var path = Path.Combine(ScriptsDir, "sources.json");
        Assert.IsTrue(File.Exists(path), $"sources.json not found at: {path}");
        var ex = Record(() => JsonDocument.Parse(File.ReadAllText(path)));
        Assert.IsNull(ex, $"sources.json is not valid JSON: {ex?.Message}");
    }

    /// <summary>scripts/sources.json conforms to schemas/seed-sources.schema.json.</summary>
    [TestMethod]
    public void SeedSources_ConformsToSchema()
    {
        var path    = Path.Combine(ScriptsDir, "sources.json");
        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var result  = SeedSourcesSchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("sources.json", result));
    }

    // ── cache files ───────────────────────────────────────────────────────────

    /// <summary>Every cache file in scripts/cache/ is valid JSON.</summary>
    [TestMethod]
    public void CacheFiles_AllAreValidJson()
    {
        if (!Directory.Exists(CacheDir)) return;

        foreach (var file in Directory.EnumerateFiles(CacheDir, "*.json"))
        {
            var ex = Record(() => JsonDocument.Parse(File.ReadAllText(file)));
            Assert.IsNull(ex, $"{Path.GetFileName(file)} is not valid JSON: {ex?.Message}");
        }
    }

    /// <summary>Cached vilaboim source conforms to the quoted-string upstream schema.</summary>
    [TestMethod]
    public void CacheFile_Vilaboim_ConformsToSchema()
    {
        var path = Path.Combine(CacheDir, "vilaboim_movie-quotes.json");
        if (!File.Exists(path))
        {
            Assert.Inconclusive("scripts/cache/vilaboim_movie-quotes.json not present — run seed.csx --no-fetch to populate.");
            return;
        }

        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var result  = QuotedStringSchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("vilaboim_movie-quotes.json", result));
    }

    /// <summary>Cached NikhilNamal17 source conforms to the object-array upstream schema.</summary>
    [TestMethod]
    public void CacheFile_NikhilNamal17_ConformsToSchema()
    {
        var path = Path.Combine(CacheDir, "NikhilNamal17_popular-movie-quotes.json");
        if (!File.Exists(path))
        {
            Assert.Inconclusive("scripts/cache/NikhilNamal17_popular-movie-quotes.json not present — run seed.csx --no-fetch to populate.");
            return;
        }

        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var result  = ObjectArraySchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("NikhilNamal17_popular-movie-quotes.json", result));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Exception? Record(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static string FormatErrors(string fileName, EvaluationResults result)
    {
        var errors = (result.Details ?? [])
            .Where(d => !d.IsValid && d.Errors != null)
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
        return $"Schema validation failed for {fileName}:\n{string.Join('\n', errors)}";
    }
}
