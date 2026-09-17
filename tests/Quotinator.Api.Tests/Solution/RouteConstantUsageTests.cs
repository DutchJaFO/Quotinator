using System.Text.Json;
using Quotinator.Constants.Routes;

namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// Endpoint files do not hardcode their route group prefix — `CLAUDE.md`'s string-centralisation
/// policy puts every route string in `Quotinator.Constants.Routes.ApiRoutes`, and a literal beside a
/// constant that already holds the same value is how the two drift apart.
/// <para>
/// Scoped to the files already brought into line, the same ratchet `.editorconfig`'s `IDE0008` list
/// uses: every file named here has been converted, so the assertion holds today, and a file is added
/// the moment it is converted. The remaining endpoint files still hold literals and are not this
/// list's business yet.
/// </para>
/// </summary>
[TestClass]
public class RouteConstantUsageTests
{
    private static readonly string EndpointsDir =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "Endpoints"));

    /// <summary>The endpoint files converted so far, by file name.</summary>
    private static readonly string[] ConvertedFiles =
    [
        "AdminEndpoints.cs",
        "BackupEndpoints.cs",
        "ImportEndpoints.cs",
        "ImportFileResourceEndpoints.cs",
        "ImportRuleEndpoints.cs",
        "NotificationEndpoints.cs",
    ];

    [TestMethod]
    public void ConvertedEndpointFiles_DoNotHardcodeTheirGroupPrefix()
    {
        Assert.IsTrue(Directory.Exists(EndpointsDir), $"Endpoints folder not found at: {EndpointsDir}");

        List<string> violations = [];

        foreach (string fileName in ConvertedFiles)
        {
            string path = Path.Combine(EndpointsDir, fileName);
            Assert.IsTrue(File.Exists(path), $"{fileName} no longer exists — this list needs updating.");

            if (File.ReadAllText(path).Contains("MapGroup(\"", StringComparison.Ordinal))
                violations.Add(fileName);
        }

        Assert.IsEmpty(violations,
            $"These files pass a literal route to MapGroup:\n{string.Join("\n", violations)}\n\n"
            + "Use the ApiRoutes constant for the group prefix, adding one if it does not exist yet.");
    }

    /// <summary>
    /// The positive control: each constant names a route the API actually serves. Without it, the
    /// assertion above is satisfied by a constant holding anything at all — and comparing a constant to
    /// a literal cannot do this job, since the compiler folds both sides (MSTEST0032).
    /// </summary>
    [TestMethod]
    public async Task EveryGroupPrefixConstant_NamesAPathTheApiServes()
    {
        await using QuotinatorWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        string spec = await client.GetStringAsync(ApiRoutes.OpenApiSpec, TestContext.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(spec);
        JsonElement paths = document.RootElement.GetProperty("paths");

        string[] prefixes =
        [
            ApiRoutes.Admin,
            ApiRoutes.AdminBackups,
            ApiRoutes.Import,
            ApiRoutes.ImportFileResources,
            ApiRoutes.ImportRules,
            ApiRoutes.Notifications,
        ];

        foreach (string prefix in prefixes)
        {
            bool served = paths.EnumerateObject().Any(path => path.Name.StartsWith(prefix, StringComparison.Ordinal));

            Assert.IsTrue(served,
                $"No published path starts with {prefix}, so that constant names a route the API does not serve.");
        }
    }

    /// <summary>MSTest's per-test context, used for the cancellation token the HTTP call takes.</summary>
    public TestContext TestContext { get; set; } = default!;
}
