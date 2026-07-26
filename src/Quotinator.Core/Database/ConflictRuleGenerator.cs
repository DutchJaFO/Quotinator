using System.Text.Json;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Database;

/// <summary>
/// Generates <see cref="ConflictResolutionRule"/> entries from a batch's already-decided
/// <see cref="ImportActionFieldRow"/> export rows (#153, Phase 2 of #163) — the same flat rows
/// <c>GET /import/actions/export</c> produces. Reuses that shape rather than re-reading raw
/// <c>SystemImportAction</c> payloads directly, so a generated rule is always built from exactly the
/// same field set a human reviewing the export file would see.
/// </summary>
public static class ConflictRuleGenerator
{
    /// <summary>
    /// Groups <paramref name="rows"/> by <see cref="ImportActionFieldRow.EntityId"/> and emits one
    /// <see cref="ConflictResolutionRule"/> per entity that has at least one field with a real
    /// <see cref="ImportActionFieldRow.Decision"/> — worst case one governed field, best case every
    /// decided field for that entity collapses into a single rule (#153's own Step 10 finding: the
    /// schema's per-entity grouping already expresses the "one rule per action vs. one shared rule"
    /// collapsing the issue described; no separate pattern-matching mechanism is needed). An entity
    /// with no decided fields at all (still <c>Pending</c>/<c>Stale</c>, every row's own <c>Decision</c>
    /// is <see langword="null"/>) produces nothing — there is nothing to generate a rule from yet.
    /// <see cref="ConflictResolutionRule.ExistingRecord"/>/<see cref="ConflictResolutionRule.IncomingRecord"/>
    /// are built from every row in the group, decided or not, since #163's own export already emits one
    /// row per <em>decidable</em> field for that entity type — the full mergeable field set, not a
    /// subset — matching every hand-authored rule's own "complete field set on both sides" convention.
    /// </summary>
    public static IReadOnlyList<ConflictResolutionRule> Generate(IReadOnlyList<ImportActionFieldRow> rows)
    {
        var result = new List<ConflictResolutionRule>();

        foreach (var group in rows.GroupBy(r => r.EntityId, StringComparer.OrdinalIgnoreCase))
        {
            var existingRecord = new Dictionary<string, object?>();
            var incomingRecord = new Dictionary<string, object?>();
            var fieldRules      = new List<ConflictResolutionFieldRule>();

            foreach (var row in group)
            {
                existingRecord[row.Field] = DecodeFieldValue(row.Field, row.ExistingValue);
                incomingRecord[row.Field] = DecodeFieldValue(row.Field, row.IncomingValue);

                if (row.Decision is { } decision)
                {
                    fieldRules.Add(new ConflictResolutionFieldRule
                    {
                        Field       = row.Field,
                        Resolution  = decision,
                        CustomValue = decision == FieldResolutionChoice.Custom ? row.CustomValue : null,
                    });
                }
            }

            if (fieldRules.Count == 0) continue;

            result.Add(new ConflictResolutionRule
            {
                EntityId       = group.Key,
                ExistingRecord = JsonSerializer.SerializeToElement(existingRecord),
                IncomingRecord = JsonSerializer.SerializeToElement(incomingRecord),
                Fields         = fieldRules,
            });
        }

        return result;
    }

    /// <summary>
    /// Merges <paramref name="generated"/> into <paramref name="existing"/> without ever overwriting a
    /// field a human already hand-authored: an entity id not yet in <paramref name="existing"/> is
    /// appended whole; an entity id already present has only its genuinely new fields (not already
    /// covered by that entry's own <see cref="ConflictResolutionRule.Fields"/>) added — an already
    /// hand-authored field's resolution, and the entry's own recorded <c>ExistingRecord</c>/
    /// <c>IncomingRecord</c> snapshot, are left exactly as the file already has them.
    /// </summary>
    public static ConflictResolutionRuleFile Merge(ConflictResolutionRuleFile? existing, IReadOnlyList<ConflictResolutionRule> generated)
    {
        var merged = existing?.Rules.ToList() ?? [];
        var byEntityId = merged.ToDictionary(r => r.EntityId, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in generated)
        {
            if (!byEntityId.TryGetValue(candidate.EntityId, out var existingRule))
            {
                merged.Add(candidate);
                byEntityId[candidate.EntityId] = candidate;
                continue;
            }

            var coveredFields = new HashSet<string>(existingRule.Fields.Select(f => f.Field), StringComparer.OrdinalIgnoreCase);
            var newFields     = candidate.Fields.Where(f => !coveredFields.Contains(f.Field)).ToList();
            if (newFields.Count == 0) continue;

            var index = merged.IndexOf(existingRule);
            merged[index] = new ConflictResolutionRule
            {
                EntityId       = existingRule.EntityId,
                ExistingRecord = existingRule.ExistingRecord,
                IncomingRecord = existingRule.IncomingRecord,
                Fields         = [.. existingRule.Fields, .. newFields],
            };
        }

        return new ConflictResolutionRuleFile { Rules = merged };
    }

    private static object? DecodeFieldValue(string field, string? value) =>
        field == "genres" ? ImportActionFieldRowMapper.DecodeGenres(value) : value;
}
