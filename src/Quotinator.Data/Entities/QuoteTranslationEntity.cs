using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>A translated version of a quote's text for a specific language.</summary>
[Table("QuoteTranslations")]
public sealed class QuoteTranslationEntity : RecordBase
{
    /// <summary>The quote this translation belongs to.</summary>
    public Guid   QuoteId   { get; init; }

    /// <summary>ISO 639-1 language code of the translation (e.g. "nl", "de").</summary>
    public string Language  { get; init; } = string.Empty;

    /// <summary>The translated quote text.</summary>
    public string QuoteText { get; init; } = string.Empty;
}
