using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #367: the process-scoped record of which notifications are running their action right now. It is
/// what stops a second confirmed click starting a second full reseed, and what the Status column reads
/// to show a run in progress.
/// </summary>
[TestClass]
public class NotificationExecutionStateTests
{
    [TestMethod]
    public void TryBegin_FirstCaller_IsAdmitted()
    {
        NotificationExecutionState state = new();

        Assert.IsTrue(state.TryBegin(Guid.NewGuid()));
    }

    /// <summary>
    /// The whole point of the type. A second confirmed click during an ~11-second reseed queues on
    /// <c>SharedSeedLock</c> and then performs a second full reseed, which is the defect #367 reports.
    /// </summary>
    [TestMethod]
    public void TryBegin_WhileHeld_IsRefused()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();

        Assert.IsTrue(state.TryBegin(id));
        Assert.IsFalse(state.TryBegin(id), "A run is already in flight for this notification.");
        Assert.IsTrue(state.IsExecuting(id));
    }

    [TestMethod]
    public void TryBegin_AfterEnd_IsAdmittedAgain()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();

        state.TryBegin(id);
        state.End(id);

        Assert.IsTrue(state.TryBegin(id), "A finished run must not block the notification forever.");
        Assert.IsTrue(state.IsExecuting(id));
    }

    /// <summary>
    /// Scoped per notification, not globally: one long reseed must not make every other notification's
    /// action unavailable.
    /// </summary>
    [TestMethod]
    public void TryBegin_DifferentIds_BothAdmitted()
    {
        NotificationExecutionState state = new();
        Guid first  = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Assert.IsTrue(state.TryBegin(first));
        Assert.IsTrue(state.TryBegin(second));
        Assert.IsFalse(state.IsExecuting(Guid.NewGuid()), "An id nobody started is not executing.");
    }

    /// <summary>
    /// Two circuits can click at the same instant, so the check and the claim must be one atomic step —
    /// a read-then-write would admit both. Exactly one caller wins.
    /// </summary>
    [TestMethod]
    public void TryBegin_ConcurrentCallers_AdmitsExactlyOne()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();
        int admitted = 0;

        Parallel.For(0, 64, _ =>
        {
            if (state.TryBegin(id)) Interlocked.Increment(ref admitted);
        });

        Assert.AreEqual(1, admitted, "Only one caller may run the action.");
    }

    /// <summary>
    /// Positive control for the two refusals below: a notification nobody is running does run, reports
    /// that it ran, and is released afterwards. Without this, both negatives would pass equally well
    /// against an implementation that never runs anything.
    /// </summary>
    [TestMethod]
    public async Task RunExclusivelyAsync_FreeNotification_RunsTheActionAndReleasesIt()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();
        bool ran = false;

        bool result = await state.RunExclusivelyAsync(id, () => { ran = true; return Task.CompletedTask; });

        Assert.IsTrue(ran, "The action must actually run.");
        Assert.IsTrue(result, "A run that happened must report that it happened.");
        Assert.IsFalse(state.IsExecuting(id), "The claim must be released once the action returns.");
    }

    /// <summary>
    /// #367's second consequence: a confirmed click during an in-flight reseed must not reach the
    /// executor at all. The first run holds the claim for its whole duration, so the second call is
    /// skipped rather than queued behind it.
    /// </summary>
    [TestMethod]
    public async Task RunExclusivelyAsync_WhileRunning_DoesNotInvokeTheActionAgain()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();
        int invocations = 0;
        TaskCompletionSource release = new();

        Task first = state.RunExclusivelyAsync(id, async () =>
        {
            Interlocked.Increment(ref invocations);
            await release.Task;
        });

        bool second = await state.RunExclusivelyAsync(id, () =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        release.SetResult();
        await first;

        Assert.IsFalse(second, "The second call must report that it did not run.");
        Assert.AreEqual(1, invocations, "The executor must be reached exactly once.");
    }

    /// <summary>
    /// A failing action releases its claim. Without this a single error would strand the notification
    /// as permanently executing for the life of the process — no Run control, no way back.
    /// </summary>
    [TestMethod]
    public async Task RunExclusivelyAsync_ActionThrows_StillReleasesTheClaim()
    {
        NotificationExecutionState state = new();
        Guid id = Guid.NewGuid();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            state.RunExclusivelyAsync(id, () => throw new InvalidOperationException("boom")));

        Assert.IsFalse(state.IsExecuting(id));
        Assert.IsTrue(state.TryBegin(id), "A failed run must not block the notification forever.");
    }
}
