using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>
/// Parses <see cref="DuplicateResolutionPolicy"/> wire strings from application configuration.
/// Extracted out of <c>Program.cs</c>'s top-level statements (where it started as a local function)
/// so the parsing logic is directly unit-testable.
/// </summary>
public static class ConflictPolicyParser
{
    /// <summary>Parses a policy value, falling back to <see cref="DuplicateResolutionPolicy.NewestWins"/> when <paramref name="value"/> is absent or unrecognised.</summary>
    /// <remarks>
    /// Falls back to <see cref="DuplicateResolutionPolicy.Review"/> (#303). This is the only default any
    /// running instance actually reaches — <c>Program.cs</c> passes
    /// <c>Quotinator:DefaultConflictPolicy</c> through here, and an absent key lands on this value.
    /// <para>
    /// It was <see cref="DuplicateResolutionPolicy.NewestWins"/>, under which a file dropped into the
    /// imports folder overwrote stored quotes with no notification and no record. Holding the change
    /// for a decision is the only default that cannot lose content silently:
    /// <see cref="DuplicateResolutionPolicy.Skip"/> discards the incoming value just as quietly, and
    /// the merge policies pick a side without being asked.
    /// </para>
    /// </remarks>
    public static DuplicateResolutionPolicy Parse(string? value) =>
        ParseNullable(value) ?? DuplicateResolutionPolicy.Review;

    /// <summary>Parses a policy value, returning <c>null</c> when <paramref name="value"/> is absent or unrecognised (used for per-entity-type overrides, where <c>null</c> means "inherit the default").</summary>
    public static DuplicateResolutionPolicy? ParseNullable(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "skip"         => DuplicateResolutionPolicy.Skip,
            "newest-wins"  => DuplicateResolutionPolicy.NewestWins,
            "merge-ours"   => DuplicateResolutionPolicy.MergeOurs,
            "merge-theirs" => DuplicateResolutionPolicy.MergeTheirs,
            "review"       => DuplicateResolutionPolicy.Review,
            _              => null
        };
}
