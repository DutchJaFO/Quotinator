using System.Text.Json.Serialization;

namespace Quotinator.Core.Models;

/// <summary>Outcome of a filtered quote query, scoped to application semantics rather than HTTP status codes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilteredResultStatus
{
    /// <summary>One or more quotes were returned.</summary>
    Ok,

    /// <summary>All filter values were valid but no quotes matched.</summary>
    NoResults,

    /// <summary>One or more <c>type</c> values are not recognised.</summary>
    InvalidType,

    /// <summary>One or more <c>genre</c> values are not recognised.</summary>
    InvalidGenre,

    /// <summary>A text filter value (character, author, or source) exceeds the maximum allowed length.</summary>
    InputTooLong,

    /// <summary>A text filter value contains suspicious characters that suggest an injection attempt.</summary>
    InvalidInput,
}
