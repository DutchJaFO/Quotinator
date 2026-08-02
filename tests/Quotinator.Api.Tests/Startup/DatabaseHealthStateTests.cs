using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

[TestClass]
public class DatabaseHealthStateTests
{
    [TestMethod]
    public void NewInstance_IsHealthyByDefault()
    {
        var state = new DatabaseHealthState();

        Assert.IsTrue(state.IsHealthy);
        Assert.IsNull(state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_SetsUnhealthyAndReason()
    {
        var state = new DatabaseHealthState();

        state.MarkFailed("schema mismatch");

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("schema mismatch", state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_CalledTwice_KeepsFirstReason()
    {
        var state = new DatabaseHealthState();

        state.MarkFailed("first failure");
        state.MarkFailed("second failure");

        Assert.AreEqual("first failure", state.FailureReason,
            "A second MarkFailed call must not overwrite the original diagnostic reason");
    }

    [TestMethod]
    public void MarkHealthy_AfterMarkFailed_RestoresHealthyStateAndClearsReason()
    {
        var state = new DatabaseHealthState();
        state.MarkFailed("schema mismatch");

        state.MarkHealthy();

        Assert.IsTrue(state.IsHealthy);
        Assert.IsNull(state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_AfterMarkHealthy_RecordsNewFailure()
    {
        var state = new DatabaseHealthState();
        state.MarkFailed("first failure");
        state.MarkHealthy();

        state.MarkFailed("second failure");

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("second failure", state.FailureReason,
            "After a genuine recovery, a fresh failure must be recorded normally, not suppressed by the earlier MarkFailed idempotency guard");
    }
}
