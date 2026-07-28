using System.Text.Json;
using Json.Schema;

namespace Quotinator.Core.Tests.Data;

/// <summary>
/// Validates the committed raw-upstream-format fixtures (<c>tests/Quotinator.Api.Tests/Solution/Fixtures/</c>)
/// used by <c>RepositoryStructureTests.ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline</c>
/// and by the individual converter plugin test projects. These fixtures replaced
/// <c>scripts/cache/</c> (retired alongside <c>scripts/seed.csx</c>/<c>scripts/sources.json</c>) as the
/// committed source of truth for each source's raw upstream shape.
/// </summary>
[TestClass]
public class RawSourceFixtureIntegrityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string FixturesDir =
        Path.Combine(RepoRoot, "tests", "Quotinator.Api.Tests", "Solution", "Fixtures");

    private static readonly string SchemasDir = Path.Combine(RepoRoot, "schemas");

    private static readonly JsonSchema QuotedStringSchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "upstream-quoted-string.schema.json"));

    private static readonly JsonSchema ObjectArraySchema =
        JsonSchema.FromFile(Path.Combine(SchemasDir, "upstream-object-array.schema.json"));

    private static readonly EvaluationOptions StrictOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    /// <summary>The vilaboim raw fixture conforms to the quoted-string upstream schema.</summary>
    [TestMethod]
    public void VilaboimRawFixture_ConformsToSchema()
    {
        var path    = Path.Combine(FixturesDir, "vilaboim_raw.json");
        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var result  = QuotedStringSchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("vilaboim_raw.json", result));
    }

    /// <summary>The NikhilNamal17 raw fixture conforms to the object-array upstream schema.</summary>
    [TestMethod]
    public void NikhilNamal17RawFixture_ConformsToSchema()
    {
        var path    = Path.Combine(FixturesDir, "nikhilnamal17_raw.json");
        var element = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var result  = ObjectArraySchema.Evaluate(element, StrictOptions);
        Assert.IsTrue(result.IsValid, FormatErrors("nikhilnamal17_raw.json", result));
    }

    private static string FormatErrors(string fileName, EvaluationResults result)
    {
        var errors = (result.Details ?? [])
            .Where(d => !d.IsValid && d.Errors != null)
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
        return $"Schema validation failed for {fileName}:\n{string.Join('\n', errors)}";
    }
}
