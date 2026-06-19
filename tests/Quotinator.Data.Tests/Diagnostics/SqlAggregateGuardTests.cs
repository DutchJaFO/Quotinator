using Quotinator.Data.Diagnostics;

namespace Quotinator.Data.Tests.Diagnostics;

[TestClass]
public class SqlAggregateGuardTests
{
    // ── Patterns that must be flagged ─────────────────────────────────────────

    [TestMethod]
    public void IsVulnerablePattern_GroupByWithCount_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT dept, COUNT(*) FROM Employees GROUP BY dept"));

    [TestMethod]
    public void IsVulnerablePattern_HavingWithCount_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT dept, COUNT(*) FROM Employees GROUP BY dept HAVING COUNT(*) > 1"));

    [TestMethod]
    public void IsVulnerablePattern_GroupByWithSum_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT category, SUM(amount) FROM Orders GROUP BY category"));

    [TestMethod]
    public void IsVulnerablePattern_GroupByWithAvg_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT dept, AVG(salary) FROM Staff GROUP BY dept"));

    // SQLite-specific aggregate functions — absent from the T-SQL parser (see docs/sql-safety.md)
    [TestMethod]
    public void IsVulnerablePattern_GroupByWithGroupConcat_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT source, GROUP_CONCAT(quote, ', ') FROM Quotes GROUP BY source"));

    [TestMethod]
    public void IsVulnerablePattern_GroupByWithTotal_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT type, TOTAL(amount) FROM Ledger GROUP BY type"));

    [TestMethod]
    public void IsVulnerablePattern_HavingWithoutExplicitGroupBy_ReturnsTrue()
        => Assert.IsTrue(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT COUNT(*) FROM Quotes HAVING COUNT(*) > 100"));

    // ── Patterns that must pass ───────────────────────────────────────────────

    [TestMethod]
    public void IsVulnerablePattern_SimpleCountStar_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT COUNT(*) FROM Quotes"));

    [TestMethod]
    public void IsVulnerablePattern_CoalesceMax_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion"));

    [TestMethod]
    public void IsVulnerablePattern_CountWithWhere_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT COUNT(*) FROM Quotes WHERE IsDeleted = 0"));

    [TestMethod]
    public void IsVulnerablePattern_GroupByWithoutAggregate_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT Id, Name FROM Sources GROUP BY Type"));

    [TestMethod]
    public void IsVulnerablePattern_PlainSelect_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "SELECT * FROM Quotes WHERE Id = @id AND IsDeleted = 0"));

    [TestMethod]
    public void IsVulnerablePattern_EmptyString_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(string.Empty));

    [TestMethod]
    public void IsVulnerablePattern_NonSqlInput_ReturnsFalse()
        => Assert.IsFalse(SqlAggregateGuard.IsVulnerablePattern(
            "This is not SQL at all"));
}
