using Quotinator.Data.Queries;

namespace Quotinator.Data.Tests.Queries;

/// <summary>Verifies <see cref="TextClauses.Equals"/> emits the expected case-insensitive fragment. See #211.</summary>
[TestClass]
public class TextClausesTests
{
    [TestMethod]
    public void Equals_WrapsBothColumnAndParamInLower()
        => Assert.AreEqual("LOWER(Name) = LOWER(@name)", TextClauses.Equals("Name", "name"));

    [TestMethod]
    public void Equals_AliasedColumn_WrapsColumnInLower()
        => Assert.AreEqual("LOWER(s.Title) = LOWER(@title)", TextClauses.Equals("s.Title", "title"));
}
