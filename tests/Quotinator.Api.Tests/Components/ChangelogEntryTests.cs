using Quotinator.Api.Components.Controls;
using Quotinator.Changelog.Models;

namespace Quotinator.Api.Tests.Components;

[TestClass]
public sealed class ChangelogEntryTests
{
    // ── FormatInline — markdown link conversion ───────────────────────────────

    [TestMethod]
    public void FormatInline_MarkdownLink_ConvertsToAnchor()
    {
        var result = ChangelogEntry.FormatInline("[GitHub](https://github.com)").Value;
        Assert.AreEqual("<a href=\"https://github.com\">GitHub</a>", result);
    }

    [TestMethod]
    public void FormatInline_BacktickCode_ConvertsToCodeElement()
    {
        var result = ChangelogEntry.FormatInline("use `dotnet build`").Value;
        Assert.AreEqual("use <code>dotnet build</code>", result);
    }

    [TestMethod]
    public void FormatInline_HtmlSpecialChars_AreEncoded()
    {
        var result = ChangelogEntry.FormatInline("<script>alert('xss')</script>").Value;
        StringAssert.Contains(result, "&lt;script&gt;");
        StringAssert.DoesNotMatch(result, new System.Text.RegularExpressions.Regex("<script>"));
    }

    [TestMethod]
    public void FormatInline_PlainText_ReturnedUnchanged()
    {
        const string plain = "Internal improvements — no user-facing changes.";
        var result = ChangelogEntry.FormatInline(plain).Value;
        Assert.AreEqual(plain, result);
    }

    // ── CategoryBadgeClass — all known categories ─────────────────────────────

    [TestMethod]
    public void CategoryBadgeClass_Added_ReturnsSuccess()
        => Assert.AreEqual("bg-success", ChangelogEntry.CategoryBadgeClass("Added"));

    [TestMethod]
    public void CategoryBadgeClass_Fixed_ReturnsDanger()
        => Assert.AreEqual("bg-danger", ChangelogEntry.CategoryBadgeClass("Fixed"));

    [TestMethod]
    public void CategoryBadgeClass_Changed_ReturnsPrimary()
        => Assert.AreEqual("bg-primary", ChangelogEntry.CategoryBadgeClass("Changed"));

    [TestMethod]
    public void CategoryBadgeClass_Removed_ReturnsWarning()
        => Assert.AreEqual("bg-warning text-dark", ChangelogEntry.CategoryBadgeClass("Removed"));

    [TestMethod]
    public void CategoryBadgeClass_Unknown_ReturnsSecondary()
        => Assert.AreEqual("bg-secondary", ChangelogEntry.CategoryBadgeClass("Security"));

    // ── Rendering paths — model state that drives the three if/else branches ──

    [TestMethod]
    public void RenderPath_HighlightsPresent_ModelHasHighlightsAndEmptySections()
    {
        // Path 1: highlights list + GitHub link shown; section badges hidden
        var release = new ChangelogRelease(
            "1.0.0", "2026-01-01",
            ["Quote support added."],
            [],
            [], [], new Dictionary<string, ChangelogReleaseTranslation>());

        Assert.IsTrue(release.Highlights.Count > 0, "highlights must be non-empty for this path");
        Assert.AreEqual(0, release.Sections.Count);
    }

    [TestMethod]
    public void RenderPath_HighlightsEmptySectionsPresent_ModelHasSectionsOnly()
    {
        // Path 2: section badges shown; highlights list + GitHub link hidden
        var release = new ChangelogRelease(
            "1.0.0", "2026-01-01",
            [],
            [new ChangelogSection("Added", ["New endpoint added."])],
            [], [], new Dictionary<string, ChangelogReleaseTranslation>());

        Assert.AreEqual(0, release.Highlights.Count, "highlights must be empty for this path");
        Assert.IsTrue(release.Sections.Count > 0, "at least one section must be present");
    }

    [TestMethod]
    public void RenderPath_BothEmpty_ModelHasNoHighlightsAndNoSections()
    {
        // Path 3: only the summary header renders; no error
        var release = new ChangelogRelease(
            "1.0.0", "2026-01-01",
            [],
            [],
            [], [], new Dictionary<string, ChangelogReleaseTranslation>());

        Assert.AreEqual(0, release.Highlights.Count);
        Assert.AreEqual(0, release.Sections.Count);
    }
}
