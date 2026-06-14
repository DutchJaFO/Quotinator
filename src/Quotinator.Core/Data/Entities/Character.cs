using Dapper.Contrib.Extensions;

namespace Quotinator.Core.Data.Entities;

[Table("Characters")]
public sealed class Character : RecordBase
{
    /// <summary>Scopes this character to a source so "John" from two different films stays separate.</summary>
    public Guid   SourceId { get; init; }
    public string Name     { get; init; } = string.Empty;
}
