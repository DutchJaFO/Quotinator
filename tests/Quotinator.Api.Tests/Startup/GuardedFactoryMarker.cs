namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Registered by <see cref="QuotinatorWebApplicationFactory"/> in every host it builds, so
/// <see cref="UnguardedFactoryRuntimeGuard"/> can tell a guarded host from one built by a factory that
/// never waited for startup (#313).
/// </summary>
internal sealed class GuardedFactoryMarker
{
}
