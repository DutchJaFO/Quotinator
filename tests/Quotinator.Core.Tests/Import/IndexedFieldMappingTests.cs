using System.Text.Json;
using Quotinator.Core.Import;

namespace Quotinator.Core.Tests.Import;

[TestClass]
public class IndexedFieldMappingTests
{
    [TestMethod]
    public void Deserialize_PartialMapping_UnmappedSlotsAreNull()
    {
        var mapping = JsonSerializer.Deserialize<IndexedFieldMapping>("""{"quote": 1, "source": 2}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(mapping);
        Assert.AreEqual(1, mapping!.Quote);
        Assert.AreEqual(2, mapping.Source);
        Assert.IsNull(mapping.Id);
        Assert.IsNull(mapping.OriginalLanguage);
        Assert.IsNull(mapping.Date);
        Assert.IsNull(mapping.Character);
        Assert.IsNull(mapping.Author);
        Assert.IsNull(mapping.Type);
        Assert.IsNull(mapping.Genres);
    }

    [TestMethod]
    public void Deserialize_EmptyObject_AllSlotsAreNull()
    {
        var mapping = JsonSerializer.Deserialize<IndexedFieldMapping>("{}");

        Assert.IsNotNull(mapping);
        Assert.IsNull(mapping!.Quote);
        Assert.IsNull(mapping.Source);
    }
}
