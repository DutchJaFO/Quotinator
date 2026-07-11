using System.Reflection;
using Quotinator.Data.Diagnostics;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Tests.Security;

[TestClass]
public class SqlQueryGuardTests
{
    /// <summary>
    /// Reflects over every string constant in <see cref="Sql"/> and its nested classes,
    /// and asserts that none match the CVE-2025-6965 vulnerable aggregate pattern
    /// (aggregate function + GROUP BY or HAVING).
    /// Adding a new constant to <see cref="Sql"/> automatically adds it to this test.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AllNamedSqlConstants))]
    public void SqlConstant_PassesAggregateGuard(string name, string sql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(sql),
            $"Sql.{name} contains a vulnerable aggregate pattern (aggregate + GROUP BY/HAVING). " +
            "Review the query and consult docs/sql-safety.md before suppressing.");
    }

    /// <summary>
    /// Verifies that every dynamically-assembled query produced by the generic-infrastructure
    /// factory methods (<c>SystemAudit</c>, <c>SystemImportActions</c>, <c>Queries</c>) passes the
    /// aggregate guard for all filter-flag combinations.
    /// Quotinator-domain factory methods (<c>Quotes</c>, etc.) are covered in
    /// <c>Quotinator.Engine.Tests.Security.SqlQueryGuardTests</c>.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AssembledQueryCases))]
    public void AssembledQuery_PassesAggregateGuard(string label, string fullSql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(fullSql),
            $"Assembled query '{label}' contains a vulnerable aggregate pattern. " +
            "Review the factory method and consult docs/sql-safety.md.");
    }

    /// <summary>
    /// Asserts that the set of SQL constants containing aggregate functions exactly matches the
    /// documented inventory. If a new aggregate query is added, this test fails — update the list
    /// and confirm the query has been reviewed against docs/sql-safety.md.
    /// </summary>
    [TestMethod]
    public void AggregateQueries_MatchDocumentedInventory()
    {
        // These are the only SQL constants permitted to contain aggregate function calls.
        // All were reviewed when the CVE-2025-6965 guard was implemented (2026-06-19).
        // None use GROUP BY or HAVING, so none trigger the vulnerability.
        var documented = new HashSet<string>
        {
            "Schema.GetDataCurrentVersion",       // COALESCE(MAX(...))
            "Schema.GetConsumerCurrentVersion",   // COALESCE(MAX(...))
            "Schema.LegacySchemaVersionExists",   // COUNT(*) — one-time bootstrap legacy-table detection (#141 amendment)
            "Schema.AnyTableExists",              // COUNT(*) — fresh-database detection for the baseline path (#143)
            "SystemAudit.CountPagedBase",         // COUNT(*) — private base for CountPaged factory
            "SystemImportActions.CountPagedBase", // COUNT(*) — private base for CountPaged factory
        };

        var actual = EnumerateSqlConstants()
            .Where(x => SqlAggregateGuard.HasAggregateFunction(x.Sql))
            .Select(x => x.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            documented.ToList(),
            actual.ToList(),
            "The set of SQL constants containing aggregate functions has changed. " +
            "Review any new or removed aggregate queries against docs/sql-safety.md " +
            "and update the documented list in this test.");
    }

    public static IEnumerable<object[]> AssembledQueryCases()
    {
        // Sql.Queries factory methods — one case per method.
        yield return ["Queries.WidgetWithOwner()", Sql.Queries.WidgetWithOwner()];

        // SystemAudit.SelectPaged/CountPaged — one case per filter-flag combination.
        foreach (var (filterTable, filterRecordId) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            yield return [$"SystemAudit.SelectPaged({filterTable},{filterRecordId})", Sql.SystemAudit.SelectPaged(filterTable, filterRecordId)];
            yield return [$"SystemAudit.CountPaged({filterTable},{filterRecordId})", Sql.SystemAudit.CountPaged(filterTable, filterRecordId)];
        }

        // SystemImportActions.SelectPaged/CountPaged — one case per filter-flag combination.
        foreach (var (filterBatchId, filterStatus, filterEntityType) in new[]
        {
            (false, false, false), (true, false, false), (false, true, false), (false, false, true),
            (true, true, false), (true, false, true), (false, true, true), (true, true, true),
        })
        {
            yield return [$"SystemImportActions.SelectPaged({filterBatchId},{filterStatus},{filterEntityType})", Sql.SystemImportActions.SelectPaged(filterBatchId, filterStatus, filterEntityType)];
            yield return [$"SystemImportActions.CountPaged({filterBatchId},{filterStatus},{filterEntityType})", Sql.SystemImportActions.CountPaged(filterBatchId, filterStatus, filterEntityType)];
        }
    }

    /// <summary>
    /// Asserts that <c>Sql.Joins.Inner</c> and <c>Sql.Joins.Left</c> bracket-quote all identifiers
    /// in their output — table name, alias, and column references all wrapped in <c>[…]</c>.
    /// </summary>
    [TestMethod]
    public void SqlJoins_Inner_OutputIsBracketQuoted()
    {
        var sql = Sql.Joins.Inner("Owners", "o", "w", "OwnerId", "Id");
        StringAssert.Contains(sql, "[Owners]",  "Table name must be bracket-quoted");
        StringAssert.Contains(sql, "[o]",       "Right alias must be bracket-quoted");
        StringAssert.Contains(sql, "[w]",       "Left alias must be bracket-quoted");
        StringAssert.Contains(sql, "[OwnerId]", "Left key must be bracket-quoted");
        StringAssert.Contains(sql, "[Id]",      "Right key must be bracket-quoted");
        StringAssert.Contains(sql, "INNER JOIN", "Fragment must be an INNER JOIN");
    }

    [TestMethod]
    public void SqlJoins_Left_OutputIsBracketQuoted()
    {
        var sql = Sql.Joins.Left("Owners", "o", "w", "OwnerId", "Id");
        StringAssert.Contains(sql, "[Owners]",  "Table name must be bracket-quoted");
        StringAssert.Contains(sql, "[o]",       "Right alias must be bracket-quoted");
        StringAssert.Contains(sql, "[w]",       "Left alias must be bracket-quoted");
        StringAssert.Contains(sql, "[OwnerId]", "Left key must be bracket-quoted");
        StringAssert.Contains(sql, "[Id]",      "Right key must be bracket-quoted");
        StringAssert.Contains(sql, "LEFT JOIN", "Fragment must be a LEFT JOIN");
    }

    /// <summary>
    /// Discovers all concrete <see cref="IJoinStrategy{TResult}"/> implementations in <c>Quotinator.Data</c>
    /// via reflection, calls <see cref="IJoinStrategy{TResult}.BuildSql"/> on each, and asserts the result
    /// passes the aggregate vulnerability guard. Adding a new strategy class automatically adds it to this test.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AllJoinStrategyBuildSqlCases))]
    public void AllJoinStrategies_BuildSql_PassesAggregateGuard(string typeName, string sql)
    {
        Assert.IsFalse(
            SqlAggregateGuard.IsVulnerablePattern(sql),
            $"{typeName}.BuildSql() contains a vulnerable aggregate pattern. " +
            "Review the strategy and consult docs/sql-safety.md before suppressing.");
    }

    public static IEnumerable<object[]> AllJoinStrategyBuildSqlCases()
    {
        var joinStrategyType = typeof(IJoinStrategy<>);
        return typeof(IJoinStrategy<>).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == joinStrategyType))
            .Select(t =>
            {
                var instance = Activator.CreateInstance(t)!;
                var sql      = (string)t.GetMethod("BuildSql")!.Invoke(instance, null)!;
                return new object[] { t.Name, sql };
            });
    }

    /// <summary>Enumerates all string constants in <see cref="Sql"/> and its nested classes.</summary>
    public static IEnumerable<object[]> AllNamedSqlConstants()
        => EnumerateSqlConstants().Select(x => new object[] { x.Name, x.Sql });

    private static IEnumerable<(string Name, string Sql)> EnumerateSqlConstants()
        => typeof(Sql)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
            .SelectMany(t => t
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => ($"{t.Name}.{f.Name}", (string)f.GetValue(null)!)));
}
