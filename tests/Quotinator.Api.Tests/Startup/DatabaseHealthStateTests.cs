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

        state.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("schema mismatch", state.FailureReason);
    }

    [TestMethod]
    public void MarkFailed_CalledTwice_KeepsFirstReason()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("first failure", DatabaseFaultKind.ExpectedSchemaMissing);
        state.MarkFailed("second failure", DatabaseFaultKind.ExpectedSchemaMissing);

        Assert.AreEqual("first failure", state.FailureReason,
            "A second MarkFailed call must not overwrite the original diagnostic reason");
    }

    [TestMethod]
    public void MarkHealthy_AfterMarkFailed_RestoresHealthyStateAndClearsReason()
    {
        DatabaseHealthState state = new();
        state.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);

        state.MarkHealthy();

        Assert.IsTrue(state.IsHealthy);
        Assert.IsNull(state.FailureReason);
    }

    /// <summary>
    /// #350: an overshot database opens and queries perfectly well. Everything this build expects of
    /// the schema is there, plus changes it does not know about, so the diagnostic surfaces an
    /// operator reads while degraded can still run their own queries against it.
    /// </summary>
    [TestMethod]
    public void MarkFailed_SchemaAheadOfThisBuild_LeavesTheExpectedSchemaPresent()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("recorded schema version is ahead", DatabaseFaultKind.SchemaAheadOfThisBuild);

        Assert.IsFalse(state.IsHealthy, "an overshoot is a fault: the application degrades");
        Assert.IsTrue(
            state.ExpectedSchemaIsPresent,
            "a query written against this build's schema can execute here, so suppressing every live "
            + "read would hide the notification that explains the degraded state");
    }

    /// <summary>
    /// The control for the test above: every fault that existed before #350 leaves the schema this
    /// build expects unguaranteed, so no live read may be attempted.
    /// </summary>
    [TestMethod]
    public void MarkFailed_ExpectedSchemaMissing_MarksItAbsent()
    {
        DatabaseHealthState state = new();

        state.MarkFailed("data directory is not writable", DatabaseFaultKind.ExpectedSchemaMissing);

        Assert.IsFalse(state.ExpectedSchemaIsPresent);
    }

    [TestMethod]
    public void MarkFailed_AfterMarkHealthy_RecordsNewFailure()
    {
        DatabaseHealthState state = new();
        state.MarkFailed("first failure", DatabaseFaultKind.ExpectedSchemaMissing);
        state.MarkHealthy();

        state.MarkFailed("second failure", DatabaseFaultKind.ExpectedSchemaMissing);

        Assert.IsFalse(state.IsHealthy);
        Assert.AreEqual("second failure", state.FailureReason,
            "After a genuine recovery, a fresh failure must be recorded normally, not suppressed by the earlier MarkFailed idempotency guard");
    }
}
