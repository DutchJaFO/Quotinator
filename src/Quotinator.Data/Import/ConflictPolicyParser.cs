namespace Quotinator.Data.Import;

/// <summary>
/// Parses <see cref="DuplicateResolutionPolicy"/> wire strings from application configuration.
/// Extracted out of <c>Program.cs</c>'s top-level statements (where it started as a local function)
/// so the parsing logic is directly unit-testable.
/// </summary>
public static class ConflictPolicyParser
{
    /// <summary>Parses a policy value, falling back to <see cref="DuplicateResolutionPolicy.NewestWins"/> when <paramref name="value"/> is absent or unrecognised.</summary>
    public static DuplicateResolutionPolicy Parse(string? value) =>
        ParseNullable(value) ?? DuplicateResolutionPolicy.NewestWins;

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
