using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class GetHighlightsTests
{
    private static ChangelogUnreleased MakeEntry(
        IEnumerable<string>? highlights = null,
        Dictionary<string, ChangelogReleaseTranslation>? translations = null) =>
        new()
        {
            Highlights   = [.. highlights ?? ["English highlight."]],
            Translations = translations ?? []
        };

    private static ChangelogReleaseTranslation MakeTranslation(params string[] texts) =>
        new() { Highlights = [.. texts.Select(t => new ChangelogTranslationItem { Text = t })] };

    // ── Ideal path ──────────────────────────────────────────────────────────

    [TestMethod]
    public void GetHighlights_TranslationPresent_ReturnsTranslated()
    {
        var entry = MakeEntry(
            translations: new() { ["nl"] = MakeTranslation("Nederlandstalige highlight.") });

        var result = entry.GetHighlights("nl");

        CollectionAssert.AreEqual(new[] { "Nederlandstalige highlight." }, result.ToList());
    }

    [TestMethod]
    public void GetHighlights_MultipleTranslatedItems_ReturnsAllInOrder()
    {
        var entry = MakeEntry(
            translations: new() { ["de"] = MakeTranslation("Erste.", "Zweite.") });

        var result = entry.GetHighlights("de");

        CollectionAssert.AreEqual(new[] { "Erste.", "Zweite." }, result.ToList());
    }

    // ── Fallback path ────────────────────────────────────────────────────────

    [TestMethod]
    public void GetHighlights_NoTranslationsAtAll_ReturnsEnglish()
    {
        var entry = MakeEntry(highlights: ["English highlight."]);

        var result = entry.GetHighlights("nl");

        CollectionAssert.AreEqual(new[] { "English highlight." }, result.ToList());
    }

    [TestMethod]
    public void GetHighlights_CultureNotInTranslations_ReturnsEnglish()
    {
        var entry = MakeEntry(
            highlights:   ["English highlight."],
            translations: new() { ["de"] = MakeTranslation("Deutschsprachige highlight.") });

        var result = entry.GetHighlights("nl");

        CollectionAssert.AreEqual(new[] { "English highlight." }, result.ToList());
    }

    [TestMethod]
    public void GetHighlights_TranslationHighlightsEmpty_ReturnsEnglish()
    {
        var entry = MakeEntry(
            highlights:   ["English highlight."],
            translations: new() { ["nl"] = new ChangelogReleaseTranslation() });

        var result = entry.GetHighlights("nl");

        CollectionAssert.AreEqual(new[] { "English highlight." }, result.ToList());
    }

    [TestMethod]
    public void GetHighlights_NullCulture_ReturnsEnglish()
    {
        var entry = MakeEntry(
            highlights:   ["English highlight."],
            translations: new() { ["nl"] = MakeTranslation("Nederlandstalige highlight.") });

        var result = entry.GetHighlights(null);

        CollectionAssert.AreEqual(new[] { "English highlight." }, result.ToList());
    }

    [TestMethod]
    public void GetHighlights_TranslationItemsWithNullText_SkipsNullsAndFallsBack()
    {
        var entry = MakeEntry(
            highlights:   ["English highlight."],
            translations: new()
            {
                ["nl"] = new ChangelogReleaseTranslation
                {
                    Highlights = [new ChangelogTranslationItem { Text = null }]
                }
            });

        var result = entry.GetHighlights("nl");

        CollectionAssert.AreEqual(new[] { "English highlight." }, result.ToList());
    }
}
