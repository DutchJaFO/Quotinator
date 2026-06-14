using Dapper.Contrib.Extensions;

namespace Quotinator.Core.Data.Entities;

[Table("CharacterTranslations")]
public sealed class CharacterTranslation : RecordBase
{
    public Guid   CharacterId { get; init; }
    public string Language    { get; init; } = string.Empty;
    public string Name        { get; init; } = string.Empty;
}
