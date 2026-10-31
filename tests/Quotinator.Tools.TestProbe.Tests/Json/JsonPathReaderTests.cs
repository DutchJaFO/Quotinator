using System.Text.Json;
using Quotinator.Tools.TestProbe.Json;

namespace Quotinator.Tools.TestProbe.Tests.Json;

[TestClass]
public sealed class JsonPathReaderTests
{
    [TestMethod]
    public void TryRead_TopLevelField_IsFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"status":"unhealthy"}""");

        Assert.IsTrue(JsonPathReader.TryRead(document.RootElement, "status", out _));
    }

    [TestMethod]
    public void TryRead_NestedField_ReturnsItsValue()
    {
        using JsonDocument document = JsonDocument.Parse("""{"database":{"reason":"locked"}}""");

        JsonPathReader.TryRead(document.RootElement, "database.reason", out JsonElement value);

        Assert.AreEqual("locked", value.GetString());
    }

    [TestMethod]
    public void TryRead_MissingField_IsNotFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"status":"healthy"}""");

        Assert.IsFalse(JsonPathReader.TryRead(document.RootElement, "reason", out _));
    }

    [TestMethod]
    public void TryRead_PathThroughAString_IsNotFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"status":"healthy"}""");

        Assert.IsFalse(JsonPathReader.TryRead(document.RootElement, "status.inner", out _));
    }

    [TestMethod]
    public void TryRead_DifferentlyCasedName_IsNotFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"status":"healthy"}""");

        Assert.IsFalse(JsonPathReader.TryRead(document.RootElement, "Status", out _));
    }

    [TestMethod]
    public void TryRead_ArrayIndex_ReturnsThatElementsField()
    {
        using JsonDocument document = JsonDocument.Parse("""{"items":[{"quote":"first"},{"quote":"second"}]}""");

        JsonPathReader.TryRead(document.RootElement, "items.1.quote", out JsonElement value);

        Assert.AreEqual("second", value.GetString());
    }

    [TestMethod]
    public void TryRead_ArrayIndexPastTheEnd_IsNotFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"items":[{"quote":"first"}]}""");

        Assert.IsFalse(JsonPathReader.TryRead(document.RootElement, "items.1.quote", out _));
    }

    [TestMethod]
    public void TryRead_NamedSegmentOnAnArray_IsNotFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"items":[{"quote":"first"}]}""");

        Assert.IsFalse(JsonPathReader.TryRead(document.RootElement, "items.quote", out _));
    }

    [TestMethod]
    public void TryRead_NumericPropertyNameOnAnObject_IsFound()
    {
        using JsonDocument document = JsonDocument.Parse("""{"counts":{"0":"zero"}}""");

        JsonPathReader.TryRead(document.RootElement, "counts.0", out JsonElement value);

        Assert.AreEqual("zero", value.GetString());
    }

    [TestMethod]
    public void Render_String_IsUnquoted()
    {
        using JsonDocument document = JsonDocument.Parse("\"text\"");

        Assert.AreEqual("text", JsonPathReader.Render(document.RootElement));
    }

    [TestMethod]
    public void Render_Number_IsItsJsonText()
    {
        using JsonDocument document = JsonDocument.Parse("42");

        Assert.AreEqual("42", JsonPathReader.Render(document.RootElement));
    }

    [TestMethod]
    public void Render_Boolean_IsItsJsonText()
    {
        using JsonDocument document = JsonDocument.Parse("true");

        Assert.AreEqual("true", JsonPathReader.Render(document.RootElement));
    }
}
