using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// <see cref="StartupBackgroundWork"/>: the work startup begins in the background is waited for when the
/// host stops (#424).
/// <para>
/// Until this, <c>Program.cs</c> started that work fire-and-forget. A host stopped while it ran reported
/// itself stopped with a query still open, and the work carried on against a container that had already
/// been disposed: found 2026-09-27, when a test host's data directory could not be removed because the
/// changelog database was still in use.
/// </para>
/// </summary>
[TestClass]
public class StartupBackgroundWorkTests
{
    [TestMethod]
    public async Task StopAsync_WaitsForWorkStillRunning()
    {
        StartupBackgroundWork backgroundWork = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        backgroundWork.Start(() => release.Task);

        Task stopping = backgroundWork.StopAsync(CancellationToken.None);
        bool stoppedWhileWorkRan = stopping.IsCompleted;

        release.SetResult();
        await stopping;

        Assert.IsFalse(stoppedWhileWorkRan, "the host reported itself stopped while its own work was still running");
    }
}
