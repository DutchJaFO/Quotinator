using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// Serializes <see cref="DuplicateResolutionPolicy"/> using kebab-case wire values (e.g. <c>MergeOurs</c> becomes
/// <c>"merge-ours"</c>). A parameterless-constructor subclass is required because <see cref="JsonConverterAttribute"/>
/// can only invoke a converter's parameterless constructor — it cannot pass <see cref="JsonNamingPolicy.KebabCaseLower"/>
/// as a constructor argument to the generic <see cref="JsonStringEnumConverter{TEnum}"/> directly.
/// </summary>
public sealed class DuplicateResolutionPolicyJsonConverter : JsonStringEnumConverter<DuplicateResolutionPolicy>
{
    /// <summary>Initializes the converter with kebab-case naming for all five policy values.</summary>
    public DuplicateResolutionPolicyJsonConverter() : base(JsonNamingPolicy.KebabCaseLower) { }
}
