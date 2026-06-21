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

    [TestMethod]
    public void RenderPath_ReferencesPresent_ModelHasIssuesAndCves()
    {
        // References block shown when Issues or Cves are non-empty
        var release = new ChangelogRelease(
            "1.0.0", "2026-01-01",
            ["Something changed."],
            [],
            [80, 82], ["CVE-2025-6965"], new Dictionary<string, ChangelogReleaseTranslation>());

        Assert.IsTrue(release.Issues.Count > 0,  "issues must be non-empty to trigger references block");
        Assert.IsTrue(release.Cves.Count > 0,    "cves must be non-empty to trigger references block");
        Assert.AreEqual(80,             release.Issues[0]);
        Assert.AreEqual("CVE-2025-6965", release.Cves[0]);
    }

    [TestMethod]
    public void RenderPath_Unreleased_HasHighlightsNoVersionNoDate()
    {
        // Unreleased entries: IsUnreleased=true on the component suppresses version/date/GitHub link;
        // the model uses empty version and date strings.
        var release = new ChangelogRelease(
            "", "",
            ["New feature in progress."],
            [],
            [80], [], new Dictionary<string, ChangelogReleaseTranslation>());

        Assert.IsTrue(release.Highlights.Count > 0);
        Assert.AreEqual("", release.Version, "version is empty for unreleased entries");
        Assert.AreEqual("", release.Date,    "date is empty for unreleased entries");
        Assert.AreEqual(80, release.Issues[0]);
    }
}
