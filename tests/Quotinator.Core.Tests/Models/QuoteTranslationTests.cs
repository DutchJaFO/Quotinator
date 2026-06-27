using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Models;

[TestClass]
public class QuoteTranslationTests
{
    [TestMethod]
    public void QuoteTranslation_RequiredQuoteText_IsSet()
    {
        var translation = new SourceQuoteTranslation { QuoteText = "Hier kijk ik naar je, kind." };

        Assert.AreEqual("Hier kijk ik naar je, kind.", translation.QuoteText);
    }

    [TestMethod]
    public void QuoteTranslation_Source_IsOptional()
    {
        var translation = new SourceQuoteTranslation { QuoteText = "Hier kijk ik naar je, kind." };

        Assert.IsNull(translation.Source);
    }

    [TestMethod]
    public void QuoteTranslation_Source_CanBeSet()
    {
        var translation = new SourceQuoteTranslation
        {
            QuoteText = "Hier kijk ik naar je, kind.",
            Source = "Casablanca"
        };

        Assert.AreEqual("Casablanca", translation.Source);
    }
}
