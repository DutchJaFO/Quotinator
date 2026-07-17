using Quotinator.Data.Diagnostics;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class RepositorySqlGuardTests
{
    /// <summary>
    /// Verifies that every SQL string produced by <see cref="RepositorySql"/> factory methods
    /// passes the CVE-2025-6965 aggregate guard.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(RepositorySqlCases))]
    public void RepositorySqlFactory_PassesAggregateGuard(string name, string sql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(sql),
            $"RepositorySql.{name} contains a vulnerable aggregate pattern. " +
            "Review the factory method and consult docs/sql-safety.md.");
    }

    public static IEnumerable<object[]> RepositorySqlCases()
    {
        const string t = "TestWidgets";
        return
        [
            ["SelectById",    RepositorySql.SelectById(t)],
            ["SoftDelete",    RepositorySql.SoftDelete(t)],
            ["SelectDeleted", RepositorySql.SelectDeleted(t)],
            ["Restore",       RepositorySql.Restore(t)],
            ["HardDelete",           RepositorySql.HardDelete(t)],
            ["Purge",                RepositorySql.Purge(t)],
            ["SelectByForeignKey",   RepositorySql.SelectByForeignKey(t, "ParentId")],
            ["SelectJunctionRow",    RepositorySql.SelectJunctionRow(t, "LeftId", "RightId")],
            ["SelectByIds",          RepositorySql.SelectByIds(t)],
            ["SelectPage(default order)", RepositorySql.SelectPage(t)],
            ["SelectPage(single column)", RepositorySql.SelectPage(t, [new SortColumn("Label")])],
            ["SelectPage(descending)",    RepositorySql.SelectPage(t, [new SortColumn("Label", Descending: true)])],
            ["SelectPage(multi-column)",  RepositorySql.SelectPage(t, [new SortColumn("Label"), new SortColumn("DateCreated", Descending: true)])],
            ["CountActive",               RepositorySql.CountActive(t)],

            // Audit factory methods — all four filter-flag combinations.
            ["SystemAudit.SelectPaged(false,false)", Sql.SystemAudit.SelectPaged(false, false)],
            ["SystemAudit.SelectPaged(true,false)",  Sql.SystemAudit.SelectPaged(true,  false)],
            ["SystemAudit.SelectPaged(false,true)",  Sql.SystemAudit.SelectPaged(false, true)],
            ["SystemAudit.SelectPaged(true,true)",   Sql.SystemAudit.SelectPaged(true,  true)],
            ["SystemAudit.CountPaged(false,false)",  Sql.SystemAudit.CountPaged(false,  false)],
            ["SystemAudit.CountPaged(true,false)",   Sql.SystemAudit.CountPaged(true,   false)],
            ["SystemAudit.CountPaged(false,true)",   Sql.SystemAudit.CountPaged(false,  true)],
            ["SystemAudit.CountPaged(true,true)",    Sql.SystemAudit.CountPaged(true,   true)],
        ];
    }
}
