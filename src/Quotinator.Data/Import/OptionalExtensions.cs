namespace Quotinator.Data.Import;

/// <summary>Resolution helpers for <see cref="Optional{T}"/> (#190).</summary>
public static class OptionalExtensions
{
    /// <summary>
    /// Resolves an entry's optional field to what should actually be treated as "incoming" — the
    /// entry's own value if the property was present (null or not), or <paramref name="existingValue"/>
    /// if it was absent. This is the single mechanism that makes "absent = never a change" true under
    /// every <see cref="DuplicateResolutionPolicy"/>, not just the merge policies' own
    /// empty-side-loses rule in <see cref="FieldMergeResolver"/>. Passing <c>null</c> for
    /// <paramref name="existingValue"/> (no existing row — a genuine Add) makes an absent property
    /// resolve to <c>null</c>, matching this project's existing behaviour for a brand-new row.
    /// </summary>
    public static T? ResolveAgainst<T>(this Optional<T> optional, T? existingValue) =>
        optional.HasValue ? optional.Value : existingValue;
}
