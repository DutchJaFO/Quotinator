namespace Quotinator.Core.Import;

/// <summary>The full set of sections a Quotinator source file (extended format) can contain.</summary>
public sealed class ParsedSourceFileDto
{
    /// <summary>Canonical quote entries. Always present, even for a bare-array (flat format) file.</summary>
    public required IReadOnlyList<SourceQuoteDto> Quotes { get; init; }

    /// <summary>Explicit Source declarations (#162). Empty for a flat-format file or a file with no <c>sources</c> section.</summary>
    public IReadOnlyList<SourceEntryDto> Sources { get; init; } = [];

    /// <summary>Explicit Person declarations (#173). Empty for a flat-format file or a file with no <c>people</c> section.</summary>
    public IReadOnlyList<PersonEntryDto> People { get; init; } = [];

    /// <summary>Explicit Character declarations (#175). Empty for a flat-format file or a file with no <c>characters</c> section.</summary>
    public IReadOnlyList<CharacterEntryDto> Characters { get; init; } = [];

    /// <summary>Reusable stage directions. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceStageDirectionDto> StageDirections { get; init; } = [];

    /// <summary>Reusable sound cues. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceSoundCueDto> SoundCues { get; init; } = [];

    /// <summary>Ordered groupings of quotes, stage directions, and sound cues. Empty for a flat-format file.</summary>
    public IReadOnlyList<SourceConversationDto> Conversations { get; init; } = [];

    /// <summary>Explicit Series declarations (#180). Empty for a flat-format file or a file with no <c>series</c> section.</summary>
    public IReadOnlyList<SeriesEntryDto> Series { get; init; } = [];

    /// <summary>Explicit Universe declarations (#180). Empty for a flat-format file or a file with no <c>universe</c> section.</summary>
    public IReadOnlyList<UniverseEntryDto> Universe { get; init; } = [];

    /// <summary>Explicit Season declarations (#375). Empty for a flat-format file or a file with no <c>seasons</c> section.</summary>
    public IReadOnlyList<SeasonEntryDto> Seasons { get; init; } = [];
}
