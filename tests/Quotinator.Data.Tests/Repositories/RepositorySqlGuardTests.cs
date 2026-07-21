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

    /// <summary>
    /// Verifies that every SQL string produced by <see cref="RepositorySql"/> factory methods does not
    /// compare an id-named column to a bound parameter case-sensitively. See ADR 012 and #210 — every
    /// entity reachable through the generic repository layer gets the same protection, not just the
    /// domain-specific queries in <c>Quotinator.Core</c>.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(RepositorySqlCases))]
    public void RepositorySqlFactory_PassesIdCaseGuard(string name, string sql)
    {
        var violations = SqlIdCaseGuard.FindViolations(sql);
        Assert.IsEmpty(violations,
            $"RepositorySql.{name} contains a case-sensitive id comparison: {string.Join(", ", violations)}. " +
            "Wrap both sides in LOWER(...) — see ADR 012.");
    }

    /// <summary>
    /// Verifies that every SQL string produced by <see cref="RepositorySql"/> factory methods does not
    /// return any <c>*Id</c>-suffixed column unwrapped in its SELECT column list — PK or FK, regardless
    /// of downstream C# type. See ADR 012's "read-time presentation normalization" revision.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(RepositorySqlCases))]
    public void RepositorySqlFactory_PassesSelectPresentationGuard(string name, string sql)
    {
        var violations = SqlSelectPresentationGuard.FindUnwrappedSelectColumns(sql);
        Assert.IsEmpty(violations,
            $"RepositorySql.{name} selects {string.Join(", ", violations)} unwrapped — wrap in " +
            "LOWER(...) AS ColumnName in the SELECT column list. See ADR 012's \"read-time " +
            "presentation normalization\" revision.");
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
