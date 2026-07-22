using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class RepositorySqlTests
{
    private sealed record FakeColumnMetadata(IReadOnlyList<string> ValidColumnNames, IReadOnlyList<string> IdColumnNames)
        : IEntityColumnMetadata;

    private static readonly IEntityColumnMetadata TestColumns = new FakeColumnMetadata(
        ValidColumnNames: ["Id", "Label", "DateCreated"],
        IdColumnNames:    ["Id"]);

    [TestMethod]
    public void SelectPage_ColumnNameNotIdentifierShaped_ThrowsArgumentException()
        => Assert.ThrowsExactly<ArgumentException>(
            () => RepositorySql.SelectPage("Widgets", TestColumns, [new SortColumn("Id; DROP TABLE Widgets;")]));
}
