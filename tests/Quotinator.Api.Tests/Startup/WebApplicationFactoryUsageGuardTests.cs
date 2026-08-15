namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Stops #313 from silently regressing. The readiness guard only works if tests actually construct
/// <see cref="QuotinatorWebApplicationFactory"/> — a new test file written with a bare
/// <c>new WebApplicationFactory&lt;Program&gt;()</c> would reintroduce the race for itself, and would do
/// so invisibly, since the symptom is usually a *passing* test asserting against the startup wait page.
/// <para>
/// Source-scanning rather than reflection, mirroring <c>SqlSourceScanTests</c>' own precedent in
/// <c>Quotinator.Core.Tests</c>: the thing being guarded is how the code is *written*, which reflection
/// over compiled output cannot see.
/// </para>
/// </summary>
[TestClass]
public class WebApplicationFactoryUsageGuardTests
{
    private static readonly string TestProjectRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    /// <summary>
    /// Two files legitimately contain the scanned text without constructing anything:
    /// the guarded factory's own definition (it names the base type because it derives from it), and
    /// this file (its own failure message quotes the pattern it looks for — found live, the guard
    /// flagged itself on first run).
    /// </summary>
    private static readonly string[] ExemptFiles =
    [
        "QuotinatorWebApplicationFactory.cs",
        "WebApplicationFactoryUsageGuardTests.cs",
    ];

    [TestMethod]
    public void NoTestConstructsTheUnguardedWebApplicationFactory()
    {
        Assert.IsTrue(Directory.Exists(TestProjectRoot), $"Test project root not found at: {TestProjectRoot}");

        var violations = Directory
            .GetFiles(TestProjectRoot, "*.cs", SearchOption.AllDirectories)
            // bin/obj hold generated copies of these same sources; scanning them would double-report.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !ExemptFiles.Contains(Path.GetFileName(f)))
            .Where(f => File.ReadAllText(f).Contains("new WebApplicationFactory<Program>()", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(TestProjectRoot, f))
            .ToList();

        Assert.IsEmpty(violations,
            $"These test files construct WebApplicationFactory<Program> directly:\n{string.Join("\n", violations)}\n\n" +
            "Use QuotinatorWebApplicationFactory instead — the bare factory returns a client before startup " +
            "completes, so requests can be answered by the startup wait page rather than the endpoint under " +
            "test (#313). Measured: the unguarded factory saw startup incomplete on 5 of 5 runs.");
    }
}
