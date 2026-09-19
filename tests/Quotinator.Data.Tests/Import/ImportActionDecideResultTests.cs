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
        Assert.AreEqual("The following fields are ambiguous and need an explicit decision: quoteText, character",
            ImportActionDecideResult.Unresolved(id, ["quoteText", "character"]).Describe());
    }

    /// <summary>
    /// #410: the not-decidable text names the action kind, and no longer says the entity type does not
    /// support a Modify decision — untrue for a Quote Add, whose type does.
    /// </summary>
    [TestMethod]
    public void Describe_NotDecidable_NamesTheActionKind()
    {
        Guid id = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

        Assert.AreEqual($"Import action '{id}' is a 'Quote' Add action and cannot be manually decided.",
            ImportActionDecideResult.NotDecidable(id, "Quote", "Add").Describe());
    }

    /// <summary>#410: a held Add is resolved by correcting the file or adding a rule, and the text says both.</summary>
    [TestMethod]
    public void Describe_HeldForReview_NamesBothWaysToResolveIt()
    {
        Guid id = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

        Assert.AreEqual($"Import action '{id}' is held for review (current status: 'Pending') and cannot be decided: correct the imported file, or add a rule that resolves it.",
            ImportActionDecideResult.HeldForReview(id, "Pending").Describe());
    }

    [TestMethod]
    public void Describe_Decided_IsEmpty()
        => Assert.AreEqual(string.Empty, ImportActionDecideResult.Decided(Guid.NewGuid()).Describe(),
            "Control: a decided action is not an error, so it has nothing to describe");
}
