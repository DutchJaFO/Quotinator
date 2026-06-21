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

    // ── Rendering paths — model state that drives the two display branches ────

    [TestMethod]
    public void RenderPath_HighlightsPresent_NoTechnicalLists()
    {
        // Path 1: highlights shown; Added/Changed/Fixed/Removed blocks hidden
        var release = new ChangelogRelease
        {
            Version    = "1.0.0",
            Date       = "2026-01-01",
            Highlights = ["Quote support added."]
        };

        Assert.IsTrue(release.Highlights.Count > 0);
        Assert.AreEqual(0, release.Added.Count);
        Assert.AreEqual(0, release.Changed.Count);
        Assert.AreEqual(0, release.Fixed.Count);
        Assert.AreEqual(0, release.Removed.Count);
    }

    [TestMethod]
    public void RenderPath_HighlightsEmpty_TechnicalListsShown()
    {
        // Path 2: Added/Changed/Fixed/Removed blocks shown; highlights hidden
        var release = new ChangelogRelease
        {
            Version = "1.0.0",
            Date    = "2026-01-01",
            Added   = ["New endpoint added."],
            Fixed   = ["Rate limit header fixed."]
        };

        Assert.AreEqual(0, release.Highlights.Count);
        Assert.IsTrue(release.Added.Count > 0);
        Assert.IsTrue(release.Fixed.Count > 0);
    }

    [TestMethod]
    public void RenderPath_AllEmpty_OnlySummaryHeaderShown()
    {
        // Path 3: nothing to render; no error expected
        var release = new ChangelogRelease { Version = "1.0.0", Date = "2026-01-01" };

        Assert.AreEqual(0, release.Highlights.Count);
        Assert.AreEqual(0, release.Added.Count);
        Assert.AreEqual(0, release.Changed.Count);
        Assert.AreEqual(0, release.Fixed.Count);
        Assert.AreEqual(0, release.Removed.Count);
    }

    [TestMethod]
    public void RenderPath_ReferencesPresent_ModelHasIssuesAndCves()
    {
        var release = new ChangelogRelease
        {
            Version    = "1.0.0",
            Date       = "2026-01-01",
            Highlights = ["Something changed."],
            Issues     = [80, 82],
            Cves       = ["CVE-2025-6965"]
        };

        Assert.IsTrue(release.Issues.Count > 0);
        Assert.IsTrue(release.Cves.Count > 0);
        Assert.AreEqual(80,              release.Issues[0]);
        Assert.AreEqual("CVE-2025-6965", release.Cves[0]);
    }

    // ── Unreleased — same model, no Version or Date ───────────────────────────

    [TestMethod]
    public void RenderPath_Unreleased_HasHighlightsAndReferences()
    {
        var unreleased = new ChangelogUnreleased
        {
            Highlights = ["New feature in progress."],
            Issues     = [80]
        };

        Assert.IsTrue(unreleased.Highlights.Count > 0);
        Assert.AreEqual(80, unreleased.Issues[0]);
    }
}
