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

    /// <summary>
    /// Resolves every field in <paramref name="existing"/> against <paramref name="incoming"/> using an
    /// explicit per-field <paramref name="decisions"/> map (#149's manual conflict-review workflow),
    /// git-merge-style: a supplied decision always wins for that field, regardless of whether it was
    /// actually ambiguous. Any field with no decision auto-resolves the same way <see cref="Resolve"/>
    /// already does (empty-side wins, equal values keep existing). A field that is genuinely ambiguous
    /// (both sides non-empty and differ) with no decision supplied is collected and reported via
    /// <see cref="UnresolvedFieldConflictException"/> once every field has been examined — mirroring a
    /// git merge refusing to complete while unresolved conflicts remain.
    /// </summary>
    /// <exception cref="UnresolvedFieldConflictException">
    /// One or more fields are ambiguous and have no decision. <see cref="UnresolvedFieldConflictException.FieldNames"/>
    /// lists every such field, not just the first one found.
    /// </exception>
    public static FieldMergeResult ResolveWithDecisions(
        IReadOnlyDictionary<string, object?> existing,
        IReadOnlyDictionary<string, object?> incoming,
        IReadOnlyDictionary<string, FieldMergeDecision> decisions)
    {
        var merged       = new Dictionary<string, object?>(existing.Count);
        var fromIncoming = new List<string>();
        var unresolved    = new List<string>();

        foreach (var (field, existingValue) in existing)
        {
            var incomingValue = incoming.TryGetValue(field, out var iv) ? iv : null;

            if (decisions.TryGetValue(field, out var decision))
            {
                switch (decision.Choice)
                {
                    case FieldResolutionChoice.Keep:
                        merged[field] = existingValue;
                        break;
                    case FieldResolutionChoice.Replace:
                        merged[field] = incomingValue;
                        fromIncoming.Add(field);
                        break;
                    case FieldResolutionChoice.Custom:
                        merged[field] = decision.CustomValue;
                        fromIncoming.Add(field);
                        break;
                }
                continue;
            }

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
                merged[field] = existingValue;
            }
            else if (ValuesEqual(existingValue, incomingValue))
            {
                merged[field] = existingValue;
            }
            else
            {
                unresolved.Add(field);
            }
        }

        if (unresolved.Count > 0)
            throw new UnresolvedFieldConflictException(unresolved);

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

    /// <summary>
    /// Compares two field values for equality, treating list/array-valued fields (e.g. <c>genres</c>)
    /// by sequence content rather than reference identity — <see cref="List{T}"/> doesn't override
    /// <see cref="object.Equals(object)"/>, so two equal-content-but-different-instance lists would
    /// otherwise compare unequal. Used both for merge resolution and for any changed-field diff a
    /// caller computes outside this class (e.g. <c>ImportActionPlanner</c>'s completeness-blocking
    /// check, #168).
    /// </summary>
    public static bool ValuesEqual(object? a, object? b)
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
