using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

[TestClass]
public class StartupUxStateTests
{
    [TestMethod]
    public void NewInstance_SummaryNotDismissedByDefault()
    {
        var state = new StartupUxState();

        Assert.IsFalse(state.SummaryDismissed);
    }

    [TestMethod]
    public void Dismiss_SetsSummaryDismissed()
    {
        var state = new StartupUxState();

        state.Dismiss();

        Assert.IsTrue(state.SummaryDismissed);
    }

    [TestMethod]
    public void Dismiss_CalledTwice_StaysDismissed()
    {
        var state = new StartupUxState();

        state.Dismiss();
        state.Dismiss();

        Assert.IsTrue(state.SummaryDismissed);
    }
}
