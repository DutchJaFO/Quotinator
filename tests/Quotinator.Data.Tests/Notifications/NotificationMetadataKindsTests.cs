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
            NotificationMetadataDto instance = (NotificationMetadataDto)System.Runtime.CompilerServices
                .RuntimeHelpers.GetUninitializedObject(payloadType);

            Assert.AreEqual(kind, instance.Kind,
                $"{payloadType.Name} is registered under {kind} but reports {instance.Kind} — a row would be written " +
                "with one discriminator and read back expecting another.");
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
