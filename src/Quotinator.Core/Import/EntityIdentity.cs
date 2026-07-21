using System.Security.Cryptography;
using System.Text;

namespace Quotinator.Core.Import;

/// <summary>
/// Generates stable, deterministic ids for Source/Character/Person/Series/Universe rows created
/// during staging (#154, #180), mirroring <see cref="QuoteIdentity.StableId"/>'s algorithm so a
/// not-yet-existing entity's id can be computed up front — enabling a read-only "does this already
/// exist" check at planning time without ever creating a row. Does not modify
/// <see cref="QuoteIdentity"/> itself, whose own algorithm must never change once committed.
/// </summary>
public static class EntityIdentity
{
    /// <summary>Derives a stable id for a Source from its title and type.</summary>
    public static string SourceId(string title, string type) => StableId("source", title, type);

    /// <summary>Derives a stable id for a Character from its owning Source's id and its name.</summary>
    public static string CharacterId(string sourceId, string name) => StableId("character", sourceId, name);

    /// <summary>Derives a stable id for a Person from their name.</summary>
    public static string PersonId(string name) => StableId("person", name);

    /// <summary>Derives a stable id for a Series (#180) from its name.</summary>
    public static string SeriesId(string name) => StableId("series", name);

    /// <summary>Derives a stable id for a Universe (#180) from its name.</summary>
    public static string UniverseId(string name) => StableId("universe", name);

    /// <summary>
    /// SHA-256 of the normalised, pipe-joined <paramref name="parts"/>, truncated to 16 bytes with
    /// the UUID version/variant bits forced — identical mechanics to <see cref="QuoteIdentity.StableId"/>,
    /// and now (ADR 012) identical casing too: both render <c>Guid.ToString("D")</c>'s default
    /// lowercase form, this project's single canonical id format. The first part is always a type tag
    /// (<c>"source"</c>/<c>"character"</c>/<c>"person"</c>/<c>"series"</c>/<c>"universe"</c>) so these
    /// id spaces can never collide with each other or with a <see cref="QuoteIdentity.StableId"/> value.
    /// This id is stored directly as a Source/Character/Person/Series/Universe <c>Id</c> without passing
    /// through <c>GuidHandler</c> at creation time (Character/Series/Universe's own <c>Add</c> ids are
    /// always this-method-derived, never file-authored, so there is no separate capture point to
    /// canonicalize at afterward — they must already be canonical the moment they're computed here).
    /// Matching <c>GuidHandler</c>'s own lowercase convention (see <c>GuidHandler.cs</c>) is what lets a
    /// later lookup's <see cref="Guid"/>-typed round-trip compare equal to what was actually written,
    /// since SQLite's default TEXT comparison is case-sensitive.
    /// </summary>
    private static string StableId(params string[] parts)
    {
        var key  = string.Join('|', parts.Select(QuoteIdentity.Normalise));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]).ToString("D");
    }
}
