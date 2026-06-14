using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data.Entities;

[Table("QuoteGenres")]
public sealed class QuoteGenreEntity : RecordBase
{
    public Guid QuoteId { get; init; }

    /// <summary>Genre stored as TEXT (enum name). Alias avoids naming conflict with the property.</summary>
    public SafeValue<GenreEnum?> Genre { get; init; } = SafeValue<GenreEnum?>.Empty;
}
