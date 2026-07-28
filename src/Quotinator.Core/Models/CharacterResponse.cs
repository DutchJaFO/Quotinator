using Quotinator.Data.Entities;

namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single Character — a fictional character who delivers a quote, possibly across multiple Sources (#179).</summary>
public sealed class CharacterResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The character's name in the source's original language.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }

    /// <summary>Every Source this character appears in (#179's many-to-many), as minimal read-only
    /// references (<c>{id, name}</c> only — see <see cref="MasterDataReference"/>). Empty, never null,
    /// when the character has no linked Source. A Source that has been soft-deleted is never included
    /// (per CLAUDE.md's "Soft-deleted rows are invisible by default" convention).</summary>
    public IReadOnlyList<MasterDataReference> Sources { get; init; } = [];
}
