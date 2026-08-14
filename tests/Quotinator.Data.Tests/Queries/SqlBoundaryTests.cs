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
    /// correct. <c>FileResources</c> (#251) stays here for the same reason — it only touches
    /// Import_FileResource/Import_FileResourceLine/Import_FileResourceBatch and the already-Data-owned
    /// Import_Batch, never a consumer-defined entity. <c>Notifications</c> (#278) stays here too —
    /// System_Notification is operational/system content, not quote-domain content, and never
    /// references a consumer-defined entity. <c>ChangelogSchema</c>/<c>ChangelogContent</c> (#309)
    /// stay here too — the separate changelog database's own version-bookkeeping and content-refresh
    /// SQL, with no relational or transactional coupling to any consumer-defined entity (ADR 018).
    /// <c>AppVersion</c> (#81) stays here too — tracks the last app version that completed a healthy
    /// startup, pure app-instance state with no consumer-entity coupling of any kind.
    /// </summary>
    [TestMethod]
    public void Sql_ContainsOnlyGenericInfrastructureQueries()
    {
        var expected = new HashSet<string> { "Schema", "Joins", "Queries", "SystemAudit", "SystemImportActions", "SystemChangeLog", "ImportBatches", "SystemSourceFileOverrides", "FileResources", "Notifications", "ChangelogSchema", "ChangelogContent", "AppVersion" };

        var actual = typeof(Sql)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(t => t.Name)
            .ToHashSet();

        Assert.AreSequenceEqual(
            [.. expected], [.. actual], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder, "Quotinator.Data.Queries.Sql contains a nested type outside the documented generic-infrastructure " +
            "set. Domain-specific query sets (Quotes, Characters, Sources, Conversations, etc.) must live in " +
            "Quotinator.Core.Queries.Sql instead — see ADR 004 and issues #157/#206.");
    }
}
