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

    private const string Timestamped =
        "2026-10-24T19:51:50.1Z first\n" +
        "2026-10-24T19:51:58.123456789Z second ERR\n" +
        "2026-10-24T19:51:58.2Z third ERR\n";

    [TestMethod]
    public void LastTimestamp_ReturnsTheNewestLineStamp()
        => Assert.AreEqual(DateTimeOffset.Parse("2026-10-24T19:51:58.2Z"), LogCounter.LastTimestamp(Timestamped));

    [TestMethod]
    public void LastTimestamp_EmptyLog_IsMinValue()
        => Assert.AreEqual(DateTimeOffset.MinValue, LogCounter.LastTimestamp(string.Empty));

    [TestMethod]
    public void LinesAfter_KeepsOnlyLaterLines()
        => Assert.AreEqual(1, LogCounter.Count(LogCounter.LinesAfter(Timestamped, DateTimeOffset.Parse("2026-10-24T19:51:58.15Z")), "ERR"));

    [TestMethod]
    public void LinesAfter_ExcludesTheLineAtTheMark()
        => Assert.AreEqual(0, LogCounter.Count(LogCounter.LinesAfter(Timestamped, DateTimeOffset.Parse("2026-10-24T19:51:58.2Z")), "ERR"));

    [TestMethod]
    public void LinesAfter_MinValue_KeepsEveryLine()
        => Assert.AreEqual(2, LogCounter.Count(LogCounter.LinesAfter(Timestamped, DateTimeOffset.MinValue), "ERR"));

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
