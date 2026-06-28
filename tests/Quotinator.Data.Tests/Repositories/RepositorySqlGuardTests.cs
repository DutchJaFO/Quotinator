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

            // Audit factory methods — all four filter-flag combinations.
            ["Audit.SelectPaged(false,false)", Sql.Audit.SelectPaged(false, false)],
            ["Audit.SelectPaged(true,false)",  Sql.Audit.SelectPaged(true,  false)],
            ["Audit.SelectPaged(false,true)",  Sql.Audit.SelectPaged(false, true)],
            ["Audit.SelectPaged(true,true)",   Sql.Audit.SelectPaged(true,  true)],
            ["Audit.CountPaged(false,false)",  Sql.Audit.CountPaged(false,  false)],
            ["Audit.CountPaged(true,false)",   Sql.Audit.CountPaged(true,   false)],
            ["Audit.CountPaged(false,true)",   Sql.Audit.CountPaged(false,  true)],
            ["Audit.CountPaged(true,true)",    Sql.Audit.CountPaged(true,   true)],
        ];
    }
}
