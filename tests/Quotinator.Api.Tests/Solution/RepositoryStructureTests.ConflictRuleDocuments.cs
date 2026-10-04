using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// #420 / ADR 023: every document that describes the conflict-rule file format says that an entity is
    /// named at most once per file. Asserted over the documents' own text, following
    /// <see cref="SourceRefreshDocuments_SayTheRefreshIsOffByDefault"/>'s precedent, so a document still
    /// describing the old shape fails the build rather than waiting on someone to reread it.
    /// <para>
    /// A paragraph has to name the rule file (or its <c>rules</c> array) and state the one-per-entity
    /// contract together: "one entry per entity" asserted anywhere in a file, about something else,
    /// proves nothing about this.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ConflictRuleDocuments_StateTheOneEntryPerEntityContract()
    {
        string[] documents =
        [
            "docs/api-endpoints.md",
            "schemas/conflict-resolution-rules.schema.json",
            "docs/architecture-decisions/023-one-conflict-rule-entry-per-entity.md",
        ];

        string[] silent = [.. documents.Where(document => !ParagraphStatesOneEntryPerEntity(Path.Combine(RepoRoot, document)))];

        Assert.IsEmpty(silent,
            "no paragraph names the conflict-rule file and states the one-entry-per-entity contract (ADR 023) in: "
            + string.Join(", ", silent));
    }

    private static bool ParagraphStatesOneEntryPerEntity(string path) =>
        ConflictRuleParagraphBreak().Split(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal))
            .Any(paragraph => (paragraph.Contains("conflict-rule", StringComparison.OrdinalIgnoreCase)
                            || paragraph.Contains("conflict-resolution", StringComparison.OrdinalIgnoreCase)
                            || paragraph.Contains("\"rules\"", StringComparison.Ordinal)
                            || paragraph.Contains("rules array", StringComparison.OrdinalIgnoreCase))
                           && (paragraph.Contains("at most once", StringComparison.OrdinalIgnoreCase)
                            || paragraph.Contains("one entry per entity", StringComparison.OrdinalIgnoreCase)));

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ConflictRuleParagraphBreak();
}
