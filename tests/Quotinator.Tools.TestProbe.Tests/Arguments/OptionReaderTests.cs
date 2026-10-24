using Quotinator.Tools.TestProbe.Arguments;

namespace Quotinator.Tools.TestProbe.Tests.Arguments;

[TestClass]
public sealed class OptionReaderTests
{
    private static readonly string[] FirstValues = ["a", "b"];
    private static readonly string[] PathAndText = ["path", "text"];

    [TestMethod]
    public void All_RepeatedOption_ReturnsEveryOccurrenceInOrder()
    {
        IReadOnlyList<string[]> found = OptionReader.All(["--present", "a", "--present", "b"], "--present", 1, out _);

        Assert.AreSequenceEqual(FirstValues, found.Select(values => values[0]));
    }

    [TestMethod]
    public void All_ArityTwo_ReturnsBothValues()
    {
        IReadOnlyList<string[]> found = OptionReader.All(["--write-text", "path", "text"], "--write-text", 2, out _);

        Assert.AreSequenceEqual(PathAndText, found[0]);
    }

    [TestMethod]
    public void All_OccurrenceMissingItsValue_ReportsIncomplete()
    {
        _ = OptionReader.All(["--present"], "--present", 1, out bool incomplete);

        Assert.IsTrue(incomplete);
    }

    [TestMethod]
    public void All_CompleteOccurrences_ReportsNothingIncomplete()
    {
        _ = OptionReader.All(["--present", "a"], "--present", 1, out bool incomplete);

        Assert.IsFalse(incomplete);
    }

    [TestMethod]
    public void All_OptionAbsent_ReturnsNothing()
    {
        IReadOnlyList<string[]> found = OptionReader.All(["--equals", "a=b"], "--present", 1, out _);

        Assert.IsEmpty(found);
    }

    [TestMethod]
    public void Single_OptionPresent_ReturnsItsValue()
    {
        Assert.AreEqual("http://x", OptionReader.Single(["--url", "http://x"], "--url"));
    }

    [TestMethod]
    public void Single_OptionAbsent_ReturnsNull()
    {
        Assert.IsNull(OptionReader.Single(["--field", "status"], "--url"));
    }

    [TestMethod]
    public void Single_OptionWithoutValue_ReturnsNull()
    {
        Assert.IsNull(OptionReader.Single(["--url"], "--url"));
    }
}
