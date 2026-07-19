using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Entities;

/// <summary>Associates a Character with a Source it appears in. A Character may appear in multiple Sources.</summary>
[Table("CharacterSources")]
public sealed class CharacterSourceEntity : RecordBase
{
    /// <summary>The character appearing in the source.</summary>
    public Guid CharacterId { get; init; }

    /// <summary>The source the character appears in.</summary>
    public Guid SourceId { get; init; }
}
