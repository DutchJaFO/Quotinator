using Quotinator.Data.Database;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    private static readonly string DatabaseStatsSummaryRazor =
        Path.Combine(RepoRoot, "src", "Quotinator.Api", "Components", "Controls", "DatabaseStatsSummary.razor");

    /// <summary>
    /// Every <see cref="IDatabaseInitializer"/> <c>*Count</c> property is rendered by the Blazor
    /// statistics component.
    /// </summary>
    /// <remarks>
    /// This is the surface #375's own fix missed. The count was wired into the log line, the ready
    /// banner, <c>/version</c> and both admin responses, and the completeness guard added alongside
    /// them covers <c>/version</c> only — so the Blazor page kept rendering nine hardcoded rows and
    /// the gap was found by a person looking at the page, which is exactly what a guard is for.
    /// The miss came from deriving the surface list with a <c>--include="*.cs"</c> grep, which cannot
    /// match a <c>.razor</c> file; CLAUDE.md's own Razor caveat warns that these files fall outside
    /// checks that appear to cover the codebase.
    /// </remarks>
    [TestMethod]
    public void DatabaseStatsSummary_RendersEveryEntityTypeCount()
    {
        string markup = File.ReadAllText(DatabaseStatsSummaryRazor);

        string[] missing = [.. typeof(IDatabaseInitializer).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.EndsWith("Count", StringComparison.Ordinal))
            .Where(n => !markup.Contains($"DatabaseInitializer.{n}", StringComparison.Ordinal))
            .Order()];

        Assert.IsEmpty(missing, $"DatabaseStatsSummary.razor renders no row for: {string.Join(", ", missing)}");
    }
}
