using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that does not hand out a client until the app has
/// actually finished starting up (#313).
/// <para>
/// Since #280, Kestrel listens *before* startup initialisation completes, and
/// <see cref="Quotinator.Api.Middleware.StartupWaitMiddleware"/> answers every non-exempt request with
/// <c>200 OK</c> and an HTML wait page until <see cref="StartupPhaseState.MarkComplete"/> runs. The base
/// factory returns as soon as the host is built, which is earlier than that — so a test could assert
/// against the wait page instead of the endpoint it names. An intermittent red is the mild version of
/// that; the dangerous version is a test expecting <c>200</c> passing against the wait page while
/// verifying nothing at all.
/// </para>
/// <para>
/// The wait lives here, on the factory, rather than at each of the ~376 <c>CreateClient()</c> call
/// sites: <see cref="StartupPhaseState"/> is a singleton and startup completes exactly once, so polling
/// per client would be both 376 edits and pointless repetition.
/// </para>
/// </summary>
internal class QuotinatorWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc/>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Reads StartupPhaseState straight from the host's own container rather than polling
        // GET /api/v1/health: this is the exact flag StartupWaitMiddleware branches on, not a proxy for
        // it, and it needs no HTTP round trip from inside host construction. Quotinator.Api sets
        // InternalsVisibleTo for this project, so the internal type is reachable.
        var phase = host.Services.GetRequiredService<StartupPhaseState>();
        StartupReadiness.WaitUntilComplete(() => phase.IsComplete);

        return host;
    }
}

/// <summary>
/// The bounded wait behind <see cref="QuotinatorWebApplicationFactory"/>, extracted so the timeout path
/// is testable — a guard whose failure mode cannot be exercised is not a verified guard.
/// </summary>
internal static class StartupReadiness
{
    /// <summary>
    /// How long to wait for startup before failing. Generous relative to a real test host's startup
    /// (sub-second) — this exists to turn a genuinely stuck startup into a clear failure rather than a
    /// hung suite, not to paper over slow-but-working initialisation.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Blocks until <paramref name="isComplete"/> returns <see langword="true"/>, or throws
    /// <see cref="TimeoutException"/> once <paramref name="timeout"/> elapses.
    /// <para>
    /// Synchronous by necessity — <see cref="WebApplicationFactory{TEntryPoint}.CreateHost"/> is a
    /// synchronous override, and this runs on the test's own thread during factory construction, where
    /// there is no <see cref="SynchronizationContext"/> to deadlock against.
    /// </para>
    /// </summary>
    internal static void WaitUntilComplete(Func<bool> isComplete, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var effectivePoll    = pollInterval ?? DefaultPollInterval;
        var clock            = Stopwatch.StartNew();

        while (!isComplete())
        {
            if (clock.Elapsed > effectiveTimeout)
            {
                throw new TimeoutException(
                    $"Startup did not complete within {effectiveTimeout.TotalSeconds:0} seconds. The app never called " +
                    $"{nameof(StartupPhaseState)}.{nameof(StartupPhaseState.MarkComplete)}, so every non-exempt request " +
                    "would have been served the startup wait page instead of reaching its endpoint (#313).");
            }

            Thread.Sleep(effectivePoll);
        }
    }
}
