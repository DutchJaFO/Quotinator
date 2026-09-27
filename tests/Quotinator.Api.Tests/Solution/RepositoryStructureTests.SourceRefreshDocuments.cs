using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// Every document #424 names says, where it mentions the source refresh, that it is off by default
    /// (#424, requirements 4 to 8). Asserted over the documents' own text, so a document still describing the
    /// old default fails the build rather than waiting on someone to reread it.
    /// <para>
    /// A paragraph must name the setting and say it is off by default together: "off by default" alone
    /// elsewhere in a file, about another setting, proves nothing about this one.
    /// </para>
    /// </summary>
    [TestMethod]
    public void SourceRefreshDocuments_SayTheRefreshIsOffByDefault()
    {
        string[] documents =
        [
            "docs/api-endpoints.md",
            "docs/knowledgebase/transport-connection-cancelled-during-a-request.md",
            "docs/automated-testing/README.md",
            "docs/automated-testing/import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md",
            "CLAUDE.md",
            "scripts/SOURCES.md",
        ];

        string[] silent = [.. documents.Where(document => !ParagraphSays(Path.Combine(RepoRoot, document)))];

        Assert.IsEmpty(silent, "no paragraph names the source refresh and says it is off by default in: " + string.Join(", ", silent));
    }

    private static bool ParagraphSays(string path) =>
        ParagraphBreak().Split(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal))
            .Any(paragraph => paragraph.Contains("AutoUpdateSources", StringComparison.Ordinal)
                           && paragraph.Contains("off by default", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();
}
