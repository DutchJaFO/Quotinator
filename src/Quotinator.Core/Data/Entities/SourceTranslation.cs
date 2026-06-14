using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

[Table("SourceTranslations")]
public sealed class SourceTranslation : RecordBase
{
    public Guid   SourceId { get; init; }
    public string Language { get; init; } = string.Empty;
    public string Title    { get; init; } = string.Empty;
}
