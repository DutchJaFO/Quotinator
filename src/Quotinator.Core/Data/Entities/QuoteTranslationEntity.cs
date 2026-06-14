using Dapper.Contrib.Extensions;

namespace Quotinator.Core.Data.Entities;

[Table("QuoteTranslations")]
public sealed class QuoteTranslationEntity : RecordBase
{
    public Guid   QuoteId   { get; init; }
    public string Language  { get; init; } = string.Empty;
    public string QuoteText { get; init; } = string.Empty;
}
