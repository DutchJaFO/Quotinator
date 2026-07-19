using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Entities;

/// <summary>A translated version of a sound cue's text for a specific language.</summary>
[Table("SoundCueTranslations")]
public sealed class SoundCueTranslationEntity : RecordBase
{
    /// <summary>The sound cue this translation belongs to.</summary>
    public Guid SoundCueId { get; init; }

    /// <summary>ISO 639-1 language code of the translation (e.g. "nl", "de").</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>The translated text.</summary>
    public string Text { get; init; } = string.Empty;
}
