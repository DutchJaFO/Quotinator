using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Quotinator.Core.Tests.Data;

[TestClass]
public class SourceDataIntegrityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SourcesDir  = Path.Combine(RepoRoot, "data", "sources");
    private static readonly string SchemasDir  = Path.Combine(RepoRoot, "schemas");
    private static readonly string ManifestPath = Path.Combine(SourcesDir, "manifest.json");

    private static readonly JsonSchema ManifestSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "manifest.schema.json"));

    private static readonly JsonSchema FlatSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "source-flat.schema.json"));

    private static readonly JsonSchema ExtendedSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "source-extended.schema.json"));

    private static readonly EvaluationOptions StrictOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private static IEnumerable<string> SourceFiles =>
        Directory.Exists(SourcesDir)
            ? Directory.EnumerateFiles(SourcesDir, "*.json")
                       .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            : [];

    // ── JSON validity ─────────────────────────────────────────────────────────

    /// <summary>manifest.json exists and is valid JSON.</summary>
    [TestMethod]
    public void Manifest_IsValidJson()
    {
        Assert.IsTrue(File.Exists(ManifestPath), $"manifest.json not found at: {ManifestPath}");
        var ex = Record(() => JsonNode.Parse(File.ReadAllText(ManifestPath)));
        Assert.IsNull(ex, $"manifest.json is not valid JSON: {ex?.Message}");
    }

    /// <summary>Every *.json file in data/sources/ (including manifest) is valid JSON.</summary>
    [TestMethod]
    public void SourceFiles_AllAreValidJson()
    {
        Assert.IsTrue(Directory.Exists(SourcesDir), $"data/sources/ not found at: {SourcesDir}");

        foreach (var file in Directory.EnumerateFiles(SourcesDir, "*.json"))
        {
            var ex = Record(() => JsonNode.Parse(File.ReadAllText(file)));
            Assert.IsNull(ex, $"{Path.GetFileName(file)} is not valid JSON: {ex?.Message}");
        }
    }

    // ── Schema validation ─────────────────────────────────────────────────────

    /// <summary>manifest.json conforms to schemas/manifest.schema.json.</summary>
    [TestMethod]
    public void Manifest_ConformsToSchema()
    {
        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(ManifestPath));
        var result  = ManifestSchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("manifest.json", result));
    }

    /// <summary>Each source file conforms to its applicable schema (flat or extended).</summary>
    [TestMethod]
    public void SourceFiles_ConformToSchema()
    {
        foreach (var file in SourceFiles)
        {
            var name    = Path.GetFileName(file);
            var text    = File.ReadAllText(file);
            var element = JsonSerializer.Deserialize<JsonElement>(text);
            var schema  = element.ValueKind == JsonValueKind.Array ? FlatSchema : ExtendedSchema;
            var result  = schema.Evaluate(element, StrictOptions);
            Assert.IsTrue(result.IsValid, FormatErrors(name, result));
        }
    }

    /// <summary>A manifest file entry that sets both `github` and `url` violates the schema — the two source kinds are mutually exclusive.</summary>
    [TestMethod]
    public void Manifest_EntryWithBothGithubAndUrl_FailsSchemaValidation()
    {
        var manifest = new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"]   = "a.json",
                ["name"]   = "a",
                ["url"]    = "https://example.com/a",
                ["github"] = new JsonObject
                {
                    ["owner"] = "owner",
                    ["repo"]  = "repo",
                    ["path"]  = "a.json"
                }
            })
        };

        var element = JsonSerializer.Deserialize<JsonElement>(manifest.ToJsonString());
        var result  = ManifestSchema.Evaluate(element, StrictOptions);

        Assert.IsFalse(result.IsValid, "A manifest entry with both github and url should fail schema validation");
    }

    // ── Manifest structure ────────────────────────────────────────────────────

    /// <summary>Every file listed in manifest.json exists on disk.</summary>
    [TestMethod]
    public void Manifest_AllListedFilesExist()
    {
        var root  = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        var files = root["files"]!.AsArray();

        foreach (var entry in files)
        {
            var fileName = entry!["file"]!.GetValue<string>();
            var fullPath = Path.Combine(SourcesDir, fileName);
            Assert.IsTrue(File.Exists(fullPath), $"Manifest lists '{fileName}' but the file does not exist");
        }
    }

    /// <summary>Every *.json source file in data/sources/ (excluding manifest) is listed in the manifest.</summary>
    [TestMethod]
    public void SourceFiles_AllListedInManifest()
    {
        var root   = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        var listed = root["files"]!.AsArray()
                        .Select(e => e!["file"]!.GetValue<string>())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(SourcesDir, "*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            Assert.IsTrue(listed.Contains(name), $"'{name}' exists in data/sources/ but is not listed in manifest.json");
        }
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
