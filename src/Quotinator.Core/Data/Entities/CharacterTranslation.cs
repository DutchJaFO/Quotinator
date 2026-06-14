using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

/// <summary>A translated name for a <see cref="Character"/>.</summary>
[Table("CharacterTranslations")]
public sealed class CharacterTranslation : RecordBase
{
    /// <summary>The character this translation belongs to.</summary>
    public Guid   CharacterId { get; init; }

    /// <summary>ISO 639-1 language code of the translation (e.g. "nl", "de").</summary>
    public string Language    { get; init; } = string.Empty;

    /// <summary>The translated character name.</summary>
    public string Name        { get; init; } = string.Empty;
}
