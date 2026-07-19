using System.Text.Json;
using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

[TestClass]
public class OptionalJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new OptionalJsonConverterFactory() },
    };

    private sealed class Wrapper
    {
        public Optional<string> Value { get; init; }
    }

    [TestMethod]
    public void Read_PropertyAbsent_ReturnsAbsentOptional()
    {
        var result = JsonSerializer.Deserialize<Wrapper>("{}", Options)!;

        Assert.IsFalse(result.Value.HasValue);
    }

    [TestMethod]
    public void Read_PropertyPresentNull_ReturnsOfNull()
    {
        var result = JsonSerializer.Deserialize<Wrapper>("""{"value":null}""", Options)!;

        Assert.IsTrue(result.Value.HasValue);
        Assert.IsNull(result.Value.Value);
    }

    [TestMethod]
    public void Read_PropertyPresentValue_ReturnsOfValue()
    {
        var result = JsonSerializer.Deserialize<Wrapper>("""{"value":"1994"}""", Options)!;

        Assert.IsTrue(result.Value.HasValue);
        Assert.AreEqual("1994", result.Value.Value);
    }

    [TestMethod]
    public void ResolveAgainst_Absent_ReturnsExistingValue()
    {
        var result = Optional<string>.Absent.ResolveAgainst("existing");

        Assert.AreEqual("existing", result);
    }

    [TestMethod]
    public void ResolveAgainst_PresentNull_ReturnsNullRegardlessOfExisting()
    {
        var result = Optional<string>.Of(null).ResolveAgainst("existing");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveAgainst_PresentValue_ReturnsItsOwnValueRegardlessOfExisting()
    {
        var result = Optional<string>.Of("new").ResolveAgainst("existing");

        Assert.AreEqual("new", result);
    }

    [TestMethod]
    public void ImplicitConversion_FromBareValue_IsEquivalentToOf()
    {
        Optional<string> value = "1994";

        Assert.IsTrue(value.HasValue);
        Assert.AreEqual("1994", value.Value);
    }
}
