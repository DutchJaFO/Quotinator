using System.Text.Json;
using Quotinator.Tools.DbInspector;

namespace Quotinator.Tools.DbInspector.Tests;

/// <summary>
/// The rows a query returns, written as JSON so a test document can assert on them with TestProbe instead of
/// reading the printed table.
/// </summary>
[TestClass]
public class JsonRowWriterTests
{
    [TestMethod]
    public void ToJson_Rows_ProducesAnArrayOfObjectsKeyedByColumn()
    {
        IDictionary<string, object> first = new Dictionary<string, object> { ["Genres"] = 25L, ["Name"] = "a" };
        IDictionary<string, object> second = new Dictionary<string, object> { ["Genres"] = 0L, ["Name"] = "b" };

        using JsonDocument document = JsonDocument.Parse(JsonRowWriter.ToJson([first, second]));

        Assert.AreEqual(2, document.RootElement.GetArrayLength());
        Assert.AreEqual(25, document.RootElement[0].GetProperty("Genres").GetInt64());
        Assert.AreEqual("b", document.RootElement[1].GetProperty("Name").GetString());
    }

    [TestMethod]
    public void ToJson_NullValue_IsWrittenAsNull()
    {
        IDictionary<string, object> row = new Dictionary<string, object> { ["Application"] = null! };

        using JsonDocument document = JsonDocument.Parse(JsonRowWriter.ToJson([row]));

        Assert.AreEqual(JsonValueKind.Null, document.RootElement[0].GetProperty("Application").ValueKind);
    }

    [TestMethod]
    public void ToJson_NoRows_ProducesAnEmptyArray()
    {
        using JsonDocument document = JsonDocument.Parse(JsonRowWriter.ToJson([]));

        Assert.AreEqual(0, document.RootElement.GetArrayLength());
    }
}
