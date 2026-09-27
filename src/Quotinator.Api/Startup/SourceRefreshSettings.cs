using Microsoft.Extensions.Configuration;
using Quotinator.Data.Import;

namespace Quotinator.Api.Startup;

/// <summary>
/// How the source refresh's switch is read from configuration (#424), in one place so the rule for a key
/// that is not configured at all can be tested directly.
/// </summary>
internal static class SourceRefreshSettings
{
    /// <summary>The configuration key that turns the source refresh on.</summary>
    internal const string AutoUpdateSourcesKey = "Quotinator:AutoUpdateSources";

    /// <summary>
    /// Whether the source refresh may reach the network: the configured value, or
    /// <see cref="SourceCacheUpdater.DefaultAutoUpdateSources"/> when none is configured.
    /// </summary>
    /// <param name="configuration">The application's configuration.</param>
    internal static bool AutoUpdateSources(IConfiguration configuration) =>
        configuration.GetValue(AutoUpdateSourcesKey, SourceCacheUpdater.DefaultAutoUpdateSources);
}
