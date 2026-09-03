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

    private static readonly JsonSchema ConflictResolutionRuleSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "conflict-resolution-rules.schema.json"));

    private static readonly JsonSchema SourceAliasRuleSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "source-alias-rules.schema.json"));

    private static readonly EvaluationOptions StrictOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    /// <summary>Every *.json file listed in a manifest entry's own `ruleFile` property (#181) — a different shape from a source file, validated separately.</summary>
    private static HashSet<string> RuleFilesListedInManifest()
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        return root["files"]!.AsArray()
            .Select(e => e!["ruleFile"]?.GetValue<string>())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    /// <summary>Every *.json file listed in a manifest entry's own `sourceAliasFile` property (#181) — a different shape from a source file, validated separately.</summary>
    private static HashSet<string> SourceAliasFilesListedInManifest()
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        return root["files"]!.AsArray()
            .Select(e => e!["sourceAliasFile"]?.GetValue<string>())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    private static IEnumerable<string> SourceFiles
    {
        get
        {
            if (!Directory.Exists(SourcesDir)) return [];
            HashSet<string> ruleFiles = RuleFilesListedInManifest();
            HashSet<string> aliasFiles = SourceAliasFilesListedInManifest();
            return Directory.EnumerateFiles(SourcesDir, "*.json")
                .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                .Where(f => !ruleFiles.Contains(Path.GetFileName(f)))
                .Where(f => !aliasFiles.Contains(Path.GetFileName(f)));
        }
    }

    private static IEnumerable<string> RuleFiles
    {
        get
        {
            if (!Directory.Exists(SourcesDir)) return [];
            HashSet<string> ruleFiles = RuleFilesListedInManifest();
            return Directory.EnumerateFiles(SourcesDir, "*.json")
                .Where(f => ruleFiles.Contains(Path.GetFileName(f)));
        }
    }

    private static IEnumerable<string> SourceAliasFiles
    {
        get
        {
            if (!Directory.Exists(SourcesDir)) return [];
            HashSet<string> aliasFiles = SourceAliasFilesListedInManifest();
            return Directory.EnumerateFiles(SourcesDir, "*.json")
                .Where(f => aliasFiles.Contains(Path.GetFileName(f)));
        }
    }

    // ── JSON validity ─────────────────────────────────────────────────────────

    /// <summary>manifest.json exists and is valid JSON.</summary>
    [TestMethod]
    public void Manifest_IsValidJson()
    {
        Assert.IsTrue(File.Exists(ManifestPath), $"manifest.json not found at: {ManifestPath}");
        Exception? ex = Record(() => JsonNode.Parse(File.ReadAllText(ManifestPath)));
        Assert.IsNull(ex, $"manifest.json is not valid JSON: {ex?.Message}");
    }

    /// <summary>Every *.json file in data/sources/ (including manifest) is valid JSON.</summary>
    [TestMethod]
    public void SourceFiles_AllAreValidJson()
    {
        Assert.IsTrue(Directory.Exists(SourcesDir), $"data/sources/ not found at: {SourcesDir}");

        foreach (string file in Directory.EnumerateFiles(SourcesDir, "*.json"))
        {
            Exception? ex = Record(() => JsonNode.Parse(File.ReadAllText(file)));
            Assert.IsNull(ex, $"{Path.GetFileName(file)} is not valid JSON: {ex?.Message}");
        }
    }

    // ── Schema validation ─────────────────────────────────────────────────────

    /// <summary>manifest.json conforms to schemas/manifest.schema.json.</summary>
    [TestMethod]
    public void Manifest_ConformsToSchema()
    {
        JsonElement element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(ManifestPath));
        EvaluationResults result  = ManifestSchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("manifest.json", result));
    }

    /// <summary>Each source file conforms to its applicable schema (flat or extended).</summary>
    [TestMethod]
    public void SourceFiles_ConformToSchema()
    {
        foreach (string file in SourceFiles)
        {
            string name    = Path.GetFileName(file);
            string text    = File.ReadAllText(file);
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(text);
            JsonSchema schema  = element.ValueKind == JsonValueKind.Array ? FlatSchema : ExtendedSchema;
            EvaluationResults result  = schema.Evaluate(element, StrictOptions);
            Assert.IsTrue(result.IsValid, FormatErrors(name, result));
        }
    }

    /// <summary>
    /// A quote listing the same genre twice must fail schema validation. The database enforces
    /// uniqueness per quote (<c>UNIQUE (QuoteId, Genre)</c> on <c>Quotinator_QuoteGenre</c>), but that
    /// only catches it once the row is written — the source JSON files a curator hand-edits have no such
    /// guard, and a literal duplicate there (a copy-paste slip, most plausibly) is exactly the kind of
    /// authoring mistake this project's own priority ("quotes must be real and accurately attributed")
    /// exists to catch before it ships, not silently absorb via <c>INSERT OR IGNORE</c> at write time.
    /// Found live (2026-09-04): neither <c>source-flat.schema.json</c> nor
    /// <c>source-extended.schema.json</c> declared <c>uniqueItems</c> on <c>genres</c>, so a duplicate
    /// passed validation silently.
    /// </summary>
    [TestMethod]
    public void FlatSchema_QuoteWithDuplicateGenre_FailsValidation()
    {
        JsonArray quotes = new(new JsonObject
        {
            ["id"]               = "9a02c1dc-8a7f-1f4e-9b90-3229f4c2a361",
            ["quote"]            = "A quote for schema testing only.",
            ["originalLanguage"] = "en",
            ["source"]           = "Schema Test Fixture",
            ["type"]             = "movie",
            ["genres"]           = new JsonArray("action", "action"),
            ["translations"]     = new JsonObject(),
        });

        JsonElement element = JsonSerializer.Deserialize<JsonElement>(quotes.ToJsonString());
        EvaluationResults result = FlatSchema.Evaluate(element, StrictOptions);

        Assert.IsFalse(result.IsValid, "A quote listing the same genre twice must fail schema validation.");
    }

    /// <summary>The control for the row above: the same fixture with its duplicate removed must pass, proving the failure above is about the duplicate specifically.</summary>
    [TestMethod]
    public void FlatSchema_QuoteWithDistinctGenres_PassesValidation()
    {
        JsonArray quotes = new(new JsonObject
        {
            ["id"]               = "9a02c1dc-8a7f-1f4e-9b90-3229f4c2a361",
            ["quote"]            = "A quote for schema testing only.",
            ["originalLanguage"] = "en",
            ["source"]           = "Schema Test Fixture",
            ["type"]             = "movie",
            ["genres"]           = new JsonArray("action", "sci-fi"),
            ["translations"]     = new JsonObject(),
        });

        JsonElement element = JsonSerializer.Deserialize<JsonElement>(quotes.ToJsonString());
        EvaluationResults result = FlatSchema.Evaluate(element, StrictOptions);

        Assert.IsTrue(result.IsValid, FormatErrors("synthetic quote with distinct genres", result));
    }

    /// <summary>
    /// <c>uniqueItems</c> is exact-match, not case-insensitive on its own — <c>["action", "Action"]</c>
    /// is two distinct JSON string values as far as that keyword is concerned, so it alone would not
    /// catch a case-variant duplicate the way <see cref="FlatSchema_QuoteWithDuplicateGenre_FailsValidation"/>
    /// proves it catches an exact one. Verified this is not a live gap: the pre-existing <c>enum</c>
    /// constraint on <c>genres</c>' items is itself case-sensitive and closed to the canonical
    /// all-lowercase vocabulary, so <c>"Action"</c> is already rejected on its own before uniqueness is
    /// ever considered — confirmed by asserting the specific failure reason below, not just that
    /// validation failed for some reason. If the vocabulary ever stopped being a closed, lowercase enum
    /// (a free-text tag field, say), this reasoning would no longer hold and <c>uniqueItems</c> alone
    /// would need to become case-insensitive too.
    /// </summary>
    [TestMethod]
    public void FlatSchema_QuoteWithCaseVariantGenre_FailsValidation_ViaEnumNotUniqueItems()
    {
        JsonArray quotes = new(new JsonObject
        {
            ["id"]               = "9a02c1dc-8a7f-1f4e-9b90-3229f4c2a361",
            ["quote"]            = "A quote for schema testing only.",
            ["originalLanguage"] = "en",
            ["source"]           = "Schema Test Fixture",
            ["type"]             = "movie",
            ["genres"]           = new JsonArray("action", "Action"),
            ["translations"]     = new JsonObject(),
        });

        JsonElement element = JsonSerializer.Deserialize<JsonElement>(quotes.ToJsonString());
        EvaluationResults result = FlatSchema.Evaluate(element, StrictOptions);

        Assert.IsFalse(result.IsValid, "A case-variant genre must still fail validation.");
        string errors = FormatErrors("case-variant genre", result);
        Assert.Contains("enum", errors, "The rejection must come from the enum constraint (wrong casing is not a valid tag at all), not from uniqueItems — confirming the closed lowercase vocabulary is what actually prevents a case-insensitive duplicate, not the uniqueness check.");
    }

    /// <summary>Each per-source conflict-resolution rule file (#181) conforms to schemas/conflict-resolution-rules.schema.json.</summary>
    [TestMethod]
    public void RuleFiles_ConformToSchema()
    {
        foreach (string file in RuleFiles)
        {
            string name    = Path.GetFileName(file);
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file));
            EvaluationResults result  = ConflictResolutionRuleSchema.Evaluate(element, StrictOptions);
            Assert.IsTrue(result.IsValid, FormatErrors(name, result));
        }
    }

    /// <summary>Each per-source title-alias file (#181) conforms to schemas/source-alias-rules.schema.json.</summary>
    [TestMethod]
    public void SourceAliasFiles_ConformToSchema()
    {
        foreach (string file in SourceAliasFiles)
        {
            string name    = Path.GetFileName(file);
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file));
            EvaluationResults result  = SourceAliasRuleSchema.Evaluate(element, StrictOptions);
            Assert.IsTrue(result.IsValid, FormatErrors(name, result));
        }
    }

    /// <summary>A manifest file entry that sets both `github` and `url` violates the schema — the two source kinds are mutually exclusive.</summary>
    [TestMethod]
    public void Manifest_EntryWithBothGithubAndUrl_FailsSchemaValidation()
    {
        JsonObject manifest = new()
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

        JsonElement element = JsonSerializer.Deserialize<JsonElement>(manifest.ToJsonString());
        EvaluationResults result  = ManifestSchema.Evaluate(element, StrictOptions);

        Assert.IsFalse(result.IsValid, "A manifest entry with both github and url should fail schema validation");
    }

    /// <summary>A manifest file entry may declare `converterOptions` (an opaque, converter-specific object) alongside `converter`.</summary>
    [TestMethod]
    public void Manifest_EntryWithConverterOptions_PassesSchemaValidation()
    {
        JsonObject manifest = new()
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"]             = "a.json",
                ["name"]             = "a",
                ["url"]              = "https://example.com/a",
                ["converter"]        = "basic-json-array",
                ["converterOptions"] = new JsonObject
                {
                    ["propertyMapping"] = new JsonObject { ["source"] = "movie", ["date"] = "year" }
                }
            })
        };

        JsonElement element = JsonSerializer.Deserialize<JsonElement>(manifest.ToJsonString());
        EvaluationResults result  = ManifestSchema.Evaluate(element, StrictOptions);

        Assert.IsTrue(result.IsValid, FormatErrors("synthetic manifest with converterOptions", result));
    }

    // ── Manifest structure ────────────────────────────────────────────────────

    /// <summary>Every file listed in manifest.json exists on disk.</summary>
    [TestMethod]
    public void Manifest_AllListedFilesExist()
    {
        JsonNode root  = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        JsonArray files = root["files"]!.AsArray();

        foreach (JsonNode? entry in files)
        {
            string fileName = entry!["file"]!.GetValue<string>();
            string fullPath = Path.Combine(SourcesDir, fileName);
            Assert.IsTrue(File.Exists(fullPath), $"Manifest lists '{fileName}' but the file does not exist");
        }
    }

    /// <summary>Every *.json source file in data/sources/ (excluding manifest) is listed in the manifest, either as a source file's own `file` entry or as some entry's `ruleFile`/`sourceAliasFile` (#181).</summary>
    [TestMethod]
    public void SourceFiles_AllListedInManifest()
    {
        JsonNode root   = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        HashSet<string> listed = root["files"]!.AsArray()
                        .Select(e => e!["file"]!.GetValue<string>())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        listed.UnionWith(RuleFilesListedInManifest());
        listed.UnionWith(SourceAliasFilesListedInManifest());

        foreach (string file in Directory.EnumerateFiles(SourcesDir, "*.json"))
        {
            string name = Path.GetFileName(file);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            Assert.Contains(name, listed, $"'{name}' exists in data/sources/ but is not listed in manifest.json (as either 'file', 'ruleFile', or 'sourceAliasFile')");
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
        IEnumerable<string> errors = (result.Details ?? [])
            .Where(d => !d.IsValid && d.Errors != null)
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
        return $"Schema validation failed for {fileName}:\n{string.Join('\n', errors)}";
    }
}
