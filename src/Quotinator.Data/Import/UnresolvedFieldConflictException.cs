namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="FieldMergeResolver.ResolveWithDecisions"/> when one or more fields are
/// genuinely ambiguous (both sides non-empty and differ) and no explicit decision was supplied for
/// them. Mirrors a git merge refusing to complete with unresolved conflicts remaining.
/// </summary>
public sealed class UnresolvedFieldConflictException : Exception
{
    /// <summary>The field names that are ambiguous and still need an explicit decision.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>Creates the exception with the list of fields still needing a decision.</summary>
    public UnresolvedFieldConflictException(IReadOnlyList<string> fieldNames)
        : base($"The following fields are ambiguous and need an explicit decision: {string.Join(", ", fieldNames)}")
    {
        FieldNames = fieldNames;
    }
}
