namespace Quotinator.Api.Tests.Startup;

/// <summary>What <see cref="UnguardedFactoryRuntimeGuard"/> observed while recording.</summary>
internal sealed class UnguardedFactoryRuntimeResult
{
    /// <summary>Every host for <c>Program</c> built without the guarded factory, described for a failure message.</summary>
    public IReadOnlyList<string> UnguardedHosts { get; init; } = [];

    /// <summary>Hosts for <c>Program</c> built through the guarded factory.</summary>
    public int GuardedHosts { get; init; }

    /// <summary>
    /// Hosts that are not <c>Program</c>'s. Counted so that recording none of the other two is evidence the
    /// guard looked, rather than that nothing reached it.
    /// </summary>
    public int OtherHosts { get; init; }
}
