using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// Serializes/deserializes <see cref="Optional{T}"/>. <see cref="Read"/> only ever runs when the JSON
/// property is genuinely present (<see cref="System.Text.Json"/> never invokes a property's converter
/// for an absent key), so returning <see cref="Optional{T}.Of"/> unconditionally here is correct — an
/// absent property simply never reaches this converter and keeps the struct's default
/// (<see cref="Optional{T}.Absent"/>).
/// </summary>
/// <typeparam name="T">The wrapped property's value type.</typeparam>
internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    /// <inheritdoc/>
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Optional<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            JsonSerializer.Serialize(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}

/// <summary>
/// Constructs an <see cref="OptionalJsonConverter{T}"/> for whichever <see cref="Optional{T}"/> a
/// property declares. Registered once on a shared <see cref="JsonSerializerOptions"/> (see
/// <c>Quotinator.Core.Import.SourceQuoteFileReader</c>) covers every <see cref="Optional{T}"/>-typed
/// property with no per-property attribute repetition.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
