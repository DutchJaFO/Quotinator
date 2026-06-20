using Quotinator.Data.Diagnostics;
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
            ["HardDelete",    RepositorySql.HardDelete(t)],
            ["Purge",         RepositorySql.Purge(t)],
        ];
    }
}
