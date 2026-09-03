namespace Quotinator.Core.Tests.Data;

/// <summary>
/// #375: asserts over `docs/workflow/source-verification.md`'s own text — this is the "verified,
/// not read" alternative to a documentation-confirmation row a human is trusted to remember to check,
/// per the pattern #307 established (see its own plan doc). The procedure's tiers govern which work a
/// title or date refers to; they said nothing about verifying a quote's text, speaker, or episode until
/// #375 added it, and this test is what stops that gap silently reopening.
/// </summary>
[TestClass]
public class SourceVerificationDocTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string DocPath = Path.Combine(RepoRoot, "docs", "workflow", "source-verification.md");

    private static string DocText => File.ReadAllText(DocPath);

    [TestMethod]
    public void NamesImdbAsTheSourceForAQuotesTextSpeakerAndEpisode()
    {
        string text = DocText;
        Assert.Contains("Verifying a quote's text, speaker, and episode", text);
        Assert.Contains("IMDb is already Tier 1", text);
    }

    [TestMethod]
    public void StatesAbsenceFromImdbIsNotEvidenceOfAnUnverifiedQuote()
    {
        Assert.Contains("A quote absent from IMDb is not an unverified quote", DocText);
    }

    [TestMethod]
    public void StatesPartialAttributionIsExpectedAndNamesTheFallback()
    {
        string text = DocText;
        Assert.Contains("Attribution is expected to be partial", text);
        Assert.Contains("attach the", text, "The fallback — attach to the nearest identifiable Source — must be stated, not left implicit.");
    }
}
