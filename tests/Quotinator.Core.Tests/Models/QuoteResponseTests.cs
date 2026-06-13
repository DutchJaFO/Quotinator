using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Models;

[TestClass]
public class QuoteResponseTests
{
    private static QuoteResponse BuildResponse(string language, string originalLanguage) => new()
    {
        Id = "123e4567-e89b-12d3-a456-426614174000",
        Quote = "Here's looking at you, kid.",
        Language = language,
        OriginalLanguage = originalLanguage,
        Source = "Casablanca",
        Type = "movie"
    };

    [TestMethod]
    public void QuoteResponse_IsTranslated_FalseWhenLanguageMatchesOriginal()
    {
        var response = BuildResponse("en", "en");

        Assert.IsFalse(response.IsTranslated);
    }

    [TestMethod]
    public void QuoteResponse_IsTranslated_TrueWhenLanguageDiffersFromOriginal()
    {
        var response = BuildResponse("nl", "en");

        Assert.IsTrue(response.IsTranslated);
    }

    [TestMethod]
    public void QuoteResponse_Genres_DefaultsToEmpty()
    {
        Assert.IsEmpty(BuildResponse("en", "en").Genres);
    }

    [TestMethod]
    public void QuoteResponse_OptionalProperties_DefaultToNull()
    {
        var response = BuildResponse("en", "en");

        Assert.IsNull(response.Date);
        Assert.IsNull(response.Character);
        Assert.IsNull(response.Author);
    }
}
