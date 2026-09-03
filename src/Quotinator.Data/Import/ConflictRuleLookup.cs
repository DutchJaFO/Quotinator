using System.Text.Json;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>
/// Fast lookup over a loaded <see cref="ConflictResolutionRuleFileDto"/>'s rules, keyed by entity id +
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
        foreach (ConflictResolutionRule rule in rules)
            foreach (ConflictResolutionFieldRule field in rule.Fields)
            {
                FieldMergeDecision decision = new(field.Resolution, field.CustomValue);
                _rules[Key(rule.EntityId, field.Field)] = new RuleEntry(decision, rule.IncomingRecord);
            }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a rule exists for <paramref name="entityId"/> + <paramref name="field"/>.
    /// <paramref name="decision"/> is ready to feed directly into <see cref="FieldMergeResolver.ResolveWithDecisions"/>
    /// only when <paramref name="outcome"/> is <see cref="ConflictRuleOutcome.Apply"/> or
    /// <see cref="ConflictRuleOutcome.AlreadyApplied"/> — the caller must not apply it otherwise (#153, #374).
    /// See <see cref="ConflictRuleOutcome"/> for what each outcome means and what the caller does with it.
    /// </summary>
    /// <remarks>
    /// #374: staleness is judged on the incoming side alone — <see cref="ConflictResolutionRule.ExistingRecord"/>
    /// is never read here (a stored value is expected to drift from what was recorded at authoring time;
    /// that is the rule doing its job, not a reason to distrust it). A field's <em>wanted</em> value is
    /// computed from <paramref name="decision"/> against the current sides (never against the recorded
    /// snapshot) and compared against <paramref name="currentExistingValue"/> — the value actually
    /// stored — to tell <see cref="ConflictRuleOutcome.Apply"/> from <see cref="ConflictRuleOutcome.AlreadyApplied"/>.
    /// </remarks>
    public bool TryResolve(string entityId, string field, object? currentExistingValue, object? currentIncomingValue, out FieldMergeDecision decision, out ConflictRuleOutcome outcome)
    {
        if (!_rules.TryGetValue(Key(entityId, field), out RuleEntry entry))
        {
            decision = default;
            outcome  = ConflictRuleOutcome.Apply;
            return false;
        }

        decision = entry.Decision;

        bool incomingMoved = !TryExtractFieldValue(entry.RecordedIncoming, field, out object? recordedIncoming)
            || !FieldMergeResolver.ValuesEqual(recordedIncoming, currentIncomingValue);
        if (incomingMoved)
        {
            // Moved *into* agreement means the field would resolve identically with the rule removed —
            // report that as a candidate for retirement, not as an ordinary staleness warning.
            bool sidesNowAgree = FieldMergeResolver.ValuesEqual(currentExistingValue, currentIncomingValue);
            outcome = sidesNowAgree ? ConflictRuleOutcome.Retirable : ConflictRuleOutcome.Stale;
            return true;
        }

        object? wantedValue = decision.Choice switch
        {
            FieldResolutionChoice.Custom  => decision.CustomValue,
            FieldResolutionChoice.Replace => currentIncomingValue,
            _                             => currentExistingValue, // Keep, and any future default.
        };
        outcome = FieldMergeResolver.ValuesEqual(currentExistingValue, wantedValue)
            ? ConflictRuleOutcome.AlreadyApplied
            : ConflictRuleOutcome.Apply;
        return true;
    }

    private static bool TryExtractFieldValue(JsonElement record, string field, out object? value)
    {
        if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty(field, out JsonElement prop))
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

    private readonly record struct RuleEntry(FieldMergeDecision Decision, JsonElement RecordedIncoming);
}
