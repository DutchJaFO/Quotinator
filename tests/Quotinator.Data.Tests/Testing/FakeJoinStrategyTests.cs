using Quotinator.Data.Testing.Fakes;

namespace Quotinator.Data.Tests.Testing;

[TestClass]
public class FakeJoinStrategyTests
{
    [TestMethod]
    public void BuildSql_ReturnsConstructorSuppliedSql()
    {
        const string sql = "SELECT 1 FROM Foo JOIN Bar ON Foo.Id = Bar.FooId";
        var strategy = new FakeJoinStrategy<object>(sql);

        Assert.AreEqual(sql, strategy.BuildSql());
    }
}
