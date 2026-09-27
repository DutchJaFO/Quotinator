using Microsoft.Extensions.Hosting;

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
internal sealed class StartupBackgroundWork : IHostedService
{
    private readonly List<Task> _running = [];
    private readonly Lock _gate = new();

    /// <summary>Starts <paramref name="work"/> in the background, and keeps it to be waited for.</summary>
    /// <param name="work">The work to run. It handles its own failures; this only waits for it.</param>
    public void Start(Func<Task> work)
    {
        Task running = Task.Run(work);
        lock (_gate)
            _running.Add(running);
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
