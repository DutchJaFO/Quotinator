using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Services;

[TestClass]
public class ImportActionNotDecidableExceptionTests
{
    [TestMethod]
    [DataRow("Source")]
    [DataRow("Character")]
    public void ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType(string entityType)
    {
        var actionId = Guid.NewGuid();
        var exception = new ImportActionNotDecidableException(actionId, entityType);

        Assert.IsFalse(exception.Message.Contains("Quote", StringComparison.OrdinalIgnoreCase),
            "The message must describe the rule generically, not name a specific entity type as the one exception");
        StringAssert.Contains(exception.Message, entityType,
            "The message must actually report the entity type that was passed in, not a hardcoded one — proves the parameter is genuinely used, not just absent from a static string");
        StringAssert.Contains(exception.Message, actionId.ToString(),
            "The message must report the actual action id that was passed in");
        Assert.AreEqual(actionId, exception.ActionId);
        Assert.AreEqual(entityType, exception.EntityType);
    }
}
