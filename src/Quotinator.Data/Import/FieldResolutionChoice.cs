using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>How a single field's value was decided during manual conflict resolution (#149).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FieldResolutionChoice
{
    /// <summary>Keep the existing side's value.</summary>
    Keep,

    /// <summary>Take the incoming side's value.</summary>
    Replace,

    /// <summary>Use a caller-supplied value, overriding both sides.</summary>
    Custom
}
