using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Services;

[TestClass]
public class ImportActionNotDecidableExceptionTests
{
    [TestMethod]
    public void ImportActionNotDecidableException_Message_DoesNotNameASpecificEntityType()
    {
        var exception = new ImportActionNotDecidableException(Guid.NewGuid(), "Source");

        Assert.IsFalse(exception.Message.Contains("Quote", StringComparison.OrdinalIgnoreCase),
            "The message must describe the rule generically, not name a specific entity type as the one exception");
    }
}
