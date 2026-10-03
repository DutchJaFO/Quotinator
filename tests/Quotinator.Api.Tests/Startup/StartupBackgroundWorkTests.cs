using Microsoft.Extensions.Logging;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Api.Startup;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

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
    public TestContext TestContext { get; set; } = null!;

    private CaptureSink _sink = null!;

    /// <summary>A work runner whose log this test can read, so a swallowed failure is visible.</summary>
    private StartupBackgroundWork CreateWork()
    {
        _sink = new CaptureSink();
        Serilog.Core.Logger serilog = new LoggerConfiguration().MinimumLevel.Is(LogEventLevel.Verbose).WriteTo.Sink(_sink).CreateLogger();
        return new StartupBackgroundWork(new SerilogLoggerFactory(serilog).CreateLogger<StartupBackgroundWork>());
    }

    [TestMethod]
    public async Task StopAsync_WaitsForWorkStillRunning()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        backgroundWork.Start(() => release.Task);

        Task stopping = backgroundWork.StopAsync(CancellationToken.None);
        bool stoppedWhileWorkRan = stopping.IsCompleted;

        release.SetResult();
        await stopping;

        Assert.IsFalse(stoppedWhileWorkRan, "the host reported itself stopped while its own work was still running");
    }

    /// <summary>
    /// Startup is not finished while work it began is still running (#419). The gate that holds external
    /// writes reads this: before it, <c>MarkComplete()</c> was called the moment the work was started, so
    /// a reset arriving immediately afterwards ran against a database the what's-new write was still
    /// writing to.
    /// </summary>
    [TestMethod]
    public void WhenAllCompleted_IsNotCompleted_WhileWorkIsStillRunning()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        backgroundWork.Start(() => release.Task);

        Task waiting = backgroundWork.WhenAllCompletedAsync();

        bool completedWhileWorkRan = waiting.IsCompleted;
        release.SetResult();

        Assert.IsFalse(completedWhileWorkRan);
    }

    /// <summary>
    /// And it finishes once that work has. Pairs with
    /// <see cref="WhenAllCompleted_IsNotCompleted_WhileWorkIsStillRunning"/>: without this, that test
    /// passes against a wait that never completes at all, which would hold the gate shut for ever.
    /// </summary>
    [TestMethod]
    public async Task WhenAllCompleted_Completes_OnceTheWorkHasFinished()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        backgroundWork.Start(() => release.Task);

        Task waiting = backgroundWork.WhenAllCompletedAsync();
        release.SetResult();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.IsTrue(waiting.IsCompletedSuccessfully);
    }

    /// <summary>
    /// Work that failed does not hold the gate shut. Each piece handles its own failures and startup
    /// continues regardless, which is the existing contract: a changelog import that cannot reach its
    /// database must not stop the application from ever serving.
    /// </summary>
    [TestMethod]
    public async Task WhenAllCompleted_Completes_EvenWhenTheWorkFailed()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        backgroundWork.Start(() => Task.FromException(new InvalidOperationException("work failed")));

        Task waiting = backgroundWork.WhenAllCompletedAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.IsTrue(waiting.IsCompletedSuccessfully,
            "a failed piece of work must not fault the wait the gate reads, or startup could never complete");
    }

    /// <summary>
    /// That failure is reported, not discarded. Pairs with
    /// <see cref="WhenAllCompleted_Completes_EvenWhenTheWorkFailed"/>: on its own, that test is equally
    /// satisfied by swallowing the fault in silence, which is what this one forbids. ADR 022: every
    /// exception is logged, and a fault reaching the waiter is one its own work did not handle.
    /// </summary>
    [TestMethod]
    public async Task WhenAllCompleted_LogsWorkThatFailed()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        backgroundWork.Start(() => Task.FromException(new InvalidOperationException("work failed")));

        await backgroundWork.WhenAllCompletedAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.ContainsSingle(_sink.Events.Where(e => e.Level == LogEventLevel.Error && e.Message.Contains("startup background task failed", StringComparison.Ordinal)));
    }

    /// <summary>
    /// And work that succeeded logs nothing. Pairs with <see cref="WhenAllCompleted_LogsWorkThatFailed"/>:
    /// without it, that test passes against a waiter that logs an error for every piece of work it ever
    /// waits for, which would report a failure on every healthy start.
    /// </summary>
    [TestMethod]
    public async Task WhenAllCompleted_LogsNothing_WhenTheWorkSucceeded()
    {
        StartupBackgroundWork backgroundWork = CreateWork();
        backgroundWork.Start(() => Task.CompletedTask);

        await backgroundWork.WhenAllCompletedAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.IsEmpty(_sink.Events.Where(e => e.Level >= LogEventLevel.Error));
    }
}
