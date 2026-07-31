using Quotinator.Changelog.Formatting;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Tests.Formatting;

[TestClass]
public sealed class ChangelogQuoteFormatterTests
{
    [TestMethod]
    public void Format_Null_ReturnsNull()
    {
        Assert.IsNull(ChangelogQuoteFormatter.Format(null));
    }

    [TestMethod]
    public void Format_EmptyText_ReturnsNull()
    {
        var quote = new ChangelogQuote { Text = "   " };
        Assert.IsNull(ChangelogQuoteFormatter.Format(quote));
    }

    [TestMethod]
    public void Format_TextOnly_RendersBlockquoteWithoutAttribution()
    {
        var quote = new ChangelogQuote { Text = "Simplicity is the ultimate sophistication." };
        var result = ChangelogQuoteFormatter.Format(quote);
        Assert.AreEqual("> \"Simplicity is the ultimate sophistication.\"", result);
    }

    [TestMethod]
    public void ReleaseWithQuote_RendersInOutput()
    {
        var quote = new ChangelogQuote { Text = "Simplicity is the ultimate sophistication.", Attribution = "Leonardo da Vinci" };
        var result = ChangelogQuoteFormatter.Format(quote);
        Assert.AreEqual("> \"Simplicity is the ultimate sophistication.\" — Leonardo da Vinci", result);
    }
}
