using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Startup;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// The source refresh is opted into, not out of (#424).
/// <para>
/// Proven in two halves. A key that is not configured at all cannot be produced through the test factory,
/// which pins the setting for every host by design, and <c>UseSetting(key, null)</c> stores an empty string
/// rather than nothing. So the default is proven where it is decided,
/// <see cref="SourceRefreshSettings.AutoUpdateSources"/>, against a configuration that genuinely lacks the
/// key; and the wiring from configuration to <see cref="ISourceCacheUpdater"/> through the real composition
/// root, with a spy recording what reaches it.
/// </para>
/// </summary>
[TestClass]
public class SourceRefreshDefaultTests
{
    [TestMethod]
    public void AutoUpdateSources_KeyAbsent_IsOff()
    {
        Assert.IsFalse(SourceRefreshSettings.AutoUpdateSources(new ConfigurationBuilder().Build()));
    }

    /// <summary>
    /// The positive control: an explicit <c>true</c> still reaches the updater through the real startup, so
    /// the default being off cannot be mistaken for the setting not being read at all.
    /// </summary>
    [TestMethod]
    public void Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork()
    {
        RecordingSourceCacheUpdater updater = new();

        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(SourceRefreshSettings.AutoUpdateSourcesKey, "true");
            builder.ConfigureServices(services => services.AddSingleton<ISourceCacheUpdater>(updater));
        });
        _ = factory.Services;

        Assert.IsTrue(updater.AllowNetwork);
    }

    /// <summary>Records what startup asked the source refresh to do, and refreshes nothing.</summary>
    private sealed class RecordingSourceCacheUpdater : ISourceCacheUpdater
    {
        /// <summary>The <c>allowNetwork</c> of the first resolution startup asked for; null if none.</summary>
        public bool? AllowNetwork { get; private set; }

        /// <inheritdoc/>
        public Task<SourceCacheResolution> ResolveAsync(
            IReadOnlyList<SeedBatch> candidateBatches, bool allowNetwork, bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            AllowNetwork ??= allowNetwork;
            return Task.FromResult(new SourceCacheResolution(candidateBatches, []));
        }
    }
}
