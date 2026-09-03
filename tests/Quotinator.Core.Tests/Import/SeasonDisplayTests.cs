using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

/// <summary>#375: a Season carries an ordinal plus an optional title and subtitle, and the three
/// combine into one display name. Every combination is covered, because each is a real shape in the
/// data: Avatar names both halves, most series name neither.</summary>
[TestClass]
public class SeasonDisplayTests
{
    /// <summary>The worked example from ADR 011 — Avatar: The Last Airbender's first season.</summary>
    [TestMethod]
    public void Format_NumberTitleAndSubtitle_RendersAllThree()
        => Assert.AreEqual("Book One: Water", SeasonDisplay.Format(1, "Book One", "Water"));

    /// <summary>The control: a season with no name of its own is identified by its ordinal alone, and
    /// must not render a stray separator or an empty title.</summary>
    [TestMethod]
    public void Format_NumberOnly_RendersWithoutTitleOrSubtitle()
        => Assert.AreEqual("Season 3", SeasonDisplay.Format(3, null, null));

    /// <summary>A title without a subtitle renders no trailing separator.</summary>
    [TestMethod]
    public void Format_TitleWithoutSubtitle_RendersNoSeparator()
        => Assert.AreEqual("Book Two", SeasonDisplay.Format(2, "Book Two", null));

    /// <summary>A subtitle without a title still identifies the season by its ordinal.</summary>
    [TestMethod]
    public void Format_SubtitleWithoutTitle_FallsBackToTheOrdinal()
        => Assert.AreEqual("Season 4: Earth", SeasonDisplay.Format(4, null, "Earth"));

    /// <summary>Whitespace-only is not a name. Treated as absent rather than rendered as a blank.</summary>
    [TestMethod]
    public void Format_WhitespaceOnlyTitleAndSubtitle_TreatedAsAbsent()
        => Assert.AreEqual("Season 1", SeasonDisplay.Format(1, "   ", "  "));
}
