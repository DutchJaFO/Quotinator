namespace Quotinator.Logging.Tests;

/// <summary>
/// The id that ties together every line written about one exception (#397): the thrown line, a handled
/// line, or a Critical line where nothing handled it. Without a stable id per instance, a reader cannot
/// tell "thrown then handled" (expected) from "thrown, and nothing ever said what became of it".
/// </summary>
[TestClass]
public class ExceptionIdsTests
{
    /// <summary>
    /// The same instance answers with the same id however often it is asked — the property the whole
    /// scheme rests on, since the thrown line and the handled line are written from different places.
    /// </summary>
    [TestMethod]
    public void SameException_GetsTheSameId()
    {
        InvalidOperationException exception = new("thrown once, logged twice");

        string first  = ExceptionIds.For(exception);
        string second = ExceptionIds.For(exception);

        Assert.AreEqual(first, second);
        Assert.IsFalse(string.IsNullOrWhiteSpace(first), "an id must actually be assigned");
    }

    /// <summary>
    /// Two exceptions never share an id — the negative control for the test above, which a constant
    /// would otherwise satisfy just as well.
    /// </summary>
    [TestMethod]
    public void DifferentExceptions_GetDifferentIds()
    {
        string first  = ExceptionIds.For(new InvalidOperationException("one"));
        string second = ExceptionIds.For(new InvalidOperationException("two"));

        Assert.AreNotEqual(first, second);
    }
}
