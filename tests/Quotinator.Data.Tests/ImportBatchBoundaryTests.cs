using Quotinator.Data.Models;

namespace Quotinator.Data.Tests;

[TestClass]
public class ImportBatchBoundaryTests
{
    /// <summary>
    /// ImportBatch bookkeeping never interacts with a consumer-defined entity (Quote, Source,
    /// Character, Person, Conversation, etc.) — the same category of generic import/seed
    /// infrastructure as the already-correctly-placed <c>SeedBatch</c>/<c>ManifestPolicy</c>. ADR 004
    /// names <c>ImportBatchType</c> and <c>IImportBatchRepository</c> explicitly as Data examples.
    /// This asserts the entity, its two enums, and its repository contract all live in the
    /// <c>Quotinator.Data</c> assembly, not the domain-consumer project (<c>Quotinator.Core</c> since
    /// #206's merge; was <c>Quotinator.Engine</c> before it) — see issue #158.
    /// </summary>
    [TestMethod]
    public void ImportBatch_And_Repository_LiveInQuotinatorData()
    {
        var dataAssembly = typeof(RecordBase).Assembly;

        foreach (var typeName in new[] { "ImportBatch", "ImportBatchType", "ImportBatchStatus", "IImportBatchRepository" })
        {
            var found = dataAssembly.GetTypes().Any(t => t.Name == typeName);
            Assert.IsTrue(found, $"'{typeName}' was not found in the Quotinator.Data assembly. " +
                "ADR 004 (see the consumer-entity-interaction test) requires ImportBatch bookkeeping " +
                "to live in Quotinator.Data, not the domain-consumer project.");
        }
    }
}
