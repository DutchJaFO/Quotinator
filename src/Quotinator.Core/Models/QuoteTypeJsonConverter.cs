using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Core.Models;

/// <summary>
/// Serializes <see cref="QuoteType"/> using kebab-case wire values, matching
/// <c>Quotinator.Data.Import.DuplicateResolutionPolicyJsonConverter</c>'s convention. Today's members
/// are all single words (so this only lowercases them), but kebab-case keeps the convention consistent
/// for any future multi-word value. A parameterless-constructor subclass is required because
/// <see cref="JsonConverterAttribute"/> can only invoke a converter's parameterless constructor.
/// </summary>
public sealed class QuoteTypeJsonConverter : JsonStringEnumConverter<QuoteType>
{
    /// <summary>Initializes the converter with kebab-case naming.</summary>
    public QuoteTypeJsonConverter() : base(JsonNamingPolicy.KebabCaseLower) { }
}
