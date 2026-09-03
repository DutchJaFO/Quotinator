using Quotinator.Data.Enums;
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

        Dictionary<string, object?> merged       = new(existing.Count);
        List<string> fromIncoming = [];

        foreach ((string? field, object? existingValue) in existing)
        {
            object? incomingValue = incoming.TryGetValue(field, out object? iv) ? iv : null;
            bool existingEmpty = IsEmpty(existingValue);
            bool incomingEmpty = IsEmpty(incomingValue);

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
        Dictionary<string, object?> merged       = new(existing.Count);
        List<string> fromIncoming = [];
        List<string> unresolved    = [];

        foreach ((string? field, object? existingValue) in existing)
        {
            object? incomingValue = incoming.TryGetValue(field, out object? iv) ? iv : null;

            if (decisions.TryGetValue(field, out FieldMergeDecision decision))
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

            bool existingEmpty = IsEmpty(existingValue);
            bool incomingEmpty = IsEmpty(incomingValue);

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
    /// as a set rather than a sequence — order carries no meaning for any list-valued field this
    /// project stores (nothing in the schema, storage, or UI attaches meaning to which genre is listed
    /// first), so two lists holding the same elements compare equal regardless of position. Set
    /// equality, not multiset: a genre is unique to a quote by construction
    /// (<c>UNIQUE (QuoteId, Genre)</c> on <c>Quotinator_QuoteGenre</c>), so a duplicate can never
    /// legitimately occur and nothing here needs to count occurrences. <see cref="List{T}"/> doesn't
    /// override <see cref="object.Equals(object)"/> either, so two equal-content-but-different-instance
    /// lists would otherwise compare unequal on identity alone. String values (scalar or within a
    /// collection) compare case-insensitively, matching this project's case-insensitive-by-default
    /// convention already applied to id/enum comparisons — a value arriving from an outside source (an
    /// import file, in this case) must never be treated as a "conflict" purely because of letter casing
    /// (e.g. "star wars" vs "Star Wars"), the same reasoning <c>QuoteIdentity.StableId</c> already
    /// applies when generating a quote's own id. Applied uniformly to every field, including free-text
    /// ones — a casing-only correction to a field's own content is treated the same as any other
    /// non-conflict. Used both for merge resolution and for any changed-field diff a caller computes
    /// outside this class (e.g. <c>ImportActionPlanner</c>'s completeness-blocking check, #168).
    /// <para>
    /// Found live (2026-09-04): the previous <c>SequenceEqual</c> implementation made an unchanged
    /// quote compare as Modified whenever its stored genre row order (SQLite gives no ordering
    /// guarantee absent an explicit <c>ORDER BY</c>, which <c>Sql.QuoteGenres.LoadForQuote</c> does not
    /// have) differed from the source file's own listed order — a false conflict over something that
    /// was never a real difference. The fix is this comparison, not pinning the read order: order was
    /// never meaningful data, so nothing should depend on it, including the query.
    /// </para>
    /// </summary>
    public static bool ValuesEqual(object? a, object? b)
    {
        if (a is IEnumerable ea && a is not string && b is IEnumerable eb && b is not string)
            return new HashSet<object?>(ea.Cast<object?>(), ScalarComparer.Instance).SetEquals(eb.Cast<object?>());

        return ScalarComparer.Instance.Equals(a, b);
    }

    /// <summary>Scalar equality with case-insensitive string comparison; delegates to <see cref="object.Equals(object)"/> for every other type.</summary>
    private sealed class ScalarComparer : IEqualityComparer<object?>
    {
        public static readonly ScalarComparer Instance = new();

        public new bool Equals(object? a, object? b) => a is string sa && b is string sb
            ? string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)
            : object.Equals(a, b);

        public int GetHashCode(object? obj) => obj switch
        {
            string s => StringComparer.OrdinalIgnoreCase.GetHashCode(s),
            null     => 0,
            _        => obj.GetHashCode(),
        };
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
