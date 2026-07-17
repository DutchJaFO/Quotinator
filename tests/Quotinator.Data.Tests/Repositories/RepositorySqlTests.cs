using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class RepositorySqlTests
{
    [TestMethod]
    public void SelectPage_ColumnNameNotIdentifierShaped_ThrowsArgumentException()
        => Assert.ThrowsExactly<ArgumentException>(
            () => RepositorySql.SelectPage("Widgets", [new SortColumn("Id; DROP TABLE Widgets;")]));
}
