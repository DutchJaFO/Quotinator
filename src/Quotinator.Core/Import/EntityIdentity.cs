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

    /// <summary>
    /// Derives a stable id for a Character from the resolving Source's id, the Character's Name, and
    /// the resolving Source's Type. Used only as the fallback when <see cref="Quotinator.Core.Database.
    /// ImportActionPlanner.ResolveCharacterAsync"/> finds no existing match at all (ADR 013 Decision
    /// 5/7) — an actual match (same-Source or Series-scoped cross-Source) always reuses the found
    /// row's real id, never this hash. <c>sourceType</c> is included for defense-in-depth/self-
    /// documentation of the Type anchor (ADR 011) even though it is technically derivable from
    /// <c>sourceId</c> alone; <c>sourceId</c> itself must stay part of the hash — dropping it in
    /// favour of <c>(name, sourceType)</c> alone (as an earlier draft of this issue's plan doc
    /// speculated) would deterministically collide two independent, Series-unrelated Characters onto
    /// the same id the moment each is introduced for the first time, since two such Characters can
    /// legitimately share <c>(Name, SourceType)</c> under ADR 013's Series-scoped merge semantics (see
    /// ADR 013 Decision 5 for the full reasoning).
    /// </summary>
    public static string CharacterId(string sourceId, string name, string sourceType) => StableId("character", sourceId, name, sourceType);

    /// <summary>Derives a stable id for a Person from their name.</summary>
    public static string PersonId(string name) => StableId("person", name);

    /// <summary>Derives a stable id for a Series (#180) from its name.</summary>
    public static string SeriesId(string name) => StableId("series", name);

    /// <summary>
    /// Derives a stable id for a Season (#375) from its Series' id and its ordinal. Unlike
    /// <see cref="SeriesId"/> and <see cref="UniverseId"/>, which key on a globally unique name, a
    /// season's number only identifies it within its parent — "Season 1" recurs for every series — so
    /// the parent id is part of the hash, as it is for <see cref="CharacterId"/>.
    /// </summary>
    public static string SeasonId(string seriesId, int number) =>
        StableId("season", seriesId, number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Derives a stable id for a Universe (#180) from its name.</summary>
    public static string UniverseId(string name) => StableId("universe", name);

    /// <summary>
    /// SHA-256 of the normalised, pipe-joined <paramref name="parts"/>, truncated to 16 bytes with
    /// the UUID version/variant bits forced — identical mechanics to <see cref="QuoteIdentity.StableId"/>,
    /// and now (ADR 012) identical casing too: both render <c>Guid.ToString("D")</c>'s default
    /// lowercase form, this project's single canonical id format. The first part is always a type tag
    /// (<c>"source"</c>/<c>"character"</c>/<c>"person"</c>/<c>"series"</c>/<c>"season"</c>/<c>"universe"</c>) so these
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
        string key  = string.Join('|', parts.Select(QuoteIdentity.Normalise));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]).ToString("D");
    }
}
