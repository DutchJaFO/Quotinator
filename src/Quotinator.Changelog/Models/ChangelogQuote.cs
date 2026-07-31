namespace Quotinator.Changelog.Models;

/// <summary>
/// Optional, freely-written one-line release-note flavour text attached to a <see cref="ChangelogRelease"/>.
/// This is release-note prose — the same category as <see cref="ChangelogUnreleased.Highlights"/> — and is
/// never a served Quotinator quote from <c>data/sources/</c>, so it is not subject to the project's
/// "never invent quotes" policy.
/// </summary>
public sealed class ChangelogQuote
{
    /// <summary>The one-line flavour quote text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Optional attribution — who said it, or where it came from.</summary>
    public string? Attribution { get; init; }
}
