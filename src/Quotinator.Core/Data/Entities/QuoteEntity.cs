using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

[Table("Quotes")]
public sealed class QuoteEntity : RecordBase
{
    public string QuoteText        { get; init; } = string.Empty;
    public string OriginalLanguage { get; init; } = "en";
    public Guid   SourceId         { get; init; }
    public Guid?  CharacterId      { get; init; }
    public Guid?  PersonId         { get; init; }
}
