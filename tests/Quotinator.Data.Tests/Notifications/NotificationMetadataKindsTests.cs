using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// Guards the <see cref="NotificationMetadataKind"/> → payload-type registry (#312).
/// <para>
/// The registry is what makes the <c>MetadataKind</c> column pay for itself: a stored payload
/// deserializes straight back into its producer's own type. A kind with no entry cannot be read back,
/// so <c>NotificationSeeding</c> would silently treat every such row as unidentifiable and re-announce
/// it on every restart — a bug with no exception and no log line. This test is the mechanical gate that
/// stops a new enum member shipping without its type, mirroring how ADR 008's CHECK constraints guard
/// the storage side of the same enum.
/// </para>
/// </summary>
[TestClass]
public class NotificationMetadataKindsTests
{
    [TestMethod]
    public void EveryKind_HasARegisteredPayloadType()
    {
        List<NotificationMetadataKind> unregistered =
            [.. Enum.GetValues<NotificationMetadataKind>().Except(NotificationMetadataKinds.RegisteredKinds)];

        Assert.IsEmpty(unregistered,
            $"NotificationMetadataKind member(s) {string.Join(", ", unregistered)} have no payload type registered in " +
            "NotificationMetadataKinds.PayloadTypes — a notification of that kind could never be identified when read back.");
    }

    [TestMethod]
    public void EveryRegisteredPayloadType_ReportsTheKindItIsRegisteredUnder()
    {
        foreach (NotificationMetadataKind kind in NotificationMetadataKinds.RegisteredKinds)
        {
            Type payloadType = NotificationMetadataKinds.PayloadTypeFor(kind);
            NotificationMetadataDto instance = (NotificationMetadataDto)Activator.CreateInstance(payloadType)!;

            Assert.AreEqual(kind, instance.Kind,
                $"{payloadType.Name} is registered under {kind} but reports {instance.Kind} — a row would be written " +
                "with one discriminator and read back expecting another.");
        }
    }

    /// <summary>
    /// <c>Kind</c> must never reach the stored JSON: the row's own <c>MetadataKind</c> column already
    /// carries it, and two copies can disagree.
    /// <para>
    /// Found live, by a Docker run against a real v1.8.3 database rather than by any unit test — stored
    /// payloads read <c>{"announcement":"…","Kind":0}</c>. <c>Kind</c> was an abstract property with
    /// <c>[JsonIgnore]</c> on the base, but <c>System.Text.Json</c> reads attributes from the
    /// most-derived declaration, so every override silently dropped the attribute. No test caught it
    /// because none asserted on the serialized text — round-tripping succeeded either way, since the
    /// extra property simply deserialized back into an ignored member.
    /// </para>
    /// </summary>
    [TestMethod]
    public void SerializedPayload_NeverContainsTheKindDiscriminator()
    {
        foreach (NotificationMetadataKind kind in NotificationMetadataKinds.RegisteredKinds)
        {
            Type payloadType = NotificationMetadataKinds.PayloadTypeFor(kind);
            object instance = Activator.CreateInstance(payloadType)!;

            string json = System.Text.Json.JsonSerializer.Serialize(instance, payloadType);

            Assert.DoesNotContain("Kind", json, StringComparison.OrdinalIgnoreCase,
                $"{payloadType.Name} serialized its Kind into the payload ({json}) — the MetadataKind column already " +
                "records it, and a stored copy can drift out of step with the column.");
        }
    }

    /// <summary>A pre-#312 row carries neither kind nor payload, and must read back as "cannot identify" rather than throwing.</summary>
    [TestMethod]
    public void TryDeserialize_NullKindOrPayload_ReturnsNull()
    {
        Assert.IsNull(NotificationMetadataKinds.TryDeserialize(null, null));
        Assert.IsNull(NotificationMetadataKinds.TryDeserialize(null, "{\"version\":\"1.0.0\"}"));
        Assert.IsNull(NotificationMetadataKinds.TryDeserialize(NotificationMetadataKind.WhatsNew, null));
    }

    /// <summary>
    /// Malformed stored JSON is skipped, not thrown on. One unreadable historical row must not stop the
    /// rest of the history being evaluated on every subsequent startup.
    /// </summary>
    [TestMethod]
    public void TryDeserialize_MalformedJson_ReturnsNull()
    {
        Assert.IsNull(NotificationMetadataKinds.TryDeserialize(NotificationMetadataKind.WhatsNew, "{not json"));
    }
}
