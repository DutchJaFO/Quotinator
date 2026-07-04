using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Converters.NikhilNamal17;

/// <summary>Reads a raw upstream <c>year</c> value that may be either a JSON number or a JSON string, normalising it to a plain <see cref="string"/>.</summary>
internal sealed class YearJsonConverter : JsonConverter<string?>
{
    /// <inheritdoc/>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetInt32().ToString(),
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Null   => null,
        _                    => throw new JsonException($"Unexpected token {reader.TokenType} for year")
    };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
