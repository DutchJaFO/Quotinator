using Quotinator.Data.Import;

namespace Quotinator.Core.Tests.Models;

[TestClass]
public class QuoteTests
{
    private static SourceQuote BuildMinimal() => new()
    {
        Id = "123e4567-e89b-12d3-a456-426614174000",
        QuoteText = "Here's looking at you, kid.",
        Source = "Casablanca"
    };

    [TestMethod]
    public void Quote_RequiredProperties_AreSet()
    {
        var quote = BuildMinimal();

        Assert.AreEqual("123e4567-e89b-12d3-a456-426614174000", quote.Id);
        Assert.AreEqual("Here's looking at you, kid.", quote.QuoteText);
        Assert.AreEqual("Casablanca", quote.Source);
    }

    [TestMethod]
    public void Quote_Type_DefaultsToMovie()
    {
        Assert.AreEqual("movie", BuildMinimal().Type);
    }

    [TestMethod]
    public void Quote_OriginalLanguage_DefaultsToEnglish()
    {
        Assert.AreEqual("en", BuildMinimal().OriginalLanguage);
    }

    [TestMethod]
    public void Quote_Genres_DefaultsToEmpty()
    {
        Assert.IsEmpty(BuildMinimal().Genres);
    }

    [TestMethod]
    public void Quote_Translations_DefaultsToEmpty()
    {
        Assert.IsEmpty(BuildMinimal().Translations);
    }

    [TestMethod]
    public void Quote_OptionalProperties_DefaultToNull()
    {
        var quote = BuildMinimal();

        Assert.IsNull(quote.Date);
        Assert.IsNull(quote.Character);
        Assert.IsNull(quote.Author);
    }

    [TestMethod]
    public void Quote_AllProperties_CanBeSet()
    {
        var translation = new SourceQuoteTranslation { QuoteText = "Hier kijk ik naar je, kind.", Source = "Casablanca" };
        var quote = new SourceQuote
        {
            Id = "123e4567-e89b-12d3-a456-426614174000",
            QuoteText = "Here's looking at you, kid.",
            OriginalLanguage = "en",
            Source = "Casablanca",
            Date = "1942",
            Character = "Rick Blaine",
            Author = null,
            Type = "movie",
            Genres = ["drama", "romance"],
            Translations = new Dictionary<string, SourceQuoteTranslation> { ["nl"] = translation }
        };

        Assert.AreEqual("en", quote.OriginalLanguage);
        Assert.AreEqual("1942", quote.Date);
        Assert.AreEqual("Rick Blaine", quote.Character);
        Assert.IsNull(quote.Author);
        Assert.AreEqual("movie", quote.Type);
        Assert.HasCount(2, quote.Genres);
        Assert.IsTrue(quote.Genres.Contains("drama"));
        Assert.IsTrue(quote.Translations.ContainsKey("nl"));
    }

    [TestMethod]
    public void Quote_PersonType_UsesAuthorNotCharacter()
    {
        var quote = new SourceQuote
        {
            Id = "223e4567-e89b-12d3-a456-426614174001",
            QuoteText = "We shall fight on the beaches.",
            OriginalLanguage = "en",
            Source = "House of Commons, 4 June 1940",
            Date = "1940-06-04",
            Author = "Winston Churchill",
            Type = "person",
            Genres = ["non-fiction"]
        };

        Assert.AreEqual("Winston Churchill", quote.Author);
        Assert.IsNull(quote.Character);
        Assert.AreEqual("person", quote.Type);
        Assert.AreEqual("1940-06-04", quote.Date);
    }

    [TestMethod]
    public void Quote_BookType_HasBothAuthorAndCharacter()
    {
        var quote = new SourceQuote
        {
            Id = "323e4567-e89b-12d3-a456-426614174002",
            QuoteText = "All that is gold does not glitter.",
            OriginalLanguage = "en",
            Source = "The Fellowship of the Ring",
            Date = "1954",
            Character = "Bilbo Baggins",
            Author = "J.R.R. Tolkien",
            Type = "book",
            Genres = ["fantasy", "fiction"]
        };

        Assert.AreEqual("J.R.R. Tolkien", quote.Author);
        Assert.AreEqual("Bilbo Baggins", quote.Character);
        Assert.AreEqual("book", quote.Type);
    }
}
