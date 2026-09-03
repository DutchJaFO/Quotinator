namespace Quotinator.Data.Import;

/// <summary>
/// Fast lookup over a loaded <see cref="SourceAliasRuleFileDto"/>'s aliases, keyed by raw
/// <c>(title, type)</c> — case-insensitive on both, per this project's id/value-comparison convention.
/// Consulted before Source resolution runs, so it applies uniformly whether the referencing quote is a
/// brand-new Add or a re-imported Modify.
/// </summary>
public sealed class SourceAliasLookup
{
    /// <summary>A lookup with no aliases — every <see cref="TryResolve"/> call returns <see langword="false"/>.</summary>
    public static readonly SourceAliasLookup Empty = new([]);

    private readonly Dictionary<string, (string CanonicalTitle, string CanonicalType, string? CanonicalDate)> _datedAliases;
    private readonly Dictionary<string, (string CanonicalTitle, string CanonicalType, string? CanonicalDate)> _datelessAliases;

    /// <summary>
    /// Builds a lookup from every alias in <paramref name="aliases"/>. A later duplicate (same raw
    /// title + type[+ date]) overwrites an earlier one. #374: an alias carrying a raw <c>Date</c> is
    /// kept separately from a date-less one, since a date-less alias must keep matching every date of
    /// that raw title — the three alias files shipped before this field existed all rely on that.
    /// </summary>
    public SourceAliasLookup(IEnumerable<SourceAliasRule> aliases)
    {
        _datedAliases = new Dictionary<string, (string, string, string?)>(StringComparer.OrdinalIgnoreCase);
        _datelessAliases = new Dictionary<string, (string, string, string?)>(StringComparer.OrdinalIgnoreCase);
        foreach (SourceAliasRule alias in aliases)
        {
            (string CanonicalTitle, string CanonicalType, string? CanonicalDate) canonical = (alias.CanonicalTitle, alias.CanonicalType, alias.CanonicalDate);
            if (alias.Date is null)
                _datelessAliases[Key(alias.Title, alias.Type)] = canonical;
            else
                _datedAliases[DatedKey(alias.Title, alias.Type, alias.Date)] = canonical;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> and the canonical <c>(title, type, date)</c> triple when a
    /// matching alias exists for the raw <paramref name="title"/> + <paramref name="type"/> [+
    /// <paramref name="date"/>]. #374: an alias scoped to a specific raw <paramref name="date"/> is
    /// tried first — an exact match — before falling back to a date-less alias for the same raw title,
    /// so a date-less alias file keeps applying to every date exactly as it did before this field
    /// existed.
    /// </summary>
    public bool TryResolve(string title, string type, string? date, out (string CanonicalTitle, string CanonicalType, string? CanonicalDate) canonical)
    {
        if (date is not null && _datedAliases.TryGetValue(DatedKey(title, type, date), out canonical))
            return true;
        return _datelessAliases.TryGetValue(Key(title, type), out canonical);
    }

    private static string Key(string title, string type) => $"{title}|{type}";
    private static string DatedKey(string title, string type, string date) => $"{title}|{type}|{date}";
}
