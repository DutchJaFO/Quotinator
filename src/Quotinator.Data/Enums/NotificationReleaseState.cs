namespace Quotinator.Data.Enums;

/// <summary>
/// Which kind of release a notification's payload describes (#312) — a tagged one, the changelog's
/// <c>unreleased</c> section, or no release at all.
/// <para>
/// An explicit state rather than an inferred one. The released/unreleased distinction was originally
/// drawn from the payload's <c>Version</c> being <see langword="null"/>, which is the fault this
/// replaces: null is indistinguishable from "not set", "failed to parse" and "the producer forgot", so
/// any of those three accidents read as <see cref="Unreleased"/>, and every future reader had to know
/// the convention to interpret a stored row at all.
/// </para>
/// <para>
/// Not persisted as a column of its own and therefore not an ADR 008 case — it is a field inside the
/// notification's JSON payload, serialized by name via <c>JsonStringEnumConverter</c>.
/// </para>
/// </summary>
public enum NotificationReleaseState
{
    /// <summary>
    /// This notification is not about a release at all — a schema-version overshoot describes a
    /// database's own state, not a version's contents.
    /// <para>
    /// Deliberately the zero value, so a payload that somehow reached storage without stating its
    /// state cannot silently claim to describe a tagged release. Stating "no release" is a claim a
    /// producer makes on purpose; it is not the same as leaving the question unanswered, which the
    /// <c>required</c> property is what actually prevents.
    /// </para>
    /// </summary>
    NotApplicable,

    /// <summary>A tagged release. The payload's version identifies which one.</summary>
    Released,

    /// <summary>
    /// The changelog's <c>unreleased</c> section, which has no version number yet. The payload's
    /// version stays <see langword="null"/> here — but nothing infers the state from that any more.
    /// </summary>
    Unreleased
}
