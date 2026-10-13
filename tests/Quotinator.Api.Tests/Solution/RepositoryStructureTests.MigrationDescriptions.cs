using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// #371: both documents a migration author reads say that every migration carries a description.
    /// Asserted over the documents' own text, so a rule that was never written down fails the build
    /// rather than waiting on someone to notice its absence.
    /// <para>
    /// A paragraph must name the property and state the requirement together: "required" elsewhere in
    /// the same file, about something else, proves nothing about this rule. Same shape, and same
    /// reason, as <c>SourceRefreshDocuments_SayTheRefreshIsOffByDefault</c>.
    /// </para>
    /// </summary>
    [TestMethod]
    public void MigrationDocuments_SayEveryMigrationCarriesADescription()
    {
        string[] documents =
        [
            "CLAUDE.md",
            "docs/database-conventions.md",
        ];

        string[] silent = [.. documents.Where(document => LacksDescriptionParagraph(Path.Combine(RepoRoot, document)))];

        Assert.IsEmpty(silent,
            "no paragraph states that every migration carries a required description in: " + string.Join(", ", silent));
    }

    private static bool LacksDescriptionParagraph(string path) =>
        !DocumentParagraphBreak().Split(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal))
            .Any(paragraph => paragraph.Contains("Description", StringComparison.Ordinal)
                           && paragraph.Contains("migration", StringComparison.OrdinalIgnoreCase)
                           && paragraph.Contains("required", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex DocumentParagraphBreak();
}
