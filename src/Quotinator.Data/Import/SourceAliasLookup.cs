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

    private readonly Dictionary<string, (string CanonicalTitle, string CanonicalType)> _aliases;

    /// <summary>Builds a lookup from every alias in <paramref name="aliases"/>. A later duplicate (same raw title + type) overwrites an earlier one.</summary>
    public SourceAliasLookup(IEnumerable<SourceAliasRule> aliases)
    {
        _aliases = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
            _aliases[Key(alias.Title, alias.Type)] = (alias.CanonicalTitle, alias.CanonicalType);
    }

    /// <summary>Returns <see langword="true"/> and the canonical <c>(title, type)</c> pair when a matching alias exists for the raw <paramref name="title"/> + <paramref name="type"/>.</summary>
    public bool TryResolve(string title, string type, out (string CanonicalTitle, string CanonicalType) canonical)
        => _aliases.TryGetValue(Key(title, type), out canonical);

    private static string Key(string title, string type) => $"{title}|{type}";
}
