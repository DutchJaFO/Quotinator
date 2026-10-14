using Quotinator.Api.Enums;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

[TestClass]
public class DatabaseHealthStateTests
{
    [TestMethod]
    public void NewInstance_IsHealthyByDefault()
    {
        DatabaseHealthState state = new();

        Assert.IsTrue(state.IsHealthy);
        Assert.IsNull(state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_SetsUnhealthyAndReason()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("schema mismatch");

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("schema mismatch", state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_CalledTwice_KeepsFirstReason()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("first failure");
        state.MarkFailed("second failure");

        Assert.AreEqual("first failure", state.FailureReason,
            "A second MarkFailed call must not overwrite the original diagnostic reason");
    }

    [TestMethod]
    public void MarkHealthy_AfterMarkFailed_RestoresHealthyStateAndClearsReason()
    {
        DatabaseHealthState state = new();
        state.MarkFailed("schema mismatch");

        state.MarkHealthy();

        Assert.IsTrue(state.IsHealthy);
        Assert.IsNull(state.FailureReason);
    }

    /// <summary>
    /// #350: a schema version ahead of this build degrades the application like any other fault, and
    /// it is one flag rather than a kind. An earlier draft of that issue let this fault keep the
    /// diagnostic pages reading, on the grounds that the queries would execute; that was reversed,
    /// because a schema of unknown shape is untrusted content whether or not it can be read.
    /// </summary>
    [TestMethod]
    public void MarkFailed_SchemaAheadOfThisBuild_DegradesLikeAnyOtherFault()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("recorded schema version is ahead");

        Assert.IsFalse(state.IsHealthy, "an overshoot is a fault: the application degrades");
        Assert.AreEqual("recorded schema version is ahead", state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_AfterMarkHealthy_RecordsNewFailure()
    {
        DatabaseHealthState state = new();
        state.MarkFailed("first failure");
        state.MarkHealthy();

        state.MarkFailed("second failure");

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("second failure", state.FailureReason,
            "After a genuine recovery, a fresh failure must be recorded normally, not suppressed by the earlier MarkFailed idempotency guard");
    }
}
