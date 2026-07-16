namespace Quotinator.Core.Import;

/// <summary>The full set of sections a Quotinator source file (extended format) can contain.</summary>
public sealed class ParsedSourceFile
{
    /// <summary>Canonical quote entries. Always present, even for a bare-array (flat format) file.</summary>
    public required IReadOnlyList<SourceQuote> Quotes { get; init; }

    /// <summary>Explicit Source declarations (#162). Empty for a flat-format file or a file with no <c>sources</c> section.</summary>
    public IReadOnlyList<SourceEntry> Sources { get; init; } = [];

    /// <summary>Explicit Person declarations (#173). Empty for a flat-format file or a file with no <c>people</c> section.</summary>
    public IReadOnlyList<PersonEntry> People { get; init; } = [];

    /// <summary>Reusable stage directions. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceStageDirection> StageDirections { get; init; } = [];

    /// <summary>Reusable sound cues. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceSoundCue> SoundCues { get; init; } = [];

    /// <summary>Ordered groupings of quotes, stage directions, and sound cues. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceConversation> Conversations { get; init; } = [];

    /// <summary>Explicit Series declarations (#180). Empty for a flat-format file or a file with no <c>series</c> section.</summary>
    public IReadOnlyList<SeriesEntry> Series { get; init; } = [];

    /// <summary>Explicit Universe declarations (#180). Empty for a flat-format file or a file with no <c>universe</c> section.</summary>
    public IReadOnlyList<UniverseEntry> Universe { get; init; } = [];
}
