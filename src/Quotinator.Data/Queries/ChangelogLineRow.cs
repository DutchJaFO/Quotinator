namespace Quotinator.Data.Queries;

/// <summary>
/// One flat row from the separate changelog database's <c>Changelog</c> LEFT JOIN <c>ChangelogLine</c>
/// (#309, ADR 018) — read model for <see cref="ChangelogWithLinesStrategy"/>. A <c>Changelog</c> row
/// with zero lines still appears once, with every line-only field <see langword="null"/>.
/// </summary>
public sealed record ChangelogLineRow
{
    /// <summary>The owning <c>Changelog_Entry</c> row's id.</summary>
    public Guid ChangelogEntryId { get; init; }

    /// <summary>ISO 639-1 language code.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Semver release version, or <see langword="null"/> for that language's <c>unreleased</c> entry.</summary>
    public string? Version { get; init; }

    /// <summary>Release date (ISO 8601), or <see langword="null"/> for <c>unreleased</c>.</summary>
    public string? Date { get; init; }

    /// <summary>Whether <see cref="Language"/>'s content was machine-translated.</summary>
    public bool MachineTranslated { get; init; }

    /// <summary>Optional release-note flavour quote text.</summary>
    public string? QuoteText { get; init; }

    /// <summary>Optional attribution for <see cref="QuoteText"/>.</summary>
    public string? QuoteAttribution { get; init; }

    /// <summary>The line's <c>Kind</c> as its raw string form (e.g. <c>"Highlight"</c>), or <see langword="null"/> when this <c>Changelog</c> row has no lines.</summary>
    public string? Kind { get; init; }

    /// <summary>Populated only when <see cref="Kind"/> is <c>"AudienceHighlight"</c>.</summary>
    public string? AudienceKey { get; init; }

    /// <summary>The line's text, or <see langword="null"/> when this <c>Changelog</c> row has no lines.</summary>
    public string? Value { get; init; }

    /// <summary>The line's position within its own <c>(Kind, AudienceKey)</c> list, or <see langword="null"/> when this <c>Changelog</c> row has no lines.</summary>
    public int? SortOrder { get; init; }
}
