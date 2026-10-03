using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Startup;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Api.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that does not hand out a client until the app has
/// actually finished starting up (#313).
/// <para>
/// Since #280, Kestrel listens *before* startup initialisation completes, and
/// <see cref="Quotinator.Api.Middleware.StartupWaitMiddleware"/> answers every non-exempt request with
/// <c>200 OK</c> and an HTML wait page until <see cref="StartupPhaseState.MarkComplete"/> runs. The base
/// factory returns as soon as the host is built, which is earlier than that, so a test could assert
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
/// <param name="preparedDatabase">
/// Whether the host starts from a copy of <see cref="PreparedDatabase"/> rather than an empty data
/// directory (#424): for a test whose host replaces the database initializer and still needs the schema.
/// </param>
internal sealed class QuotinatorWebApplicationFactory(bool preparedDatabase = false) : WebApplicationFactory<Program>
{
    /// <summary>
    /// Marks every host this factory builds, so <see cref="UnguardedFactoryRuntimeGuard"/> can tell it from a
    /// host built by a factory that never waited. <c>WithWebHostBuilder</c> runs this too, before its own
    /// configuration, so a configured copy is marked as well.
    /// <para>
    /// Every host is also self-contained (#424): a test depends on nothing outside the project unless that
    /// is what it tests (<c>docs/testing-policy.md</c>). So the source refresh is off (left on, startup
    /// fetched the bundled sources from GitHub whenever the cached copy was a day old), the bundled sources
    /// are off, and the data directory is a new temporary one, removed when this factory is
    /// disposed, instead of the build output's own folder, which kept every run's database, backups and
    /// key ring for the next. A
    /// test whose subject needs one of these otherwise sets it with its own <c>UseSetting</c>, which runs
    /// after this.
    /// </para>
    /// </summary>
    /// <param name="builder">The host's web builder.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TempDirectory dataDirectory = new("quotinator-apitest-");

        if (preparedDatabase)
        {
            File.Copy(PreparedDatabase.FilePath, Path.Combine(dataDirectory.Path, Quotinator.Data.Paths.DataPaths.DatabaseFile));
        }

        builder.UseSetting("Quotinator:AutoUpdateSources", "false");
        builder.UseSetting("Quotinator:IncludeDefaultSources", "false");
        builder.UseSetting("Quotinator:DataDir", dataDirectory.Path);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<GuardedFactoryMarker>();
            services.AddSingleton<IHostedService>(provider =>
                new DataDirectoryRemoval(dataDirectory, provider.GetRequiredService<IHostApplicationLifetime>()));
        });
    }

    /// <inheritdoc/>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        // Reads StartupPhaseState straight from the host's own container rather than polling
        // GET /api/v1/health: this is the exact flag StartupWaitMiddleware branches on, not a proxy for
        // it, and it needs no HTTP round trip from inside host construction. Quotinator.Api sets
        // InternalsVisibleTo for this project, so the internal type is reachable.
        StartupPhaseState phase = host.Services.GetRequiredService<StartupPhaseState>();
        StartupReadiness.WaitUntilComplete(() => phase.IsComplete);

        return host;
    }

    /// <summary>
    /// Deletes a host's temporary data directory once that host has stopped (#424).
    /// <para>
    /// Tied to the host rather than to the factory, deliberately: a test usually disposes the factory
    /// <c>WithWebHostBuilder</c> returned, not the one it constructed, so a factory-level cleanup would
    /// leak. <c>ApplicationStopped</c> fires after every hosted service has stopped.
    /// </para>
    /// <para>
    /// The deletion itself belongs to <see cref="TempDirectory"/> (#419), so this project and
    /// <c>Quotinator.Data.Tests</c> share one implementation of "clear the pools, then delete", and a
    /// deletion that cannot happen is reported instead of vanishing. It used to be written out here and
    /// to throw into a lifetime event that swallowed it, which is how a directory outlived its host with
    /// nothing to say so.
    /// </para>
    /// </summary>
    /// <param name="dataDirectory">The directory this host was given, and will delete.</param>
    /// <param name="lifetime">The host's lifetime, whose stop the removal waits for.</param>
    private sealed class DataDirectoryRemoval(TempDirectory dataDirectory, IHostApplicationLifetime lifetime) : IHostedService
    {
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.ApplicationStopped.Register(dataDirectory.Dispose);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
internal static class StartupReadiness
{
    /// <summary>
    /// How long to wait for startup before failing. Generous relative to a real test host's startup
    /// (sub-second): this exists to turn a genuinely stuck startup into a clear failure rather than a
    /// hung suite, not to paper over slow-but-working initialisation.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Blocks until <paramref name="isComplete"/> returns <see langword="true"/>, or throws
    /// <see cref="TimeoutException"/> once <paramref name="timeout"/> elapses.
    /// <para>
    /// Synchronous by necessity: <see cref="WebApplicationFactory{TEntryPoint}.CreateHost"/> is a
    /// synchronous override, and this runs on the test's own thread during factory construction, where
    /// there is no <see cref="SynchronizationContext"/> to deadlock against.
    /// </para>
    /// </summary>
    internal static void WaitUntilComplete(Func<bool> isComplete, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        TimeSpan  effectiveTimeout = timeout ?? DefaultTimeout;
        TimeSpan  effectivePoll    = pollInterval ?? DefaultPollInterval;
        Stopwatch clock            = Stopwatch.StartNew();

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
