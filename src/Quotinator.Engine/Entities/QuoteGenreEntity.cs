using Dapper.Contrib.Extensions;
using Quotinator.Core.Models;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>Associates a quote with a genre tag.</summary>
[Table("QuoteGenres")]
public sealed class QuoteGenreEntity : RecordBase
{
    /// <summary>The quote this genre tag belongs to.</summary>
    public Guid QuoteId { get; init; }

    /// <summary>Genre stored as TEXT (enum name). <see cref="SafeValue{T}.Raw"/> is preserved if the stored value is unrecognised.</summary>
    public SafeValue<Genre?> Genre { get; init; } = SafeValue<Genre?>.Empty;
}
