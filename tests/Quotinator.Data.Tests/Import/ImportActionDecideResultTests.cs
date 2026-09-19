using Quotinator.Data.Import;

namespace Quotinator.Data.Tests.Import;

/// <summary>
/// <see cref="ImportActionDecideResult.Describe"/> carries the text a bulk-decide row error showed while
/// each outcome was still an exception, so replacing the throws changes nothing a caller reads.
/// </summary>
[TestClass]
public class ImportActionDecideResultTests
{
    [TestMethod]
    [DataRow("Source")]
    [DataRow("Character")]
    public void Describe_NotDecidable_DoesNotNameASpecificEntityType(string entityType)
    {
        Guid actionId = Guid.NewGuid();

        string description = ImportActionDecideResult.NotDecidable(actionId, entityType, "Add").Describe();

        Assert.IsFalse(description.Contains("Quote", StringComparison.OrdinalIgnoreCase),
            "The text must describe the rule generically, not name a specific entity type as the one exception");
        Assert.Contains(entityType, description,
            "The text must report the entity type passed in, not a hardcoded one");
        Assert.Contains(actionId.ToString(), description, "The text must report the action id passed in");
    }

    [TestMethod]
    public void Describe_EachOutcome_MatchesTheMessageItReplaces()
    {
        Guid id = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

        Assert.AreEqual($"Import action '{id}' does not exist.",
            ImportActionDecideResult.NotFound(id).Describe());
        Assert.AreEqual($"Import action '{id}' is not in a valid state for this operation (current status: 'Applied').",
            ImportActionDecideResult.AlreadyResolved(id, "Applied").Describe());
        Assert.AreEqual($"Import action '{id}' is a 'Source' action and cannot be manually decided — this action's entity type does not currently support a Modify decision.",
            ImportActionDecideResult.NotDecidable(id, "Source", "Add").Describe());
        Assert.AreEqual("The following fields are ambiguous and need an explicit decision: quoteText, character",
            ImportActionDecideResult.Unresolved(id, ["quoteText", "character"]).Describe());
    }

    [TestMethod]
    public void Describe_Decided_IsEmpty()
        => Assert.AreEqual(string.Empty, ImportActionDecideResult.Decided(Guid.NewGuid()).Describe(),
            "Control: a decided action is not an error, so it has nothing to describe");
}
