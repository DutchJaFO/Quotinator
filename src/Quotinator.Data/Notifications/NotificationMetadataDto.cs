using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Base shape for a notification's <c>Metadata</c> column payload (#312), and the thing that decides
/// whether two notifications are "the same one".
/// <para>
/// Suffixed <c>Dto</c> per ADR 016's #264 revision: a JSON blob serialized into and read back out of a
/// database column takes the same suffix as one living in a file, since the suffix tracks the boundary
/// crossed, not where the bytes end up.
/// </para>
/// <para>
/// <b>There is deliberately no dedupe-key string.</b> #278 identified a notification by a magic token
/// embedded in its human-readable message and matched with <c>Contains</c> — which is why
/// <c>WhatsNew:v1.9.1</c> could falsely match inside <c>WhatsNew:v1.9.10</c>, and why #81 had to append
/// a delimiter to work around it. #312's first pass moved that token into a metadata field, which fixed
/// the false match but kept the fossil: callers still hand-concatenated an identity string. Identity is
/// now expressed as data — <see cref="Kind"/> plus <see cref="IdentityComponents"/> — so no caller
/// composes a key, and no substring hazard can exist in the first place.
/// </para>
/// </summary>
/// <param name="kind">Which payload shape this is; supplied by each derived type's own constructor.</param>
public abstract class NotificationMetadataDto(NotificationMetadataKind kind)
{
    /// <summary>
    /// Which payload shape this is. Persisted to the row's own <c>MetadataKind</c> column, which is
    /// what lets a reader deserialize an arbitrary stored payload back into the right type without
    /// knowing in advance which producer wrote it — see <see cref="NotificationMetadataKinds"/>.
    /// <para>
    /// <see cref="JsonIgnoreAttribute"/> because the column already carries it; storing it twice would
    /// create two copies that can disagree.
    /// </para>
    /// <para>
    /// Set through the constructor rather than declared <c>abstract</c> and overridden, which is what
    /// it was until a Docker run against a real database showed <c>"Kind":0</c> written into the stored
    /// JSON. <see cref="JsonIgnoreAttribute"/> is not inherited by an overriding property —
    /// <c>System.Text.Json</c> reads attributes from the most-derived declaration — so every derived
    /// type silently reintroduced the duplicate this attribute exists to prevent. With no override to
    /// declare, a producer cannot forget the attribute, because there is nowhere to forget it.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public NotificationMetadataKind Kind { get; } = kind;

    /// <summary>
    /// The values that together identify this notification, in a fixed order. Two payloads of the same
    /// <see cref="Kind"/> whose components are all equal are the same notification.
    /// <para>
    /// Deliberately a chosen subset rather than "every property": a payload may carry detail that
    /// describes the notification without identifying it (a timestamp, a row count), and including
    /// such a field would make an otherwise-identical notification re-announce itself. Each derived
    /// type states its own answer.
    /// </para>
    /// </summary>
    [JsonIgnore]
    protected abstract IEnumerable<object?> IdentityComponents { get; }

    /// <summary>
    /// Whether <paramref name="other"/> identifies the same notification as this one. Different kinds
    /// are never the same notification, whatever their components.
    /// </summary>
    /// <param name="other">The payload to compare against, typically one read back from storage.</param>
    public bool IsSameNotificationAs(NotificationMetadataDto other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind
            && IdentityComponents.SequenceEqual(other.IdentityComponents, IdentityComponentComparer);
    }

    // Strings compare case-insensitively, per this project's rule that identifier-valued comparisons
    // are case-insensitive by default; anything else falls back to its own Equals. A version string
    // reaching here from two different producers' formatting should not create a duplicate.
    private static readonly IEqualityComparer<object?> IdentityComponentComparer = new ComponentComparer();

    private sealed class ComponentComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y) => (x, y) switch
        {
            (null, null)                 => true,
            (string a, string b)         => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            (null, _) or (_, null)       => false,
            _                            => x!.Equals(y),
        };

        public int GetHashCode(object? obj) => obj switch
        {
            null     => 0,
            string s => StringComparer.OrdinalIgnoreCase.GetHashCode(s),
            _        => obj.GetHashCode(),
        };
    }
}
