namespace Quotinator.Data.Import;

/// <summary>
/// Fast lookup over a loaded <see cref="ConflictResolutionRuleFile"/>'s rules, keyed by entity id +
/// field name. Entity id matching is case-insensitive, per this project's id-comparison convention —
/// a rule file is hand-authored independently of whatever casing an import file's own explicit id
/// happens to use.
/// </summary>
public sealed class ConflictRuleLookup
{
    /// <summary>A lookup with no rules — every <see cref="TryResolve"/> call returns <see langword="false"/>.</summary>
    public static readonly ConflictRuleLookup Empty = new([]);

    private readonly Dictionary<string, FieldMergeDecision> _rules;

    /// <summary>Builds a lookup from every entity entry in <paramref name="rules"/>, flattening each entry's <see cref="ConflictResolutionRule.Fields"/> into the per-field index. A later duplicate (same entity id + field) overwrites an earlier one.</summary>
    public ConflictRuleLookup(IEnumerable<ConflictResolutionRule> rules)
    {
        _rules = new Dictionary<string, FieldMergeDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            foreach (var field in rule.Fields)
                _rules[Key(rule.EntityId, field.Field)] = new FieldMergeDecision(field.Resolution, field.CustomValue);
    }

    /// <summary>Returns <see langword="true"/> and the matching rule's decision (ready to feed directly into <see cref="FieldMergeResolver.ResolveWithDecisions"/>) when one exists for <paramref name="entityId"/> + <paramref name="field"/>.</summary>
    public bool TryResolve(string entityId, string field, out FieldMergeDecision decision)
        => _rules.TryGetValue(Key(entityId, field), out decision);

    private static string Key(string entityId, string field) => $"{entityId}|{field}";
}
