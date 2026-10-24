using Quotinator.Tools.TestProbe.Waiting;

namespace Quotinator.Tools.TestProbe.Tests.Waiting;

[TestClass]
public sealed class FieldWaiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Wait_FieldAlreadyHoldsAValue_IsReached()
    {
        FieldWaitResult result = Run(["""{"status":"unhealthy"}"""], TimeSpan.FromSeconds(10));

        Assert.IsTrue(result.Reached);
    }

    [TestMethod]
    public void Wait_FieldReachesAValueLater_IsReached()
    {
        FieldWaitResult result = Run([null, """{"status":"starting"}""", """{"status":"healthy"}"""], TimeSpan.FromSeconds(10));

        Assert.IsTrue(result.Reached);
    }

    [TestMethod]
    public void Wait_Reached_ReturnsTheBodyThatMatched()
    {
        FieldWaitResult result = Run(["""{"status":"starting"}""", """{"status":"healthy","reason":null}"""], TimeSpan.FromSeconds(10));

        Assert.AreEqual("""{"status":"healthy","reason":null}""", result.Body);
    }

    [TestMethod]
    public void Wait_BodyThatIsNotJson_IsReadAgain()
    {
        FieldWaitResult result = Run(["<html>", """{"status":"healthy"}"""], TimeSpan.FromSeconds(10));

        Assert.IsTrue(result.Reached);
    }

    [TestMethod]
    public void Wait_FieldNeverHoldsAValue_IsNotReached()
    {
        FieldWaitResult result = Run(["""{"status":"starting"}"""], TimeSpan.FromSeconds(3));

        Assert.IsFalse(result.Reached);
    }

    [TestMethod]
    public void Wait_NotReached_ReportsTheLastValue()
    {
        FieldWaitResult result = Run(["""{"status":"starting"}"""], TimeSpan.FromSeconds(3));

        Assert.AreEqual("starting", result.LastValue);
    }

    [TestMethod]
    public void Wait_NothingEverAnswers_HasNoBody()
    {
        FieldWaitResult result = Run([null], TimeSpan.FromSeconds(3));

        Assert.IsNull(result.Body);
    }

    [TestMethod]
    public void Wait_ValueComparedExactly_DifferentCasingIsNotReached()
    {
        FieldWaitResult result = Run(["""{"status":"Healthy"}"""], TimeSpan.FromSeconds(3));

        Assert.IsFalse(result.Reached);
    }

    /// <summary>
    /// Serves <paramref name="bodies"/> in order, repeating the last, with a clock that advances one
    /// second per pause, so a timeout is reached after a known number of reads with no real waiting.
    /// </summary>
    private static FieldWaitResult Run(string?[] bodies, TimeSpan timeout)
    {
        int read = 0;
        DateTimeOffset clock = Start;

        return FieldWaiter.Wait(
            () => bodies[Math.Min(read++, bodies.Length - 1)],
            "status",
            ["healthy", "unhealthy"],
            timeout,
            () => clock,
            () => clock = clock.AddSeconds(1));
    }
}
