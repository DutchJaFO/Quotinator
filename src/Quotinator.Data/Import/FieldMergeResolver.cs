using System.Collections;

namespace Quotinator.Data.Import;

/// <summary>
/// Generic per-field merge resolution for the <see cref="DuplicateResolutionPolicy.MergeOurs"/> and
/// <see cref="DuplicateResolutionPolicy.MergeTheirs"/> conflict policies. Operates over a plain
/// field-name → value map so this project has no dependency on any specific domain schema — callers
/// convert their own model to and from this representation.
/// </summary>
public static class FieldMergeResolver
{
    /// <summary>
    /// Resolves every field in <paramref name="existing"/> against <paramref name="incoming"/>. For each
    /// field: if one side is null/empty and the other is not, the non-empty side wins. If both sides have
    /// non-empty, differing values, <paramref name="policy"/> breaks the tie — <see cref="DuplicateResolutionPolicy.MergeOurs"/>
    /// keeps <paramref name="existing"/>'s value, <see cref="DuplicateResolutionPolicy.MergeTheirs"/> takes
    /// <paramref name="incoming"/>'s value. Scalar and array/list values are treated identically — arrays
    /// are never unioned, only replaced wholesale on a true conflict.
    /// </summary>
    public static FieldMergeResult Resolve(
        IReadOnlyDictionary<string, object?> existing,
        IReadOnlyDictionary<string, object?> incoming,
        DuplicateResolutionPolicy policy)
    {
        if (policy is not (DuplicateResolutionPolicy.MergeOurs or DuplicateResolutionPolicy.MergeTheirs))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "FieldMergeResolver only supports MergeOurs and MergeTheirs.");

        var merged       = new Dictionary<string, object?>(existing.Count);
        var fromIncoming = new List<string>();

        foreach (var (field, existingValue) in existing)
        {
            var incomingValue = incoming.TryGetValue(field, out var iv) ? iv : null;
            var existingEmpty = IsEmpty(existingValue);
            var incomingEmpty = IsEmpty(incomingValue);

            if (!existingEmpty && incomingEmpty)
            {
                merged[field] = existingValue;
            }
            else if (existingEmpty && !incomingEmpty)
            {
                merged[field] = incomingValue;
                fromIncoming.Add(field);
            }
            else if (existingEmpty)
            {
                // Both empty — nothing to fill from either side.
                merged[field] = existingValue;
            }
            else if (ValuesEqual(existingValue, incomingValue))
            {
                merged[field] = existingValue;
            }
            else if (policy == DuplicateResolutionPolicy.MergeTheirs)
            {
                merged[field] = incomingValue;
                fromIncoming.Add(field);
            }
            else
            {
                merged[field] = existingValue;
            }
        }

        return new FieldMergeResult(merged, fromIncoming);
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null     => true,
        string s => s.Length == 0,
        ICollection c => c.Count == 0,
        IEnumerable e => !e.Cast<object?>().Any(),
        _ => false
    };

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is IEnumerable ea && a is not string && b is IEnumerable eb && b is not string)
            return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
        return Equals(a, b);
    }
}

/// <summary>Result of a <see cref="FieldMergeResolver.Resolve"/> call.</summary>
/// <param name="MergedFields">The resolved value for every field present in the original <c>existing</c> map.</param>
/// <param name="FieldsFromIncoming">
/// Names of the fields whose resolved value came from <c>incoming</c> — used to populate provenance
/// (e.g. <c>System_ImportConflicts.MergedFields</c>).
/// </param>
public sealed record FieldMergeResult(
    IReadOnlyDictionary<string, object?> MergedFields,
    IReadOnlyList<string> FieldsFromIncoming);
