namespace Quotinator.Changelog.Enums;

/// <summary>
/// Reserved <see cref="Models.ChangelogUnreleased.AudienceHighlights"/> keys with application-level
/// runtime meaning, distinct from the free-form audience names <c>scripts/changelog.csx</c> renders
/// markdown for (e.g. <c>ha-addon</c>), which stay open string values outside this project's own
/// knowledge.
/// </summary>
public enum ChangelogReservedAudience
{
    /// <summary>Highlights surfaced as a startup notification, not a markdown-generation audience.</summary>
    Notification
}
