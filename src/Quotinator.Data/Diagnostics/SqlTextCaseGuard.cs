using System.Reflection;
using System.Text.RegularExpressions;

namespace Quotinator.Data.Diagnostics;

/// <summary>
/// Guard against a case-sensitive comparison between a non-id text column (a Name/Title natural key,
/// a Status/EntityType/TableName discriminator, or a Language code) and a caller-or-file-supplied
/// parameter. Sibling to <see cref="SqlIdCaseGuard"/>, which covers <c>*Id</c>-suffixed columns only —
/// this guard covers everything CLAUDE.md's "case-insensitive by default" rule extends to beyond ids
/// (#211).
/// </summary>
/// <remarks>
/// Unlike <see cref="SqlIdCaseGuard"/>, the columns this guard protects share no common name suffix,
/// so a single compile-time regex pattern can't identify them the way <c>\w*Id</c> does. Instead,
/// <see cref="DiscoverTextColumnNames"/> reflects over the caller-supplied entity types (each already
/// carries a Dapper.Contrib <c>[Table(...)]</c> attribute) and collects every public property whose
/// *declared C# type* is exactly <c>string</c>/<c>string?</c> — a <c>SafeValue&lt;TEnum?&gt;</c>-typed
/// property is a different .NET type entirely and is skipped automatically, with no enum-specific
/// logic required (the developer's own framing: "we know that enums will not be a string property so
/// that is easy to identify"). Columns already governed by <see cref="SqlIdCaseGuard"/> (name ends in
/// <c>Id</c>) are excluded here to avoid double-coverage. This method takes <c>Type[]</c> generically
/// so it never references any specific entity type itself and stays domain-agnostic (ADR 004) — each
/// test project supplies its own locally-relevant entity types.
/// <para/>
/// <b>Reflecting on storage type alone is not sufficient</b> — found while designing this guard, not
/// assumed away. <c>SystemImportActions.Status</c> is <c>SafeValue&lt;ImportActionStatus?&gt;</c> on
/// its entity (correctly enum-backed for storage), so reflection alone would skip it. But its query
/// parameter (<c>SystemImportActionReader.GetPagedAsync(string? status, ...)</c>) is raw external text
/// with no enum round-trip at all before reaching <c>Sql.SystemImportActions.BuildWhere</c>'s
/// <c>@status</c> — unlike <c>Characters.SourceType</c>/<c>ImportBatches.Type</c>/the
/// <c>type[]</c>/<c>genre[]</c> filters, where the bound parameter is always <c>.ToString()</c>'d from
/// a parsed enum first. The entity's own storage type says nothing about whether its *filter
/// parameter* was ever validated against that same enum. <see cref="AdditionalColumnNames"/> is the
/// explicit, justified supplement for exactly this class of gap — mirroring
/// <see cref="SqlSelectPresentationGuard.ExemptColumnNames"/>'s own precedent, just additive instead
/// of subtractive.
/// </remarks>
public static partial class SqlTextCaseGuard
{
    /// <summary>
    /// Column names that need this guard's protection despite not being discoverable via
    /// <see cref="DiscoverTextColumnNames"/> — see this class's remarks for why <c>Status</c>
    /// (<c>SystemImportActions</c>) is the sole entry: it's enum-backed on its entity, but its query
    /// parameter is raw external text with no enum safety net of its own.
    /// </summary>
    public static readonly IReadOnlyList<string> AdditionalColumnNames = ["Status"];

    // A bare or alias-qualified, optionally bracket-quoted column reference followed by "=" or "IN"
    // and a bound parameter. Deliberately not restricted to any name pattern (unlike SqlIdCaseGuard's
    // \w*Id) — the column-name set is supplied by the caller instead (see FindViolations). The
    // optional leading "LOWER(" lets this still match a *half*-wrapped comparison (only the parameter
    // side wrapped) — an unprotected match here is exactly as unsafe as an unwrapped one.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:\w+\.)?\[?(\w+)\]?\)?\s*(=|IN)\s*(?:LOWER\s*\(\s*)?@\w+", RegexOptions.IgnoreCase)]
    private static partial Regex EqualityComparisonPattern();

    // An already-protected equality: LOWER(column) = LOWER(@param). Both sides must be wrapped —
    // a half-protected form (only one side wrapped) is deliberately NOT matched here, so it still
    // falls through to EqualityComparisonPattern and gets flagged.
    [GeneratedRegex(@"LOWER\s*\(\s*(?:\w+\.)?\[?\w+\]?\s*\)\s*=\s*LOWER\s*\(\s*@\w+\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedEqualityPattern();

    // UPDATE ... SET <assignments> WHERE ... — the SET portion writes new values ("Name = @name" is
    // an assignment, not a comparison) and must never be flagged; mirrors SqlIdCaseGuard's own
    // StripUpdateSetClause exactly. Found live while verifying this guard: every UpdateFieldsById-style
    // query in the codebase false-positived on its own SET clause until this was added.
    [GeneratedRegex(@"\bSET\b[\s\S]*?(?=\bWHERE\b)", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateSetClausePattern();

    [GeneratedRegex(@"^\s*UPDATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingUpdateKeywordPattern();

    /// <summary>
    /// Reflects over <paramref name="entityTypes"/> and returns every public property name whose
    /// declared type is exactly <c>string</c>/<c>string?</c> and whose name doesn't end in <c>Id</c>
    /// (governed by <see cref="SqlIdCaseGuard"/> instead). See this class's remarks for why this
    /// excludes enum-backed (<c>SafeValue&lt;TEnum?&gt;</c>) properties automatically.
    /// </summary>
    public static IReadOnlySet<string> DiscoverTextColumnNames(params Type[] entityTypes)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in entityTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string)) continue;
                if (property.Name.EndsWith("Id", StringComparison.Ordinal)) continue;
                names.Add(property.Name);
            }
        }
        return names;
    }

    /// <summary>
    /// Returns every case-sensitive text-column comparison found in <paramref name="sql"/>, for
    /// diagnostics — every unwrapped <c>column = @param</c>/<c>column IN @param</c> match whose
    /// column name is in <paramref name="knownTextColumnNames"/> or <see cref="AdditionalColumnNames"/>.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(string sql, IReadOnlyCollection<string> knownTextColumnNames)
    {
        if (string.IsNullOrWhiteSpace(sql)) return [];

        var columnSet = new HashSet<string>(knownTextColumnNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in AdditionalColumnNames) columnSet.Add(name);
        if (columnSet.Count == 0) return [];

        var scanned = StripUpdateSetClause(sql);
        scanned = ProtectedEqualityPattern().Replace(scanned, " ");

        return [.. EqualityComparisonPattern().Matches(scanned)
            .Where(m => columnSet.Contains(m.Groups[1].Value))
            .Select(m => m.Value)];
    }

    private static string StripUpdateSetClause(string sql)
        => LeadingUpdateKeywordPattern().IsMatch(sql)
            ? UpdateSetClausePattern().Replace(sql, " ")
            : sql;
}
