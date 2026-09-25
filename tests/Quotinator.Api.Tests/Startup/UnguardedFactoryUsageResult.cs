using Quotinator.Api.Tests.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// What one layer of the #313 guard found in the code it analysed: every unguarded usage, and the
/// evidence that it actually read that code, without which "found nothing" proves nothing.
/// </summary>
internal sealed class UnguardedFactoryUsageResult
{
    /// <summary>Every unguarded usage found, each with where it was found.</summary>
    public IReadOnlyList<(UnguardedFactoryUsageKind Kind, string Location)> Findings { get; init; } = [];

    /// <summary>
    /// How much code the analysis actually examined: syntax nodes of the kinds its rules judge for the
    /// source analysis, IL instructions for the assembly analysis. Zero means it looked at nothing, and an
    /// empty <see cref="Findings"/> beside a zero here proves nothing.
    /// </summary>
    public int InspectedCount { get; init; }

    /// <summary>
    /// How many constructions of the guarded factory the analysis resolved. Zero over code known to
    /// construct it means the analysis did not see that code.
    /// </summary>
    public int GuardedConstructionCount { get; init; }

    /// <summary>
    /// What stopped the analysis reading the code correctly: compiler errors for the source analysis, an
    /// unresolvable IL token for the assembly analysis. Any entry here makes the findings untrustworthy.
    /// </summary>
    public IReadOnlyList<string> ReadFailures { get; init; } = [];

    /// <summary>The distinct kinds found, for comparing a verdict against an expected set.</summary>
    public IReadOnlySet<UnguardedFactoryUsageKind> Kinds => Findings.Select(f => f.Kind).ToHashSet();
}
