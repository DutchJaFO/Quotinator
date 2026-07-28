using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteFieldDefaultsTests
{
    [TestMethod]
    public void Deserialize_PartialDefaults_UnsetSlotsAreNull()
    {
        var defaults = JsonSerializer.Deserialize<QuoteFieldDefaults>("""{"originalLanguage": "en", "type": "movie"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(defaults);
        Assert.AreEqual("en", defaults!.OriginalLanguage);
        Assert.AreEqual(QuoteType.Movie, defaults.Type);
        Assert.IsNull(defaults.Date);
        Assert.IsNull(defaults.Character);
        Assert.IsNull(defaults.Author);
        Assert.IsNull(defaults.Genres);
    }

    [TestMethod]
    public void Deserialize_GenresArray_PopulatesList()
    {
        var defaults = JsonSerializer.Deserialize<QuoteFieldDefaults>("""{"genres": ["drama", "sci-fi"]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(defaults);
        CollectionAssert.AreEqual(new[] { "drama", "sci-fi" }, defaults!.Genres!.ToList());
    }
}
