using Microsoft.Extensions.Hosting;
using Quotinator.Api.Logging;
using Microsoft.Extensions.Logging;

namespace Quotinator.Api.Startup;

/// <summary>
/// Runs the work startup begins in the background, and makes the host wait for it when it stops (#424).
/// <para>
/// Some startup work runs detached so it does not hold back the moment the application can serve (the
/// changelog import, the what's-new notification). Started with a bare <c>Task.Run</c>, nothing waited
/// for it: a host stopped while it ran reported itself stopped with a query still open, and the work
/// carried on against a container that had already been disposed. Found 2026-09-27, when a test host's
/// data directory could not be removed because the changelog database was still in use.
/// </para>
/// <para>
/// Registered as a hosted service, so the host's own shutdown awaits <see cref="StopAsync"/> before it
/// reports stopped and disposes its services: every connection the work opened is closed by then, by the
/// work itself. The wait is bounded by the host's shutdown timeout, which is what the token passed to
/// <see cref="StopAsync"/> carries.
/// </para>
/// </summary>
internal sealed class StartupBackgroundWork(ILogger<StartupBackgroundWork> logger) : IHostedService
{
    private readonly List<Task> _running = [];
    private readonly Lock _gate = new();
    private readonly ILogger<StartupBackgroundWork> _logger = logger;

    /// <summary>Starts <paramref name="work"/> in the background, and keeps it to be waited for.</summary>
    /// <param name="work">The work to run. It handles its own failures; this only waits for it.</param>
    public void Start(Func<Task> work)
    {
        Task running = Task.Run(work);
        lock (_gate)
            _running.Add(running);
    }

    /// <summary>
    /// Completes once every piece of work started here has, whether it succeeded or failed (#419).
    /// <para>
    /// Startup is not finished while work it began is still running, and the gate that holds external
    /// writes reads that. Before this, <c>MarkComplete()</c> ran the moment the work was started, so a
    /// reset arriving immediately after could rebuild the database while the what's-new write was still
    /// pointing at a version row it was about to remove.
    /// </para>
    /// <para>
    /// A failure never holds the gate shut: this waits for each piece to finish or to fail, not to
    /// succeed. Each piece handles and logs its own failures and startup continues regardless, which is
    /// the existing contract: a changelog import that cannot reach its database must not stop the
    /// application from ever serving.
    /// </para>
    /// <para>
    /// A fault that still reaches here escaped that handling, so it is logged rather than discarded, per
    /// ADR 022. Swallowing it silently would leave the one case nobody anticipated invisible, which is
    /// the opposite of what waiting for a result is for.
    /// </para>
    /// </summary>
    public Task WhenAllCompletedAsync()
    {
        Task[] running;
        lock (_gate)
            running = [.. _running];

        // WhenAll faults as soon as any one of them does; this waits for all of them either way.
        return Task.WhenAll(running.Select(ReportFaults));

        async Task ReportFaults(Task work)
        {
            try
            {
                await work;
            }
            catch (Exception ex)
            {
                _logger.LogStartupBackgroundWorkFailed(ex);
            }
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Completes once every piece of work started here has.</summary>
    /// <param name="cancellationToken">Signalled when the host's shutdown timeout elapses.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] running;
        lock (_gate)
            running = [.. _running];

        return Task.WhenAll(running).WaitAsync(cancellationToken);
    }
}
