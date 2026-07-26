using System.Text.Json;

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

    private readonly Dictionary<string, RuleEntry> _rules;

    /// <summary>Builds a lookup from every entity entry in <paramref name="rules"/>, flattening each entry's <see cref="ConflictResolutionRule.Fields"/> into the per-field index. A later duplicate (same entity id + field) overwrites an earlier one.</summary>
    public ConflictRuleLookup(IEnumerable<ConflictResolutionRule> rules)
    {
        _rules = new Dictionary<string, RuleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            foreach (var field in rule.Fields)
            {
                var decision = new FieldMergeDecision(field.Resolution, field.CustomValue);
                _rules[Key(rule.EntityId, field.Field)] = new RuleEntry(decision, rule.ExistingRecord, rule.IncomingRecord);
            }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a rule exists for <paramref name="entityId"/> + <paramref name="field"/>.
    /// <paramref name="decision"/> is ready to feed directly into <see cref="FieldMergeResolver.ResolveWithDecisions"/>
    /// when <paramref name="isStale"/> is <see langword="false"/> — the caller must not apply it otherwise (#153).
    /// A rule is stale when the current staging run's own <paramref name="currentExistingValue"/>/
    /// <paramref name="currentIncomingValue"/> for this field no longer match what was recorded in the
    /// rule's <see cref="ConflictResolutionRule.ExistingRecord"/>/<see cref="ConflictResolutionRule.IncomingRecord"/>
    /// snapshot at authoring time — meaning the underlying source's shape moved since the rule was
    /// written and silently reapplying it could produce a wrong result. A field missing from either
    /// recorded snapshot (e.g. a schema field added after the rule was authored) is treated as stale
    /// rather than assumed unrelated — a rule can only be trusted when both sides were actually recorded.
    /// </summary>
    public bool TryResolve(string entityId, string field, object? currentExistingValue, object? currentIncomingValue, out FieldMergeDecision decision, out bool isStale)
    {
        if (!_rules.TryGetValue(Key(entityId, field), out var entry))
        {
            decision = default;
            isStale  = false;
            return false;
        }

        decision = entry.Decision;
        var existingMatches = TryExtractFieldValue(entry.RecordedExisting, field, out var recordedExisting)
            && FieldMergeResolver.ValuesEqual(recordedExisting, currentExistingValue);
        var incomingMatches = TryExtractFieldValue(entry.RecordedIncoming, field, out var recordedIncoming)
            && FieldMergeResolver.ValuesEqual(recordedIncoming, currentIncomingValue);
        isStale = !existingMatches || !incomingMatches;
        return true;
    }

    private static bool TryExtractFieldValue(JsonElement record, string field, out object? value)
    {
        if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty(field, out var prop))
        {
            value = null;
            return false;
        }

        value = prop.ValueKind switch
        {
            JsonValueKind.Null   => null,
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Array  => prop.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()).ToList(),
            _                    => prop.GetRawText(),
        };
        return true;
    }

    private static string Key(string entityId, string field) => $"{entityId}|{field}";

    private readonly record struct RuleEntry(FieldMergeDecision Decision, JsonElement RecordedExisting, JsonElement RecordedIncoming);
}
