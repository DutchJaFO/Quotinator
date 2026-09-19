using Quotinator.Core.Database;
using Quotinator.Core.Enums;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// #409: <see cref="QuoteFieldMerge"/> is the one entry point for comparing a quote's fields, and it
/// applies <see cref="QuoteFieldMerge.CaseSensitiveContentFields"/>. Before it, staging applied the set and
/// the listing, the decide and the import response did not.
/// </summary>
[TestClass]
public class QuoteFieldMergeTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static SourceQuoteDto Quote(string quoteText = "Here's looking at you, kid.", string source = "Casablanca") => new()
    {
        Id               = "40911111-1111-4111-8111-111111111111",
        QuoteText        = quoteText,
        OriginalLanguage = "en",
        Source           = source,
        Character        = "Rick Blaine",
        Type             = QuoteType.Movie,
        Genres           = [],
    };

    [TestMethod]
    public void ResolveWithDecisions_QuoteTextDiffersOnlyByCase_IsUnresolved()
    {
        FieldMergeResult result = QuoteFieldMerge.ResolveWithDecisions(
            QuoteFieldMerge.ToFieldMap(Quote()),
            QuoteFieldMerge.ToFieldMap(Quote(quoteText: "HERE'S LOOKING AT YOU, KID.")),
            new Dictionary<string, FieldMergeDecision>());

        Assert.AreSequenceEqual(["quoteText"], result.UnresolvedFields);
    }

    /// <summary>
    /// Control: <c>source</c> is outside the set (see <see cref="QuoteFieldMerge.CaseSensitiveContentFields"/>
    /// for why), so a difference in letter case alone still needs no decision.
    /// </summary>
    [TestMethod]
    public void ResolveWithDecisions_SourceDiffersOnlyByCase_NeedsNoDecision()
    {
        FieldMergeResult result = QuoteFieldMerge.ResolveWithDecisions(
            QuoteFieldMerge.ToFieldMap(Quote()),
            QuoteFieldMerge.ToFieldMap(Quote(source: "CASABLANCA")),
            new Dictionary<string, FieldMergeDecision>());

        Assert.IsEmpty(result.UnresolvedFields);
        Assert.AreEqual("Casablanca", result.MergedFields["source"], "Equal values keep the stored side");
    }

    /// <summary>
    /// <c>CLAUDE.md</c> states which quote fields compare case-sensitively and that every quote comparison
    /// goes through <see cref="QuoteFieldMerge"/> — asserted rather than left for someone to read, as
    /// <c>SourceVerificationDocTests</c> does. Its earlier text said every field compared
    /// case-insensitively, which #374 had already made untrue.
    /// </summary>
    [TestMethod]
    public void CaseSensitiveContentFields_AreDocumentedInClaudeMd()
    {
        string claudeMd = File.ReadAllText(Path.Combine(RepoRoot, "CLAUDE.md"));

        Assert.Contains("compared case-sensitively", claudeMd);
        Assert.Contains("goes through `QuoteFieldMerge`", claudeMd);
        foreach (string field in QuoteFieldMerge.CaseSensitiveContentFields)
            Assert.Contains($"`{field}`", claudeMd);
    }
}
