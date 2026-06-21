namespace Quotinator.Changelog.Models;

/// <summary>
/// A versioned release — a <see cref="ChangelogUnreleased"/> block promoted by assigning
/// a <see cref="Version"/> and <see cref="Date"/>.
/// </summary>
public sealed class ChangelogRelease : ChangelogUnreleased
{
    /// <summary>Version string, e.g. <c>1.1.0</c>.</summary>
    public string Version { get; init; } = "";

    /// <summary>Release date in ISO 8601 format, e.g. <c>2026-06-15</c>.</summary>
    public string Date { get; init; } = "";
}
