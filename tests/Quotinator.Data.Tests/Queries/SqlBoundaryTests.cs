using System.Reflection;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Tests.Queries;

[TestClass]
public class SqlBoundaryTests
{
    /// <summary>
    /// Quotinator.Data must stay domain-agnostic (ADR 004) — this asserts that
    /// <see cref="Sql"/> contains only generic infrastructure query sets, never a query touching a
    /// consumer-defined entity (Quotes, Characters, Conversations, etc.), which belong in
    /// Quotinator.Core's own Sql class instead (moved there from Quotinator.Engine by #206).
    /// <c>ImportBatches</c> stays here — it never interacts with a consumer-defined entity (see
    /// ADR 004's consumer-entity-interaction test, issue #158) — after #157 briefly moved it to
    /// Engine on the mistaken assumption that its existing (also-misplaced) entity location was
    /// correct.
    /// </summary>
    [TestMethod]
    public void Sql_ContainsOnlyGenericInfrastructureQueries()
    {
        var expected = new HashSet<string> { "Schema", "Joins", "Queries", "SystemAudit", "SystemImportActions", "SystemChangeLog", "ImportBatches" };

        var actual = typeof(Sql)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(t => t.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            expected.ToList(),
            actual.ToList(),
            "Quotinator.Data.Queries.Sql contains a nested type outside the documented generic-infrastructure " +
            "set. Domain-specific query sets (Quotes, Characters, Sources, Conversations, etc.) must live in " +
            "Quotinator.Core.Queries.Sql instead — see ADR 004 and issues #157/#206.");
    }
}
