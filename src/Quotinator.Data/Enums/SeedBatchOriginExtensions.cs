namespace Quotinator.Data.Enums;

/// <summary>
/// Translates a <see cref="SeedBatchOrigin"/> into the <see cref="FileResourceOrigin"/> the rest of the
/// application records provenance with.
/// </summary>
/// <remarks>
/// Extracted rather than copied when #302 became the second consumer: the mapping was written inline in
/// <c>QuotinatorDatabaseInitializer</c>'s file-resource capture (#251), and the per-file reseed
/// confirmation needs the same answer. Two copies of a two-value mapping drift silently — the second
/// one is only ever exercised by whichever feature added it.
/// <para>
/// The two enums stay separate deliberately. <see cref="SeedBatchOrigin"/> names where a *seed batch*
/// came from and has exactly the two values seeding can produce; <see cref="FileResourceOrigin"/> is
/// the application-wide provenance vocabulary and carries <see cref="FileResourceOrigin.Upload"/>,
/// which no seed batch can ever be. Collapsing them would force seeding to acknowledge a case it
/// cannot reach.
/// </para>
/// </remarks>
public static class SeedBatchOriginExtensions
{
    /// <summary>The provenance value corresponding to <paramref name="origin"/>.</summary>
    /// <param name="origin">The seed batch's own origin.</param>
    public static FileResourceOrigin ToFileResourceOrigin(this SeedBatchOrigin origin) =>
        origin == SeedBatchOrigin.UserImports ? FileResourceOrigin.User : FileResourceOrigin.System;
}
