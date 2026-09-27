using Quotinator.Data.Helpers;

namespace Quotinator.Data.Tests.Helpers;

/// <summary><see cref="ByteSize"/> (#348): sizes a person can read, in the backup quota warning.</summary>
[TestClass]
public class ByteSizeTests
{
    [TestMethod]
    public void Format_BelowOneGigabyte_IsInMegabytes()
    {
        Assert.AreEqual("921.6 MB", ByteSize.Format(1_073_741_824L * 90 / 100));
    }

    [TestMethod]
    public void Format_FromOneGigabyte_IsInGigabytes()
    {
        Assert.AreEqual("1.00 GB", ByteSize.Format(1_073_741_824L));
    }
}
