using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Entities;

/// <summary>A translated title for a <see cref="SourceEntity"/>.</summary>
[Table("Quotinator_SourceTranslation")]
public sealed class SourceTranslationEntity : RecordBase
{
    /// <summary>The source this translation belongs to.</summary>
    public Guid   SourceId { get; init; }

    /// <summary>ISO 639-1 language code of the translation (e.g. "nl", "de").</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>The translated source title.</summary>
    public string Title    { get; init; } = string.Empty;
}
