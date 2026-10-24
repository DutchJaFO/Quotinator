using Quotinator.Tools.TestProbe.Logs;

namespace Quotinator.Tools.TestProbe.Tests.Logs;

[TestClass]
public sealed class LogCounterTests
{
    [TestMethod]
    public void Count_CountsOccurrencesNotLines()
        => Assert.AreEqual(3, LogCounter.Count("ERR ERR\nok\nERR", "ERR"));

    [TestMethod]
    public void Count_PatternIsARegularExpression()
        => Assert.AreEqual(2, LogCounter.Count("id=4a id=7f id=", @"id=[0-9a-f]+"));

    [TestMethod]
    public void Count_NoOccurrence_IsZero()
        => Assert.AreEqual(0, LogCounter.Count("all fine", "ERR"));

    [TestMethod]
    public void Check_AtLeastMet_Holds()
        => Assert.IsTrue(LogCounter.Check(2, atLeast: 1, exactly: null).Holds);

    [TestMethod]
    public void Check_AtLeastNotMet_Fails()
        => Assert.IsFalse(LogCounter.Check(0, atLeast: 1, exactly: null).Holds);

    [TestMethod]
    public void Check_ExactlyMet_Holds()
        => Assert.IsTrue(LogCounter.Check(1, atLeast: null, exactly: 1).Holds);

    [TestMethod]
    public void Check_ExactlyExceeded_Fails()
        => Assert.IsFalse(LogCounter.Check(2, atLeast: null, exactly: 1).Holds);

    [TestMethod]
    public void Check_NeitherBound_Fails()
        => Assert.IsFalse(LogCounter.Check(1, atLeast: null, exactly: null).Holds);

    [TestMethod]
    public void Check_BothBounds_Fails()
        => Assert.IsFalse(LogCounter.Check(1, atLeast: 1, exactly: 1).Holds);

    [TestMethod]
    public void Check_ObservesTheCount()
        => Assert.Contains(line => line.Contains('7'), LogCounter.Check(7, atLeast: 1, exactly: null).Observations);
}
