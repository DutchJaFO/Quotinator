namespace Quotinator.Data.Import;

/// <summary>
/// Fast lookup over a loaded <see cref="ConflictResolutionRuleFile"/>'s rules, keyed by quote id +
/// field name. Quote id matching is case-insensitive, per this project's id-comparison convention —
/// a rule file is hand-authored independently of whatever casing an import file's own explicit id
/// happens to use.
/// </summary>
public sealed class ConflictRuleLookup
{
    /// <summary>A lookup with no rules — every <see cref="TryResolve"/> call returns <see langword="false"/>.</summary>
    public static readonly ConflictRuleLookup Empty = new([]);

    private readonly Dictionary<string, FieldResolutionChoice> _rules;

    /// <summary>Builds a lookup from every rule in <paramref name="rules"/>. A later duplicate (same quote id + field) overwrites an earlier one.</summary>
    public ConflictRuleLookup(IEnumerable<ConflictResolutionRule> rules)
    {
        _rules = new Dictionary<string, FieldResolutionChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            _rules[Key(rule.QuoteId, rule.Field)] = rule.Resolution;
    }

    /// <summary>Returns <see langword="true"/> and the matching rule's resolution when one exists for <paramref name="quoteId"/> + <paramref name="field"/>.</summary>
    public bool TryResolve(string quoteId, string field, out FieldResolutionChoice resolution)
        => _rules.TryGetValue(Key(quoteId, field), out resolution);

    private static string Key(string quoteId, string field) => $"{quoteId}|{field}";
}
