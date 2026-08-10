using Quotinator.Core.Enums;
using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Core.Models;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class QuoteFieldDefaultsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public void Deserialize_PartialDefaults_UnsetSlotsAreNull()
    {
        var defaults = JsonSerializer.Deserialize<QuoteFieldDefaults>("""{"originalLanguage": "en", "type": "movie"}""", CaseInsensitive);

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
        var defaults = JsonSerializer.Deserialize<QuoteFieldDefaults>("""{"genres": ["drama", "sci-fi"]}""", CaseInsensitive);

        Assert.IsNotNull(defaults);
        Assert.AreSequenceEqual(["drama", "sci-fi"], [.. defaults!.Genres!]);
    }
}
