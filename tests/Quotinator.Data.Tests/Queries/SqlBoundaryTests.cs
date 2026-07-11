using System.Reflection;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Tests.Queries;

[TestClass]
public class SqlBoundaryTests
{
    /// <summary>
    /// Quotinator.Data must stay domain-agnostic (ADR 004) — this asserts that
    /// <see cref="Sql"/> contains only generic infrastructure query sets, never a
    /// Quotinator-domain table (Quotes, Characters, Conversations, etc.), which belong
    /// in Quotinator.Engine's own Sql class instead.
    /// </summary>
    [TestMethod]
    public void Sql_ContainsOnlyGenericInfrastructureQueries()
    {
        var expected = new HashSet<string> { "Schema", "Joins", "Queries", "SystemAudit", "SystemImportActions", "SystemChangeLog" };

        var actual = typeof(Sql)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(t => t.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            expected.ToList(),
            actual.ToList(),
            "Quotinator.Data.Queries.Sql contains a nested type outside the documented generic-infrastructure " +
            "set. Domain-specific query sets (Quotes, Characters, Sources, Conversations, etc.) must live in " +
            "Quotinator.Engine.Queries.Sql instead — see ADR 004 and issue #157.");
    }
}
