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
/// <remarks>
/// <b>Metadata is strictly non-text data</b> (developer direction, 2026-08-16): structured values that
/// help a renderer display the notification and that parameterise its actions. Identifiers, version
/// numbers, counts, ids. It never holds user-facing prose, and it never holds the notification's
/// language.
/// <para>
/// Anything textual — title, body, and the language they are written in — is a first-class column on
/// the notification itself, not a field smuggled into this payload. That is what keeps text
/// translatable through the same mechanism quotes already use (an <c>OriginalLanguage</c> on the row
/// plus a translations table), rather than trapped inside a JSON blob in whichever language happened to
/// be current when it was written.
/// </para>
/// </remarks>
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
    /// Which kind of release this notification describes. Common to every payload, not a what's-new
    /// concern: an announcement belongs to the release that shipped it, and a notification about
    /// nothing releasable says <see cref="NotificationReleaseState.NotApplicable"/> outright rather
    /// than leaving a reader to work out that the question does not apply.
    /// <para>
    /// <c>required</c>, so no payload can exist without stating it — the same guarantee
    /// <see cref="IdentityComponents"/> gives identity.
    /// </para>
    /// </summary>
    [JsonPropertyName("releaseState")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required NotificationReleaseState ReleaseState { get; init; }

    /// <summary>
    /// The release this notification is *about*, or <see langword="null"/> when there is none to name
    /// (the <c>unreleased</c> section, or a notification not about a release at all).
    /// <para>
    /// Deliberately distinct from the row's <c>AppVersionId</c> provenance column, and the two
    /// routinely differ: provenance records the version that *wrote* the row. Upgrading from 1.2 to
    /// 1.8.3 writes several what's-new notifications in one startup — every one written by 1.8.3, each
    /// describing a different release.
    /// </para>
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Hash of the content this notification presents, for producers whose content can change under a
    /// fixed identity — see <see cref="NotificationContentHash"/>.
    /// <para>
    /// <see langword="null"/> where the content is frozen by whatever else identifies the notification
    /// (a tagged release's highlights, say, cannot change after the tag).
    /// </para>
    /// </summary>
    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; init; }

    /// <summary>
    /// The values *this payload adds* to the common ones above, in a fixed order. Two payloads of the
    /// same <see cref="Kind"/> whose full identities match are the same notification.
    /// <para>
    /// Deliberately a chosen subset rather than "every property": a payload may carry detail that
    /// describes the notification without identifying it (a timestamp, a row count), and including
    /// such a field would make an otherwise-identical notification re-announce itself. Each derived
    /// type states its own answer, and returns an empty sequence when the common fields already say
    /// everything.
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
            && FullIdentity.SequenceEqual(other.FullIdentity, IdentityComponentComparer);
    }

    // The common fields lead, then whatever the payload adds. Comparing them at this level rather than
    // asking every derived type to remember to include them is the same reasoning that moved Kind and
    // the null-omission rule out of the derived types: a rule nobody has to apply cannot be forgotten.
    private IEnumerable<object?> FullIdentity => [ReleaseState, Version, ContentHash, .. IdentityComponents];

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
